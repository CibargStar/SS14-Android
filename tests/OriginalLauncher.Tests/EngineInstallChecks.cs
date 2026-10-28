using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using NSec.Cryptography;
using Splat;
using SS14.Launcher;
using SS14.Launcher.Models;
using SS14.Launcher.Models.EngineManager;
using SS14.Launcher.Utility;

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
            ["obsolete-alias"] = new { insecure = true, redirect = "0.0.1001", platforms = new { } },
            ["revoked-target"] = new { insecure = true, platforms = new { } },
            ["unsafe-alias"] = new { insecure = true, redirect = "revoked-target", platforms = new { } },
            ["loop-a"] = new { redirect = "loop-b", platforms = new { } },
            ["loop-b"] = new { redirect = "loop-a", platforms = new { } },
            ["missing-target"] = new { redirect = "not-published", platforms = new { } },
            ["wrong-platform"] = new { insecure = false, platforms = new Dictionary<string, object>
                { [rid == "win-x64" ? "osx-arm64" : "win-x64"] = new { url = "memory://desktop", sha256 = hash, sig = signature } } }
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
        var redirected = await manager.DownloadEngineIfNecessary("obsolete-alias");
        check(redirected.Version == "0.0.1001" && !redirected.Changed && downloads == 1,
            "official redirect from a revoked old binary uses the signed safe target");
        var unsafeRedirectRejected = false;
        try { await manager.DownloadEngineIfNecessary("unsafe-alias"); }
        catch (UpdateException) { unsafeRedirectRejected = true; }
        check(unsafeRedirectRejected && downloads == 1, "redirect cannot make a revoked target executable");
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
        foreach (var unavailable in new[] { "294.0.0", "missing-target", "wrong-platform" })
        {
            var previousDownloads = downloads;
            AndroidEngineUnavailableException? failure = null;
            try { await manager.DownloadEngineIfNecessary(unavailable); }
            catch (AndroidEngineUnavailableException e) { failure = e; }
            check(failure?.Version == unavailable && downloads == previousDownloads,
                "missing Android engine reports requested version without downloading desktop code: " + unavailable);
        }
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
        var cfg = Locator.Current.GetRequiredService<SS14.Launcher.Models.Data.DataManager>();
        cfg.SetCVar(SS14.Launcher.Models.Data.CVars.AndroidEnginePatching, true);
        var originalPayload = "authentic original desktop engine fixture"u8.ToArray();
        var originalHash = Convert.ToHexString(SHA256.HashData(originalPayload));
        var originalSignature = Convert.ToHexString(SignatureAlgorithm.Ed25519.Sign(key, originalPayload));
        var patchedPayload = "authentic Android port fixture"u8.ToArray();
        var patchedHash = Convert.ToHexString(SHA256.HashData(patchedPayload));
        var patchedSignature = Convert.ToHexString(SignatureAlgorithm.Ed25519.Sign(key, patchedPayload));
        foreach (var (patchVersion, badSource, badDelta) in new[] {
                     ("0.0.1004", false, false), ("0.0.1005", true, false), ("0.0.1006", false, true) })
        {
            using var deltaOutput = new MemoryStream();
            using (var writer = new BinaryWriter(deltaOutput, Encoding.UTF8, true))
            {
                var header = JsonSerializer.SerializeToUtf8Bytes(new { format = 1, version = patchVersion,
                    sourceSha256 = originalHash, targetSha256 = patchedHash, targetLength = patchedPayload.Length });
                writer.Write("SS14DP01"u8);
                writer.Write(header.Length);
                writer.Write(header);
                writer.Write((byte)1);
                writer.Write((long)patchedPayload.Length);
                writer.Write(patchedPayload);
                writer.Write((byte)255);
            }
            var deltaBytes = deltaOutput.ToArray();
            var deltaHash = Convert.ToHexString(SHA256.HashData(deltaBytes));
            var deltaSignature = Convert.ToHexString(SignatureAlgorithm.Ed25519.Sign(key, deltaBytes));
            var patchInfo = new EnginePatchInfo("https://fixture/original", originalHash,
                badSource ? new string('0', 128) : originalSignature, "https://fixture/delta", deltaHash,
                badDelta ? new string('0', 128) : deltaSignature);
            var patchedManifest = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object> {
                [patchVersion] = new { insecure = false, platforms = new Dictionary<string, object> {
                    [rid] = new { url = "https://fixture/full", sha256 = patchedHash, sig = patchedSignature, patch = patchInfo } } } });
            LauncherMobile.OpenEngineManifest = _ => Task.FromResult<Stream>(new MemoryStream(patchedManifest));
            LauncherMobile.OpenEnginePayload = (url, _) => Task.FromResult<Stream>(new MemoryStream(url switch {
                "https://fixture/original" => originalPayload,
                "https://fixture/delta" => deltaBytes,
                _ => throw new Exception("Experimental patch unexpectedly downloaded the full Android package.") }));
            var patchManager = new EngineManagerDynamic();
            var rejected = false;
            try { await patchManager.DownloadEngineIfNecessary(patchVersion); }
            catch (CryptographicException) { rejected = true; }
            if (badSource || badDelta)
                check(rejected && !File.Exists(Path.Combine(LauncherPaths.DirEngineInstallations, patchVersion + ".zip")),
                    "patch installer rejects unauthenticated " + (badSource ? "original engine" : "Android patch"));
            else
                check(!rejected && File.ReadAllBytes(patchManager.GetEnginePath(patchVersion)).SequenceEqual(patchedPayload) &&
                      patchManager.GetEngineSignature(patchVersion) == patchedSignature,
                    "original signed engine + signed patch installs exact signed Android engine");
        }
        cfg.SetCVar(SS14.Launcher.Models.Data.CVars.AndroidEnginePatching, false);
        check(!Directory.EnumerateFiles(LauncherPaths.DirEngineInstallations, "*.tmp").Any(),
            "failed experimental patches leave no temporary engine installation");
        LauncherMobile.OpenEngineManifest = null;
        LauncherMobile.OpenEnginePayload = null;
    }
}
