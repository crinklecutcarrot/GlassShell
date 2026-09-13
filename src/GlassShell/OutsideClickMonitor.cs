using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace GlassShell;

// Observe presses without swallowing or replaying them. WH_MOUSE_LL executes on
// the installing UI thread, so the callback only snapshots state and queues work.
internal sealed class OutsideClickMonitor : IDisposable
{
    private readonly HookProc callback;
    private readonly Action<Native.Point> pressed;
    private IntPtr hook;
    public OutsideClickMonitor(Action<Native.Point> onPressed)
    {
        pressed = onPressed; callback = OnHook;
        hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not observe outside clicks");
    }
    private IntPtr OnHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && message.ToInt32() is 0x0201 or 0x0204 or 0x0207 or 0x020B)
        {
            try { pressed(Marshal.PtrToStructure<MouseData>(data).Point); }
            catch (Exception ex) { Storage.Log("Outside click: " + ex.Message); }
        }
        return CallNextHookEx(hook, code, message, data);
    }
    public void Dispose() { if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; } }
    [StructLayout(LayoutKind.Sequential)] private struct MouseData { public Native.Point Point; public uint Mouse, Flags, Time; public UIntPtr Extra; }
    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
