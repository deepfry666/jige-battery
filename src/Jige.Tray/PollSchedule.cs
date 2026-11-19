namespace Jige.Tray;

internal sealed class PollSchedule(DateTimeOffset created)
{
    private DateTimeOffset? lastStarted;
    private bool requested;

    public bool Running { get; private set; }

    public void Request()
    {
        if (!Running)
            requested = true;
    }

    public DateTimeOffset? Due(TraySettings settings, DateTimeOffset now, bool game)
    {
        if (Running)
            return null;
        if (game)
            return (lastStarted ?? created).AddSeconds(settings.GamePollSeconds);
        if (requested || lastStarted is null)
            return now;
        return lastStarted.Value.AddSeconds(settings.PollSeconds);
    }

    public void Start(DateTimeOffset now, bool game)
    {
        Running = true;
        lastStarted = now;
        if (!game)
            requested = false;
    }

    public void Complete() => Running = false;
}

internal readonly record struct GameMode(bool Game, bool BlockPopups)
{
    public static GameMode Evaluate(int notificationState, bool fullscreen, bool gameSilent)
    {
        // Windows 的忙碌、D3D 全屏和演示状态始终优先保护游戏或演示。
        bool game = fullscreen || notificationState is 2 or 3 or 4;
        return new(game, game || (gameSilent && notificationState == 6));
    }
}
