using System.Diagnostics;
using Jige.Core.Adapters;

namespace Jige.Core.Diagnostics;

internal enum DiagnosticOperation { OpenHid, OpenSerial, HidOutput, HidInput, SetFeature, GetFeature, SerialWrite, SerialRead }

internal sealed record DiagnosticTransfer(
    ReceiverEndpoint Endpoint, DiagnosticOperation Operation, long Milliseconds, ReadIssue Issue, int? NativeCode, int? Argument, byte[] Data, bool Masked = false, string? ReplySummary = null);

internal sealed class DiagnosticTransport(IReceiverTransport inner, IDeviceAdapter adapter) : IReceiverTransport
{
    private readonly object gate = new();
    private readonly List<(long Started, DiagnosticTransfer Transfer)> transfers = [];
    private const int TransferLimit = 2048;
    private bool finished;
    private bool truncated;
    private byte[] request = [];
    private readonly Dictionary<(byte Slot, byte Index), HidppFeature> features = [];

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
        new Hid(this, endpoint, Open(endpoint, DiagnosticOperation.OpenHid, null, () => inner.OpenHid(endpoint, token)));

    public ISerialSession OpenSerial(UsbSerialPort endpoint, int baudRate, CancellationToken token) =>
        new Serial(this, endpoint, Open(endpoint, DiagnosticOperation.OpenSerial, baudRate, () => inner.OpenSerial(endpoint, baudRate, token)));

    private T Open<T>(ReceiverEndpoint endpoint, DiagnosticOperation kind, int? argument, Func<T> open)
    {
        var entry = Begin(endpoint, kind, argument);
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

    private (int Index, long Started) Begin(ReceiverEndpoint endpoint, DiagnosticOperation kind, int? argument)
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
            transfers.Add((started, new(endpoint, kind, 0, ReadIssue.Timeout, null, argument, [])));
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
            var safe = transfer.Endpoint is HidInterface hid &&
                transfer.Operation is (DiagnosticOperation.HidInput or DiagnosticOperation.GetFeature)
                ? DiagnosticPrivacy.HidReply(adapter, hid, data, request, features) : data.ToArray();
            string? replySummary = null;
            if (issue == ReadIssue.None && data.Length > 0 &&
                transfer.Endpoint is HidInterface replyHid &&
                transfer.Operation is (DiagnosticOperation.HidInput or DiagnosticOperation.GetFeature) &&
                ((adapter is PassiveMouseAdapter or RapooVt9Adapter
                    && DomesticMouseStatus.Match(replyHid)?.Decode(data) is null) ||
                    safe.AsSpan().IndexOfAnyExcept((byte)0) < 0))
                replySummary = DiagnosticPrivacy.SummarizeUnrecognizedHidReply(adapter, replyHid, data, request);
            if (issue == ReadIssue.None && transfer.Operation is DiagnosticOperation.HidOutput or DiagnosticOperation.SetFeature)
                request = safe;
            transfers[entry.Index] = (0, transfer with
            {
                Milliseconds = (long)Stopwatch.GetElapsedTime(entry.Started).TotalMilliseconds,
                Issue = issue, NativeCode = code, Data = safe, Masked = !data.SequenceEqual(safe),
                ReplySummary = replySummary
            });
        }
    }

    private async ValueTask<int> TransferAsync(ReceiverEndpoint endpoint, DiagnosticOperation op, byte[] buffer,
        Func<ValueTask<int>> transfer, bool write = false, int? argument = null)
    {
        var entry = Begin(endpoint, op, argument);
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

    private async ValueTask TransferReportAsync(ReceiverEndpoint endpoint, DiagnosticOperation op, byte[] bytes, Func<ValueTask> transfer,
        bool write = false, int? argument = null)
    {
        await TransferAsync(endpoint, op, bytes, async () =>
        {
            await transfer().ConfigureAwait(false);
            return bytes.Length;
        }, write, argument).ConfigureAwait(false);
    }

    private sealed class Hid(DiagnosticTransport owner, HidInterface endpoint, IHidSession session) : IHidSession
    {
        public ValueTask WriteOutputAsync(byte[] report, CancellationToken token) =>
            owner.TransferReportAsync(endpoint, DiagnosticOperation.HidOutput, report, () => session.WriteOutputAsync(report, token), true);

        public ValueTask<int> ReadInputAsync(byte[] report, CancellationToken token) =>
            owner.TransferAsync(endpoint, DiagnosticOperation.HidInput, report, () => session.ReadInputAsync(report, token));

        public ValueTask WriteFeatureAsync(byte[] report, CancellationToken token) =>
            owner.TransferReportAsync(endpoint, DiagnosticOperation.SetFeature, report, () => session.WriteFeatureAsync(report, token), true);

        public ValueTask ReadFeatureAsync(byte[] report, CancellationToken token) =>
            owner.TransferReportAsync(endpoint, DiagnosticOperation.GetFeature, report, () => session.ReadFeatureAsync(report, token),
                argument: report[0]);

        public void Dispose() => session.Dispose();
    }

    private sealed class Serial(DiagnosticTransport owner, UsbSerialPort endpoint, ISerialSession session) : ISerialSession
    {
        public ValueTask WriteAsync(byte[] bytes, CancellationToken token) =>
            owner.TransferReportAsync(endpoint, DiagnosticOperation.SerialWrite, bytes, () => session.WriteAsync(bytes, token), true);

        public ValueTask<int> ReadAsync(byte[] buffer, CancellationToken token) =>
            owner.TransferAsync(endpoint, DiagnosticOperation.SerialRead, buffer, () => session.ReadAsync(buffer, token));

        public void Dispose() => session.Dispose();
    }
}
