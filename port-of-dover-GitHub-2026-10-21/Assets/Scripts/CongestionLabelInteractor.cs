using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>Participant hypotheses; one fresh candidate drives preview and commit.</summary>
[DefaultExecutionOrder(100)]
public sealed class CongestionLabelInteractor : MonoBehaviour
{
    [Serializable] public sealed class Label
    {
        public string RouteId { get; }
        public Transform Route { get; }
        public Vector3 Position { get; }
        public int Segment { get; }
        public float RouteProgress { get; }
        internal GameObject Visual;
        internal TextMesh Text;
        internal Transform Board;
        internal string Number;
        internal Label(string id, Transform route, Vector3 position, int segment, float progress)
        { RouteId=id; Route=route; Position=position; Segment=segment; RouteProgress=progress; }
    }
    public enum TargetKind { Invalid, Add, Remove, Vehicle, UI, SelectMarker }
    // Map interaction is limited to the existing selection badge.
    private static readonly Rect BadgeBounds = new Rect(-2.2f, -.75f, 4.4f, 2.55f);
    public struct Candidate
    {
        public TargetKind Kind;
        public string Reason, RouteId;
        public Transform Route;
        public Vector3 Position;
        public int Segment, Frame, Revision;
        public float RouteProgress;
        public Collider Surface;
        internal Label Existing;
        public bool Valid => Kind != TargetKind.Invalid;
    }
    [SerializeField] private ActionBasedController controller;
    [SerializeField] private XRRayInteractor ray;
    [SerializeField] private Transform terrain, routeA, routeB;
    [SerializeField] private Material lineMaterial;
    [SerializeField, Min(.1f), Tooltip("Baseline 2.5 m. Evaluate 3.25 m temporarily; ambiguity is always rejected.")] private float roadHalfWidth=2.5f;
    [SerializeField, Min(.1f), Tooltip("Duplicate protection only; road proximity never deletes a marker.")] private float deleteRadius=3f;
    [SerializeField] private bool diagnosticLogging;
    [SerializeField, Range(1, IdentificationTrialState.MarkerLimit)] private int maxMarkers = IdentificationTrialState.MarkerLimit;
    public int MaxMarkers => Mathf.Clamp(maxMarkers, 1, IdentificationTrialState.MarkerLimit);
    public Label SelectedMarker { get; private set; }
    public string SelectedMarkerNumber => SelectedMarker != null ? SelectedMarker.Number : "None";
    private readonly List<Label> formalLabels=new List<Label>();
    private readonly List<Label> practiceLabels=new List<Label>();
    private List<Label> labels=>PracticeMode?practiceLabels:formalLabels;
    public bool PracticeMode { get; private set; }
    public Label PracticeSelected=>PracticeMode?SelectedMarker:null;
    private int ActiveLimit=>PracticeMode?1:MaxMarkers;
    public void SetPracticeMode(bool active)
    {
        if(PracticeMode==active)return;
        if(active && formalLabels.Count!=0)throw new InvalidOperationException("Formal markers must be cleared before tutorial.");
        ClearLabels();PracticeMode=active;SelectedMarker=null;revision++;
    }
    private void NotifyLabelsChanged(){if(!PracticeMode)LabelsChanged?.Invoke();}
    private IReadOnlyList<Label> readOnlyLabels;
    private int revision;
    private int nextNumberA, nextNumberB;
    private LineRenderer preview;
    private TextMesh notice, targetText;
    private float noticeUntil;
    private XRInteractorLineVisual lineVisual;
    private Gradient oldValid, oldInvalid, oldBlocked;
    private readonly Gradient feedbackGradient=new Gradient();
    private readonly GradientColorKey[] colorKeys=new GradientColorKey[2];
    private readonly GradientAlphaKey[] alphaKeys={new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)};
    private Color lastColor;
    private bool lineConfigured;
    public Candidate CurrentCandidate { get; private set; }
    public string LastDiagnostic { get; private set; }
    public Material FeedbackMaterial=>lineMaterial;
    public IReadOnlyList<Label> Labels=>readOnlyLabels??(readOnlyLabels=formalLabels.AsReadOnly());
    public event Action LabelsChanged;
    public bool EditingAllowed {get;set;}=true;
    public bool RoadEditingAllowed {get;set;}=true;

    private Candidate Invalid(string reason, Vector3 position=default) => new Candidate {Kind=TargetKind.Invalid,Reason=reason,Position=position,Frame=Time.frameCount,Revision=revision};
    private void LateUpdate()
    {
        var camera=Camera.main;
        foreach(var label in labels) if(camera!=null) {
            label.Board.rotation=camera.transform.rotation;
            label.Board.localScale=Vector3.one*Mathf.Clamp(Vector3.Distance(camera.transform.position,label.Position)/100f,1,3);
        }
        if(notice!=null) notice.gameObject.SetActive(Time.unscaledTime<noticeUntil);
        HidePreview();
        var inspection=GetComponent<VehicleInspectInteractor>();
        if(inspection!=null) inspection.SetHover(null);
        bool pressed=controller!=null && controller.selectAction.action!=null && controller.selectAction.action.WasPressedThisFrame();
        if(ray==null || !ray.isActiveAndEnabled || controller==null) {
            CurrentCandidate=Invalid("Tracking invalid / ray unavailable");
            Diagnose(pressed ? "Tracking invalid: press rejected" : "Tracking invalid"); return;
        }
        var origin=ray.rayOriginTransform!=null?ray.rayOriginTransform:ray.transform;
        var pointer=new Ray(origin.position,origin.forward);
        // Fresh physics query at dispatch time; UI and world use exactly this pointer/hit.
        bool hasHit=Physics.Raycast(pointer,out var hit,ray.maxRaycastDistance,ray.raycastMask,QueryTriggerInteraction.Ignore);
        float distance=hasHit?hit.distance:ray.maxRaycastDistance;
        var trial=GetComponent<IdentificationTrialController>();
        if(trial!=null && trial.isActiveAndEnabled && trial.TryHandlePanelRay(pointer,distance,pressed)) {
            CurrentCandidate=new Candidate {Kind=TargetKind.UI,Reason="UI",Frame=Time.frameCount,Revision=revision};
            SetRayColor(new Color(.3f,.6f,1)); Diagnose(pressed?"UI intercepted":"Trigger not pressed: UI"); return;
        }
        CurrentCandidate=ResolveCandidate(pointer,hasHit,hit,distance);
        Present(CurrentCandidate,camera);
        if(CurrentCandidate.Kind==TargetKind.Vehicle && inspection!=null) inspection.SetHover(CurrentCandidate.Surface);
        if(!pressed) { Diagnose("Trigger not pressed"); return; }
        if(CurrentCandidate.Kind==TargetKind.Vehicle) {
            if(inspection!=null) inspection.HandleHit(CurrentCandidate.Surface);
            Diagnose("Vehicle selected"); return;
        }
        if(CurrentCandidate.Kind==TargetKind.Add || CurrentCandidate.Kind==TargetKind.Remove || CurrentCandidate.Kind==TargetKind.SelectMarker) {
            if(inspection!=null) inspection.ClearSelection();
            Commit(CurrentCandidate); return;
        }
        if(!hasHit && inspection!=null) inspection.ClearSelection();
        Notify(CurrentCandidate.Reason); Diagnose(CurrentCandidate.Reason);
    }
    private void Diagnose(string message)
    {
        if(diagnosticLogging && LastDiagnostic!=message) Debug.Log("[Marker input] "+message,this);
        LastDiagnostic=message;
    }
    public Candidate ResolveCandidate(Ray pointer,bool hasHit,RaycastHit hit,float nearestDistance)
    {
        if(!EditingAllowed) return Invalid("Editing locked",hasHit?hit.point:default);
        if (!RoadEditingAllowed) {
            var inspection = GetComponent<VehicleInspectInteractor>();
            if (hasHit && hit.collider.GetComponentInParent<VehicleRouteFollower>() != null && inspection != null && inspection.SelectionAllowed)
                return new Candidate { Kind=TargetKind.Vehicle,Reason="Inspect HGV",Surface=hit.collider,Position=hit.point,Frame=Time.frameCount,Revision=revision };
            return Invalid("Street-level: road marking disabled");
        }
        // Map badges select only; removal is available exclusively in the task menu.
        // Analytic UI hits precede world actions, but never bypass a nearer world occluder.
        Label remove=null; float closest=nearestDistance;
        foreach(var label in labels) {
            var plane=new Plane(label.Board.forward,label.Board.position);
            if(!plane.Raycast(pointer,out float d)||d<0||d>closest) continue;
            var p=label.Board.InverseTransformPoint(pointer.GetPoint(d));
            if(BadgeBounds.Contains(new Vector2(p.x,p.y))) { remove=label; closest=d; }
        }
        if(remove!=null) return new Candidate {Kind=TargetKind.SelectMarker,Reason="Select "+remove.Number,Existing=remove,RouteId=remove.RouteId,Route=remove.Route,Position=remove.Position,Segment=remove.Segment,RouteProgress=remove.RouteProgress,Frame=Time.frameCount,Revision=revision};
        if(!hasHit) return Invalid("Raycast missed");
        if(hit.collider.GetComponentInParent<VehicleRouteFollower>()!=null) {
            var inspection=GetComponent<VehicleInspectInteractor>();
            return inspection!=null&&inspection.SelectionAllowed ? new Candidate{Kind=TargetKind.Vehicle,Reason="Inspect HGV",Surface=hit.collider,Position=hit.point,Frame=Time.frameCount,Revision=revision} : Invalid("Vehicle inspection unavailable",hit.point);
        }
        return ResolveRoad(hit.collider,hit.point);
    }
    public Candidate ResolveRoad(Collider surface,Vector3 point)
    {
        if(!EditingAllowed || !RoadEditingAllowed) return Invalid("Editing locked",point);
        if(terrain==null||surface==null||!surface.transform.IsChildOf(terrain)) return Invalid("Invalid surface",point);
        float a=Project(routeA,point,out int sa,out float pa,out Vector3 xa);
        float b=Project(routeB,point,out int sb,out float pb,out Vector3 xb);
        if(Mathf.Min(a,b)>roadHalfWidth||float.IsInfinity(Mathf.Min(a,b))) return Invalid("Outside road corridor",point);
        if(Mathf.Abs(a-b)<.25f) return Invalid("Ambiguous Route A/B",point);
        bool isA=a<b; var route=isA?routeA:routeB; Vector3 snap=isA?xa:xb;
        // Confirm actual terrain at snapped XZ; never use route Y or snap onto roofs.
        snap.y=point.y;
        if(!Physics.Raycast(snap+Vector3.up*5,Vector3.down,out var ground,10,~0,QueryTriggerInteraction.Ignore)||!ground.transform.IsChildOf(terrain)) return Invalid("Snapped terrain unavailable or obstructed",point);
        snap=ground.point;
        foreach(var label in labels) {
            Vector3 delta=label.Position-snap; delta.y=0;
            if(label.Route==route&&delta.sqrMagnitude<=deleteRadius*deleteRadius) return Invalid("Already marked - select its badge, then use menu Remove",snap);
        }
        if(labels.Count>=ActiveLimit) return Invalid("Maximum Markers ("+ActiveLimit+")",snap);
        return new Candidate{Kind=TargetKind.Add,Reason=isA?"Add Route A":"Add Route B",RouteId=isA?"A":"B",Route=route,Position=snap,Segment=isA?sa:sb,RouteProgress=isA?pa:pb,Surface=surface,Frame=Time.frameCount,Revision=revision};
    }
    public bool Commit(Candidate candidate)
    {
        if(!EditingAllowed||!RoadEditingAllowed||candidate.Frame!=Time.frameCount||candidate.Revision!=revision) return false;
        if(candidate.Kind==TargetKind.SelectMarker) {
            if(candidate.Existing==null || !labels.Contains(candidate.Existing))return false;
            SelectedMarker=candidate.Existing; RefreshLabels(); Notify("Selected marker: "+SelectedMarker.Number); return true;
        }
        if(candidate.Kind==TargetKind.Remove) return false; // Menu-only deletion.
        if(candidate.Kind!=TargetKind.Add||labels.Count>=ActiveLimit) return false;
        var label=new Label(candidate.RouteId,candidate.Route,candidate.Position,candidate.Segment,candidate.RouteProgress);
        label.Number=label.RouteId+(label.RouteId=="A"?++nextNumberA:++nextNumberB);
        CreateVisual(label); labels.Add(label); revision++; RefreshLabels(); HidePreview();
        Notify("Marked: "+label.Number+" - Route "+label.RouteId); Diagnose("Add success"); NotifyLabelsChanged(); return true;
    }
    // Retained for existing callers; road clicks now only add, never toggle-delete.
    public bool ToggleAt(Collider surface,Vector3 point)=>Commit(ResolveRoad(surface,point));
    public bool TryResolve(Collider surface,Vector3 point,out Label candidate) {
        var c=ResolveRoad(surface,point); candidate=c.Kind==TargetKind.Add?new Label(c.RouteId,c.Route,c.Position,c.Segment,c.RouteProgress):null; return candidate!=null;
    }
    public void UpdatePreview(Collider surface,Vector3 point)=>Present(ResolveRoad(surface,point),Camera.main);
    private static float Project(Transform route,Vector3 point,out int segment,out float progress,out Vector3 snapped)
    {
        segment=-1;progress=0;snapped=point; float best=float.PositiveInfinity,cumulative=0;
        if(route==null)return best; point.y=0;
        for(int i=0;i+1<route.childCount;i++) {
            var start=route.GetChild(i).position;var end=route.GetChild(i+1).position;start.y=end.y=0;
            var edge=end-start;float length=edge.magnitude;if(length<.001f)continue;
            float t=Mathf.Clamp01(Vector3.Dot(point-start,edge)/(length*length));var nearest=start+edge*t;
            float distance=Vector3.Distance(point,nearest);
            if(distance<best){best=distance;segment=i;progress=cumulative+t*length;snapped=nearest;} cumulative+=length;
        }return best;
    }
    private void CreateVisual(Label label)
    {
        label.Visual=new GameObject("Suspected congestion - Route "+label.RouteId);label.Visual.transform.position=label.Position;
        var ring=InteractionFeedbackVisuals.Ring("Road outline",lineMaterial,new Color(1,.7f,.05f),2.5f,.28f);ring.transform.SetParent(label.Visual.transform,false);
        var stem=new GameObject("Pointer").AddComponent<LineRenderer>();stem.transform.SetParent(label.Visual.transform,false);stem.useWorldSpace=false;stem.sharedMaterial=lineMaterial;stem.positionCount=2;stem.SetPosition(0,Vector3.up*.4f);stem.SetPosition(1,Vector3.up*7);stem.widthMultiplier=.28f;stem.startColor=stem.endColor=Color.yellow;stem.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        label.Board=new GameObject("Marker badge").transform;label.Board.SetParent(label.Visual.transform,false);label.Board.localPosition=Vector3.up*7;
        var plate=new GameObject("Contrast plate",typeof(RectTransform),typeof(UnityEngine.Canvas),typeof(UnityEngine.UI.Image));plate.transform.SetParent(label.Board,false);plate.transform.localPosition=Vector3.forward*.04f;plate.GetComponent<RectTransform>().sizeDelta=new Vector2(4.8f,3.6f);plate.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;plate.GetComponent<UnityEngine.UI.Image>().color=new Color(.02f,.025f,.04f,.95f);plate.GetComponent<UnityEngine.UI.Image>().raycastTarget=false;
        label.Text=MakeText(label.Board,"Number",Vector3.up*.35f,.55f);label.Text.color=Color.yellow;
        if(Camera.main!=null)label.Board.rotation=Camera.main.transform.rotation;
    }
    private static TextMesh MakeText(Transform parent,string name,Vector3 position,float size) {
        var text=new GameObject(name).AddComponent<TextMesh>();text.transform.SetParent(parent,false);text.transform.localPosition=position;text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;text.fontSize=64;text.characterSize=size;return text;
    }
    public bool RemoveSelectedMarker()
    {
        if(!EditingAllowed || !RoadEditingAllowed || SelectedMarker==null || !labels.Remove(SelectedMarker))return false;
        RemoveVisual(SelectedMarker);SelectedMarker=null;revision++;RefreshLabels();HidePreview();
        Notify("Marker removed");Diagnose("Remove success");NotifyLabelsChanged();return true;
    }
    private void RefreshLabels(){foreach(var l in labels){l.Text.text=(PracticeMode?"Practice\n":"")+l.Number;l.Text.color=l==SelectedMarker?Color.cyan:Color.yellow;}}
    private void Present(Candidate c,Camera camera)
    {
        HidePreview();Color color=c.Kind==TargetKind.Add?new Color(.2f,1,.4f):c.Kind==TargetKind.Remove?new Color(1,.4f,.1f):(c.Kind==TargetKind.Vehicle||c.Kind==TargetKind.SelectMarker)?Color.cyan:c.Reason=="Maximum Markers ("+ActiveLimit+")"?new Color(1,.2f,1):Color.gray;
        SetRayColor(color);
        if(c.Kind==TargetKind.Add||c.Kind==TargetKind.Remove||c.Kind==TargetKind.SelectMarker){
            if(preview==null)preview=InteractionFeedbackVisuals.Ring("Road marker preview",lineMaterial,color,2.5f,.22f);
            preview.transform.position=c.Position;preview.startColor=preview.endColor=new Color(color.r,color.g,color.b,.65f);preview.gameObject.SetActive(true);
        }
        if(camera==null)return;
        if(targetText==null)targetText=MakeText(null,"Pointer status",Vector3.zero,.009f);
        targetText.text=c.Reason;targetText.color=color;
        targetText.transform.SetPositionAndRotation(camera.transform.TransformPoint(new Vector3(.35f,-.3f,1.4f)),camera.transform.rotation);targetText.gameObject.SetActive(true);
    }
    private void SetRayColor(Color color)
    {
        if(lineVisual==null)lineVisual=GetComponent<XRInteractorLineVisual>();if(lineVisual==null)return;
        if(!lineConfigured){oldValid=lineVisual.validColorGradient;oldInvalid=lineVisual.invalidColorGradient;oldBlocked=lineVisual.blockedColorGradient;lineConfigured=true;}
        if(lastColor==color)return;lastColor=color;colorKeys[0]=new GradientColorKey(color,0);colorKeys[1]=new GradientColorKey(color,1);feedbackGradient.SetKeys(colorKeys,alphaKeys);
        lineVisual.validColorGradient=lineVisual.invalidColorGradient=lineVisual.blockedColorGradient=feedbackGradient;
    }
    private void HidePreview(){if(preview!=null)preview.gameObject.SetActive(false);if(targetText!=null)targetText.gameObject.SetActive(false);}
    public void Notify(string message)
    {
        var camera=Camera.main;if(camera==null)return;
        if(notice==null){notice=MakeText(null,"Interaction confirmation",Vector3.zero,.012f);var view=GetComponentInParent<XRViewControls>();if(view!=null)notice.transform.SetParent(view.transform,true);}
        notice.text=message;notice.transform.SetPositionAndRotation(camera.transform.TransformPoint(new Vector3(0,-.45f,1.5f)),camera.transform.rotation);noticeUntil=Time.unscaledTime+1.6f;notice.gameObject.SetActive(true);
    }
    public void ClearLabels(){SelectedMarker=null;nextNumberA=nextNumberB=0;foreach(var l in labels)RemoveVisual(l);labels.Clear();revision++;HidePreview();noticeUntil=0;if(notice!=null)notice.gameObject.SetActive(false);NotifyLabelsChanged();}
    private void OnDisable(){ClearLabels();if(lineConfigured&&lineVisual!=null){lineVisual.validColorGradient=oldValid;lineVisual.invalidColorGradient=oldInvalid;lineVisual.blockedColorGradient=oldBlocked;}lineConfigured=false;lastColor=default;}
    private void OnDestroy(){if(preview!=null)InteractionFeedbackVisuals.Remove(preview.gameObject);if(notice!=null)InteractionFeedbackVisuals.Remove(notice.gameObject);if(targetText!=null)InteractionFeedbackVisuals.Remove(targetText.gameObject);}
    private static void RemoveVisual(Label label)=>InteractionFeedbackVisuals.Remove(label.Visual);
}



