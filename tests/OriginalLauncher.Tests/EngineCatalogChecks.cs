using System.Text.Json.Nodes;
using SS14.Launcher.Utility;

internal static class EngineCatalogChecks
{
    public static void Run(Action<bool, string> check)
    {
        JsonObject Catalog(string url, string hash = "ABCD", string signature = "1234", bool revoked = false) =>
            new() { ["294.0.0"] = new JsonObject { ["insecure"] = revoked,
                ["platforms"] = new JsonObject { ["android-arm64"] = new JsonObject {
                    ["url"] = url, ["sha256"] = hash, ["sig"] = signature } } } };
        string? Url(JsonObject catalog) => catalog["294.0.0"]?["platforms"]?["android-arm64"]?["url"]?.GetValue<string>();
        var bundled = Catalog("asset://engines/original.zip");
        var remote = Catalog("https://release/engine.zip", hash: "abcd");
        check(Url(EngineCatalogMerge.Merge(bundled, remote)) == "asset://engines/original.zip",
            "exact bundled signed engine is used without downloading its online copy");
        check(Url(remote) == "https://release/engine.zip" && Url(bundled) == "asset://engines/original.zip",
            "engine catalog merging does not mutate source catalogs");
        check(Url(EngineCatalogMerge.Merge(bundled, Catalog("https://release/repair.zip", "DIFFERENT"))) == "https://release/repair.zip",
            "online engine repair replaces the bundled package URL");
        check(EngineCatalogMerge.Merge(bundled, Catalog("https://release/engine.zip", revoked: true))["294.0.0"]?["insecure"]?.GetValue<bool>() == true,
            "online engine revocation survives bundled payload reuse");
        var redirected = new JsonObject { ["294.0.0"] = new JsonObject { ["redirect"] = "294.0.1", ["platforms"] = new JsonObject() } };
        check(EngineCatalogMerge.Merge(bundled, redirected)["294.0.0"]?["redirect"]?.GetValue<string>() == "294.0.1",
            "online authorized redirect replaces bundled engine metadata");
    }
}
