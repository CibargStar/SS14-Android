using Android.App;
using Android.Content;
using Android.Content.PM;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SS14.Android;

internal sealed record UpdateSources(string? EngineManifest, string? ApkManifest,
    Dictionary<string, string>? ApkManifests = null);
internal sealed record ApkRelease(string ApplicationId, long VersionCode, string Url, string Sha256, int MinSdk);

// Android-specific transport and package installation. The original launcher owns
// the update UI and engine/content workflow; no parallel hub or login is introduced.
internal sealed class AndroidUpdates(Activity activity)
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly SemaphoreSlim _install = new(1);
    private UpdateSources _sources = new(null, null);
    private ApkRelease? _release;
    private Task<string>? _download;
    private bool _waitingForPermission;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Initialize()
    {
        using var input = activity.Assets!.Open("update-sources.json");
        _sources = JsonSerializer.Deserialize<UpdateSources>(input, Json) ?? new(null, null);
        var rid = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => "android-arm64",
            System.Runtime.InteropServices.Architecture.X64 => "android-x64",
            _ => "unsupported"
        };
        if (_sources.ApkManifests?.TryGetValue(rid, out var apkManifest) == true)
            _sources = _sources with { ApkManifest = apkManifest };
        if (_sources.EngineManifest != null) RequireHttps(_sources.EngineManifest);
        if (_sources.ApkManifest != null) RequireHttps(_sources.ApkManifest);
    }

    public async Task<Stream> OpenEngineManifest(CancellationToken cancel)
    {
        using var bundled = activity.Assets!.Open("engines/manifest.json");
        var merged = await JsonNode.ParseAsync(bundled, cancellationToken: cancel) as JsonObject
            ?? throw new InvalidDataException("Invalid bundled engine catalog.");
        if (_sources.EngineManifest != null)
        {
            // A configured online catalog must be consulted: silently using a stale
            // catalog could ignore an engine revocation or mandatory host update.
            var remote = await _http.GetFromJsonAsync<JsonObject>(_sources.EngineManifest, cancel)
                ?? throw new InvalidDataException("Empty engine catalog.");
            merged = SS14.Launcher.Utility.EngineCatalogMerge.Merge(merged, remote);
        }
        return new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(merged));
    }

    public Task<Stream> OpenEnginePayload(string address, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        var uri = new Uri(address, UriKind.Absolute);
        if (uri.Scheme == "asset" && uri.Host == "engines" &&
            !uri.AbsolutePath.TrimStart('/').Contains('/') && !uri.AbsolutePath.Contains(".."))
            return Task.FromResult(activity.Assets!.Open("engines/" + uri.AbsolutePath.TrimStart('/')));
        RequireHttps(address);
        return _http.GetStreamAsync(uri, cancel);
    }

    public async Task<bool> CheckLauncherUpdate()
    {
        if (_sources.ApkManifest == null) return false;
        var release = await _http.GetFromJsonAsync<ApkRelease>(_sources.ApkManifest, Json)
            ?? throw new InvalidDataException("Empty APK release manifest.");
        if (release.ApplicationId != activity.PackageName) throw new InvalidDataException("APK application ID mismatch.");
        RequireHttps(release.Url);
        if (Convert.FromHexString(release.Sha256).Length != 32) throw new InvalidDataException("Invalid APK digest.");
        if (release.MinSdk > (int)global::Android.OS.Build.VERSION.SdkInt) return false;
        var installed = InstalledPackage();
        if (release.VersionCode <= Version(installed)) return false;
        if (_release != release)
        {
            _release = release;
            _download = null;
        }
        return true;
    }

    public async Task InstallLauncherUpdate()
    {
        if (!await _install.WaitAsync(0)) return;
        try
        {
            if (_release == null) return;
            if (_download == null || _download.IsFaulted || _download.IsCanceled)
                _download = DownloadApk(_release);
            var path = await _download;
            if (!activity.PackageManager!.CanRequestPackageInstalls())
            {
                _waitingForPermission = true;
                activity.StartActivity(new Intent(global::Android.Provider.Settings.ActionManageUnknownAppSources,
                    global::Android.Net.Uri.Parse("package:" + activity.PackageName)));
                return;
            }
            _waitingForPermission = false;
            // Verify again immediately before handing the file to the OS installer.
            await VerifyApk(path, _release);
            var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(activity, "io.ss14.android.files", new Java.IO.File(path));
            var intent = new Intent(Intent.ActionView);
            intent.SetDataAndType(uri, "application/vnd.android.package-archive");
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);
            activity.StartActivity(intent);
        }
        finally { _install.Release(); }
    }

    public async Task ResumePendingInstall()
    {
        if (_waitingForPermission && activity.PackageManager!.CanRequestPackageInstalls())
            await InstallLauncherUpdate();
    }

    private async Task<string> DownloadApk(ApkRelease release)
    {
        var directory = Path.Combine(activity.CacheDir!.AbsolutePath, "updates");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ss14-" + release.VersionCode + "-" + release.Sha256.ToLowerInvariant() + ".apk");
        if (File.Exists(path))
        {
            try { await VerifyApk(path, release); return path; }
            catch (InvalidDataException) { File.Delete(path); }
            catch (CryptographicException) { File.Delete(path); }
        }
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            await using (var input = await _http.GetStreamAsync(release.Url, timeout.Token))
            await using (var output = File.Create(temporary))
            {
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    if (output.Length + read > 512L * 1024 * 1024) throw new InvalidDataException("APK exceeds the download limit.");
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                }
            }
            await VerifyApk(temporary, release);
            File.Move(temporary, path, overwrite: true);
            return path;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

#pragma warning disable CS0618, CA1422 // Keep API 26/27 package signature support.
    private PackageInfo InstalledPackage() => activity.PackageManager!.GetPackageInfo(activity.PackageName!, SignatureFlags())
        ?? throw new InvalidOperationException("Installed package metadata is unavailable.");
    private static PackageInfoFlags SignatureFlags() => OperatingSystem.IsAndroidVersionAtLeast(28)
        ? PackageInfoFlags.SigningCertificates : PackageInfoFlags.Signatures;
    private static long Version(PackageInfo package) => OperatingSystem.IsAndroidVersionAtLeast(28)
        ? package.LongVersionCode : package.VersionCode;
    private static HashSet<string> Signers(PackageInfo package)
    {
        var signatures = OperatingSystem.IsAndroidVersionAtLeast(28)
            ? package.SigningInfo?.GetApkContentsSigners() : package.Signatures;
        return (signatures ?? []).Select(s => Convert.ToHexString(SHA256.HashData(s.ToByteArray()!)))
            .ToHashSet(StringComparer.Ordinal);
    }

    private async Task VerifyApk(string path, ApkRelease release)
    {
        await using (var stream = File.OpenRead(path))
        {
            var digest = await SHA256.HashDataAsync(stream);
            if (!CryptographicOperations.FixedTimeEquals(digest, Convert.FromHexString(release.Sha256)))
                throw new CryptographicException("APK hash verification failed.");
        }
        var archive = activity.PackageManager!.GetPackageArchiveInfo(path, SignatureFlags())
            ?? throw new InvalidDataException("Invalid APK archive.");
        if (archive.PackageName != activity.PackageName || Version(archive) != release.VersionCode)
            throw new InvalidDataException("APK identity or version mismatch.");
        var installedSigners = Signers(InstalledPackage());
        if (installedSigners.Count == 0 || !installedSigners.SetEquals(Signers(archive)))
            throw new CryptographicException("APK signing certificate does not match the installed application.");
    }
#pragma warning restore CS0618, CA1422

    private static void RequireHttps(string value)
    {
        var uri = new Uri(value, UriKind.Absolute);
        if (uri.Scheme != "https" || uri.UserInfo.Length != 0)
            throw new InvalidDataException("Update sources require HTTPS.");
    }
}
