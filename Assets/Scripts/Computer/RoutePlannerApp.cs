using System.Collections;
using System.Collections.Generic;
using MoonThemedGame.Interactions;
using MoonThemedGame.Repair;
using MoonThemedGame.Scavenger;
using MoonThemedGame.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace MoonThemedGame.Computer
{
    // A surveyed station chart, with run-local discoveries and one shared rover waypoint.
    [DisallowMultipleComponent]
    public sealed class RoutePlannerApp : MonoBehaviour
    {
        sealed class Site
        {
            public Vector3 position;
            public string code;
            public bool discovered;
            public readonly List<RobotLever> controls=new List<RobotLever>();
            public Label label;
            public bool Open=>controls.Count>0&&controls[0]&&controls[0].PassageOpen;
        }
        [Min(5)] public float discoveryRadius=30;
        [Range(256,1024)] public int surveyResolution=768;
        public bool IsOpen=>window!=null&&window.style.display==DisplayStyle.Flex;
        public bool HasWaypoint {get;private set;}
        public Vector3 Waypoint {get;private set;}
        public bool SurveyReady {get;private set;}
        public int DiscoveredPassages {get {int count=0;foreach(var site in sites)if(site.controls.Count>0&&site.discovered)count++;return count;}}
        public Bounds WorldBounds {get;private set;}
        PcMonitorTerminal terminal;
        ScavengerLink link;
        VisualElement boundRoot,icon,window,canvas;
        Image terrain;
        Label title,iconName,summary,legend,waypointText,instructions,footer,roverLabel,waypointLabel;
        readonly List<Site> sites=new List<Site>();
        readonly List<(Label label,System.Action action)> actions=new List<(Label,System.Action)>();
        Texture2D chart;
        Color32[] pixels;
        float[] heights;
        float zoom=1,nextRefresh,mapWidth,mapHeight;
        Vector2 viewCenter,lastPan;
        bool panning;
        static readonly Color Ink=new Color(.9f,.91f,.78f),Muted=new Color(.5f,.54f,.42f),Amber=new Color(1,.78f,.35f),Good=new Color(.55f,.85f,.65f);
        static string T(string en,string ru)=>Loc.Russian?ru:en;
        void Awake()=>terminal=GetComponent<PcMonitorTerminal>();
        void OnEnable()=>Loc.Changed+=Refresh;
        void OnDisable(){Loc.Changed-=Refresh;Close();}
        IEnumerator Start()
        {
            while(!(link=GetComponent<ScavengerLink>())||!link.robot)yield return null;
            yield return null;
            FindWorld();BuildSites();
            CaptureSurface();
        }
        void FindWorld()
        {
            bool found=false;var bounds=new Bounds();
            foreach(var mesh in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                if(mesh.gameObject.scene==gameObject.scene&&mesh.sharedMesh&&(LandscapeCollisions.IsSurface(mesh.name)||LandscapeCollisions.IsSurface(mesh.sharedMesh.name))&&mesh.TryGetComponent<Renderer>(out var renderer))
                {if(!found){bounds=renderer.bounds;found=true;}else bounds.Encapsulate(renderer.bounds);}
            if(!found)
            {
                bounds=new Bounds(link.robot.position,new Vector3(100,100,100));
                foreach(var point in SalvageSpawnPoint.All)if(point&&point.gameObject.scene==gameObject.scene)bounds.Encapsulate(point.transform.position);
            }
            bounds.Expand(new Vector3(12,40,12));WorldBounds=bounds;viewCenter=new Vector2(bounds.center.x,bounds.center.z);
        }
        void CaptureSurface()
        {
            // Orthographic mesh scan: physics volumes never enter this image.
            // The alpha channel carries visible-surface height for map clicks.
            var shader=Resources.Load<Shader>("RouteSurfaceScan");
            if(!shader){Debug.LogError("Route-LINK surface scan shader is missing.",this);return;}
            int n=Mathf.Clamp(surveyResolution,256,1024);
            var material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
            material.SetVector("_ScanMin",WorldBounds.min);material.SetVector("_ScanSize",WorldBounds.size);
            var target=RenderTexture.GetTemporary(n,n,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var readback=new Texture2D(n,n,TextureFormat.RGBAFloat,false,true);
            var command=new CommandBuffer{name="Route-LINK top-down visible surface scan"};
            var previous=RenderTexture.active;
            try
            {
                command.SetRenderTarget(target);
                command.SetViewport(new Rect(0,0,n,n));
                // Unity converts the far-plane clear value for reversed-Z graphics APIs.
                command.ClearRenderTarget(true,true,new Color(0,0,0,-1),1);
                foreach(var mesh in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                {
                    if(!mesh.sharedMesh||mesh.gameObject.scene!=gameObject.scene||!mesh.TryGetComponent<MeshRenderer>(out var renderer)||
                        !renderer.enabled||renderer.forceRenderingOff||renderer.shadowCastingMode==ShadowCastingMode.ShadowsOnly||
                        renderer.gameObject.layer==31||!renderer.bounds.Intersects(WorldBounds)||
                        renderer.GetComponentInParent<FirstPersonController>()||renderer.GetComponentInParent<ScavengerRobotState>()||
                        renderer.GetComponentInParent<RepairRobotTarget>()||renderer.GetComponentInParent<RepairCarryable>())continue;
                    var body=mesh.GetComponentInParent<Rigidbody>();if(body&&!body.isKinematic)continue;
                    var materials=renderer.sharedMaterials;
                    for(int sub=0;sub<mesh.sharedMesh.subMeshCount;sub++)
                    {
                        var source=materials.Length>0?materials[Mathf.Min(sub,materials.Length-1)]:null;
                        if(!source||source.renderQueue>=3000)continue;
                        if(source.HasProperty("_BaseColor")&&source.GetColor("_BaseColor").a<=.001f||
                            source.HasProperty("_Color")&&source.GetColor("_Color").a<=.001f)continue;
                        command.DrawMesh(mesh.sharedMesh,mesh.transform.localToWorldMatrix,material,sub,0);
                    }
                }
                Graphics.ExecuteCommandBuffer(command);
                RenderTexture.active=target;readback.ReadPixels(new Rect(0,0,n,n),0,0);readback.Apply();
                var scan=readback.GetPixels();heights=new float[n*n];pixels=new Color32[n*n];
                for(int i=0;i<scan.Length;i++)
                {
                    bool visible=scan[i].a>=0;heights[i]=visible?WorldBounds.min.y+scan[i].a*WorldBounds.size.y:float.NegativeInfinity;
                    float shade=visible?scan[i].r:0;
                    pixels[i]=visible?new Color(shade*.88f,shade,shade*.69f):new Color(.008f,.012f,.008f);
                }
                if(chart)Destroy(chart);
                chart=new Texture2D(n,n,TextureFormat.RGBA32,false){name="Route-LINK top-down surface scan",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
                chart.SetPixels32(pixels);chart.Apply(false,true);SurveyReady=true;
                if(terrain!=null)terrain.image=chart;Refresh();
            }
            finally
            {
                RenderTexture.active=previous;command.Release();RenderTexture.ReleaseTemporary(target);Destroy(readback);Destroy(material);
            }
        }
        bool Ground(Vector3 position,out Vector3 surface)
        {
            surface=default;if(!chart||heights==null)return false;
            int n=chart.width,x=Mathf.Clamp(Mathf.FloorToInt((position.x-WorldBounds.min.x)/WorldBounds.size.x*n),0,n-1),
                z=Mathf.Clamp(Mathf.FloorToInt((position.z-WorldBounds.min.z)/WorldBounds.size.z*n),0,n-1);
            float height=heights[z*n+x];if(float.IsNegativeInfinity(height))return false;
            surface=new Vector3(position.x,height,position.z);return true;
        }
        void BuildSites()
        {
            var start=ScavengerStartLocation.Marker(ScavengerStartLocation.Find());
            sites.Add(new Site{code="B",position=start?start.position:link.robot.position,discovered=true});
            foreach(var charger in FindObjectsByType<ChargeStationZone>(FindObjectsSortMode.None))
                if(charger.gameObject.scene==gameObject.scene)sites.Add(new Site{code="C",position=charger.transform.TransformPoint(charger.localCenter),discovered=true});
            var mechanisms=new Dictionary<Component,Site>();
            foreach(var lever in RobotLever.All)
            {
                if(!lever||lever.gameObject.scene!=gameObject.scene||!lever.Mechanism)continue;
                if(!mechanisms.TryGetValue(lever.Mechanism,out var site))
                {
                    site=new Site{code=lever.Mechanism is LocationGate?"G":lever.Mechanism is LocationBridge?"H":"L",position=LocationMechanisms.WorldBounds(lever.transform).center};
                    mechanisms.Add(lever.Mechanism,site);sites.Add(site);
                }
                site.controls.Add(lever);
            }
        }
        void Discover()
        {
            if(!link||!link.robot)return;
            foreach(var site in sites)if(!site.discovered)
                foreach(var control in site.controls)
                {
                    var offset=LocationMechanisms.WorldBounds(control.transform).center-link.robot.position;
                    if(new Vector2(offset.x,offset.z).magnitude<=discoveryRadius&&Mathf.Abs(offset.y)<12){site.discovered=true;break;}
                }
        }
        void Update()
        {
            if(!terminal||terminal.Root==null)return;
            if(boundRoot!=terminal.Root)Build();
            if(Time.time>=nextRefresh){nextRefresh=Time.time+.2f;Discover();if(IsOpen)Refresh();}
            if(GameSettingsMenu.BlockGameplayInput)return;
            bool pointer=terminal.IsFocused&&!terminal.IsTransitioning&&terminal.PointerActive&&PcLogin.IsUnlocked(terminal);
            var point=terminal.PointerPosition;var mouse=Mouse.current;
            bool free=!(GetComponent<ShopApp>()?.IsOpen??false)&&!(GetComponent<SatelliteConsoleApp>()?.IsOpen??false)&&!(GetComponent<RepairMessagesApp>()?.IsOpen??false)&&
                !(GetComponent<MoonFinderApp>()?.IsOpen??false)&&!(GetComponent<MoneyGetApp>()?.IsOpen??false)&&!(GetComponent<CrazyCosmonautsApp>()?.IsOpen??false);
            bool hover=pointer&&!IsOpen&&free&&icon.worldBound.Contains(point);icon.EnableInClassList("pointer-over",hover);
            if(IsOpen)
            {
                foreach(var action in actions)action.label.style.color=pointer&&action.label.worldBound.Contains(point)?Color.white:Ink;
                bool over=pointer&&canvas.worldBound.Contains(point);
                if(over&&mouse!=null)
                {
                    float scroll=mouse.scroll.ReadValue().y;
                    if(Mathf.Abs(scroll)>.01f)SetZoom(zoom*Mathf.Exp(scroll*.0018f));
                    var local=canvas.WorldToLocal(point);
                    if(mouse.rightButton.isPressed)
                    {
                        if(panning){var span=ViewSpan;viewCenter+=new Vector2((lastPan.x-local.x)/mapWidth*span.x,(local.y-lastPan.y)/mapHeight*span.y);ClampView();Refresh();}
                        lastPan=local;panning=true;
                    }
                    else panning=false;
                }
                else panning=false;
            }
            if(!pointer||mouse==null||!mouse.leftButton.wasPressedThisFrame)return;
            if(hover){Open();return;}
            if(IsOpen)HandleClick(point);
        }
        public void Open()
        {
            if(!terminal||terminal.Root==null)return;if(boundRoot!=terminal.Root)Build();
            GetComponent<ShopApp>()?.Close();GetComponent<SatelliteConsoleApp>()?.Close();GetComponent<RepairMessagesApp>()?.Close();
            GetComponent<MoonFinderApp>()?.Close();GetComponent<MoneyGetApp>()?.Close();GetComponent<CrazyCosmonautsApp>()?.Close();
            window.style.display=DisplayStyle.Flex;window.BringToFront();terminal.Root.Q("screen-cursor")?.BringToFront();Refresh();
        }
        public void Close(){if(window!=null)window.style.display=DisplayStyle.None;panning=false;}
        public void ClearWaypoint(){HasWaypoint=false;Refresh();}
        public bool SetWaypoint(Vector3 world)
        {
            if(!SurveyReady||world.x<WorldBounds.min.x||world.x>WorldBounds.max.x||world.z<WorldBounds.min.z||world.z>WorldBounds.max.z||!Ground(world,out var hit))return false;
            Waypoint=hit+Vector3.up*.4f;HasWaypoint=true;Refresh();return true;
        }
        void HandleClick(Vector2 panelPoint)
        {
            foreach(var action in actions)if(action.label.worldBound.Contains(panelPoint)){action.action();return;}
            if(!canvas.worldBound.Contains(panelPoint))return;
            var point=canvas.WorldToLocal(panelPoint);
            SalvagePiece selected=null;float nearest=10;
            foreach(var part in SalvagePiece.All)
                if(part&&part.gameObject.scene==gameObject.scene)
                {
                    float distance=Vector2.Distance(WorldToMap(part.PickupPosition),point);
                    if(distance<nearest){nearest=distance;selected=part;}
                }
            if(selected)
            {
                // A known part may be inside the ship or below a bridge. A fresh
                // top-down ray would incorrectly move its marker onto the roof/deck.
                Waypoint=selected.PickupPosition;HasWaypoint=true;Refresh();return;
            }
            SetWaypoint(MapToWorld(point));
        }
        Vector2 ViewSpan
        {
            get
            {
                float units=Mathf.Max(WorldBounds.size.x/Mathf.Max(1,mapWidth),WorldBounds.size.z/Mathf.Max(1,mapHeight))/zoom;
                return new Vector2(mapWidth,mapHeight)*units;
            }
        }
        internal Vector2 WorldToMap(Vector3 world){var span=ViewSpan;return new Vector2(mapWidth*.5f+(world.x-viewCenter.x)/span.x*mapWidth,mapHeight*.5f-(world.z-viewCenter.y)/span.y*mapHeight);}
        Vector3 MapToWorld(Vector2 point){var span=ViewSpan;return new Vector3(viewCenter.x+(point.x/mapWidth-.5f)*span.x,0,viewCenter.y+(.5f-point.y/mapHeight)*span.y);}
        void SetZoom(float value){zoom=Mathf.Clamp(value,1,4);ClampView();Refresh();}
        void ClampView(){viewCenter=new Vector2(Mathf.Clamp(viewCenter.x,WorldBounds.min.x,WorldBounds.max.x),Mathf.Clamp(viewCenter.y,WorldBounds.min.z,WorldBounds.max.z));}
        void HomeView(){zoom=1;viewCenter=new Vector2(WorldBounds.center.x,WorldBounds.center.z);Refresh();}
        static VisualElement Box(VisualElement parent){var e=new VisualElement{pickingMode=PickingMode.Ignore};parent?.Add(e);return e;}
        static Label Text(VisualElement parent,int size=13){var e=new Label{pickingMode=PickingMode.Ignore};e.style.fontSize=size;e.style.color=Ink;e.style.whiteSpace=WhiteSpace.Normal;parent.Add(e);return e;}
        Label Action(VisualElement parent,string text,System.Action action){var label=Text(parent,14);label.text=text;label.style.marginLeft=12;actions.Add((label,action));return label;}
        void Build()
        {
            icon?.RemoveFromHierarchy();window?.RemoveFromHierarchy();boundRoot=terminal.Root;actions.Clear();
            var desktop=terminal.Root.Q("desktop");if(desktop==null)return;
            icon=Box(desktop);icon.name="route-map";icon.AddToClassList("desktop-icon");icon.style.position=Position.Absolute;icon.style.left=204;icon.style.top=175;
            var art=new Image{vectorImage=terminal.assets.mapIcon,scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};art.AddToClassList("icon-image");icon.Add(art);
            iconName=Text(icon,17);iconName.AddToClassList("icon-label");
            window=Box(desktop);window.name="route-map-window";window.style.position=Position.Absolute;window.style.left=8;window.style.top=8;
            window.style.width=terminal.assets.resolution.x-16;window.style.height=terminal.assets.resolution.y-16;
            window.style.paddingLeft=window.style.paddingRight=12;window.style.paddingTop=window.style.paddingBottom=10;
            window.style.backgroundColor=new Color(.012f,.019f,.012f,1);window.style.display=DisplayStyle.None;
            window.style.borderTopWidth=window.style.borderBottomWidth=window.style.borderLeftWidth=window.style.borderRightWidth=1;
            window.style.borderTopColor=window.style.borderBottomColor=window.style.borderLeftColor=window.style.borderRightColor=Muted;
            var header=Box(window);header.style.flexDirection=FlexDirection.Row;header.style.marginBottom=8;title=Text(header,18);title.style.flexGrow=1;
            Action(header,"[-]",()=>SetZoom(zoom/1.4f));Action(header,"[+]",()=>SetZoom(zoom*1.4f));Action(header,"[X]",Close);
            var row=Box(window);row.style.flexDirection=FlexDirection.Row;
            canvas=Box(row);canvas.name="route-map-canvas";mapWidth=Mathf.Max(280,terminal.assets.resolution.x-318);mapHeight=Mathf.Max(220,terminal.assets.resolution.y-126);
            canvas.style.width=mapWidth;canvas.style.height=mapHeight;canvas.style.flexShrink=0;canvas.style.overflow=Overflow.Hidden;canvas.style.backgroundColor=new Color(.006f,.009f,.006f);
            terrain=new Image{image=chart,scaleMode=ScaleMode.StretchToFill,pickingMode=PickingMode.Ignore};terrain.style.position=Position.Absolute;canvas.Add(terrain);
            var drawing=Box(canvas);drawing.style.position=Position.Absolute;drawing.style.left=drawing.style.right=drawing.style.top=drawing.style.bottom=0;drawing.generateVisualContent+=DrawMap;
            foreach(var site in sites)site.label=null;
            roverLabel=Text(canvas,11);waypointLabel=Text(canvas,11);roverLabel.style.position=waypointLabel.style.position=Position.Absolute;
            var side=Box(row);side.style.flexGrow=1;side.style.marginLeft=14;
            instructions=Text(side,13);instructions.style.marginBottom=14;
            waypointText=Text(side,15);waypointText.style.color=Amber;waypointText.style.marginBottom=12;
            summary=Text(side,13);summary.style.marginBottom=14;
            legend=Text(side,12);legend.style.color=Muted;legend.style.marginBottom=12;
            var buttons=Box(side);buttons.style.flexDirection=FlexDirection.Column;
            var clear=Action(buttons,"",ClearWaypoint);clear.name="route-map-clear";clear.style.marginLeft=0;clear.style.marginBottom=10;
            var home=Action(buttons,"",HomeView);home.name="route-map-home";home.style.marginLeft=0;
            footer=Text(window,12);footer.style.color=Muted;footer.style.marginTop=8;Refresh();
        }
        void Refresh()
        {
            if(window==null)return;
            iconName.text=T("MAP","КАРТА");title.text=T("ROUTE-LINK [TOP-DOWN SCAN]","ROUTE-LINK [СКАН СВЕРХУ]");
            instructions.text=T("CLICK MAP [SET WAYPOINT]\nWheel: zoom\nRMB drag: pan","НАЖМИТЕ НА КАРТУ [МЕТКА]\nКолесо: масштаб\nПКМ: перемещение карты");
            waypointText.text=HasWaypoint?T("WAYPOINT [","МЕТКА [")+Mathf.CeilToInt(Flat(Waypoint-(link&&link.robot?link.robot.position:Waypoint)).magnitude)+" m]":T("NO WAYPOINT","МЕТКА НЕ ЗАДАНА");
            int total=0;foreach(var site in sites)if(site.controls.Count>0)total++;
            summary.text=(link&&link.state?T("BATTERY ","ЗАРЯД ")+Mathf.RoundToInt(link.state.Normalized*100)+"%\n"+T("CARGO ","ГРУЗ ")+link.state.Cargo+" / "+link.state.cargoCapacity+"\n\n":"")+T("PASSAGES FOUND ","ПРОХОДОВ НАЙДЕНО ")+DiscoveredPassages+" / "+total;
            legend.text=T("B  BASE [UNLOAD]\nC  CHARGER\nR  SCAV_BOT\n.  REQUIRED PART\n+  WAYPOINT\nG  GATE\nH  BRIDGE\nL  LIFT","B  БАЗА [ВЫГРУЗКА]\nC  ЗАРЯДКА\nR  SCAV_BOT\n.  НУЖНАЯ ДЕТАЛЬ\n+  МЕТКА\nG  ВОРОТА\nH  МОСТ\nL  ЛИФТ");
            window.Q<Label>("route-map-clear").text=T("[CLEAR WAYPOINT]","[УБРАТЬ МЕТКУ]");window.Q<Label>("route-map-home").text=T("[FULL MAP]","[ВСЯ КАРТА]");
            footer.text=SurveyReady?T("DIRECT BEARING [CHECK GATES]   ZOOM [","НАПРАВЛЕНИЕ [УЧИТЫВАЙТЕ ВОРОТА]   МАСШТАБ [")+zoom.ToString("0.0")+"x]":T("SURVEYING SURFACE...","СКАНИРОВАНИЕ ПОВЕРХНОСТИ...");
            if(WorldBounds.size.x<=0)return;
            var top=WorldToMap(new Vector3(WorldBounds.min.x,0,WorldBounds.max.z));var bottom=WorldToMap(new Vector3(WorldBounds.max.x,0,WorldBounds.min.z));
            terrain.style.left=top.x;terrain.style.top=top.y;terrain.style.width=bottom.x-top.x;terrain.style.height=bottom.y-top.y;
            foreach(var site in sites)
            {
                if(site.label==null){site.label=Text(canvas,11);site.label.style.position=Position.Absolute;}
                string state=site.controls.Count>0?" ["+(site.Open?T("OPEN","ОТКРЫТО"):T("CLOSED","ЗАКРЫТО"))+"]":"";
                site.label.text=site.code+state;site.label.style.color=site.controls.Count>0?(site.Open?Good:Muted):Ink;
                PositionLabel(site.label,site.position,site.discovered);
            }
            roverLabel.text="R";roverLabel.style.color=Good;PositionLabel(roverLabel,link&&link.robot?link.robot.position:WorldBounds.center,link&&link.robot&&link.robot.gameObject.activeInHierarchy,true);
            waypointLabel.text="+";waypointLabel.style.color=Amber;PositionLabel(waypointLabel,Waypoint,HasWaypoint);
            canvas.MarkDirtyRepaint();foreach(var child in canvas.Children())child.MarkDirtyRepaint();
        }
        void PositionLabel(Label label,Vector3 world,bool show,bool below=false){var point=WorldToMap(world);label.style.display=show&&Inside(point)?DisplayStyle.Flex:DisplayStyle.None;label.style.left=point.x+(below?-4:6);label.style.top=point.y+(below?4:-16);}
        bool Inside(Vector2 p)=>p.x>=0&&p.y>=0&&p.x<=mapWidth&&p.y<=mapHeight;
        void DrawMap(MeshGenerationContext context)
        {
            if(WorldBounds.size.x<=0)return;var p=context.painter2D;p.lineWidth=1;p.strokeColor=new Color(.4f,.5f,.35f,.15f);
            var span=ViewSpan;float step=50;
            for(float x=Mathf.Floor((viewCenter.x-span.x*.5f)/step)*step;x<viewCenter.x+span.x*.5f;x+=step)Line(p,WorldToMap(new Vector3(x,0,viewCenter.y-span.y*.5f)),WorldToMap(new Vector3(x,0,viewCenter.y+span.y*.5f)));
            for(float z=Mathf.Floor((viewCenter.y-span.y*.5f)/step)*step;z<viewCenter.y+span.y*.5f;z+=step)Line(p,WorldToMap(new Vector3(viewCenter.x-span.x*.5f,0,z)),WorldToMap(new Vector3(viewCenter.x+span.x*.5f,0,z)));
            foreach(var site in sites)if(site.discovered)Dot(p,WorldToMap(site.position),4,site.controls.Count>0?(site.Open?Good:Muted):Ink);
            foreach(var part in SalvagePiece.All)if(part&&part.gameObject.scene==gameObject.scene)Dot(p,WorldToMap(part.PickupPosition),2.5f,Ink);
            if(link&&link.robot&&link.robot.gameObject.activeInHierarchy)
            {
                var rover=WorldToMap(link.robot.position);Dot(p,rover,4,Good);
                if(HasWaypoint)
                {
                    p.strokeColor=new Color(Amber.r,Amber.g,Amber.b,.65f);var destination=WorldToMap(Waypoint);var delta=destination-rover;float length=delta.magnitude;
                    for(float d=0;d<length;d+=10)Line(p,rover+delta.normalized*d,rover+delta.normalized*Mathf.Min(length,d+5));
                    p.strokeColor=Amber;p.lineWidth=2;Line(p,destination+Vector2.left*6,destination+Vector2.right*6);Line(p,destination+Vector2.up*6,destination+Vector2.down*6);
                }
            }
        }
        static void Line(Painter2D p,Vector2 a,Vector2 b){p.BeginPath();p.MoveTo(a);p.LineTo(b);p.Stroke();}
        void Dot(Painter2D p,Vector2 at,float radius,Color color){if(!Inside(at))return;p.fillColor=color;p.BeginPath();p.Arc(at,radius,0,360);p.Fill();}
        static Vector3 Flat(Vector3 v){v.y=0;return v;}
        void OnDestroy(){if(chart)Destroy(chart);icon?.RemoveFromHierarchy();window?.RemoveFromHierarchy();}
    }
}
