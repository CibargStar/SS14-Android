using System.Numerics;
using Robust.Client.UserInterface.Mobile;

var checks = 0;
void Check(bool success, string name)
{
    if (!success) throw new Exception(name);
    checks++;
    Console.WriteLine("PASS " + name);
}
Vector2 At(float degrees, float length = 40) =>
    new(MathF.Cos(degrees * MathF.PI / 180) * length, MathF.Sin(degrees * MathF.PI / 180) * length);
var stick = new ThumbstickState();
bool Press(long? id, Vector2 offset) => stick.Press(id, offset, 10, 43);
bool Move(long? id, Vector2 offset) => stick.Move(id, offset, 10, 43);
Check(!stick.IsPressed && stick.Direction == StickDirection.None, "idle stick does not move");
Check(Press(11, Vector2.Zero) && stick.IsPressed, "first finger owns the stick");
Move(11, new Vector2(9.9f, 0));
Check(stick.Direction == StickDirection.None, "small centre motions cannot start walking");
Move(11, new Vector2(10.1f, 0));
Check(stick.Direction == StickDirection.Right, "crossing the start radius begins walking");
for (var i = 0; i < 200; i++) Move(11, new Vector2(i % 2 == 0 ? 9.8f : 10.2f, 0));
Check(stick.Direction == StickDirection.Right, "radial jitter does not repeatedly stop and start walking");
Move(11, new Vector2(7, 0));
Check(stick.Direction == StickDirection.None, "moving back into the centre stops walking");
Move(11, At(0));
Check(!Press(22, At(180)) && !Move(22, At(180)) && !stick.Release(22), "another finger cannot steal or release a held stick");
Check(!Press(null, At(180)) && !Move(null, At(180)) && !stick.Release(null), "synthetic mouse input cannot steal a touch gesture");
Check(stick.Direction == StickDirection.Right && stick.FingerId == 11, "owner and direction survive unrelated pointer events");
for (var i = 0; i < 200; i++) Move(11, At(i % 2 == 0 ? 21 : 24));
Check(stick.Direction == StickDirection.Right, "angle jitter near a diagonal boundary keeps the cardinal direction");
Move(11, At(29));
Check(stick.Direction == (StickDirection.Right | StickDirection.Down), "a deliberate turn enters the diagonal");
for (var i = 0; i < 200; i++) Move(11, At(i % 2 == 0 ? 21 : 24));
Check(stick.Direction == (StickDirection.Right | StickDirection.Down), "angle jitter does not drop an established diagonal");
Move(11, At(16));
Check(stick.Direction == StickDirection.Right, "turning back deliberately returns to cardinal movement");
Move(11, new Vector2(10000, 10000));
Check(MathF.Abs(stick.Offset.Length() - 43) < 0.001f, "dragging outside the ring clamps the knob inside its base");
Check(stick.Release(11) && !stick.IsPressed && stick.Offset == Vector2.Zero && stick.Direction == StickDirection.None,
    "lifting the owning finger clears every movement direction");
Check(!Move(11, At(0)), "late motion after release cannot restart walking");
var directions = new[] { StickDirection.Right, StickDirection.Right | StickDirection.Down, StickDirection.Down,
    StickDirection.Down | StickDirection.Left, StickDirection.Left, StickDirection.Left | StickDirection.Up,
    StickDirection.Up, StickDirection.Up | StickDirection.Right };
for (var sector = 0; sector < directions.Length; sector++)
{
    Press(11, At(sector * 45));
    Check(stick.Direction == directions[sector], "correct direction at " + sector * 45 + " degrees");
    stick.Cancel();
}
Press(11, At(170));
for (var i = 0; i < 200; i++) Move(11, At(i % 2 == 0 ? 179 : -179));
Check(stick.Direction == StickDirection.Left, "angle wraparound does not disturb leftward movement");
Move(11, new Vector2(float.NaN, 1));
Check(stick.Direction == StickDirection.None && stick.Offset == Vector2.Zero, "invalid pointer coordinates stop movement");
Move(11, At(90));
Move(11, new Vector2(float.PositiveInfinity, 0));
Check(stick.Direction == StickDirection.None, "infinite pointer coordinates cannot latch movement");
Move(11, At(90));
stick.Cancel();
Check(!stick.IsPressed && stick.Direction == StickDirection.None, "cancellation or focus loss resets the gesture");
Check(Press(22, At(-90)) && stick.Direction == StickDirection.Up, "a new finger works after cancellation");
Check(!stick.Release(11) && stick.IsPressed, "a late release from the old gesture cannot cancel the new one");
stick.Cancel();
Check(Press(null, At(180)) && Move(null, At(90)) && stick.Release(null), "mouse dragging retains the same stick behaviour");
for (var angle = -360; angle <= 360; angle++)
{
    Press(11, At(angle));
    var d = stick.Direction;
    if ((d & (StickDirection.Up | StickDirection.Down)) == (StickDirection.Up | StickDirection.Down) ||
        (d & (StickDirection.Left | StickDirection.Right)) == (StickDirection.Left | StickDirection.Right) || d == StickDirection.None)
        throw new Exception("Opposite or missing directions at " + angle);
    stick.Cancel();
}
Check(true, "full angle sweep never presses opposite keys or loses a direction");
Console.WriteLine($"PASS {checks} thumbstick checks");
