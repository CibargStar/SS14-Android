using System.Text.Json;
using SS14.Launcher.Models;

namespace SS14.Android;

// Private, atomic shared preferences for launcher and :game. Read afresh instead
// of relying on Android's per-process SharedPreferences cache.
internal sealed class GameDisplaySettingsStore(string root)
{
    private readonly string _path = Path.Combine(root, "android-display.json");
    public GameDisplaySettings Read()
    {
        try
        {
            return (JsonSerializer.Deserialize<GameDisplaySettings>(File.ReadAllText(_path))
                    ?? new GameDisplaySettings()).Normalize();
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return new GameDisplaySettings();
        }
    }

    public void Write(GameDisplaySettings settings)
    {
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings.Normalize()));
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
