# Port of Dover XR Foundation

Unity 2022.3.55f1, OpenXR 1.14.0, XR Interaction Toolkit 2.5.4.

The existing XR container now owns one XR Origin, a tracked camera and two
action-based controllers with straight XR Ray Interactors. Observation.inputactions
contains only head/controller tracking and trigger inputs. Rays use controller aim
poses, reach 2000 Unity metres, stop at colliders, and hide when tracking is lost.
No vehicle interactables, selection workflow, UI interaction or locomotion are added.
Later stages can use XRRayInteractor.TryGetCurrent3DRaycastHit for collider hits.

The origin uses Floor tracking with a 1.6 m Device-mode fallback. Its initial eye
position approximates the previous observation camera. Only the original yaw is
retained: pitch and roll belong to the headset. The origin stays fixed; natural
physical head movement is retained. Initial altitude, viewing angle and comfort
must be checked on Quest before participant use. World scale remains 1.

The original Main Camera and DesktopCameraController remain unchanged on an
inactive GameObject. Keep that object inactive during XR use to avoid duplicate
cameras, listeners and desktop movement. No desktop simulation/input rig is added.

## Device validation still required

- Install Android Build Support, SDK/NDK Tools and OpenJDK for this Unity version
  through Unity Hub. No build module was installed by this change.
- Select Android and re-run XR Plug-in Management > Project Validation. OpenXR,
  Meta Quest Support, Touch and Touch Plus profiles are configured. ARM64, IL2CPP
  and minimum API 29 are set. Existing graphics APIs and color space are preserved.
  Review Android Active Input Handling (currently Both) before the first APK build.
- Ensure PortOfDover is enabled in Build Settings; connect an authorized Quest 3
  with developer mode/USB debugging, then build and test. For PC VR testing use
  Quest Link and the Meta OpenXR runtime. This project does not change the PC runtime.
- Check stereo rendering, floor height, initial direction, both aim rays and triggers,
  ray disappearance on tracking loss, and sustained frame rate. No Quest test passed
  simply because the project compiles.
- Existing CartoBasemap reads a Windows/process environment variable. That Windows
  credential is not deployed to Android. Secure device-side map authentication and
  VR-visible map attribution remain deployment prerequisites; map code was not changed.
- Existing terrain cliffs and low map contrast have not been resolved in this stage.

Editor validation on 2026-10-13 found one origin/camera/listener, valid action
references and OpenXR profiles, and no blocking OpenXR validation issues with the
currently installed modules. Two optional PoseControl/StickControl migration notes
remain. Android build support and a connected headset were unavailable.

## Identify & Label (participant hypotheses)

Right Trigger on exposed terrain within 2.5 m of Route A/B's ordered waypoint
centreline places an amber outline and `? A` / `? B`. Buildings, vehicles and
terrain outside these corridors are rejected. Near-equal route distances are
rejected rather than guessing a direction. Wait for terrain colliders to load.
Aim at the road inside an existing outline and press Trigger to delete it;
then point elsewhere to re-mark. At most two labels total; zero is valid.
There is no ground-truth congestion lookup or correctness feedback.

CongestionLabelInteractor is attached to the existing right controller and reuses
its Select/trigger action and tracked ray. Labels expose route reference/id,
terrain hit position, segment and horizontal route progress through a read-only
list. LabelsChanged/ClearLabels provide a small integration boundary. No collider,
traffic changes, desktop controls, submission or data persistence are added.
Labels clear on component disable/Play exit. Traffic Reset is deliberately not
wired to labels; a later trial lifecycle must explicitly call ClearLabels.

Device checks still required: both route corridors at the actual observation
height, road-hit precision between trucks, ring/text legibility, repeated press
and release, delete/re-place, max two and tracking loss. Terrain-only hit validation
means a truck/building occluding the road blocks placement until the road is visible.

## Select & Inspect

The same right Trigger now dispatches by the first XR ray hit: an HGV selects it,
the same HGV again closes inspection, another HGV replaces selection. A nonvehicle
hit or empty ray clears inspection; valid road hits still perform Identify & Label.
There is only one Trigger reader. Input actions and vehicle colliders are unchanged.

A fixed world-space panel appears 1.6 m ahead, offset right/down from the head pose
at selection. It shows route-prefixed spawn name (unique within the current run),
Route A/B and measured horizontal route-progress speed in km/h. The initial reading
is -- until sampled. No configured speed, waiting-time guess or scenario truth is
shown. Vehicle inactive/destroyed/finished clears selection. The panel has no collider
or UI raycaster. VehicleRouteFollower only adds a read-only AssignedRoute property.

Quest validation remains required for distant HGV aiming, panel text size/placement,
stereo readability and Trigger coexistence with road labels. The panel stays in the
world after selection; reselect a vehicle to reposition it relative to the new view.

## VR View Controls

Right stick Y: bounded zoom along initial yaw tilted 35 degrees downward (dolly,
not FOV). Left stick: horizontal pan relative to head yaw. Left X: instant reset
of rig initial position/yaw, with smoothing state cleared; tracked head pose is
untouched. Existing Trigger dispatch and input reference IDs are unchanged.
The shared Observation action asset adds ViewPan/ViewZoom/ViewReset; the existing
InputActionManager owns their lifecycle. No turn, teleport or desktop bindings.

XRViewControls lives on XR Origin. Inspector defaults: zoom/pan 15 m/s, dead zone
0.2, smooth time 0.25 s, zoom -80..120 m, pan radius 80 m, combined horizontal
radius 140 m, vertical range -80..+60 m relative to initial rig position, map
clearance 20 m. Positions are Unity world metres in the existing local georeference;
negative world Y is not treated as below ground. No georeference changes.
Loaded Terrain/Buildings collider checks block unsafe movement; no loaded terrain
below the candidate eye also blocks movement. This is not a guarantee against
future streamed geometry or physical head movement; validate boundaries on Quest.
Reset returns to the authored initial observation pose. Inspection panels now move
with the rig, while retaining their position relative to the rig rather than head.
Quest stereo visibility, comfort, physical head offsets and boundary coverage
remain device-validation items. PC Multi Pass from the preceding rendering fix
is retained; Android rendering mode is unchanged.

## Identification trial stages / Confirm & Submit

IdentificationTrialController on the right controller observes the existing traffic
trial number and Idle state. Start Scenario begins Observe & Identify; traffic Reset
clears labels, selection, snapshots and submission state. Reset then Start within one
frame is detected by the new trial number. View Reset does not clear judgments.

Use the existing right Trigger to point and click the task panel. Panel hits take
priority over vehicle/road hits and consume the press, including its blank area.
The panel uses ray-plane hit testing with world-hit occlusion, no extra input actions,
physics colliders or EventSystem trigger reader. It follows the rig, not head turns.

1. Observe & Identify: edit up to two labels; vehicle inspection is blocked.
2. Review Initial Identification: edits locked; confirm markers or explicitly click
   Confirm No Congestion for zero labels. Back to edit is available before confirmation.
3. Select & Inspect: select HGVs and revise labels; initial snapshot stays immutable.
4. Review Final Identification: edits/inspection locked; confirm and submit markers,
   or explicitly Submit No Congestion. Back to edit returns to inspection.
5. Submission Complete: labels, inspection and repeated submission are locked.

IdentificationSnapshot stores read-only marker count/list (route ID/name, segment,
along-route progress in metres, world position), relative confirmation time and
explicit No Congestion. IdentificationTrialResult contains trial number, initial/final
snapshots, EditedAfterInitial and FinalDiffersFromInitial (unordered comparison, 1 cm
position/progress tolerance). Deleting then restoring a marker can set the first flag
without the second. Timestamps are real elapsed seconds from detected trial start,
including traffic pauses; they do not use accelerated simulation time.

Initial/Result properties and the one-shot Submitted event expose structured data
for future logging. Results are in memory only and cleared on the next trial/reset;
there is no disk export, correctness feedback or participant answer scoring.
Unity compile and isolated component/state checks passed. Quest controller clicks,
stereo readability, panel placement and full in-headset workflow remain unverified.

## Visual interaction feedback

Road hypotheses retain the same world position and immutable route/progress data.
Amber base rings and vertical pointers now use thicker lines and larger, route-local
numbering (A1/A2 or B1/B2), renumbered after deletion. Only text orientation faces the
view; marker positions never follow the rig. Moving a marker remains delete/re-add.
A translucent amber ring previews valid road placement; orange previews deletion.
At the two-marker limit no placement preview appears except near a removable marker.

During Select & Inspect only, a pale cyan ellipse previews a selectable HGV and a
strong cyan ellipse identifies the selected HGV. Both follow vehicle position/yaw.
Exactly two shared, reusable vehicle ring objects are allocated on demand, not one
per truck. The road preview is also reused; preview route resolution allocates no
Label objects. No feedback objects have colliders or alter vehicle materials.

The existing Trigger dispatcher clears hover/preview before resolving the task UI,
so task panel hits consume input and suppress world feedback. Lost tracking hides
hover/preview. Selection and phase/reset cleanup retain the existing rules. Short
1.6-second messages acknowledge marking/removal/selection/clearing without correctness
feedback. View controls/input actions and traffic simulation remain unchanged.

Unity compilation and isolated stage/component tests passed: valid/invalid preview,
number/location consistency, early hover/inspect blocking, moving selected ring,
deselect/inactive/destroy cleanup, switching route/vehicle information, UI consumption,
immutable snapshots, submission lock and new-trial cleanup. Quest stereo visibility,
distant aiming, ring size against loaded terrain and comfort/readability after pan/zoom
still require in-headset validation; these are not claimed as device-tested.

## Road marking reliability update (supersedes proximity deletion above)

The current-frame candidate is now authoritative for preview and Trigger commit.
One fresh pointer raycast supplies UI priority, explicit badge removal, vehicle or
road resolution. No stale-frame aim grace is used; frame/revision checks reject
stale or twice-consumed candidates. Tracking loss still rejects input.

Road clicks only add. Clicking within the existing 3 m duplicate protection area
never deletes; aim specifically at the visible [X] Remove strip on the badge.
Deletion is an occlusion-tested analytic hit with no collider. At most two markers
remain allowed and all existing stage gates/snapshot formats are preserved.

Road Half Width stays at the 2.5 m baseline. Temporary isolated tests evaluated
3.25 m, without changing the scene. Snapping projects to the chosen route XZ and
checks loaded terrain vertically within +/-5 m of the original surface height.
Non-terrain first hits, obstructed snap positions and ambiguous A/B are rejected.
Preview, marker world position and stored snapshot position use that same result.
This corridor is based on authored routes, not semantic recognition of CARTO roads;
real loaded terrain and map alignment remain headset validation requirements.

Ray/status colors: green Add, orange Remove, gray invalid, magenta Maximum Markers,
cyan vehicle, blue task UI. Number badges have dark backing, a visible delete strip,
and bounded 1x-3x distance scaling; road anchors remain fixed. Success text lasts
1.6 s. The task panel default offset is now (-1.05,-0.3,1.8) m relative to the head
at placement, configurable separately from phase logic. No grip drag or new input
binding is implemented.

Diagnostic Logging is off by default. Enable it temporarily to log state changes
for unpressed Trigger, unavailable tracking/ray, miss, validation failure, UI,
maximum count, Add and Remove. CurrentCandidate and LastDiagnostic are available
for developer inspection. This observes app input, not proof of physical button
presses that the device/runtime never delivered.

Compile and isolated tests passed: 20/20 fresh Add candidates committed once;
repeat road clicks do not delete; explicit removal works; A/B preview/saved snap
positions agree; stale candidates, buildings and obstructed snaps are rejected;
UI priority, immutable snapshots, early Inspect block, submit lock, Reset/new trial
and view-reset judgment preservation remain covered. Real Trigger delivery,
post-Zoom/Pan aiming, streamed Cesium surfaces, badge legibility and stereo comfort
still require Quest validation. Temporary editor harness removed after testing.
