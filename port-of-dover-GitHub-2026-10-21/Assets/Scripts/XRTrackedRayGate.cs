using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>Hide the pointing ray when the headset or controller loses tracking.</summary>
[DisallowMultipleComponent]
public sealed class XRTrackedRayGate : MonoBehaviour
{
    [SerializeField] private ActionBasedController controller;
    [SerializeField] private XRRayInteractor ray;
    [SerializeField] private XRInteractorLineVisual line;
    [SerializeField] private InputActionProperty headTracked;

    private void OnEnable() => SetVisible(false);

    private void Update()
    {
        bool tracked = headTracked.action != null && headTracked.action.ReadValue<float>() > 0.5f
            && controller.isTrackedAction.action != null
            && controller.isTrackedAction.action.ReadValue<float>() > 0.5f
            && controller.trackingStateAction.action != null
            && (controller.trackingStateAction.action.ReadValue<int>() & 3) == 3;
        SetVisible(tracked);
    }

    private void OnDisable() => SetVisible(false);

    private void SetVisible(bool visible)
    {
        if (ray != null) ray.enabled = visible;
        if (line != null) line.enabled = visible;
        if (!visible && TryGetComponent<LineRenderer>(out var renderer)) renderer.enabled = false;
    }
}
