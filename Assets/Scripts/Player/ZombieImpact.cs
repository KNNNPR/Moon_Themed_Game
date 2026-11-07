using UnityEngine;

namespace MoonThemedGame.Repair
{
    public static class ZombieImpact
    {
        // Suit insulation and pale dust, with no blood or red particles.
        public static void Emit(Vector3 point, Vector3 normal, float scale, Material material, bool glass = false)
        {
            var root = new GameObject(glass ? "Lamp / glass shards" : "Zombie / suit debris");
            root.transform.SetPositionAndRotation(point, Quaternion.LookRotation(normal.sqrMagnitude > .01f ? normal : Vector3.up));
            var particles = root.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = false; main.playOnAwake = false; main.duration = .1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.3f, glass ? 1.5f : .8f);
            main.startSize = new ParticleSystem.MinMaxCurve(.008f * scale, .035f * scale);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.5f * scale, 2.5f * scale);
            main.startColor = new ParticleSystem.MinMaxGradient(glass ? new Color(.55f, .8f, .9f) : new Color(.43f, .47f, .44f), new Color(.9f, .93f, .85f));
            main.gravityModifier = glass ? .22f : .04f; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 48;
            var emission = particles.emission; emission.enabled = false;
            var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 65; shape.radius = .025f * scale;
            var fade = particles.colorOverLifetime; fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) }); fade.color = gradient;
            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material; renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = glass ? 2 : .4f; renderer.velocityScale = .03f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            particles.Play(); particles.Emit(glass ? 36 : 20);
            Object.Destroy(root, 2);
        }
    }
}
