// TaskbarCollider.cs
// Attach to a GameObject at (0,0,0) with a BoxCollider2D.
// Converts the Win32 taskbar pixel rect into world-space collider size + offset.
// transform.position stays at (0,0,0) forever — only offset and size move.

using UnityEngine;

namespace DesktopOverlay.Window
{
    [RequireComponent(typeof(BoxCollider2D))]
    [DisallowMultipleComponent]
    public sealed class TaskbarCollider : MonoBehaviour
    {
        // ── Inspector ────────────────────────────────────────────────
        [Header("Dependencies")]
        [SerializeField] private ScreenEdgeDetector _detector;
        [SerializeField] private Camera _worldCamera;

        [Header("Collider Tuning")]
        [Tooltip("Extra thickness added on top of the real taskbar height. Helps pet not clip through.")]
        [SerializeField] private float _surfacePadding = 0.05f;

        // ── State ────────────────────────────────────────────────────
        private BoxCollider2D _col;
        private bool _isReady;

        // ── Public ───────────────────────────────────────────────────
        /// <summary>World-space Y of the taskbar's top surface. Use to spawn/reset pet position.</summary>
        public float SurfaceWorldY { get; private set; }

        // ────────────────────────────────────────────────────────────
        //  Unity lifecycle
        // ────────────────────────────────────────────────────────────
        private void Awake()
        {
            _col = GetComponent<BoxCollider2D>();
            _col.isTrigger = false;

            if (_detector == null)
                Debug.LogError("[TaskbarCollider] ScreenEdgeDetector not assigned.", this);
            if (_worldCamera == null)
                Debug.LogError("[TaskbarCollider] World Camera not assigned.", this);
        }

        private void Start()
        {
            // Detector runs its own Start — safe to read here
            Apply();
        }

        // ────────────────────────────────────────────────────────────
        //  Public API
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Re-read taskbar data from the detector and update the collider.
        /// Call this after ScreenEdgeDetector.DetectAll() if you re-detect at runtime.
        /// </summary>
        public void Apply()
        {
            if (_detector == null || _worldCamera == null) return;

            if (_detector.taskbarEdge == ScreenEdgeDetector.TaskbarEdge.NotFound)
            {
                Debug.LogWarning("[TaskbarCollider] Taskbar not found — collider disabled.");
                _col.enabled = false;
                _isReady = false;
                return;
            }

            RectInt tb = _detector.taskbarRect;
            int sh = _detector.screenRect.height;

            // ── Convert the four taskbar corners from Win32 pixels to world space ──
            // Win32: Y=0 at top.  Unity ScreenToWorldPoint: Y=0 at bottom.
            // Flip: unityY = screenH - win32Y
            Vector3 worldBL = ScreenPixelToWorld(tb.x, sh - (tb.y + tb.height));
            Vector3 worldTR = ScreenPixelToWorld(tb.x + tb.width, sh - tb.y);

            float worldW = worldTR.x - worldBL.x;
            float worldH = worldTR.y - worldBL.y;

            // Centre of the taskbar slab in world space
            float cx = worldBL.x + worldW * 0.5f;
            float cy = worldBL.y + worldH * 0.5f;

            // ── Apply to collider (offset is relative to transform which is at 0,0,0) ──
            _col.offset = new Vector2(cx, cy);
            _col.size = new Vector2(worldW, worldH + _surfacePadding);
            _col.enabled = true;

            // Top surface Y — handy for snapping pet spawn position
            SurfaceWorldY = worldTR.y + _surfacePadding;

            _isReady = true;

            Debug.Log($"[TaskbarCollider] Applied — edge={_detector.taskbarEdge}  " +
                      $"offset={_col.offset}  size={_col.size}  surfaceY={SurfaceWorldY:F3}");
        }

        // ────────────────────────────────────────────────────────────
        //  Private helpers
        // ────────────────────────────────────────────────────────────

        // Converts a Win32-style screen pixel (already Y-flipped) to world space.
        // z=0 because we're 2D; camera distance doesn't matter for orthographic.
        private Vector3 ScreenPixelToWorld(float screenX, float screenY)
        {
            // ScreenToWorldPoint needs z = distance from camera for perspective,
            // but for orthographic z just needs to be != 0 to place in the scene plane.
            Vector3 screenPos = new Vector3(screenX, screenY, Mathf.Abs(_worldCamera.transform.position.z));
            return _worldCamera.ScreenToWorldPoint(screenPos);
        }

        // ────────────────────────────────────────────────────────────
        //  Gizmos
        // ────────────────────────────────────────────────────────────
#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!_isReady || _col == null) return;

            // Filled slab
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.18f);
            Gizmos.DrawCube((Vector3)(Vector2)_col.offset, (Vector3)(Vector2)_col.size);

            // Outline
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.8f);
            Gizmos.DrawWireCube((Vector3)(Vector2)_col.offset, (Vector3)(Vector2)_col.size);

            // Surface line
            Gizmos.color = Color.yellow;
            float hw = _col.size.x * 0.5f;
            float ox = _col.offset.x;
            Gizmos.DrawLine(new Vector3(ox - hw, SurfaceWorldY), new Vector3(ox + hw, SurfaceWorldY));
        }
#endif
    }
}