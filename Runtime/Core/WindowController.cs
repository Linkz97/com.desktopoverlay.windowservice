// ============================================================
//  WindowController.cs
//  Unity integration layer — the ONLY MonoBehaviour in the system.
//
//  Responsibilities:
//    • Instantiate the correct IWindowService for the current platform
//    • Apply the initial window configuration on startup
//    • Expose runtime toggle hotkeys (debug only; strip in release)
//    • Render an in-game HUD showing current overlay state
//    • Provide a clean public API for gameplay systems
//
//  What this class does NOT do:
//    • No P/Invoke, no platform ifdefs beyond the factory switch
//    • No per-frame OS calls (all state changes are event-driven)
//    • No game logic — it owns the window, not the game
// ============================================================

using UnityEngine;

namespace DesktopOverlay.Window
{
    /// <summary>
    /// Scene singleton that owns the <see cref="IWindowService"/> instance
    /// and bridges it to Unity's lifecycle and input system.
    ///
    /// Drop this on a dedicated "WindowManager" GameObject that persists
    /// for the lifetime of the application (DontDestroyOnLoad).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WindowController : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════
        //  Inspector Configuration
        // ════════════════════════════════════════════════════════

        [Header("Initial State")]
        [Tooltip("Apply transparent background on startup.")]
        [SerializeField] private bool _startTransparent = true;

        [Tooltip("Start in click-through (passive) mode.")]
        [SerializeField] private bool _startClickThrough = false;

        [Tooltip("Pin window above all others on startup.")]
        [SerializeField] private bool _startAlwaysOnTop = true;

        [Header("Debug HUD")]
        [Tooltip("Show on-screen state overlay (disable in release).")]
        [SerializeField] private bool _showDebugHud = true;

        [SerializeField] private Color _hudColorInteractive = new Color(0.2f, 1f, 0.4f);
        [SerializeField] private Color _hudColorPassive = new Color(1f, 0.6f, 0.2f);

        // ════════════════════════════════════════════════════════
        //  Private fields
        // ════════════════════════════════════════════════════════

        private IWindowService _windowService;

        private GUIStyle _hudStyle;
        private bool _hudStyleInitialised;
        private string _hudText = string.Empty;
        private bool _hudDirty = true;

        // ════════════════════════════════════════════════════════
        //  Public API
        // ════════════════════════════════════════════════════════

        /// <summary>Direct access to the underlying service (read-only).</summary>
        public IWindowService Service => _windowService;

        public bool IsTransparent => _windowService?.IsTransparent ?? false;
        public bool IsClickThrough => _windowService?.IsClickThrough ?? false;
        public bool IsAlwaysOnTop => _windowService?.IsAlwaysOnTop ?? false;

        /// <summary>
        /// Switches the window into interactive mode:
        /// disables click-through and brings the window to focus.
        /// </summary>
        public void EnterInteractiveMode()
        {
            _windowService?.SetClickThrough(false);
            _windowService?.FocusWindow();
            _hudDirty = true;
        }

        /// <summary>
        /// Switches the window back to passive / idle mode:
        /// enables click-through so the desktop is usable beneath the overlay.
        /// </summary>
        public void EnterPassiveMode()
        {
            _windowService?.SetClickThrough(true);
            _hudDirty = true;
        }

        // ════════════════════════════════════════════════════════
        //  Unity Lifecycle
        // ════════════════════════════════════════════════════════

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);

            _windowService = WindowServiceFactory.Create(Application.productName);

            if (_windowService == null)
            {
                Debug.LogWarning(
                    "[WindowController] No IWindowService available for this platform. " +
                    "Window features will be disabled.");
                return;
            }

            ApplyInitialState();
        }

        private void Update()
        {
            if (_windowService == null) return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            HandleDebugHotkeys();
#endif
        }

        private void OnGUI()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!_showDebugHud || _windowService == null) return;
            DrawDebugHud();
#endif
        }

        private void OnDestroy()
        {
            if (_windowService is System.IDisposable disposable)
                disposable.Dispose();
        }

        // ════════════════════════════════════════════════════════
        //  Private — Initialisation
        // ════════════════════════════════════════════════════════

        private void ApplyInitialState()
        {
            // Order matters: transparency before click-through (WS_EX_LAYERED prerequisite).
            _windowService.SetTransparent(_startTransparent);
            _windowService.SetClickThrough(_startClickThrough);
            _windowService.SetAlwaysOnTop(_startAlwaysOnTop);
            _hudDirty = true;
        }

        // ════════════════════════════════════════════════════════
        //  Private — Debug Hotkeys (dev builds only)
        // ════════════════════════════════════════════════════════

        private void HandleDebugHotkeys()
        {
            // F1 — Toggle click-through
            if (Input.GetKeyDown(KeyCode.F1))

            {
                bool next = !_windowService.IsClickThrough;
                _windowService.SetClickThrough(next);
                if (!next) _windowService.FocusWindow();
                _hudDirty = true;
            }

            // F2 — Toggle always-on-top
            if (Input.GetKeyDown(KeyCode.F2))
            {
                _windowService.SetAlwaysOnTop(!_windowService.IsAlwaysOnTop);
                _hudDirty = true;
            }

            // F3 — Toggle transparency
            if (Input.GetKeyDown(KeyCode.F3))
            {
                _windowService.SetTransparent(!_windowService.IsTransparent);
                _hudDirty = true;
            }
        }

        // ════════════════════════════════════════════════════════
        //  Private — Debug HUD
        // ════════════════════════════════════════════════════════

        private void DrawDebugHud()
        {
            if (!_hudStyleInitialised)
            {
                _hudStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 14,
                    alignment = TextAnchor.UpperLeft,
                    padding = new RectOffset(8, 8, 6, 6),
                    richText = true,
                };
                _hudStyleInitialised = true;
            }

            if (_hudDirty)
            {
                RebuildHudText();
                _hudDirty = false;
            }

            GUI.color = _windowService.IsClickThrough ? _hudColorPassive : _hudColorInteractive;
            GUI.Box(new Rect(10, 10, 260, 88), _hudText, _hudStyle);
            GUI.color = Color.white;
        }

        private void RebuildHudText()
        {
            string mode = _windowService.IsClickThrough ? "PASSIVE (click-through)" : "INTERACTIVE";
            string transparent = _windowService.IsTransparent ? "ON" : "OFF";
            string topmost = _windowService.IsAlwaysOnTop ? "ON" : "OFF";

            _hudText =
                $"<b>Window Overlay</b>\n" +
                $"Mode        : {mode}\n" +
                $"Transparent : {transparent}  [F3]\n" +
                $"Always-Top  : {topmost}  [F2]\n" +
                $"[F1] Toggle Click-Through";
        }
    }
}