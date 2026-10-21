using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

[Serializable]
public sealed class StreetObservationVisit
{
    public double enteredAtSeconds, exitedAtSeconds, durationSeconds;
}

/// <summary>Export-only data. Never displayed as participant feedback.</summary>
[Serializable]
public sealed class EvaluationTrialDocument
{
    public int schemaVersion = 2;
    public int selectionSeed;
    public long randomDrawNumber;
    public string participantId, trialId, trialStartedUtc, finalConfirmedUtc;
    public string unityScene, routeAScenarioId, routeBScenarioId;
    public string routeAActiveBottleneck, routeBActiveBottleneck;
    public bool trafficTruthAvailable, routeBPresent, streetMetricsAvailable;
    public string truthNotes = "Configured active bottlenecks at trial start; not measured queue extent. Seed and draw number identify the selection sequence with the same ordered configuration pools.";
    public string timeBasis = "Unscaled seconds from task start, including pauses; street visits exclude fade and preparation.";
    public int streetVisitCount;
    public double streetDurationSeconds;
    public List<StreetObservationVisit> streetVisits = new List<StreetObservationVisit>();
    public IdentificationTrialResult identification;
}

public static class EvaluationDataLog
{
    public static string DirectoryPath => @"C:\Users\SEER04\Desktop\SJY project";

    // A same-directory atomic rename exposes only complete JSON. Never overwrite an existing trial.
    public static bool TrySave(EvaluationTrialDocument document, out string path, out string error, string directory = null)
    {
        path = ""; error = "";
        string temporary = null;
        try
        {
            if (document == null || document.identification == null ||
                !Guid.TryParseExact(document.trialId, "N", out _)) throw new ArgumentException("Invalid trial document.");
            string json = JsonUtility.ToJson(document, true);
            directory = directory ?? DirectoryPath;
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "trial_" + document.trialId + ".json");
            if (File.Exists(path))
            {
                if (File.ReadAllText(path, Encoding.UTF8) == json) return true;
                throw new IOException("A different record already exists for this trial ID.");
            }
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            byte[] bytes = new UTF8Encoding(false).GetBytes(json);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            File.Move(temporary, path);
            return true;
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
            exception is ArgumentException || exception is NotSupportedException || exception is System.Security.SecurityException)
        { error = exception.Message; return false; }
        finally
        {
            if (temporary != null)
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
