using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

[DefaultExecutionOrder(90)]
public sealed partial class IdentificationTrialController : MonoBehaviour
{
    [SerializeField] private TrafficScenarioManager traffic;
    [SerializeField] private CongestionLabelInteractor labels;
    [SerializeField] private VehicleInspectInteractor inspection;
    [SerializeField] private Camera head;
    [SerializeField] private Transform rig;
    [Header("Evaluation logging (Windows PC)")]
    [Tooltip("Anonymous study code only, e.g. P001. Leave blank to generate a session ID. Set before starting trials; do not enter a name or email.")]
    [SerializeField] private string participantId = "";
    [Tooltip("Windows JSON output folder. Leave blank to use persistentDataPath/EvaluationData. Created automatically on submission.")]
    [SerializeField] private string evaluationDirectory = @"C:\Users\SEER04\Desktop\SJY project\Data Collection";
    private string sessionParticipantId;
    private EvaluationTrialDocument evaluation;
    private bool saveFailed;
    private double streetEnteredAt = -1;
    public string EvaluationDirectory => string.IsNullOrWhiteSpace(evaluationDirectory) ? EvaluationDataLog.DirectoryPath : evaluationDirectory.Trim();
    public string LastSavedFile { get; private set; }
    public string ParticipantId => sessionParticipantId;
    [SerializeField] private Vector3 panelViewOffset = new Vector3(-1.05f, -.3f, 1.8f);
    [Tooltip("Welcome/tutorial default relative to the headset. Used only on creation or explicit reset, never on Next/Back.")]
    [SerializeField] private Vector3 tutorialPanelViewOffset = new Vector3(0, 0, 2f);
    // Trigger grab preserves a controller-local offset; panel orientation remains rig-relative.
    private readonly IdentificationTrialState state = new IdentificationTrialState();
    private double trialStartedAt;
    private readonly List<IdentificationTrialResult> sessionResults = new List<IdentificationTrialResult>();
    private IReadOnlyList<IdentificationTrialResult> readOnlyResults;
    public IReadOnlyList<IdentificationTrialResult> SessionResults => readOnlyResults ?? (readOnlyResults = sessionResults.AsReadOnly());
    private bool nextTrialFailed;
    private bool welcomeComplete, helpOpen;
    private bool sessionRandomInitialized;
    private string welcomeError;
    private RectTransform helpRect;
    private Text helpText;
    private Image helpImage;
    private GameObject welcomeContent;
    private Text welcomeNote;
    private Image dragImage;
    private bool? welcomeLayout;

    public bool StartObservation()
    {
        if(welcomeComplete || !Application.isPlaying || tutorialStep!=8 || !TutorialReady)return false;
        if(traffic==null || traffic.State!=TrafficScenarioState.Idle) {
            welcomeError="Ask the researcher to reset traffic before starting."; RefreshPanel(); return false;
        }
        if (!sessionRandomInitialized)
        {
            try { sessionRandomInitialized = traffic.InitializeParticipantRandomSequence(); }
            catch (Exception error) { Debug.LogException(error, this); }
            if (!sessionRandomInitialized) {
                welcomeError="Unable to initialize session. Please ask the researcher."; RefreshPanel(); return false;
            }
        }
        CleanupTutorial();
        traffic.StartScenario();
        if(traffic.State!=TrafficScenarioState.Running) {
            if(labels!=null)labels.SetPracticeMode(true);
            welcomeError="Unable to start. Please ask the researcher."; RefreshPanel(); return false;
        }
        welcomeComplete=true;helpOpen=false;welcomeError=null;
        FinishTutorialState();
        SynchronizeTrial();return true;
    }
    private void RetainSubmittedResult()
    {
        var result=state.Result;
        if(result!=null && !sessionResults.Contains(result))sessionResults.Add(result);
    }
    public bool StartNextTrial()
    {
        if(!Application.isPlaying || state.Stage!=IdentificationStage.Submitted || traffic==null ||
            state.TrialNumber==long.MaxValue || (observationModes!=null&&observationModes.IsTransitioning))return false;
        RetainSubmittedResult();
        long previous=state.TrialNumber;
        if(observationModes!=null)observationModes.RestoreForTrialChange();
        var view=rig!=null?rig.GetComponent<XRViewControls>():null;
        if(view!=null)view.ResetView();
        PositionPanel();
        traffic.ResetScenario();
        traffic.StartScenario(); // Preserve existing seeded sequence and bidirectional orchestration.
        nextTrialFailed=traffic.State==TrafficScenarioState.Idle || traffic.CurrentTrialNumber<=previous;
        if(nextTrialFailed){ApplyPermissions();RefreshPanel();return false;}
        SynchronizeTrial();return true;
    }
    private GameObject panel;
    [SerializeField] private XRObservationModes observationModes;
    private RectTransform panelRect, primaryRect, backRect, observationRect, dragRect;
    private ActionBasedController leftHand, rightHand, draggingHand;
    private InputAction leftTracked, rightTracked, resetPanel;
    private Vector3 dragLocalOffset;
    private Quaternion dragPanelRotation, dragBoundsYaw;
    private bool dragInputsReady;
    public bool IsDragging => draggingHand != null;
    private Text observationText;
    private Image observationImage;
    public long TrialNumber => state.TrialNumber;
    public void RefreshObservationPermissions() { ApplyPermissions(); RefreshPanel(); }
    public void RepositionObservationPanel() { PositionPanel(); }

    private Text body, primaryText, submitHint;
    private RectTransform removeMarkerRect;
    private Text removeMarkerText;
    private Image removeMarkerImage;
    private float removePressedUntil, observationPressedUntil;
    private bool feedbackStreet;
    private bool CanRemoveMenuMarker => labels!=null && labels.SelectedMarker!=null && labels.EditingAllowed && labels.RoadEditingAllowed && !helpOpen &&
        (observationModes==null || (!observationModes.IsStreet && !observationModes.IsTransitioning)) &&
        (welcomeComplete ? state.CanEdit : tutorialStep==5);
    private void RefreshMarkerMenu()
    {
        bool available=CanRemoveMenuMarker;
        bool flash=!helpOpen && Time.unscaledTime<removePressedUntil;
        removeMarkerRect.gameObject.SetActive(available || flash);
        if(flash && welcomeComplete){body.rectTransform.sizeDelta=new Vector2(610,250);body.rectTransform.anchoredPosition=new Vector2(0,60);}
        if(!available)return;
        var selected=labels.SelectedMarker;
        removeMarkerText.text="Remove "+selected.Number+"\nRoute "+selected.RouteId+" · Position "+
            Math.Round(selected.RouteProgress,0,MidpointRounding.AwayFromZero).ToString("F0",System.Globalization.CultureInfo.InvariantCulture)+" m";
        if(welcomeComplete){body.rectTransform.sizeDelta=new Vector2(610,250);body.rectTransform.anchoredPosition=new Vector2(0,60);}
    }
    private Image primaryImage, backImage;
    private string lastText;
    public IdentificationStage Stage => state.Stage;
    public IdentificationSnapshot Initial => state.Initial;
    public IdentificationTrialResult Result => state.Result;
    public event Action<IdentificationTrialResult> Submitted;

    private void Awake() { InitializeTutorial(); ApplyPermissions(); }
    private void OnEnable() { Application.onBeforeRender += UpdateDragBeforeRender; if (labels != null) labels.LabelsChanged += LabelsChanged; if(observationModes!=null) observationModes.StreetObservationChanged += RecordStreetVisit; ApplyPermissions(); }
    private void OnDisable()
    {
        Application.onBeforeRender -= UpdateDragBeforeRender;
        RecordStreetVisit(false);
        if(observationModes!=null) observationModes.StreetObservationChanged -= RecordStreetVisit;
        if (labels != null) { labels.LabelsChanged -= LabelsChanged; labels.EditingAllowed = false; }
        if (inspection != null) { inspection.SelectionAllowed = false; inspection.ClearSelection(); }
        draggingHand=null; if (panel != null) panel.SetActive(false);
    }
    private void LateUpdate() { SynchronizeTrial(); TickTutorial(); RefreshPanel(); UpdatePanelDrag(); UpdateDragFeedback(); }

    public void SynchronizeTrial()
    {
        if(!welcomeComplete)return; // No trial ID, evaluation record or observation timer during Welcome.
        long number = traffic != null && traffic.State != TrafficScenarioState.Idle ? traffic.CurrentTrialNumber : 0;
        if (number == state.TrialNumber) return;
        if (state.PendingResult != null) return; // Do not lose an unsaved response on an external Traffic Reset.
        RecordStreetVisit(false);
        RetainSubmittedResult();
        if(number==0 && state.Stage==IdentificationStage.Submitted)return;
        nextTrialFailed=false; helpOpen=false; saveFailed=false; evaluation=null; streetEnteredAt=-1; trialStartedAt=0;
        state.Clear();
        // Clear before beginning, so cleanup is never counted as a participant edit.
        if (labels != null) labels.ClearLabels();
        if (inspection != null) inspection.ClearSelection();
        if (number > 0) { state.Begin(number); trialStartedAt = Time.realtimeSinceStartupAsDouble; BeginEvaluation(); }
        ApplyPermissions();
        EnsurePanel();
        PositionPanel();
        RefreshPanel();
    }

    private void BeginEvaluation()
    {
        // Lock participant identity for this controller session; Next Trial does not replace it.
        if (string.IsNullOrEmpty(sessionParticipantId))
        {
            string configured = participantId.Trim();
            if (configured.Length > 32 || !System.Text.RegularExpressions.Regex.IsMatch(configured, "^[A-Za-z0-9_-]+$"))
                configured = "anon_" + Guid.NewGuid().ToString("N");
            sessionParticipantId = configured;
        }
        evaluation = new EvaluationTrialDocument {
            participantId=sessionParticipantId, trialId=Guid.NewGuid().ToString("N"),
            trialStartedUtc=DateTime.UtcNow.ToString("O"), unityScene=gameObject.scene.name,
            trafficTruthAvailable=traffic!=null && traffic.State!=TrafficScenarioState.Idle,
            routeAScenarioId=traffic!=null?traffic.CurrentScenarioId:null,
            routeBScenarioId=traffic!=null?traffic.OppositeScenarioId:null,
            selectionSeed=traffic!=null?traffic.SelectionSeed:0,
            randomDrawNumber=traffic!=null?traffic.RandomDrawNumber:0,
            routeAActiveBottleneck=traffic!=null?traffic.ActiveBottleneck:null,
            routeBPresent=traffic!=null && traffic.HasOppositeDirection,
            routeBActiveBottleneck=traffic!=null && traffic.HasOppositeDirection?traffic.OppositeActiveBottleneck:null,
            streetMetricsAvailable=observationModes!=null
        };
    }
    private void RecordStreetVisit(bool entered)
    {
        if(evaluation==null || evaluation.identification!=null) return;
        double now=Math.Max(0,Time.realtimeSinceStartupAsDouble-trialStartedAt);
        if(entered) { if(streetEnteredAt<0) streetEnteredAt=now; return; }
        if(streetEnteredAt<0)return;
        double duration=Math.Max(0,now-streetEnteredAt);
        evaluation.streetVisits.Add(new StreetObservationVisit {enteredAtSeconds=streetEnteredAt,exitedAtSeconds=now,durationSeconds=duration});
        evaluation.streetVisitCount=evaluation.streetVisits.Count;
        evaluation.streetDurationSeconds+=duration; streetEnteredAt=-1;
    }
    private bool PersistFinal(IdentificationTrialResult result)
    {
        if(evaluation==null) { saveFailed=true; Debug.LogError("Evaluation trial metadata unavailable; response retained.",this); return false; }
        if(evaluation.identification==null) {
            RecordStreetVisit(false);
            evaluation.identification=result; evaluation.finalConfirmedUtc=DateTime.UtcNow.ToString("O");
        }
        bool saved=EvaluationDataLog.TrySave(evaluation,out var path,out var error,EvaluationDirectory);
        saveFailed=!saved;
        if(saved) LastSavedFile=path;
        else Debug.LogError("Evaluation JSON save failed; response retained for retry: "+error,this);
        return saved;
    }

    private void LabelsChanged() { state.MarkEdited(); RefreshPanel(); }
    private void ApplyPermissions()
    {
        if(!welcomeComplete) { ApplyTutorialPermissions(); return; }
        if (labels != null) {
            labels.EditingAllowed = isActiveAndEnabled && state.CanEdit && (observationModes == null || !observationModes.IsTransitioning);
            labels.RoadEditingAllowed = observationModes == null || (!observationModes.IsStreet && !observationModes.IsTransitioning);
        }
        if (inspection != null)
        {
            inspection.SelectionAllowed = isActiveAndEnabled && (state.CanInspect || (state.Stage == IdentificationStage.ObserveIdentify && observationModes != null && observationModes.IsStreet)) && (observationModes == null || !observationModes.IsTransitioning);
            if (!inspection.SelectionAllowed) inspection.ClearSelection();
        }
    }
    private List<IdentificationMarker> CopyMarkers()
    {
        var markers = new List<IdentificationMarker>();
        foreach (var label in labels.Labels)
            markers.Add(new IdentificationMarker(label.RouteId, label.Route != null ? label.Route.name : "",
                label.Segment, label.RouteProgress, label.Position));
        return markers;
    }

    public bool Review()
    { if(!welcomeComplete || helpOpen)return false; SynchronizeTrial(); bool changed = state.Review(); ApplyPermissions(); RefreshPanel(); return changed; }
    public bool Back()
    { if(!welcomeComplete || helpOpen)return false; SynchronizeTrial(); bool changed = state.Back(); ApplyPermissions(); RefreshPanel(); return changed; }
    public bool Confirm(bool explicitlyNoCongestion)
    {
        SynchronizeTrial();
        if(!welcomeComplete || helpOpen)return false;
        if (labels == null) return false;
        bool changed = state.Confirm(CopyMarkers(), explicitlyNoCongestion, Time.realtimeSinceStartupAsDouble - trialStartedAt, PersistFinal);
        ApplyPermissions(); RefreshPanel();
        if (changed && state.Stage == IdentificationStage.Submitted) { RetainSubmittedResult(); Submitted?.Invoke(state.Result); }
        return changed;
    }

    // Analytic panel hit: no physics collider, EventSystem trigger reader or desktop input.
    // Entire panel consumes the press; occluded panels cannot be clicked through world geometry.
    public bool TryHandlePanelRay(Ray ray, float nearestWorldDistance, bool pressed)
    {
        SynchronizeTrial(); EnsurePanel(); RefreshPanel();
        if(IsDragging)return true;
        if (panel == null || !panel.activeInHierarchy) return false;
        SetHover(false, false);
        var plane = new Plane(panelRect.forward, panelRect.position);
        if (!plane.Raycast(ray, out float distance) || distance < 0) return helpOpen || (!welcomeComplete && tutorialStep!=5);
        Vector3 world = ray.GetPoint(distance);
        if (!panelRect.rect.Contains(panelRect.InverseTransformPoint(world))) return helpOpen || (!welcomeComplete && tutorialStep!=5);
        if(distance<=4.5f && dragRect!=null && dragRect.rect.Contains(dragRect.InverseTransformPoint(world))) {
            if(pressed && rightHand!=null)BeginPanelDrag(rightHand);
            return true;
        }
        if(distance>nearestWorldDistance+.002f)return helpOpen || (!welcomeComplete && tutorialStep!=5);
        bool help=helpRect.rect.Contains(helpRect.InverseTransformPoint(world));
        helpImage.color=help?new Color(.1f,.5f,.7f):new Color(.08f,.23f,.35f);
        if(pressed && help) {helpOpen=!helpOpen;RefreshPanel();return true;}
        if(helpOpen)return true;
        if(removeMarkerRect.gameObject.activeSelf && removeMarkerRect.rect.Contains(removeMarkerRect.InverseTransformPoint(world))) {
            removeMarkerImage.color=Time.unscaledTime<removePressedUntil?new Color(.95f,.35f,.25f):new Color(.65f,.13f,.10f);
            if(pressed && CanRemoveMenuMarker){removePressedUntil=Time.unscaledTime+.18f;labels.RemoveSelectedMarker();RefreshPanel();}
            return true;
        }
        if(!welcomeComplete) { HandleTutorialClick(world,pressed);return true; }
        bool primary = primaryRect.gameObject.activeSelf && primaryRect.rect.Contains(primaryRect.InverseTransformPoint(world));
        bool back = backRect.gameObject.activeSelf && backRect.rect.Contains(backRect.InverseTransformPoint(world));
        bool observation = observationRect.gameObject.activeSelf && observationRect.rect.Contains(observationRect.InverseTransformPoint(world));
        observationImage.color = observation ? new Color(.1f,.5f,.7f) : new Color(.08f,.23f,.35f);
        ApplyActionHierarchy(primary, back, observation);
        if (pressed && (observationModes == null || !observationModes.IsTransitioning || (observation && observationModes.IsPreparing)))
        {
            if (observation) { observationPressedUntil=Time.unscaledTime+.18f;observationModes.RequestToggle(); ApplyActionHierarchy(primary,back,true);return true; }
            if (back) Back();
            else if (primary)
            {
                if(!welcomeComplete)StartObservation();
                else if(state.Stage==IdentificationStage.Submitted)StartNextTrial();
                else if (state.Stage == IdentificationStage.ObserveIdentify || state.Stage == IdentificationStage.SelectInspect) Review();
                else Confirm(labels.Labels.Count == 0); // Button explicitly says Confirm No Congestion for zero labels.
            }
        }
        return true;
    }

    private void SetHover(bool primary, bool back)
    {
        ApplyActionHierarchy(primary, back);
    }
    private void StyleAction(Image image, Text text, bool prominent, bool hover=false)
    {
        image.color=prominent ? (hover?new Color(.12f,.58f,.84f):new Color(.08f,.38f,.65f)) :
            (hover?new Color(.20f,.29f,.37f):new Color(.10f,.16f,.22f));
        text.fontStyle=prominent?FontStyle.Bold:FontStyle.Normal;
    }
    private void ApplyActionHierarchy(bool primaryHover=false,bool backHover=false,bool observationHover=false,bool practiceHover=false)
    {
        bool street=observationModes!=null && observationModes.IsStreet;
        if(street!=feedbackStreet || helpOpen){observationPressedUntil=0;removePressedUntil=0;feedbackStreet=street;}
        bool nextReady=!welcomeComplete && (tutorialStep==8?TutorialReady:CanAdvanceTutorial());
        bool main=!helpOpen && (welcomeComplete ? !street && state.Stage!=IdentificationStage.Submitted && state.Stage!=IdentificationStage.Idle : nextReady);
        if(state.Stage==IdentificationStage.ConfirmInitial && labels!=null && labels.Labels.Count==0)main=false;
        StyleAction(primaryImage,primaryText,main,primaryHover);
        StyleAction(observationImage,observationText,!helpOpen && welcomeComplete && street,observationHover);
        if(observationHover)observationImage.color=new Color(.18f,.43f,.61f);
        if(Time.unscaledTime<observationPressedUntil)observationImage.color=new Color(.32f,.64f,.82f);
        StyleAction(backImage,backRect.GetChild(0).GetComponent<Text>(),false,backHover);
        StyleAction(helpImage,helpText,false);
        StyleAction(removeMarkerImage,removeMarkerText,false);
        removeMarkerText.color=Color.white;
        removeMarkerImage.color=Time.unscaledTime<removePressedUntil?new Color(.95f,.35f,.25f):new Color(.32f,.07f,.06f);
        if(practiceImage!=null)StyleAction(practiceImage,practiceText,!helpOpen && !welcomeComplete && !nextReady &&
            (tutorialStep==2 || tutorialStep==6 || (tutorialStep==7 && (streetMoved||tutorialDone[7]))),practiceHover);
    }
    private string ContextHelp()
    {
        if(!welcomeComplete) {
            switch(tutorialStep) {
                case 1:return "Welcome help\nRead the introduction, then select Next.\nRight Trigger: select menu buttons.\nReading is not timed.";
                case 2:return "Step 2: Select\nAim at Practice Button and press Right Trigger once.\nAfter success, select Next.";
                case 3:return "Step 3: Move the panel\nAim at the top nine-dot icon. Hold either Trigger,\nmove your hand, then release to place the panel.";
                case 4:return "Step 4: Explore\nLeft Stick: pan. Right Stick: zoom.\nTry each separately, then select Next.\nLeft X resets the view and panel.";
                case 5:return "Step 5: Practice marker\nRight Trigger: place a marker on Route A or B.\nSelect its numbered badge to continue.\nTo try again, select the badge, then menu Remove.\nPractice markers are not trial answers.";
                case 6:if(observationModes!=null && observationModes.IsStreet)return "Step 6: Street-level\nYou have reached the practice viewpoint.\nSelect Next to learn movement and returning.\nFormal submission is only available in Overview.";
                    return "Step 6: Street-level\nSelect Inspect Location for your practice marker.\nWait for the safe viewpoint check. If unavailable,\ngo Back and choose another marker. Then select Next.";
                case 7:return "Step 7: Move and return\nLeft Stick: move a short safe distance.\nThen select Return to Overview with Right Trigger.\nLeft X: reset to the viewpoint. Then select Next.";
                default:return "Step 8: Ready\nSelect Start Observation with Right Trigger.\nPractice is cleared and the timed trial begins.";
            }
        }
        if(state.Stage==IdentificationStage.Submitted)return "Task Completed\nYour answers have been saved.\nThank you for participating!\nPlease inform the researcher and remove your headset when ready.";
        if(observationModes!=null && observationModes.IsStreet)return "Street-level help\nLeft Stick: move within the safe area.\nPoint at a truck and press the Right Trigger to view its information.\nSelect Return to Overview when ready.\nReturn to Overview before editing markers or submitting.\nLeft X: reset viewpoint and panel.";
        if(state.Stage==IdentificationStage.SelectInspect || state.Stage==IdentificationStage.ConfirmFinal)
            return saveFailed ? "Save failed\nYour response is retained. Select Retry Save.\nDo not close the application before saving succeeds." :
                "Final judgment help\nInspect HGVs or revise your markers.\nIn review, use Back to edit first. Select a marker\nfor optional Inspect Location or menu Remove.\nWhen satisfied, select Submit Final Answer.\nWith zero markers, submission confirms No Congestion.\nYou do not need to wait for traffic to stop.";
        return "Initial judgment help\nRight Trigger: place a marker on Route A or B.\nSelect its badge; menu Remove deletes that marker.\nUse zero markers for No Congestion.\nSelect Confirm Initial, then confirm your review\nor explicitly confirm No Congestion.\nLeft Stick: pan. Right Stick: zoom. Left X: reset.";
    }
    private void UpdatePanelDrag()
    {
        if(!dragInputsReady && rig!=null) {
            rightHand=GetComponent<ActionBasedController>();
            var left=rig.Find("Camera Offset/Left Controller");
            if(left!=null)leftHand=left.GetComponent<ActionBasedController>();
            var asset=rightHand!=null?rightHand.selectAction.action?.actionMap?.asset:null;
            if(asset!=null) {leftTracked=asset.FindAction("Observation/LeftTracked",false);rightTracked=asset.FindAction("Observation/RightTracked",false);resetPanel=asset.FindAction("Observation/ViewReset",false);dragInputsReady=true;}
        }
        if(resetPanel!=null && resetPanel.WasPressedThisFrame() && Tracked(leftTracked)) {PositionPanel();return;}
        if(observationModes!=null && observationModes.IsTransitioning) {draggingHand=null;return;}
        if(IsDragging) {
            var tracking=draggingHand==leftHand?leftTracked:rightTracked;
            if(!Tracked(tracking)||draggingHand.selectAction.action==null||!draggingHand.selectAction.action.IsPressed()) {draggingHand=null;return;}
            ApplyDragPose();
            return;
        }
        if(leftHand==null || !Tracked(leftTracked) || leftHand.selectAction.action==null || !leftHand.selectAction.action.WasPressedThisFrame() || dragRect==null)return;
        var leftRay=leftHand.GetComponent<XRRayInteractor>();
        if(leftRay==null || !leftRay.isActiveAndEnabled)return;
        var origin=leftRay.rayOriginTransform!=null?leftRay.rayOriginTransform:leftRay.transform;
        var pointer=new Ray(origin.position,origin.forward);
        var plane=new Plane(dragRect.forward,dragRect.position);
        if(!plane.Raycast(pointer,out float d)||d<0||d>Mathf.Min(4.5f,leftRay.maxRaycastDistance))return;
        if(!dragRect.rect.Contains(dragRect.InverseTransformPoint(pointer.GetPoint(d))))return;
        // The nearby task handle has UI priority; world hits still gate all task buttons.
        BeginPanelDrag(leftHand);
    }
    private static bool Tracked(InputAction action)=>action!=null&&action.enabled&&action.ReadValue<float>()>.5f;
    private void BeginPanelDrag(ActionBasedController hand)
    {
        if(IsDragging)return;
        if(head==null || panel==null || (observationModes!=null&&observationModes.IsTransitioning))return;
        if(!Tracked(hand==leftHand?leftTracked:rightTracked))return;
        draggingHand=hand;
        TutorialGrabStarted();
        dragLocalOffset=hand.transform.InverseTransformPoint(panel.transform.position);
        dragPanelRotation=panel.transform.localRotation;
        var worldYaw=Quaternion.Euler(0,head.transform.eulerAngles.y,0);
        dragBoundsYaw=rig!=null?Quaternion.Inverse(rig.rotation)*worldYaw:worldYaw;
    }
    [BeforeRenderOrder(200)]
    private void UpdateDragBeforeRender()
    {
        UpdateDragFeedback();
        if(!IsDragging)return;
        if(!isActiveAndEnabled || panel==null || !panel.activeInHierarchy ||
            (observationModes!=null&&observationModes.IsTransitioning) ||
            !Tracked(draggingHand==leftHand?leftTracked:rightTracked) ||
            draggingHand.selectAction.action==null || !draggingHand.selectAction.action.IsPressed()) {
            draggingHand=null;return;
        }
        ApplyDragPose();
    }
    private void ApplyDragPose()
    {
        var target=draggingHand.transform.TransformPoint(dragLocalOffset);
        var bounded=BoundPanelPosition(target);
        panel.transform.position=bounded;
        panel.transform.localRotation=dragPanelRotation;
        TutorialGrabMoved();
        // At a boundary, discard excess travel so reversal responds immediately.
        if((bounded-target).sqrMagnitude>.000001f)
            dragLocalOffset=draggingHand.transform.InverseTransformPoint(bounded);
    }
    private Vector3 BoundPanelPosition(Vector3 position)
    {
        Quaternion yaw=rig!=null?rig.rotation*dragBoundsYaw:dragBoundsYaw;
        Vector3 local=Quaternion.Inverse(yaw)*(position-head.transform.position);
        local.x=Mathf.Clamp(local.x,-2f,2f);local.y=Mathf.Clamp(local.y,-1.2f,1.2f);local.z=Mathf.Clamp(local.z,.55f,3f);
        return head.transform.position+yaw*Vector3.ClampMagnitude(local,3.3f);
    }
    private void PositionPanel(bool formalMenu = false)
    {
        draggingHand=null;
        if (panel != null && head != null)
        {
            Vector3 offset = !welcomeComplete && !formalMenu ? tutorialPanelViewOffset : panelViewOffset;
            panel.transform.SetPositionAndRotation(head.transform.TransformPoint(offset), head.transform.rotation);
        }
    }
    private void EnsurePanel()
    {
        if (panel != null || head == null) return;
        panel = new GameObject("Identification task", typeof(RectTransform), typeof(Canvas), typeof(Image));
        panel.transform.SetParent(rig, false);
        panelRect = panel.GetComponent<RectTransform>();
        panelRect.sizeDelta = new Vector2(660, 480);
        panelRect.localScale = Vector3.one * .0013f;
        panel.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var background = panel.GetComponent<Image>(); background.color = new Color(.025f, .04f, .07f, .97f); background.raycastTarget = false;
        body = MakeText(panel.transform, "Summary", new Vector2(0, 30), new Vector2(610, 310), 20); body.resizeTextForBestFit=true;body.resizeTextMinSize=17;body.resizeTextMaxSize=20;
        primaryRect = MakeButton("Confirm", new Vector2(0, -160), out primaryText, out primaryImage);
        removeMarkerRect=MakeButton("Remove selected marker",new Vector2(0,-105),out removeMarkerText,out removeMarkerImage);
        removeMarkerText.fontSize=18;
        removeMarkerImage.color=new Color(.4f,.12f,.07f);
        removeMarkerRect.gameObject.SetActive(false);
        submitHint = MakeText(panel.transform, "Submit guidance", new Vector2(0, -198), new Vector2(610, 24), 18);
        submitHint.alignment = TextAnchor.MiddleCenter;
        submitHint.gameObject.SetActive(false);
        if(tutorialControlHints!=null)tutorialControlHints.SetActive(false);
        backRect = MakeButton("Back to edit", new Vector2(0, -215), out var backText, out backImage);
        backText.text = "Back to edit";
        observationRect = MakeButton("Observation mode", new Vector2(0,-215), out observationText, out observationImage);
        // Share the existing bottom row; the top drag region remains unchanged.
        foreach(var rect in new[]{backRect,observationRect}) {
            rect.sizeDelta=new Vector2(440,50);rect.anchoredPosition=new Vector2(-80,-215);
            rect.GetChild(0).GetComponent<RectTransform>().sizeDelta=new Vector2(420,48);
            rect.GetChild(0).GetComponent<Text>().resizeTextForBestFit=true;
        }
        helpRect=MakeButton("Help",new Vector2(230,-215),out helpText,out helpImage);
        helpRect.sizeDelta=new Vector2(140,50);
        helpText.rectTransform.sizeDelta=new Vector2(130,48);helpText.fontSize=20;
        var handle = new GameObject("Drag Handle", typeof(RectTransform),typeof(Image));
        handle.transform.SetParent(panel.transform, false);
        dragRect = handle.GetComponent<RectTransform>();
        dragRect.sizeDelta = new Vector2(660, 90);
        dragRect.anchoredPosition = new Vector2(0, panelRect.rect.yMax - dragRect.sizeDelta.y / 2);
        dragImage=handle.GetComponent<Image>();dragImage.raycastTarget=false;
        // Nine circular glyphs use the existing UI font; no textures or raycast targets.
        for(int row=0;row<3;row++) for(int column=0;column<3;column++) {
            var dot=MakeText(handle.transform,"Grip dot "+row+"-"+column,
                new Vector2((column-1)*10,10-row*10),new Vector2(12,14),10);
            dot.text="\u25CF";dot.alignment=TextAnchor.MiddleCenter;
            dot.color=new Color(.65f,.8f,1);
        }
        BuildWelcomeContent();
        BuildTutorialButton();
        SetPanelLayout(true);UpdateDragFeedback();
        PositionPanel(); SetHover(false, false);
    }
    private void BuildWelcomeContent()
    {
        // Presentation-only container; future tutorial content can be separate from the trial state.
        welcomeContent=new GameObject("Welcome Content",typeof(RectTransform));welcomeContent.transform.SetParent(panel.transform,false);
        var title=MakeText(welcomeContent.transform,"Title",new Vector2(0,260),new Vector2(600,48),32);
        title.text="Welcome to Port of Dover XR";title.fontStyle=FontStyle.Bold;title.alignment=TextAnchor.MiddleCenter;
        var intro=MakeText(welcomeContent.transform,"Introduction",new Vector2(0,205),new Vector2(590,54),22);
        intro.text="Observe traffic on Routes A and B.\nShare your judgment about possible congestion.";intro.alignment=TextAnchor.MiddleCenter;
        string[] headings={"1   Observe & mark","2   Explore & confirm Initial","3   Inspect & submit Final"};
        string[] descriptions={"Mark up to 6 suspected locations, or choose No Congestion.","Select a marker for optional Street-level observation.\nReturn to Overview, then confirm your initial judgment.","Inspect HGVs, revise if needed, then submit your final judgment."};
        for(int i=0;i<3;i++) {
            float y=140-i*75;
            var heading=MakeText(welcomeContent.transform,"Step "+(i+1),new Vector2(0,y+17),new Vector2(590,30),24);
            heading.text=headings[i];heading.fontStyle=FontStyle.Bold;heading.color=new Color(.55f,.78f,1);
            var detail=MakeText(welcomeContent.transform,"Step detail "+(i+1),new Vector2(0,y-17),new Vector2(590,48),20);detail.text=descriptions[i];
        }
        var controls=MakeText(welcomeContent.transform,"Controls heading",new Vector2(0,-65),new Vector2(590,28),23);
        controls.text="Basic controls";controls.fontStyle=FontStyle.Bold;
        var left=MakeText(welcomeContent.transform,"Left control column",new Vector2(-155,-135),new Vector2(280,100),20);
        left.text="Right Trigger\nMark / select / click\nSelect a marker, then use menu Remove";
        var right=MakeText(welcomeContent.transform,"Right control column",new Vector2(155,-135),new Vector2(280,100),20);
        right.text="Left Stick: pan / walk\nRight Stick: Overview zoom\nLeft X: reset view & panel";
        welcomeNote=MakeText(welcomeContent.transform,"Welcome status",new Vector2(0,-225),new Vector2(590,50),20);
        welcomeNote.alignment=TextAnchor.MiddleCenter;
    }
    private void SetPanelLayout(bool welcome)
    {
        if(welcomeLayout==welcome)return;
        welcomeLayout=welcome;
        panelRect.sizeDelta=new Vector2(660,welcome?760:580);
        dragRect.anchoredPosition=new Vector2(0,panelRect.rect.yMax-45);
        primaryRect.anchoredPosition=new Vector2(0,welcome?-280:-160);
        helpRect.anchoredPosition=new Vector2(230,welcome?-340:-215);
        // Resize contents only; never reposition or recreate the grabbed panel root.
    }
    private bool HandOverHandle(ActionBasedController hand,InputAction tracking)
    {
        if(hand==null || !Tracked(tracking) || dragRect==null)return false;
        var ray=hand.GetComponent<XRRayInteractor>();if(ray==null || !ray.isActiveAndEnabled)return false;
        var origin=ray.rayOriginTransform!=null?ray.rayOriginTransform:ray.transform;
        var pointer=new Ray(origin.position,origin.forward);
        return new Plane(dragRect.forward,dragRect.position).Raycast(pointer,out float d) && d>=0 &&
            d<=Mathf.Min(4.5f,ray.maxRaycastDistance) && dragRect.rect.Contains(dragRect.InverseTransformPoint(pointer.GetPoint(d)));
    }
    private void UpdateDragFeedback()
    {
        if(dragImage==null || panel==null || !panel.activeInHierarchy)return;
        bool blocked=observationModes!=null && observationModes.IsTransitioning;
        bool hover=!blocked && (HandOverHandle(leftHand,leftTracked)||HandOverHandle(rightHand,rightTracked));
        dragImage.color=IsDragging?new Color(.08f,.43f,.58f):hover?new Color(.1f,.34f,.57f):new Color(.07f,.16f,.26f);
    }
    private RectTransform MakeButton(string name, Vector2 position, out Text text, out Image image)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(panel.transform, false);
        var rect = go.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(600, 50); rect.anchoredPosition = position;
        image = go.GetComponent<Image>(); image.raycastTarget = false;
        text = MakeText(rect, "Text", Vector2.zero, new Vector2(580, 48), 25); text.alignment = TextAnchor.MiddleCenter;
        return rect;
    }
    private Text MakeText(Transform parent, string name, Vector2 position, Vector2 size, int fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>(); rect.sizeDelta = size; rect.anchoredPosition = position;
        var text = go.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate; text.fontSize = fontSize; text.color = Color.white; text.raycastTarget = false; text.supportRichText = false;
        return text;
    }
    private void RefreshPanel()
    {
        EnsurePanel(); if (panel == null) return;
        panel.SetActive(isActiveAndEnabled);
        removeMarkerRect.gameObject.SetActive(false);
        if(welcomeComplete){body.rectTransform.sizeDelta=new Vector2(610,310);body.rectTransform.anchoredPosition=new Vector2(0,30);}
        submitHint.gameObject.SetActive(false);
        if(tutorialControlHints!=null)tutorialControlHints.SetActive(false);
        bool showWelcome=!welcomeComplete && !helpOpen;
        SetPanelLayout(showWelcome);
        welcomeContent.SetActive(showWelcome);body.gameObject.SetActive(!showWelcome);
        helpText.text=helpOpen?"Close Help":"Help";
        if(!welcomeComplete && !helpOpen) { RenderTutorial();return; }
        if(practiceRect!=null)practiceRect.gameObject.SetActive(false);
        if(showWelcome) {
            welcomeNote.text=string.IsNullOrEmpty(welcomeError)?"Take your time. Reading is not included in the trial.":welcomeError;
            welcomeNote.color=string.IsNullOrEmpty(welcomeError)?new Color(.7f,.79f,.88f):new Color(1,.75f,.4f);
            primaryRect.gameObject.SetActive(true);primaryText.text="Start Observation";
            backRect.gameObject.SetActive(false);observationRect.gameObject.SetActive(false);return;
        }
        if(!welcomeComplete || helpOpen) {
            string guide=ContextHelp();
            body.rectTransform.sizeDelta=new Vector2(610,310);body.rectTransform.anchoredPosition=new Vector2(0,30);
            if(!welcomeComplete && !string.IsNullOrEmpty(welcomeError))guide+="\n"+welcomeError;
            if(guide!=lastText){body.text=guide;lastText=guide;}
            primaryRect.gameObject.SetActive(!helpOpen && !welcomeComplete);
            primaryText.text="Start Observation";
            backRect.gameObject.SetActive(false);observationRect.gameObject.SetActive(false);
            ApplyActionHierarchy();
            return;
        }
        string title, instruction;
        switch (state.Stage)
        {
            case IdentificationStage.ObserveIdentify: title = "1 / Observe & Identify"; instruction = observationModes != null && observationModes.IsStreet ? "Inspect HGVs. Return to Overview to edit.\nInitial judgment has NOT been saved." : "Mark suspected congestion (0-" + (labels != null ? labels.MaxMarkers : IdentificationTrialState.MarkerLimit) + ").\nSelect a marker, then Inspect Location."; break;
            case IdentificationStage.ConfirmInitial: title = "2 / Confirm Initial Identification"; instruction = "Review your markers or select No Congestion.\nUse Back to edit if needed."; break;
            case IdentificationStage.SelectInspect: title = "3 / Select & Inspect"; instruction = observationModes != null && observationModes.IsStreet ? "Inspect HGVs from the roadside.\nReturn to Overview to edit markers." : "Inspect HGVs and revise your markers.\nYour initial judgment has been saved."; break;
            case IdentificationStage.ConfirmFinal: title = "4 / Confirm & Submit"; instruction = saveFailed ? "Save failed. Response retained.\nPlease retry saving before continuing." : "Inspect traffic or adjust markers.\nWhen satisfied, select Submit Final Answer."; break;
            case IdentificationStage.Submitted: title = "Task Completed"; instruction = nextTrialFailed ? "Next trial could not start. Please retry.\nPrevious response saved on this PC." : "Your answers have been saved.\nThank you for participating!\nPlease inform the researcher and remove your headset when ready."; break;
            default: title = "Waiting for a new trial"; instruction = "Start the traffic scenario to begin."; break;
        }
        bool street=observationModes!=null && observationModes.IsStreet;
        if(state.CanEdit) instruction=street ? "Observe nearby traffic. Return to Overview when ready." : state.CanInspect ?
            "Inspect traffic or adjust markers.\nWhen satisfied, select Submit Final Answer." :
            "Observe Routes A and B. Mark suspected congestion locations.";
        if(street && state.CanEdit) instruction="Point at a truck and press the Right Trigger to view its information.\nReturn to Overview when you are ready to submit.";
        if(state.Stage==IdentificationStage.ConfirmFinal && !saveFailed && labels!=null && labels.Labels.Count==0)
            instruction="No markers: submitting explicitly confirms No Congestion.\nSubmission locks your response.";
        string hint=street?"Left Stick: move | Left X: reset | Trigger: inspect/UI":state.CanEdit?
            "Left Stick: pan | Right Stick: zoom | Left X: reset":"Right Trigger: choose a button | Help: controls";
        var summary = new StringBuilder(title + "\n" + instruction + "\n" + hint + "\n");
        if (state.Stage != IdentificationStage.Idle && state.Stage != IdentificationStage.Submitted)
        {
            summary.Append("Markers: ").Append(labels != null ? labels.Labels.Count : 0).Append(" / ").Append(labels != null ? labels.MaxMarkers : IdentificationTrialState.MarkerLimit).Append(" | Selected: ").Append(labels != null ? labels.SelectedMarkerNumber : "None").Append('\n');
            if (labels != null) foreach (var label in labels.Labels)
                summary.Append(label.Number).Append(" — Route ").Append(label.RouteId).Append(" · Position ").Append(Math.Round(label.RouteProgress, 0, MidpointRounding.AwayFromZero).ToString("F0", System.Globalization.CultureInfo.InvariantCulture)).Append(" m\n");
        }
        if(observationModes!=null && observationModes.Status!="Overview") summary.Append(observationModes.Status).Append('\n');
        // Persistent normal-menu guidance, independent of selection, hover and Help.
        // Marker details remain in Overview, leaving room for readable roadside instructions.
        if(street) {
            summary.Clear();
            summary.Append("Street-level\n\nInspect a Truck\n")
                .Append("Point at an HGV and press the Right Trigger to view its information.\n\n")
                .Append("Left Stick: Move\nLeft X: Reset viewpoint and panel\n\n")
                .Append("Return to Overview\nReturn when ready to edit markers or submit.");
            body.rectTransform.sizeDelta=new Vector2(610,310);
            body.rectTransform.anchoredPosition=new Vector2(0,30);
        }
        string content = summary.ToString(); if (content != lastText) { body.text = content; lastText = content; }
        bool review = state.Stage == IdentificationStage.ConfirmInitial || state.Stage == IdentificationStage.ConfirmFinal;
        backRect.gameObject.SetActive(review && state.PendingResult==null && (observationModes == null || (!observationModes.IsStreet && !observationModes.IsTransitioning)));
        observationRect.gameObject.SetActive(observationModes != null && (state.CanEdit || observationModes.IsStreet || observationModes.IsTransitioning));
        observationText.text = observationModes != null && observationModes.IsPreparing ? "Preparing... (Cancel)" : observationModes != null && observationModes.IsTransitioning ? "Changing view..." : observationModes != null && observationModes.IsStreet ? "Return to Overview" : labels == null || labels.SelectedMarker == null ? "Select a Marker first" : "Inspect Location: " + labels.SelectedMarkerNumber;
        primaryRect.gameObject.SetActive((state.CanEdit || review || state.Stage==IdentificationStage.Submitted) && (observationModes == null || (!observationModes.IsStreet && !observationModes.IsTransitioning)));
        primaryText.text = saveFailed ? "Retry Save" : state.Stage==IdentificationStage.Submitted ? "Start Next Trial" : state.CanEdit ? (state.CanInspect ? "Submit Final Answer" : "Confirm Initial") :
            (labels != null && labels.Labels.Count == 0 ? (state.Stage == IdentificationStage.ConfirmFinal ? "Submit Final Answer" : "Confirm No Congestion") :
            (state.Stage == IdentificationStage.ConfirmFinal ? "Submit Final Answer" : "Confirm Initial Markers"));
        bool finalAction = !saveFailed && primaryRect.gameObject.activeSelf &&
            (state.Stage==IdentificationStage.SelectInspect || state.Stage==IdentificationStage.ConfirmFinal);
        submitHint.gameObject.SetActive(finalAction);
        submitHint.text="Finish the task and save your response.";
        float bottomY=finalAction?-240:-215;
        backRect.anchoredPosition=new Vector2(backRect.anchoredPosition.x,bottomY);
        observationRect.anchoredPosition=new Vector2(observationRect.anchoredPosition.x,bottomY);
        helpRect.anchoredPosition=new Vector2(helpRect.anchoredPosition.x,bottomY);
        RefreshMarkerMenu();
        ApplyActionHierarchy();
    }
    private void OnDestroy()
    {
        if (panel == null) return;
        if (Application.isPlaying) Destroy(panel); else DestroyImmediate(panel);
    }
}



