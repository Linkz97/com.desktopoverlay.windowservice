// ScreenEdgeDetector.cs — quick feasibility prototype, not production code
using DesktopOverlay.Window;
using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DesktopOverlay.Window
{
    [DisallowMultipleComponent]

    public class ScreenEdgeDetector : MonoBehaviour
    {
        [DllImport("user32.dll")] static extern IntPtr FindWindow(string cls, string wnd);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        public enum TaskbarEdge { Bottom, Top, Left, Right, NotFound }

        [Header("Results (read-only)")]
        public RectInt screenRect;
        public RectInt taskbarRect;
        public TaskbarEdge taskbarEdge;
        public RectInt safeArea; // screen minus taskbar strip

        [SerializeField] private TaskbarCollider _taskbarCollider; // wire in Inspector

        void Start()
        {
            DetectAll();
        }

        [ContextMenu("Detect Now")]
        public void DetectAll()
        {
            // ── 1. Screen bounds ─────────────────────────────────────────
            int sw = Display.main.systemWidth;
            int sh = Display.main.systemHeight;
            screenRect = new RectInt(0, 0, sw, sh);
            Debug.Log($"[Screen] {sw} x {sh}");

            // ── 2. Taskbar ───────────────────────────────────────────────
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR || UNITY_EDITOR_WIN
            IntPtr trayHwnd = FindWindow("Shell_TrayWnd", null);
            if (trayHwnd == IntPtr.Zero)
            {
                Debug.LogWarning("[Taskbar] Shell_TrayWnd not found.");
                taskbarEdge = TaskbarEdge.NotFound;
                safeArea = screenRect;
                return;
            }

            GetWindowRect(trayHwnd, out RECT r);
            int tw = r.Right - r.Left;
            int th = r.Bottom - r.Top;
            taskbarRect = new RectInt(r.Left, r.Top, tw, th);

            Debug.Log($"[Taskbar] RECT  L={r.Left} T={r.Top} R={r.Right} B={r.Bottom}");
            Debug.Log($"[Taskbar] Size  {tw} x {th}");

            // ── 3. Infer docked edge ─────────────────────────────────────
            // The taskbar always spans the full width (or height) of the screen.
            // Whichever edge its thin dimension touches = docked edge.
            bool spansWidth = tw >= sw * 0.9f;
            bool spansHeight = th >= sh * 0.9f;

            if (spansWidth && r.Top == 0) taskbarEdge = TaskbarEdge.Top;
            else if (spansWidth && r.Bottom == sh) taskbarEdge = TaskbarEdge.Bottom;
            else if (spansHeight && r.Left == 0) taskbarEdge = TaskbarEdge.Left;
            else if (spansHeight && r.Right == sw) taskbarEdge = TaskbarEdge.Right;
            else taskbarEdge = TaskbarEdge.NotFound;

            Debug.Log($"[Taskbar] Edge = {taskbarEdge}");

            // ── 4. Safe area ─────────────────────────────────────────────
            safeArea = taskbarEdge switch
            {
                TaskbarEdge.Bottom => new RectInt(0, 0, sw, sh - th),
                TaskbarEdge.Top => new RectInt(0, th, sw, sh - th),
                TaskbarEdge.Left => new RectInt(tw, 0, sw - tw, sh),
                TaskbarEdge.Right => new RectInt(0, 0, sw - tw, sh),
                _ => screenRect,
            };

            Debug.Log($"[SafeArea] {safeArea}");
#else
            Debug.Log("[ScreenEdgeDetector] Not on Windows — skipping taskbar check.");
            safeArea = screenRect;
#endif

            _taskbarCollider?.Apply();
        }

        void OnDrawGizmos()
        {
            // Visualize as colored lines in Scene view (screen-space, Y flipped for Unity)
            if (screenRect.width == 0) return;

            float sh = screenRect.height;

            // Safe area — green outline
            Gizmos.color = Color.green;
            DrawScreenRect(safeArea, sh, -1f); // z=-1 so it draws behind

            // Taskbar strip — red outline
            Gizmos.color = Color.red;
            DrawScreenRect(taskbarRect, sh, 0f);
        }

        // Converts a Win32 screen rect (Y-down, pixels) to Unity world gizmos (Y-up)
        void DrawScreenRect(RectInt r, float screenH, float z)
        {
            // Scale to Unity units — assume 1 unit = 1 pixel for debug purposes
            float x0 = r.x;
            float y0 = screenH - r.y - r.height; // flip Y
            float x1 = r.x + r.width;
            float y1 = y0 + r.height;

            Gizmos.DrawLine(new Vector3(x0, y0, z), new Vector3(x1, y0, z));
            Gizmos.DrawLine(new Vector3(x1, y0, z), new Vector3(x1, y1, z));
            Gizmos.DrawLine(new Vector3(x1, y1, z), new Vector3(x0, y1, z));
            Gizmos.DrawLine(new Vector3(x0, y1, z), new Vector3(x0, y0, z));
        }
    }
}