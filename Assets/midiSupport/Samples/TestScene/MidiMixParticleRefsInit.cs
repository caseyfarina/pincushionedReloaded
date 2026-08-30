#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Automatically creates and populates the MidiMixParticleRefs asset in
    /// Assets/midiSupport/Samples/Resources/ whenever the editor loads.
    /// Re-populates any null slots in case paths changed.
    /// </summary>
    [InitializeOnLoad]
    static class MidiMixParticleRefsInit
    {
        const string FOLDER = "Assets/midiSupport/Samples/Resources";
        const string PATH   = FOLDER + "/MidiMixParticleRefs.asset";

        static MidiMixParticleRefsInit()
        {
            EditorApplication.delayCall += Ensure;
        }

        static void Ensure()
        {
            if (!AssetDatabase.IsValidFolder(FOLDER))
                AssetDatabase.CreateFolder("Assets/midiSupport/Samples", "Resources");

            var refs = AssetDatabase.LoadAssetAtPath<MidiMixParticleRefs>(PATH);
            if (refs == null)
            {
                refs = ScriptableObject.CreateInstance<MidiMixParticleRefs>();
                AssetDatabase.CreateAsset(refs, PATH);
            }

            bool dirty = false;

            for (int i = 0; i < 8; i++)
            {
                if (refs.faderParticles[i] == null)
                {
                    refs.faderParticles[i] =
                        AssetDatabase.LoadAssetAtPath<GameObject>(MidiMixParticleRefs.FaderPaths[i]);
                    dirty = true;
                }

                if (refs.explosionPrefabs[i] == null)
                {
                    refs.explosionPrefabs[i] =
                        AssetDatabase.LoadAssetAtPath<GameObject>(MidiMixParticleRefs.ExplosionPaths[i]);
                    dirty = true;
                }
            }

            if (dirty)
            {
                EditorUtility.SetDirty(refs);
                AssetDatabase.SaveAssets();
                Debug.Log("[MidiMixParticleRefs] Asset populated at " + PATH);
            }
        }
    }
}
#endif
