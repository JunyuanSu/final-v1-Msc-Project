using System;
using System.Collections.Generic;
using UnityEngine;

public enum IdentificationStage { Idle, ObserveIdentify, ConfirmInitial, SelectInspect, ConfirmFinal, Submitted }

[Serializable]
public sealed class IdentificationMarker
{
    [SerializeField] private string routeId, routeName;
    [SerializeField] private int segment;
    [SerializeField] private float routeProgress;
    [SerializeField] private Vector3 worldPosition;
    public string RouteId => routeId;
    public string RouteName => routeName;
    public int Segment => segment;
    public float RouteProgress => routeProgress;
    public Vector3 WorldPosition => worldPosition;
    public IdentificationMarker(string id, string name, int segmentIndex, float progress, Vector3 position)
    { routeId = id; routeName = name; segment = segmentIndex; routeProgress = progress; worldPosition = position; }
}

[Serializable]
public sealed class IdentificationSnapshot
{
    [SerializeField] private IdentificationMarker[] markers;
    [SerializeField] private double relativeSeconds;
    [SerializeField] private bool explicitlyNoCongestion;
    public IReadOnlyList<IdentificationMarker> Markers => Array.AsReadOnly(markers);
    public int Count => markers.Length;
    public double RelativeSeconds => relativeSeconds;
    public bool ExplicitlyNoCongestion => explicitlyNoCongestion;
    internal IdentificationSnapshot(IReadOnlyList<IdentificationMarker> source, double seconds, bool noCongestion)
    {
        markers = new IdentificationMarker[source.Count];
        for (int i = 0; i < source.Count; i++) markers[i] = source[i];
        relativeSeconds = seconds; explicitlyNoCongestion = noCongestion;
    }
}

[Serializable]
public sealed class IdentificationTrialResult
{
    [SerializeField] private long trialNumber;
    [SerializeField] private IdentificationSnapshot initial, final;
    [SerializeField] private bool editedAfterInitial, finalDiffersFromInitial;
    public long TrialNumber => trialNumber;
    public IdentificationSnapshot Initial => initial;
    public IdentificationSnapshot Final => final;
    public bool EditedAfterInitial => editedAfterInitial;
    public bool FinalDiffersFromInitial => finalDiffersFromInitial;
    internal IdentificationTrialResult(long number, IdentificationSnapshot first, IdentificationSnapshot last, bool edited)
    {
        trialNumber = number; initial = first; final = last; editedAfterInitial = edited;
        finalDiffersFromInitial = !SameJudgment(first, last);
    }
    private static bool SameJudgment(IdentificationSnapshot a, IdentificationSnapshot b)
    {
        if (a.Count != b.Count) return false;
        var used = new bool[b.Count];
        foreach (var x in a.Markers)
        {
            bool found = false;
            for (int i = 0; i < b.Count; i++)
            {
                var y = b.Markers[i];
                if (!used[i] && x.RouteId == y.RouteId && x.RouteName == y.RouteName &&
                    Mathf.Abs(x.RouteProgress - y.RouteProgress) <= .01f &&
                    (x.WorldPosition - y.WorldPosition).sqrMagnitude <= .0001f)
                { used[i] = true; found = true; break; }
            }
            if (!found) return false;
        }
        return true;
    }
}

/// <summary>Participant-only state. No scenario answers, vehicle speeds or random state.</summary>
public sealed class IdentificationTrialState
{
    public const int MarkerLimit = 6;
    public IdentificationStage Stage { get; private set; }
    public long TrialNumber { get; private set; }
    public IdentificationSnapshot Initial { get; private set; }
    public IdentificationTrialResult Result { get; private set; }
    public IdentificationTrialResult PendingResult { get; private set; }
    public bool EditedAfterInitial { get; private set; }
    public bool CanEdit => Stage == IdentificationStage.ObserveIdentify || Stage == IdentificationStage.SelectInspect;
    public bool CanInspect => Stage == IdentificationStage.SelectInspect;
    public void Clear()
    { Stage = IdentificationStage.Idle; TrialNumber = 0; Initial = null; Result = null; PendingResult = null; EditedAfterInitial = false; }
    public void Begin(long number)
    { Clear(); if (number > 0) { TrialNumber = number; Stage = IdentificationStage.ObserveIdentify; } }
    public void MarkEdited() { if (Stage == IdentificationStage.SelectInspect) EditedAfterInitial = true; }
    public bool Review()
    {
        if (Stage == IdentificationStage.ObserveIdentify) Stage = IdentificationStage.ConfirmInitial;
        else if (Stage == IdentificationStage.SelectInspect) Stage = IdentificationStage.ConfirmFinal;
        else return false;
        return true;
    }
    public bool Back()
    {
        if (PendingResult != null) return false; // A failed write retains the exact confirmed response for retry.
        if (Stage == IdentificationStage.ConfirmInitial) Stage = IdentificationStage.ObserveIdentify;
        else if (Stage == IdentificationStage.ConfirmFinal) Stage = IdentificationStage.SelectInspect;
        else return false;
        return true;
    }
    public bool Confirm(IReadOnlyList<IdentificationMarker> markers, bool explicitlyNone, double seconds,
        Func<IdentificationTrialResult, bool> persistFinal = null)
    {
        if (Stage != IdentificationStage.ConfirmInitial && Stage != IdentificationStage.ConfirmFinal) return false;
        if (Stage == IdentificationStage.ConfirmFinal && PendingResult != null)
        {
            if (persistFinal != null && !persistFinal(PendingResult)) return false;
            Result = PendingResult; PendingResult = null; Stage = IdentificationStage.Submitted;
            return true;
        }
        if (markers == null || markers.Count > MarkerLimit || double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) return false;
        if ((markers.Count == 0) != explicitlyNone) return false;
        foreach (var marker in markers) if (marker == null) return false;
        if (Stage == IdentificationStage.ConfirmInitial)
        {
            Initial = new IdentificationSnapshot(markers, seconds, explicitlyNone);
            Stage = IdentificationStage.SelectInspect;
        }
        else
        {
            if (seconds < Initial.RelativeSeconds) return false;
            if (PendingResult == null) PendingResult = new IdentificationTrialResult(TrialNumber, Initial,
                new IdentificationSnapshot(markers, seconds, explicitlyNone), EditedAfterInitial);
            if (persistFinal != null && !persistFinal(PendingResult)) return false;
            Result = PendingResult; PendingResult = null;
            Stage = IdentificationStage.Submitted;
        }
        return true;
    }
}
