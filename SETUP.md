# Window System — Setup & Architecture Guide

## Files

| File | Layer | Purpose |
|---|---|---|
| `IWindowService.cs` | Interface | Platform-agnostic contract |
| `WindowsWindowService.cs` | Platform | Win32 implementation |
| `WindowController.cs` | Unity | MonoBehaviour integration |

---

## Unity Project Setup (Required — Read All Steps)

### 1. Player Settings

| Setting | Required Value | Why |
|---|---|---|
| **Fullscreen Mode** | Windowed | Borderless fullscreen bypasses the HWND we target |
| **Display Resolution Dialog** | Disabled | Avoids a dialog window stealing the HWND on first launch |
| **Run In Background** | ✅ Enabled | Keeps the game loop alive when the overlay loses focus |
| **Resizable Window** | Optional | Safe either way |
| **Use DXGI flip model** | Disabled | Avoids transparent window not functioning properly|

### 2. Camera Settings

The camera **background must output alpha = 0** or transparency won't work:

```
Camera → Clear Flags   : Solid Color
Camera → Background    : R=0, G=0, B=0, A=0   ← alpha zero is critical
```

### 3. Canvas Settings (UI)

**Screen Space - Overlay is incompatible with layered window hit-testing.** Buttons placed over transparent regions will not receive clicks because Windows treats color-keyed pixels as outside the window's hit area. Use Screen Space - Camera instead.

```
Canvas → Render Mode    : Screen Space - Camera
Canvas → Render Camera  : [Main Camera]
Canvas → Plane Distance : 1                    ← keeps UI in front of all world geometry
```

> ⚠️ Every Canvas in your project must use **Screen Space - Camera**. A single Overlay canvas will break UI interaction over transparent regions.

### 4. Input System

The project supports both Unity's legacy Input Manager and the new Input System package. The hotkeys in `WindowController` use `Input.GetKeyDown` which is **legacy Input Manager only**.

**If you are using the new Input System package:**

Install via Package Manager: `com.unity.inputsystem`

In **Project Settings → Player → Active Input Handling**, set to either:
```
Both                  ← recommended during migration (legacy + new run simultaneously)
Input System Package  ← only if you have fully migrated all input
```

Then in `WindowController.cs`, replace the `Input.GetKeyDown` calls with Input System equivalents:

```csharp
// Add at top of file
using UnityEngine.InputSystem;

// Replace legacy calls inside HandleDebugHotkeys():
if (Keyboard.current.f1Key.wasPressedThisFrame) { /* toggle click-through */ }
if (Keyboard.current.f2Key.wasPressedThisFrame) { /* toggle always-on-top  */ }
if (Keyboard.current.f3Key.wasPressedThisFrame) { /* toggle transparency    */ }
```

> ⚠️ `Keyboard.current` can be null if no keyboard is connected or if the Input System package is not initialised. Guard with `if (Keyboard.current != null)` in production.

**Validation — confirm keyboard input is working:**
1. Build with Development Build ✅
2. Launch the executable
3. Press F1 — the HUD should toggle between PASSIVE and INTERACTIVE
4. If F1 does nothing, open Player Settings and confirm Active Input Handling is not set to "Input System Package (New)" while your code still uses the legacy `Input` class

### 5. Render Pipeline

**Built-in RP:**
No changes required. Built-in RP outputs alpha naturally when the camera background alpha is 0.

**URP:**
In your **Universal Renderer Data** asset, ensure:
```
Post Processing → Enabled: false (or alpha-safe effects only)
```
Add `_AlphaOutput` to your URP renderer if you need explicit alpha pass-through.

**HDRP:**
Requires a custom `CustomPassVolume` to write alpha to the backbuffer.
HDRP is not recommended for this use case.

### 6. Scene Setup

```
Hierarchy:
└── WindowManager (GameObject)
    └── WindowController  [Component]
        ├── Start Transparent  : ✅
        ├── Start Click-Through: ❌  ← OFF on start so UI is immediately interactive
        ├── Start Always On Top: ✅
        └── Show Debug HUD     : ✅ (disable in release)
```

`WindowManager` should be in your bootstrap/persistent scene and never unloaded.

> **Why Click-Through OFF on start?** Starting with click-through enabled means the player cannot interact with any UI until F1 is pressed. For a tower defense game the player needs to be able to place towers immediately on launch. Switch to click-through ON only after the first wave ends, or when a specific idle/passive gameplay state is entered.

### 7. Build Settings

- **Target Platform:** Windows (x86_64)
- **Scripting Backend:** Mono or IL2CPP (both work)
- **Development Build:** ✅ during development (enables debug HUD)

---

## Hotkeys (Development / DEVELOPMENT_BUILD only)

| Key | Action |
|---|---|
| **F1** | Toggle Click-Through (Passive ↔ Interactive) |
| **F2** | Toggle Always-On-Top |
| **F3** | Toggle Transparency |

These are stripped automatically from release builds via `#if DEVELOPMENT_BUILD` guards.

---

## Architecture Notes

### Why three layers?

```
┌──────────────────────────────────────────────┐
│           Gameplay / UI Systems              │  ← know nothing about Win32
├──────────────────────────────────────────────┤
│          WindowController (MB)               │  ← Unity lifecycle only
├──────────────────────────────────────────────┤
│           IWindowService                     │  ← the stable contract
├──────────────────────────────────────────────┤
│  WindowsWindowService │ (macOS) │ (Linux)    │  ← platform detail
└──────────────────────────────────────────────┘
```

Any gameplay system (wave manager, UI, input router) calls only `WindowController.EnterInteractiveMode()` / `EnterPassiveMode()`.  
It never touches Win32. Platform ports require zero gameplay changes.

### Win32 Flag Interaction Map

```
Transparency ON:
  GWL_EXSTYLE |= WS_EX_LAYERED
  SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA)

Click-Through ON:
  GWL_EXSTYLE |= WS_EX_TRANSPARENT | WS_EX_LAYERED
  (WS_EX_LAYERED is required for WS_EX_TRANSPARENT to work)

Click-Through OFF + Transparency OFF:
  GWL_EXSTYLE &= ~(WS_EX_TRANSPARENT | WS_EX_LAYERED)

Always-On-Top ON:
  SetWindowPos(hwnd, HWND_TOPMOST, ...)

Always-On-Top OFF:
  SetWindowPos(hwnd, HWND_NOTOPMOST, ...)
```

### No per-frame OS calls

All Win32 calls are **event-driven** (state changes only).  
`Update()` only reads `Input.GetKeyDown` — a managed bool check, no P/Invoke.

---

## Extending to Other Platforms

1. Create `MacOSWindowService : IWindowService` using Objective-C via `DllImport("__Internal")` or a native plugin.
2. In `WindowController.CreateServiceForPlatform()`, add:
```csharp
#elif UNITY_STANDALONE_OSX
    return new MacOSWindowService();
```
No other file needs to change.

---

## Integrating with Gameplay

```csharp
// Example: Wave Manager triggers interactive mode when a wave starts
public class WaveManager : MonoBehaviour
{
    [SerializeField] private WindowController _window;

    public void StartWave()
    {
        _window.EnterInteractiveMode();   // window becomes clickable
        // ... spawn enemies
    }

    public void EndWave()
    {
        _window.EnterPassiveMode();       // desktop usable again
    }
}
```

---

## Known Edge Cases

| Situation | Behaviour | Mitigation |
|---|---|---|
| Game launched in background | `GetActiveWindow` returns 0 | Falls back to `FindWindow` by title |
| Editor play mode | Win32 calls skipped | `null` service returned; all calls no-op safely |
| UAC dialog appears | HWND_TOPMOST yields to UAC | By OS design; not a bug |
| Alt+Tab away | Focus lost; click-through unaffected | Call `FocusWindow()` only on explicit player action |
| DPI scaling (150%+) | Window position unaffected | `SWP_NOMOVE` prevents drift |