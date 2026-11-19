namespace Jige.Tray;

internal sealed class PollSchedule(DateTimeOffset created)
{
    private DateTimeOffset? lastStarted;
    private bool requested;
    private bool userRequested;

    public bool Running { get; private set; }
    public bool StartedByUser { get; private set; }

    public void Request(bool userInitiated = true)
    {
        if (Running)
            return;
        requested = true;
        userRequested |= userInitiated;
    }

    public DateTimeOffset? Due(TraySettings settings, DateTimeOffset now, bool game)
    {
        if (Running)
            return null;
        if (userRequested)
            return now;
        if (game)
            return (lastStarted ?? created).AddSeconds(settings.GamePollSeconds);
        if (requested || lastStarted is null)
            return now;
        return lastStarted.Value.AddSeconds(settings.PollSeconds);
    }

    public void Start(DateTimeOffset now, bool game)
    {
        Running = true;
        StartedByUser = userRequested;
        lastStarted = now;
        if (!game || userRequested)
            requested = false;
        userRequested = false;
    }

    public void Complete() => Running = false;
}

internal enum ForegroundKind { System, Own, Other }

internal readonly record struct GameMode(bool Game, bool BlockPopups)
{
    public static GameMode Evaluate(int notificationState, bool foregroundGame, bool gameSilent)
    {
        bool game = notificationState is 2 or 3 or 4 || gameSilent && foregroundGame;
        return new(game, game);
    }

    public static bool IsGameWindow(ForegroundKind kind, Bounds frame, Bounds screen, Bounds work,
        bool hasCaption, bool knownGame)
    {
        return kind == ForegroundKind.Other && (knownGame || Covers(frame, screen) ||
            !hasCaption && Covers(frame, work));
    }

    private static bool Covers(Bounds frame, Bounds area)
    {
        const int tolerance = 2;
        return frame.Right > frame.Left && frame.Bottom > frame.Top &&
            area.Right > area.Left && area.Bottom > area.Top &&
            frame.Left <= area.Left + tolerance && frame.Top <= area.Top + tolerance &&
            frame.Right >= area.Right - tolerance && frame.Bottom >= area.Bottom - tolerance;
    }
}
