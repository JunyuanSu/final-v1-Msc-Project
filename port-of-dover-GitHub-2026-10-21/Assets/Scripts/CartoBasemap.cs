using System;
using CesiumForUnity;
using UnityEngine;

/// <summary>Local CARTO imagery setup. Credentials never belong in the scene.</summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Cesium3DTileset))]
public sealed class CartoBasemap : MonoBehaviour
{
    [NonSerialized] private CesiumUrlTemplateRasterOverlay overlay;

    private void OnEnable()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += Configure;
#else
        Configure();
#endif
    }

    private void Configure()
    {
        if (this == null || !isActiveAndEnabled || overlay != null) return;

        // A domain reload may discard an editor cleanup callback. Remove our stale transient overlay.
        foreach (var existing in GetComponents<CesiumUrlTemplateRasterOverlay>())
            if (existing.hideFlags == HideFlags.HideAndDontSave)
                DestroyImmediate(existing);

        string key = Environment.GetEnvironmentVariable("CARTO_API_KEY");
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (string.IsNullOrWhiteSpace(key))
            key = Environment.GetEnvironmentVariable("CARTO_API_KEY", EnvironmentVariableTarget.User);
#endif
        if (string.IsNullOrWhiteSpace(key))
        {
            Debug.LogError("CARTO imagery requires the local CARTO_API_KEY environment variable.", this);
            return;
        }

        // This component is deliberately transient: saving the scene must not serialize its URL.
        overlay = gameObject.AddComponent<CesiumUrlTemplateRasterOverlay>();
        overlay.enabled = false;
        overlay.hideFlags = HideFlags.HideAndDontSave;
        overlay.projection = CesiumUrlTemplateRasterOverlayProjection.WebMercator;
        overlay.minimumLevel = 0;
        overlay.maximumLevel = 20;
        overlay.tileWidth = 256;
        overlay.tileHeight = 256;
        // Cesium's reverseY is XYZ's north-origin Y.
        overlay.templateUrl = "https://a.basemaps.cartocdn.com/light_nolabels/{z}/{x}/{reverseY}.png?key="
            + Uri.EscapeDataString(key.Trim());
        overlay.enabled = true;
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall -= Configure;
#endif
        if (overlay == null) return;
        overlay.enabled = false;
        overlay.templateUrl = string.Empty;
        var oldOverlay = overlay;
        if (Application.isPlaying) Destroy(oldOverlay);
#if UNITY_EDITOR
        else UnityEditor.EditorApplication.delayCall += () =>
        {
            if (oldOverlay != null) DestroyImmediate(oldOverlay);
        };
#else
        else Destroy(oldOverlay);
#endif
        overlay = null;
    }

    private void OnGUI()
    {
        if (overlay == null || !overlay.enabled) return;
        // Mandatory map attribution; no interaction controls are added.
        GUI.Box(new Rect(Mathf.Max(0, Screen.width - 290), 4, 286, 24),
            "© OpenStreetMap contributors  © CARTO");
    }
}
