using UnityEngine;

/// <summary>Desktop prototype: follows direct-child waypoints, without vehicle physics.</summary>
public class VehicleRouteFollower : MonoBehaviour
{
    [SerializeField] private Transform route;
    [SerializeField, Min(0)] private float speed = 10f;
    [SerializeField, Min(0)] private float rotationSpeed = 6f;
    [SerializeField, Min(0.01f)] private float waypointReachDistance = 2f;
    [SerializeField, Min(0)] private float groundOffset = 0.05f;
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField, Min(1)] private float rayHeight = 50f;
    [SerializeField, Min(1)] private float rayDistance = 200f;
    [SerializeField, Min(0.1f)] private float heightAdjustmentSpeed = 8f;

    private Transform[] waypoints;
    private float[] cumulativeDistances;
    private bool externallyDriven;
    public Transform AssignedRoute => route;
    public float RouteProgress
    {
        get
        {
            if (waypoints == null) return 0;
            int end = Mathf.Clamp(CurrentWaypointIndex, 1, waypoints.Length - 1);
            Vector3 segment = Vector3.ProjectOnPlane(waypoints[end].position - waypoints[end - 1].position, Vector3.up);
            float length = segment.magnitude;
            float projected = length > 0 ? Vector3.Dot(transform.position - waypoints[end - 1].position, segment / length) : 0;
            return cumulativeDistances[end - 1] + Mathf.Clamp(projected, 0, length);
        }
    }

    // Explicit initialization prevents Unity Start from resetting a manager-owned vehicle.
    public void Initialize(Transform assignedRoute, float initialSpeed, LayerMask surfaceLayers)
    {
        route = assignedRoute;
        Speed = initialSpeed;
        groundLayers = surfaceLayers;
        externallyDriven = true;
        ResetToStart();
    }
    public int CurrentWaypointIndex { get; private set; } = 1;
    public bool IsMoving { get; private set; }
    public bool IsFinished { get; private set; }
    public float Speed { get => speed; set => speed = Mathf.Max(0, value); }

    private void Start()
    {
        if (externallyDriven) return;
        ResetToStart();
        if (playOnStart) StartMovement();
    }

    [ContextMenu("Reset To Start")]
    public void ResetToStart()
    {
        if (!Application.isPlaying) return;
        IsMoving = false;
        IsFinished = false;
        CurrentWaypointIndex = 1;
        if (route == null || route.childCount < 2)
        {
            waypoints = null;
            Debug.LogError("VehicleRouteFollower requires an assigned route with at least two direct children.", this);
            return;
        }
        waypoints = new Transform[route.childCount];
        for (int i = 0; i < waypoints.Length; i++) waypoints[i] = route.GetChild(i);
        cumulativeDistances = new float[waypoints.Length];
        for (int i = 1; i < waypoints.Length; i++)
            cumulativeDistances[i] = cumulativeDistances[i - 1] + Vector3.ProjectOnPlane(
                waypoints[i].position - waypoints[i - 1].position, Vector3.up).magnitude;
        transform.position = waypoints[0].position;
        // Stable upright yaw replaces the static instance's terrain-aligned pitch/roll.
        transform.rotation = Quaternion.identity;
        for (int i = 1; i < waypoints.Length; i++)
        {
            Vector3 forward = Vector3.ProjectOnPlane(waypoints[i].position - transform.position, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f) continue;
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            break;
        }
        AdaptHeight(true);
    }

    [ContextMenu("Start Movement")]
    public void StartMovement()
    {
        if (Application.isPlaying && waypoints != null && !IsFinished) IsMoving = true;
    }

    [ContextMenu("Stop Movement")]
    public void StopMovement() => IsMoving = false;

    private void Update()
    {
        if (!externallyDriven) TickMovement(Time.deltaTime, float.PositiveInfinity);
    }

    // The manager supplies a route-distance budget after applying predecessor and hold constraints.
    public void TickMovement(float deltaTime, float maximumTravel)
    {
        if (waypoints == null || deltaTime <= 0) return;
        if (IsMoving)
        {
            float remaining = Mathf.Min(Mathf.Max(0, speed) * deltaTime, Mathf.Max(0, maximumTravel));
            // Carry the travel budget across short segments; always visit indices in order.
            while (IsMoving)
            {
                Transform target = waypoints[CurrentWaypointIndex];
                if (target == null) { StopMovement(); return; }
                Vector3 direction = Vector3.ProjectOnPlane(target.position - transform.position, Vector3.up);
                float distance = direction.magnitude;
                if (distance <= waypointReachDistance)
                {
                    if (CurrentWaypointIndex == waypoints.Length - 1)
                    {
                        IsFinished = true;
                        IsMoving = false;
                        break;
                    }
                    // Managed vehicles consume the remaining distance to a corner before switching
                    // segments, so the reach tolerance cannot bypass a spacing or processing cap.
                    if (!externallyDriven || distance <= 0.00001f)
                    {
                        CurrentWaypointIndex++;
                        continue;
                    }
                }
                if (remaining <= 0) break;
                float travel = Mathf.Min(remaining, distance);
                transform.position += direction / distance * travel;
                remaining -= travel;
                Quaternion desired = Quaternion.LookRotation(direction, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, desired,
                    1f - Mathf.Exp(-Mathf.Max(0, rotationSpeed) * deltaTime));
                if (travel < distance) break;
            }
        }
        AdaptHeight(false, deltaTime);
    }

    private void AdaptHeight(bool immediate, float deltaTime = 0)
    {
        // Route height only places the ray above the surface; a miss preserves current Y.
        float referenceY = transform.position.y;
        if (waypoints[CurrentWaypointIndex] != null)
            referenceY = Mathf.Max(referenceY, waypoints[CurrentWaypointIndex].position.y);
        Vector3 origin = new Vector3(transform.position.x, referenceY + rayHeight, transform.position.z);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, rayDistance,
            groundLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        float groundY = 0;
        foreach (RaycastHit hit in hits)
        {
            // Primitive child colliders must never be mistaken for the ground.
            if (hit.collider.transform.IsChildOf(transform) || hit.normal.y < 0.4f || hit.distance >= nearest) continue;
            nearest = hit.distance;
            groundY = hit.point.y + groundOffset;
        }
        if (float.IsPositiveInfinity(nearest)) return;
        Vector3 position = transform.position;
        position.y = immediate ? groundY : Mathf.MoveTowards(position.y, groundY,
            heightAdjustmentSpeed * deltaTime);
        transform.position = position;
    }
}
