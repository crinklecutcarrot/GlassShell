Add-Type @"
using System;using System.Text;using System.Runtime.InteropServices;
public static class GlassStop {public delegate bool Callback(IntPtr h,IntPtr l);[DllImport("user32.dll")]public static extern bool EnumWindows(Callback c,IntPtr l);[DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);[DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int GetWindowText(IntPtr h,StringBuilder s,int n);[DllImport("user32.dll")]public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);}
"@
foreach($glassProcess in (Get-Process GlassShell -ErrorAction SilentlyContinue)) {
$glassPid=$glassProcess.Id
[GlassStop]::EnumWindows({param($h,$l) $windowPid=0;[void][GlassStop]::GetWindowThreadProcessId($h,[ref]$windowPid);if($windowPid -eq $glassPid){$title=New-Object Text.StringBuilder 256;[void][GlassStop]::GetWindowText($h,$title,256);if($title.ToString().EndsWith('Status')){[void][GlassStop]::PostMessage($h,0x312,[IntPtr]1,[IntPtr]::Zero)}};return $true},[IntPtr]::Zero)|Out-Null
if(-not $glassProcess.WaitForExit(5000)){throw 'App did not exit gracefully'}
}
