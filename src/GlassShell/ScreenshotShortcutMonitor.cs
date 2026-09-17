using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace GlassShell;

// Observe Win+Shift+S before Windows builds the Snipping Tool background frame.
// The hook never swallows the shortcut; it only freezes GlassShell first.
internal sealed class ScreenshotShortcutMonitor : IDisposable
{
    readonly HookProc callback; readonly Action pressed; IntPtr hook; bool chordDown;
    public ScreenshotShortcutMonitor(Action onPressed)
    {
        pressed = onPressed; callback = OnHook; hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not observe screenshot shortcut");
    }
    IntPtr OnHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            int kind = message.ToInt32(); var key = Marshal.PtrToStructure<KeyboardData>(data);
            if (key.Key == 0x53 && kind is 0x0100 or 0x0104)
            {
                bool chord = Down(0x5B) || Down(0x5C); chord &= Down(0x10);
                if (chord && !chordDown) { chordDown = true; try { pressed(); } catch (Exception e) { Storage.Log("Screenshot shortcut: " + e.Message); } }
            }
            else if (key.Key == 0x53 && kind is 0x0101 or 0x0105) chordDown = false;
        }
        return CallNextHookEx(hook, code, message, data);
    }
    static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    public void Dispose() { if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; } }
    [StructLayout(LayoutKind.Sequential)] struct KeyboardData { public uint Key, Scan, Flags, Time; public UIntPtr Extra; }
    delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
}
