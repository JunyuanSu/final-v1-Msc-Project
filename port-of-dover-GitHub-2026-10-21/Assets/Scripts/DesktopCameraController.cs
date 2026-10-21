using UnityEngine;

/// <summary>Bounded desktop scroll zoom and horizontal WASD pan from the initial camera pose.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class DesktopCameraController : MonoBehaviour
{
    [SerializeField, Min(0f), Tooltip("Metres per unit of mouse wheel input.")]
    private float zoomSpeed = 20f;
    [SerializeField, Min(0.01f)] private float smoothTime = 0.2f;
    [SerializeField, Tooltip("Maximum backward travel from the default position; must be <= 0.")]
    private float minZoomOffset = -150f;
    [SerializeField, Tooltip("Maximum forward travel from the default position; must be >= 0.")]
    private float maxZoomOffset = 200f;

    [SerializeField, Min(0f), Tooltip("Horizontal WASD movement speed in metres per second.")]
    private float panSpeed = 60f;
    [SerializeField, Min(0f), Tooltip("Maximum horizontal pan offset from the default view, excluding zoom, in metres.")]
    private float maxPanDistance = 200f;

    public Vector3 DefaultPosition { get; private set; }
    public Quaternion DefaultRotation { get; private set; }
    private Vector3 defaultForward;
    private Vector3 panForward;
    private Vector3 panRight;
    private Vector3 panOffset;
    private float targetOffset;
    private float currentOffset;
    private float zoomVelocity;

    private void Awake()
    {
        ValidateSettings();
        DefaultPosition = transform.position;
        DefaultRotation = transform.rotation;
        defaultForward = DefaultRotation * Vector3.forward;
        panForward = Vector3.ProjectOnPlane(defaultForward, Vector3.up);
        // Retain a usable horizontal direction even for an exactly vertical camera.
        if (panForward.sqrMagnitude < 0.0001f)
            panForward = Vector3.ProjectOnPlane(DefaultRotation * Vector3.up, Vector3.up);
        panForward.Normalize();
        panRight = Vector3.Cross(Vector3.up, panForward).normalized;
        panOffset = Vector3.zero;
        targetOffset = currentOffset = zoomVelocity = 0f;
    }

    private void Update()
    {
        if (Application.isFocused && Input.GetKeyDown(KeyCode.R))
        {
            ResetView();
            // Reset takes priority over all other movement input this frame.
            return;
        }

        float scroll = Application.isFocused ? Input.mouseScrollDelta.y : 0f;
        targetOffset = Mathf.Clamp(targetOffset + scroll * zoomSpeed, minZoomOffset, maxZoomOffset);
        currentOffset = Mathf.SmoothDamp(currentOffset, targetOffset, ref zoomVelocity,
            smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
        currentOffset = Mathf.Clamp(currentOffset, minZoomOffset, maxZoomOffset);

        Vector2 panInput = Vector2.zero;
        if (Application.isFocused)
        {
            panInput.x = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            panInput.y = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
        }
        panInput = Vector2.ClampMagnitude(panInput, 1f);
        panOffset += (panRight * panInput.x + panForward * panInput.y) * panSpeed * Time.deltaTime;
        panOffset.y = 0f;
        panOffset = Vector3.ClampMagnitude(panOffset, maxPanDistance);
        transform.position = DefaultPosition + panOffset + defaultForward * currentOffset;
    }

    private void ResetView()
    {
        panOffset = Vector3.zero;
        targetOffset = currentOffset = zoomVelocity = 0f;
        transform.SetPositionAndRotation(DefaultPosition, DefaultRotation);
    }

    private void OnDisable() => zoomVelocity = 0f;
    private void OnValidate() => ValidateSettings();

    private void ValidateSettings()
    {
        zoomSpeed = Finite(zoomSpeed) ? Mathf.Max(0f, zoomSpeed) : 20f;
        smoothTime = Finite(smoothTime) ? Mathf.Max(0.01f, smoothTime) : 0.2f;
        minZoomOffset = Finite(minZoomOffset) ? Mathf.Min(0f, minZoomOffset) : -150f;
        maxZoomOffset = Finite(maxZoomOffset) ? Mathf.Max(0f, maxZoomOffset) : 200f;
        panSpeed = Finite(panSpeed) ? Mathf.Max(0f, panSpeed) : 60f;
        maxPanDistance = Finite(maxPanDistance) ? Mathf.Max(0f, maxPanDistance) : 200f;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
