using Android.App;
using Android.OS;
using Android.Widget;
using System.Text.Json;
using RuntimeProbe.Core;

namespace RuntimeProbe.Android;

[Activity(Name = "io.ss14.runtimeprobe.MainActivity", Label = "SS14 Runtime Probe", MainLauncher = true, Exported = true)]
public class MainActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var output = new TextView(this) { Text = "Running runtime checks…", TextSize = 13 };
        output.SetTextIsSelectable(true);
        var scroll = new ScrollView(this);
        scroll.AddView(output);
        SetContentView(scroll);
        var directory = Path.Combine(FilesDir!.AbsolutePath, "runtime-probe");
        _ = Task.Run(() =>
        {
            try
            {
                var report = ProbeRunner.Run(directory);
                var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(FilesDir!.AbsolutePath, "runtime-probe-result.json"), json);
                RunOnUiThread(() => output.Text = json);
            }
            catch (Exception e)
            {
                var error = e.ToString();
                RunOnUiThread(() => output.Text = error);
            }
        });
    }
}
