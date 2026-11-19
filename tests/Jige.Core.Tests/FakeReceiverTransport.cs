using System.Globalization;
using Jige.Core.Adapters;
using Jige.Core.Diagnostics;

namespace Jige.Core.Tests;

internal sealed record ReceiverStep(
    DiagnosticOperation Operation, string Hex = "-", ReadIssue Issue = ReadIssue.None,
    int? Code = null, int? Argument = null);

internal sealed class FakeReceiverTransport : IReceiverTransport
{
    private readonly Dictionary<string, Queue<ReceiverStep>> scripts;
    private readonly Lock gate = new();
    private int activeSessions;
    public int Calls { get; private set; }

    public FakeReceiverTransport(params (string Path, ReceiverStep[] Steps)[] scripts) =>
        this.scripts = scripts.ToDictionary(script => script.Path, script => new Queue<ReceiverStep>(script.Steps));

    public IHidSession OpenHid(HidInterface endpoint, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        CheckError(Next(endpoint.Path, DiagnosticOperation.OpenHid));
        Interlocked.Increment(ref activeSessions);
        return new Session(this, endpoint.Path);
    }

    public ISerialSession OpenSerial(UsbSerialPort endpoint, int baudRate, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        CheckError(Next(endpoint.Path, DiagnosticOperation.OpenSerial, baudRate));
        Interlocked.Increment(ref activeSessions);
        return new Session(this, endpoint.Path);
    }

    public void AssertComplete()
    {
        Assert.Equal(0, Volatile.Read(ref activeSessions));
        Assert.All(scripts.Values, script => Assert.Empty(script));
    }

    private ReceiverStep Next(string path, DiagnosticOperation operation, int? argument = null)
    {
        lock (gate)
        {
            Calls++;
            Assert.True(scripts.TryGetValue(path, out var script), $"未安排接口 {path} 的 {operation}");
            Assert.True(script!.TryDequeue(out var step), $"接口 {path} 的脚本已结束，仍收到 {operation}");
            Assert.Equal(step!.Operation, operation);
            Assert.Equal(step.Argument, argument);
            return step;
        }
    }

    private static void CheckError(ReceiverStep step)
    {
        if (step.Issue != ReadIssue.None)
            throw new TransportException(step.Issue, "模拟传输错误", nativeCode: step.Code);
    }

    private sealed class Session(FakeReceiverTransport owner, string path) : IHidSession, ISerialSession
    {
        public ValueTask WriteOutputAsync(byte[] buffer, CancellationToken token) =>
            Write(DiagnosticOperation.HidOutput, buffer, token);

        public ValueTask<int> ReadInputAsync(byte[] buffer, CancellationToken token) =>
            Read(DiagnosticOperation.HidInput, buffer, token);

        public ValueTask WriteFeatureAsync(byte[] buffer, CancellationToken token) =>
            Write(DiagnosticOperation.SetFeature, buffer, token);

        public async ValueTask ReadFeatureAsync(byte[] buffer, CancellationToken token) =>
            Assert.Equal(buffer.Length, await Read(DiagnosticOperation.GetFeature, buffer, token, buffer[0]));

        public ValueTask WriteAsync(byte[] bytes, CancellationToken token) =>
            Write(DiagnosticOperation.SerialWrite, bytes, token);

        public ValueTask<int> ReadAsync(byte[] buffer, CancellationToken token) =>
            Read(DiagnosticOperation.SerialRead, buffer, token);

        private async ValueTask Write(DiagnosticOperation op, byte[] buffer, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var step = owner.Next(path, op);
            Assert.Equal(step.Hex, Convert.ToHexString(buffer));
            await Apply(step, token);
        }

        private async ValueTask<int> Read(DiagnosticOperation op, byte[] buffer, CancellationToken token, int? arg = null)
        {
            token.ThrowIfCancellationRequested();
            var step = owner.Next(path, op, arg);
            await Apply(step, token);
            var data = step.Hex == "-" ? [] : Convert.FromHexString(step.Hex);
            data.CopyTo(buffer, 0);
            return data.Length;
        }

        private static async ValueTask Apply(ReceiverStep step, CancellationToken token)
        {
            if (step.Issue == ReadIssue.Timeout && step.Code is null)
                await Task.Delay(Timeout.Infinite, token);
            CheckError(step);
        }

        public void Dispose() => Interlocked.Decrement(ref owner.activeSessions);
    }

    public static (DeviceInventory Inventory, FakeReceiverTransport Transport, int SonyBaud) FromReport(string report)
    {
        using var reader = new StringReader(report);
        if (reader.ReadLine() != DiagnosticReport.Header)
            throw new FormatException("诊断报告版本不受支持");

        List<HidInterface> hid = [];
        List<UsbSerialPort> serial = [];
        Dictionary<string, List<ReceiverStep>> steps = new();
        int baud = 115200;
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("截断 ", StringComparison.Ordinal))
                throw new FormatException("诊断记录已截断，无法完整回放");
            var fields = line.Split('|');
            switch (fields[0])
            {
                case "HID" when fields.Length == 12:
                    hid.Add(new(fields[2], fields[1], HexNumber(fields[3]), HexNumber(fields[4]), fields[11],
                        HexNumber(fields[5]), HexNumber(fields[6]), Length(fields[7]), Length(fields[8]),
                        Length(fields[9]), (byte?)HexNumber(fields[10])));
                    steps.Add(fields[1], []);
                    break;
                case "USB" when fields.Length == 4:
                    serial.Add(new(fields[2], fields[1], null, fields[3].Split(',', StringSplitOptions.RemoveEmptyEntries)));
                    steps.Add(fields[1], []);
                    break;
                case "SONY-BAUD" when fields.Length == 2:
                    baud = int.Parse(fields[1], CultureInfo.InvariantCulture);
                    break;
                case "OP" when fields.Length == 9 && steps.TryGetValue(fields[1], out var script):
                    script.Add(new(Enum.Parse<DiagnosticOperation>(fields[2]), fields[7], Enum.Parse<ReadIssue>(fields[4]),
                        OptionalInt(fields[5]), OptionalInt(fields[6])));
                    break;
                case "HID" or "USB" or "SONY-BAUD" or "OP":
                    throw new FormatException("诊断报告字段无效");
            }
        }

        // 一个可查询接口才会有脚本。其余接口仍参与枚举和冲突消歧。
        return (new(hid, serial, []), new(steps.Select(pair => (pair.Key, pair.Value.ToArray())).ToArray()), baud);
    }

    private static int? OptionalInt(string field) => field == "-" ? null : int.Parse(field, CultureInfo.InvariantCulture);
    private static ushort? HexNumber(string field) =>
        field.Length == 0 ? null : ushort.Parse(field, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    private static ushort? Length(string field) =>
        field.Length == 0 ? null : ushort.Parse(field, CultureInfo.InvariantCulture);
}
