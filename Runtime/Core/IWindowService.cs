// ============================================================
//  IWindowService.cs
//  Platform-agnostic contract for all window manipulation.
//
//  Design intent:
//    This interface is the ONLY thing gameplay/Unity code should
//    ever depend on.  No Win32, no P/Invoke, no platform ifdefs
//    should leak past this boundary.  Every platform ships its
//    own implementation; callers never know or care which one
//    is active.
// ============================================================

namespace DesktopOverlay.Window
{
    /// <summary>
    /// Represents all OS-level window capabilities required by the
    /// desktop-overlay game.  Implementations are platform-specific;
    /// consumers should always program against this interface.
    /// </summary>
    public interface IWindowService
    {
        // ── Transparency ─────────────────────────────────────────
        /// <summary>
        /// Makes the window background fully transparent so the
        /// desktop is visible behind rendered game content.
        /// Passing <c>false</c> restores a solid background.
        /// </summary>
        void SetTransparent(bool value);

        // ── Input routing ────────────────────────────────────────
        /// <summary>
        /// When <c>true</c>, mouse and keyboard events pass through
        /// this window to whatever lies beneath it on the desktop.
        /// The game enters a "passive / idle" state.
        ///
        /// When <c>false</c>, the window captures input normally
        /// ("interactive" state).
        /// </summary>
        void SetClickThrough(bool value);

        // ── Z-order ──────────────────────────────────────────────
        /// <summary>
        /// Pins the window above all normal (non-topmost) windows.
        /// Passing <c>false</c> returns the window to the normal
        /// Z-order stack.
        /// </summary>
        void SetAlwaysOnTop(bool value);

        // ── Focus ────────────────────────────────────────────────
        /// <summary>
        /// Brings the window to the foreground and gives it input
        /// focus.  Should only be called on explicit player action
        /// to avoid focus-stealing.
        /// </summary>
        void FocusWindow();

        // ── State queries ────────────────────────────────────────
        // Implementations must keep these in sync with the real
        // OS state so UI/gameplay can read them without another
        // round-trip to the platform layer.
        bool IsTransparent { get; }
        bool IsClickThrough { get; }
        bool IsAlwaysOnTop { get; }
    }
}