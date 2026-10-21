using System;
using System.Collections.Generic;
using UnityEngine;

public enum TrafficScenarioType { FreeFlow, Congestion }
public enum TrafficScenarioState { Idle, Running, Paused, Completed }
public enum TrafficScenarioSource { FiniteBaseline, Predefined }

/// <summary>Development/test scenarios only; not a calibrated port traffic model.</summary>
public class TrafficScenarioManager : MonoBehaviour
{
    [Serializable]
    private class Parameters
    {
        [Min(1)] public int vehicleCount;
        [Min(0.01f)] public float normalSpeed;
        [Min(0.01f)] public float spawnInterval;
        public Parameters(int count, float speed, float interval)
        { vehicleCount = count; normalSpeed = speed; spawnInterval = interval; }
    }

    [Header("Scenario — changes apply after Reset / Start")]
    [SerializeField] private TrafficScenarioType scenario = TrafficScenarioType.FreeFlow;
    [Header("Vehicle — explicit scene references")]
    [SerializeField] private GameObject vehiclePrefab;
    [SerializeField] private Transform route;
    [SerializeField] private Transform vehiclesParent;
    [SerializeField] private GameObject singleVehicleDebug;
    [Header("Free Flow — Development / Test Parameters")]
    [SerializeField] private Parameters freeFlow = new Parameters(5, 10, 8);
    [Header("Congestion — Development / Test Parameters")]
    [SerializeField] private Parameters congestion = new Parameters(10, 10, 4);
    [SerializeField, Min(0.01f)] private float queueSpeed = 2.5f;
    [SerializeField, Min(0)] private float processingWaitingTime = 12f;
    [Header("Congestion points — changes apply after Reset / Start")]
    [SerializeField, Tooltip("Direct child of Route. Normally upstream of the processing point; downstream placement is allowed for engineering tests.")]
    private Transform queueStartWaypoint;
    [SerializeField, Tooltip("Direct child of Route, before the final waypoint.")]
    private Transform processingWaypoint;
    [Header("Queue — route-center spacing, metres")]
    [SerializeField, Min(12)] private float minimumCenterSpacing = 16f;

    [Header("Selected scenario — Development / Experimental")]
    [SerializeField] private TrafficScenarioSource selectedSource = TrafficScenarioSource.Predefined;
    [SerializeField, Min(0)] private int selectedScenarioIndex;
    [SerializeField] private TrafficScenarioConfiguration[] predefinedScenarios = Array.Empty<TrafficScenarioConfiguration>();
    [Header("Random selection — only the scenario is random")]
    [SerializeField, Tooltip("Start Scenario draws a non-repeating A/B pair from this seed. A linked manager's own seed is used only when started separately.")]
    private int randomSeed = 1234;
    [SerializeField, Tooltip("Single-route debug starts only. Bidirectional Start excludes the previous pair, not each direction separately.")]
    private bool avoidImmediateRepeat;

    [Header("Optional opposite direction — independent spatial configuration")]
    [SerializeField, Tooltip("Separate manager / route with its own continuous configuration pool. Leave empty on Route B.")]
    private TrafficScenarioManager oppositeDirection;
    private TrafficScenarioManager runningOppositeDirection;

    private class Vehicle
    {
        public VehicleRouteFollower follower;
        public float targetSpeed;
        public bool processed;
        public float serviceElapsed;
    }
    private readonly List<Vehicle> vehicles = new List<Vehicle>();
    private Transform ownedContainer;
    private GameObject lockedPrefab;
    private Transform lockedRoute;
    private int lockedCount;
    private float lockedNormalSpeed, lockedSpawnInterval, lockedQueueSpeed, lockedWaitingTime, lockedSpacing;
    private float queueStartProgress, processingProgress, spawnRemaining;
    private int vehicleLayer;
    private bool lockedContinuous, lockedProcessingEnabled, lockedSlowZoneEnabled;
    private int lockedMaxActive;
    private System.Random selectionRandom;
    private int sequenceSeed, lastRandomIndex = -1;
    private int lastPairA = -1, lastPairB = -1;
    public long RandomDrawNumber { get; private set; }
    public int SelectionSeed => sequenceSeed;
    public string OppositeScenarioId => runningOppositeDirection ? runningOppositeDirection.CurrentScenarioId : null;
    private long trialSequence;
    // Independent, counter-based speed stream; never consumes selectionRandom.
    private ulong speedRunSequence, lockedSpeedRun;
    private uint lockedSpeedSeed, lockedRouteSalt;
    private bool lockedSpeedVariation;
    private float lockedMinTargetSpeed, lockedMaxTargetSpeed;

    public TrafficScenarioState State { get; private set; } = TrafficScenarioState.Idle;
    public TrafficScenarioType CurrentScenario { get; private set; }
    public long SpawnedVehicleCount { get; private set; }
    public int ActiveVehicleCount => vehicles.Count;
    public bool HasOppositeDirection => oppositeDirection || runningOppositeDirection;
    public int OppositeActiveVehicleCount => runningOppositeDirection ? runningOppositeDirection.ActiveVehicleCount : 0;
    public string OppositeActiveBottleneck => runningOppositeDirection ? runningOppositeDirection.ActiveBottleneck : "None";
    public long CompletedVehicleCount { get; private set; }
    public float ElapsedTime { get; private set; }
    public string CurrentScenarioId { get; private set; } = "";
    public string ActiveBottleneck { get; private set; } = "None";
    public long CurrentTrialNumber { get; private set; }
    public bool ContinuousMode => State != TrafficScenarioState.Idle && lockedContinuous;
    public bool ProcessingEnabled => State != TrafficScenarioState.Idle && lockedProcessingEnabled;
    public bool SlowZoneEnabled => State != TrafficScenarioState.Idle && lockedSlowZoneEnabled;
    public int ActiveVehicleLimit => lockedContinuous ? lockedMaxActive : lockedCount;
    public float SpawnCountdown => spawnRemaining;
    public bool AdmissionLimited => ContinuousMode && vehicles.Count >= lockedMaxActive;
    public bool EntranceBlocked => State != TrafficScenarioState.Idle && vehicles.Count > 0
        && vehicles[vehicles.Count - 1].follower.RouteProgress < lockedSpacing;
    public int QueuedVehicleCount { get; private set; }
    public float QueueTailProgress { get; private set; }
    public float QueueHeadProgress { get; private set; }
    public string ProcessingVehicleName { get; private set; } = "";
    public float ProcessingElapsed { get; private set; }

    // Each manager owns its route reference and candidate pool.
    [ContextMenu("Start Scenario")]
    public void StartScenario()
    {
        if (!Application.isPlaying || State != TrafficScenarioState.Idle) return;
        TrafficScenarioManager other = oppositeDirection;
        if (other)
        {
            // Validate before either draw. Simulation state remains local to each route.
            if (other == this || other.oppositeDirection || !other.isActiveAndEnabled
                || other.State != TrafficScenarioState.Idle || other.route == route)
            { Debug.LogError("Opposite direction needs an idle, enabled manager on a separate route, and no further linked manager.", this); return; }
            if (!other.ValidateRandomPool()) return;
            foreach (var config in other.predefinedScenarios)
                if (!config.continuous)
                { Debug.LogError("Opposite-direction spatial configurations must be continuous.", this); return; }
        }
        if (!other) { StartRandomTrial(); return; }
        if (!ValidateRandomPool()) return;
        if (selectionRandom == null || sequenceSeed != randomSeed) RestartRandomSequence();
        int countB = other.predefinedScenarios.Length;
        int pair = SelectRandomPair(selectionRandom, predefinedScenarios.Length, countB, lastPairA, lastPairB);
        RandomDrawNumber++;
        int indexA = pair / countB, indexB = pair % countB;
        if (!StartConfiguration(predefinedScenarios[indexA])) return;
        if (!other.StartConfiguration(other.predefinedScenarios[indexB])) { ResetScenario(); return; }
        runningOppositeDirection = other;
        // Retained across traffic Reset; cleared only when restarting the seeded sequence.
        lastPairA = lastRandomIndex = indexA;
        lastPairB = other.lastRandomIndex = indexB;
    }

    private static int SelectRandomPair(System.Random random, int countA, int countB, int previousA, int previousB)
    {
        int total = checked(countA * countB);
        bool exclude = total > 1 && previousA >= 0 && previousA < countA && previousB >= 0 && previousB < countB;
        int selected = random.Next(exclude ? total - 1 : total);
        // Uniformly sample all eligible pairs without a retry loop. A or B alone may repeat.
        if (exclude && selected >= previousA * countB + previousB) selected++;
        return selected;
    }

    // Original finite execution path retained as an explicit developer control.
    [ContextMenu("Start Finite Baseline")]
    public void StartFiniteBaseline()
    {
        if (!Application.isPlaying) return;
        if (State == TrafficScenarioState.Paused) { State = TrafficScenarioState.Running; return; }
        if (State != TrafficScenarioState.Idle) return;
        Parameters selected = scenario == TrafficScenarioType.FreeFlow ? freeFlow : congestion;
        if (selected == null) { Debug.LogError("Missing finite baseline parameters.", this); return; }
        StartConfiguration(new TrafficScenarioConfiguration
        {
            scenarioId = scenario == TrafficScenarioType.FreeFlow ? "FreeFlowBaseline" : "CongestionBaseline",
            trafficMode = scenario, continuous = false, vehicleCount = selected.vehicleCount,
            normalSpeed = selected.normalSpeed, spawnInterval = selected.spawnInterval,
            slowSpeed = queueSpeed, processingDelay = processingWaitingTime, minimumSpacing = minimumCenterSpacing,
            processingEnabled = scenario == TrafficScenarioType.Congestion,
            slowZoneEnabled = scenario == TrafficScenarioType.Congestion,
            slowZoneStart = queueStartWaypoint, processingPoint = processingWaypoint
        });
    }

    [ContextMenu("Start Selected Scenario")]
    public void StartSelectedScenario()
    {
        if (!Application.isPlaying || State != TrafficScenarioState.Idle) return;
        if (selectedSource == TrafficScenarioSource.FiniteBaseline) { StartFiniteBaseline(); return; }
        if (predefinedScenarios == null || selectedScenarioIndex < 0 || selectedScenarioIndex >= predefinedScenarios.Length)
        { Debug.LogError("Select a valid predefined scenario index.", this); return; }
        StartConfiguration(predefinedScenarios[selectedScenarioIndex]);
    }

    [ContextMenu("Start Random Trial")]
    public void StartRandomTrial()
    {
        if (!Application.isPlaying || State != TrafficScenarioState.Idle) return;
        if (!ValidateRandomPool()) return;
        if (selectionRandom == null || sequenceSeed != randomSeed) RestartRandomSequence();
        StartRandomConfiguration(selectionRandom);
    }

    private bool ValidateRandomPool()
    {
        if (predefinedScenarios == null || predefinedScenarios.Length == 0)
        { Debug.LogError("The predefined scenario pool is empty.", this); return false; }
        var ids = new HashSet<string>();
        foreach (var config in predefinedScenarios)
        {
            if (!ValidateConfiguration(config)) return false;
            if (!ids.Add(config.scenarioId)) { Debug.LogError("Scenario IDs must be unique in the random pool.", this); return false; }
        }
        return true;
    }

    private bool StartRandomConfiguration(System.Random random)
    {
        int count = predefinedScenarios.Length;
        bool excludePrevious = avoidImmediateRepeat && count > 1 && lastRandomIndex >= 0 && lastRandomIndex < count;
        int index = random.Next(excludePrevious ? count - 1 : count);
        RandomDrawNumber++;
        if (excludePrevious && index >= lastRandomIndex) index++;
        if (!StartConfiguration(predefinedScenarios[index])) return false;
        lastRandomIndex = index;
        return true;
    }

    // Called once at formal session start, never by tutorial or Next Trial.
    public bool InitializeParticipantRandomSequence()
    {
        if (!Application.isPlaying || State != TrafficScenarioState.Idle) return false;
        if (oppositeDirection && (oppositeDirection == this || oppositeDirection.oppositeDirection ||
            !oppositeDirection.isActiveAndEnabled || oppositeDirection.State != TrafficScenarioState.Idle ||
            oppositeDirection.route == route)) return false;
        var bytes = new byte[4];
        using (var entropy = System.Security.Cryptography.RandomNumberGenerator.Create())
            entropy.GetBytes(bytes);
        randomSeed = BitConverter.ToInt32(bytes, 0) & int.MaxValue;
        // One coordinator draws the pair; both independent speed streams use this session seed.
        if (oppositeDirection) oppositeDirection.randomSeed = randomSeed;
        RestartRandomSequence();
        return true;
    }

    [ContextMenu("Restart Random Sequence (Idle only)")]
    public void RestartRandomSequence()
    {
        if (!Application.isPlaying || State != TrafficScenarioState.Idle) return;
        sequenceSeed = randomSeed;
        selectionRandom = new System.Random(sequenceSeed);
        RandomDrawNumber = 0;
        lastRandomIndex = -1;
        lastPairA = lastPairB = -1;
        speedRunSequence = 0;
        if (oppositeDirection && oppositeDirection != this && oppositeDirection.State == TrafficScenarioState.Idle)
        {
            oppositeDirection.lastRandomIndex = -1;
            oppositeDirection.speedRunSequence = 0;
        }
    }

    private bool StartConfiguration(TrafficScenarioConfiguration config)
    {
        if (!ValidateConfiguration(config)) return false;
        CurrentScenario = config.trafficMode;
        CurrentScenarioId = config.scenarioId;
        ActiveBottleneck = config.processingEnabled ? config.processingPoint.name : "None";
        if (trialSequence < long.MaxValue) trialSequence++;
        CurrentTrialNumber = trialSequence;
        lockedContinuous = config.continuous;
        lockedMaxActive = config.maxActiveVehicles;
        lockedProcessingEnabled = config.processingEnabled;
        lockedSlowZoneEnabled = config.slowZoneEnabled;
        lockedCount = config.vehicleCount;
        lockedNormalSpeed = config.normalSpeed;
        lockedSpawnInterval = config.spawnInterval;
        lockedQueueSpeed = config.slowSpeed;
        lockedWaitingTime = config.processingDelay;
        lockedSpacing = config.minimumSpacing;
        lockedPrefab = vehiclePrefab;
        lockedRoute = route;
        lockedSpeedVariation = config.continuous && config.individualSpeedVariation;
        lockedMinTargetSpeed = config.minTargetSpeed;
        lockedMaxTargetSpeed = config.maxTargetSpeed;
        lockedSpeedSeed = unchecked((uint)randomSeed);
        lockedRouteSalt = StableRouteSalt(route.name);
        // Count continuous starts even when variation is off; finite tests do not advance it.
        lockedSpeedRun = config.continuous ? speedRunSequence++ : 0;
        queueStartProgress = processingProgress = 0f;
        float distance = 0;
        for (int i = 0; i < lockedRoute.childCount; i++)
        {
            if (i > 0)
                distance += Vector3.ProjectOnPlane(lockedRoute.GetChild(i).position - lockedRoute.GetChild(i - 1).position, Vector3.up).magnitude;
            // Snapshot distances once; subsequent Inspector reference edits do not affect this run.
            if (lockedRoute.GetChild(i) == config.slowZoneStart) queueStartProgress = distance;
            if (lockedRoute.GetChild(i) == config.processingPoint) processingProgress = distance;
        }
        if (singleVehicleDebug) singleVehicleDebug.SetActive(false);
        ownedContainer = new GameObject("ScenarioVehicles").transform;
        ownedContainer.SetParent(vehiclesParent, false);
        State = TrafficScenarioState.Running;
        SpawnVehicle();
        spawnRemaining = lockedSpawnInterval;
        return true;
    }

    [ContextMenu("Stop Scenario")]
    public void StopScenario()
    {
        if (State == TrafficScenarioState.Running) State = TrafficScenarioState.Paused;
        if (runningOppositeDirection) runningOppositeDirection.StopScenario();
    }

    [ContextMenu("Resume Scenario")]
    public void ResumeScenario()
    {
        if (Application.isPlaying && State == TrafficScenarioState.Paused) State = TrafficScenarioState.Running;
        if (Application.isPlaying && runningOppositeDirection) runningOppositeDirection.ResumeScenario();
    }

    [ContextMenu("Reset Scenario")]
    public void ResetScenario()
    {
        if (!Application.isPlaying) return;
        if (runningOppositeDirection) runningOppositeDirection.ResetScenario();
        runningOppositeDirection = null;
        // Deactivate immediately; Unity destruction completes at end of frame.
        if (ownedContainer) { ownedContainer.gameObject.SetActive(false); Destroy(ownedContainer.gameObject); }
        ownedContainer = null;
        vehicles.Clear();
        SpawnedVehicleCount = CompletedVehicleCount = 0;
        ElapsedTime = spawnRemaining = 0;
        CurrentScenario = default;
        CurrentScenarioId = "";
        ActiveBottleneck = "None";
        CurrentTrialNumber = 0;
        lockedPrefab = null;
        lockedRoute = null;
        lockedContinuous = lockedProcessingEnabled = lockedSlowZoneEnabled = false;
        lockedCount = lockedMaxActive = 0;
        lockedNormalSpeed = lockedSpawnInterval = lockedQueueSpeed = lockedWaitingTime = lockedSpacing = 0;
        queueStartProgress = processingProgress = 0;
        lockedSpeedVariation = false;
        lockedMinTargetSpeed = lockedMaxTargetSpeed = 0;
        lockedSpeedRun = 0;
        lockedSpeedSeed = lockedRouteSalt = 0;
        ClearProcessingStatus();
        // Selection RNG/session numbering intentionally survive a traffic Reset.
        State = TrafficScenarioState.Idle;
    }

    private bool ValidateConfiguration(TrafficScenarioConfiguration p)
    {
        vehicleLayer = LayerMask.NameToLayer("Vehicle");
        bool valid = vehiclePrefab && route && vehiclesParent && singleVehicleDebug && vehicleLayer >= 8
            && route.childCount >= 15 && p != null && !string.IsNullOrWhiteSpace(p.scenarioId)
            && (p.continuous ? p.maxActiveVehicles > 0 : p.vehicleCount > 0)
            && Positive(p.normalSpeed) && Positive(p.spawnInterval) && Positive(p.slowSpeed)
            && (!p.continuous || !p.individualSpeedVariation ||
                (Positive(p.minTargetSpeed) && Positive(p.maxTargetSpeed) && p.minTargetSpeed <= p.maxTargetSpeed))
            && Finite(p.processingDelay) && p.processingDelay >= 0
            && Finite(p.minimumSpacing) && p.minimumSpacing >= 12f
            && (p.trafficMode == TrafficScenarioType.FreeFlow ? !p.processingEnabled && !p.slowZoneEnabled
                : p.trafficMode == TrafficScenarioType.Congestion && p.processingEnabled);
        if (valid)
            for (int i = 1; i < route.childCount; i++)
                if (Vector3.ProjectOnPlane(route.GetChild(i).position - route.GetChild(i - 1).position, Vector3.up).sqrMagnitude < 0.0001f)
                    valid = false;
        if (!valid) Debug.LogError("Scenario not started: verify scene references, Vehicle layer, scenario ID/mode, positive finite test parameters, continuous capacity or finite count; route needs 15 ordered, distinct horizontal waypoints and spacing >= 12 m. FreeFlow must disable processing and slow zone; Congestion requires processing.", this);
        if (valid && p.processingEnabled)
        {
            valid = p.processingPoint && p.processingPoint.parent == route
                && p.processingPoint.GetSiblingIndex() < route.childCount - 1;
            if (!valid)
                Debug.LogError("Scenario not started: Processing Point must be a direct child of Route before the final waypoint.", this);
        }
        if (valid && p.slowZoneEnabled)
        {
            valid = p.slowZoneStart && p.slowZoneStart.parent == route;
            if (!valid) Debug.LogError("Scenario not started: Slow Zone Start must be a direct child of Route.", this);
            else if (p.processingEnabled && p.slowZoneStart.GetSiblingIndex() >= p.processingPoint.GetSiblingIndex())
                Debug.LogWarning("Queue slow-start waypoint is not upstream of the processing point. The processing point test may run, but the configured slow zone will not represent the standard congestion baseline.", this);
        }
        return valid;
    }
    private static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);
    private static bool Positive(float n) => Finite(n) && n > 0;

    private void SpawnVehicle()
    {
        GameObject instance = Instantiate(lockedPrefab, ownedContainer);
        instance.name = "TestTruck_" + SpawnedVehicleCount.ToString("D2");
        foreach (Transform child in instance.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = vehicleLayer;
        var follower = instance.GetComponent<VehicleRouteFollower>();
        if (!follower) follower = instance.AddComponent<VehicleRouteFollower>();
        float targetSpeed = lockedSpeedVariation ? SampleTargetSpeed(lockedSpeedSeed, lockedRouteSalt,
            lockedSpeedRun, (ulong)SpawnedVehicleCount, lockedMinTargetSpeed, lockedMaxTargetSpeed) : lockedNormalSpeed;
        follower.Initialize(lockedRoute, targetSpeed, ~(1 << vehicleLayer));
        follower.StartMovement();
        vehicles.Add(new Vehicle { follower = follower, targetSpeed = targetSpeed });
        if (SpawnedVehicleCount < long.MaxValue) SpawnedVehicleCount++;
    }

    private void Update()
    {
        if (State != TrafficScenarioState.Running) return;
        float dt = Time.deltaTime;
        ElapsedTime += dt;
        float leaderProgress = float.PositiveInfinity;
        bool serviceHeadAssigned = false;
        ClearProcessingStatus();
        // Creation order is route order: predecessor moves first; followers use its updated progress.
        for (int i = 0; i < vehicles.Count;)
        {
            Vehicle car = vehicles[i];
            var follower = car.follower;
            float progress = follower.RouteProgress;
            float allowed = Mathf.Max(0, leaderProgress - lockedSpacing - progress);
            float speed = car.targetSpeed;
            if (lockedProcessingEnabled || lockedSlowZoneEnabled)
            {
                if (lockedSlowZoneEnabled)
                {
                    if (progress < queueStartProgress - 0.001f)
                        allowed = Mathf.Min(allowed, queueStartProgress - progress);
                    else if (!car.processed) speed = lockedQueueSpeed;
                }
                if (lockedProcessingEnabled && !car.processed)
                {
                    bool head = !serviceHeadAssigned;
                    serviceHeadAssigned = true;
                    if (head && progress >= processingProgress - 0.001f)
                    {
                        // Count only time already spent at the processing point, not approach time.
                        car.serviceElapsed += dt;
                        ProcessingVehicleName = follower.name;
                        ProcessingElapsed = car.serviceElapsed;
                        if (car.serviceElapsed >= lockedWaitingTime && allowed > 0.001f) car.processed = true;
                        else allowed = 0;
                    }
                    if (!car.processed) allowed = Mathf.Min(allowed, Mathf.Max(0, processingProgress - progress));
                    else speed = car.targetSpeed;
                }
            }
            follower.Speed = speed;
            follower.TickMovement(dt, allowed);
            if (follower.IsFinished)
            {
                follower.gameObject.SetActive(false);
                Destroy(follower.gameObject);
                vehicles.RemoveAt(i);
                if (CompletedVehicleCount < long.MaxValue) CompletedVehicleCount++;
                continue;
            }
            if (lockedProcessingEnabled && !car.processed && follower.RouteProgress - progress < 0.0001f)
            {
                if (QueuedVehicleCount == 0) QueueHeadProgress = follower.RouteProgress;
                QueueTailProgress = follower.RouteProgress;
                QueuedVehicleCount++;
            }
            leaderProgress = follower.RouteProgress;
            i++;
        }
        if (lockedContinuous || SpawnedVehicleCount < lockedCount)
        {
            spawnRemaining = Mathf.Max(0, spawnRemaining - dt);
            bool entranceClear = vehicles.Count == 0 || vehicles[vehicles.Count - 1].follower.RouteProgress >= lockedSpacing;
            bool capacityAvailable = !lockedContinuous || vehicles.Count < lockedMaxActive;
            if (spawnRemaining <= 0 && entranceClear && capacityAvailable) { SpawnVehicle(); spawnRemaining = lockedSpawnInterval; }
        }
        if (!lockedContinuous && SpawnedVehicleCount == lockedCount && vehicles.Count == 0) State = TrafficScenarioState.Completed;
    }

    // Explicit integer arithmetic is stable across sessions; no runtime string hash or object ID.
    private static uint StableRouteSalt(string name)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in name) hash = (hash ^ c) * 16777619;
            return hash;
        }
    }

    private static ulong MixSpeed(ulong value)
    {
        unchecked
        {
            value += 0x9E3779B97F4A7C15UL;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }

    private static float SampleTargetSpeed(uint seed, uint routeSalt, ulong run, ulong vehicle, float min, float max)
    {
        ulong bits = MixSpeed(((ulong)seed << 32) | routeSalt) ^ MixSpeed(run) ^ MixSpeed(vehicle ^ 0xD1B54A32D192ED03UL);
        double fraction = (MixSpeed(bits) >> 11) * (1.0 / 9007199254740992.0);
        return Mathf.Clamp((float)(min + (max - (double)min) * fraction), min, max);
    }

    private void ClearProcessingStatus()
    {
        QueuedVehicleCount = 0;
        QueueTailProgress = QueueHeadProgress = ProcessingElapsed = 0;
        ProcessingVehicleName = "";
    }

    private void OnDisable() => StopScenario();
    private void OnDestroy()
    {
        if (runningOppositeDirection) runningOppositeDirection.ResetScenario();
        if (ownedContainer) { ownedContainer.gameObject.SetActive(false); Destroy(ownedContainer.gameObject); }
    }
}
