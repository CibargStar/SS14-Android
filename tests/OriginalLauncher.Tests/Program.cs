using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using SS14.Launcher;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Utility;
using SS14.Launcher.Views;
using Splat;

var root = Path.Combine(Path.GetTempPath(), "ss14-original-" + Guid.NewGuid());
Directory.CreateDirectory(root);
LauncherMobile.DataRoot = root;
var checks = 0;
using var http = new HttpClient(new OfflineHandler());

SS14.Launcher.Program.BuildForAndroid(http)
    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();
typeof(App).GetMethod("OnStartup", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
    .Invoke(Application.Current, [null, null]);
Assert(LauncherMobile.Root is Grid, "mobile startup created original launcher root");
var grid = LauncherMobile.Root!;
var main = grid.GetLogicalDescendants().OfType<MainWindow>().Single();
Assert(main.Content is MainWindowContent, "original MainWindowContent is used");
Assert(main.DataContext is SS14.Launcher.ViewModels.MainWindowViewModel, "original MainWindowViewModel is used");
Assert(File.Exists(LauncherPaths.PathContentDb), "original content database migration ran");
Assert(File.Exists(LauncherPaths.PathOverrideAssetsDb), "original override assets database migration ran");
Assert(LauncherPaths.DirUserData.StartsWith(root, StringComparison.Ordinal), "user data stays in app storage");
var host = new Window { Width = 960, Height = 540, Content = grid };
host.Show();
Dispatcher.UIThread.RunJobs();
using (var frame = host.CaptureRenderedFrame())
{
    Assert(frame != null, "original launcher renders through Skia");
    Directory.CreateDirectory("artifacts");
    frame!.Save("artifacts/original-launcher-login.png");
}
var earlyAccessAccept = main.GetVisualDescendants().OfType<Button>().Single(button =>
    Equals(button.Content, SS14.Launcher.Localization.LocalizationManager.Instance.GetString("main-window-early-access-accept")));
var acceptPoint = earlyAccessAccept.TranslatePoint(new Point(earlyAccessAccept.Bounds.Width / 2,
    earlyAccessAccept.Bounds.Height / 2), host)!.Value;
host.MouseDown(acceptPoint, MouseButton.Left);
host.MouseUp(acceptPoint, MouseButton.Left);
Dispatcher.UIThread.RunJobs();
Assert(Locator.Current.GetRequiredService<DataManager>().GetCVar(CVars.HasDismissedEarlyAccessWarning),
    "pointer click reaches the original early-access acknowledgement command");
host.Height = 260;
Dispatcher.UIThread.RunJobs();
Assert(main.Bounds.Height >= 500, "original controls retain a usable layout above the Android keyboard");
var mobileViewport = grid.Children.OfType<ScrollViewer>().Single();
Assert(mobileViewport.Extent.Height > mobileViewport.Viewport.Height,
    "short viewport can scroll to the original lower controls");
host.Height = 540;
Dispatcher.UIThread.RunJobs();
host.Width = 600;
Dispatcher.UIThread.RunJobs();
Assert(main.Bounds.Width >= 800 && mobileViewport.Extent.Width > mobileViewport.Viewport.Width,
    "narrow phone viewport retains access to the complete original launcher width");
host.Width = 960;
Dispatcher.UIThread.RunJobs();
var direct = new DirectConnectDialog();
var result = direct.ShowDialog<string?>(main);
Assert(!main.IsEnabled && grid.Children.Count == 2, "original direct-connect dialog is modal");
direct.Close("ss14://example.org:1212");
Assert(result.GetAwaiter().GetResult() == "ss14://example.org:1212", "original dialog return value preserved");
Assert(main.IsEnabled && grid.Children.Count == 1, "owner restored when dialog closes");
var favorite = new AddFavoriteDialog();
var canceled = favorite.ShowDialog<(string name, string address)>(main);
Assert(LauncherMobile.CloseTopDialog(), "Android back adapter closes current dialog");
Assert(canceled.GetAwaiter().GetResult().address == null, "cancel returns default dialog result");
var hubs = new HubSettingsDialog();
var hubResult = hubs.ShowDialog(main);
Assert(hubs.DataContext is SS14.Launcher.ViewModels.HubSettingsViewModel, "original hub settings view model populated");
var nested = new DirectConnectDialog();
var nestedResult = nested.ShowDialog<string?>(hubs);
Assert(!main.IsEnabled && !hubs.IsEnabled, "nested dialog keeps both owners modal");
LauncherMobile.CloseTopDialog();
Assert(nestedResult.IsCompleted && hubs.IsEnabled && !main.IsEnabled, "back closes only the top dialog");
LauncherMobile.CloseTopDialog();
Assert(hubResult.IsCompleted && main.IsEnabled, "hub settings closes through original dialog adapter");
string? externalUri = null;
LauncherMobile.OpenUri = uri => externalUri = uri;
Helpers.OpenUri("https://spacestation14.com/");
Assert(externalUri == "https://spacestation14.com/", "original external links route through OS adapter");
var logExports = 0;
LauncherMobile.OpenLogs = () => logExports++;
var model = (SS14.Launcher.ViewModels.MainWindowViewModel)main.DataContext!;
model.LoginViewModel.OpenLogDirectory();
model.OptionsTab.OpenLogDirectory();
Assert(logExports == 2, "both original log-directory actions route through Android export adapter");
Assert(RidUtility.FindBestRid(["linux-arm64"], "android-arm64") == null, "Android never selects desktop Linux engine");
Assert(RidUtility.FindBestRid(["android-arm64", "linux-arm64"], "android-arm64") == "android-arm64", "Android engine RID selected exactly");
Assert(RidUtility.FindBestRid(["linux-x64"], "linux-x64") == "linux-x64", "desktop RID selection retained");
host.Close();
Task.Run(() => EngineInstallChecks.Run(
    Locator.Current.GetRequiredService<SS14.Launcher.Models.EngineManager.IEngineManager>(), Assert))
    .GetAwaiter().GetResult();
var config = Locator.Current.GetRequiredService<DataManager>();
config.AddFavoriteServer(new FavoriteServer("Port persistence test", "ss14://example.org:1212"));
config.CommitConfig();
config.Close();
var reloaded = new DataManager();
reloaded.Load();
Assert(reloaded.FavoriteServers.Items.Any(server => server.Name == "Port persistence test"), "original favorites survive config reload");
reloaded.Close();
Console.WriteLine($"PASS: {checks} checks of original launcher under Android window adapter");

void Assert(bool condition, string message)
{ if (!condition) throw new Exception("FAIL: " + message); checks++; }

sealed class OfflineHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
