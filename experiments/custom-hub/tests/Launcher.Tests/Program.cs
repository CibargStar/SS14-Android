using System.Net;
using System.Text;
using SS14.Launcher.Core;

var checks = 0;
Assert(ServerAddress.Api(ServerAddress.Parse("example.org"), "info").AbsoluteUri == "http://example.org:1212/info", "default port");
Assert(ServerAddress.Api(ServerAddress.Parse("ss14s://example.org/base"), "info").AbsoluteUri == "https://example.org/base/info", "HTTPS path");
Assert(ServerAddress.Api(ServerAddress.Parse("ss14://[::1]:2345/base/"), "info").AbsoluteUri == "http://[::1]:2345/base/info", "IPv6 and explicit port");
foreach (var bad in new[] { "ftp://example.org", "ss14://user:password@example.org", "ss14://example.org?foo=bar" })
{
    try { ServerAddress.Parse(bad); throw new Exception("Accepted invalid address"); }
    catch (FormatException) { checks++; }
}
var requests = new List<Uri>();
using var client = new HttpClient(new Handler(request =>
{
    requests.Add(request.RequestUri!);
    if (request.RequestUri!.Host == HubClient.OfficialHubs[0].Host)
        return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
    return Json("""[{"address":"ss14://example.org:1212/","statusData":{"name":"Station","players":3,"soft_max_players":80,"tags":["lang:ru"]}}]""");
}));
var hub = new HubClient(client);
var servers = await hub.GetServersAsync(CancellationToken.None);
Assert(requests.Count == 2 && requests[1].Host == HubClient.OfficialHubs[1].Host, "fallback used");
Assert(servers.Length == 1 && servers[0].StatusData?.Players == 3, "official hub JSON contract");
using var timeoutClient = new HttpClient(new Handler(request =>
    request.RequestUri!.Host == HubClient.OfficialHubs[0].Host
        ? throw new TaskCanceledException("simulated request timeout") : Json("[]")));
Assert((await new HubClient(timeoutClient).GetServersAsync(CancellationToken.None)).Length == 0,
    "timeout falls back without treating it as user cancellation");
using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
try { await hub.GetServersAsync(cancelled.Token); throw new Exception("Cancellation ignored"); }
catch (OperationCanceledException) { Assert(requests.Count == 2, "cancel does not attempt fallback"); }
using var directClient = new HttpClient(new Handler(request =>
{
    Assert(request.RequestUri!.AbsoluteUri == "http://example.org:1212/prefix/info", "direct server info URL");
    return Json("""{"build":{"engine_version":"292.0.0","version":"content-abc","fork_id":"any-fork","acz":true}}""");
}));
var address = ServerAddress.Parse("ss14://example.org/prefix/");
var info = await new HubClient(directClient).GetInfoAsync(address, false, CancellationToken.None);
var content = ContentRequest.Create(address, info);
Assert(content.ForkId == "any-fork" && content.EngineVersion == "292.0.0" && content.ContentVersion == "content-abc", "exact versions retained");
Assert(content.Manifest?.AbsoluteUri == "http://example.org:1212/prefix/manifest.txt" && content.ManifestDownload?.AbsoluteUri == "http://example.org:1212/prefix/download", "ACZ inferred URLs");
try { ContentRequest.Create(address, info with { Build = null }); throw new Exception("Missing build accepted"); }
catch (InvalidDataException) { checks++; }
var directory = Path.Combine(Path.GetTempPath(), "ss14-favorites-" + Guid.NewGuid());
try
{
    var store = new FavoritesStore(Path.Combine(directory, "favorites.json"));
    store.Save(["example.org", "ss14://example.org/", "ss14s://another.org/base"]);
    Assert(store.Load().Length == 2, "favorites normalized and persisted");
    store.Save(["ss14s://example.org/A/", "ss14s://example.org/a/"]);
    Assert(store.Load().Length == 2, "distinct case-sensitive server paths retained");
    Assert(!Directory.GetFiles(directory, "*.tmp").Any(), "no leftover temporary favorites file");
}
finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
Console.WriteLine($"PASS: {checks} launcher checks");

void Assert(bool condition, string label)
{ if (!condition) throw new Exception("FAIL: " + label); checks++; }
static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
{ Content = new StringContent(value, Encoding.UTF8, "application/json") };

sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
    { cancel.ThrowIfCancellationRequested(); return Task.FromResult(handle(request)); }
}
