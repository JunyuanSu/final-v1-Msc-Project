using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Identity-only road ribbons. No colliders, input, or traffic state.</summary>
[DefaultExecutionOrder(110)]
public sealed class OverviewRouteVisuals : MonoBehaviour
{
    private XRObservationModes modes;
    private Transform terrain;
    private Transform[] routes;
    private readonly List<LineRenderer>[] lines = { new List<LineRenderer>(), new List<LineRenderer>() };
    public int VisibleSections { get; private set; }
    public int MissingGroundSamples { get; private set; }
    private readonly List<Vector3> points = new List<Vector3>(1024);
    private readonly RaycastHit[] hits = new RaycastHit[32];
    private Material material;
    private float nextRefresh;
    private bool wasVisible;

    public void Initialize(XRObservationModes owner, Transform a, Transform b, Transform ground)
    {
        modes=owner; terrain=ground; routes=new[]{a,b};
        var shader=Resources.Load<Shader>("OverviewRouteRibbon");
        if(shader==null) { Debug.LogError("Overview route shader missing.",this); enabled=false; return; }
        material=new Material(shader){name="Overview route identity"};
        for(int i=0;i<2;i++) CreateLine(i);
    }
    private LineRenderer CreateLine(int i)
    {
            var go=new GameObject(i==0?"Route A overview ribbon":"Route B overview ribbon");
            go.transform.SetParent(transform,false);
            var line=go.AddComponent<LineRenderer>(); lines[i].Add(line);
            line.useWorldSpace=true; line.loop=false; line.sharedMaterial=material;
            // A horizontal ribbon, rather than a camera-facing wall, lies against the road.
            go.transform.rotation=Quaternion.Euler(90,0,0);
            line.alignment=LineAlignment.TransformZ; line.numCornerVertices=2; line.numCapVertices=2;
            line.shadowCastingMode=ShadowCastingMode.Off; line.receiveShadows=false;
            line.lightProbeUsage=LightProbeUsage.Off; line.reflectionProbeUsage=ReflectionProbeUsage.Off;
            line.enabled=false;
            line.positionCount=0;
            return line;
    }

    private void LateUpdate()
    {
        if(modes==null || routes==null)return;
        bool visible=modes.isActiveAndEnabled && !modes.IsStreet && !modes.IsTransitioning;
        if(visible && (!wasVisible || Time.unscaledTime>=nextRefresh)) {
            nextRefresh=Time.unscaledTime+1;
            MissingGroundSamples=0;
            for(int i=0;i<2;i++) Rebuild(routes[i],i);
        }
        VisibleSections=0;
        for(int i=0;i<2;i++) foreach(var line in lines[i]) {
            line.enabled=visible && line.positionCount>=2;
            if(line.enabled)VisibleSections++;
            line.widthMultiplier=Mathf.Max(.05f,modes.OverviewRouteWidth);
            Color color=i==0?new Color(230/255f,162/255f,60/255f):new Color(118/255f,184/255f,82/255f);
            color.a=Mathf.Clamp01(modes.OverviewRouteOpacity);
            line.startColor=line.endColor=color;
        }
        wasVisible=visible;
    }

    private void FlushSection(int routeIndex,ref int section)
    {
        if(points.Count>=2) {
            var line=section<lines[routeIndex].Count?lines[routeIndex][section]:CreateLine(routeIndex);
            line.positionCount=points.Count;
            for(int i=0;i<points.Count;i++)line.SetPosition(i,points[i]);
            section++;
        }
        points.Clear();
    }
    private void Rebuild(Transform route,int routeIndex)
    {
        foreach(var line in lines[routeIndex])line.positionCount=0;
        if(route==null || terrain==null || route.childCount<2)return;
        points.Clear(); int section=0;
        for(int i=0;i+1<route.childCount;i++) {
            Vector3 a=route.GetChild(i).position,b=route.GetChild(i+1).position;
            int steps=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(a,b)/2f));
            for(int j=0;j<=steps;j++) {
                if(i>0 && j==0)continue;
                Vector3 sample=Vector3.Lerp(a,b,(float)j/steps);
                int count=Physics.RaycastNonAlloc(sample+Vector3.up*50,Vector3.down,hits,200,~0,QueryTriggerInteraction.Ignore);
                float nearest=float.PositiveInfinity; Vector3 surface=default;
                if(count<hits.Length) for(int k=0;k<count;k++) {
                    var hit=hits[k];
                    if(!hit.transform.IsChildOf(terrain) || hit.normal.y<.4f || hit.distance>=nearest)continue;
                    nearest=hit.distance;surface=hit.point;
                }
                if(float.IsPositiveInfinity(nearest)) {
                    MissingGroundSamples++;
                    FlushSection(routeIndex,ref section);
                    continue; // A missing tile must not suppress the rest of the route or be bridged.
                }
                points.Add(surface+Vector3.up*Mathf.Clamp(modes.OverviewRouteLift,.02f,.2f));
            }
        }
        FlushSection(routeIndex,ref section);
    }
    private void OnDisable(){foreach(var group in lines)foreach(var line in group)if(line!=null)line.enabled=false;wasVisible=false;}
    private void OnDestroy(){if(material!=null)Destroy(material);}
}
