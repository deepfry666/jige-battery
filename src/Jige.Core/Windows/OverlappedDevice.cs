using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Jige.Core.Adapters;
using Microsoft.Win32.SafeHandles;

namespace Jige.Core.Windows;

[SupportedOSPlatform("windows")]
internal sealed class OverlappedDevice : IDisposable
{
    private const int ErrorIoPending = 997;
    private const int ErrorOperationAborted = 995;
    private const int DisposeDrainTimeoutMs = 400;

    private readonly object sync = new();
    private readonly SafeFileHandle handle;
    private readonly ThreadPoolBoundHandle binding;
    private readonly HashSet<Transfer> active = [];
    private readonly ManualResetEventSlim drained = new(true);
    private bool disposed;
    private bool drainedDisposed;
    private bool skipFree;

    public OverlappedDevice(SafeFileHandle handle)
    {
        this.handle = handle;
        try
        {
            binding = ThreadPoolBoundHandle.BindHandle(handle);
        }
        catch (ArgumentException error)
        {
            drained.Dispose();
            throw new TransportException(ReadIssue.IoError, "无法为设备建立重叠 I/O 通道", error);
        }
    }

    private sealed class Transfer(OverlappedDevice device)
    {
        public readonly TaskCompletionSource<(uint Error, uint Count)> Completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public unsafe NativeOverlapped* Pointer;
        public volatile bool KernelCompleted;

        public unsafe void Cancel()
        {
            var pointer = Pointer;
            if (pointer is null)
                return;
            try
            {
                Native.CancelIoEx(device.handle, pointer);
            }
            catch (ObjectDisposedException)
            {
                // 句柄已关闭时系统已取消在途 I/O。
            }
        }

        public static unsafe void Complete(uint error, uint count, NativeOverlapped* pointer)
        {
            var state = (Transfer)ThreadPoolBoundHandle.GetNativeOverlappedState(pointer)!;
            state.KernelCompleted = true;
            state.Completion.TrySetResult((error, count));
            state.device.OnKernelCompleted(state);
        }

        private readonly OverlappedDevice device = device;
    }

    public ValueTask<int> ReadAsync(byte[] bytes, CancellationToken token) => TransferAsync(bytes, false, token);

    public async ValueTask WriteAsync(byte[] bytes, CancellationToken token)
    {
        var count = await TransferAsync(bytes, true, token).ConfigureAwait(false);
        if (count != bytes.Length)
            throw new TransportException(ReadIssue.IoError, $"设备只接收了 {count}/{bytes.Length} 字节");
    }

    private async ValueTask<int> TransferAsync(byte[] bytes, bool write, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var state = new Transfer(this);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            active.Add(state);
            drained.Reset();
        }
        try
        {
            Begin(state, bytes, write);
            using var cancellation = token.UnsafeRegister(static transfer => ((Transfer)transfer!).Cancel(), state);
            var (error, count) = await state.Completion.Task.ConfigureAwait(false);
            if (error == ErrorOperationAborted)
            {
                token.ThrowIfCancellationRequested();
                throw new OperationCanceledException();
            }
            if (error != 0)
                throw WindowsErrors.FromCode((int)error, write ? "发送报文" : "接收报文");
            return (int)count;
        }
        finally
        {
            Free(state);
        }
    }

    private unsafe void Begin(Transfer state, byte[] bytes, bool write)
    {
        lock (sync)
        {
            if (disposed)
            {
                state.KernelCompleted = true;
                state.Pointer = null;
                active.Remove(state);
                if (active.Count == 0 && !drainedDisposed)
                    drained.Set();
                throw new OperationCanceledException();
            }
            // 重叠结构关联的缓冲区会固定到完成回调或同步失败为止。
            state.Pointer = binding.AllocateNativeOverlapped(Transfer.Complete, state, bytes);
        }
        fixed (byte* buffer = bytes)
        {
            int accepted = write
                ? Native.WriteFile(handle, buffer, (uint)bytes.Length, 0, state.Pointer)
                : Native.ReadFile(handle, buffer, (uint)bytes.Length, 0, state.Pointer);
            int error = Marshal.GetLastPInvokeError();
            if (accepted == 0 && error != ErrorIoPending)
            {
                state.KernelCompleted = true;
                OnKernelCompleted(state);
                throw WindowsErrors.FromCode(error, write ? "发送报文" : "接收报文");
            }
        }
    }

    private void OnKernelCompleted(Transfer state)
    {
        lock (sync)
        {
            active.Remove(state);
            if (active.Count == 0 && !drainedDisposed)
                drained.Set();
        }
    }

    private unsafe void Free(Transfer state)
    {
        if (state.Pointer is null)
            return;
        bool canFree;
        lock (sync)
            canFree = state.KernelCompleted && !skipFree;
        if (canFree)
        {
            var pointer = state.Pointer;
            state.Pointer = null;
            try
            {
                binding.FreeNativeOverlapped(pointer);
            }
            catch (ObjectDisposedException)
            {
                // Dispose 在 drained 后释放了 binding，吞掉关停竞态异常。
            }
        }
    }

    public void Dispose()
    {
        bool hadActive;
        lock (sync)
        {
            if (disposed)
                return;
            disposed = true;
            hadActive = active.Count > 0;
        }

        if (hadActive && !handle.IsInvalid)
        {
            unsafe { Native.CancelIoEx(handle, null); }
        }

        bool completed = !hadActive || drained.Wait(DisposeDrainTimeoutMs);
        Transfer[] stuck = [];
        lock (sync)
        {
            if (!completed && active.Count > 0)
            {
                // 驱动在 400ms 内仍未结束在途重叠 I/O：放弃释放 binding 与 NativeOverlapped（接受进程级一次性泄漏），
                // 同时取消所有等待方避免 TransferAsync 永久挂起。
                skipFree = true;
                stuck = [.. active];
            }
            drainedDisposed = true;
        }

        foreach (var state in stuck)
            state.Completion.TrySetCanceled();

        if (!skipFree)
            binding.Dispose();
        handle.Dispose();
        drained.Dispose();
    }
}
