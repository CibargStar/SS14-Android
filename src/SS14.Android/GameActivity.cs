extern alias ppysdl;

using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;
using System.Text.Json;
using SS14.Launcher.Models;
using SDLActivity = ppysdl::Org.Libsdl.App.SDLActivity;
using Environment = System.Environment;

namespace SS14.Android;

[Activity(Name = "io.ss14.android.GameActivity", Label = "Space Station 14",
    Exported = false, Process = ":game", Theme = "@android:style/Theme.NoTitleBar.Fullscreen",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden |
                           ConfigChanges.Keyboard | ConfigChanges.UiMode,
    ScreenOrientation = ScreenOrientation.SensorLandscape)]
public sealed class GameActivity : SDLActivity
{
    private string _requestPath = "";
    private string _nativeDirectory = "";
    private string _gameDirectory = "";
    private readonly AndroidTouchContacts _touchContacts = new();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        _requestPath = Intent?.GetStringExtra("launch_request") ?? throw new InvalidOperationException("Missing launch request.");
        var allowed = Path.Combine(FilesDir!.AbsolutePath, "launches") + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(_requestPath).StartsWith(allowed, StringComparison.Ordinal))
            throw new InvalidOperationException("Launch request is outside private app storage.");
        _nativeDirectory = ApplicationInfo!.NativeLibraryDir!;
        _gameDirectory = Path.Combine(FilesDir.AbsolutePath, "client");
        Directory.CreateDirectory(_gameDirectory);
        AppDomain.CurrentDomain.SetData("SS14_ANDROID_ACTIVE_TOUCHES", (Func<long[]>)_touchContacts.Snapshot);
        AppDomain.CurrentDomain.SetData("SS14_ANDROID_METRICS", (Func<double[]>)ReadMetrics);
        base.OnCreate(savedInstanceState);
        ApplyFullscreen();
    }

    public override bool DispatchTouchEvent(MotionEvent? ev)
    {
        if (ev != null && (ev.PointerCount == 0 || ev.GetToolType(0) != MotionEventToolType.Mouse))
        {
            switch (ev.ActionMasked)
            {
                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                case MotionEventActions.Outside:
                    _touchContacts.Clear();
                    break;
                case MotionEventActions.Down:
                case MotionEventActions.PointerDown:
                case MotionEventActions.Move:
                case MotionEventActions.PointerUp:
                    var pointers = new int[ev.PointerCount];
                    for (var i = 0; i < pointers.Length; i++) pointers[i] = ev.GetPointerId(i);
                    _touchContacts.Update(pointers, ev.ActionMasked == MotionEventActions.PointerUp ? ev.ActionIndex : -1);
                    break;
            }
        }
        return base.DispatchTouchEvent(ev);
    }

    protected override void OnPause()
    {
        _touchContacts.Clear();
        base.OnPause();
    }

    private double[] ReadMetrics()
    {
        using var memory = new ActivityManager.MemoryInfo();
        ((ActivityManager)GetSystemService(ActivityService)!).GetMemoryInfo(memory);
        // CPU milliseconds, process PSS bytes, available/total system RAM, cores.
        return [global::Android.OS.Process.ElapsedCpuTime, global::Android.OS.Debug.Pss * 1024d,
            memory.AvailMem, memory.TotalMem, Environment.ProcessorCount];
    }

    private void ApplyFullscreen() => AndroidFullscreen.Apply(Window);

    protected override void OnResume()
    {
        base.OnResume();
        ApplyFullscreen();
        // SDL also updates window flags while resuming; apply after its work.
        Window?.DecorView.Post(ApplyFullscreen);
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        if (!hasFocus) _touchContacts.Clear();
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus)
        {
            ApplyFullscreen();
            Window?.DecorView.Post(ApplyFullscreen);
        }
    }

    public override void OnConfigurationChanged(global::Android.Content.Res.Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        ApplyFullscreen();
    }

    protected override string[] GetLibraries() => ["SDL3", "sodium", "zstd", "freetype", "openal"];

    protected override void Main()
    {
        var exitCode = 1;
        try
        {
            var request = JsonSerializer.Deserialize<MobileGameLaunch>(File.ReadAllText(_requestPath))
                ?? throw new InvalidDataException("Empty launch request.");
            File.Delete(_requestPath);
            foreach (var (key, value) in request.Environment)
                Environment.SetEnvironmentVariable(key, value);
            Environment.SetEnvironmentVariable("ROBUST_ANDROID_DATA", _gameDirectory);
            var displayStore = new GameDisplaySettingsStore(FilesDir!.AbsolutePath);
            var display = displayStore.Read();
            Environment.SetEnvironmentVariable("ROBUST_ANDROID_MOBILE_UI", display.MobileUi ? "true" : "false");
            Environment.SetEnvironmentVariable("ROBUST_ANDROID_UI_SCALE",
                display.UiScale.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Environment.SetEnvironmentVariable("ROBUST_ANDROID_SHOW_CONTROLS", display.ShowControls ? "true" : "false");
            AppDomain.CurrentDomain.SetData("SS14_ANDROID_DISPLAY_SAVE", (Action<float, bool>)((scale, show) =>
                displayStore.Write(displayStore.Read() with { UiScale = scale, ShowControls = show })));
            var verifierDirectory = Path.Combine(FilesDir!.AbsolutePath, "verification");
            Directory.CreateDirectory(verifierDirectory);
            var metadataFiles = Assets!.List("verification") ?? [];
            if (!metadataFiles.Contains("System.Runtime.dll") || !metadataFiles.Contains("System.Private.CoreLib.dll"))
                throw new InvalidDataException("The APK is missing its runtime verifier metadata.");
            foreach (var name in metadataFiles)
            {
                if (Path.GetFileName(name) != name || !name.EndsWith(".dll", StringComparison.Ordinal))
                    throw new InvalidDataException("Invalid runtime metadata asset.");
                using var source = Assets.Open("verification/" + name);
                using var target = File.Create(Path.Combine(verifierDirectory, name));
                source.CopyTo(target);
            }
            Environment.SetEnvironmentVariable("ROBUST_ANDROID_BCL", verifierDirectory);
            Directory.SetCurrentDirectory(_gameDirectory);
            using var log = new StreamWriter(Path.Combine(_gameDirectory, "client.log"), append: false) { AutoFlush = true };
            Console.SetOut(log);
            Console.SetError(log);
            exitCode = SS14.Loader.MobileLoader.Run(request.Arguments, _nativeDirectory, (address, reason) =>
            {
                RunOnUiThread(() =>
                {
                    var redial = new Intent(this, typeof(MainActivity));
                    redial.SetAction(Intent.ActionView);
                    redial.SetData(global::Android.Net.Uri.Parse(address));
                    redial.PutExtra("redial_reason", reason);
                    redial.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
                    StartActivity(redial);
                });
            });
        }
        catch (Exception e)
        {
            global::Android.Util.Log.Error("SS14.Game", e.ToString());
            File.AppendAllText(Path.Combine(_gameDirectory, "crash.log"), e + Environment.NewLine);
        }
        finally
        {
            if (File.Exists(_requestPath)) File.Delete(_requestPath);
            var result = exitCode;
            RunOnUiThread(() =>
            {
                SetResult(result == 0 ? Result.Ok : Result.Canceled,
                    new Intent().PutExtra("exit_code", result));
                Finish();
            });
        }
    }

    protected override void OnDestroy()
    {
        try { base.OnDestroy(); }
        finally
        {
            // Each engine session owns native SDL/audio state. Never reuse a process
            // for a different engine after Activity shutdown or a redial.
            global::Android.OS.Process.KillProcess(global::Android.OS.Process.MyPid());
        }
    }
}
