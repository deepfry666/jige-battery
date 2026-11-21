using System.Globalization;

namespace Jige.Core.Adapters;

/// <summary>安全门拦截记录，仅含硬件描述符信息，不含序列号、路径或实例 ID。</summary>
public sealed record SafetyGateRejection(
    string Signature, ushort? VendorId, ushort? ProductId, ushort? UsagePage, ushort? Usage, int Gate, string Reason);

/// <summary>管理通用协议识别的三道安全门状态与持久化拦截列表。</summary>
public sealed class SafetyGateStore
{
    public const int MaxBlockedEntries = 64;

    private readonly object gate = new();
    private readonly Dictionary<string, (string Reason, long Order)> blocked = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SafetyGateRejection> rejections = [];
    private long nextOrder;
    private long version;

    public event Action<SafetyGateRejection>? Rejected;

    public SafetyGateStore(IEnumerable<KeyValuePair<string, string>>? initialBlocked = null)
    {
        if (initialBlocked is not null)
            Load(initialBlocked);
    }

    /// <summary>按 VID:PID:接口特征 生成稳定、无个人信息的接收器签名。</summary>
    public static string Signature(HidInterface hid) => string.Create(CultureInfo.InvariantCulture,
        $"{hid.VendorId:X4}:{hid.ProductId:X4}:{hid.UsagePage:X4}:{hid.Usage:X4}:{hid.InputLength ?? 0}:{hid.OutputLength ?? 0}:{hid.FeatureLength ?? 0}");

    public bool IsBlocked(HidInterface hid, out string? reason) => IsBlocked(Signature(hid), out reason);

    public bool IsBlocked(string signature, out string? reason)
    {
        lock (gate)
        {
            if (blocked.TryGetValue(signature, out var entry))
            {
                reason = entry.Reason;
                return true;
            }
            reason = null;
            return false;
        }
    }

    public int BlockedCount
    {
        get
        {
            lock (gate)
                return blocked.Count;
        }
    }

    /// <summary>每次运行期拦截表变化（新增拦截或清空）单调递增，用于脏检查，Load 恢复不递增。</summary>
    public long Version
    {
        get
        {
            lock (gate)
                return version;
        }
    }

    /// <summary>第三道安全门：记录回复不符合协议的接收器，内存中持续生效，持久化按最新序号保留上限条数。</summary>
    public bool Block(HidInterface hid, string reason)
    {
        string signature = Signature(hid);
        bool added;
        lock (gate)
        {
            if (blocked.ContainsKey(signature))
                return false;
            blocked[signature] = (reason, ++nextOrder);
            version++;
            added = true;
        }
        if (added)
            RecordRejection(hid, 3, reason);
        return added;
    }

    public void RecordRejection(HidInterface hid, int gateNumber, string reason)
    {
        var entry = new SafetyGateRejection(
            Signature(hid), hid.VendorId, hid.ProductId, hid.UsagePage, hid.Usage, gateNumber, reason);
        lock (gate)
        {
            if (rejections.Count >= MaxBlockedEntries * 2)
                rejections.RemoveAt(0);
            rejections.Add(entry);
        }
        Rejected?.Invoke(entry);
    }

    public void Load(IEnumerable<KeyValuePair<string, string>> entries)
    {
        lock (gate)
        {
            blocked.Clear();
            nextOrder = 0;
            foreach (var (signature, reason) in entries)
            {
                if (blocked.Count >= MaxBlockedEntries)
                    break;
                if (!string.IsNullOrWhiteSpace(signature) && !string.IsNullOrWhiteSpace(reason))
                    blocked[signature.Trim()] = (reason.Trim(), ++nextOrder);
            }
        }
    }

    public Dictionary<string, string> Snapshot()
    {
        lock (gate)
        {
            var persisted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // 取最新 MaxBlockedEntries 条后按原先后顺序写出，避免重启 Load 递增编号后新旧次序反转。
            foreach (var pair in blocked.OrderByDescending(item => item.Value.Order)
                         .Take(MaxBlockedEntries)
                         .OrderBy(item => item.Value.Order))
                persisted[pair.Key] = pair.Value.Reason;
            return persisted;
        }
    }

    public SafetyGateRejection[] RecentRejections()
    {
        lock (gate)
            return [.. rejections];
    }

    /// <summary>用户主动点击「重新检测」时清除已记住的第三道安全门拦截记录。</summary>
    public bool Clear()
    {
        lock (gate)
        {
            bool had = blocked.Count > 0 || rejections.Count > 0;
            if (blocked.Count > 0)
                version++;
            blocked.Clear();
            rejections.Clear();
            return had;
        }
    }
}
