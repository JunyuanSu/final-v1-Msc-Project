using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;

public sealed partial class IdentificationTrialController
{
    private int tutorialStep=1, pendingTutorialStep;
    private readonly bool[] tutorialDone=new bool[9];
    private XRViewControls tutorialView;
    private float panBaseline,zoomBaseline,streetBaseline;
    private bool panDone,zoomDone,streetMoved,returnRequested,grabMoved;
    private Vector3 grabStart,returnPosition;
    private Quaternion returnRotation;
    private ActionBasedController practiceGrabHand;
    private RectTransform practiceRect;
    private Text practiceText;
    private Image practiceImage;
    private GameObject tutorialControlHints;
    private int controlHintStep = -1;

    // Decorative controls only: no colliders, raycast targets or input bindings.
    private void ShowTutorialControlHints()
    {
        // Welcome already has its own controls block and status row.
        // Do not place the step-specific icon strip over that status text.
        if (tutorialStep == 1) {
            if (tutorialControlHints != null) tutorialControlHints.SetActive(false);
            return;
        }
        if (controlHintStep != tutorialStep)
        {
            if (tutorialControlHints != null) {
                tutorialControlHints.SetActive(false);
                Destroy(tutorialControlHints);
            }
            tutorialControlHints = new GameObject("Tutorial control icons", typeof(RectTransform));
            tutorialControlHints.transform.SetParent(panel.transform, false);
            var root = tutorialControlHints.GetComponent<RectTransform>();
            root.anchoredPosition = new Vector2(0, -49);
            root.sizeDelta = new Vector2(600, 46);
            string[] kinds, captions;
            if (tutorialStep == 3) { kinds=new[]{"dots","trigger"}; captions=new[]{"Drag handle","Either Trigger"}; }
            else if (tutorialStep == 4) { kinds=new[]{"stick","stick"}; captions=new[]{"Left Stick: pan","Right Stick: zoom"}; }
            else if (tutorialStep == 7) { kinds=new[]{"stick","trigger","x"}; captions=new[]{"Left Stick","Right Trigger","Left X: reset"}; }
            else { kinds=new[]{"trigger"}; captions=new[]{"Right Trigger: select"}; }
            float width=600f/kinds.Length;
            for (int i=0;i<kinds.Length;i++) {
                float x=-300+width*i+width/2;
                var card=new GameObject("Control hint",typeof(RectTransform));
                card.transform.SetParent(root,false);
                card.GetComponent<RectTransform>().anchoredPosition=new Vector2(x,0);
                float iconX=-width/2+25;
                if(kinds[i]=="dots") {
                    for(int row=0;row<3;row++) for(int column=0;column<3;column++)
                        HintGlyph(card.transform,"Dot","●",new Vector2(iconX+(column-1)*10,(1-row)*10),14);
                } else if(kinds[i]=="stick") {
                    HintGlyph(card.transform,"Stick cap","●",new Vector2(iconX,9),28);
                    HintBar(card.transform,new Vector2(iconX,-5),new Vector2(5,16));
                    HintBar(card.transform,new Vector2(iconX,-14),new Vector2(28,4));
                } else if(kinds[i]=="x") {
                    HintGlyph(card.transform,"Button outline","○",new Vector2(iconX,0),42);
                    HintGlyph(card.transform,"X button","X",new Vector2(iconX,0),20);
                } else {
                    HintBar(card.transform,new Vector2(iconX-7,7),new Vector2(6,26));
                    HintBar(card.transform,new Vector2(iconX+3,18),new Vector2(20,5));
                    var lever=HintBar(card.transform,new Vector2(iconX+7,4),new Vector2(7,24));
                    lever.localRotation=Quaternion.Euler(0,0,-25);
                }
                var caption=MakeText(card.transform,"Control name",new Vector2(22,0),new Vector2(width-62,44),18);
                caption.text=captions[i];caption.alignment=TextAnchor.MiddleLeft;
            }
            controlHintStep=tutorialStep;
        }
        tutorialControlHints.SetActive(true);
    }
    private void HintGlyph(Transform parent,string name,string glyph,Vector2 position,int size)
    {
        var text=MakeText(parent,name,position,new Vector2(44,44),size);
        text.text=glyph;text.alignment=TextAnchor.MiddleCenter;text.color=new Color(.75f,.9f,1f);
    }
    private RectTransform HintBar(Transform parent,Vector2 position,Vector2 size)
    {
        var go=new GameObject("Control shape",typeof(RectTransform),typeof(Image));
        go.transform.SetParent(parent,false);
        var rect=go.GetComponent<RectTransform>();rect.anchoredPosition=position;rect.sizeDelta=size;
        var image=go.GetComponent<Image>();image.color=new Color(.75f,.9f,1f);image.raycastTarget=false;
        return rect;
    }
    private bool TutorialReady=>tutorialDone[7] && observationModes!=null && !observationModes.IsStreet && !observationModes.IsTransitioning;
    private bool TutorialBusy=>observationModes!=null && observationModes.IsTransitioning;

    private void InitializeTutorial()
    {
        tutorialView=rig!=null?rig.GetComponent<XRViewControls>():null;
        if(labels!=null){labels.ClearLabels();labels.SetPracticeMode(true);}
        if(observationModes!=null){observationModes.RestoreForTrialChange();observationModes.TutorialAccess=false;}
        if(tutorialView!=null)tutorialView.ResetView();
        tutorialDone[1]=true;
    }
    private void ApplyTutorialPermissions()
    {
        if(labels!=null) {
            labels.EditingAllowed=isActiveAndEnabled && !helpOpen && tutorialStep==5;
            labels.RoadEditingAllowed=observationModes==null || (!observationModes.IsStreet && !observationModes.IsTransitioning);
        }
        if(inspection!=null){inspection.SelectionAllowed=false;inspection.ClearSelection();}
        if(observationModes!=null)observationModes.TutorialAccess=isActiveAndEnabled && (tutorialStep==6 || tutorialStep==7 || pendingTutorialStep!=0);
    }
    private void TickTutorial()
    {
        if(welcomeComplete)return;
        ApplyTutorialPermissions();
        if(pendingTutorialStep!=0 && !TutorialBusy && observationModes!=null && !observationModes.IsStreet) {
            int next=pendingTutorialStep;pendingTutorialStep=0;EnterTutorialStep(next);
        }
        if(tutorialStep==3 && practiceGrabHand!=null && !IsDragging) {
            var tracking=practiceGrabHand==leftHand?leftTracked:rightTracked;
            if(grabMoved && Tracked(tracking) && practiceGrabHand.selectAction.action!=null &&
                !practiceGrabHand.selectAction.action.IsPressed() && !TutorialBusy)tutorialDone[3]=true;
            practiceGrabHand=null;
        }
        if(tutorialStep==4 && tutorialView!=null) {
            panDone|=tutorialView.SuccessfulPanDistance-panBaseline>=.1f;
            zoomDone|=tutorialView.SuccessfulZoomDistance-zoomBaseline>=.1f;
            tutorialDone[4]|=panDone&&zoomDone;
        }
        if(tutorialStep==5 && labels!=null && labels.PracticeSelected!=null)tutorialDone[5]=true;
        if(tutorialStep==6 && observationModes!=null && observationModes.IsStreet && !TutorialBusy)tutorialDone[6]=true;
        if(tutorialStep==7 && observationModes!=null) {
            streetMoved|=observationModes.SuccessfulStreetDistance-streetBaseline>=.05f;
            if(streetMoved && returnRequested && !TutorialBusy && !observationModes.IsStreet && rig!=null &&
                Vector3.Distance(rig.position,returnPosition)<.05f && Quaternion.Angle(rig.rotation,returnRotation)<1)tutorialDone[7]=true;
        }
    }
    private void TutorialGrabStarted()
    {
        if(welcomeComplete || tutorialStep!=3)return;
        practiceGrabHand=draggingHand;grabStart=panel.transform.localPosition;grabMoved=false;
    }
    private void TutorialGrabMoved()
    {
        if(!welcomeComplete && tutorialStep==3 && practiceGrabHand==draggingHand && IsDragging)
            grabMoved|=Vector3.Distance(panel.transform.localPosition,grabStart)>=.10f;
    }
    private void EnterTutorialStep(int step)
    {
        tutorialStep=step;practiceGrabHand=null;
        if(step==4 && tutorialView!=null) {
            panBaseline=tutorialView.SuccessfulPanDistance;zoomBaseline=tutorialView.SuccessfulZoomDistance;
            if(!tutorialDone[4]){panDone=false;zoomDone=false;}
        }
        if(step==7 && observationModes!=null)streetBaseline=observationModes.SuccessfulStreetDistance;
        ApplyTutorialPermissions();RefreshPanel();
    }
    private void TutorialNavigate(int target)
    {
        if(TutorialBusy || pendingTutorialStep!=0)return;
        if(observationModes!=null && observationModes.IsStreet && target<6) {
            pendingTutorialStep=target;observationModes.RequestToggle();return;
        }
        EnterTutorialStep(target);
    }
    private void BuildTutorialButton()
    {
        practiceRect=MakeButton("Tutorial practice action",new Vector2(0,-105),out practiceText,out practiceImage);
        practiceRect.sizeDelta=new Vector2(600,44);practiceText.rectTransform.sizeDelta=new Vector2(580,42);
        practiceRect.gameObject.SetActive(false);
    }
    private void HandleTutorialClick(Vector3 world,bool pressed)
    {
        bool hitPractice=practiceRect.gameObject.activeSelf && practiceRect.rect.Contains(practiceRect.InverseTransformPoint(world));
        bool hitNext=primaryRect.gameObject.activeSelf && primaryRect.rect.Contains(primaryRect.InverseTransformPoint(world));
        bool canNext=tutorialStep==8?TutorialReady:CanAdvanceTutorial();
        practiceImage.color=hitPractice?new Color(.1f,.5f,.7f):new Color(.08f,.23f,.35f);
        ApplyActionHierarchy(hitNext, false, false, hitPractice);
        if(!pressed || TutorialBusy || pendingTutorialStep!=0)return;
        if(practiceRect.gameObject.activeSelf && practiceRect.rect.Contains(practiceRect.InverseTransformPoint(world))) {
            if(tutorialStep==2)tutorialDone[2]=true; // Only this right-ray UI hit can complete trigger practice.
            else if((tutorialStep==6 || tutorialStep==7) && observationModes!=null) {
                if(!observationModes.IsStreet) {
                    returnPosition=rig.position;returnRotation=rig.rotation;
                    observationModes.RequestToggle();
                } else if(tutorialStep==7 && (streetMoved||tutorialDone[7])) {
                    returnRequested=true;observationModes.RequestToggle();
                }
            }
        } else if(backRect.gameObject.activeSelf && backRect.rect.Contains(backRect.InverseTransformPoint(world))) {
            TutorialNavigate(tutorialStep-1);
        } else if(primaryRect.gameObject.activeSelf && primaryRect.rect.Contains(primaryRect.InverseTransformPoint(world))) {
            if(tutorialStep==8) {if(TutorialReady)StartObservation();}
            else if(CanAdvanceTutorial())TutorialNavigate(tutorialStep+1);
        }
        RefreshPanel();
    }
    private bool CanAdvanceTutorial()
    {
        if(TutorialBusy || !tutorialDone[tutorialStep])return false;
        if(tutorialStep==5)return labels!=null && labels.PracticeSelected!=null;
        if(tutorialStep==6)return observationModes!=null && observationModes.IsStreet;
        if(tutorialStep==7)return observationModes!=null && !observationModes.IsStreet;
        return true;
    }
    private void RenderTutorial()
    {
        bool welcome=tutorialStep==1;
        SetPanelLayout(welcome);welcomeContent.SetActive(welcome);body.gameObject.SetActive(!welcome);
        body.rectTransform.sizeDelta=new Vector2(610,210);body.rectTransform.anchoredPosition=new Vector2(0,90);
        ShowTutorialControlHints();
        primaryRect.gameObject.SetActive(true);primaryText.text=tutorialStep==8?"Start Observation":"Next";
        primaryImage.color=(tutorialStep==8?TutorialReady:CanAdvanceTutorial())?new Color(.08f,.3f,.5f):new Color(.15f,.18f,.22f);
        backRect.gameObject.SetActive(tutorialStep>1);observationRect.gameObject.SetActive(false);
        practiceRect.gameObject.SetActive(tutorialStep==2 || tutorialStep==6 || tutorialStep==7);
        practiceText.text=tutorialStep==2?"Practice Button":observationModes!=null && observationModes.IsStreet?"Return to Overview":"Inspect Location";
        ApplyActionHierarchy();
        if(welcome){welcomeNote.text="Step 1 / 8 - Read the introduction, then select Next.";return;}
        string title="",instruction="",status=tutorialDone[tutorialStep]?"Completed - select Next.":"Complete the action to unlock Next.";
        switch(tutorialStep) {
            case 2:title="Learn to Select";instruction="Point at the Practice Button and press the Right Trigger.";if(tutorialDone[2])status="Great! You can now select objects.";break;
            case 3:title="Move the Task Panel";instruction="Point at the handle shown below, hold either Trigger, move the panel, then release.";if(tutorialDone[3])status="Great! You can reposition the panel.";break;
            case 4:title="Explore the Map";instruction="Use the Left Stick to pan and the Right Stick to zoom, one at a time.";status=(panDone||tutorialDone[4]?"Pan completed":"Pan: move the view")+"\n"+(zoomDone||tutorialDone[4]?"Zoom completed":"Zoom: move closer or farther");break;
            case 5:title="Place and Select a Marker";instruction="Point at Route A or B and press Right Trigger to place a Practice marker. Select its badge to continue.";status="Practice only - no trial data is recorded.\nUse menu Remove to try another location.\n"+(labels!=null&&labels.PracticeSelected!=null?"Practice marker selected.":"Place and select one marker.");break;
            case 6:title="Inspect Traffic at Street Level";instruction="Choose Inspect Location for your selected Practice marker.";status=observationModes!=null?observationModes.Status:"Street-level unavailable.";status+="\nIf unsafe, go Back and choose another marker.";break;
            case 7:title="Move and Return";instruction="Use the Left Stick to move, then select Return to Overview.\nLeft X: Reset View (optional).";status=(streetMoved||tutorialDone[7]?"Movement completed.":"Move a short, safe distance.")+"\n"+(tutorialDone[7]?"Returned to Overview. Select Next.":"Return after moving to unlock Next.");break;
            case 8:title="You're Ready!";instruction="You have completed the tutorial.\nObserve traffic, mark suspected congestion or choose No Congestion, and submit Initial / Final judgments.";status=string.IsNullOrEmpty(welcomeError)?"Start Observation clears practice and begins the timed trial.":welcomeError;break;
        }
        // Keep the text within its dedicated region above the control icons.
        string content="Step "+tutorialStep+" / 8\n"+title+"\n"+instruction+"\n"+status;
        body.text=content;lastText=content;
        RefreshMarkerMenu();
    }
    private void CleanupTutorial()
    {
        if(observationModes!=null){observationModes.RestoreForTrialChange();observationModes.TutorialAccess=false;}
        if(labels!=null){labels.ClearLabels();labels.SetPracticeMode(false);}
        if(inspection!=null)inspection.ClearSelection();
        if(tutorialView!=null)tutorialView.ResetView();
        practiceGrabHand=null;pendingTutorialStep=0;PositionPanel(true);
        body.rectTransform.sizeDelta=new Vector2(610,310);body.rectTransform.anchoredPosition=new Vector2(0,30);
    }
    private void FinishTutorialState()
    {
        System.Array.Clear(tutorialDone,0,tutorialDone.Length);
        tutorialStep=0;panDone=zoomDone=streetMoved=returnRequested=grabMoved=false;
        panBaseline=zoomBaseline=streetBaseline=0;
        practiceGrabHand=null;pendingTutorialStep=0;
    }
}
