using UnityEditor;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>Hide XRI's editor-only hit-normal debug strokes; runtime ray rendering is unchanged.</summary>
internal static class ObservationRayGizmos
{
    [InitializeOnLoadMethod]
    private static void Configure()
    {
        EditorApplication.delayCall += () =>
            GizmoUtility.SetGizmoEnabled(typeof(XRRayInteractor), false, false);
    }
}
