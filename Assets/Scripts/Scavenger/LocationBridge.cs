using UnityEngine;
namespace MoonThemedGame.Scavenger
{
    [DisallowMultipleComponent]
    public sealed class LocationBridge : MonoBehaviour
    {
        [Min(.1f)] public float loweringSeconds=5;
        public Transform loweredPose;
        // Cube.080 has its longitudinal direction baked diagonally into the FBX
        // vertices. Use the actual road edge, not the mesh's axis-aligned bounds.
        public Vector3 localHinge=new Vector3(.000333f,.02068916f,.005946622f);
        public Vector3 localSpanAxis=new Vector3(-.41338532f,-.9105562f,0);
        public Vector3 localDeckNormal=Vector3.forward;
        public Vector3 hinge;
        public Vector3 loweredPosition;
        public Vector3 loweredEuler;
        [SerializeField] Transform controlButton;
        Rigidbody body;

        void Start()
        {
            if (!body && controlButton) Setup(controlButton);
        }
        Vector3 raisedPosition;
        Quaternion raisedRotation,loweredRotation;
        float progress;
        bool requested,readyReported;
        public bool IsLowered=>progress>=1;
        public bool IsInitialized=>body;
        public void Setup(Transform button)
        {
            controlButton=button;
            raisedPosition=transform.position;raisedRotation=transform.rotation;
            var filter=GetComponent<MeshFilter>();
            hinge=transform.TransformPoint(localHinge);
            Vector3 along=transform.TransformDirection(localSpanAxis.normalized);
            var horizontal=Vector3.ProjectOnPlane(along,Vector3.up);
            if(horizontal.magnitude<.05f)
                horizontal=button?Vector3.ProjectOnPlane(hinge-button.position,Vector3.up):Vector3.forward;
            if(horizontal.sqrMagnitude<.001f)horizontal=Vector3.forward;
            loweredRotation=Quaternion.LookRotation(horizontal.normalized,Vector3.up)*Quaternion.Inverse(Quaternion.LookRotation(localSpanAxis,localDeckNormal));
            var turn=loweredRotation*Quaternion.Inverse(raisedRotation);
            loweredPosition=hinge+turn*(raisedPosition-hinge);loweredEuler=loweredRotation.eulerAngles;
            if(loweredPose){loweredPosition=loweredPose.position;loweredRotation=loweredPose.rotation;loweredEuler=loweredRotation.eulerAngles;}
            body=LocationMechanisms.Kinematic(gameObject);
            if(!GetComponent<Collider>() && filter && filter.sharedMesh)gameObject.AddComponent<MeshCollider>().sharedMesh=filter.sharedMesh;
        }
        public void Press()
        {
            if(requested)return;
            requested=true;LocationMechanisms.Say(Loc.Russian?"МОСТ ОПУСКАЕТСЯ":"BRIDGE LOWERING");
        }
        public void Advance(float seconds)
        {
            if(!body || !requested || progress>=1)return;
            progress=Mathf.MoveTowards(progress,1,Mathf.Max(0,seconds)/Mathf.Max(.1f,loweringSeconds));
            var eased=LocationMechanisms.Ease(progress);
            var turn=Quaternion.Slerp(Quaternion.identity,loweredRotation*Quaternion.Inverse(raisedRotation),eased);
            var position=loweredPose?Vector3.Lerp(raisedPosition,loweredPosition,eased):hinge+turn*(raisedPosition-hinge);
            body.MovePosition(position);body.MoveRotation(turn*raisedRotation);
            if(progress>=1 && !readyReported){readyReported=true;LocationMechanisms.Say(Loc.Russian?"МОСТ ОПУЩЕН":"BRIDGE READY");}
        }
        void FixedUpdate()=>Advance(Time.fixedDeltaTime);
    }
}
