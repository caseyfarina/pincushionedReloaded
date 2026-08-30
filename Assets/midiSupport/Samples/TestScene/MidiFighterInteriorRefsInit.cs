#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace MidiFighter64.Samples
{
    [InitializeOnLoad]
    static class MidiFighterInteriorRefsInit
    {
        const string FOLDER = "Assets/midiSupport/Samples/Resources";
        const string PATH   = FOLDER + "/MidiFighterInteriorRefs.asset";

        static MidiFighterInteriorRefsInit()
        {
            EditorApplication.delayCall += Ensure;
        }

        static void Ensure()
        {
            if (!AssetDatabase.IsValidFolder(FOLDER))
                AssetDatabase.CreateFolder("Assets/midiSupport/Samples", "Resources");

            var refs = AssetDatabase.LoadAssetAtPath<MidiFighterInteriorRefs>(PATH);
            if (refs == null)
            {
                refs = ScriptableObject.CreateInstance<MidiFighterInteriorRefs>();
                refs.prefabs = new GameObject[MidiFighterInteriorRefs.Paths.Length];
                AssetDatabase.CreateAsset(refs, PATH);
            }

            if (refs.prefabs == null || refs.prefabs.Length != MidiFighterInteriorRefs.Paths.Length)
                refs.prefabs = new GameObject[MidiFighterInteriorRefs.Paths.Length];

            bool dirty = false;
            for (int i = 0; i < MidiFighterInteriorRefs.Paths.Length; i++)
            {
                if (refs.prefabs[i] == null)
                {
                    refs.prefabs[i] = AssetDatabase.LoadAssetAtPath<GameObject>(MidiFighterInteriorRefs.Paths[i]);
                    dirty = true;
                }
            }

            if (dirty)
            {
                EditorUtility.SetDirty(refs);
                AssetDatabase.SaveAssets();
                Debug.Log($"[MidiFighterInteriorRefs] Populated {MidiFighterInteriorRefs.Paths.Length} prefabs at {PATH}");
            }
        }
    }
}
#endif
