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
host.Width = 600;
host.Height = 260;
Dispatcher.UIThread.RunJobs();
var earlyAccessAccept = main.GetVisualDescendants().OfType<Button>().Single(button =>
    Equals(button.Content, SS14.Launcher.Localization.LocalizationManager.Instance.GetString("main-window-early-access-accept")));
var acceptPoint = earlyAccessAccept.TranslatePoint(new Point(earlyAccessAccept.Bounds.Width / 2,
    earlyAccessAccept.Bounds.Height / 2), host)!.Value;
host.MouseDown(acceptPoint, MouseButton.Left);
host.MouseUp(acceptPoint, MouseButton.Left);
Dispatcher.UIThread.RunJobs();
Assert(Locator.Current.GetRequiredService<DataManager>().GetCVar(CVars.HasDismissedEarlyAccessWarning),
    "pointer click reaches the original early-access acknowledgement command");
var mobileViewport = grid.Children.OfType<LauncherFitViewport>().Single();
foreach (var size in new[] { new Size(600, 260), new Size(780, 360), new Size(1040, 480), new Size(960, 540), new Size(480, 320) })
{
    host.Width = size.Width;
    host.Height = size.Height;
    Dispatcher.UIThread.RunJobs();
    var top = main.TranslatePoint(default, grid)!.Value;
    var bottom = main.TranslatePoint(new Point(main.Bounds.Width, main.Bounds.Height), grid)!.Value;
    Assert(Math.Abs(main.Bounds.Width - size.Width / mobileViewport.Scale) < 1 &&
           Math.Abs(main.Bounds.Height - size.Height / mobileViewport.Scale) < 1,
        $"mobile launcher lays out at actual viewport size at {size}");
    Assert(top.X >= -1 && top.Y >= -1 && bottom.X <= size.Width + 1 && bottom.Y <= size.Height + 1,
        $"complete launcher fits viewport without outer scrolling at {size}");
    Assert(mobileViewport.Scale > 0 && mobileViewport.Scale <= 1,
        $"launcher uses uniform downscaling at {size}");
}
Assert(main.GetVisualDescendants().OfType<MainWindowLogin>().Single().FindControl<ScrollViewer>("LoginScroll") is not null,
    "original authentication screens have a native scrolling viewport");
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
EngineCatalogChecks.Run(Assert);
var displayPreferences = new SS14.Launcher.Models.GameDisplaySettings();
LauncherMobile.ReadGameDisplaySettings = () => displayPreferences;
LauncherMobile.WriteGameDisplaySettings = value => displayPreferences = value.Normalize();
model.OptionsTab.GameUiScaleAutomatic = false;
model.OptionsTab.GameUiScalePercent = 75;
Assert(Math.Abs(displayPreferences.UiScale - 0.75f) < 0.001f, "original Options tab changes the game UI scale");
model.OptionsTab.UseMobileGameUi = false;
Assert(!displayPreferences.MobileUi, "original launcher checkbox disables mobile server UI override");
model.OptionsTab.ShowGameControls = false;
Assert(!displayPreferences.ShowControls && !displayPreferences.MobileUi && displayPreferences.UiScale == 0.75f, "hiding touch controls preserves selected UI scale");
model.OptionsTab.GameUiScaleAutomatic = true;
Assert(displayPreferences.UiScale == 0 && !displayPreferences.ShowControls, "automatic scale preserves hidden control preference");
displayPreferences = new SS14.Launcher.Models.GameDisplaySettings(1.25f, true);
LauncherMobile.RefreshGameDisplaySettings?.Invoke();
Assert(!model.OptionsTab.GameUiScaleAutomatic && Math.Abs(model.OptionsTab.GameUiScalePercent - 125) < 0.01 &&
       model.OptionsTab.ShowGameControls, "original Options tab refreshes settings changed in game");
// Render the genuine authenticated hub with local fixture rows, never a live account.
var logins = Locator.Current.GetRequiredService<SS14.Launcher.Models.Logins.LoginManager>();
var fixtureLogin = new LoginInfo { UserId = Guid.NewGuid(), Username = "AndroidLayoutTest",
    Token = new SS14.Launcher.Models.LoginToken("layout-fixture-not-a-real-token", DateTimeOffset.UtcNow.AddDays(1)) };
logins.AddFreshLogin(fixtureLogin);
logins.ActiveAccountId = fixtureLogin.UserId;
model.SelectedIndex = 1;
Dispatcher.UIThread.RunJobs();
var serverCache = Locator.Current.GetRequiredService<SS14.Launcher.Models.ServerStatus.ServerListCache>();
serverCache.Status = SS14.Launcher.Models.ServerStatus.RefreshListStatus.Updated;
for (var index = 0; index < 180; index++)
    serverCache.AllServers.Add(new SS14.Launcher.Models.ServerStatus.ServerStatusData($"ss14://layout-{index}.invalid:1212")
    {
        Name = $"Server {index + 1} — original SS14 hub layout",
        Status = SS14.Launcher.Models.ServerStatus.ServerStatusCode.Online,
        PlayerCount = index % 100, SoftMaxPlayerCount = 100
    });
Dispatcher.UIThread.RunJobs();
Assert(model.LoggedIn && model.ServersTab.SearchedServers.Count > 0, "original authenticated hub displays local fixture servers");
foreach (var size in new[] { new Size(600, 260), new Size(780, 360), new Size(1040, 480), new Size(960, 540), new Size(480, 320) })
{
    host.Width = size.Width;
    host.Height = size.Height;
    Dispatcher.UIThread.RunJobs();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
    Dispatcher.UIThread.RunJobs();
    using var hubFrame = host.CaptureRenderedFrame();
    hubFrame!.Save($"artifacts/launcher-hub-fit-{size.Width}x{size.Height}.png");
    var tabs = main.GetVisualDescendants().OfType<TabControl>().Single();
    var bottom = main.TranslatePoint(new Point(main.Bounds.Width, main.Bounds.Height), grid)!.Value;
    Assert(bottom.X <= size.Width + 1 && bottom.Y <= size.Height + 1 && tabs.Bounds.Height > 150,
        $"original hub and navigation fit complete phone viewport at {size}");
    Assert(grid.Children.All(child => child is not ScrollViewer), "hub has no outer scrolling container");
    var rows = main.GetVisualDescendants().OfType<SS14.Launcher.Views.MainWindowTabs.ServerEntryView>()
        .Where(row => row.IsVisible && row.Bounds.Height > 0).ToArray();
    Assert(rows.Length > 0, "native server cards are realized");
    foreach (var row in rows.Take(3))
    {
        var connect = row.FindControl<Border>("ConnectCell")!;
        var name = row.FindControl<Border>("ServerNameCell")!;
        Assert(connect.Child is Button { MinHeight: >= 48, Command: not null }, "original connect command has a touch-sized target");
        Assert(connect.Bounds.Left >= name.Bounds.Right - 1 && connect.Bounds.Width > 80,
            "server name and connect action do not overlap");
    }
    var firstRow = rows[0];
    var favoriteAction = firstRow.FindControl<Button>("FavoriteButton")!;
    var serverModel = (SS14.Launcher.ViewModels.MainWindowTabs.ServerEntryViewModel)firstRow.DataContext!;
    var wasFavorite = serverModel.IsFavorite;
    var wasExpanded = serverModel.IsExpanded;
    var favoritePoint = favoriteAction.TranslatePoint(new Point(favoriteAction.Bounds.Width / 2, favoriteAction.Bounds.Height / 2), host)!.Value;
    host.MouseDown(favoritePoint, MouseButton.Left); host.MouseUp(favoritePoint, MouseButton.Left);
    Dispatcher.UIThread.RunJobs();
    Assert(serverModel.IsFavorite != wasFavorite && serverModel.IsExpanded == wasExpanded,
        "quick favorite uses the original command without expanding the server card");
    serverModel.FavoriteButtonPressed();
    var listView = main.GetVisualDescendants().OfType<SS14.Launcher.Views.MainWindowTabs.ServerListTabView>().Single();
    var toolbar = listView.FindControl<DockPanel>("SearchToolbar")!;
    Assert(toolbar.Bounds.Height >= 44 && toolbar.Bounds.Top < 8, "search stays at the top with touch targets");
    model.ServersTab.FiltersVisible = true;
    Dispatcher.UIThread.RunJobs();
    var filters = listView.GetVisualDescendants().OfType<SS14.Launcher.Views.MainWindowTabs.ServerListFiltersView>().Single();
    Assert(filters.Bounds.Height > 15 && filters.Bounds.Bottom <= listView.Bounds.Height + 1,
        "filters retain a bounded scrolling pane on short screens");
    model.ServersTab.FiltersVisible = false;
    Dispatcher.UIThread.RunJobs();
}
logins.ActiveAccountId = null;
Locator.Current.GetRequiredService<DataManager>().RemoveLogin(fixtureLogin);
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
