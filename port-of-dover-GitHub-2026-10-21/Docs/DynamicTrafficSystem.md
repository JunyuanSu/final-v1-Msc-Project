# Dynamic Traffic Scenario System

Development / Experimental parameters only; not calibrated Port of Dover traffic data.

## Scope and operation

The existing TrafficScenarioManager remains the only traffic simulation controller. VehicleRouteFollower, TestTruck, route/waypoints, Cesium and DesktopCameraController are unchanged. There is no TrialManager, participant UI, experiment DataLogger, zone system or multi-bottleneck model.

In Play Mode select PortOfDover/Traffic/TrafficStates:

- **Start Selected Scenario** uses Selected Source. Predefined index 0 is ContinuousFreeFlow; index 1 is ContinuousCongestion. FiniteBaseline uses the original Scenario enum and 5/10-vehicle settings.
- **Start Random Trial** selects from the two predefined configurations. Only the scenario choice is random.
- **Pause / Resume** freezes/resumes the current traffic trial, including spawn countdown, service clocks and scenario time.
- **Reset** immediately deactivates the owned vehicle container, schedules its destruction, clears active vehicles, counters, queue/service state, locked settings and current trial identity, and returns to Idle. Another start is required.
- **Start Finite Baseline** and the existing public `StartScenario()` method preserve the original finite regression entry point. Calling `StartScenario()` while paused resumes the same run, as before.
- **Restart Random Sequence (current seed)** is available only in Idle. It recreates the selection RNG without starting traffic.

The saved Scene stays idle until a start command is used. Default Selected Source is Predefined, Selected Index is 0. Original finite Scenario remains Congestion.

## Configurations

| Setting | ContinuousFreeFlow | ContinuousCongestion | Original finite FreeFlow | Original finite Congestion |
|---|---:|---:|---:|---:|
| Continuous | true | true | false | false |
| Finite count | ignored | ignored | 5 | 10 |
| Normal speed (m/s) | 10 | 10 | 10 | 10 |
| Requested spawn interval (s) | 8 | 12 | 8 | 4 |
| Centre spacing along route (m) | 16 | 16 | 16 | 16 |
| Processing enabled | false | true | false | true |
| Processing point | unused | WP_12 | unused | WP_12 |
| Processing delay (s) | unused | 12 | unused | 12 |
| Slow zone enabled | false | true | false | true |
| Slow start / speed | unused | WP_09 / 2.5 m/s | unused | WP_09 / 2.5 m/s |
| Maximum active admission | 16 | 16 | not applied | not applied |

TrafficScenarioConfiguration is a small serializable class embedded in the Manager. All settings, IDs and explicit waypoint references are stored in the Scene. The Manager copies the chosen values and computes point progress once at start. Inspector edits do not change a running or paused trial. Reordering/moving route objects at runtime remains unsupported.

## Continuous admission and processing capacity

The 12 s processing delay is not the complete saturated departure headway. The next vehicle must also advance approximately 16 / 2.5 = 6.4 s. Nominal saturated processing capacity is therefore about 3600 / 18.4 = 195.65 vehicles/hour, before frame-step effects.

The original finite 4 s spawn interval corresponds to 900 vehicles/hour while generation lasts. Removing its count limit alone would overload this single processing point and grow the queue towards the entrance. This implementation does not claim otherwise.

ContinuousCongestion instead requests a vehicle every 12 s (300 vehicles/hour) and explicitly limits admission to 16 active vehicles. This demand is still greater than service capacity: stability comes from **visible admission control**, not from pretending the requested arrival rate is below capacity. When capacity is full or the entrance spacing is insufficient, the countdown remains at zero. Once both constraints allow it, exactly one vehicle is created and a fresh interval begins. There is no stored arrival backlog and no catch-up burst. In saturation, completions make room for new admissions, so admitted flow follows processing capacity.

The active limit bounds simulated occupancy and memory, while the particular limit/route/delay combination must still be tested for a persistent queue and safe distance from the entrance. Arbitrary user edits to these development parameters are not guaranteed to preserve the tested behaviour.

Vehicles are deactivated, destroyed and removed from the active list at the existing final-waypoint tolerance. No per-completed-vehicle objects or history are kept. Spawned/Completed are constant-memory 64-bit cumulative counters, guarded against overflow; they reset each trial. Continuous mode never enters Completed merely because the old finite count has been reached.

## Selection and reset semantics

The dedicated System.Random uses integer seed 1234 by default; it does not consume Unity's global random state. The same seed, unchanged pool order and same Avoid Immediate Repeat setting reproduce the selection sequence within this Unity/runtime version.

Traffic Reset preserves RNG progression and the session trial-number counter so repeated trials do not continually choose the first seeded result. It clears the current trial number/ID and all traffic state. Restart Random Sequence explicitly restarts selection; it does not reset session numbering. Changing the seed is picked up at the next random start.

Avoid Immediate Repeat is optional and defaults to false. When enabled it excludes the previous random selection; with only two configurations the sequence alternates after its first random choice. It is not balanced experimental randomisation.

## Inspector status

The inspector displays state, locked scenario ID/type, session trial number, continuous/processing/slow flags, cumulative and active counts, elapsed time, admission limit, spawn countdown, capacity/entrance blocking, current service vehicle/time and stopped-unprocessed queue count/range.

The stopped count is intentionally not an estimate of every vehicle in a queue: after a release, tightly spaced vehicles move forward together and may temporarily all be moving. Validation also measures an independent dense-queue count using unprocessed vehicles at the processing point or within 0.1 m of minimum predecessor spacing. This avoids reporting a moving queue as having vanished.

## Validation

See DynamicTrafficValidation.txt for actual Unity Play Mode observations. A temporary Editor acceptance probe measures each run after Manager.Update, including per-vehicle service times, route spacing, BoxCollider penetration, active-list/scene consistency, random sequence reproduction and reset isolation. It is removed after acceptance; it is not a product DataLogger.

## Completion report — 2026-10-06

### A–D. Architecture and files

Previously the manager selected one of two finite parameter sets, spawned up to 5/10 vehicles, and entered Completed after all exited. Each active vehicle owned one processed flag and service timer. Movement and spacing already used horizontal route progress.

The manager now starts a locked configuration from either the existing finite settings or the embedded predefined pool. Continuous mode changes generation termination/admission only; the existing processing point, slow-zone and predecessor-distance mechanisms remain the movement foundation. Pause is still a manager state gate. No second simulation controller was added.

New product files: Assets/Scripts/TrafficScenarioConfiguration.cs and its .meta.

Modified product files: Assets/Scripts/TrafficScenarioManager.cs, Assets/Editor/TrafficScenarioManagerEditor.cs, Assets/Scenes/PortOfDover.unity. Scene changes are restricted to the new manager configuration block. Original finite settings, waypoint geometry and references, Cesium and camera settings are byte-for-byte unchanged outside that block.

New documentation/evidence: Docs/DynamicTrafficSystem.md and Docs/DynamicTrafficValidation.txt. The temporary acceptance script and .meta were removed. SHA256 comparisons confirm VehicleRouteFollower.cs, TestTruck.prefab and DesktopCameraController.cs are unchanged.

### E–H. Continuous behaviour and capacity

Continuous generation ignores finite vehicleCount and keeps replacing completed vehicles. Admission checks active capacity and entrance spacing, creates at most one vehicle per frame and resets its interval after an actual spawn. No completed-vehicle list or virtual demand backlog is stored.

ContinuousFreeFlow disables processing and the slow zone. The tested 8 s interval at 10 m/s produces approximately 80 m route spacing and did not require the active cap.

ContinuousCongestion retains WP09 / WP12 / 2.5 m/s / 12 s / 16 m. Its requested interval is 12 s and active cap is 16. The stable regime is explicitly admission-controlled. Observed saturated mean release headway after t=600 s was 18.40139 s, or 195.637 vehicles/hour, agreeing with the approximately 18.4 s service-plus-advance capacity estimate. Nominal demand is 300 vehicles/hour; the admitted stream is throttled to completions. The original 4 s finite baseline remains intact.

### I–L. Predefined scenarios, random choice, reset and pause

Both configurations include stable ID, traffic mode, continuous/finite setting, spawn interval, normal speed, minimum spacing, processing enable/point/delay, slow enable/start/speed and continuous active limit. Values and point distances are locked for each run.

Seed=1234, Avoid Immediate Repeat=false produced the following ten selections, where F=ContinuousFreeFlow and C=ContinuousCongestion:

`F, C, F, C, F, C, C, C, C, F`

After Restart Random Sequence, the same seed reproduced all ten choices exactly. Both predefined entries were selected. Four consecutive C choices demonstrate that ordinary random choice can repeat; this is not parameter randomness. With Avoid Immediate Repeat=true, a separate ten-trial check produced `F,C,F,C,F,C,F,C,F,C`. The default remains false.

Every tested Reset immediately left Active/Spawned/Completed/time/countdown/current-trial identity/queue/service status at zero/empty and no active old follower objects. Starting another scenario in the same frame succeeded. Pause/resume was tested in both continuous modes, including during congestion service: transforms, progress, spawn countdown, service timer, scenario time and counts stayed frozen for at least one real second, then resumed the same run.

### M–N. Continuous Play Mode results

Actual Unity 2022.3.55f1 Editor Play Mode; temporary timeScale=8 to accelerate complete cycles. All times below are Scenario seconds, not wall-clock seconds. Original timeScale=1 was restored on exit. The probe observed each LateUpdate after the real manager/follower updates rather than substituting a simulation model.

| Measurement | ContinuousFreeFlow | ContinuousCongestion |
|---|---:|---:|
| Duration (s) | 244.5529 | 1800.0200 |
| Spawned | 31 | 106 |
| Completed | 20 | 90 |
| Active at end | 11 | 16 |
| Maximum active | 12 | 16 |
| Minimum route centre spacing (m) | 80.00003 | 15.99994 |
| Maximum measured Body/Cab penetration (m) | 0 | 0 |
| Maximum stopped-unprocessed vehicles | 0 | 13 |
| Maximum dense-unprocessed queue count | 0 | 13 |
| Minimum dense queue count after t=600 s | not applicable | 11 |
| Furthest upstream dense-queue progress (m) | not applicable | 636.6055 |
| Furthest upstream stopped-queue progress (m) | not applicable | 636.7049 |
| Processing wait range (s) | disabled | 12.00000–12.03345 |
| Actual spawn interval range (s) | 8.00011–8.02383 | 12.00017–18.44141 |
| Due requests blocked by entrance spacing (s) | 0 | 0 |
| Frames with catch-up spawning | 0 | 0 |

The continuous congestion queue remained roughly around WP09–WP12, occasionally extending about 11 m upstream of WP09; it stayed more than 636 m from the route origin. In the t=600–1800 s window, counts changed from Spawned/Completed/Active=40/24/16 to 106/90/16: 66 admissions and 66 completions with unchanged occupancy. Count-window rate has boundary-phase error; per-release headway gives the capacity estimate above.

The 16-vehicle capacity was occupied for 1562.399 s of the run (this includes times when the next spawn was not yet due). The queue stopped and moved forward cyclically. There were 417.0373 s in the final 1200 s with no fully stopped vehicle, but the dense queue never fell below 11 vehicles. It was not an uninterrupted stationary line, and was not claimed to be one.

Per-frame checks confirmed Spawned minus Completed equals Active, scene active follower count equals the manager list, no destroyed references remain in the active list, no unprocessed vehicle crosses its processing cap, and releases follow vehicle creation order. Every inter-vehicle Body/Cab BoxCollider pair was checked with Physics.ComputePenetration.

### O–P. Random selection and Reset isolation

Passed 10 random trials, 10 same-seed replay trials, and 10 optional avoid-repeat trials. Each reset/start produced one fresh vehicle with zero scenario time and no old active vehicles.

After the 1800 s congestion run had a visible stopped/dense queue, Reset followed immediately by ContinuousFreeFlow completed two vehicles (13 spawned, 11 active) with no processing, slowing or stopped vehicles and minimum spacing 80.00922 m. Reset followed by ContinuousCongestion formed a new three-vehicle stopped queue at approximately t=148.477 s with minimum spacing 15.99994 m. Reset cleared this queue before random testing. These checks cover both directions of isolation.

### Q–R. Original finite baseline regression

| Measurement | Finite FreeFlow | Finite Congestion |
|---|---:|---:|
| Spawned / Completed / Active | 5 / 5 / 0 | 10 / 10 / 0 |
| Final state | Completed | Completed |
| Duration (s) | 124.4136 | 324.3045 |
| Minimum centre spacing (m) | 80.09097 | 15.99994 |
| Maximum penetration (m) | 0 | 0 |
| Processing wait (s) | disabled | 12.00154–12.02003 |

Finite Congestion retains 4 s requested spawn interval, WP09 slow start, WP12 processing point, 12 s delay and 16 m spacing. All ten vehicles processed and completed in order. Finite runs were not limited by continuous admission capacity.

### S. Console and compilation

Runtime/configuration/editor code compiled successfully with Unity's matching assemblies, and the actual Play Mode suite completed with 0 captured Warnings and 0 Errors/Asserts/Exceptions. The editor had shown its existing invalid-GC-handle message during scene/domain reload before the acceptance run; that message was not a traffic-run failure and was not reproduced in the monitored suite. No Console history was cleared to hide it.

An initial acceptance run was stopped after FreeFlow passed so the temporary observer could distinguish stopped vehicles from dense moving queues. No product behaviour was changed to make this distinction. The final retained log comes from the complete corrected suite.

### T. Known limitations

- All parameters and the admission cap are experimental. This is a bounded admitted traffic stream, not unlimited external demand, and not a real Dover capacity estimate.
- Stability was exercised over 1800 simulated seconds with 90 completions, not proven for arbitrary configurations or indefinitely changing terrain.
- Frame-step and float accumulation affect exact timestamps. Continuous congestion scaled deltaTime ranged from 0.013924 to 0.930578 s; the finite congestion regression ranged from 0.012690 to 0.082695 s. Per-car service timers still met the configured threshold. Accelerated acceptance does not replace a normal-speed visual/pilot study.
- Predecessor spacing is route-centre spacing, not a universal body-clearance guarantee on arbitrary new curves. Current-route measured penetration was zero.
- Route geometry and hierarchy must remain fixed during a run. The parameter snapshot does not freeze waypoint transforms.
- Ordinary Reset intentionally preserves random-sequence progress and session numbering. Seed replay requires the explicit restart action and unchanged pool order/settings.
- Only a single processing point and route are supported. There is no participant readiness gate, research logging, or XR integration in this stage.

### U. Definition of Done

| Requirement | Result |
|---|---|
| Continuous spawn, route traversal and destruction | PASS |
| FreeFlow does not form an unexplained queue | PASS |
| Congestion forms a stable natural queue | PASS for the documented admission-controlled configuration |
| Random selection from the predefined pool | PASS |
| Reset starts a fresh trial with no old vehicle/state contamination | PASS |

### V. Recommended next step

Review the two continuous scenarios at normal speed with the existing Desktop Observation Camera, and confirm that the chosen admission-controlled congestion presentation is suitable for the intended participant task. Do not add zones, multiple bottlenecks, random traffic parameters, TrialManager or XR before that review. Development stops at this checkpoint.
