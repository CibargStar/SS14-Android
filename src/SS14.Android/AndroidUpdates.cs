using Android.App;
using Android.Content;
using Android.Content.PM;
using SS14.Launcher.Utility;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SS14.Android;

internal sealed record UpdateSources(string? EngineManifest, string? ApkManifest,
    Dictionary<string, string>? ApkManifests = null);

// Android-specific transport and package installation. The original launcher owns
// the update UI and engine/content workflow; no parallel hub or login is introduced.
internal sealed class AndroidUpdates(Activity activity)
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly SemaphoreSlim _install = new(1);
    private UpdateSources _sources = new(null, null);
    private LauncherApkRelease? _release;
    private LauncherApkDownload _apks = default!;
    private readonly SemaphoreSlim _check = new(1);
    private long _nextCheck;
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
        if (_sources.EngineManifest == null) throw new InvalidDataException("Missing online engine catalog.");
        RequireHttps(_sources.EngineManifest);
        _apks = new LauncherApkDownload(_http, Path.Combine(activity.CacheDir!.AbsolutePath, "updates"), VerifyApkIdentity);
        if (_sources.ApkManifest != null) RequireHttps(_sources.ApkManifest);
    }

    public async Task<Stream> OpenEngineManifest(CancellationToken cancel)
    {
        var catalog = await UpdateCatalogClient.GetJson<JsonObject>(_http, _sources.EngineManifest!, cancel);
        return new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(catalog));
    }

    public Task<Stream> OpenEnginePayload(string address, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        var uri = new Uri(address, UriKind.Absolute);
        RequireHttps(address);
        return _http.GetStreamAsync(uri, cancel);
    }

    public async Task<bool> CheckLauncherUpdate()
    {
        await _check.WaitAsync();
        try
        {
            if (_sources.ApkManifest == null) return false;
            if (Environment.TickCount64 < _nextCheck) return _release != null;
            var release = await UpdateCatalogClient.GetJson<LauncherApkRelease>(_http, _sources.ApkManifest);
            _nextCheck = Environment.TickCount64 + 60_000;
            if (!release.IsApplicable(activity.PackageName!, Version(InstalledPackage()), (int)global::Android.OS.Build.VERSION.SdkInt))
            {
                _release = null;
                return false;
            }
            if (_release != release) { _release = release; _download = null; }
            StartDownload(release);
            return true;
        }
        finally { _check.Release(); }
    }

    private void StartDownload(LauncherApkRelease release)
    {
        if (_download != null && !_download.IsFaulted && !_download.IsCanceled) return;
        _download = Task.Run(() => _apks.Download(release));
        _ = _download.ContinueWith(task => global::Android.Util.Log.Warn("SS14.Update", task.Exception!.ToString()),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    public async Task InstallLauncherUpdate()
    {
        if (!await _install.WaitAsync(0)) return;
        try
        {
            var release = _release;
            if (release == null) return;
            StartDownload(release);
            var path = await _download!;
            if (_release != release) return;
            if (!activity.PackageManager!.CanRequestPackageInstalls())
            {
                _waitingForPermission = true;
                activity.StartActivity(new Intent(global::Android.Provider.Settings.ActionManageUnknownAppSources,
                    global::Android.Net.Uri.Parse("package:" + activity.PackageName)));
                return;
            }
            _waitingForPermission = false;
            // Verify again immediately before handing the file to the OS installer.
            await _apks.Verify(path, release);
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

    private Task VerifyApkIdentity(string path, LauncherApkRelease release)
    {
        var archive = activity.PackageManager!.GetPackageArchiveInfo(path, SignatureFlags())
            ?? throw new InvalidDataException("Invalid APK archive.");
        if (archive.PackageName != activity.PackageName || Version(archive) != release.VersionCode)
            throw new InvalidDataException("APK identity or version mismatch.");
        var installedSigners = Signers(InstalledPackage());
        if (installedSigners.Count == 0 || !installedSigners.SetEquals(Signers(archive)))
            throw new CryptographicException("APK signing certificate does not match the installed application.");
        return Task.CompletedTask;
    }
#pragma warning restore CS0618, CA1422

    private static void RequireHttps(string value)
    {
        var uri = new Uri(value, UriKind.Absolute);
        if (uri.Scheme != "https" || uri.UserInfo.Length != 0)
            throw new InvalidDataException("Update sources require HTTPS.");
    }
}
