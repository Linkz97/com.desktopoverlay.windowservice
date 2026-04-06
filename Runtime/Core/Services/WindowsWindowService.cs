// ============================================================
//  WindowsWindowService.cs
//  Windows platform implementation of IWindowService.
//
//  All Win32 P/Invoke lives here and ONLY here.
//  Nothing outside this file should ever #ifdef UNITY_STANDALONE_WIN.
//
//  Tested against:
//    Unity 2022 LTS / 2023 LTS (IL2CPP & Mono)
//    Windows 10 21H2+ / Windows 11
// ============================================================

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DesktopOverlay.Window
{
    /// <summary>
    /// Windows-specific window service.  Uses Win32 APIs to manage
    /// transparency, click-through, always-on-top, and focus.
    /// Must only be instantiated on the Windows platform.
    /// </summary>
    public sealed class WindowsWindowService : IWindowService
    {
        // ════════════════════════════════════════════════════════
        //  Win32 API Declarations
        // ════════════════════════════════════════════════════════

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy,
            uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(
            IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr CreateRectRgn(int nLeftRect, int nTopRect,
                                                    int nRightRect, int nBottomRect);

        [DllImport("user32.dll")]
        private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        // ════════════════════════════════════════════════════════
        //  Win32 Constants
        // ════════════════════════════════════════════════════════

        private const int GWL_EXSTYLE = -20;
        private const int GWL_STYLE = -16;

        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_POPUP = unchecked((int)0x80000000);

        private const uint LWA_ALPHA = 0x00000002;
        private const uint LWA_COLORKEY = 0x00000001;
        private const uint TRANSPARENT_COLOR_KEY = 0x00FF00FF; // magenta

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_FRAMECHANGED = 0x0020;

        private const int SW_SHOW = 5;

        // ════════════════════════════════════════════════════════
        //  Internal state
        // ════════════════════════════════════════════════════════

        private readonly string _windowTitle;
        private IntPtr _hwnd;
        private bool _isTransparent;
        private bool _isClickThrough;
        private bool _isAlwaysOnTop;
        private bool _hasLoggedMissingHandle;

        // ════════════════════════════════════════════════════════
        //  IWindowService — state queries
        // ════════════════════════════════════════════════════════

        public bool IsTransparent => _isTransparent;
        public bool IsClickThrough => _isClickThrough;
        public bool IsAlwaysOnTop => _isAlwaysOnTop;

        // ════════════════════════════════════════════════════════
        //  Constructor
        // ════════════════════════════════════════════════════════

        /// <param name="windowTitle">
        ///   The Unity Player window title set in Player Settings.
        ///   Used as a fallback when GetActiveWindow returns zero.
        /// </param>
        public WindowsWindowService(string windowTitle)
        {
            _windowTitle = windowTitle;

            if (!TryRefreshWindowHandle())
                Debug.LogError(
                    "[WindowsWindowService] Could not obtain a valid window handle. " +
                    "All window operations will be no-ops. " +
                    "Ensure the game window title matches Application.productName.");
        }

        // ════════════════════════════════════════════════════════
        //  IWindowService — Transparency
        // ════════════════════════════════════════════════════════

        public void SetTransparent(bool value)
        {
            _isTransparent = value;
            ApplyWindowStyles(false);
        }

        // ════════════════════════════════════════════════════════
        //  IWindowService — Click-Through
        // ════════════════════════════════════════════════════════

        public void SetClickThrough(bool value)
        {
            if (_hwnd == IntPtr.Zero) return;
            if (_isClickThrough == value) return;

            int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);

            if (value)
            {
                // Entering passive mode — clear hit region, then apply transparent flag.
                UpdateHitTestRegion(false);
                exStyle |= (WS_EX_TRANSPARENT | WS_EX_LAYERED);
                SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);
            }
            else
            {
                // Entering interactive mode — strip transparent flag, claim full hit region.
                exStyle &= ~WS_EX_TRANSPARENT;
                if (!_isTransparent) exStyle &= ~WS_EX_LAYERED;

                SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);
                SetWindowPos(_hwnd, _isAlwaysOnTop ? HWND_TOPMOST : IntPtr.Zero,
                    0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);

                UpdateHitTestRegion(true);
                ShowWindow(_hwnd, SW_SHOW);
                SetForegroundWindow(_hwnd);
                SetFocus(_hwnd);
            }

            _isClickThrough = value;
        }

        // ════════════════════════════════════════════════════════
        //  IWindowService — Always-On-Top
        // ════════════════════════════════════════════════════════

        public void SetAlwaysOnTop(bool value)
        {
            _isAlwaysOnTop = value;
            ApplyTopMost();
            Debug.Log($"[WindowsWindowService] Always-on-top → {value}");
        }

        // ════════════════════════════════════════════════════════
        //  IWindowService — Focus
        // ════════════════════════════════════════════════════════

        public void FocusWindow()
        {
            if (!TryRefreshWindowHandle()) return;

            ShowWindow(_hwnd, SW_SHOW);
            BringWindowToTop(_hwnd);

            bool result = SetForegroundWindow(_hwnd);
            if (!result)
                Debug.LogWarning(
                    "[WindowsWindowService] SetForegroundWindow failed. " +
                    "The OS may have blocked the focus request.");

            SetFocus(_hwnd);
        }

        // ════════════════════════════════════════════════════════
        //  Private helpers
        // ════════════════════════════════════════════════════════

        private void ApplyWindowStyles(bool restoreInteractiveInput)
        {
            if (!TryRefreshWindowHandle()) return;

            int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
            bool layeredRequired = _isTransparent || _isClickThrough;

            exStyle = layeredRequired
                ? exStyle | WS_EX_LAYERED
                : exStyle & ~WS_EX_LAYERED;

            exStyle = _isClickThrough
                ? exStyle | WS_EX_TRANSPARENT
                : exStyle & ~WS_EX_TRANSPARENT;

            int style = GetWindowLong(_hwnd, GWL_STYLE);
            if ((style & WS_POPUP) == 0)
                SetWindowLong(_hwnd, GWL_STYLE, style | WS_POPUP);

            SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);

            if (layeredRequired)
            {
                if (_isTransparent)
                    SetLayeredWindowAttributes(_hwnd, TRANSPARENT_COLOR_KEY, 0, LWA_COLORKEY);
                else
                    SetLayeredWindowAttributes(_hwnd, 0, 255, LWA_ALPHA);
            }

            SetWindowPos(_hwnd, _isAlwaysOnTop ? HWND_TOPMOST : HWND_NOTOPMOST,
                0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_FRAMECHANGED | SWP_SHOWWINDOW |
                (_isClickThrough ? SWP_NOACTIVATE : 0));

            if (restoreInteractiveInput)
                FocusWindow();
        }

        private void ApplyTopMost()
        {
            if (!TryRefreshWindowHandle()) return;

            uint flags = SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW;
            if (_isClickThrough) flags |= SWP_NOACTIVATE;

            SetWindowPos(_hwnd, _isAlwaysOnTop ? HWND_TOPMOST : HWND_NOTOPMOST,
                0, 0, 0, 0, flags);
        }

        private bool TryRefreshWindowHandle()
        {
            if (_hwnd != IntPtr.Zero && IsWindow(_hwnd)) return true;

            IntPtr handle = GetActiveWindow();

            if (handle == IntPtr.Zero)
                handle = Process.GetCurrentProcess().MainWindowHandle;

            if (handle == IntPtr.Zero)
                handle = FindWindow(null, _windowTitle);

            if (handle == IntPtr.Zero)
            {
                if (!_hasLoggedMissingHandle)
                {
                    Debug.LogWarning(
                        $"[WindowsWindowService] Could not resolve HWND for \"{_windowTitle}\".");
                    _hasLoggedMissingHandle = true;
                }
                return false;
            }

            _hwnd = handle;
            _hasLoggedMissingHandle = false;
            return true;
        }

        /// <summary>
        /// Interactive mode: set hit-test region to the full client rect so clicks
        /// over transparent pixels still reach Unity.
        /// Passive mode: clear the region so WS_EX_TRANSPARENT can pass clicks through.
        /// </summary>
        private void UpdateHitTestRegion(bool interactive)
        {
            if (_hwnd == IntPtr.Zero) return;

            if (interactive)
            {
                GetClientRect(_hwnd, out RECT rect);
                IntPtr rgn = CreateRectRgn(rect.Left, rect.Top, rect.Right, rect.Bottom);
                SetWindowRgn(_hwnd, rgn, true);
                // Windows owns the region handle after SetWindowRgn — do NOT DeleteObject it.
            }
            else
            {
                // IntPtr.Zero clears the region, restoring pixel-based hit-testing.
                SetWindowRgn(_hwnd, IntPtr.Zero, true);
            }
        }
    }
}