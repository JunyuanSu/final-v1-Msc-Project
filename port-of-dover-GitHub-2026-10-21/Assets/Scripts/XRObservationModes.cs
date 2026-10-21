using System.Collections;
using UnityEngine;

/// <summary>Bounded roadside observation of a selected hypothesis marker; never writes tracked camera poses.</summary>
[DefaultExecutionOrder(95)]
public sealed class XRObservationModes : MonoBehaviour
{
    [SerializeField] private Camera head;
    [SerializeField] private XRViewControls overviewControls;
    [SerializeField] private IdentificationTrialController trial;
    [SerializeField] private VehicleInspectInteractor inspection;
    [SerializeField] private CongestionLabelInteractor labels;
    [SerializeField] private Transform terrain, routeA, routeB;
    [Header("Overview route identity ribbons")]
    [SerializeField, Min(.05f)] private float overviewRouteWidth=.8f;
    [SerializeField, Range(0,1)] private float overviewRouteOpacity=.75f;
    [SerializeField, Range(.02f,.2f)] private float overviewRouteLift=.08f;
    public float OverviewRouteWidth=>overviewRouteWidth;
    public float OverviewRouteOpacity=>overviewRouteOpacity;
    public float OverviewRouteLift=>overviewRouteLift;
    private GameObject routeVisuals;
    private void Start()
    {
        // Keep visuals outside both waypoint hierarchies and the moving XR rig.
        routeVisuals=new GameObject("Overview route visualisation");
        routeVisuals.AddComponent<OverviewRouteVisuals>().Initialize(this,routeA,routeB,terrain);
    }
    [SerializeField] private Shader fadeShader;
    [SerializeField, Min(.5f)] private float eyeHeight = 1.65f;
    [SerializeField, Min(4)] private float routeClearance = 8;
    [SerializeField, Min(.1f)] private float bodyRadius = .5f;
    [SerializeField, Range(0,30)] private float maxSlope = 12;
    [SerializeField, Min(.1f)] private float fadeSeconds = .3f;
    public bool IsStreet { get; private set; }
    public bool IsTransitioning { get; private set; }
    public string Status { get; private set; } = "Overview";
    // Logging observes successful, visible street visits; no movement or input ownership changes.
    public event System.Action<bool> StreetObservationChanged;
    private bool visitOpen;
    private void SetVisitOpen(bool open)
    {
        if(visitOpen==open)return;
        visitOpen=open; StreetObservationChanged?.Invoke(open);
    }
    private Vector3 savedPosition;
    private Quaternion savedRotation;
    private bool controlsWereEnabled, hasSavedPose;
    private Coroutine transition;
    private Material fadeMaterial;
    private GameObject fadeObject;
    private Mesh fadeMesh;
    private long enteredTrial;
    private bool returning;
    public bool IsPreparing { get; private set; }
    [SerializeField, Min(.5f)] private float preparationTimeout = 3f;
    private bool missingTerrain;
    private CongestionLabelInteractor.Label preparingMarker;
    [SerializeField, Min(8)] private float sideDistance = 12;
    private Vector3 lockedSample, lockedLook;
    private bool destinationLocked;
    [Header("Street-level movement")]
    [SerializeField, Min(0)] private float streetMoveSpeed = 1f;
    [SerializeField, Range(0, 8)] private float streetMaximumRadius = 8f;
    [SerializeField, Range(0, .95f)] private float streetDeadzone = .2f;
    private Vector3 streetCenter;
    private float streetHeadOffsetY;
    private bool streetMovementReady;
    private bool streetInputArmed;
    private static readonly Vector3[] SupportOffsets = { Vector3.zero, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };

    private void Update()
    {
        if (!IsStreet || IsTransitioning || !streetMovementReady || !ExplorationStage ||
            trial.TrialNumber != enteredTrial || overviewControls == null || head == null) return;
        if (!overviewControls.ReadStreetInput(out var input, out bool resetPressed)) return;
        if (resetPressed) { ResetStreetPosition(); return; }
        float magnitude = Mathf.Min(input.magnitude, 1);
        float deadzone = Mathf.Clamp(streetDeadzone, 0, .95f);
        if (magnitude <= deadzone) { streetInputArmed=true; return; }
        if (!streetInputArmed) return; // A held Overview stick must return to neutral after teleport/reset.
        input = input.normalized * ((magnitude - deadzone) / (1 - deadzone));
        Vector3 forward = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < .001f) forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        float distance = Mathf.Max(0, streetMoveSpeed) * Mathf.Min(Time.unscaledDeltaTime, .05f);
        // Short substeps prevent skipping narrow unsafe terrain/route regions even at low frame rates.
        Vector3 travel = (forward * input.y + right * input.x) * Mathf.Min(distance, .2f);
        int steps = Mathf.Max(1, Mathf.CeilToInt(travel.magnitude / .05f));
        travel /= steps;
        for (int i = 0; i < steps; i++)
        {
            if (TryStreetMove(travel)) continue;
            // Slide using the same full checks, never accumulate blocked movement.
            Vector3 along = forward * Vector3.Dot(travel, forward);
            Vector3 across = right * Vector3.Dot(travel, right);
            if (along.sqrMagnitude >= across.sqrMagnitude) { if (!TryStreetMove(along)) TryStreetMove(across); }
            else { if (!TryStreetMove(across)) TryStreetMove(along); }
        }
    }

    private bool TryStreetMove(Vector3 travel)
    {
        if (travel.sqrMagnitude < .00000001f || terrain == null || routeA == null || routeB == null) return false;
        Vector3 current = head.transform.position;
        // Validate the occupied ground too: a missing/replaced Cesium tile stops movement immediately.
        if (!TryStandingGround(current, out var oldGround, out _)) return false;
        Vector3 offset = Vector3.ProjectOnPlane(current + travel - streetCenter, Vector3.up);
        offset = Vector3.ClampMagnitude(offset, Mathf.Clamp(streetMaximumRadius, 0, 8));
        Vector3 sample = streetCenter + offset; sample.y = oldGround.point.y;
        if (!TryStandingGround(sample, out var ground, out _)) return false;
        Vector3 horizontalTravel = Vector3.ProjectOnPlane(ground.point - current, Vector3.up);
        // No automatic catch-up after physically leaving the boundary; X can safely recenter.
        if (horizontalTravel.magnitude > travel.magnitude + .001f) return false;
        float rise = ground.point.y - oldGround.point.y;
        if (Mathf.Abs(rise) > .02f + horizontalTravel.magnitude * Mathf.Tan(maxSlope * Mathf.Deg2Rad)) return false;
        Vector3 delta = horizontalTravel + Vector3.up * rise;
        if (delta.sqrMagnitude < .00000001f) return false;
        foreach (var hit in Physics.CapsuleCastAll(oldGround.point + Vector3.up * (bodyRadius + .1f),
            oldGround.point + Vector3.up * eyeHeight, bodyRadius, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(transform)) return false;
        Vector3 position = transform.position + horizontalTravel;
        // Keep tracking's crouch/standing changes intact; only the rig follows the ground elevation.
        float desiredY = ground.point.y + eyeHeight - streetHeadOffsetY;
        if (Mathf.Abs(desiredY - transform.position.y) > .15f) return false;
        position.y = desiredY;
        SuccessfulStreetDistance += horizontalTravel.magnitude;
        transform.position = position;
        return true;
    }

    private void ResetStreetPosition()
    {
        streetInputArmed=false;
        if (!TryStandingGround(streetCenter, out var ground, out _) || Mathf.Abs(ground.point.y-streetCenter.y)>.25f)
        { Message("Viewpoint not ready. Please try again."); return; }
        Vector3 trackedOffset = head.transform.position - transform.position;
        Vector3 position = ground.point + Vector3.up * eyeHeight - trackedOffset;
        position.y = ground.point.y + eyeHeight - streetHeadOffsetY;
        transform.position = position; // Preserve natural head/rig heading; no artificial rotation.
    }
    public bool TutorialAccess { get; set; }
    public float SuccessfulStreetDistance { get; private set; }
    private CongestionLabelInteractor.Label ObservationMarker=>TutorialAccess?labels?.PracticeSelected:labels?.SelectedMarker;
    private bool ExplorationStage => trial != null && trial.isActiveAndEnabled &&
        (TutorialAccess || trial.Stage == IdentificationStage.ObserveIdentify || trial.Stage == IdentificationStage.SelectInspect);
    public bool CanEnter => !IsStreet && !IsTransitioning && ExplorationStage && labels != null && ObservationMarker != null;
    private void LateUpdate()
    {
        if ((IsStreet || IsTransitioning) && (!ExplorationStage || trial.TrialNumber != enteredTrial))
        {
            if(IsPreparing) { CancelPreparation(); return; }
            if (!returning && hasSavedPose) {
                if (transition != null) StopCoroutine(transition);
                transition = StartCoroutine(Switch(false));
            }
        }
    }
    public bool RequestToggle()
    {
        if(IsPreparing) { CancelPreparation(); return true; }
        if (IsTransitioning) return false;
        if (IsStreet) { transition = StartCoroutine(Switch(false)); return true; }
        if (!CanEnter) { Message("Select a Marker in Overview first"); return false; }
        destinationLocked=false;
        if (!TryGetStreetPose(out _, out _, out string reason)) {
            if(!missingTerrain) { Failure(reason,false); return false; }
            enteredTrial=trial.TrialNumber; preparingMarker=ObservationMarker;
            transition=StartCoroutine(Prepare()); return true;
        }
        if (!EnsureFade()) { Message("Street view unavailable: fade shader missing"); return false; }
        enteredTrial = trial.TrialNumber;
        transition = StartCoroutine(Switch(true)); return true;
    }
    public bool TryGetStreetPose(out Vector3 position, out Quaternion rotation, out string reason)
    {
        position=default; rotation=Quaternion.identity; reason="Observation references unavailable"; missingTerrain=false;
        if(head==null || terrain==null || routeA==null || routeB==null || routeA.childCount<2 || routeB.childCount<2) return false;
        if((transform.lossyScale-Vector3.one).sqrMagnitude>.0001f) {reason="XR rig must use real-world scale";return false;}
        if(destinationLocked) return ValidateSample(lockedSample,lockedLook,out position,out rotation,out reason);
        var marker=ObservationMarker;
        if(marker==null) {reason="Select a Marker first";return false;}
        if((marker.Route!=routeA && marker.Route!=routeB) || marker.Segment<0 || marker.Segment+1>=marker.Route.childCount) {reason="Marker route unavailable";return false;}
        Vector3 tangent=Vector3.ProjectOnPlane(marker.Route.GetChild(marker.Segment+1).position-marker.Route.GetChild(marker.Segment).position,Vector3.up);
        if(tangent.sqrMagnitude<.001f) {reason="Route direction unavailable";return false;}
        Vector3 side=Vector3.Cross(Vector3.up,tangent.normalized);
        string failures="";
        // Bounded search on both sides; never relax slope, route clearance or collision tests.
        for(int step=0;step<3;step++) for(int sign=-1;sign<=1;sign+=2) {
            var sample=marker.Position+side*sign*(Mathf.Max(sideDistance,routeClearance+bodyRadius)+step*6);
            if(ValidateSample(sample,marker.Position,out position,out rotation,out reason)) {
                lockedSample=sample;lockedLook=marker.Position;destinationLocked=true;return true;
            }
            failures+=(failures.Length>0?"; ":"")+(sign<0?"L":"R")+(step+1)+": "+reason;
        }
        reason="No safe roadside position: "+failures;return false;
    }
    private bool ValidateSample(Vector3 sample,Vector3 road,out Vector3 position,out Quaternion rotation,out string reason)
    {
        position=default;rotation=Quaternion.identity;reason="Ready";
        if (!TryStandingGround(sample, out var hit, out reason)) return false;
        Vector3 eye=hit.point+Vector3.up*eyeHeight;
        var look=road+Vector3.up*2;
        foreach(var obstacle in Physics.RaycastAll(eye,(look-eye).normalized,Vector3.Distance(eye,look),~0,QueryTriggerInteraction.Ignore))
            if(obstacle.collider.GetComponentInParent<VehicleRouteFollower>()==null && !obstacle.transform.IsChildOf(transform))
            {reason="road view obstructed";return false;}
        var direction=Vector3.ProjectOnPlane(look-eye,Vector3.up);
        if(direction.sqrMagnitude<.01f) {reason="observation heading unavailable";return false;}
        Quaternion delta=Quaternion.Euler(0,Mathf.DeltaAngle(head.transform.eulerAngles.y,Quaternion.LookRotation(direction).eulerAngles.y),0);
        rotation=delta*transform.rotation;
        position=eye-delta*(head.transform.position-transform.position);
        return true;
    }
    private bool TryStandingGround(Vector3 sample, out RaycastHit hit, out string reason)
    {
        reason="Ready"; hit=default;
        if (terrain==null || routeA==null || routeB==null || routeA.childCount<2 || routeB.childCount<2) return false;
        if(!Physics.Raycast(sample+Vector3.up*20,Vector3.down,out hit,40,~0,QueryTriggerInteraction.Ignore))
        {missingTerrain=true;reason="terrain collider unavailable";return false;}
        if(!hit.transform.IsChildOf(terrain)) {reason="ground obstructed";return false;}
        if(Vector3.Angle(hit.normal,Vector3.up)>maxSlope) {reason="ground too steep";return false;}
        if(DistanceToRoute(hit.point,routeA)<routeClearance || DistanceToRoute(hit.point,routeB)<routeClearance)
        {reason="too close to Route A/B";return false;}
        Vector3 eye=hit.point+Vector3.up*eyeHeight;
        foreach(var offset in SupportOffsets) {
            if(!Physics.Raycast(hit.point+offset*bodyRadius+Vector3.up*2,Vector3.down,out var support,4,~0,QueryTriggerInteraction.Ignore))
            {missingTerrain=true;reason="standing terrain collider unavailable";return false;}
            if(!support.transform.IsChildOf(terrain)||Mathf.Abs(support.point.y-hit.point.y)>.25f)
            {reason="standing area uneven or obstructed";return false;}
        }
        foreach(var c in Physics.OverlapCapsule(hit.point+Vector3.up*(bodyRadius+.1f),eye,bodyRadius,~0,QueryTriggerInteraction.Ignore))
            if(!c.transform.IsChildOf(transform)) {reason="standing space blocked by "+c.name;return false;}
        return true;
    }
    private static float DistanceToRoute(Vector3 point, Transform route)
    {
        float best=float.PositiveInfinity;point.y=0;
        for(int i=0;i+1<route.childCount;i++) {
            var a=route.GetChild(i).position;var b=route.GetChild(i+1).position;a.y=b.y=0;
            var edge=b-a;float t=edge.sqrMagnitude>.0001f?Mathf.Clamp01(Vector3.Dot(point-a,edge)/edge.sqrMagnitude):0;
            best=Mathf.Min(best,Vector3.Distance(point,a+t*edge));
        }return best;
    }
    private IEnumerator Switch(bool enter)
    {
        if(!enter)SetVisitOpen(false);
        IsTransitioning=true; returning=!enter;
        if (enter) { savedPosition=transform.position;savedRotation=transform.rotation;hasSavedPose=true;controlsWereEnabled=overviewControls!=null&&overviewControls.enabled; }
        if(overviewControls!=null)overviewControls.enabled=false;
        if(inspection!=null)inspection.ClearSelection();
        if(trial!=null)trial.RefreshObservationPermissions();
        yield return Fade(fadeMaterial != null ? fadeMaterial.GetFloat("_Alpha") : 0,1);
        yield return new WaitForEndOfFrame(); // Full black must actually render before teleport.
        if(enter) {
            bool ready=TryGetStreetPose(out var position,out var rotation,out var reason);
            if(!CanContinueEntry() || !ready) {
                Failure(reason,missingTerrain);
                yield return Fade(1,0); Finish(false); yield break;
            }
            transform.SetPositionAndRotation(position,rotation);IsStreet=true;
            Physics.SyncTransforms();
            // Hold black across rendered destination frames and validate again after Cesium updates.
            yield return new WaitForEndOfFrame();yield return null;
            ready=TryGetStreetPose(out _,out _,out reason);
            if(!CanContinueEntry() || !ready) {
                transform.SetPositionAndRotation(savedPosition,savedRotation);IsStreet=false;Failure(reason,missingTerrain);
            }
        } else { transform.SetPositionAndRotation(savedPosition,savedRotation);IsStreet=false; }
        
        yield return new WaitForEndOfFrame();
        yield return Fade(1,0);Finish(IsStreet);
    }
    private bool CanContinueEntry()=>ExplorationStage&&trial.TrialNumber==enteredTrial;
    private IEnumerator Fade(float from,float to)
    {
        if(!EnsureFade())yield break;
        fadeObject.SetActive(true);float elapsed=0;
        while(elapsed<fadeSeconds){fadeMaterial.SetFloat("_Alpha",Mathf.Lerp(from,to,elapsed/fadeSeconds));elapsed+=Time.unscaledDeltaTime;yield return null;}
        fadeMaterial.SetFloat("_Alpha",to);if(to==0)fadeObject.SetActive(false);
    }
    private void Finish(bool street)
    {
        IsStreet=street;IsTransitioning=false;transition=null;returning=false;
        SetVisitOpen(street);
        streetMovementReady=street;
        streetInputArmed=false;
        if(street) { streetCenter=head.transform.position-Vector3.up*eyeHeight; streetHeadOffsetY=head.transform.position.y-transform.position.y; }
        if(!street){destinationLocked=false;if(overviewControls!=null)overviewControls.enabled=controlsWereEnabled;hasSavedPose=false;}
        Status=street?"Street-level: Inspect HGVs; use Return to Overview":"Overview";
        if(trial!=null)trial.RefreshObservationPermissions();
    }
    public void RestoreForTrialChange()
    {
        SetVisitOpen(false);
        streetMovementReady=false;
        streetInputArmed=false;
        if(IsPreparing)CancelPreparation();
        destinationLocked=false; if(!hasSavedPose)return;
        if(transition!=null)StopCoroutine(transition);
        // Lifecycle cleanup is immediate, even if coroutine execution is unavailable.
        transform.SetPositionAndRotation(savedPosition,savedRotation);
        if(fadeObject!=null)fadeObject.SetActive(false);
        IsStreet=false;IsTransitioning=false;hasSavedPose=false;transition=null;returning=false;
        if(overviewControls!=null)overviewControls.enabled=controlsWereEnabled;
        if(inspection!=null)inspection.ClearSelection();
        Status="Overview";
        if(trial!=null){trial.RefreshObservationPermissions();}
    }
    private IEnumerator Prepare()
    {
        IsPreparing=true;IsTransitioning=true;Message("Preparing viewpoint...");trial.RefreshObservationPermissions();
        float deadline=Time.unscaledTime+preparationTimeout;
        while(Time.unscaledTime<deadline) {
            yield return new WaitForSecondsRealtime(.25f);
            if(!CanContinueEntry() || ObservationMarker!=preparingMarker) {CancelPreparation();yield break;}
            destinationLocked=false;
            if(TryGetStreetPose(out _,out _,out var reason)) {
                IsPreparing=false;IsTransitioning=false;
                if(!EnsureFade()){Failure("Fade shader unavailable",false);trial.RefreshObservationPermissions();yield break;}
                transition=StartCoroutine(Switch(true));yield break;
            }
            if(!missingTerrain) {IsPreparing=false;IsTransitioning=false;transition=null;Failure(reason,false);trial.RefreshObservationPermissions();yield break;}
        }
        IsPreparing=false;IsTransitioning=false;transition=null;destinationLocked=false;
        Failure("Terrain preparation timed out",true);if(trial!=null)trial.RefreshObservationPermissions();
    }
    public void CancelPreparation()
    {
        if(!IsPreparing)return;
        if(transition!=null)StopCoroutine(transition);
        transition=null;IsPreparing=false;IsTransitioning=false;destinationLocked=false;preparingMarker=null;Status="Overview";
        if(trial!=null)trial.RefreshObservationPermissions();
    }
    private void Failure(string detail,bool unavailable)
    {
        Debug.LogWarning("[Street viewpoint] "+detail,this);
        Message(unavailable?"Viewpoint not ready.\nPlease try again.":"No safe viewpoint here.\nSelect another marker.");
    }
    private bool EnsureFade()
    {
        if(fadeMaterial!=null)return true;if(fadeShader==null||head==null)return false;
        fadeMaterial=new Material(fadeShader);fadeMaterial.SetFloat("_Alpha",0);
        fadeObject=new GameObject("Stereo observation fade",typeof(MeshFilter),typeof(MeshRenderer));
        fadeObject.transform.SetParent(head.transform,false);fadeObject.transform.localPosition=Vector3.forward*(head.nearClipPlane+1);
        fadeMesh=new Mesh{name="Stereo fade quad"};fadeMesh.vertices=new[]{new Vector3(-1,-1,0),new Vector3(-1,1,0),new Vector3(1,1,0),new Vector3(1,-1,0)};fadeMesh.triangles=new[]{0,1,2,0,2,3};fadeMesh.bounds=new Bounds(Vector3.zero,Vector3.one*10000);
        fadeObject.GetComponent<MeshFilter>().sharedMesh=fadeMesh;var renderer=fadeObject.GetComponent<MeshRenderer>();renderer.sharedMaterial=fadeMaterial;renderer.sortingOrder=32767;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;fadeObject.SetActive(false);return true;
    }
    private void Message(string message){Status=message;if(labels!=null)labels.Notify(message);}
    private void OnDisable()=>RestoreForTrialChange();
    private void OnDestroy(){if(routeVisuals!=null)Destroy(routeVisuals);if(fadeObject!=null)Destroy(fadeObject);if(fadeMaterial!=null)Destroy(fadeMaterial);if(fadeMesh!=null)Destroy(fadeMesh);}
}


