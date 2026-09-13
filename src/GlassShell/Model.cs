using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace GlassShell;

public static class RoundedShape
{
    public static bool Contains(double x, double y, double width, double height, double radius)
    {
        if (x < 0 || y < 0 || x >= width || y >= height) return false;
        double r = Math.Clamp(radius, 0, Math.Min(width, height) / 2);
        double cx = Math.Clamp(x, r, width - r), cy = Math.Clamp(y, r, height - r);
        return (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;
    }
}

// These values describe application state. Animation never owns timers or note text.
public sealed class ShellModel
{
    public TimeSpan Remaining { get; private set; }
    public TimeSpan Duration { get; private set; }
    public bool TimerRunning { get; private set; }
    public bool TimerFinished { get; private set; }
    public bool TimerActive => Duration > TimeSpan.Zero;
    public double TimerProgress => TimerActive ? Math.Clamp(Remaining.TotalSeconds / Duration.TotalSeconds, 0, 1) : 0;
    public event Action? Changed;
    private long lastTick;
    public void StartTimer(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        Duration = Remaining = duration; TimerRunning = true; TimerFinished = false; lastTick = Stopwatch.GetTimestamp(); Changed?.Invoke();
    }
    public void PauseResume()
    {
        if (TimerRunning) Tick();
        if (Remaining <= TimeSpan.Zero) return;
        TimerRunning = !TimerRunning; lastTick = Stopwatch.GetTimestamp(); Changed?.Invoke();
    }
    public void AddMinute()
    {
        if (!TimerActive) return;
        if (TimerRunning) Tick();
        Remaining = (Remaining > TimeSpan.Zero ? Remaining : TimeSpan.Zero) + TimeSpan.FromMinutes(1);
        Duration += TimeSpan.FromMinutes(1);
        if (TimerFinished) { TimerFinished = false; TimerRunning = true; }
        lastTick = Stopwatch.GetTimestamp(); Changed?.Invoke();
    }
    public void ResetTimer() { TimerRunning = false; TimerFinished = false; Duration = Remaining = TimeSpan.Zero; Changed?.Invoke(); }
    public void Tick()
    {
        if (!TimerRunning) return;
        var now = Stopwatch.GetTimestamp();
        Advance(TimeSpan.FromSeconds((double)(now - lastTick) / Stopwatch.Frequency));
        lastTick = now;
    }
    public void Advance(TimeSpan elapsed)
    {
        if (!TimerRunning || elapsed <= TimeSpan.Zero) return;
        Remaining -= elapsed;
        if (Remaining <= TimeSpan.Zero) TimerFinished = true;
        Changed?.Invoke();
    }
    public string TimerText => FormatTime(Remaining, TimerFinished);
    public static string FormatTime(TimeSpan value, bool overtime = false)
    {
        double seconds = overtime ? Math.Floor(Math.Abs(value.TotalSeconds)) : Math.Ceiling(Math.Max(0, value.TotalSeconds));
        var time = TimeSpan.FromSeconds(seconds);
        string text = time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
        return (overtime ? "−" : "") + text;
    }
}

public sealed class Spring
{
    public double Value, Velocity, Target;
    public Spring(double value) { Value = Target = value; }
    public bool Active => Math.Abs(Target - Value) > .03 || Math.Abs(Velocity) > .03;
    public void Step(double elapsed)
    {
        // Small integration steps preserve velocity when a transition reverses.
        var remaining = Math.Min(elapsed, .1);
        while (remaining > 0)
        {
            var dt = Math.Min(remaining, 1.0 / 240);
            Velocity += ((Target - Value) * 230 - Velocity * 29) * dt;
            Value += Velocity * dt; remaining -= dt;
        }
        if (!Active) { Value = Target; Velocity = 0; }
    }
}

internal static class Storage
{
    internal static string? OverrideRoot { get; set; }
    internal static string Root => OverrideRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassShell");
    internal static void Write(string name, string text)
    {
        Directory.CreateDirectory(Root);
        var dest = Path.Combine(Root, name); var temp = dest + ".tmp";
        File.WriteAllText(temp, text); File.Move(temp, dest, true);
    }
    internal static string Read(string name) { var p = Path.Combine(Root, name); return File.Exists(p) ? File.ReadAllText(p) : ""; }
    internal static void Log(string text) { try { Directory.CreateDirectory(Root); File.AppendAllText(Path.Combine(Root, "session.log"), DateTime.Now.ToString("O") + " " + text + Environment.NewLine); } catch { } }
}
