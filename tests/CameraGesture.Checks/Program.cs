using System.Numerics;
using Robust.Client.UserInterface.Mobile;

var checks = 0;
void Check(bool value, string name)
{
    if (!value) throw new Exception(name);
    Console.WriteLine("PASS " + name);
    checks++;
}
var state = new CameraTouchState();
var mouse = new List<(TouchMouseAction Action, Vector2 Position)>();
var transforms = new List<(float Ratio, float Angle)>();
state.Mouse += (action, position) => mouse.Add((action, position));
state.Transform += (ratio, angle) => transforms.Add((ratio, angle));
void Reset() { state.Cancel(); mouse.Clear(); transforms.Clear(); }
void Down(long id, Vector2 pos, CameraTouchTarget target = CameraTouchTarget.World) => state.Down(id, pos, target, 0);
Down(1, Vector2.Zero);
Check(mouse.Count == 0, "first world finger does not prematurely click an object");
Down(2, new Vector2(100, 0));
Check(state.GestureActive, "two world fingers reserve a camera gesture");
state.Move(2, new Vector2(200, 0));
Check(transforms.Count == 1 && MathF.Abs(transforms[0].Ratio - 2) < .001f && transforms[0].Angle == 0,
    "spreading fingers doubles magnification without rotation");
state.Move(2, new Vector2(100, 0));
Check(MathF.Abs(transforms[^1].Ratio - .5f) < .001f, "pinching back emits inverse magnification");
state.Move(2, new Vector2(0, 100));
Check(MathF.Abs(transforms[^1].Angle - MathF.PI / 2) < .001f, "quarter-turn emits clockwise rotation");
state.Up(1, false);
state.Move(2, new Vector2(400, 200));
state.Up(2, false);
Check(mouse.Count == 0 && !state.GestureActive, "gesture and remaining finger never generate game clicks");
Reset();
Down(1, Vector2.Zero, CameraTouchTarget.Interface);
Check(mouse.Count == 2 && mouse[^1].Action == TouchMouseAction.Down, "UI receives immediate original mouse down");
Down(2, new Vector2(100, 0));
state.Move(2, new Vector2(200, 0));
Check(!state.GestureActive && transforms.Count == 0, "a finger on UI cannot join a world gesture");
state.Move(1, new Vector2(20, 30));
Check(mouse[^1] == (TouchMouseAction.Move, new Vector2(20, 30)), "UI dragging retains pointer movement");
state.Up(1, false);
Check(mouse[^1].Action == TouchMouseAction.Up, "UI dragging releases its pointer");
Reset();
Down(1, Vector2.Zero, CameraTouchTarget.Overlay);
Down(2, Vector2.Zero);
Down(3, new Vector2(100, 0));
state.Move(3, new Vector2(150, 0));
Check(state.GestureActive && transforms.Count == 1 && mouse.Count == 0,
    "joystick finger stays independent of two world fingers");
state.Invalidate(2);
state.Move(3, new Vector2(300, 0));
Check(!state.GestureActive && transforms.Count == 1 && mouse.Count == 0,
    "crossing onto UI cancels the gesture without converting it into a click");
Down(4, new Vector2(200, 200));
state.Up(4, false);
Check(mouse.Count == 0, "new world touches cannot click while a cancelled gesture is draining");
Reset();
Down(1, new Vector2(10, 20));
state.Up(1, false);
Check(mouse.Select(x => x.Action).SequenceEqual(new[] { TouchMouseAction.Move, TouchMouseAction.Down, TouchMouseAction.Up }),
    "single world tap is delivered exactly once through the mouse path");
Reset();
Down(1, Vector2.Zero);
state.Move(1, new Vector2(30, 0));
Check(mouse.Select(x => x.Action).SequenceEqual(new[] { TouchMouseAction.Move, TouchMouseAction.Down, TouchMouseAction.Move }),
    "single finger world dragging starts at the original point");
Down(2, new Vector2(100, 0));
Check(!state.GestureActive, "already committed world drag cannot turn into an accidental camera gesture");
state.Up(1, false);
Check(mouse[^1].Action == TouchMouseAction.Up, "world drag releases normally");
Reset();
Down(1, Vector2.Zero);
state.Update(.34);
Check(mouse.Count == 0, "recognition interval leaves room for a second finger");
state.Update(.36);
Check(mouse[^1].Action == TouchMouseAction.Down, "single finger long hold retains held game actions");
state.Cancel();
Check(mouse[^1].Action == TouchMouseAction.Cancel, "cancellation releases held input without a UI click");
Reset();
Down(1, Vector2.Zero);
state.Up(1, true);
Check(mouse.Count == 0, "cancelled pending touch never uses an object");
Reset();
Down(1, Vector2.Zero);
Down(2, new Vector2(100, 0));
state.Move(2, new Vector2(102, 0));
Check(transforms.Count == 0, "minor pinch jitter is ignored");
state.Move(2, new Vector2(120, 0));
for (var degrees = 1; degrees <= 4; degrees++)
{
    var angle = degrees * MathF.PI / 180;
    state.Move(2, new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 120);
}
Check(transforms.Any(x => x.Angle > 0.04f), "slow rotation still activates during an existing pinch");
Reset();
Down(1, Vector2.Zero);
Down(2, new Vector2(-100, 1));
state.Move(2, new Vector2(-100, -8));
Check(transforms.Count == 1 && MathF.Abs(transforms[0].Angle) < .2f,
    "rotation across the angle wrap does not jump by a full turn");
Reset();
Down(1, Vector2.Zero);
Down(2, new Vector2(1, 0));
state.Move(2, new Vector2(100, 0));
Check(transforms.Count == 0, "nearly coincident fingers rebase without zoom spikes");
state.Move(2, new Vector2(150, 0));
Check(transforms.Count == 1 && float.IsFinite(transforms[0].Ratio), "gesture resumes with finite values after rebasing");
state.Move(2, new Vector2(float.NaN, 0));
Check(!state.GestureActive, "invalid coordinates cancel the pair safely");
Reset();
Down(1, Vector2.Zero);
Down(2, new Vector2(100, 0));
state.Cancel();
state.Move(2, new Vector2(200, 0));
state.Up(1, false);
Check(!state.GestureActive && transforms.Count == 0 && mouse.Count == 0, "focus loss ignores stale motions and releases");
Console.WriteLine($"PASS {checks} camera gesture checks");
