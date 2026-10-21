using System;
using UnityEngine;

/// <summary>Development/experimental settings, not calibrated Dover traffic data.</summary>
[Serializable]
public sealed class TrafficScenarioConfiguration
{
    public string scenarioId;
    public TrafficScenarioType trafficMode;
    public bool continuous = true;
    [Min(1)] public int vehicleCount = 10;
    [Min(1), Tooltip("Continuous admission limit. No waiting demand is stored or caught up.")]
    public int maxActiveVehicles = 16;
    [Min(0.01f)] public float spawnInterval = 8f;
    [Min(0.01f)] public float normalSpeed = 10f;
    [Tooltip("Development option; continuous scenarios only. Finite baselines retain normalSpeed.")]
    public bool individualSpeedVariation;
    [Min(0.01f)] public float minTargetSpeed = 9.5f;
    [Min(0.01f)] public float maxTargetSpeed = 10.5f;
    [Min(12f)] public float minimumSpacing = 16f;
    public bool processingEnabled;
    public Transform processingPoint;
    [Min(0f)] public float processingDelay = 12f;
    public bool slowZoneEnabled;
    public Transform slowZoneStart;
    [Min(0.01f)] public float slowSpeed = 2.5f;
}
