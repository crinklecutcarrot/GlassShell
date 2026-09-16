using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace GlassShell;

internal sealed record DockApp(string Key, string Name, string LaunchPath, ImageSource? Icon, IReadOnlyList<IntPtr> Windows, bool Pinned);

internal sealed class DockService : IDisposable
{
    readonly int ownPid = Environment.ProcessId;
    List<string> pins = new();
    string signature = "";
    public IReadOnlyList<DockApp> Apps { get; private set; } = Array.Empty<DockApp>();
    public event Action? Changed;
    public DockService() { LoadPins(); }
    void LoadPins()
    {
        try { pins = JsonSerializer.Deserialize<List<string>>(Storage.Read("dock-pins.json")) ?? new(); } catch { pins = new(); }
        if (pins.Count != 0) return;
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
        if (Directory.Exists(folder)) pins.AddRange(Directory.GetFiles(folder, "*.lnk").OrderBy(x => x));
    }
    void SavePins() => Storage.Write("dock-pins.json", JsonSerializer.Serialize(pins));
    public void Refresh()
    {
        var windows = new List<(IntPtr hwnd, string key, string name, string path)>();
        Native.EnumWindows((h, _) =>
        {
            if (!Native.IsWindowVisible(h) || Native.Title(h).Length == 0 || Native.GetWindow(h, 4) != IntPtr.Zero) return true;
            Native.GetWindowThreadProcessId(h, out uint pid); if (pid == ownPid || pid == 0) return true;
            try { using var p = Process.GetProcessById((int)pid); string path = p.MainModule?.FileName ?? ""; if (path.Length == 0) return true; windows.Add((h, path.ToLowerInvariant(), p.MainWindowTitle.Length > 0 ? p.MainWindowTitle : p.ProcessName, path)); } catch { }
            return true;
        }, IntPtr.Zero);
        var result = new List<DockApp>();
        foreach (string pin in pins.ToArray())
        {
            string target = ShortcutTarget(pin); string key = (target.Length > 0 ? target : pin).ToLowerInvariant(); var matching = windows.Where(w => w.key == key || Path.GetFileNameWithoutExtension(w.path).Equals(Path.GetFileNameWithoutExtension(target), StringComparison.OrdinalIgnoreCase)).ToList();
            result.Add(new DockApp(key, Path.GetFileNameWithoutExtension(pin), pin, Icon(target.Length > 0 ? target : pin), matching.Select(x => x.hwnd).ToArray(), true));
        }
        foreach (var group in windows.GroupBy(x => x.key).Where(g => !result.Any(a => a.Windows.Intersect(g.Select(x => x.hwnd)).Any())))
        { var first = group.First(); result.Add(new DockApp(first.key, Path.GetFileNameWithoutExtension(first.path), first.path, Icon(first.path), group.Select(x => x.hwnd).ToArray(), false)); }
        string next = string.Join("|", result.Select(app => app.Key + ":" + app.Pinned + ":" + string.Join(",", app.Windows)));
        if (next == signature) return; signature = next; Apps = result; Changed?.Invoke();
    }
    static ImageSource? Icon(string path)
    {
        try { using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path); if (icon == null) return null; var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(64, 64)); source.Freeze(); return source; } catch { return null; }
    }
    static string ShortcutTarget(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return path;
        try { var link = (IShellLinkW)new ShellLink(); ((IPersistFile)link).Load(path, 0); var target = new StringBuilder(1024); link.GetPath(target, target.Capacity, IntPtr.Zero, 0); return target.ToString(); } catch { return ""; }
    }
    public void Activate(DockApp app)
    {
        if (app.Windows.Count == 0) { try { Process.Start(new ProcessStartInfo(app.LaunchPath) { UseShellExecute = true }); } catch { } return; }
        var h = app.Windows[0]; if (Native.GetForegroundWindow() == h) Native.ShowWindow(h, 6); else { Native.ShowWindow(h, 9); Native.BringWindowToTop(h); Native.SetForegroundWindow(h); }
    }
    public void TogglePin(DockApp app) { if (app.Pinned) pins.RemoveAll(x => x.Equals(app.LaunchPath, StringComparison.OrdinalIgnoreCase)); else pins.Add(app.LaunchPath); SavePins(); Refresh(); }
    public void Move(DockApp app, int delta) { int i = pins.FindIndex(x => x.Equals(app.LaunchPath, StringComparison.OrdinalIgnoreCase)); if (i < 0) return; int n = Math.Clamp(i + delta, 0, pins.Count - 1); if (n == i) return; (pins[i], pins[n]) = (pins[n], pins[i]); SavePins(); Refresh(); }
    public void Dispose() { }
}

[ComImport, Guid("00021401-0000-0000-C000-000000000046")] internal class ShellLink { }
[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int count, IntPtr findData, uint flags);
    void GetIDList(out IntPtr idList); void SetIDList(IntPtr idList); void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int count); void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name); void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int count); void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir); void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int count); void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args); void GetHotkey(out short hotkey); void SetHotkey(short hotkey); void GetShowCmd(out int command); void SetShowCmd(int command); void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int count, out int iconIndex); void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex); void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved); void Resolve(IntPtr hwnd, uint flags); void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
}
