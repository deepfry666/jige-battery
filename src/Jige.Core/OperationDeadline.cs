namespace Jige.Core;

internal static class OperationDeadline
{
    public static async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> operation, TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var token = deadline.Token;
        // 原生同步调用放在工作线程。超时后仍等待驱动返回，再释放它持有的句柄和接收器锁。
        var work = Task.Run(() => operation(token), token);
        try
        {
            return await work.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && token.IsCancellationRequested)
        {
            throw new TimeoutException();
        }
        finally
        {
            _ = DisposeWhenCompleteAsync(work, deadline);
        }
    }

    private static async Task DisposeWhenCompleteAsync(Task work, CancellationTokenSource deadline)
    {
        await work.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        deadline.Dispose();
    }
}
