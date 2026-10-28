using System.Net.Http.Json;
using System.Text.Json;

namespace SS14.Launcher.Core;

public sealed class HubClient(HttpClient http)
{
    public static readonly Uri[] OfficialHubs =
    [new("https://hub.spacestation14.com/"), new("https://hub.fallback.spacestation14.com/")];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ServerEntry[]> GetServersAsync(CancellationToken cancel)
    {
        var entries = await FromHub<ServerEntry[]>("api/servers", cancel);
        // Do not filter forks, languages, populations or tags out of the hub response.
        return entries.DistinctBy(x => x.Address, StringComparer.Ordinal).ToArray();
    }

    public async Task<ServerInfo> GetInfoAsync(Uri server, bool listedInHub, CancellationToken cancel)
    {
        if (listedInHub)
        {
            try { return await FromHub<ServerInfo>("api/servers/info?url=" + Uri.EscapeDataString(server.AbsoluteUri), cancel); }
            catch (Exception e) when (e is HttpRequestException or JsonException or InvalidDataException) { }
        }
        return await http.GetFromJsonAsync<ServerInfo>(ServerAddress.Api(server, "info"), Json, cancel)
            ?? throw new InvalidDataException("Сервер вернул пустой ответ.");
    }

    private async Task<T> FromHub<T>(string path, CancellationToken cancel)
    {
        var errors = new List<Exception>();
        foreach (var hub in OfficialHubs)
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                return await http.GetFromJsonAsync<T>(new Uri(hub, path), Json, cancel)
                    ?? throw new InvalidDataException("Хаб вернул пустой ответ.");
            }
            catch (Exception e) when (e is HttpRequestException or JsonException or InvalidDataException)
            { errors.Add(e); }
            catch (OperationCanceledException e) when (!cancel.IsCancellationRequested)
            { errors.Add(e); }
        }
        throw new HttpRequestException("Не удалось получить ответ от основного и резервного хаба.", new AggregateException(errors));
    }
}
