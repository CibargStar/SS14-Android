using System.Security.Cryptography;
using System.Text.Json;
using NSec.Cryptography;
using SS14.Launcher;
using SS14.Launcher.Models.EngineManager;

internal static class EngineInstallChecks
{
    public static async Task Run(IEngineManager manager, Action<bool, string> check)
    {
        using var key = Key.Create(SignatureAlgorithm.Ed25519);
        var payload = "signed engine fixture"u8.ToArray();
        var signature = Convert.ToHexString(SignatureAlgorithm.Ed25519.Sign(key, payload));
        Directory.CreateDirectory(Path.GetDirectoryName(LauncherPaths.PathPublicKey)!);
        await File.WriteAllBytesAsync(LauncherPaths.PathPublicKey, key.PublicKey.Export(KeyBlobFormat.PkixPublicKeyText));
        var hash = Convert.ToHexString(SHA256.HashData(payload));
        var rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        object Version(string digest, string sig) => new { insecure = false,
            platforms = new Dictionary<string, object> { [rid] = new { url = "memory://engine", sha256 = digest, sig } } };
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["0.0.1001"] = Version(hash, signature),
            ["0.0.1002"] = Version(new string('0', 64), signature),
            ["0.0.1003"] = Version(hash, new string('0', 128)),
            ["loop-a"] = new { redirect = "loop-b", platforms = new { } },
            ["loop-b"] = new { redirect = "loop-a", platforms = new { } }
        });
        var downloads = 0;
        LauncherMobile.OpenEngineManifest = _ => Task.FromResult<Stream>(new MemoryStream(manifest));
        LauncherMobile.OpenEnginePayload = (_, _) =>
        {
            downloads++;
            return Task.FromResult<Stream>(new MemoryStream(payload));
        };
        var first = await manager.DownloadEngineIfNecessary("0.0.1001");
        check(first.Changed && File.Exists(manager.GetEnginePath("0.0.1001")), "signed engine installed atomically");
        var second = await manager.DownloadEngineIfNecessary("0.0.1001");
        check(!second.Changed && downloads == 1, "installed engine reused without downloading again");
        foreach (var version in new[] { "0.0.1002", "0.0.1003" })
        {
            var rejected = false;
            try { await manager.DownloadEngineIfNecessary(version); }
            catch (CryptographicException) { rejected = true; }
            check(rejected && !File.Exists(Path.Combine(LauncherPaths.DirEngineInstallations, version + ".zip")),
                "invalid hash/signature rejected before engine installation: " + version);
        }
        check(!Directory.EnumerateFiles(LauncherPaths.DirEngineInstallations, "*.tmp").Any(),
            "failed download left no temporary package");
        var loopRejected = false;
        try { await manager.DownloadEngineIfNecessary("loop-a"); }
        catch (InvalidDataException) { loopRejected = true; }
        check(loopRejected, "redirect cycle rejected");
        var enginePath = manager.GetEnginePath("0.0.1001");
        var previous = Environment.GetEnvironmentVariable("SS14_DISABLE_SIGNING");
        try
        {
            Environment.SetEnvironmentVariable("SS14_DISABLE_SIGNING", "true");
            var result = SS14.Loader.MobileLoader.Run([enginePath, new string('0', 128), LauncherPaths.PathPublicKey],
                Path.GetDirectoryName(enginePath)!, (_, _) => { });
            check(result == 2, "Android loader enforces signatures even if desktop debug bypass is set");
        }
        finally { Environment.SetEnvironmentVariable("SS14_DISABLE_SIGNING", previous); }
        var updatedPayload = "signed Android platform repair"u8.ToArray();
        var updatedSignature = Convert.ToHexString(SignatureAlgorithm.Ed25519.Sign(key, updatedPayload));
        var updatedHash = Convert.ToHexString(SHA256.HashData(updatedPayload));
        byte[] UpdatedManifest(string digest) => JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        { ["0.0.1001"] = Version(digest, updatedSignature) });
        LauncherMobile.OpenEngineManifest = _ => Task.FromResult<Stream>(new MemoryStream(UpdatedManifest(updatedHash)));
        LauncherMobile.OpenEnginePayload = (_, _) => Task.FromResult<Stream>(new MemoryStream(updatedPayload));
        var repairedManager = new EngineManagerDynamic();
        var repaired = await repairedManager.DownloadEngineIfNecessary("0.0.1001");
        check(repaired.Changed && File.ReadAllBytes(enginePath).SequenceEqual(updatedPayload) &&
              repairedManager.GetEngineSignature("0.0.1001") == updatedSignature,
            "signed platform repair replaces the same upstream engine version");
        var nextPayload = "next signed Android platform repair"u8.ToArray();
        var nextSignature = Convert.ToHexString(SignatureAlgorithm.Ed25519.Sign(key, nextPayload));
        var nextHash = Convert.ToHexString(SHA256.HashData(nextPayload));
        var nextManifest = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        { ["0.0.1001"] = Version(nextHash, nextSignature) });
        // Advertise a new signed revision, but return the wrong download bytes.
        LauncherMobile.OpenEngineManifest = _ => Task.FromResult<Stream>(new MemoryStream(nextManifest));
        var repairRejected = false;
        try { await new EngineManagerDynamic().DownloadEngineIfNecessary("0.0.1001"); }
        catch (CryptographicException) { repairRejected = true; }
        check(repairRejected && File.ReadAllBytes(enginePath).SequenceEqual(updatedPayload) &&
              repairedManager.GetEngineSignature("0.0.1001") == updatedSignature,
            "corrupt platform repair preserves the working engine and stored signature");
        LauncherMobile.OpenEngineManifest = null;
        LauncherMobile.OpenEnginePayload = null;
    }
}
