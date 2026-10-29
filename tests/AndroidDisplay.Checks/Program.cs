using Robust.Client.UserInterface.Mobile;
using SS14.Android;
using SS14.Launcher.Models;

var checks = 0;
void Check(bool result, string name)
{
    if (!result) throw new Exception(name);
    checks++;
    Console.WriteLine("PASS " + name);
}
var resolutions = new[] { (2340, 1080), (3120, 1440), (1920, 1080), (1280, 591), (960, 540), (2560, 1600) };
foreach (var (width, height) in resolutions)
{
    var automatic = MobileDisplaySizing.UiScale(width, height, 0);
    Check(width / automatic >= 1279 && height / automatic >= 719,
        $"{width}x{height}: automatic UI retains the normal desktop workspace");
    var previousButtonPixels = 0f;
    foreach (var selected in new[] { 0.5f, 0.75f, 1f, 1.5f, 2f })
    {
        var ui = MobileDisplaySizing.UiScale(width, height, selected);
        Check(ui == selected, $"{width}x{height}: selected {selected * 100}% UI scale is honored");
        var buttonPixels = 54 * MobileDisplaySizing.ControlsScale(ui, height, 2) * ui;
        if (previousButtonPixels != 0)
            Check(MathF.Abs(buttonPixels - previousButtonPixels) < 0.01f,
                $"{width}x{height}: changing UI scale does not enlarge touch buttons");
        previousButtonPixels = buttonPixels;
    }
}
var fhd = MobileDisplaySizing.UiScale(2340, 1080, 0);
var qhd = MobileDisplaySizing.UiScale(3120, 1440, 0);
Check(MathF.Abs(2340 / fhd - 3120 / qhd) < 0.01f && MathF.Abs(1080 / fhd - 1440 / qhd) < 0.01f,
    "FHD+ and QHD+ produce the same logical interface layout");
var root = Path.Combine(Path.GetTempPath(), "ss14-display-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var launcher = new GameDisplaySettingsStore(root);
    var game = new GameDisplaySettingsStore(root);
    Check(launcher.Read() == new GameDisplaySettings(), "first launch selects automatic UI and visible touch controls");
    launcher.Write(new GameDisplaySettings(0.75f, false));
    Check(game.Read() == new GameDisplaySettings(0.75f, false), "game process reads launcher display settings");
    game.Write(new GameDisplaySettings(1.25f, true));
    Check(launcher.Read() == new GameDisplaySettings(1.25f, true), "launcher reads in-game changes without a stale process cache");
    var reopened = new GameDisplaySettingsStore(root);
    Check(reopened.Read() == new GameDisplaySettings(1.25f, true), "scale and visibility survive restarting both processes");
    launcher.Write(new GameDisplaySettings(0.75f, false, false));
    Check(!game.Read().MobileUi, "launcher opt-out reaches the game process");
    game.Write(game.Read() with { UiScale = 1.25f, ShowControls = true });
    Check(!launcher.Read().MobileUi, "in-game scale/controls save preserves original-server-UI opt-out");
    File.WriteAllText(Path.Combine(root, "android-display.json"), "{\"UiScale\":0.75,\"ShowControls\":false}");
    Check(reopened.Read().MobileUi, "existing preferences migrate to mobile UI without losing scale/visibility");
    File.WriteAllText(Path.Combine(root, "android-display.json"), "incomplete");
    Check(reopened.Read() == new GameDisplaySettings(), "damaged preferences recover to visible controls and automatic scale");
    launcher.Write(new GameDisplaySettings(float.NaN, false));
    Check(game.Read().UiScale == 0 && !game.Read().ShowControls, "invalid scale cannot create an unusable interface");
}
finally { Directory.Delete(root, true); }
Console.WriteLine($"PASS {checks} Android display checks");
