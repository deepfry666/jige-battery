using System.Runtime.CompilerServices;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Variant;
using Windows.Win32.UI.Accessibility;
using static Windows.Win32.UI.Accessibility.UIA_PROPERTY_ID;

namespace Jige.Tray.Windows;

/// <summary>Reads and observes the taskbar's accessibility tree on a windowless MTA thread.</summary>
[SupportedOSPlatform("windows10.0.17763")]
internal sealed unsafe class TaskbarStripAutomation : IDisposable
{
    private static readonly Guid AutomationClass = new("e22ad333-b25f-460c-83d0-0581107395c9"); // CUIAutomation8
    private const int EventDebounceMs = 120;
    private readonly HWND host;
    private readonly uint message;
    private readonly object sync = new();
    private readonly AutoResetEvent changed = new(false);
    private HWND requested;
    private TaskbarStripGeometry? pending, lastPublished;
    private bool posted, disposed, changedDisposed, reconnect, urgent;
    private long lastEventMs;
    private bool alive = true;
    private string? lastErrorLog;

    public bool Alive => Volatile.Read(ref alive);

    public TaskbarStripAutomation(HWND host, uint message)
    {
        this.host = host;
        this.message = message;
        new Thread(Run) { IsBackground = true, Name = "Jige taskbar UI Automation" }.Start();
    }

    public void Request(HWND owner, bool reset = false)
    {
        lock (sync)
        {
            if (disposed || changedDisposed)
                return;
            if (requested != owner || reset)
            {
                pending = lastPublished = null;
                requested = owner;
                reconnect = true;
            }
            urgent = true;
            try { changed.Set(); }
            catch (ObjectDisposedException) { }
        }
    }

    public TaskbarStripGeometry? Take()
    {
        lock (sync)
        {
            posted = false;
            var snapshot = pending;
            pending = null;
            return snapshot;
        }
    }

    private void Signal()
    {
        lock (sync)
        {
            if (disposed || changedDisposed)
                return;
            lastEventMs = Environment.TickCount64;
            try { changed.Set(); }
            catch (ObjectDisposedException) { }
        }
    }

    private void Publish(HWND owner, TaskbarStripGeometry geometry)
    {
        if (geometry.Owner.IsNull)
            return;
        lock (sync)
        {
            if (disposed || reconnect || requested != owner || lastPublished == geometry)
                return;
            pending = geometry;
            if (!posted)
                posted = PInvoke.PostMessage(host, message, default, default);
            if (posted)
                lastPublished = geometry;
        }
    }

    private void Run()
    {
        var initialized = PInvoke.CoInitializeEx(COINIT.COINIT_MULTITHREADED);
        IUIAutomation2* automation = null;
        IUIAutomationCacheRequest* cache = null;
        IUIAutomationCondition* condition = null;
        IUIAutomationElement* root = null;
        try
        {
            while (changed.WaitOne())
            {
                // 高频 UIA 事件先做短防抖合并，显式 Request（启动/AppBar/DPI）则立即执行。
                while (true)
                {
                    int waitMs;
                    lock (sync)
                    {
                        if (disposed)
                            return;
                        if (urgent || reconnect)
                            break;
                        long remaining = lastEventMs + EventDebounceMs - Environment.TickCount64;
                        if (remaining <= 0)
                            break;
                        waitMs = (int)Math.Clamp(remaining, 10, EventDebounceMs);
                    }
                    changed.WaitOne(waitMs);
                }

                HWND owner;
                bool reset;
                lock (sync)
                {
                    if (disposed)
                        return;
                    owner = requested;
                    reset = reconnect;
                    reconnect = false;
                    urgent = false;
                }

                if (reset)
                    Disconnect(automation, ref root);
                if (owner.IsNull)
                    continue;

                TaskbarStripGeometry geometry = default;
                bool hasShellGeometry = false;
                try
                {
                    if (!TaskbarStripShell.TryRead(owner, out geometry, out var nativeTray))
                        continue;
                    hasShellGeometry = !geometry.Owner.IsNull;
                    if (nativeTray is not null)
                        geometry = geometry with { Controls = TaskbarControls.Locate(geometry.Bar, nativeTray, []) };
                    Graphics.Check(initialized, "初始化任务栏 UI Automation");
                    if (automation == null)
                    {
                        automation = ConnectAutomation();
                        CreateReadQuery(automation, out cache, out condition);
                    }
                    if (root == null)
                    {
                        root = automation->ElementFromHandle(owner);
                        if (root == null)
                            throw new COMException();
                        TrySubscribeEvents(automation, root);
                    }
                    var controls = ReadControls(root, cache, condition);
                    geometry = geometry with { Controls = TaskbarControls.Locate(geometry.Bar, nativeTray, controls) };
                    lastErrorLog = null;
                }
                catch (Exception error) when (error is ExternalException or UnauthorizedAccessException or InvalidOperationException)
                {
                    // Explorer may be restarting or have an unavailable provider. The
                    // next Shell or UI Automation notification can supply fresh bounds.
                    Disconnect(automation, ref root);
                    try { LogErrorOnce("读取任务栏布局失败", error); }
                    catch { }
                }
                finally
                {
                    if (hasShellGeometry)
                        Publish(owner, geometry);
                }
            }
        }
        catch (Exception error)
        {
            try { LogErrorOnce("任务栏 UI Automation 线程异常退出", error); }
            catch { }
        }
        finally
        {
            lock (sync)
            {
                disposed = true;
                Volatile.Write(ref alive, false);
            }
            try
            {
                Disconnect(automation, ref root);
                Graphics.Release(condition);
                Graphics.Release(cache);
                Graphics.Release(automation);
                if (initialized.Succeeded)
                    PInvoke.CoUninitialize();
            }
            catch
            {
            }
            finally
            {
                lock (sync)
                {
                    changedDisposed = true;
                    changed.Dispose();
                }
            }
        }
    }

    private static IUIAutomation2* ConnectAutomation()
    {
        var clsid = AutomationClass;
        var iid = IUIAutomation2.IID_Guid;
        IUIAutomation2* automation = null;
        Graphics.Check(PInvoke.CoCreateInstance(&clsid, null, CLSCTX.CLSCTX_INPROC_SERVER, &iid,
            (void**)&automation), "连接任务栏 UI Automation");
        try
        {
            automation->ConnectionTimeout = 500;
            automation->TransactionTimeout = 750;
            return automation;
        }
        catch
        {
            Graphics.Release(automation);
            throw;
        }
    }

    private void TrySubscribeEvents(IUIAutomation2* automation, IUIAutomationElement* root)
    {
        try
        {
            var sink = (IUnknown*)EventComWrappers.Instance.GetOrCreateComInterfaceForObject(
                new EventSink(Signal), CreateComInterfaceFlags.None);
            IUIAutomationStructureChangedEventHandler* structure = null;
            IUIAutomationPropertyChangedEventHandler* property = null;
            try
            {
                Graphics.Check(sink->QueryInterface(out structure), "注册任务栏结构变更");
                Graphics.Check(sink->QueryInterface(out property), "注册任务栏位置变更");
                automation->AddStructureChangedEventHandler(root, TreeScope.TreeScope_Subtree, null, structure);
                UIA_PROPERTY_ID* properties = stackalloc UIA_PROPERTY_ID[]
                {
                    UIA_BoundingRectanglePropertyId, UIA_IsOffscreenPropertyId
                };
                automation->AddPropertyChangedEventHandlerNativeArray(root, TreeScope.TreeScope_Subtree,
                    null, property, properties, 2);
            }
            finally
            {
                Graphics.Release(property);
                Graphics.Release(structure);
                Graphics.Release(sink);
            }
        }
        catch (Exception error) when (error is ExternalException or UnauthorizedAccessException or InvalidOperationException)
        {
            // 即使事件订阅失败，仍继续使用本次及后续 Shell 通知读取的控件边界。
            LogErrorOnce("订阅任务栏变更失败", error);
        }
    }

    private void LogErrorOnce(string message, Exception error)
    {
        string summary = TrayLog.Describe(message, error);
        if (lastErrorLog != summary)
        {
            lastErrorLog = summary;
            Desktop.Log(summary);
        }
    }

    private static void Disconnect(IUIAutomation2* automation, ref IUIAutomationElement* root)
    {
        if (root == null)
            return;
        try { automation->RemoveAllEventHandlers(); }
        catch (Exception error) when (error is ExternalException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Explorer can destroy the provider first.
        }
        Graphics.Release(root);
        root = null;
    }

    private static void CreateReadQuery(IUIAutomation2* automation,
        out IUIAutomationCacheRequest* cache, out IUIAutomationCondition* condition)
    {
        cache = null;
        condition = null;
        try
        {
            cache = automation->CreateCacheRequest();
            cache->AutomationElementMode = AutomationElementMode.AutomationElementMode_None;
            cache->AddProperty(UIA_BoundingRectanglePropertyId);
            cache->AddProperty(UIA_IsOffscreenPropertyId);
            cache->AddProperty(UIA_ClassNamePropertyId);
            cache->AddProperty(UIA_AutomationIdPropertyId);
            cache->AddProperty(UIA_ControlTypePropertyId);
            cache->AddProperty(UIA_ProcessIdPropertyId);
            // Include XAML containers outside Control View. Only this HWND subtree
            // is read, and Name/Value (including application titles) are never queried.
            condition = automation->CreateTrueCondition();
            cache->TreeFilter = condition;
        }
        catch
        {
            Graphics.Release(condition);
            Graphics.Release(cache);
            condition = null;
            cache = null;
            throw;
        }
    }

    private static TaskbarControl[] ReadControls(
        IUIAutomationElement* root, IUIAutomationCacheRequest* cache, IUIAutomationCondition* condition)
    {
        IUIAutomationElementArray* elements = null;
        try
        {
            elements = root->FindAllBuildCache(TreeScope.TreeScope_Descendants, condition, cache);
            int count = elements == null ? 0 : elements->Length;
            List<TaskbarControl> controls = new(count);
            for (int i = 0; i < count; i++)
            {
                var element = elements->GetElement(i);
                if (element == null)
                    continue;
                try
                {
                    if (element->CachedIsOffscreen || element->CachedProcessId == Environment.ProcessId)
                        continue;
                    var bounds = TaskbarStripShell.ToBounds(element->CachedBoundingRectangle);
                    if (bounds.Width <= 0 || bounds.Height <= 0)
                        continue;
                    var type = element->CachedControlType;
                    bool button = type is UIA_CONTROLTYPE_ID.UIA_ButtonControlTypeId or
                        UIA_CONTROLTYPE_ID.UIA_SplitButtonControlTypeId or UIA_CONTROLTYPE_ID.UIA_MenuItemControlTypeId;
                    controls.Add(new(bounds, ReadId(element->CachedAutomationId),
                        ReadId(element->CachedClassName), button));
                }
                finally
                {
                    Graphics.Release(element);
                }
            }
            return controls.ToArray();
        }
        finally
        {
            Graphics.Release(elements);
        }
    }

    private static string ReadId(BSTR value)
    {
        try { return value.ToString() ?? ""; }
        finally { Marshal.FreeBSTR(value); }
    }

    public void Dispose()
    {
        // Do not join an accessibility thread from a UI thread. Explorer can still
        // be answering a request. Run removes the subscriptions before releasing COM.
        lock (sync)
        {
            if (disposed)
                return;
            disposed = true;
            if (!changedDisposed)
            {
                try { changed.Set(); }
                catch (ObjectDisposedException) { }
            }
        }
    }

    private sealed class EventSink(Action changed) : IUIAutomationStructureChangedEventHandler.Interface,
        IUIAutomationPropertyChangedEventHandler.Interface
    {
        public HRESULT HandleStructureChangedEvent(IUIAutomationElement* sender, StructureChangeType changeType,
            SAFEARRAY* runtimeId)
        {
            changed();
            return HRESULT.S_OK;
        }

        public HRESULT HandlePropertyChangedEvent(IUIAutomationElement* sender, UIA_PROPERTY_ID propertyId, VARIANT value)
        {
            changed();
            return HRESULT.S_OK;
        }
    }

    private sealed class EventComWrappers : ComWrappers
    {
        public static readonly EventComWrappers Instance = new();
        private static readonly ComInterfaceEntry* Entries = CreateEntries();

        private static ComInterfaceEntry* CreateEntries()
        {
            GetIUnknownImpl(out nint query, out nint addRef, out nint release);
            var entries = (ComInterfaceEntry*)RuntimeHelpers.AllocateTypeAssociatedMemory(
                typeof(EventComWrappers), 2 * sizeof(ComInterfaceEntry));
            var structure = (IUIAutomationStructureChangedEventHandler.Vtbl*)Vtable();
            IUIAutomationStructureChangedEventHandler.PopulateVTable(structure);
            var property = (IUIAutomationPropertyChangedEventHandler.Vtbl*)Vtable();
            IUIAutomationPropertyChangedEventHandler.PopulateVTable(property);
            entries[0] = new() { IID = IUIAutomationStructureChangedEventHandler.IID_Guid, Vtable = (nint)structure };
            entries[1] = new() { IID = IUIAutomationPropertyChangedEventHandler.IID_Guid, Vtable = (nint)property };
            return entries;

            nint Vtable()
            {
                nint table = RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(EventComWrappers), 4 * sizeof(nint));
                var slots = (nint*)table;
                slots[0] = query;
                slots[1] = addRef;
                slots[2] = release;
                return table;
            }
        }

        protected override ComInterfaceEntry* ComputeVtables(object obj, CreateComInterfaceFlags flags, out int count)
        {
            count = 2;
            return Entries;
        }

        protected override object CreateObject(nint externalComObject, CreateObjectFlags flags) =>
            throw new NotSupportedException();

        protected override void ReleaseObjects(System.Collections.IEnumerable objects) =>
            throw new NotSupportedException();
    }
}
