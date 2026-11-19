using System.Diagnostics;
using Jige.Core.Adapters;

namespace Jige.Core.Diagnostics;

internal enum DiagnosticOperation { OpenHid, OpenSerial, HidOutput, HidInput, SetFeature, GetFeature, SerialWrite, SerialRead }

internal sealed record DiagnosticTransfer(
    DiagnosticOperation Operation, long Milliseconds, ReadIssue Issue, int? NativeCode, int? Argument, byte[] Data, bool Masked = false);

internal sealed class DiagnosticTransport(IReceiverTransport inner, ReceiverEndpoint endpoint) : IReceiverTransport
{
    private readonly object gate = new();
    private readonly List<(long Started, DiagnosticTransfer Transfer)> transfers = [];
    private const int TransferLimit = 2048;
    private bool finished;
    private bool truncated;

    public (DiagnosticTransfer[] Transfers, bool Truncated) Finish()
    {
        lock (gate)
        {
            finished = true;
            return (transfers.Select(entry => entry.Started == 0 ? entry.Transfer :
                entry.Transfer with { Milliseconds = (long)Stopwatch.GetElapsedTime(entry.Started).TotalMilliseconds })
                .ToArray(), truncated);
        }
    }

    public IHidSession OpenHid(HidInterface endpoint, CancellationToken token) =>
        new Hid(this, Open(DiagnosticOperation.OpenHid, null, () => inner.OpenHid(endpoint, token)));

    public ISerialSession OpenSerial(UsbSerialPort endpoint, int baudRate, CancellationToken token) =>
        new Serial(this, Open(DiagnosticOperation.OpenSerial, baudRate, () => inner.OpenSerial(endpoint, baudRate, token)));

    private T Open<T>(DiagnosticOperation kind, int? argument, Func<T> open)
    {
        var entry = Begin(kind, argument);
        try
        {
            var session = open();
            End(entry, ReadIssue.None, null, []);
            return session;
        }
        catch (TransportException error)
        {
            End(entry, error.Issue, error.NativeCode, []);
            throw;
        }
        catch (OperationCanceledException)
        {
            End(entry, ReadIssue.Timeout, null, []);
            throw;
        }
    }

    private (int Index, long Started) Begin(DiagnosticOperation kind, int? argument)
    {
        long started = Stopwatch.GetTimestamp();
        lock (gate)
        {
            // 挂起的读取按调用顺序占位，方便回放 Compx 先挂读、再发送的路径。
            if (finished)
                return (-1, started);
            int index = transfers.Count;
            if (index == TransferLimit)
            {
                truncated = true;
                return (-1, started);
            }
            transfers.Add((started, new(kind, 0, ReadIssue.Timeout, null, argument, [])));
            return (index, started);
        }
    }

    private void End((int Index, long Started) entry, ReadIssue issue, int? code, ReadOnlySpan<byte> data)
    {
        lock (gate)
        {
            if (finished || entry.Index < 0)
                return;
            var transfer = transfers[entry.Index].Transfer;
            var safe = endpoint is HidInterface hid &&
                transfer.Operation is (DiagnosticOperation.HidInput or DiagnosticOperation.GetFeature)
                ? DiagnosticPrivacy.HidReply(hid, data) : data.ToArray();
            transfers[entry.Index] = (0, transfer with
            {
                Milliseconds = (long)Stopwatch.GetElapsedTime(entry.Started).TotalMilliseconds,
                Issue = issue, NativeCode = code, Data = safe, Masked = !data.SequenceEqual(safe)
            });
        }
    }

    private async ValueTask<int> TransferAsync(DiagnosticOperation op, byte[] buffer,
        Func<ValueTask<int>> transfer, bool write = false, int? argument = null)
    {
        var entry = Begin(op, argument);
        try
        {
            int count = await transfer().ConfigureAwait(false);
            End(entry, ReadIssue.None, null, buffer.AsSpan(0, count));
            return count;
        }
        catch (TransportException error)
        {
            End(entry, error.Issue, error.NativeCode, write ? buffer : []);
            throw;
        }
        catch (OperationCanceledException)
        {
            End(entry, ReadIssue.Timeout, null, write ? buffer : []);
            throw;
        }
    }

    private async ValueTask TransferReportAsync(DiagnosticOperation op, byte[] bytes, Func<ValueTask> transfer,
        bool write = false, int? argument = null)
    {
        await TransferAsync(op, bytes, async () =>
        {
            await transfer().ConfigureAwait(false);
            return bytes.Length;
        }, write, argument).ConfigureAwait(false);
    }

    private sealed class Hid(DiagnosticTransport owner, IHidSession session) : IHidSession
    {
        public ValueTask WriteOutputAsync(byte[] report, CancellationToken token) =>
            owner.TransferReportAsync(DiagnosticOperation.HidOutput, report, () => session.WriteOutputAsync(report, token), true);

        public ValueTask<int> ReadInputAsync(byte[] report, CancellationToken token) =>
            owner.TransferAsync(DiagnosticOperation.HidInput, report, () => session.ReadInputAsync(report, token));

        public ValueTask WriteFeatureAsync(byte[] report, CancellationToken token) =>
            owner.TransferReportAsync(DiagnosticOperation.SetFeature, report, () => session.WriteFeatureAsync(report, token), true);

        public ValueTask ReadFeatureAsync(byte[] report, CancellationToken token) =>
            owner.TransferReportAsync(DiagnosticOperation.GetFeature, report, () => session.ReadFeatureAsync(report, token),
                argument: report[0]);

        public void Dispose() => session.Dispose();
    }

    private sealed class Serial(DiagnosticTransport owner, ISerialSession session) : ISerialSession
    {
        public ValueTask WriteAsync(byte[] bytes, CancellationToken token) =>
            owner.TransferReportAsync(DiagnosticOperation.SerialWrite, bytes, () => session.WriteAsync(bytes, token), true);

        public ValueTask<int> ReadAsync(byte[] buffer, CancellationToken token) =>
            owner.TransferAsync(DiagnosticOperation.SerialRead, buffer, () => session.ReadAsync(buffer, token));

        public void Dispose() => session.Dispose();
    }
}
