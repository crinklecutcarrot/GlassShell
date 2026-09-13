using GlassShell;

var failures = new List<string>();
void Check(bool passed, string name) { Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}"); if (!passed) failures.Add(name); }
var model = new ShellModel();
model.StartTimer(TimeSpan.FromSeconds(2)); model.Advance(TimeSpan.FromSeconds(3));
Check(model.TimerFinished && model.Remaining == TimeSpan.FromSeconds(-1) && model.TimerRunning, "Timer enters overtime and keeps running");
Check(model.TimerProgress == 0 && model.TimerText == "−00:01", "Expired progress clamps and overtime is formatted");
model.StartTimer(TimeSpan.FromSeconds(10)); model.PauseResume(); var remaining = model.Remaining; model.Advance(TimeSpan.FromHours(1));
Check(model.Remaining == remaining && !model.TimerRunning, "Paused timer does not advance");
model.PauseResume(); model.Advance(TimeSpan.FromSeconds(20));
Check(model.TimerFinished && model.Remaining < TimeSpan.Zero, "Resumed timer completes after elapsed time");
model.ResetTimer(); Check(!model.TimerFinished && !model.TimerRunning && !model.TimerActive && model.Remaining == TimeSpan.Zero, "Reset clears completion state");
bool rejected = false; try { model.StartTimer(TimeSpan.Zero); } catch (ArgumentOutOfRangeException) { rejected = true; }
Check(rejected, "Invalid timer duration is rejected");
model.StartTimer(TimeSpan.FromMinutes(2)); model.Advance(TimeSpan.FromSeconds(30)); model.AddMinute();
Check(model.Duration == TimeSpan.FromMinutes(3) && model.Remaining.TotalSeconds > 149, "Add minute extends remaining time and progress denominator");
model.PauseResume(); model.AddMinute(); Check(!model.TimerRunning, "Adding time preserves pause");
model.PauseResume();model.Advance(TimeSpan.FromHours(1));model.AddMinute();Check(!model.TimerFinished && model.TimerRunning && model.Remaining.TotalSeconds > 59, "Adding to expired timer starts a fresh minute");
var spring = new Spring(292) { Target = 490 };
for (int i = 0; i < 10; i++) spring.Step(1.0 / 60);
var value = spring.Value; var velocity = spring.Velocity; spring.Target = 292;
Check(spring.Value == value && spring.Velocity == velocity, "Interrupted morph preserves current position and velocity");
for (int i = 0; i < 300; i++) spring.Step(1.0 / 60);
Check(Math.Abs(spring.Value - 292) < .1 && !spring.Active, "Reversed morph settles at compact size");
spring.Target = 490; spring.Step(30);
Check(double.IsFinite(spring.Value) && spring.Value >= 292 && spring.Value <= 510, "Long frame stall does not destabilize the spring");
Check(RoundedShape.Contains(146, 22, 292, 44, 22), "Compact pill center accepts inside clicks");
Check(!RoundedShape.Contains(0, 0, 292, 44, 22), "Transparent rounded corner counts as outside");
Check(!RoundedShape.Contains(292, 22, 292, 44, 22), "Right edge is outside the visible surface");
Check(RoundedShape.Contains(2, 22, 292, 44, 22), "Visible curved edge remains interactive");
Check(!RoundedShape.Contains(490, 276, 490, 276, 30), "Expanded bounds exclude invisible host area");
Check(RoundedShape.Contains(10, 10, 100, 30, 0), "Rectangular status bar hit testing works");
return failures.Count == 0 ? 0 : 1;

