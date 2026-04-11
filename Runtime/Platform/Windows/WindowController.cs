// WindowController.cs
using System;
using UnityEngine;

namespace DesktopOverlay.Window
{
    [DisallowMultipleComponent]
    public sealed class WindowController : MonoBehaviour
    {
        public enum Mode { Fullscreen, Corner }

        // ── Inspector ────────────────────────────────────────────────
        [Header("Initial State")]
        [SerializeField] private bool _startTransparent = true;
        [SerializeField] private bool _startClickThrough = false;
        [SerializeField] private bool _startAlwaysOnTop = true;
        [SerializeField] private bool _startHideFromTaskbar = false;
        [SerializeField] private bool _usePerPixelClickThrough = true;
        [SerializeField] private Mode _startMode = Mode.Fullscreen;

        [Header("Corner Settings")]
        [SerializeField] private Vector2Int _cornerSize = new Vector2Int(350, 250);
        [SerializeField] private Vector2Int _expandedSize = new Vector2Int(900, 600);
        [SerializeField] private int _snapCorner = 3;   // 0=TL 1=TR 2=BL 3=BR
        [SerializeField] private int _padding = 10;

        [Header("Debug HUD")]
        [SerializeField] private bool _showDebugHud = true;
        [SerializeField] private Color _hudColorInteractive = new Color(0.2f, 1f, 0.4f);
        [SerializeField] private Color _hudColorPassive = new Color(1f, 0.6f, 0.2f);

        // ── State ────────────────────────────────────────────────────
        private IWindowService _windowService;
        private Mode _currentMode;
        private bool _isExpanded;

        // ── Debug HUD ────────────────────────────────────────────────
        private GUIStyle _hudStyle;
        private bool _hudStyleInitialised;
        private string _hudText = string.Empty;
        private bool _hudDirty = true;

        // ── Public API ───────────────────────────────────────────────
        public IWindowService Service => _windowService;
        public bool IsTransparent => _windowService?.IsTransparent ?? false;
        public bool IsClickThrough => _windowService?.IsClickThrough ?? false;
        public bool IsAlwaysOnTop => _windowService?.IsAlwaysOnTop ?? false;
        public Mode CurrentMode => _currentMode;
        public bool IsExpanded => _isExpanded;

        // ────────────────────────────────────────────────────────────
        //  Mode switching — the two public entry points
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Switch to fullscreen mode. Removes all transparency.
        /// Player must call this explicitly — never called automatically.
        /// </summary>
        public void SetModeFullscreen()
        {
            _currentMode = Mode.Fullscreen;
            _isExpanded = false;

            // Don't touch Screen.SetResolution or FullScreenMode here at all.
            // Unity Player Settings already starts at 1920x1080 Windowed.
            // We just move the Win32 window to cover the screen and make it transparent.

            int sw = Display.main.systemWidth;
            int sh = Display.main.systemHeight;

            // Remove title bar — WS_POPUP makes it truly borderless
            if (_windowService is Win32WindowService w32)
                w32.SetBorderless(transparent: true, clickThrough: false);

            // Move window to cover full screen via Win32 only
            _windowService.SetWindowRect(0, 0, sw, sh, true);

            if (!_usePerPixelClickThrough)
                _windowService.SetClickThrough(false, true); // only manual if not using per-pixel

            _hudDirty = true;
        }

        /// <summary>
        /// Switch to corner widget mode. Transparent background, small window.
        /// Starts collapsed (small widget). Player expands manually.
        /// </summary>
        public void SetModeCorner()
        {
            _currentMode = Mode.Corner;
            _isExpanded = false;

            // Corner DOES need borderless (no title bar on a small widget)
            if (_windowService is Win32WindowService w32)
                w32.SetBorderless(transparent: true, clickThrough: true);

            _windowService.SetAlwaysOnTop(true);
            SnapToCorner(_cornerSize);

            if (!_usePerPixelClickThrough)
                _windowService.SetClickThrough(false, true); // only manual if not using per-pixel

            _hudDirty = true;
        }

        /// <summary>
        /// Expand the corner widget to the larger interactive popup size.
        /// Only valid in Corner mode. Disables click-through for interaction.
        /// </summary>
        public void ToggleExpand()
        {
            if (_currentMode != Mode.Corner) return;

            _isExpanded = !_isExpanded;

            if (_isExpanded)
            {
                _windowService.SetClickThrough(false, true);
                _windowService.FocusWindow();
                SnapToCorner(_expandedSize);
            }
            else
            {
                _windowService.SetClickThrough(true, true);
                SnapToCorner(_cornerSize);
            }

            _hudDirty = true;
        }

        /// <summary>Called by UI when player clicks the corner widget.</summary>
        public void EnterInteractiveMode()
        {
            _windowService?.SetClickThrough(false, true);
            _windowService?.FocusWindow();
            _hudDirty = true;
        }

        public void EnterPassiveMode()
        {
            _windowService?.SetClickThrough(true, true);
            _hudDirty = true;
        }

        // ────────────────────────────────────────────────────────────
        //  Unity Lifecycle
        // ────────────────────────────────────────────────────────────
        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            _windowService = WindowServiceFactory.Create(Application.productName);

            if (_windowService == null)
            {
                Debug.LogError("[WindowController] Service is null!");
                return;
            }

            ApplyInitialState();
            StartCoroutine(ReapplyAfterUnitySettles());
        }

        private System.Collections.IEnumerator ReapplyAfterUnitySettles()
        {
            // Unity recreates the window on first frame in some configurations.
            // Wait 3 frames then reapply everything.
            yield return null;
            yield return null;
            yield return null;

            Debug.Log("[WindowController] Reapplying window state after settle...");

            if (_windowService is Win32WindowService w32)
            {
                if (_windowService.IsTransparent)
                    w32.ForceApplyTransparency();
                _windowService.SetTransparent(_windowService.IsTransparent);
                _windowService.SetClickThrough(_windowService.IsClickThrough, true);
                _windowService.SetAlwaysOnTop(_windowService.IsAlwaysOnTop);
                if (!_windowService.IsClickThrough)
                    _windowService.FocusWindow();
                Debug.Log("[WindowController] Window state reapplied.");
            }
        }

        private void Update()
        {
            if (_windowService == null) return;

            // Smart per-pixel click-through — runs only when active
            if (_windowService is Win32WindowService w32)
                w32.UpdateSmartClickThrough();

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
            (_windowService as IDisposable)?.Dispose();
        }

        // ────────────────────────────────────────────────────────────
        //  Private
        // ────────────────────────────────────────────────────────────
        private void ApplyInitialState()
        {
            if (_windowService is Win32WindowService w32)
                w32.SetHideFromTaskbar(_startHideFromTaskbar);

            // Mode functions handle everything — no pre-calls needed
            if (_startMode == Mode.Corner)
                SetModeCorner();
            else
                SetModeFullscreen(); // fullscreen = keep title bar, just go transparent

            _windowService.SetTransparent(_startTransparent);
            _windowService.SetClickThrough(_startClickThrough, true);
            _windowService.SetAlwaysOnTop(_startAlwaysOnTop);

            if (!_startClickThrough)
                _windowService.FocusWindow();
        }

        /// <summary>
        /// Calculates the correct screen position for the given size
        /// and calls SetWindowRect on the service.
        /// </summary>
        private void SnapToCorner(Vector2Int size)
        {
            int sw = Display.main.systemWidth;
            int sh = Display.main.systemHeight;
            int x, y;

            switch (_snapCorner)
            {
                case 0: x = _padding; y = _padding; break; // TL
                case 1: x = sw - size.x - _padding; y = _padding; break; // TR
                case 2: x = _padding; y = sh - size.y - _padding; break; // BL
                default: x = sw - size.x - _padding; y = sh - size.y - _padding; break; // BR
            }

            _windowService.SetWindowRect(x, y, size.x, size.y, _windowService.IsAlwaysOnTop);
        }

        private void HandleDebugHotkeys()
        {
            if (Input.GetKeyDown(KeyCode.Q))
            {
                bool next = !_windowService.IsClickThrough;
                _windowService.SetClickThrough(next, true);
                if (!next) _windowService.FocusWindow();
                _hudDirty = true;
            }
            if (Input.GetKeyDown(KeyCode.W))
            {
                _windowService.SetAlwaysOnTop(!_windowService.IsAlwaysOnTop);
                _hudDirty = true;
            }
            if (Input.GetKeyDown(KeyCode.E))
            {
                _windowService.SetTransparent(!_windowService.IsTransparent);
                _hudDirty = true;
            }
            // F4 — Toggle between corner and fullscreen (debug only)
            if (Input.GetKeyDown(KeyCode.R))
            {
                if (_currentMode == Mode.Corner) SetModeFullscreen();
                else SetModeCorner();
            }
        }

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
                _hudText = $"<b>Window Overlay</b>\n"
                         + $"Mode        : {_currentMode} {(_isExpanded ? "(expanded)" : "")}\n"
                         + $"Transparent : {(_windowService.IsTransparent ? "ON" : "OFF")}  [E]\n"
                         + $"Always-Top  : {(_windowService.IsAlwaysOnTop ? "ON" : "OFF")}  [W]\n"
                         + $"Click-Thru  : {(_windowService.IsClickThrough ? "ON" : "OFF")}  [Q]\n"
                         + $"[F4] Toggle Corner / Fullscreen";
                _hudDirty = false;
            }

            GUI.color = _windowService.IsClickThrough ? _hudColorPassive : _hudColorInteractive;
            GUI.Box(new Rect(10, 10, 280, 108), _hudText, _hudStyle);
            GUI.color = Color.white;
        }


    }
}
