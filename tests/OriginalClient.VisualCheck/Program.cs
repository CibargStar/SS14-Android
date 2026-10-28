using System.Collections;
using System.Numerics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
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
Vector2 Vector(object obj) => new(Number(obj, "X"), Number(obj, "Y"));

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
                    for (var a = 0; a < boxes.Length; a++)
                    for (var b = a + 1; b < boxes.Length; b++)
                        if (MathF.Min(boxes[a].x + boxes[a].w, boxes[b].x + boxes[b].w) - MathF.Max(boxes[a].x, boxes[b].x) > 1 &&
                            MathF.Min(boxes[a].y + boxes[a].h, boxes[b].y + boxes[b].h) - MathF.Max(boxes[a].y, boxes[b].y) > 1)
                            throw new Exception("Overlapping touch controls: " + boxes[a].text + "/" + boxes[b].text);
                    if (name == "controls-hidden" && (boxes.Length != 1 || boxes[0].text != "UI"))
                        throw new Exception("Hidden controls remain interactive or the restore button is missing.");
                    if (name == "display-settings")
                    {
                        var dialog = Descendants(root).Single(c => c.GetType().Name == "MobileDisplaySettingsWindow");
                        if (Number(Get(dialog, "PixelSize")!, "Y") < 200 * Number(root, "UIScale"))
                            throw new Exception("Display settings content is collapsed.");
                    }
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
            // Use SDL's actual event watch and the production subscription chain.
            // Calling UI.TouchFinger directly would hide missing input wiring.
            var touchSource = await OnMain(() => new NativeTouchSource(Path.Combine(Path.GetFullPath(args[4]), "libSDL3.so")));
            void RawTouch(string action, long id, Vector2 position)
            {
                var clyde = Resolve(clydeType);
                var pixels = Get(Get(ui, "RootControl")!, "PixelSize")!;
                touchSource.Push(action, id, position / new Vector2(Number(pixels, "X"), Number(pixels, "Y")));
                var process = clyde.GetType().GetMethod("ProcessInput")!;
                process.Invoke(clyde, [Activator.CreateInstance(process.GetParameters()[0].ParameterType, [0f])]);
            }
            if (Environment.GetEnvironmentVariable("SS14_CHECK_INPUT") == "1")
            {
                await OnMain(() =>
                {
                    void Assert(bool success, string name)
                    {
                        if (!success) throw new Exception("SDL touch integration: " + name);
                        Console.WriteLine("INPUT PASS " + name);
                    }
                    var stick = Descendants(overlay).Single(c => c.GetType().Name == "VirtualThumbstick");
                    var centre = Vector(Get(stick, "GlobalPixelPosition")!) + Vector(Get(stick, "PixelSize")!) / 2;
                    var inputType = client.GetType("Robust.Client.Input.IInputManager", true)!;
                    var input = Resolve(inputType);
                    var keyType = client.GetType("Robust.Client.Input.Keyboard+Key", true)!;
                    bool Down(string key) => (bool)inputType.GetMethod("IsKeyDown")!.Invoke(input, [Enum.Parse(keyType, key)])!;
                    RawTouch("Down", 800, centre + new Vector2(30, 0) * Number(stick, "UIScale"));
                    Assert(Get(stick, "IsPressed") is true && Down("D"), "native SDL finger down reaches movement stick and original input");
                    RawTouch("Motion", 800, centre + new Vector2(0, -30) * Number(stick, "UIScale"));
                    Assert(Down("W") && !Down("D"), "native SDL finger motion turns movement");
                    RawTouch("Up", 800, centre);
                    Assert(Get(stick, "IsPressed") is false && !Down("W") && !Down("D"), "native SDL finger up releases movement");
                    var menu = Descendants(Get(ui, "RootControl")!).Single(c => c.GetType().Name == "MainMenuControl");
                    var options = Get(menu, "OptionsButton")!;
                    var point = Vector(Get(options, "GlobalPixelPosition")!) + Vector(Get(options, "PixelSize")!) / 2;
                    RawTouch("Down", 801, point);
                    RawTouch("Up", 801, point);
                    var window = Descendants(Get(ui, "RootControl")!).SingleOrDefault(c => c.GetType().Name == "OptionsMenu");
                    Assert(window != null && Get(window, "VisibleInTree") is true, "native SDL tap opens original Options button");
                    window!.GetType().GetMethod("Close")!.Invoke(window, null);
                    return true;
                });
            }
            if (Environment.GetEnvironmentVariable("SS14_CHECK_CAMERA") == "1")
            {
                await OnMain(() =>
                {
                    void Assert(bool result, string name)
                    {
                        if (!result) throw new Exception("Camera integration: " + name);
                        Console.WriteLine("CAMERA PASS " + name);
                    }
                    var eyesType = client.GetType("Robust.Client.Graphics.IEyeManager", true)!;
                    var eyes = Resolve(eyesType);
                    var oldEye = Get(eyes, "CurrentEye")!;
                    var entities = Resolve(shared.GetType("Robust.Shared.GameObjects.IEntityManager", true)!);
                    if (Get(entities, "Started") is not true) entities.GetType().GetMethod("Startup")!.Invoke(entities, null);
                    var mapSystem = Resolve(shared.GetType("Robust.Shared.GameObjects.IEntitySystemManager", true)!);
                    var system = mapSystem.GetType().GetMethod("GetEntitySystem", [typeof(Type)])!
                        .Invoke(mapSystem, [shared.GetType("Robust.Shared.GameObjects.SharedMapSystem", true)!])!;
                    var create = system.GetType().GetMethods().Single(m => m.Name == "CreateMap" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType.IsByRef);
                    object?[] mapArguments = [null, false];
                    create.Invoke(system, mapArguments);
                    var eye = Activator.CreateInstance(client.GetType("Robust.Client.Graphics.FixedEye", true)!)!;
                    var coordinatesType = shared.GetType("Robust.Shared.Map.MapCoordinates", true)!;
                    eye.GetType().GetProperty("Position")!.SetValue(eye, Activator.CreateInstance(coordinatesType, [Vector2.Zero, mapArguments[0]]));
                    eye.GetType().GetProperty("Zoom")!.SetValue(eye, Vector2.One);
                    eye.GetType().GetProperty("DrawFov")!.SetValue(eye, false);
                    eye.GetType().GetProperty("DrawLight")!.SetValue(eye, false);
                    eyesType.GetProperty("CurrentEye")!.SetValue(eyes, eye);
                    var stateRoot = Get(ui, "StateRoot")!;
                    stateRoot.GetType().GetProperty("Visible")!.SetValue(stateRoot, false);
                    var viewport = Get(ui, "MainViewport")!;
                    var viewportWasVisible = (bool)Get(viewport, "Visible")!;
                    viewport.GetType().GetProperty("Visible")!.SetValue(viewport, true);
                    var available = (Vector2)Get(Get(ui, "RootControl")!, "Size")!;
                    viewport.GetType().GetMethod("Measure")!.Invoke(viewport, [available]);
                    var arrange = viewport.GetType().GetMethod("Arrange")!;
                    arrange.Invoke(viewport, [Activator.CreateInstance(arrange.GetParameters()[0].ParameterType, [Vector2.Zero, available])]);
                    Get(viewport, "Viewport")!.GetType().GetProperty("Eye")!.SetValue(Get(viewport, "Viewport"), eye);
                    var viewportInterface = client.GetType("Robust.Client.UserInterface.CustomControls.IViewportControl", true)!;
                    var clyde = Resolve(clydeType);
                    var windowId = Get(Get(clyde, "MainWindow")!, "Id")!;
                    var screenType = shared.GetType("Robust.Shared.Map.ScreenCoordinates", true)!;
                    void Touch(string action, long id, Vector2 position) => RawTouch(action, id, position);
                    var transform = client.GetType("Robust.Client.UserInterface.Mobile.MobileCameraTransform", true)!;
                    float Scale() => (float)transform.GetProperty("Magnification")!.GetValue(null)!;
                    float Rotation() => (float)transform.GetProperty("Rotation")!.GetValue(null)!;
                    var down = 0;
                    var up = 0;
                    var input = Resolve(client.GetType("Robust.Client.Input.IInputManager", true)!);
                    var keyEvent = input.GetType().GetEvent("FirstChanceOnKeyEvent")!;
                    var parameters = keyEvent.EventHandlerType!.GetMethod("Invoke")!.GetParameters()
                        .Select(p => Expression.Parameter(p.ParameterType)).ToArray();
                    Action<object, object> count = (key, type) =>
                    {
                        if (Get(key, "Key")?.ToString() != "MouseLeft") return;
                        if (type.ToString() == "Down") down++;
                        if (type.ToString() == "Up") up++;
                    };
                    var listener = Expression.Lambda(keyEvent.EventHandlerType,
                        Expression.Invoke(Expression.Constant(count), parameters.Select(p => Expression.Convert(p, typeof(object)))), parameters).Compile();
                    keyEvent.AddEventHandler(input, listener);
                    var rootPixels = Get(Get(ui, "RootControl")!, "PixelSize")!;
                    var width = Number(rootPixels, "X");
                    var height = Number(rootPixels, "Y");
                    var centre = new Vector2(width / 2, height * .30f);
                    var first = centre - new Vector2(100, 0);
                    var second = centre + new Vector2(100, 0);
                    eyesType.GetProperty("MainViewport")!.SetValue(eyes, viewport);
                    foreach (var p in new[] { first, second })
                    {
                        var hit = ui.GetType().GetMethod("MouseGetControl")!.Invoke(ui, [Activator.CreateInstance(screenType, [p, windowId])]);
                        Console.WriteLine("CAMERA FIXTURE hit=" + hit?.GetType().FullName + " name=" + (hit == null ? "null" : Get(hit, "Name")) + " viewport=" + viewport.GetType().Name);
                    }
                    var originalPoint = (Vector2)viewportInterface.GetMethod("WorldToScreen")!.Invoke(viewport, [new Vector2(1, 0)])!;
                    var originalOrigin = (Vector2)viewportInterface.GetMethod("WorldToScreen")!.Invoke(viewport, [Vector2.Zero])!;
                    Touch("Down", 100, first);
                    Touch("Down", 101, second);
                    Touch("Motion", 101, first + new Vector2(400, 0));
                    Assert(MathF.Abs(Scale() - 2) < .01f, "world pinch changes the real viewport magnification");
                    var point = new Vector2(1, 0);
                    var before = (Vector2)viewportInterface.GetMethod("WorldToScreen")!.Invoke(viewport, [point])!;
                    Assert(MathF.Abs(Vector2.Distance(before, originalOrigin) / Vector2.Distance(originalPoint, originalOrigin) - 2) < .01f,
                        "pinch magnifies the rendered world projection rather than the interface");
                    Touch("Motion", 101, first + new Vector2(0, 400));
                    Assert(MathF.Abs(Rotation() + MathF.PI / 2) < .01f, "clockwise touch rotation updates the effective eye");
                    var after = (Vector2)viewportInterface.GetMethod("WorldToScreen")!.Invoke(viewport, [point])!;
                    var origin = (Vector2)viewportInterface.GetMethod("WorldToScreen")!.Invoke(viewport, [Vector2.Zero])!;
                    Assert(after.Y > origin.Y && MathF.Abs(after.X - origin.X) < 1,
                        "render projection rotates landmarks in the same direction as fingers");
                    var picked = viewportInterface.GetMethod("ScreenToMap")!.Invoke(viewport, [after])!;
                    Assert(Vector2.Distance((Vector2)Get(picked, "Position")!, point) < .001f,
                        "picking remains inverse to rendering after zoom and rotation");
                    var bounds = eyesType.GetMethod("GetWorldViewbounds")!.Invoke(eyes, null);
                    Assert(bounds != null && (Vector2)Get(eye, "Zoom")! == Vector2.One && Get(eye, "DrawFov") is false && Get(eye, "DrawLight") is false,
                        "server eye zoom, FOV and light flags are retained");
                    Touch("Up", 100, first);
                    Touch("Up", 101, first + new Vector2(0, 400));
                    Assert(down == 0 && up == 0, "camera gesture never clicks a world object");
                    Touch("Down", 200, centre);
                    Touch("Up", 200, centre);
                    Assert(down == 1 && up == 1, "ordinary world tap still reaches original input exactly once");
                    keyEvent.RemoveEventHandler(input, listener);
                    transform.GetMethod("Apply")!.Invoke(null, [.01f, -MathF.PI / 2]);
                    Assert(Scale() == 1 && MathF.Abs(Rotation()) < .001f, "pinch back restores the baseline view");
                    eyesType.GetProperty("CurrentEye")!.SetValue(eyes, oldEye);
                    viewport.GetType().GetProperty("Visible")!.SetValue(viewport, viewportWasVisible);
                    stateRoot.GetType().GetProperty("Visible")!.SetValue(stateRoot, true);
                    return true;
                });
            }
            if (Environment.GetEnvironmentVariable("SS14_CHECK_THUMBSTICK") == "1")
            {
                await OnMain(() =>
                {
                    var stick = Descendants(overlay).Single(c => c.GetType().Name == "VirtualThumbstick");
                    var inputType = client.GetType("Robust.Client.Input.IInputManager", true)!;
                    var input = Resolve(inputType);
                    var keyType = client.GetType("Robust.Client.Input.Keyboard+Key", true)!;
                    bool Down(string key) => (bool)inputType.GetMethod("IsKeyDown")!.Invoke(input, [Enum.Parse(keyType, key)])!;
                    void Assert(bool success, string name)
                    {
                        if (!success) throw new Exception("Thumbstick integration: " + name);
                        Console.WriteLine("STICK PASS " + name);
                    }
                    var centre = (Vector2)Get(stick, "Size")! / 2;
                    void Touch(string action, long id, Vector2 offset) => stick.GetType().GetMethod(action)!
                        .Invoke(stick, [id, centre + offset]);
                    void Up(long id) => stick.GetType().GetMethod("OnTouchUp")!.Invoke(stick, [id]);
                    Touch("OnTouchDown", 11, new Vector2(30, -30));
                    Assert(Down("W") && Down("D") && !Down("S") && !Down("A"), "diagonal reaches original input manager");
                    Touch("OnTouchDown", 22, new Vector2(-30, 30));
                    Up(22);
                    Assert(Down("W") && Down("D") && Get(stick, "IsPressed") is true, "second finger cannot interrupt movement");
                    Touch("OnTouchMove", 11, Vector2.Zero);
                    Assert(!Down("W") && !Down("D"), "returning to centre releases both original keys");
                    Touch("OnTouchMove", 11, new Vector2(-30, 0));
                    Assert(Down("A") && !Down("D"), "turning releases old direction before pressing the new one");
                    var clyde = Resolve(clydeType);
                    var focusType = client.GetType("Robust.Client.Graphics.WindowFocusedEventArgs", true)!;
                    var focus = Activator.CreateInstance(focusType, [false, clydeType.GetProperty("MainWindow")!.GetValue(clyde)])!;
                    stick.GetType().GetMethod("WindowFocusChanged", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(stick, [focus]);
                    Assert(!Down("A") && Get(stick, "IsPressed") is false, "focus loss releases movement through the original input manager");
                    Touch("OnTouchDown", 33, new Vector2(0, 30));
                    Up(11);
                    Assert(Down("S") && Get(stick, "IsPressed") is true, "old gesture release cannot stop a new gesture");
                    Up(33);
                    Assert(!Down("W") && !Down("A") && !Down("S") && !Down("D"), "finger lift leaves no movement keys held");
                    var state = stick.GetType().GetField("_stick", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(stick)!;
                    state.GetType().GetMethod("Press")!.Invoke(state, [null, new Vector2(30, 0), 10f, 43f]);
                    stick.GetType().GetMethod("ApplyMovement", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(stick, null);
                    var focused = ui.GetType().GetProperty("ControlFocused")!;
                    focused.SetValue(ui, stick);
                    focused.SetValue(ui, null);
                    Assert(!Down("D") && Get(stick, "IsPressed") is false && focused.GetValue(ui) == null,
                        "mouse capture loss releases movement without re-entering focus setters");
                    return true;
                });
            }
            await OnMain(() => { overlay.GetType().GetMethod("OpenDisplaySettings")!.Invoke(overlay, null); return true; });
            await Capture("display-settings");
            var originalOptions = await OnMain(() =>
            {
                foreach (var window in Descendants(Get(ui, "RootControl")!).Where(c => c.GetType().Name == "MobileDisplaySettingsWindow").ToArray())
                    window.GetType().GetMethod("Close")!.Invoke(window, null);
                var options = Activator.CreateInstance(loaded.LoadedAssembly.GetType("Content.Client.Options.UI.OptionsMenu", true)!)!;
                options.GetType().GetMethod("OpenCentered", Type.EmptyTypes)!.Invoke(options, null);
                return options;
            });
            await Capture("original-options");
            await OnMain(() => { originalOptions.GetType().GetMethod("Close")!.Invoke(originalOptions, null); return true; });
            await OnMain(() =>
            {
                var configType = shared.GetType("Robust.Shared.Configuration.IConfigurationManager", true)!;
                var config = Resolve(configType);
                var definition = shared.GetType("Robust.Shared.CVars", true)!.GetField("MobileControlsVisible")!.GetValue(null);
                var setter = configType.GetMethods().Single(m => m.Name == "SetCVar" && m.IsGenericMethodDefinition &&
                    m.GetParameters()[0].ParameterType.IsGenericType).MakeGenericMethod(typeof(bool));
                var values = setter.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : Type.Missing).ToArray();
                values[0] = definition;
                values[1] = false;
                setter.Invoke(config, values);
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
    "--cvar", "display.max_fps=10", "--cvar", "display.width=" + (Environment.GetEnvironmentVariable("SS14_VISUAL_WIDTH") ?? "2340"),
    "--cvar", "display.height=" + (Environment.GetEnvironmentVariable("SS14_VISUAL_HEIGHT") ?? "1080")],
    Path.GetFullPath(args[4]), (_, _) => { });

// SDL3 SDL_Event is a 128-byte union. These offsets follow SDL_TouchFingerEvent
// from the pinned SDL_events.h; no game/UI method or event handler is injected.
sealed class NativeTouchSource
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetWindows(out int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint GetWindowId(IntPtr window);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FreeMemory(IntPtr memory);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte PushEvent(IntPtr data);
    private readonly PushEvent _push;
    private readonly uint _window;

    public NativeTouchSource(string library)
    {
        var handle = NativeLibrary.Load(library);
        T Function<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(handle, name));
        var windows = Function<GetWindows>("SDL_GetWindows")(out var count);
        if (windows == IntPtr.Zero || count != 1) throw new Exception("Expected one real SDL game window.");
        _window = Function<GetWindowId>("SDL_GetWindowID")(Marshal.ReadIntPtr(windows));
        Function<FreeMemory>("SDL_free")(windows);
        _push = Function<PushEvent>("SDL_PushEvent");
    }

    public void Push(string action, long finger, Vector2 normalized)
    {
        var type = action switch { "Down" => 0x700u, "Up" => 0x701u, "Motion" => 0x702u, "Cancel" => 0x703u, _ => throw new ArgumentException(action) };
        var data = new byte[128];
        BitConverter.TryWriteBytes(data.AsSpan(0), type);
        BitConverter.TryWriteBytes(data.AsSpan(16), 1UL);
        BitConverter.TryWriteBytes(data.AsSpan(24), finger);
        BitConverter.TryWriteBytes(data.AsSpan(32), normalized.X);
        BitConverter.TryWriteBytes(data.AsSpan(36), normalized.Y);
        BitConverter.TryWriteBytes(data.AsSpan(48), action == "Up" ? 0f : 1f);
        BitConverter.TryWriteBytes(data.AsSpan(52), _window);
        var memory = Marshal.AllocHGlobal(data.Length);
        try
        {
            Marshal.Copy(data, 0, memory, data.Length);
            if (_push(memory) == 0) throw new Exception("SDL rejected injected touch event.");
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
}
