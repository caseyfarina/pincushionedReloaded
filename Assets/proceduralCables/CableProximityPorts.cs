using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Finds ports on whatever surfaces happen to be near the source, instead of
/// laying them out on a grid. The patch bay becomes the room.
///
/// Probes only when a cable is fired, not every frame. At roughly one cable
/// every couple of seconds that is a few dozen rays every couple of seconds,
/// so there is nothing here worth optimising - no spatial structure, no jobs,
/// no continuous scanning.
///
/// Found ports persist rather than being discarded with their cable, so the
/// room accumulates a patch bay that stays put and can be rendered like any
/// other. The oldest is recycled once the cap is reached.
/// </summary>
public class CableProximityPorts : MonoBehaviour
{
    [Tooltip("How far from the source a surface may be and still be plugged into.")]
    public float searchRadius = 12f;

    [Tooltip("Probes per attempt to find one port. A failed search simply fires nothing that beat.")]
    [Min(1)] public int maxAttempts = 24;

    [Tooltip("Highest a probe will aim, as a height fraction. Rooms have little worth patching into overhead, so this stays low.")]
    [Range(-1f, 1f)] public float upLimit = 0.3f;

    [Tooltip("How square-on a surface must be to accept a plug. 0 takes anything, 1 only a surface facing the source dead on.")]
    [Range(0f, 1f)] public float minFacing = 0.35f;

    [Tooltip("Closest two ports may sit. Without this, probes pile onto whatever large flat surface is nearest.")]
    public float minPortSpacing = 1.5f;

    [Tooltip("What counts as a patchable surface.")]
    public LayerMask surfaces = ~0;

    [Tooltip("Ports kept before the oldest is recycled.")]
    [Min(1)] public int portCap = 64;

    [Header("Lifetime")]
    [Tooltip("Seconds a port hangs around after its last cable has gone.")]
    public float linger = 1f;

    [Tooltip("Seconds a port takes to pop into being. Uses DOTween's OutBack curve, so it overshoots slightly.")]
    public float growTime = 0.25f;

    [Tooltip("Seconds a port takes to shrink away at the end of its linger.")]
    public float shrinkTime = 0.25f;

    private readonly List<Vector3> _points = new List<Vector3>();
    private readonly List<Vector3> _normals = new List<Vector3>();
    private readonly List<float> _born = new List<float>();

    // Negative while something is plugged in or on its way. Set to the clock
    // the moment a port's last cable leaves.
    private readonly List<float> _freedAt = new List<float>();

    private float _clock;

    public int Count => _points.Count;
    public Vector3 PointAt(int i) => (i >= 0 && i < _points.Count) ? _points[i] : Vector3.zero;

    /// <summary>The direction a plug travels to seat here: into the surface.</summary>
    public Vector3 SeatAxisAt(int i) => (i >= 0 && i < _normals.Count) ? -_normals[i] : Vector3.forward;

    public void Clear() { _points.Clear(); _normals.Clear(); _born.Clear(); _freedAt.Clear(); }

    /// <summary>How big a port should be drawn right now. 0 means do not draw it.</summary>
    public float Scale01(int i)
    {
        if (i < 0 || i >= _points.Count) return 0f;

        float sinceFreed = _freedAt[i] < 0f ? -1f : _clock - _freedAt[i];
        return CablePortLife.Scale01(_clock - _born[i], sinceFreed, linger, growTime, shrinkTime);
    }

    /// <summary>
    /// Advance the clock and note which ports still have a cable on them.
    ///
    /// Dead slots are tombstoned rather than removed: a port's index is written
    /// into every cable that uses it, so compacting the list would re-point live
    /// cables at the wrong holes. A dead slot is reused by the next discovery
    /// instead.
    /// </summary>
    public void RefreshLifetimes(float deltaTime, bool[] inUse, int count)
    {
        _clock += deltaTime;

        for (int i = 0; i < _points.Count; i++)
        {
            bool used = inUse != null && i < count && i < inUse.Length && inUse[i];

            if (used) _freedAt[i] = -1f;
            else if (_freedAt[i] < 0f) _freedAt[i] = _clock;
        }
    }

    private bool IsSlotDead(int i) =>
        _freedAt[i] >= 0f && CablePortLife.IsDead(_clock - _freedAt[i], linger);

    /// <summary>
    /// Find a port near the source, reusing one already discovered there if the
    /// probe lands close to it. Returns -1 when nothing suitable was within
    /// reach, which the instrument treats as "no cable this beat" rather than
    /// firing one into empty space.
    /// </summary>
    public int FindPort(int id, Vector3 source, uint seed)
    {
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            Vector3 dir = CableProbe.Direction(id, attempt, seed, upLimit);

            // Triggers are ignored deliberately: this project's FloorVolume
            // boxes are triggers, and taking them would hang ports in mid-air.
            if (!Physics.Raycast(source, dir, out RaycastHit hit, searchRadius,
                                 surfaces, QueryTriggerInteraction.Ignore))
                continue;

            if (!CableProbe.Accepts(source, hit.point, hit.normal, dir, searchRadius, minFacing))
                continue;

            int existing = NearestPort(hit.point);
            if (existing >= 0) return existing;

            // At the cap the room is already saturated, so share the closest
            // port rather than growing without bound. Retiring an old port
            // instead would shift every index and re-point live cables at the
            // wrong holes.
            if (LiveCount() >= Mathf.Max(1, portCap))
                return ClosestPort(hit.point);

            return AddPort(hit.point, hit.normal);
        }

        return -1;
    }

    /// <summary>A port already discovered within the spacing distance, or -1.</summary>
    private int NearestPort(Vector3 point)
    {
        if (minPortSpacing <= 0f) return -1;

        float sqr = minPortSpacing * minPortSpacing;
        for (int i = 0; i < _points.Count; i++)
            if (!IsSlotDead(i) && (_points[i] - point).sqrMagnitude < sqr) return i;

        return -1;
    }

    private int LiveCount()
    {
        int n = 0;
        for (int i = 0; i < _points.Count; i++) if (!IsSlotDead(i)) n++;
        return n;
    }

    /// <summary>The nearest discovered port at any distance, or -1 if there are none.</summary>
    private int ClosestPort(Vector3 point)
    {
        int best = -1;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < _points.Count; i++)
        {
            float d = (_points[i] - point).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = i; }
        }

        return best;
    }

    private int AddPort(Vector3 point, Vector3 normal)
    {
        Vector3 n = normal.sqrMagnitude > 1e-10f ? normal.normalized : Vector3.up;

        // Reuse a tombstoned slot before growing the list, so a source moving
        // through a room does not accumulate indices for ever.
        for (int i = 0; i < _points.Count; i++)
        {
            if (!IsSlotDead(i)) continue;

            _points[i] = point;
            _normals[i] = n;
            _born[i] = _clock;
            _freedAt[i] = -1f;
            return i;
        }

        _points.Add(point);
        _normals.Add(n);
        _born.Add(_clock);
        _freedAt.Add(-1f);
        return _points.Count - 1;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.8f);
        for (int i = 0; i < _points.Count; i++)
        {
            if (IsSlotDead(i)) continue;
            Gizmos.DrawWireSphere(_points[i], 0.15f);
            Gizmos.DrawLine(_points[i], _points[i] + _normals[i] * 0.6f);
        }

        Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.15f);
        Gizmos.DrawWireSphere(transform.position, searchRadius);
    }
}
