using UnityEngine;

// Shared lightweight visuals; no colliders, materials instantiated per vehicle, or input readers.
internal static class InteractionFeedbackVisuals
{
    internal static LineRenderer Ring(string name, Material material, Color color, float radius, float width)
    {
        var line = new GameObject(name).AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.sharedMaterial = material;
        line.positionCount = 33;
        line.widthMultiplier = width;
        line.startColor = line.endColor = color;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        for (int i = 0; i <= 32; i++)
        {
            float angle = i * Mathf.PI * 2 / 32;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, .35f, Mathf.Sin(angle) * radius));
        }
        return line;
    }
    internal static void Remove(GameObject go)
    {
        if (go == null) return;
        go.SetActive(false);
        if (Application.isPlaying) Object.Destroy(go); else Object.DestroyImmediate(go);
    }
}
