// ============================================================
//  WindowControllerEditor.cs
//  Custom Inspector for WindowController.
//
//  Adds runtime control buttons in the Inspector so you can
//  trigger mode changes without entering Play+keyboard hotkeys.
// ============================================================

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace DesktopOverlay.Window.Editor
{
    [CustomEditor(typeof(WindowController))]
    public sealed class WindowControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var controller = (WindowController)target;

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Runtime controls are only available in Play Mode.\n" +
                    "Window service is disabled in the Editor.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Runtime Controls", EditorStyles.boldLabel);

            if (controller.Service == null)
            {
                EditorGUILayout.HelpBox(
                    "No IWindowService is active (unsupported platform or Editor mode).",
                    MessageType.Warning);
                return;
            }

            // ── State display ─────────────────────────────────────────
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Toggle("Is Transparent", controller.IsTransparent);
                EditorGUILayout.Toggle("Is Click-Through", controller.IsClickThrough);
                EditorGUILayout.Toggle("Is Always-On-Top", controller.IsAlwaysOnTop);
            }

            EditorGUILayout.Space(4);

            // ── Mode buttons ──────────────────────────────────────────
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Enter Interactive"))
                    controller.EnterInteractiveMode();

                if (GUILayout.Button("Enter Passive"))
                    controller.EnterPassiveMode();
            }

            // ── Individual toggles ────────────────────────────────────
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Toggle Transparent"))
                    controller.Service.SetTransparent(!controller.IsTransparent);

                if (GUILayout.Button("Toggle Always-On-Top"))
                    controller.Service.SetAlwaysOnTop(!controller.IsAlwaysOnTop);
            }

            // Repaint each frame so toggles stay up to date.
            if (Application.isPlaying)
                Repaint();
        }
    }
}
#endif