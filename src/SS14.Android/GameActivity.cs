extern alias ppysdl;

using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
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

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        _requestPath = Intent?.GetStringExtra("launch_request") ?? throw new InvalidOperationException("Missing launch request.");
        var allowed = Path.Combine(FilesDir!.AbsolutePath, "launches") + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(_requestPath).StartsWith(allowed, StringComparison.Ordinal))
            throw new InvalidOperationException("Launch request is outside private app storage.");
        _nativeDirectory = ApplicationInfo!.NativeLibraryDir!;
        _gameDirectory = Path.Combine(FilesDir.AbsolutePath, "client");
        Directory.CreateDirectory(_gameDirectory);
        base.OnCreate(savedInstanceState);
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
