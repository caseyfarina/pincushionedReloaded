using UnityEngine;

/// <summary>
/// Fires the cable instrument on a fixed interval, so the scene plays itself
/// with no keyboard and no hardware attached.
///
/// A dumb driver in the same mould as KeyboardCableDriver: it owns a timer and
/// nothing else, and the instrument stays the sole owner of cable state.
///
/// Play mode only, deliberately. [ExecuteAlways] here would have the scene
/// firing cables continuously while you are trying to edit it.
/// </summary>
public class AutoFireCableDriver : MonoBehaviour
{
    public CableInstrument instrument;

    [Tooltip("Seconds between shots.")]
    [Min(0.05f)]
    public float interval = 2f;

    [Tooltip("Fire one immediately on entering Play mode rather than waiting out the first interval.")]
    public bool fireOnStart = true;

    private float _timer;

    private void Reset() => instrument = GetComponent<CableInstrument>();

    private void OnEnable() => _timer = fireOnStart ? 0f : interval;

    private void Update()
    {
        if (instrument == null) return;

        _timer -= Time.deltaTime;
        if (_timer > 0f) return;

        instrument.Fire();

        // Add rather than assign, so a long frame does not silently swallow a
        // shot and drift the rhythm.
        _timer += Mathf.Max(0.05f, interval);
    }
}
