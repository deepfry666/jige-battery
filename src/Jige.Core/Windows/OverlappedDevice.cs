using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Jige.Core.Adapters;
using Microsoft.Win32.SafeHandles;

namespace Jige.Core.Windows;

[SupportedOSPlatform("windows")]
internal sealed class OverlappedDevice : IDisposable
{
    private readonly SafeFileHandle handle;
    private readonly ThreadPoolBoundHandle binding;

    public OverlappedDevice(SafeFileHandle handle)
    {
        this.handle = handle;
        try
        {
            binding = ThreadPoolBoundHandle.BindHandle(handle);
        }
        catch (ArgumentException error)
        {
            throw new TransportException(ReadIssue.IoError, "无法为设备建立重叠 I/O 通道", error);
        }
    }

    private sealed class Transfer(OverlappedDevice device)
    {
        public readonly TaskCompletionSource<(uint Error, uint Count)> Completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public unsafe NativeOverlapped* Pointer;

        public unsafe void Cancel() => Native.CancelIoEx(device.handle, Pointer);

        public static unsafe void Complete(uint error, uint count, NativeOverlapped* pointer)
        {
            var state = (Transfer)ThreadPoolBoundHandle.GetNativeOverlappedState(pointer)!;
            state.Completion.SetResult((error, count));
        }
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
        try
        {
            Begin(state, bytes, write);
            using var cancellation = token.UnsafeRegister(static transfer => ((Transfer)transfer!).Cancel(), state);
            var (error, count) = await state.Completion.Task.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
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
        // 重叠结构关联的缓冲区会固定到完成回调或同步失败为止。
        state.Pointer = binding.AllocateNativeOverlapped(Transfer.Complete, state, bytes);
        fixed (byte* buffer = bytes)
        {
            int accepted = write
                ? Native.WriteFile(handle, buffer, (uint)bytes.Length, 0, state.Pointer)
                : Native.ReadFile(handle, buffer, (uint)bytes.Length, 0, state.Pointer);
            int error = Marshal.GetLastPInvokeError();
            if (accepted == 0 && error != 997)
                throw WindowsErrors.FromCode(error, write ? "发送报文" : "接收报文");
        }
    }

    private unsafe void Free(Transfer state)
    {
        if (state.Pointer is not null)
            binding.FreeNativeOverlapped(state.Pointer);
    }

    public void Dispose()
    {
        binding.Dispose();
        handle.Dispose();
    }
}
