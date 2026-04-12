using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DesktopOverlay.Window
{
    public sealed class Win32WindowService : IWindowService, IDisposable
    {
        // ── Win32 imports ────────────────────────────────────────────
        [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr hDC, int nXPos, int nYPos);

        [StructLayout(LayoutKind.Sequential)]
        struct MARGINS { public int Left, Right, Top, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        // ── Constants ────────────────────────────────────────────────
        const int GWL_STYLE = -16;
        const int GWL_EXSTYLE = -20;
        const int WS_POPUP = unchecked((int)0x80000000);
        const int WS_VISIBLE = 0x10000000;
        const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
        const int WS_EX_LAYERED = 0x00080000;
        const int WS_EX_TRANSPARENT = 0x00000020;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int WS_EX_APPWINDOW = 0x00040000;
        const uint LWA_COLORKEY = 0x00000001;
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_SHOWWINDOW = 0x0040;
        const uint SWP_FRAMECHANGED = 0x0020;
        const uint CLR_INVALID = 0xFFFFFFFF;

        // Transparent color — must match Camera background exactly
        // BGR format for Win32. 0x00000000 = black.
        const uint TRANSPARENT_COLOR = 0x00000000;

        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        // ── State ────────────────────────────────────────────────────
        public IntPtr Hwnd => _hwnd;

        private readonly IntPtr _hwnd;
        private bool _isTransparent;
        private bool _isClickThrough;
        private bool _isAlwaysOnTop;
        private bool _hideFromTaskbar;
        private bool _useSmartClickThrough; // per-pixel mode
        private bool _disposed;

        public bool IsTransparent => _isTransparent;
        public bool IsClickThrough => _isClickThrough;
        public bool IsAlwaysOnTop => _isAlwaysOnTop;

        public Win32WindowService()
        {
            _hwnd = GetActiveWindow();
            if (_hwnd == IntPtr.Zero)
                throw new InvalidOperationException("[Win32WindowService] HWND is null.");
        }

        // ── Transparency ─────────────────────────────────────────────
        public void SetTransparent(bool transparent)
        {
            if (transparent)
            {
                var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(_hwnd, ref margins);

                int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
                exStyle |= WS_EX_LAYERED;
                SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);

                SetLayeredWindowAttributes(_hwnd, TRANSPARENT_COLOR, 255, LWA_COLORKEY);

                SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
            }
            else
            {
                var margins = new MARGINS { Left = 0, Right = 0, Top = 0, Bottom = 0 };
                DwmExtendFrameIntoClientArea(_hwnd, ref margins);

                int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
                exStyle &= ~WS_EX_LAYERED;
                SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);

                SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
            }

            _isTransparent = transparent;
        }

        // ── Click-through ────────────────────────────────────────────
        // Two modes:
        //   Smart (default) — per-pixel: transparent pixels fall through,
        //                     visible pixels capture input. Best for overlay games.
        //   Full            — entire window is click-through (old behaviour).
        public void SetClickThrough(bool clickThrough, bool smart = true)
        {
            _isClickThrough = clickThrough;
            _useSmartClickThrough = clickThrough && smart;

            if (!clickThrough)
            {
                // Interactive — remove WS_EX_TRANSPARENT entirely
                int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
                exStyle &= ~WS_EX_TRANSPARENT;
                SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);
            }
            else if (!smart)
            {
                // Full click-through — whole window passes input
                int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
                exStyle |= WS_EX_TRANSPARENT;
                SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);
            }
            // Smart mode: WS_EX_TRANSPARENT is toggled per-frame in Update()
        }

        // Call this from WindowController.Update() every frame
        // Only does work when smart click-through is active
        public void UpdateSmartClickThrough()
        {
            if (!_useSmartClickThrough) return;

            bool overContent = IsMouseOverContent();

            int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
            bool currentlyTransparent = (exStyle & WS_EX_TRANSPARENT) != 0;

            if (overContent && currentlyTransparent)
            {
                // Mouse moved onto game content — capture input
                exStyle &= ~WS_EX_TRANSPARENT;
                SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);
                SetForegroundWindow(_hwnd);
            }
            else if (!overContent && !currentlyTransparent)
            {
                // Mouse moved onto empty space — let it fall through
                exStyle |= WS_EX_TRANSPARENT;
                SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);
            }
        }

        // Reads the actual pixel color under the mouse from the screen.
        // If it matches the transparent color key → empty → click-through.
        // If it's any other color → game content → capture input.
        private bool IsMouseOverContent()
        {
            GetCursorPos(out POINT p);

            IntPtr hdc = GetDC(IntPtr.Zero); // desktop DC
            uint color = GetPixel(hdc, p.X, p.Y);
            ReleaseDC(IntPtr.Zero, hdc);

            if (color == CLR_INVALID) return false;

            // Extract RGB from COLORREF (0x00BBGGRR)
            byte r = (byte)(color & 0xFF);
            byte g = (byte)((color >> 8) & 0xFF);
            byte b = (byte)((color >> 16) & 0xFF);

            // If pixel is black (or very close) → transparent background
            // threshold of 5 handles any minor rendering artifacts
            bool isBackground = r <= 5 && g <= 5 && b <= 5;
            return !isBackground;
        }

        // ── Always on top ────────────────────────────────────────────
        public void SetAlwaysOnTop(bool alwaysOnTop)
        {
            IntPtr insertAfter = alwaysOnTop ? HWND_TOPMOST : HWND_NOTOPMOST;
            SetWindowPos(_hwnd, insertAfter, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
            _isAlwaysOnTop = alwaysOnTop;
        }

        // ── Taskbar ──────────────────────────────────────────────────
        public void SetHideFromTaskbar(bool hide)
        {
            _hideFromTaskbar = hide;
            int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
            if (hide) { exStyle |= WS_EX_TOOLWINDOW; exStyle &= ~WS_EX_APPWINDOW; }
            else { exStyle &= ~WS_EX_TOOLWINDOW; exStyle |= WS_EX_APPWINDOW; }
            SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);
            ShowWindow(_hwnd, 0);
            ShowWindow(_hwnd, 5);
        }

        // ── Focus ────────────────────────────────────────────────────
        public void FocusWindow()
        {
            ShowWindow(_hwnd, 9);
            SetForegroundWindow(_hwnd);
        }

        // ── Window rect ──────────────────────────────────────────────
        public void SetWindowRect(int x, int y, int width, int height, bool topmost)
        {
            IntPtr insertAfter = topmost ? HWND_TOPMOST : HWND_NOTOPMOST;
            SetWindowPos(_hwnd, insertAfter, x, y, width, height,
                SWP_SHOWWINDOW | SWP_FRAMECHANGED);
        }

        // ── Borderless ───────────────────────────────────────────────
        public void SetBorderless(bool transparent, bool clickThrough)
        {
            SetWindowLong(_hwnd, GWL_STYLE, WS_POPUP | WS_VISIBLE);

            int exStyle = _hideFromTaskbar ? WS_EX_TOOLWINDOW : WS_EX_APPWINDOW;
            SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);

            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);

            if (transparent)
                SetTransparent(true);

            // Always use smart click-through for overlay mode
            SetClickThrough(clickThrough, smart: true);

            _isClickThrough = clickThrough;
        }

        // ── Fullscreen ───────────────────────────────────────────────
        public void SetFullscreen()
        {
            int sw = Display.main.systemWidth;
            int sh = Display.main.systemHeight;
            SetWindowLong(_hwnd, GWL_STYLE, WS_OVERLAPPEDWINDOW | WS_VISIBLE);
            int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
            exStyle &= ~WS_EX_LAYERED;
            exStyle &= ~WS_EX_TRANSPARENT;
            SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);
            _isTransparent = false;
            _isClickThrough = false;
            _useSmartClickThrough = false;
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, sw, sh,
                SWP_SHOWWINDOW | SWP_FRAMECHANGED);
        }

        public void ForceApplyTransparency()
        {
            SetTransparent(true);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}