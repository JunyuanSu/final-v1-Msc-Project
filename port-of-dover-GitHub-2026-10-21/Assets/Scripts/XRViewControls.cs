using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Bounded observation dolly/pan. Never writes tracked camera/controller poses.</summary>
[DefaultExecutionOrder(-50)]
public sealed class XRViewControls : MonoBehaviour
{
    [SerializeField] private Camera head;
    [SerializeField] private InputActionAsset actions;
    [SerializeField] private Transform terrain;
    [SerializeField] private Transform buildings;
    [SerializeField, Min(0)] private float zoomSpeed = 15;
    [SerializeField, Min(0)] private float panSpeed = 15;
    [SerializeField, Range(0, .95f)] private float deadZone = .2f;
    [SerializeField, Min(.01f)] private float smoothTime = .25f;
    [SerializeField] private float minZoom = -80;
    [SerializeField] private float maxZoom = 120;
    [SerializeField, Min(0)] private float maxPan = 80;
    [SerializeField, Min(0)] private float horizontalBoundary = 140;
    [SerializeField, Min(0)] private float maxDescent = 80;
    [SerializeField, Min(0)] private float maxAscent = 60;
    [SerializeField, Min(1)] private float surfaceClearance = 20;
    [SerializeField, Range(10, 60)] private float zoomDownAngle = 35;
    private InputAction pan, zoom, reset, headTracked, leftTracked, rightTracked;
    private Vector3 initialPosition, initialForward, zoomAxis, panOffset, velocity;
    private Quaternion initialYaw;
    private float zoomOffset;
    public Vector3 InitialPosition => initialPosition;
    public float SuccessfulPanDistance { get; private set; }
    public float SuccessfulZoomDistance { get; private set; }

    private void Awake()
    {
        initialPosition = transform.position;
        initialYaw = Quaternion.Euler(0, transform.eulerAngles.y, 0);
        initialForward = initialYaw * Vector3.forward;
        float angle = zoomDownAngle * Mathf.Deg2Rad;
        zoomAxis = initialForward * Mathf.Cos(angle) - Vector3.up * Mathf.Sin(angle);
        if (actions == null) return;
        pan = actions.FindAction("Observation/ViewPan", false);
        zoom = actions.FindAction("Observation/ViewZoom", false);
        reset = actions.FindAction("Observation/ViewReset", false);
        headTracked = actions.FindAction("Observation/HeadTracked", false);
        leftTracked = actions.FindAction("Observation/LeftTracked", false);
        rightTracked = actions.FindAction("Observation/RightTracked", false);
        // Existing InputActionManager owns action enable/disable; do not disable shared actions.
    }

    private void Update()
    {
        if (head == null || !Tracked(headTracked)) { velocity = Vector3.zero; return; }
        if (Tracked(leftTracked) && reset != null && reset.WasPressedThisFrame()) { ResetView(); return; }
        Vector2 p = Tracked(leftTracked) && pan != null ? pan.ReadValue<Vector2>() : Vector2.zero;
        float z = Tracked(rightTracked) && zoom != null ? zoom.ReadValue<Vector2>().y : 0;
        Step(p, z, head.transform.forward, Mathf.Min(Time.unscaledDeltaTime, .05f));
    }

    private static bool Tracked(InputAction action) => action != null && action.enabled && action.ReadValue<float>() > .5f;

    // InputActionManager keeps these shared actions enabled while Overview movement is disabled.
    public bool ReadStreetInput(out Vector2 move, out bool resetPressed)
    {
        move = Vector2.zero; resetPressed = false;
        if (head == null || !Tracked(headTracked) || !Tracked(leftTracked)) return false;
        move = pan != null && pan.enabled ? pan.ReadValue<Vector2>() : Vector2.zero;
        resetPressed = reset != null && reset.enabled && reset.WasPressedThisFrame();
        return true;
    }

    public Vector2 ApplyDeadZone(Vector2 value)
    {
        float magnitude = value.magnitude;
        return magnitude <= deadZone ? Vector2.zero : value.normalized * Mathf.Clamp01((magnitude - deadZone) / (1 - deadZone));
    }

    public void Step(Vector2 panInput, float zoomInput, Vector3 gazeForward, float dt)
    {
        if (dt <= 0 || head == null) return;
        Vector3 forward = Vector3.ProjectOnPlane(gazeForward, Vector3.up);
        forward = forward.sqrMagnitude > .001f ? forward.normalized : initialForward;
        Vector2 input = ApplyDeadZone(panInput);
        float zoomValue = ApplyDeadZone(new Vector2(0, zoomInput)).y;
        Vector3 oldPan = panOffset;
        float oldZoom = zoomOffset;
        panOffset = Vector3.ClampMagnitude(panOffset + (forward * input.y + Vector3.Cross(Vector3.up, forward) * input.x) * panSpeed * dt, maxPan);
        zoomOffset = Mathf.Clamp(zoomOffset + zoomValue * zoomSpeed * dt, minZoom, maxZoom);
        Vector3 target = initialPosition + panOffset + zoomAxis * zoomOffset;
        Vector3 horizontal = Vector3.ProjectOnPlane(target - initialPosition, Vector3.up);
        if (horizontal.magnitude > horizontalBoundary || target.y < initialPosition.y - maxDescent || target.y > initialPosition.y + maxAscent)
        { panOffset = oldPan; zoomOffset = oldZoom; target = initialPosition + panOffset + zoomAxis * zoomOffset; }
        Vector3 next = Vector3.SmoothDamp(transform.position, target, ref velocity, smoothTime, Mathf.Infinity, dt);
        if (!SafePosition(next))
        {
            // No accumulating hidden travel while blocked or while Cesium colliders are absent.
            panOffset = oldPan; zoomOffset = oldZoom; velocity = Vector3.zero;
            return;
        }
        float actualTravel=Vector3.Distance(transform.position,next);
        if(input.sqrMagnitude>0 && Mathf.Abs(zoomValue)<.001f)SuccessfulPanDistance+=actualTravel;
        if(Mathf.Abs(zoomValue)>.001f && input.sqrMagnitude<.001f)SuccessfulZoomDistance+=actualTravel;
        transform.position = next;
    }

    private bool MapCollider(Collider collider) => collider != null &&
        ((terrain != null && collider.transform.IsChildOf(terrain)) || (buildings != null && collider.transform.IsChildOf(buildings)));

    private bool SafePosition(Vector3 rigPosition)
    {
        Vector3 eye = head.transform.position + rigPosition - transform.position;
        Vector3 origin = new Vector3(eye.x, initialPosition.y + 1000, eye.z);
        bool terrainFound = false;
        foreach (var hit in Physics.RaycastAll(origin, Vector3.down, 3000, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!MapCollider(hit.collider)) continue;
            if (terrain != null && hit.collider.transform.IsChildOf(terrain)) terrainFound = true;
            if (eye.y < hit.point.y + surfaceClearance) return false;
        }
        if (!terrainFound) return false;
        Vector3 travel = eye - head.transform.position;
        if (travel.sqrMagnitude > .000001f)
            foreach (var hit in Physics.SphereCastAll(head.transform.position, 1, travel.normalized, travel.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (MapCollider(hit.collider)) return false;
        foreach (var collider in Physics.OverlapSphere(eye, 1, ~0, QueryTriggerInteraction.Ignore))
            if (MapCollider(collider)) return false;
        return true;
    }

    public void ResetView()
    {
        panOffset = velocity = Vector3.zero;
        zoomOffset = 0;
        transform.SetPositionAndRotation(initialPosition, initialYaw);
    }

    private void OnDisable() { velocity = Vector3.zero; }
    private void OnValidate()
    {
        deadZone = Mathf.Clamp(deadZone, 0, .95f);
        smoothTime = Mathf.Max(.01f, smoothTime);
        zoomSpeed = Mathf.Max(0, zoomSpeed); panSpeed = Mathf.Max(0, panSpeed);
        minZoom = Mathf.Min(0, minZoom); maxZoom = Mathf.Max(0, maxZoom);
        maxPan = Mathf.Max(0, maxPan); horizontalBoundary = Mathf.Max(0, horizontalBoundary);
        maxDescent = Mathf.Max(0, maxDescent); maxAscent = Mathf.Max(0, maxAscent);
        surfaceClearance = Mathf.Max(1, surfaceClearance);
    }
}
