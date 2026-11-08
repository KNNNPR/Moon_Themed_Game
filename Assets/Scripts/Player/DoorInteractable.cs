using MoonThemedGame.Audio;
using UnityEngine;

namespace MoonThemedGame.Interactions
{
    [DisallowMultipleComponent]
    public sealed class DoorInteractable : Interactable
    {
        public enum Motion { Slide, Rotate }
        public Transform movingPart;
        public Motion motion = Motion.Slide;
        [Tooltip("Offset in the moving part's PARENT space, not mesh space.")]
        public Vector3 openOffset = new Vector3(2f, 0f, 0f);
        public bool useWorldSlideDirection = true;
        [Tooltip("Choose left along the doorway as seen from the player's starting side.")]
        public bool slideLeftAlongDoor = true;
        public Vector3 worldSlideDirection = Vector3.left;
        [Tooltip("Zero chooses door width automatically.")]
        [Min(0f)] public float slideDistance;
        public Vector3 openEuler = new Vector3(0f, 90f, 0f);
        [Min(0.05f)] public float duration = 0.65f;
        public bool locked;

        Vector3 closedPosition;
        Quaternion closedRotation;
        float progress;
        bool targetOpen;
        [SerializeField] DoorInteractionZone fixedZone;
        GameObject interactionZone;
        Collider[] movingColliders;
        readonly Collider[] safetyOverlaps=new Collider[64];

        public bool IsOpen => targetOpen;
        public override string Prompt => targetOpen ? "E — Close" : "E — Open";
        public override bool CanInteract(Camera camera) => base.CanInteract(camera) && !locked;

        void Awake()
        {
            if (!movingPart) movingPart = transform;
            if (fixedZone) interactionZone = fixedZone.gameObject;
            closedPosition = movingPart.localPosition;
            closedRotation = movingPart.localRotation;
            movingColliders=movingPart.GetComponentsInChildren<Collider>();
            var renderers = movingPart.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length > 0)
            {
                if (!outline) outline = GetComponent<InteractionOutline>();
                if (!outline) outline = gameObject.AddComponent<InteractionOutline>();
                outline.SetFixedGeometry(renderers);
                Bounds bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                if (useWorldSlideDirection)
                {
                    Vector3 direction = worldSlideDirection.sqrMagnitude > 0.001f
                        ? worldSlideDirection.normalized : Vector3.left;
                    if (slideLeftAlongDoor)
                    {
                        Vector3 normal = bounds.size.x < bounds.size.z ? Vector3.right : Vector3.forward;
                        var camera = Camera.main;
                        if (camera && Vector3.Dot(camera.transform.position - bounds.center, normal) < 0f)
                            normal = -normal;
                        direction = Vector3.Cross(Vector3.up, normal).normalized;
                    }
                    float distance = slideDistance > 0f ? slideDistance :
                        Vector3.Dot(bounds.size, new Vector3(Mathf.Abs(direction.x),
                            Mathf.Abs(direction.y), Mathf.Abs(direction.z))) + 0.15f;
                    Vector3 worldOffset = direction * distance;
                    openOffset = movingPart.parent
                        ? movingPart.parent.InverseTransformVector(worldOffset) : worldOffset;
                }
                if (fixedZone) return;
                interactionZone = new GameObject("Door interaction / fixed closed position");
                interactionZone.layer = gameObject.layer;
                interactionZone.transform.position = bounds.center;
                interactionZone.AddComponent<DoorInteractionZone>().door = this;
                var box = interactionZone.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = bounds.size + Vector3.one * 0.04f;

            }
        }

        public override void Interact()
        {
            if (isActiveAndEnabled && !locked){targetOpen = !targetOpen;GameAudio.At(Sound.DoorSlide,movingPart?movingPart.position:transform.position);}
        }

        void OnEnable()
        {
            if (interactionZone) interactionZone.SetActive(true);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (interactionZone) interactionZone.SetActive(false);
        }

        void OnDestroy()
        {
            if (interactionZone) Destroy(interactionZone);
        }

        void Update()=>Advance(Time.deltaTime);
        void Advance(float dt)
        {
            if(dt<=0)return;
            float next=Mathf.MoveTowards(progress,targetOpen?1:0,dt/Mathf.Max(.05f,duration));
            if(Mathf.Approximately(progress,next))return;
            if(!targetOpen&&ClosingBlocked(next))
            {
                targetOpen=true;return;
            }
            progress=next;
            float t = Mathf.SmoothStep(0f, 1f, progress);
            if (motion == Motion.Slide)
                movingPart.localPosition = closedPosition + openOffset * t;
            else
                movingPart.localRotation = closedRotation *
                    Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(openEuler), t);
        }

        bool ClosingBlocked(float next)
        {
            if(movingColliders==null)return false;
            float from=Mathf.SmoothStep(0,1,progress),to=Mathf.SmoothStep(0,1,next);
            float travel=motion==Motion.Slide?movingPart.parent?movingPart.parent.TransformVector(openOffset*(to-from)).magnitude:
                (openOffset*(to-from)).magnitude:Mathf.Abs(to-from)*openEuler.magnitude*Mathf.Deg2Rad*4;
            int samples=Mathf.Clamp(Mathf.CeilToInt(travel/.08f),1,96);
            for(int sample=0;sample<=samples;sample++)
            {
                float t=Mathf.Lerp(from,to,(float)sample/samples);
                var position=motion==Motion.Slide?closedPosition+openOffset*t:movingPart.localPosition;
                var rotation=motion==Motion.Rotate?closedRotation*Quaternion.Slerp(Quaternion.identity,Quaternion.Euler(openEuler),t):movingPart.localRotation;
                var worldPosition=movingPart.parent?movingPart.parent.TransformPoint(position):position;
                var worldRotation=movingPart.parent?movingPart.parent.rotation*rotation:rotation;
                var turn=worldRotation*Quaternion.Inverse(movingPart.rotation);
                foreach(var shape in movingColliders)
                {
                    if(!shape||!shape.enabled||shape.isTrigger)continue;
                    Vector3 localCenter,localSize;
                    if(shape is BoxCollider box){localCenter=box.center;localSize=box.size;}
                    else if(shape is MeshCollider mesh&&mesh.sharedMesh){localCenter=mesh.sharedMesh.bounds.center;localSize=mesh.sharedMesh.bounds.size;}
                    else {localCenter=shape.transform.InverseTransformPoint(shape.bounds.center);localSize=Vector3.zero;}
                    var scale=shape.transform.lossyScale;
                    var half=localSize==Vector3.zero?shape.bounds.extents:
                        Vector3.Scale(localSize*.5f,new Vector3(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z)));
                    var center=worldPosition+turn*(shape.transform.TransformPoint(localCenter)-movingPart.position);
                    var orientation=localSize==Vector3.zero?turn:turn*shape.transform.rotation;
                    int count=Physics.OverlapBoxNonAlloc(center,half+Vector3.one*.06f,safetyOverlaps,orientation,~0,QueryTriggerInteraction.Ignore);
                    if(count==safetyOverlaps.Length)return true;
                    for(int i=0;i<count;i++)
                    {
                        var other=safetyOverlaps[i];
                        if(!other||other.transform.IsChildOf(movingPart)||
                            Physics.GetIgnoreLayerCollision(shape.gameObject.layer,other.gameObject.layer)||Physics.GetIgnoreCollision(shape,other))continue;
                        if(other is CharacterController character&&character.enabled)return true;
                    }
                }
            }
            return false;
        }
    }
}
