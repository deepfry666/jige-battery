namespace Jige.Core.Adapters;

internal static class HidQuery
{
    public static async ValueTask<byte[]?> OutputAsync(IHidSession output, IHidSession input, byte[] request,
        int inputLength, Func<byte[], bool> matches, FrameTrace? trace, CancellationToken token, int milliseconds = 500)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(milliseconds);
        var report = new byte[inputLength];
        try
        {
            await output.WriteOutputAsync(request, deadline.Token).ConfigureAwait(false);
            trace?.Invoke(FrameDirection.Sent, request);
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                int count = await input.ReadInputAsync(report, deadline.Token).ConfigureAwait(false);
                trace?.Invoke(FrameDirection.Received, report.AsSpan(0, count));
                if (count == 0)
                    throw new TransportException(ReadIssue.Disconnected, "HID 输入通道已关闭");
                var frame = count == report.Length ? report : report[..count];
                if (matches(frame))
                    return frame;
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return null;
        }
    }

    public static IEnumerable<HidInterface> UniqueCollections(IEnumerable<HidInterface> candidates) =>
        candidates.GroupBy(hid => hid.ReceiverId, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => group.Take(2).ToArray() is [var endpoint] ? [endpoint] : Array.Empty<HidInterface>());
}
