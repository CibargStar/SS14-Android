using System.Collections;
using System.Reflection;
using System.Text.Json;
using SS14.Loader;

// Loads the real signed engine and original content, then inspects the real UI
// through its main-thread dispatcher. Reflection keeps its isolated ALC intact.
if (args.Length != 6) throw new ArgumentException("engine.zip signature public.pem content.zip native-dir output-dir");
var outputDirectory = Path.GetFullPath(args[5]);
Directory.CreateDirectory(outputDirectory);
var started = 0;
object? Get(object obj, string name) => obj.GetType().GetProperty(name)?.GetValue(obj)
    ?? obj.GetType().GetField(name)?.GetValue(obj);
IEnumerable<object> Descendants(object control)
{
    yield return control;
    if (Get(control, "Children") is not IEnumerable children) yield break;
    foreach (var child in children.Cast<object>())
        foreach (var nested in Descendants(child)) yield return nested;
}
float Number(object obj, string name) => Convert.ToSingle(Get(obj, name));

AppDomain.CurrentDomain.AssemblyLoad += (_, loaded) =>
{
    if (loaded.LoadedAssembly.GetName().Name != "Content.Client" || Interlocked.Exchange(ref started, 1) != 0) return;
    var assemblies = AppDomain.CurrentDomain.GetAssemblies();
    var shared = assemblies.Single(a => a.GetName().Name == "Robust.Shared");
    var client = assemblies.Single(a => a.GetName().Name == "Robust.Client");
    var ioc = shared.GetType("Robust.Shared.IoC.IoCManager", true)!;
    var resolve = ioc.GetMethods().Single(m => m.Name == "Resolve" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
    object Resolve(Type type) => resolve.MakeGenericMethod(type).Invoke(null, null)!;
    var taskType = shared.GetType("Robust.Shared.Asynchronous.ITaskManager", true)!;
    var tasks = Resolve(taskType);
    async Task<T> OnMain<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        taskType.GetMethod("RunOnMainThread")!.Invoke(tasks, [(Action)(() =>
        {
            try { completion.SetResult(action()); }
            catch (Exception error) { completion.SetException(error); }
        })]);
        return await completion.Task.WaitAsync(TimeSpan.FromMinutes(2));
    }
    _ = Task.Run(async () =>
    {
        try
        {
            var uiType = client.GetType("Robust.Client.UserInterface.IUserInterfaceManagerInternal", true)!;
            object? ui = null;
            for (var attempt = 0; attempt < 240; attempt++)
            {
                ui = await OnMain(() =>
                {
                    var candidate = Resolve(uiType);
                    var root = Get(candidate, "RootControl");
                    return root != null && Descendants(root).Any(c => c.GetType().Name == "MainMenuControl") ? candidate : null;
                });
                if (ui != null) break;
                await Task.Delay(250);
            }
            if (ui == null) throw new Exception("Original main menu did not initialize.");
            var clydeType = client.GetType("Robust.Client.Graphics.IClyde", true)!;
            var screenshotType = client.GetType("Robust.Client.Graphics.ScreenshotType", true)!;
            async Task Capture(string name)
            {
                await Task.Delay(500);
                var request = await OnMain(() =>
                {
                    var clyde = Resolve(clydeType);
                    var screenshot = (Task)clydeType.GetMethod("ScreenshotAsync")!.Invoke(clyde,
                        [Enum.Parse(screenshotType, "Final"), null])!;
                    var root = Get(ui, "RootControl")!;
                    var overlay = Get(ui, "MobileOverlay")!;
                    var boxes = Descendants(overlay)
                        .Where(c => c.GetType().Name is "PojavActionButton" or "VirtualThumbstick")
                        .Where(c => Get(c, "VisibleInTree") is true)
                        .Select(c => new { type = c.GetType().Name, text = Get(c, "Text")?.ToString(),
                            x = Number(Get(c, "GlobalPixelPosition")!, "X"),
                            y = Number(Get(c, "GlobalPixelPosition")!, "Y"),
                            w = Number(Get(c, "PixelSize")!, "X"), h = Number(Get(c, "PixelSize")!, "Y") }).ToArray();
                    var pixels = Get(root, "PixelSize")!;
                    var width = Number(pixels, "X");
                    var height = Number(pixels, "Y");
                    foreach (var box in boxes)
                        if (box.x < -1 || box.y < -1 || box.x + box.w > width + 1 || box.y + box.h > height + 1)
                            throw new Exception("Touch control outside viewport: " + box.text);
                    File.WriteAllText(Path.Combine(outputDirectory, name + ".json"),
                        JsonSerializer.Serialize(new { width, height, uiScale = Get(root, "UIScale"), boxes },
                            new JsonSerializerOptions { WriteIndented = true }));
                    return screenshot;
                });
                await request.WaitAsync(TimeSpan.FromSeconds(30));
                var image = request.GetType().GetProperty("Result")!.GetValue(request)!;
                var imageAssembly = image.GetType().Assembly;
                var save = imageAssembly.GetType("SixLabors.ImageSharp.ImageExtensions", true)!
                    .GetMethods().Single(m => m.Name == "SaveAsPng" && m.GetParameters().Length == 2 &&
                                              m.GetParameters()[1].ParameterType == typeof(string));
                save.Invoke(null, [image, Path.Combine(outputDirectory, name + ".png")]);
                ((IDisposable)image).Dispose();
                Console.WriteLine("VISUAL PASS " + name);
            }
            await Capture("main-menu");
            var overlay = await OnMain(() => Get(ui, "MobileOverlay")!);
            await OnMain(() => { overlay.GetType().GetMethod("OpenDisplaySettings")!.Invoke(overlay, null); return true; });
            await Capture("display-settings");
            await OnMain(() =>
            {
                var configType = shared.GetType("Robust.Shared.Configuration.IConfigurationManager", true)!;
                var config = Resolve(configType);
                var definition = shared.GetType("Robust.Shared.CVars", true)!.GetField("MobileControlsVisible")!.GetValue(null);
                configType.GetMethods().Single(m => m.Name == "SetCVar" && m.IsGenericMethodDefinition &&
                    m.GetParameters()[0].ParameterType.IsGenericType).MakeGenericMethod(typeof(bool))
                    .Invoke(config, [definition, false]);
                foreach (var window in Descendants(Get(ui, "RootControl")!).Where(c => c.GetType().Name == "MobileDisplaySettingsWindow").ToArray())
                    window.GetType().GetMethod("Close")!.Invoke(window, null);
                return true;
            });
            await Capture("controls-hidden");
            Console.WriteLine("VISUAL PASS original client UI rendering and hidden controls");
            Environment.Exit(0);
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.Exit(3); }
    });
};
return MobileLoader.Run([Path.GetFullPath(args[0]), args[1], Path.GetFullPath(args[2]),
    "--mount-zip", Path.GetFullPath(args[3]), "--cvar", "mobile.touch_controls=true",
    "--cvar", "display.max_fps=10", "--cvar", "display.width=2340", "--cvar", "display.height=1080"],
    Path.GetFullPath(args[4]), (_, _) => { });
