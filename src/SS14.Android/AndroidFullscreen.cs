using Android.Views;
using AndroidX.Core.View;

namespace SS14.Android;

internal static class AndroidFullscreen
{
    public static void Apply(Window? window)
    {
        if (window == null) return;
        WindowCompat.SetDecorFitsSystemWindows(window, false);
        window.AddFlags(WindowManagerFlags.Fullscreen);
        var controller = WindowCompat.GetInsetsController(window, window.DecorView);
        controller.SystemBarsBehavior = WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
        controller.Hide(WindowInsetsCompat.Type.SystemBars());
        if (OperatingSystem.IsAndroidVersionAtLeast(28) && window.Attributes is { } attributes)
        {
            attributes.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.ShortEdges;
            window.Attributes = attributes;
        }
    }
}
