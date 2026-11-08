using MoonThemedGame.Audio;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MoonThemedGame.Computer
{
    public sealed class MainMenuTitle : IDisposable
    {
        sealed class Letter
        {
            public Transform transform;
            public Vector3 home,offset,velocity,angle,angularVelocity;
            public float phase;
        }
        readonly List<Letter> letters=new List<Letter>();
        readonly Dictionary<char,Mesh> meshes=new Dictionary<char,Mesh>();
        readonly GameObject holder;
        readonly Camera camera;
        readonly VisualElement captionGroup;
        readonly Label caption;
        readonly Material material;
        readonly float height;
        public int LetterCount=>letters.Count;
        public float MaximumDisplacement {get;private set;}
        public Transform WorldRoot=>holder.transform;

        public MainMenuTitle(VisualElement overlay,Transform parent,Camera camera,MainMenuAssets assets)
        {
            this.camera=camera;
            const float distance=2.35f;
            height=2*distance*Mathf.Tan(camera.fieldOfView*.5f*Mathf.Deg2Rad)*(assets.titleModel ? .145f : .126f);
            holder=new GameObject("Menu / PALE HORSE / 3D letters");holder.transform.SetParent(parent,false);
            holder.transform.SetPositionAndRotation(camera.ViewportToWorldPoint(new Vector3(.575f,.725f,distance)),camera.transform.rotation);
            material=new Material(assets.titleMaterial){name="PALE HORSE / letter metal"};
            if(assets.titleModel)AddAuthoredModel(assets.titleModel);
            else {AddWord("PALE",0);AddWord("HORSE",1);}
            captionGroup=new VisualElement {pickingMode=PickingMode.Ignore};captionGroup.style.position=Position.Absolute;
            captionGroup.style.left=Length.Percent(54);captionGroup.style.top=Length.Percent(assets.titleModel?56:52);overlay.Add(captionGroup);
            caption=new Label {pickingMode=PickingMode.Ignore};caption.style.fontSize=12;caption.style.color=new Color(.65f,.7f,.66f);captionGroup.Add(caption);
            Relocalize();
        }
        public void Relocalize()=>caption.text=Loc.Russian?"ЛУННАЯ СТАНЦИЯ 7  /  НЕТ СИГНАЛА С ЗЕМЛИ":"LUNAR STATION 7  /  NO SIGNAL FROM EARTH";
        void AddAuthoredModel(GameObject prefab)
        {
            var instance=UnityEngine.Object.Instantiate(prefab,holder.transform);
            var pivots=new List<Transform>();foreach(Transform pivot in instance.transform)pivots.Add(pivot);
            foreach(var pivot in pivots)
            {
                var home=pivot.localPosition*height;pivot.SetParent(holder.transform,false);
                pivot.localPosition=home;pivot.localScale=Vector3.one*height;
                foreach(var renderer in pivot.GetComponentsInChildren<MeshRenderer>())
                {renderer.sharedMaterial=material;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;}
                letters.Add(new Letter{transform=pivot,home=home,phase=letters.Count*1.73f});
            }
            UnityEngine.Object.Destroy(instance);
        }
        void AddWord(string word,int row)
        {
            for(int i=0;i<word.Length;i++)
            {
                char character=word[i];
                if(!meshes.TryGetValue(character,out var mesh)){mesh=MenuLetterMesh.Create(character);meshes.Add(character,mesh);}
                var glyph=new GameObject(character+" / extruded letter");glyph.transform.SetParent(holder.transform,false);
                glyph.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=glyph.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
                renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                var letter=new Letter {transform=glyph.transform,home=new Vector3(i*height*.87f,-row*height*1.24f,0),phase=letters.Count*1.73f};
                glyph.transform.localPosition=letter.home;glyph.transform.localScale=Vector3.one*height;letters.Add(letter);
            }
        }
        public void Animate(Vector2 pointer,bool pointerActive,bool click,float focus,float dt,float time)
        {
            float visibility=1-Mathf.SmoothStep(0,1,Mathf.Clamp01(focus*3));
            captionGroup.style.opacity=visibility;holder.SetActive(visibility>.001f);
            float step=Mathf.Min(dt,.04f);float radius=Mathf.Max(1,Screen.height)*.19f;
            object audioLetter=null;
            foreach(var letter in letters)
            {
                var screen=camera.WorldToScreenPoint(letter.transform.position);
                Vector2 delta=new Vector2(screen.x,screen.y)-pointer;float distance=delta.magnitude;
                if(pointerActive && focus<.05f && screen.z>0 && distance<radius)
                {
                    Vector2 away=distance>.5f?delta/distance:new Vector2(Mathf.Cos(letter.phase),Mathf.Sin(letter.phase));
                    var world=camera.transform.right*away.x+camera.transform.up*away.y;
                    if(distance<radius*.5f)audioLetter=letter;
                    float weight=1-distance/radius;
                    letter.velocity+=holder.transform.InverseTransformDirection(world)*weight*height*(click?5.3f:8*step);
                    letter.velocity.z+=weight*height*(click?.8f:.25f*step);
                    letter.angularVelocity+=new Vector3(-away.y*45,away.x*65,away.x*75)*weight*(click?1:step);
                }
                letter.velocity+=(-letter.offset*.8f-letter.velocity*.75f)*step;
                letter.offset=Vector3.ClampMagnitude(letter.offset+letter.velocity*step,height*2.8f);
                letter.angularVelocity+=(-letter.angle*.65f-letter.angularVelocity*.7f)*step;
                letter.angle=Vector3.ClampMagnitude(letter.angle+letter.angularVelocity*step,80);
                var drift=new Vector3(Mathf.Sin(time*.45f+letter.phase)*.08f,Mathf.Sin(time*.58f+letter.phase)*.12f,Mathf.Sin(time*.35f+letter.phase)*.1f)*height;
                letter.transform.localPosition=letter.home+letter.offset+drift;
                letter.transform.localRotation=Quaternion.Euler(letter.angle+new Vector3(Mathf.Sin(time*.4f+letter.phase)*7,10+Mathf.Cos(time*.33f+letter.phase)*9,Mathf.Sin(time*.4f+letter.phase)*3));
                letter.transform.localScale=Vector3.one*height*visibility;
                MaximumDisplacement=Mathf.Max(MaximumDisplacement,letter.offset.magnitude);
            }
            GameAudio.Hover(this,audioLetter,Sound.BigHover);
        }
        public void Dispose()
        {
            foreach(var mesh in meshes.Values)if(mesh)UnityEngine.Object.Destroy(mesh);
            if(material)UnityEngine.Object.Destroy(material);if(holder)UnityEngine.Object.Destroy(holder);
        }
    }
}
