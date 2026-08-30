using UnityEngine;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// ScriptableObject that holds references to the particle prefabs used by
    /// MidiMixTestScene. Lives in a Resources folder so it loads in builds.
    /// Auto-populated in the Editor by MidiMixParticleRefsInit.
    /// </summary>
    public class MidiMixParticleRefs : ScriptableObject
    {
        public GameObject[] faderParticles   = new GameObject[8];
        public GameObject[] explosionPrefabs = new GameObject[8];

        public const string ResourceName = "MidiMixParticleRefs";

        // Paths used by the editor initialiser to populate this asset
        internal static readonly string[] FaderPaths =
        {
            "Assets/UnityTechnologies/ParticlePack/EffectExamples/Fire & Explosion Effects/Prefabs/WildFire.prefab",
            "Assets/UnityTechnologies/ParticlePack/EffectExamples/Misc Effects/Prefabs/ElectricalSparks.prefab",
            "Assets/UnityTechnologies/ParticlePack/EffectExamples/Legacy Particles/Prefabs/RainEffect.prefab",
            "Assets/UnityTechnologies/ParticlePack/EffectExamples/Smoke & Steam Effects/Prefabs/DustStorm.prefab",
            "Assets/UnityTechnologies/ParticlePack/EffectExamples/Misc Effects/Prefabs/FireFlies.prefab",
            "Assets/UnityTechnologies/ParticlePack/EffectExamples/Goop Effects/Prefabs/GoopSpray.prefab",
            "Assets/UnityTechnologies/ParticlePack/EffectExamples/Magic Effects/Prefabs/IceLance.prefab",
            "Assets/UnityTechnologies/ParticlePack/EffectExamples/Misc Effects/Prefabs/SparksEffect.prefab",
        };

        internal static readonly string[] ExplosionPaths =
        {
            "Assets/UnityTechnologies/ParticlePack/EffectExamples/Fire & Explosion Effects/Prefabs/BigExplosion.prefab",
            "Assets/UnityTechnologies/ParticlePack/EffectExamples/Fire & Explosion Effects/Prefabs/EnergyExplosion.prefab",
            "Assets/UnityTechnologies/ParticlePack/EffectExamples/Fire & Explosion Effects/Prefabs/FireBall.prefab",
            "Assets/UnityTechnologies/ParticlePack/EffectExamples/Fire & Explosion Effects/Prefabs/SmallExplosion.prefab",
            "Assets/UnityTechnologies/ParticlePack/EffectExamples/Fire & Explosion Effects/Prefabs/DustExplosion.prefab",
            "Assets/UnityTechnologies/ParticlePack/EffectExamples/Fire & Explosion Effects/Prefabs/TinyExplosion.prefab",
            "Assets/UnityTechnologies/ParticlePack/EffectExamples/Fire & Explosion Effects/Prefabs/LargeFlames.prefab",
            "Assets/UnityTechnologies/ParticlePack/EffectExamples/Fire & Explosion Effects/Prefabs/FlameStream.prefab",
        };
    }
}
