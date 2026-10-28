using System.Text.Json;

namespace SS14.Launcher.Core;

public sealed class FavoritesStore(string path)
{
    public string[] Load() => File.Exists(path)
        ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? [] : [];

    public void Save(IEnumerable<string> addresses)
    {
        var normalized = addresses.Select(x => ServerAddress.Parse(x).AbsoluteUri)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, normalized);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
