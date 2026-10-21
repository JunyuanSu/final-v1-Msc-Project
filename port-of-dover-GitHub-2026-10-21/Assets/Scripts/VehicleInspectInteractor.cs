using UnityEngine;
using UnityEngine.UI;

/// <summary>Read-only vehicle inspection. Trigger dispatch belongs to CongestionLabelInteractor.</summary>
[DefaultExecutionOrder(110)]
public sealed class VehicleInspectInteractor : MonoBehaviour
{
    [SerializeField] private Transform routeA;
    [SerializeField] private Transform routeB;
    private VehicleRouteFollower selected;
    private Transform selectedRoute;
    private VehicleRouteFollower hovered;
    private LineRenderer hoverRing, selectedRing;
    public VehicleRouteFollower Hovered => hovered;
    private bool Valid(VehicleRouteFollower v) => SelectionAllowed && v != null && v.isActiveAndEnabled && !v.IsFinished && v.AssignedRoute != null && (v.AssignedRoute == routeA || v.AssignedRoute == routeB);
    public void SetHover(Collider hit)
    {
        var v = hit != null ? hit.GetComponentInParent<VehicleRouteFollower>() : null;
        hovered = Valid(v) && v != selected ? v : null;
        ShowRing(ref hoverRing, hovered, false);
    }
    private void ShowRing(ref LineRenderer ring, VehicleRouteFollower vehicle, bool selection)
    {
        if (vehicle == null) { if (ring != null) ring.gameObject.SetActive(false); return; }
        if (ring == null)
        {
            var labels = GetComponent<CongestionLabelInteractor>();
            ring = InteractionFeedbackVisuals.Ring(selection ? "Selected HGV" : "Hovered HGV", labels != null ? labels.FeedbackMaterial : null,
                selection ? new Color(0, 1, 1) : new Color(.55f, .85f, 1, .55f), 1, selection ? .3f : .16f);
            ring.transform.localScale = new Vector3(2, 1, 7);
        }
        ring.transform.SetPositionAndRotation(vehicle.transform.position, Quaternion.Euler(0, vehicle.transform.eulerAngles.y, 0));
        ring.gameObject.SetActive(true);
    }
    private void Notify(string message) { var labels = GetComponent<CongestionLabelInteractor>(); if (labels != null) labels.Notify(message); }
    private GameObject panel;
    private Text text;
    private float previousProgress;
    private bool hasSample;
    public VehicleRouteFollower Selected => selected;
    public float CurrentSpeed { get; private set; }
    public string DisplayText => text != null ? text.text : "";
    public bool PanelVisible => panel != null && panel.activeSelf;
    public bool SelectionAllowed { get; set; } = true;

    // Returns true only for vehicle hits, so the same press cannot also label a road.
    public bool HandleHit(Collider hit)
    {
        var vehicle = hit != null ? hit.GetComponentInParent<VehicleRouteFollower>() : null;
        if (!SelectionAllowed) { ClearSelection(); return vehicle != null; }
        if (vehicle == null) { ClearSelection(); return false; }
        if (vehicle == selected) { ClearSelection(); return true; }
        ClearSelection();
        if (!vehicle.isActiveAndEnabled || vehicle.IsFinished ||
            (vehicle.AssignedRoute != routeA && vehicle.AssignedRoute != routeB) || vehicle.AssignedRoute == null)
            return true;
        selected = vehicle;
        selectedRoute = vehicle.AssignedRoute;
        previousProgress = vehicle.RouteProgress;
        EnsurePanel();
        var camera = Camera.main;
        if (camera != null)
        {
            // Fixed world-space panel beside the view; not attached to the moving truck or head.
            panel.transform.SetPositionAndRotation(camera.transform.TransformPoint(new Vector3(.65f, -.35f, 1.6f)),
                camera.transform.rotation);
            panel.SetActive(true);
        }
        RefreshText();
        ShowRing(ref selectedRing, selected, true);
        SetHover(null);
        Notify("Selected: " + (selectedRoute == routeA ? "A / " : "B / ") + selected.name);
        return true;
    }

    private void LateUpdate()
    {
        UpdateInformation(Time.deltaTime);
        if (!Valid(hovered)) hovered = null;
        ShowRing(ref hoverRing, hovered, false);
    }

    public void UpdateInformation(float deltaTime)
    {
        if (!SelectionAllowed || selected == null || !selected.isActiveAndEnabled || selected.IsFinished || selected.AssignedRoute != selectedRoute)
        { ClearSelection(); return; }
        ShowRing(ref selectedRing, selected, true);
        float progress = selected.RouteProgress;
        // A reused instance returning to the route start is no longer the same journey.
        if (progress < previousProgress - .01f) { ClearSelection(); return; }
        CurrentSpeed = deltaTime > 0 ? Mathf.Max(0, progress - previousProgress) / deltaTime : 0;
        previousProgress = progress;
        hasSample = true;
        RefreshText();
    }

    private void RefreshText()
    {
        string route = selectedRoute == routeA ? "A" : "B";
        // Spawn names are unique within a direction; prefix the route to disambiguate A/B.
        text.text = "Vehicle ID: " + route + " / " + selected.name + "\nRoute: " + route +
            "\nCurrent Speed: " + (hasSample ? (CurrentSpeed * 3.6f).ToString("F1") + " km/h" : "--") +
            "\n\nTrigger same vehicle to close";
    }

    public void ClearSelection()
    {
        if (selectedRoute != null) Notify("Selection Cleared");
        if (selectedRing != null) selectedRing.gameObject.SetActive(false);
        selected = null;
        selectedRoute = null;
        hasSample = false;
        CurrentSpeed = 0;
        if (text != null) text.text = "";
        if (panel != null) panel.SetActive(false);
    }

    private void EnsurePanel()
    {
        if (panel != null) return;
        panel = new GameObject("HGV Inspection", typeof(RectTransform), typeof(Canvas), typeof(Image));
        var viewControls = GetComponentInParent<XRViewControls>();
        if (viewControls != null) panel.transform.SetParent(viewControls.transform, true);
        panel.SetActive(false);
        var canvas = panel.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rect = panel.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(520, 230);
        rect.localScale = Vector3.one * .0014f;
        var background = panel.GetComponent<Image>();
        background.color = new Color(.025f, .04f, .07f, .94f);
        background.raycastTarget = false;
        var content = new GameObject("Information", typeof(RectTransform), typeof(Text));
        content.transform.SetParent(panel.transform, false);
        var contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = Vector2.zero;
        contentRect.anchorMax = Vector2.one;
        contentRect.offsetMin = new Vector2(20, 16);
        contentRect.offsetMax = new Vector2(-20, -16);
        text = content.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 26;
        text.color = Color.white;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        // No collider or GraphicRaycaster: panel cannot intercept road/vehicle selection.
    }

    private void OnDisable() { ClearSelection(); SetHover(null); }
    private void OnDestroy()
    {
        if (hoverRing != null) InteractionFeedbackVisuals.Remove(hoverRing.gameObject);
        if (selectedRing != null) InteractionFeedbackVisuals.Remove(selectedRing.gameObject);
        if (panel == null) return;
        if (Application.isPlaying) Destroy(panel); else DestroyImmediate(panel);
    }
}

