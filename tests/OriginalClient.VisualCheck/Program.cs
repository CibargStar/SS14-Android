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
                        .Where(c => c.GetType().Name is "PojavActionButton" or "VirtualThumbstick" || Get(c, "Name")?.ToString() == "MobileDisplayButton")
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
                        throw new Exception("Hidden stick remains interactive or UI button is missing.");
                    if (boxes.Any(box => box.type != "VirtualThumbstick" && box.text != "UI"))
                        throw new Exception("Removed touch action buttons remain in the overlay.");
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
                    var stickState = stick.GetType().GetField("_stick", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(stick)!;
                    Assert((Vector2)Get(stickState, "Offset")! == Vector2.Zero, "finger lift returns knob exactly to centre");
                    long[] activeFingers = [810];
                    AppDomain.CurrentDomain.SetData("SS14_ANDROID_ACTIVE_TOUCHES", (Func<long[]>)(() => activeFingers));
                    void Frame()
                    {
                        var frame = ui.GetType().GetMethod("FrameUpdate")!;
                        frame.Invoke(ui, [Activator.CreateInstance(frame.GetParameters()[0].ParameterType, [0.016f])]);
                    }
                    RawTouch("Down", 810, centre + new Vector2(30, 0) * Number(stick, "UIScale"));
                    Frame();
                    Assert(Down("D"), "physical snapshot retains movement while stick finger is down");
                    activeFingers = [811]; // Owner lifted, another finger is still down; deliberately lose SDL_UP.
                    Frame();
                    Assert(!Down("D") && Get(stick, "IsPressed") is false && (Vector2)Get(stickState, "Offset")! == Vector2.Zero,
                        "Android pointer lifetime stops and recentres stick even without SDL_UP");
                    activeFingers = [810];
                    RawTouch("Down", 810, centre + new Vector2(0, -30) * Number(stick, "UIScale"));
                    Frame();
                    Assert(Down("W") && !Down("D"), "reused pointer acquires a fresh stick after lost release");
                    activeFingers = [];
                    Frame();
                    Assert(!Down("W") && Get(stick, "IsPressed") is false, "Android cancellation releases all stick movement");
                    AppDomain.CurrentDomain.SetData("SS14_ANDROID_ACTIVE_TOUCHES", null);
                    RawTouch("Up", 810, centre);
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
            if (Environment.GetEnvironmentVariable("SS14_CHECK_HUD") == "1")
            {
                void HudCheck(bool success, string name)
                {
                    if (!success) throw new Exception("Mobile HUD integration: " + name);
                    Console.WriteLine("HUD PASS " + name);
                }
                object HudFind(string name) => Descendants(Get(ui, "RootControl")!).Single(c => Get(c, "Name")?.ToString() == name);
                void HudTap(object control)
                {
                    var point = Vector(Get(control, "GlobalPixelPosition")!) + Vector(Get(control, "PixelSize")!) / 2;
                    RawTouch("Down", 970, point); RawTouch("Up", 970, point);
                }
                object? originalScreen = null;
                object? originalChat = null;
                object? originalPanel = null;
                object? originalInput = null;
                object? originalBar = null;
                object? firstButton = null;
                await OnMain(() =>
                {
                    Get(ui, "StateRoot")!.GetType().GetProperty("Visible")!.SetValue(Get(ui, "StateRoot"), false);
                    ui.GetType().GetMethod("LoadScreen")!.MakeGenericMethod(loaded.LoadedAssembly.GetType("Content.Client.UserInterface.Screens.DefaultGameScreen", true)!).Invoke(ui, null);
                    originalScreen = Get(ui, "ActiveScreen")!;
                    originalChat = Get(originalScreen, "ChatBox")!;
                    originalPanel = Get(originalChat, "ChatWindowPanel")!;
                    originalInput = Get(Get(originalChat, "ChatInput")!, "Input")!;
                    originalBar = Descendants(originalScreen).Single(c => c.GetType().Name == "GameTopMenuBar");
                    firstButton = Descendants(originalBar).First(c => c.GetType().Name == "MenuButton");
                    var addLine = originalChat.GetType().GetMethod("AddLine")!;
                    var color = addLine.GetParameters()[1].ParameterType.GetProperty("White")!.GetValue(null);
                    for (var i = 0; i < 70; i++) addLine.Invoke(originalChat, [$"{i:00} Local: Тест мобильного чата — upstream channels, history and filters.", color]);
                    return true;
                });
                await Task.Delay(1000);
                await OnMain(() =>
                {
                    HudCheck(ReferenceEquals(Get(originalChat!, "ChatWindowPanel"), originalPanel) && Descendants(HudFind("MobileChatDock")).Contains(originalChat!), "server chat is adapted using its original ChatBox instance");
                    HudCheck(Descendants(HudFind("MobileChatDock")).Contains(originalPanel!), "original messages, channels and filters are mounted in mobile dock");
                    HudCheck(ReferenceEquals(Get(originalScreen!, "ChatBox"), originalChat), "original screen/controller ChatBox lookup is retained");
                    var lookup = originalScreen!.GetType().GetMethod("GetWidget")!.MakeGenericMethod(originalChat!.GetType()).Invoke(originalScreen, null);
                    HudCheck(ReferenceEquals(lookup, originalChat), "upstream widget registry retains reparented original chat");
                    var ancestor = Get(originalPanel!, "Parent");
                    HudCheck(ReferenceEquals(ancestor, originalChat), "chat links retain the original ChatBox ancestor and entity click handler");
                    var labels = Descendants(firstButton!).Where(c => c.GetType().Name == "Label").ToArray();
                    HudCheck(labels.Length > 0 && labels.All(c => Get(c, "Visible") is false), "menu keeps original icons and hides all keyboard legends");
                    HudCheck(Descendants(originalBar!).Contains(firstButton!), "menu retains the original action button instance");
                    return true;
                });
                await Capture("mobile-hud-compact");
                await OnMain(() => { HudTap(HudFind("MobileMenuToggle")); return true; });
                await Task.Delay(250);
                await OnMain(() =>
                {
                    HudCheck(Get(HudFind("MobileMenuStrip"), "Collapsed") is true && Get(HudFind("MobileMenuIcons"), "VisibleInTree") is false, "native SDL tap collapses original action strip");
                    HudTap(HudFind("MobileMenuToggle"));
                    originalInput!.GetType().GetProperty("Text")!.SetValue(originalInput, "draft ; original channel");
                    HudTap(HudFind("MobileChatToggle"));
                    return true;
                });
                await Task.Delay(300);
                await OnMain(() =>
                {
                    HudCheck(Get(HudFind("MobileChatDock"), "Collapsed") is true && Get(originalPanel!, "VisibleInTree") is false, "chat collapses to its restore icon");
                    HudTap(HudFind("MobileChatToggle"));
                    return true;
                });
                await Task.Delay(300);
                await OnMain(() =>
                {
                    HudCheck(Get(originalInput!, "Text")?.ToString() == "draft ; original channel", "collapsing chat preserves the original draft");
                    HudTap(HudFind("MobileChatExpand"));
                    return true;
                });
                await Task.Delay(300);
                await OnMain(() =>
                {
                    HudCheck(Get(HudFind("MobileChatDock"), "Expanded") is true && Number(HudFind("MobileChatDock"), "Height") > 250, "chat expands within phone viewport");
                    var output = Descendants(originalPanel!).Single(c => c.GetType().Name == "OutputPanel");
                    var scrollbar = Descendants(output).Single(c => c.GetType().Name == "VScrollBar");
                    var before = Number(scrollbar, "Value");
                    var historyClicks = 0;
                    var clickEvent = output.GetType().GetEvent("OnKeyBindDown")!;
                    var clickArgument = Expression.Parameter(clickEvent.EventHandlerType!.GenericTypeArguments[0]);
                    Action<object> countClick = value => { if (Get(Get(value, "Function")!, "FunctionName")?.ToString() == "UIClick") historyClicks++; };
                    var clickListener = Expression.Lambda(clickEvent.EventHandlerType, Expression.Invoke(Expression.Constant(countClick), Expression.Convert(clickArgument, typeof(object))), clickArgument).Compile();
                    clickEvent.AddEventHandler(output, clickListener);
                    var point = Vector(Get(output, "GlobalPixelPosition")!) + Vector(Get(output, "PixelSize")!) / 2;
                    RawTouch("Down", 980, point); RawTouch("Motion", 980, point + new Vector2(0, 90)); RawTouch("Up", 980, point + new Vector2(0, 90));
                    HudCheck(Number(scrollbar, "Value") < before, "one-finger swipe scrolls original chat history");
                    HudCheck(historyClicks == 0, "chat history swipe never emits a message/link click");
                    RawTouch("Down", 981, point); RawTouch("Up", 981, point);
                    HudCheck(historyClicks == 1, "ordinary history tap emits one original UI click after finger release (observed " + historyClicks + ")");
                    clickEvent.RemoveEventHandler(output, clickListener);
                    // Whitespace exercises original submit/history/channel handlers
                    // without sending a fixture message to any server.
                    originalInput!.GetType().GetProperty("Text")!.SetValue(originalInput, " ");
                    HudTap(HudFind("MobileChatSend"));
                    HudCheck(Get(originalInput, "Text")?.ToString() == "", "send icon invokes original input submit and chat handler");
                    return true;
                });
                await OnMain(() =>
                {
                    var dock = HudFind("MobileChatDock");
                    var p = Vector(Get(dock, "GlobalPixelPosition")!); var size = Vector(Get(dock, "PixelSize")!);
                    var buttons = Descendants(overlay).Where(c => c.GetType().Name == "VirtualThumbstick" && Get(c, "VisibleInTree") is true);
                    HudCheck(buttons.All(c =>
                    {
                        var q = Vector(Get(c, "GlobalPixelPosition")!); var s = Vector(Get(c, "PixelSize")!);
                        return MathF.Min(p.X + size.X, q.X + s.X) <= MathF.Max(p.X, q.X) || MathF.Min(p.Y + size.Y, q.Y + s.Y) <= MathF.Max(p.Y, q.Y);
                    }), "only movement stick and UI remain; expanded chat does not overlap movement");
                    return true;
                });
                await Capture("mobile-hud-expanded");
                await OnMain(() => { Environment.SetEnvironmentVariable("ROBUST_ANDROID_MOBILE_UI", "false"); return true; });
                await Task.Delay(300);
                await OnMain(() =>
                {
                    HudCheck(!Descendants(Get(ui, "RootControl")!).Any(c => c.GetType().Name == "MobileServerUiAdapter"), "opt-out removes all mobile HUD presentation");
                    HudCheck(Get(originalChat!, "Visible") is true && ReferenceEquals(Get(originalChat!, "Parent"), originalScreen) && ReferenceEquals(Get(originalPanel!, "Parent"), originalChat), "opt-out restores original server chat ownership and visibility");
                    HudCheck(ReferenceEquals(Get(firstButton!, "Parent"), originalBar) && Descendants(firstButton!).Where(c => c.GetType().Name == "Label").All(c => Get(c, "Visible") is true), "opt-out restores original menu layout and keyboard legends");
                    Environment.SetEnvironmentVariable("ROBUST_ANDROID_MOBILE_UI", "true");
                    return true;
                });
                await Task.Delay(400);
                await OnMain(() =>
                {
                    HudCheck(Descendants(Get(ui, "RootControl")!).Count(c => Get(c, "Name")?.ToString() == "MobileChatSend") == 1, "re-enabling creates one send action without duplicate controls");
                    ui.GetType().GetMethod("UnloadScreen")!.Invoke(ui, null);
                    return true;
                });
                await Task.Delay(300);
                await OnMain(() =>
                {
                    HudCheck(ReferenceEquals(Get(originalPanel!, "Parent"), originalChat) && ReferenceEquals(Get(firstButton!, "Parent"), originalBar), "screen unload restores widgets for subsequent original screen reload");
                    Get(ui, "StateRoot")!.GetType().GetProperty("Visible")!.SetValue(Get(ui, "StateRoot"), true);
                    return true;
                });
            }
            await OnMain(() =>
            {
                var button = Descendants(overlay).Single(c => Get(c, "Name")?.ToString() == "MobileDisplayButton");
                var point = Vector(Get(button, "GlobalPixelPosition")!) + Vector(Get(button, "PixelSize")!) / 2;
                RawTouch("Down", 990, point); RawTouch("Up", 990, point);
                return true;
            });
            await Capture("display-settings");
            await OnMain(() =>
            {
                var settings = Descendants(Get(ui, "RootControl")!).Single(c => c.GetType().Name == "MobileDisplaySettingsWindow");
                var debug = Descendants(settings).Single(c => c.GetType().Name == "Button" && Get(c, "Text")?.ToString() == "Debug");
                var point = Vector(Get(debug, "GlobalPixelPosition")!) + Vector(Get(debug, "PixelSize")!) / 2;
                RawTouch("Down", 900, point);
                RawTouch("Up", 900, point);
                if (!Descendants(Get(ui, "RootControl")!).Any(c => c.GetType().Name == "MobileDebugWindow"))
                    throw new Exception("Debug button did not open resource window through SDL input.");
                Console.WriteLine("DEBUG PASS UI button opens real resource window through SDL");
                return true;
            });
            await Task.Delay(1600);
            await OnMain(() =>
            {
                var debug = Descendants(Get(ui, "RootControl")!).Single(c => c.GetType().Name == "MobileDebugWindow");
                var text = string.Join("\n", Descendants(debug).Where(c => c.GetType().Name == "Label").Select(c => Get(c, "Text")));
                if (!text.Contains("FPS:") || !text.Contains("CPU игры:") || !text.Contains("Память процесса (RSS):") || text.Contains("сбор данных"))
                    throw new Exception("Resource metrics were not sampled: " + text);
                Console.WriteLine("DEBUG PASS measured FPS, frame time, CPU and process memory: " + text.Replace("\n", " / "));
                debug.GetType().GetMethod("Close")!.Invoke(debug, null);
                return true;
            });
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
