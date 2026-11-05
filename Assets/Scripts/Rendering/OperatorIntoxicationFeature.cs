using MoonThemedGame.Interactions;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MoonThemedGame.Rendering
{
    public sealed class OperatorIntoxicationFeature : FullScreenPassRendererFeature
    {
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData renderingData)
        {
            var camera=renderingData.cameraData.camera;
            var state=camera?camera.GetComponentInParent<OperatorIntoxication>():null;
            if(!state || !state.Affects(camera))return;
            if(!passMaterial)passMaterial=Resources.Load<Material>("OperatorIntoxication");
            if(!passMaterial)return;
            passMaterial.SetFloat("_Strength",state.Strength);
            passMaterial.SetFloat("_Phase",state.Phase);
            base.AddRenderPasses(renderer,ref renderingData);
        }
    }
}
