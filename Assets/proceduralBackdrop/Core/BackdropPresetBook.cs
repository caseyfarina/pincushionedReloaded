using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct BackdropPreset
{
    public string name;
    public BackdropParameters parameters;
}

/// <summary>
/// Named points in the backdrop parameter space. One asset holding a list
/// rather than one asset per preset, so the whole set can be browsed and
/// reordered in a single inspector during a search session.
///
/// Where BackdropRanges defines the space, this holds the results worth
/// keeping from searching it.
/// </summary>
[CreateAssetMenu(menuName = "Performance/Backdrop Preset Book", fileName = "BackdropPresetBook")]
public class BackdropPresetBook : ScriptableObject
{
    public List<BackdropPreset> presets = new List<BackdropPreset>();

    public int Count => presets.Count;

    public bool TryGet(int index, out BackdropParameters p)
    {
        if (index < 0 || index >= presets.Count) { p = default; return false; }
        p = presets[index].parameters;
        return true;
    }

    /// <summary>
    /// Appends, or overwrites when the name already exists. Overwriting by name
    /// is deliberate: during a search you re-save the same slot repeatedly as a
    /// look is refined, and silently accumulating twelve presets called "tower"
    /// is worse than replacing one.
    /// </summary>
    public void Save(string name, BackdropParameters p)
    {
        for (int i = 0; i < presets.Count; i++)
        {
            if (presets[i].name != name) continue;
            presets[i] = new BackdropPreset { name = name, parameters = p };
            return;
        }

        presets.Add(new BackdropPreset { name = name, parameters = p });
    }
}