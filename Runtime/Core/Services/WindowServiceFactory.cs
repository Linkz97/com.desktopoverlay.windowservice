// ============================================================
//  WindowServiceFactory.cs
//  Central platform-dispatch factory for IWindowService.
//
//  This is the ONLY place in the codebase that contains
//  platform #ifdefs.  Adding a new platform:
//    1. Create a new sealed class implementing IWindowService
//       under Runtime/Platform/<YourOS>/
//    2. Add a branch here.
//    3. Done — no other file needs touching.
// ============================================================

using UnityEngine;

namespace DesktopOverlay.Window
{
    /// <summary>
    /// Creates the correct <see cref="IWindowService"/> for the current
    /// runtime platform.  Returns <c>null</c> on unsupported platforms
    /// so callers can degrade gracefully.
    /// </summary>
    public static class WindowServiceFactory
    {
        /// <param name="windowTitle">
        ///   The Unity Player window title (usually <c>Application.productName</c>).
        ///   Forwarded to implementations that need it for HWND lookup.
        /// </param>
        /// <returns>
        ///   A platform-appropriate <see cref="IWindowService"/>,
        ///   or <c>null</c> if the current platform is unsupported.
        /// </returns>
        public static IWindowService Create(string windowTitle)
        {

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            return new Win32WindowService();
#elif UNITY_EDITOR
            Debug.Log("[WindowServiceFactory] Running in Editor — window service disabled.");
            return null;

            // ── Future platforms ──────────────────────────────────────
            // #elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
            //     return new MacOSWindowService(windowTitle);
            //
            // #elif UNITY_STANDALONE_LINUX && !UNITY_EDITOR
            //     return new LinuxWindowService(windowTitle);
            // ─────────────────────────────────────────────────────────

#else
            Debug.LogWarning(
                $"[WindowServiceFactory] Platform '{Application.platform}' " +
                "has no IWindowService implementation. Returning null.");
            return null;
#endif
        }
    }
}