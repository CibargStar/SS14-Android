using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using SS14.Launcher.Utility;

internal static class LauncherUpdateChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "ss14-apk-update-" + Guid.NewGuid());
        var payload = "signed APK fixture bytes"u8.ToArray();
        var release = new LauncherApkRelease("io.ss14.android", 42, "https://fixture/update.apk", Convert.ToHexString(SHA256.HashData(payload)), 26);
        var handler = new UpdateHandler(payload, release);
        using var http = new HttpClient(handler);
        var identityAllowed = true;
        var identities = 0;
        var downloader = new LauncherApkDownload(http, root, (_, _) =>
        {
            identities++;
            return identityAllowed ? Task.CompletedTask : Task.FromException(new CryptographicException("Different APK signer."));
        });
        try
        {
            check(release.IsApplicable("io.ss14.android", 41, 26), "new compatible APK is offered");
            check(!release.IsApplicable("io.ss14.android", 42, 26) && !release.IsApplicable("io.ss14.android", 43, 26),
                "same or older APK is not offered");
            check(!release.IsApplicable("io.ss14.android", 41, 25), "incompatible Android SDK does not trigger APK download");
            foreach (var invalid in new[] { release with { ApplicationId = "other.app" }, release with { Url = "http://fixture/update.apk" },
                         release with { Url = "https://user@fixture/update.apk" }, release with { Sha256 = "broken" } })
            {
                var rejected = false;
                try { invalid.IsApplicable("io.ss14.android", 41, 26); } catch (Exception e) when (e is InvalidDataException or FormatException) { rejected = true; }
                check(rejected, "invalid APK identity, transport or digest is rejected");
            }
            var fetched = await UpdateCatalogClient.GetJson<LauncherApkRelease>(http, "https://fixture/catalog");
            check(fetched == release && handler.FreshCatalog, "update metadata uses HTTPS and requests fresh catalog bytes");
            var path = await downloader.Download(release);
            check(File.ReadAllBytes(path).SequenceEqual(payload) && identities == 1 && handler.Downloads == 1,
                "APK downloads and passes digest plus platform identity verification before caching");
            var reused = await downloader.Download(release);
            check(reused == path && handler.Downloads == 1 && identities == 2, "prepared APK is reverified and reused without another download");
            await File.WriteAllBytesAsync(path, "damaged APK"u8.ToArray());
            var damagedRejected = false;
            try { await downloader.Verify(path, release); } catch (CryptographicException) { damagedRejected = true; }
            check(damagedRejected, "changed cached APK is rejected before installation");
            await downloader.Download(release);
            check(File.ReadAllBytes(path).SequenceEqual(payload) && handler.Downloads == 2, "damaged cached APK is downloaded again");
            handler.Payload = "wrong download bytes"u8.ToArray();
            var badHashRejected = false;
            try { await downloader.Download(release with { VersionCode = 43 }); } catch (CryptographicException) { badHashRejected = true; }
            check(badHashRejected && Directory.GetFiles(root, "ss14-43-*.apk").Length == 0, "bad APK digest never reaches the installer cache");
            handler.Payload = payload;
            identityAllowed = false;
            var signerRejected = false;
            try { await downloader.Download(release with { VersionCode = 44 }); } catch (CryptographicException) { signerRejected = true; }
            check(signerRejected && Directory.GetFiles(root, "ss14-44-*.apk").Length == 0, "platform identity rejection leaves no installable APK");
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            var cancellation = false;
            try { await downloader.Download(release with { VersionCode = 45 }, canceled.Token); } catch (OperationCanceledException) { cancellation = true; }
            check(cancellation && Directory.GetFiles(root, "*.tmp").Length == 0, "canceled APK download removes temporary files");
            check(File.ReadAllBytes(path).SequenceEqual(payload), "failed later updates preserve the verified previous APK");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class UpdateHandler(byte[] payload, LauncherApkRelease release) : HttpMessageHandler
    {
        public byte[] Payload = payload;
        public int Downloads;
        public bool FreshCatalog;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            if (request.RequestUri!.AbsolutePath == "/catalog")
            {
                FreshCatalog = request.Headers.CacheControl is { NoCache: true, NoStore: true };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(release)) });
            }
            Downloads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) });
        }
    }
}
