// ============================================================
//  BasicUsageSample.cs  (Samples~/BasicUsage)
//
//  Shows the minimal wiring needed to use WindowController
//  from your own gameplay scripts.
//
//  How to use:
//    1. Import the "Basic Usage" sample via Package Manager.
//    2. Open the BasicUsage scene.
//    3. Press Play — F1/F2/F3 hotkeys are active on Windows builds.
// ============================================================

using UnityEngine;
using DesktopOverlay.Window;

/// <summary>
/// A bare-bones consumer of WindowController.
/// In a real project this would be your GameManager, UIManager, etc.
/// </summary>
public sealed class BasicUsageSample : MonoBehaviour
{
    [SerializeField] private WindowController _windowController;

    private void Start()
    {
        if (_windowController == null)
        {
            Debug.LogError("[BasicUsageSample] Assign the WindowController in the Inspector.");
            return;
        }

        // Example: enter interactive mode 3 seconds after launch.
        Invoke(nameof(GoInteractive), 3f);
    }

    private void GoInteractive()
    {
        Debug.Log("[BasicUsageSample] Entering interactive mode.");
        _windowController.EnterInteractiveMode();
    }

    // Called by a UI Button's OnClick event (wire up in the Inspector).
    public void OnPassiveButtonClicked()
    {
        _windowController.EnterPassiveMode();
    }

    // Called by a UI Button's OnClick event.
    public void OnInteractiveButtonClicked()
    {
        _windowController.EnterInteractiveMode();
    }
}