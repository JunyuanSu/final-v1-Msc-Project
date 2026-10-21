using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TrafficScenarioManager))]
public class TrafficScenarioManagerEditor : Editor
{
    private bool developerControls;
    public override void OnInspectorGUI()
    {
        var manager = (TrafficScenarioManager)target;
        EditorGUILayout.HelpBox("Start Scenario draws and locks separate A/B spatial configurations using this manager's seed. Pause / Resume / Reset control both directions. Debug baseline/selected starts run this route only. Admission is capped; demand is not backlogged.", MessageType.Info);
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            using (new EditorGUI.DisabledScope(manager.State != TrafficScenarioState.Idle))
            {
                if (GUILayout.Button("Start Scenario")) manager.StartScenario();
            }
            using (new EditorGUI.DisabledScope(manager.State != TrafficScenarioState.Running))
                if (GUILayout.Button("Pause")) manager.StopScenario();
            using (new EditorGUI.DisabledScope(manager.State != TrafficScenarioState.Paused))
                if (GUILayout.Button("Resume")) manager.ResumeScenario();
            if (GUILayout.Button("Reset")) manager.ResetScenario();
        }
        developerControls = EditorGUILayout.Foldout(developerControls, "Developer Settings / Baselines", true);
        if (developerControls)
        {
            DrawDefaultInspector();
            using (new EditorGUI.DisabledScope(!Application.isPlaying || manager.State != TrafficScenarioState.Idle))
            {
                if (GUILayout.Button("Start Selected Scenario (debug)")) manager.StartSelectedScenario();
                if (GUILayout.Button("Start Finite Baseline")) manager.StartFiniteBaseline();
                if (GUILayout.Button("Restart Random Sequence (current seed)")) manager.RestartRandomSequence();
            }
        }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Runtime State (read only)", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.EnumPopup("State", manager.State);
            EditorGUILayout.TextField("Locked Scenario", manager.State == TrafficScenarioState.Idle ? "—" : manager.CurrentScenario.ToString());
            EditorGUILayout.TextField("Scenario ID", manager.CurrentScenarioId);
            EditorGUILayout.TextField(manager.HasOppositeDirection ? "Route A Bottleneck" : "Active Bottleneck", manager.ActiveBottleneck);
            if (manager.HasOppositeDirection)
                EditorGUILayout.TextField("Route B Bottleneck", manager.OppositeActiveBottleneck);
            EditorGUILayout.LongField("Trial (session)", manager.CurrentTrialNumber);
            EditorGUILayout.Toggle("Continuous", manager.ContinuousMode);
            EditorGUILayout.Toggle("Processing Enabled", manager.ProcessingEnabled);
            EditorGUILayout.Toggle("Slow Zone Enabled", manager.SlowZoneEnabled);
            EditorGUILayout.LongField("Spawned", manager.SpawnedVehicleCount);
            EditorGUILayout.IntField(manager.HasOppositeDirection ? "Route A Active Vehicles" : "Active Vehicles (this route)", manager.ActiveVehicleCount);
            if (manager.HasOppositeDirection)
                EditorGUILayout.IntField("Route B Active Vehicles", manager.OppositeActiveVehicleCount);
            EditorGUILayout.LongField("Completed", manager.CompletedVehicleCount);
            EditorGUILayout.FloatField("Elapsed Seconds", manager.ElapsedTime);
            EditorGUILayout.IntField("Active Limit / Finite Count", manager.ActiveVehicleLimit);
            EditorGUILayout.FloatField("Spawn Countdown", manager.SpawnCountdown);
            EditorGUILayout.Toggle("Admission At Capacity", manager.AdmissionLimited);
            EditorGUILayout.Toggle("Entrance Spacing Blocked", manager.EntranceBlocked);
            EditorGUILayout.IntField("Stopped Unprocessed", manager.QueuedVehicleCount);
            EditorGUILayout.FloatField("Queue Tail Progress", manager.QueueTailProgress);
            EditorGUILayout.FloatField("Queue Head Progress", manager.QueueHeadProgress);
            EditorGUILayout.TextField("Processing Vehicle", manager.ProcessingVehicleName);
            EditorGUILayout.FloatField("Service Elapsed", manager.ProcessingElapsed);
        }
        if (Application.isPlaying) Repaint();
    }
}
