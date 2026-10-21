using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Editor-only display of a development simulation route, in direct-child order.
public class RouteVisualizer : MonoBehaviour
{
    private void OnDrawGizmos()
    {
#if UNITY_EDITOR
        if (Camera.current == null || Camera.current.cameraType != CameraType.SceneView) return;
        Gizmos.color = Color.cyan;
        var oldDepth = Handles.zTest;
        var oldColor = Handles.color;
        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        Handles.color = Color.cyan;
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform point = transform.GetChild(i);
            Gizmos.DrawSphere(point.position, 2f);
            if (i > 0)
            {
                var previous = transform.GetChild(i - 1).position;
                Gizmos.DrawLine(previous, point.position);
                Handles.DrawAAPolyLine(3f, previous, point.position);
            }
            Handles.Label(point.position + Vector3.up * 3f, point.name);
        }
        Handles.zTest = oldDepth;
        Handles.color = oldColor;
#endif
    }
}
