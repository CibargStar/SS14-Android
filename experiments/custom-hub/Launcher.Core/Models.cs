using System.Text.Json.Serialization;

namespace SS14.Launcher.Core;

public sealed record ServerEntry(string Address, ServerStatus? StatusData);
public sealed record ServerStatus(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("players")] int Players,
    [property: JsonPropertyName("soft_max_players")] int Capacity,
    [property: JsonPropertyName("tags")] string[]? Tags);
public sealed record ServerInfo(
    [property: JsonPropertyName("connect_address")] string? ConnectAddress,
    [property: JsonPropertyName("build")] ServerBuild? Build,
    [property: JsonPropertyName("desc")] string? Description);
public sealed record ServerBuild(
    [property: JsonPropertyName("engine_version")] string? EngineVersion,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("fork_id")] string? ForkId,
    [property: JsonPropertyName("acz")] bool Acz,
    [property: JsonPropertyName("download_url")] string? DownloadUrl,
    [property: JsonPropertyName("manifest_url")] string? ManifestUrl,
    [property: JsonPropertyName("manifest_download_url")] string? ManifestDownloadUrl,
    [property: JsonPropertyName("manifest_hash")] string? ManifestHash,
    [property: JsonPropertyName("hash")] string? Hash);

public sealed record ContentRequest(Uri Server, string EngineVersion, string ContentVersion, string ForkId,
    bool Acz, Uri Download, Uri? Manifest, Uri? ManifestDownload, string? ManifestHash, string? Hash)
{
    // Preserve the exact identity. A server-supplied version is not proof of engine compatibility.
    public static ContentRequest Create(Uri server, ServerInfo info)
    {
        var build = info.Build ?? throw new InvalidDataException("Сервер не сообщил данные клиентской сборки.");
        if (string.IsNullOrWhiteSpace(build.EngineVersion) || string.IsNullOrWhiteSpace(build.Version) ||
            string.IsNullOrWhiteSpace(build.ForkId))
            throw new InvalidDataException("Сервер сообщил неполные данные клиентской сборки.");
        var acz = build.Acz;
        return new ContentRequest(server, build.EngineVersion, build.Version, build.ForkId, acz,
            HttpUri(build.DownloadUrl, ServerAddress.Api(server, "client.zip")),
            acz ? HttpUri(build.ManifestUrl, ServerAddress.Api(server, "manifest.txt")) : null,
            acz ? HttpUri(build.ManifestDownloadUrl, ServerAddress.Api(server, "download")) : null,
            build.ManifestHash, build.Hash);
    }

    private static Uri HttpUri(string? value, Uri fallback)
    {
        var uri = string.IsNullOrWhiteSpace(value) ? fallback : new Uri(value, UriKind.Absolute);
        if (uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0)
            throw new InvalidDataException("Сервер сообщил неподдерживаемый адрес загрузки.");
        return uri;
    }
}
