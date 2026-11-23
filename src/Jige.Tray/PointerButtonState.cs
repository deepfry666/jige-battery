namespace Jige.Tray;

/// <summary>
/// 跟踪自绘窗口的鼠标按下、释放与系统捕获状态。
/// Win32 的 ReleaseCapture 会在返回前同步派发 WM_CAPTURECHANGED，必须在释放捕获前先保存并清空本次按下状态，
/// 避免重入的 Cancel() 抹掉刚按下的目标导致所有点击失效。
/// </summary>
internal struct PointerButtonState<T> where T : struct, IEquatable<T>
{
    private T down;
    private bool hasDown;
    private bool captured;

    public bool IsCaptured => captured;

    public bool Press(T target, bool capture)
    {
        down = target;
        hasDown = true;
        captured = capture;
        return capture;
    }

    public bool Release(T current, Action releaseCapture, out T clicked)
    {
        bool matched = hasDown && EqualityComparer<T>.Default.Equals(current, down);
        clicked = matched ? current : default;
        bool wasCaptured = captured;
        hasDown = false;
        down = default;
        captured = false;
        if (wasCaptured)
            releaseCapture();
        return matched;
    }

    public bool Cancel(Action? releaseCapture = null)
    {
        bool hadState = captured || hasDown;
        bool wasCaptured = captured;
        hasDown = false;
        down = default;
        captured = false;
        if (wasCaptured)
            releaseCapture?.Invoke();
        return hadState;
    }
}
