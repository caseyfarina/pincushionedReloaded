using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How a pose entry resolves the instrument's global Play/Freeze toggle.
/// </summary>
public enum PlayOverride { Inherit, ForcePlay, ForceFreeze }

/// <summary>
/// One pad's worth of pose. The per-pose overrides all use an explicit
/// "use" companion bool rather than a magic sentinel value, because 0 is a
/// legitimate hard-cut blend time and must not collide with "inherit".
/// A serializable struct cannot carry non-zero field defaults, so a bare
/// -1 = inherit convention would silently become 0 = hard cut on every
/// newly added array element.
/// </summary>
[System.Serializable]
public struct PoseEntry
{
    public string name;
    public AnimationClip clip;

    [Header("Overrides (leave 'use' off to inherit the instrument's globals)")]
    public bool  useBlendTimeOverride;
    [Min(0f)] public float blendTimeOverride;   // 0 = hard cut for this pad

    public PlayOverride playOverride;

    public bool  usePlaybackRateOverride;
    public float playbackRateOverride;          // negative = reverse
}

/// <summary>
/// The set of poses an instrument can play. A ScriptableObject so the bank is
/// reusable across characters and editable without opening the scene.
///
/// Bank size (how many poses exist) and mixer inputs (how many layers may
/// overlap mid-blend) are independent — see PoseInstrument.
/// </summary>
[CreateAssetMenu(menuName = "Performance/Pose Bank", fileName = "PoseBank")]
public class PoseBank : ScriptableObject
{
    public List<PoseEntry> poses = new List<PoseEntry>();

    public int Count => poses.Count;

    public bool TryGet(int index, out PoseEntry entry)
    {
        if (index < 0 || index >= poses.Count) { entry = default; return false; }
        entry = poses[index];
        return entry.clip != null;
    }

    public int IndexOf(string poseName)
    {
        for (int i = 0; i < poses.Count; i++)
            if (poses[i].name == poseName) return i;
        return -1;
    }
}
