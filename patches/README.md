# Android platform patch

Upstream RobustToolbox: `160be31f57129c24c1715b873b92f14d028de8b8` (Git submodule).

Source of mobile adaptations: [SolomonNumb1/space-station-14-mobile](https://github.com/SolomonNumb1/space-station-14-mobile/tree/87fe4c76c638b9fee3be9008f06cb9b94c213e62), `patches/mobile.patch`, MIT. Credit for initial Android/native work: SolomonNumb1; multitouch and scrolling: Ixee24. Original license is preserved in `mobile-MIT-LICENSE`.

This patch selects the SDL/GLES, shader portability, touch events, mobile controls and touch scrolling/window adaptations. Upstream content loader, network authentication and sandbox verification are retained. Compatibility aliases and fallback Content.Client/Content.Shared loading from the APK are not imported.

Android-specific lighting reductions in the source patch are excluded. Upstream light limits, resolution settings, shadow/blur defaults and FOV-map size remain intact. No alternate content is embedded in the launcher.

Apply through `scripts/prepare-engine.sh`. Changes to the engine submodule are represented by this patch, not by a private commit referenced from .gitmodules. New source files are included in the patch. Compilation is validated separately from running on Android; these changes alone do not provide JNI/Activity bootstrap or Android native libraries.

## Original launcher Android patch

`launcher-android-platform.patch` adapts the official SS14.Launcher commit `18fa9830576023595bffb417b0eaeb929fee5934`. Apply with `scripts/prepare-original-launcher.sh`; build with `scripts/build-original-launcher.sh`. The upstream MIT license is preserved in `launcher-upstream-LICENSE.txt`. Android sources are built from the submodule; they are not reimplemented in a parallel launcher. PlatformWindow provides the single-TopLevel dialog adapter, while the original window contents, view models and services remain in use.
