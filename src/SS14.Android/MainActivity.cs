using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Avalonia;
using Avalonia.Android;
using SS14.Launcher;
using Splat;
using SS14.Launcher.Utility;
using System.IO.Compression;
using System.Text.Json;
using SS14.Launcher.Models;

[assembly: UsesPermission(Android.Manifest.Permission.Internet)]
[assembly: UsesPermission(Android.Manifest.Permission.AccessNetworkState)]
[assembly: UsesPermission(Android.Manifest.Permission.RequestInstallPackages)]
[assembly: Application(UsesCleartextTraffic = true, AllowBackup = false, LargeHeap = true, Label = "Space Station 14")]

namespace SS14.Android;

[Activity(Name = "io.ss14.android.MainActivity", Label = "Space Station 14",
    Theme = "@style/SS14LauncherTheme",
    MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode,
    ScreenOrientation = ScreenOrientation.SensorLandscape)]
[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataSchemes = new[] { "ss14", "ss14s" })]
public sealed class MainActivity : AvaloniaMainActivity
{
    private const int GameRequestCode = 1401;
    private static AndroidClientProcess? _gameProcess;
    private static string? _pendingRequest;
    private AndroidUpdates? _updates;
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        var root = FilesDir!.AbsolutePath;
        LauncherMobile.DataRoot = root;
        LauncherMobile.Exit = Finish;
        LauncherMobile.OpenLogs = () => RunOnUiThread(ShareLogs);
        LauncherMobile.StartGame = StartGame;
        var displaySettings = new GameDisplaySettingsStore(root);
        LauncherMobile.ReadGameDisplaySettings = displaySettings.Read;
        LauncherMobile.WriteGameDisplaySettings = displaySettings.Write;
        LauncherMobile.OpenUri = uri => RunOnUiThread(() =>
        {
            var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(uri));
            StartActivity(intent);
        });
        var install = Path.Combine(root, "install");
        Directory.CreateDirectory(install);
        using (var key = Assets!.Open("signing_key"))
        using (var destination = File.Create(Path.Combine(install, "signing_key")))
            key.CopyTo(destination);
        var androidKey = Path.Combine(install, "android_signing_key");
        using (var key = Assets!.Open("android_signing_key"))
        using (var destination = File.Create(androidKey)) key.CopyTo(destination);
        LauncherMobile.EnginePublicKeyPath = androidKey;
        _updates = new AndroidUpdates(this);
        _updates.Initialize();
        LauncherMobile.OpenEngineManifest = _updates.OpenEngineManifest;
        LauncherMobile.OpenEnginePayload = _updates.OpenEnginePayload;
        LauncherMobile.CheckLauncherUpdate = _updates.CheckLauncherUpdate;
        LauncherMobile.InstallLauncherUpdate = _updates.InstallLauncherUpdate;
        base.OnCreate(savedInstanceState);
        AndroidFullscreen.Apply(Window);
#if DEBUG
        LauncherMobile.Root?.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, (_, e) =>
        {
            var point = e.GetPosition(LauncherMobile.Root);
            global::Android.Util.Log.Debug("SS14.Input", $"Avalonia {e.Pointer.Type} {point} {e.Source?.GetType().Name}");
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
#endif
        HandleConnectIntent(Intent);
    }

#if DEBUG
    public override bool DispatchTouchEvent(MotionEvent? ev)
    {
        if (ev != null) global::Android.Util.Log.Debug("SS14.Input", $"Native {ev.Action} {ev.GetX()},{ev.GetY()}");
        return base.DispatchTouchEvent(ev);
    }
#endif

    protected override async void OnResume()
    {
        base.OnResume();
        AndroidFullscreen.Apply(Window);
        Window?.DecorView.Post(() => AndroidFullscreen.Apply(Window));
        LauncherMobile.RefreshGameDisplaySettings?.Invoke();
        try
        {
            if (_updates != null) await _updates.ResumePendingInstall();
            if (LauncherMobile.RefreshLauncherUpdate is { } refresh) await refresh();
        }
        catch (Exception e)
        {
            global::Android.Util.Log.Error("SS14.Update", e.ToString());
            global::Android.Widget.Toast.MakeText(this, e.Message, global::Android.Widget.ToastLength.Long)!.Show();
        }
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus) AndroidFullscreen.Apply(Window);
    }

    protected override void OnStop()
    {
        Locator.Current.GetService<SS14.Launcher.Models.Data.DataManager>()?.CommitConfig();
        base.OnStop();
    }

    private async Task<IClientProcess> StartGame(MobileGameLaunch launch)
    {
        if (_gameProcess is { HasExited: false })
            throw new InvalidOperationException("A game session is already running.");
        var process = new AndroidClientProcess();
        _gameProcess = process;
        var directory = Path.Combine(FilesDir!.AbsolutePath, "launches");
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
        _pendingRequest = path;
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(launch));
#pragma warning disable CS0618, CA1422 // Activity results also report native process crashes.
            StartActivityForResult(new Intent(this, typeof(GameActivity)).PutExtra("launch_request", path), GameRequestCode);
#pragma warning restore CS0618, CA1422
            return process;
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            _pendingRequest = null;
            process.Exited(1);
            throw;
        }
    }

#pragma warning disable CS0672, CS0618, CA1422
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode == GameRequestCode)
        {
            _gameProcess?.Exited(data?.GetIntExtra("exit_code", resultCode == Result.Ok ? 0 : 1) ?? (resultCode == Result.Ok ? 0 : 1));
            if (_pendingRequest != null && File.Exists(_pendingRequest)) File.Delete(_pendingRequest);
            _pendingRequest = null;
        }
        base.OnActivityResult(requestCode, resultCode, data);
    }
#pragma warning restore CS0672, CS0618, CA1422

    private async void ShareLogs()
    {
        try
        {
            var directory = Path.Combine(CacheDir!.AbsolutePath, "exports");
            Directory.CreateDirectory(directory);
            var archive = Path.Combine(directory, $"ss14-logs-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip");
            await Task.Run(() =>
            {
                using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
                var logs = Directory.EnumerateFiles(LauncherPaths.DirLogs)
                    .Select(path => (Path: path, Entry: "launcher/" + Path.GetFileName(path)))
                    .Concat(new[] { "client.log", "crash.log" }
                        .Select(name => (Path: Path.Combine(FilesDir!.AbsolutePath, "client", name), Entry: "client/" + name))
                        .Where(file => File.Exists(file.Path)));
                foreach (var (path, entry) in logs)
                {
                    using var input = new FileStream(path, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    using var output = zip.CreateEntry(entry, CompressionLevel.Fastest).Open();
                    input.CopyTo(output);
                }
            });
            var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(this, "io.ss14.android.files", new Java.IO.File(archive));
            var share = new Intent(Intent.ActionSend);
            share.SetType("application/zip");
            share.PutExtra(Intent.ExtraStream, uri);
            share.AddFlags(ActivityFlags.GrantReadUriPermission);
            StartActivity(Intent.CreateChooser(share, "SS14 logs"));
        }
        catch (Exception e)
        {
            global::Android.Widget.Toast.MakeText(this, e.Message, global::Android.Widget.ToastLength.Long)!.Show();
        }
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleConnectIntent(intent);
    }

    private static void HandleConnectIntent(Intent? intent)
    {
        if (intent?.DataString is not { } address || !UriHelper.TryParseSs14Uri(address, out var uri)) return;
        var commands = Locator.Current.GetRequiredService<LauncherCommands>();
        var reason = intent.GetStringExtra("redial_reason");
        _ = commands.QueueCommand(reason == null ? LauncherCommands.BlankReasonCommand :
            "R" + Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(reason))).AsTask();
        _ = commands.QueueCommand(LauncherCommands.ConstructConnectCommand(uri)).AsTask();
    }

#pragma warning disable CS0672, CS0618, CA1422 // Required legacy back callback for API 26+.
    public override void OnBackPressed()
    {
        if (!LauncherMobile.CloseTopDialog()) base.OnBackPressed();
    }
#pragma warning restore CS0672, CS0618, CA1422

    protected override AppBuilder CreateAppBuilder() => Program.BuildForAndroid().UseAndroid();
}
