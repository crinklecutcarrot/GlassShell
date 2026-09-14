using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GlassShell;

internal sealed class TrayIconItem
{
    public required string Key { get; init; }
    public IntPtr Owner { get; set; }
    public uint Uid { get; set; }
    public uint Callback { get; set; }
    public uint Version { get; set; }
    public Guid Guid { get; set; }
    public string Tooltip { get; set; } = "Background app";
    public ImageSource? Image { get; set; }
    public bool Visible { get; set; } = true;
    public bool Backfilled { get; set; }
    public string AutomationName { get; set; } = "";
}

internal sealed class TrayService : IDisposable
{
    const ulong GlassTrayCopyData = 0x52544C47;
    const uint NimAdd = 0, NimModify = 1, NimDelete = 2, NimSetVersion = 4;
    const uint NifMessage = 1, NifIcon = 2, NifTip = 4, NifGuid = 0x20;
    readonly Dictionary<string, TrayIconItem> icons = new(StringComparer.OrdinalIgnoreCase);
    IntPtr module, hook;
    int backfillRunning;
    bool testAvailable;
    public bool Available => hook != IntPtr.Zero || testAvailable;
    internal bool NativeFlyoutRequested { get; private set; }
    public IReadOnlyList<TrayIconItem> Icons => icons.Values.Where(icon => icon.Backfilled || Native.IsWindow(icon.Owner)).OrderBy(icon => icon.Tooltip).ToArray();
    public event Action? Changed;

    public void Start()
    {
        if (Storage.OverrideRoot != null || hook != IntPtr.Zero) return;
        string path = Path.Combine(AppContext.BaseDirectory, "GlassShell.TrayHook.dll");
        if (!File.Exists(path)) { Storage.Log("Tray hook unavailable: native DLL was not built"); return; }
        // Explorer can retain an injected module after a hook is removed. Load a
        // disposable, uniquely named copy so upgrades never lock the build or app.
        string runtimeDirectory = Path.Combine(Storage.Root, "hooks");
        Directory.CreateDirectory(runtimeDirectory);
        string runtimePath = Path.Combine(runtimeDirectory, $"GlassShell.TrayHook.{Environment.ProcessId}.{Guid.NewGuid():N}.dll");
        File.Copy(path, runtimePath, true);
        module = LoadLibrary(runtimePath);
        if (module == IntPtr.Zero) { Storage.Log("Tray hook LoadLibrary failed: " + Marshal.GetLastWin32Error()); return; }
        IntPtr procedure = GetProcAddress(module, "CallWndProc");
        IntPtr shellTray = FindWindow("Shell_TrayWnd", null);
        uint thread = Native.GetWindowThreadProcessId(shellTray, out _);
        if (procedure == IntPtr.Zero || shellTray == IntPtr.Zero || thread == 0)
        {
            Storage.Log("Tray hook could not resolve Explorer entry points"); Dispose(); return;
        }
        hook = SetWindowsHookEx(4, procedure, module, thread); // WH_CALLWNDPROC
        if (hook == IntPtr.Zero) { Storage.Log("Tray hook install failed: " + Marshal.GetLastWin32Error()); Dispose(); return; }
        Storage.Log("Tray hook installed on Explorer thread " + thread);
        uint taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
        SendNotifyMessage(new IntPtr(0xffff), taskbarCreated, UIntPtr.Zero, IntPtr.Zero);
        _ = Task.Run(() => { Thread.Sleep(900); RefreshBackfill(); });
    }

    public void RefreshBackfill()
    {
        if (Storage.OverrideRoot == null && Interlocked.CompareExchange(ref backfillRunning, 1, 0) == 0)
            _ = Task.Run(BackfillFromExplorer);
    }

    public void ShowNativeOverflow()
    {
        if (Storage.OverrideRoot != null) { NativeFlyoutRequested = true; return; }
        _ = Task.Run(() =>
        {
            try
            {
                var root = AutomationElement.RootElement;
                var chevrons = root.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTrayIcon"));
                foreach (AutomationElement candidate in chevrons)
                {
                    if (!(candidate.Current.Name?.StartsWith("Show Hidden Icons", StringComparison.OrdinalIgnoreCase) ?? false)) continue;
                    if (candidate.TryGetCurrentPattern(InvokePattern.Pattern, out object pattern))
                        ((InvokePattern)pattern).Invoke();
                    return;
                }
                Storage.Log("Native tray flyout button was not found");
            }
            catch (Exception ex) { Storage.Log("Native tray flyout: " + ex.Message); }
        });
    }

    public bool ProcessCopyData(IntPtr lparam)
    {
        if (lparam == IntPtr.Zero) return false;
        var copy = Marshal.PtrToStructure<CopyData>(lparam);
        if (copy.Data.ToUInt64() != GlassTrayCopyData || copy.Payload == IntPtr.Zero || copy.Size < Marshal.SizeOf<TrayEvent>()) return false;
        var data = Marshal.PtrToStructure<TrayEvent>(copy.Payload);
        string proposedKey = data.Guid != Guid.Empty ? data.Guid.ToString("D") : $"{data.Owner:x}_{data.Uid}";
        var matched = icons.Values.FirstOrDefault(icon =>
            (data.Guid != Guid.Empty && icon.Guid == data.Guid) ||
            (icon.Owner == new IntPtr(unchecked((long)data.Owner)) && icon.Uid == data.Uid));
        string key = matched?.Key ?? proposedKey;
        if (data.Operation == NimDelete) { if (icons.Remove(key)) Changed?.Invoke(); return true; }
        if (data.Operation is not (NimAdd or NimModify or NimSetVersion)) return false;
        bool added;
        TrayIconItem item;
        if (matched != null) { item = matched; added = false; }
        else if (icons.TryGetValue(key, out var existing)) { item = existing; added = false; }
        else
        {
            added = true;
            if (data.Owner == 0) return true;
            item = new TrayIconItem { Key = key, Owner = new IntPtr(unchecked((long)data.Owner)), Uid = data.Uid, Guid = data.Guid };
            icons[key] = item;
        }
        if (added && !string.IsNullOrWhiteSpace(data.Tooltip))
        {
            string? fallback = icons.FirstOrDefault(pair => pair.Value.Backfilled &&
                string.Equals(pair.Value.AutomationName, data.Tooltip.Trim(), StringComparison.OrdinalIgnoreCase)).Key;
            if (fallback != null) icons.Remove(fallback);
        }
        if (data.Owner != 0) item.Owner = new IntPtr(unchecked((long)data.Owner));
        if (data.Uid != 0 || item.Uid == 0) item.Uid = data.Uid;
        if ((data.Flags & NifMessage) != 0) item.Callback = data.Callback;
        if ((data.Flags & NifTip) != 0 && !string.IsNullOrWhiteSpace(data.Tooltip)) item.Tooltip = data.Tooltip.Trim();
        if ((data.Flags & NifGuid) != 0) item.Guid = data.Guid;
        if (data.Version is > 0 and <= 4) item.Version = data.Version;
        item.Visible = data.Visible != 0;
        if ((data.Flags & NifIcon) != 0 && data.Icon != 0)
        {
            try
            {
                var image = Imaging.CreateBitmapSourceFromHIcon(new IntPtr(unchecked((long)data.Icon)), Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(24, 24));
                image.Freeze(); item.Image = image;
            }
            catch (Exception ex) { Storage.Log("Tray icon decode: " + ex.Message); }
        }
        if (added || data.Operation == NimSetVersion) Storage.Log($"Tray icon {(added ? "captured" : "versioned")}: {item.Tooltip} ({item.Key}), visible={item.Visible}, version={item.Version}, payload={data.SourceSize}, callback=0x{item.Callback:x}");
        Changed?.Invoke(); return true;
    }

    public void SendAction(TrayIconItem icon, string action)
    {
        if (Storage.OverrideRoot != null) { LastTestAction = action; return; }
        if (icon.Backfilled) { _ = Task.Run(() => ActivateBackfilled(icon.AutomationName, action)); return; }
        if (!Native.IsWindow(icon.Owner) || icon.Callback == 0) return;
        Native.GetWindowThreadProcessId(icon.Owner, out uint processId);
        AllowSetForegroundWindow(processId);
        // Version 4 icons use the packed NOTIFYICON_VERSION_4 protocol. Sending
        // legacy mouse messages first can open and immediately dismiss menus.
        if (icon.Version >= 4)
        {
            if (action == "left") Notify(icon, 0x0400);             // NIN_SELECT
            else if (action == "right") Notify(icon, 0x007B);       // WM_CONTEXTMENU
            else if (action == "double") Notify(icon, 0x0203);      // WM_LBUTTONDBLCLK
            else if (action == "middle") Notify(icon, 0x0208);      // WM_MBUTTONUP
            return;
        }
        uint[] messages = action switch
        {
            "double" => new uint[] { 0x0203, 0x0202 },
            "right" => new uint[] { 0x0204, 0x0205 },
            "middle" => new uint[] { 0x0207, 0x0208 },
            _ => new uint[] { 0x0201, 0x0202 },
        };
        foreach (uint message in messages) Notify(icon, message);
    }

    internal string? LastTestAction { get; private set; }
    internal void SetTestIcon(IntPtr owner)
    {
        if (Storage.OverrideRoot == null) throw new InvalidOperationException("Test tray icon requires isolated test mode");
        testAvailable = true;
        icons["test"] = new TrayIconItem { Key = "test", Owner = owner, Uid = 1, Callback = 0x500, Tooltip = "Test background app", Visible = true };
        Changed?.Invoke();
    }

    void Notify(TrayIconItem icon, uint message)
    {
        nuint wparam; nint lparam;
        if (icon.Version > 3)
        {
            Native.GetCursorPos(out var cursor);
            wparam = (nuint)(uint)((ushort)cursor.X | ((uint)(ushort)cursor.Y << 16));
            lparam = (nint)(int)((ushort)message | ((uint)(ushort)icon.Uid << 16));
        }
        else { wparam = icon.Uid; lparam = (nint)message; }
        SendNotifyMessage(icon.Owner, icon.Callback, wparam, lparam);
    }

    void BackfillFromExplorer()
    {
        try
        {
            var entries = OpenOverflowAndFindIcons();
            var found = new List<(string Name, ImageSource? Image)>();
            foreach (AutomationElement element in entries)
            {
                string name = element.Current.Name?.Trim() ?? "";
                if (name.Length > 0) found.Add((name, CaptureElement(element)));
            }
            CloseOverflow();
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                int added = 0;
                foreach (var entry in found)
                {
                    if (icons.Values.Any(icon => string.Equals(icon.Tooltip, entry.Name, StringComparison.OrdinalIgnoreCase))) continue;
                    string key = "uia_" + entry.Name;
                    int suffix = 2; while (icons.ContainsKey(key)) key = "uia_" + entry.Name + "_" + suffix++;
                    icons[key] = new TrayIconItem { Key = key, Tooltip = entry.Name, AutomationName = entry.Name,
                        Backfilled = true, Image = entry.Image };
                    added++;
                }
                if (added > 0) { Storage.Log($"Tray UI Automation backfill added {added} icons"); Changed?.Invoke(); }
            }));
        }
        catch (Exception ex) { CloseOverflow(); Storage.Log("Tray backfill: " + ex.Message); }
        finally { Interlocked.Exchange(ref backfillRunning, 0); }
    }

    static AutomationElementCollection OpenOverflowAndFindIcons()
    {
        var root = AutomationElement.RootElement;
        var chevrons = root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTrayIcon"));
        foreach (AutomationElement candidate in chevrons)
        {
            if (!(candidate.Current.Name?.StartsWith("Show Hidden Icons", StringComparison.OrdinalIgnoreCase) ?? false)) continue;
            if (candidate.TryGetCurrentPattern(InvokePattern.Pattern, out object pattern))
                ((InvokePattern)pattern).Invoke();
            break;
        }
        Thread.Sleep(350);
        return root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "NotifyItemIcon"));
    }

    static void CloseOverflow()
    {
        try
        {
            var root = AutomationElement.RootElement;
            var chevrons = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTrayIcon"));
            foreach (AutomationElement candidate in chevrons)
            {
                string name = candidate.Current.Name ?? "";
                if (!name.StartsWith("Show Hidden Icons", StringComparison.OrdinalIgnoreCase) ||
                    !name.Contains("Hide", StringComparison.OrdinalIgnoreCase)) continue;
                if (candidate.TryGetCurrentPattern(InvokePattern.Pattern, out object pattern))
                    ((InvokePattern)pattern).Invoke();
                break;
            }
        }
        catch { }
    }

    static ImageSource? CaptureElement(AutomationElement element)
    {
        try
        {
            var r = element.Current.BoundingRectangle;
            if (r.Width < 1 || r.Height < 1) return null;
            using var bitmap = new System.Drawing.Bitmap((int)Math.Ceiling(r.Width), (int)Math.Ceiling(r.Height));
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                graphics.CopyFromScreen((int)r.Left, (int)r.Top, 0, 0, bitmap.Size);
            IntPtr handle = bitmap.GetHbitmap();
            try { var image = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(24, 24)); image.Freeze(); return image; }
            finally { DeleteObject(handle); }
        }
        catch { return null; }
    }

    static void ActivateBackfilled(string name, string action)
    {
        try
        {
            var entries = OpenOverflowAndFindIcons(); AutomationElement? match = null;
            foreach (AutomationElement element in entries)
                if (string.Equals(element.Current.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase)) { match = element; break; }
            if (match == null) { CloseOverflow(); return; }
            var r = match.Current.BoundingRectangle;
            int x = (int)(r.Left + r.Width / 2), y = (int)(r.Top + r.Height / 2);
            SetCursorPos(x, y);
            uint down = action == "right" ? 0x0008u : action == "middle" ? 0x0020u : 0x0002u;
            uint up = action == "right" ? 0x0010u : action == "middle" ? 0x0040u : 0x0004u;
            mouse_event(down, 0, 0, 0, UIntPtr.Zero); mouse_event(up, 0, 0, 0, UIntPtr.Zero);
            if (action == "double") { Thread.Sleep(60); mouse_event(down, 0, 0, 0, UIntPtr.Zero); mouse_event(up, 0, 0, 0, UIntPtr.Zero); }
        }
        catch { CloseOverflow(); }
    }

    public void Dispose()
    {
        if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        if (module != IntPtr.Zero) { FreeLibrary(module); module = IntPtr.Zero; }
        icons.Clear();
        testAvailable = false;
    }

    [StructLayout(LayoutKind.Sequential)] struct CopyData { public UIntPtr Data; public int Size; public IntPtr Payload; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
    struct TrayEvent
    {
        public uint Operation, Uid, Callback, Version, Flags, Visible, SourceSize;
        public ulong Owner, Icon;
        public Guid Guid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tooltip;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibrary(string path);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, IntPtr procedure, IntPtr module, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string? className, string? title);
    [DllImport("user32.dll")] static extern bool SendNotifyMessage(IntPtr hwnd, uint message, nuint wparam, nint lparam);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr handle);
}
