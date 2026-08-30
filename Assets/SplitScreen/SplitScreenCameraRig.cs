using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Pincushioned.SplitScreen
{
    /// <summary>
    /// Quadtree split-screen: the screen subdivides into cells, each showing a
    /// different camera pointed at a shared point of interest.
    ///
    /// Placement follows the 180-degree idea from cinematography. Every sub-camera
    /// sits on a hemisphere centred on the POI and facing the main camera, at a
    /// radius strictly less than the main camera's distance — so no view ever ends
    /// up behind the subject, and none is further away than the master shot.
    ///
    /// Rendering uses one URP base camera per cell with its own viewport rect.
    /// Overlay cameras are not an option: they ignore their rect and inherit the
    /// base camera's. Borders are the gaps left by insetting each rect, with a
    /// full-screen background camera clearing to the border colour behind them.
    /// </summary>
    [RequireComponent(typeof(PointOfInterestFinder))]
    public class SplitScreenCameraRig : MonoBehaviour
    {
        // ── layout ───────────────────────────────────────────────────────────

        [Header("Layout")]
        [Tooltip("How the screen is carved up. Random Rectangles cuts existing cells " +
                 "at random ratios, so sizes vary freely. Quadtree restricts every " +
                 "edge to a half or a quarter.")]
        [SerializeField] LayoutMode _layoutMode = LayoutMode.RandomRectangles;

        [Tooltip("Number of rectangles on screen. Exact in Random Rectangles mode; " +
                 "Quadtree rounds to the nearest 1+3N.")]
        [Range(MosaicLayout.MinCells, MosaicLayout.MaxCells)]
        [SerializeField] int _cellCount = 8;

        [Tooltip("How close a cut may land to a cell's edge, as a fraction of that " +
                 "cell. 0.5 always cuts dead centre; low values allow strongly " +
                 "uneven rectangles. Random Rectangles only.")]
        [Range(0.05f, 0.5f)]
        [SerializeField] float _minSplitRatio = 0.25f;

        [Tooltip("0 picks the cut axis at random. 1 always cuts across the longer " +
                 "screen-space side, keeping cells closest to square. Random " +
                 "Rectangles only.")]
        [Range(0f, 1f)]
        [SerializeField] float _squarenessBias = 0.75f;

        [Tooltip("Seed for the layout. The same seed always rebuilds the same " +
                 "arrangement of cells.")]
        [SerializeField] int _layoutSeed = 12345;

        [Tooltip("Give the main camera the largest cell, so the master shot stays " +
                 "readable and keeps its own framing.")]
        [SerializeField] bool _mainCameraTakesLargestCell = true;

        // ── borders ──────────────────────────────────────────────────────────

        [Header("Borders")]
        [Tooltip("Border thickness in pixels. Converted per axis, so it stays square " +
                 "on a non-square display.")]
        [Range(0f, 40f)]
        [SerializeField] float _borderThickness = 4f;

        [SerializeField] Color _borderColor = Color.black;

        // ── placement ────────────────────────────────────────────────────────

        [Header("Camera placement")]
        [Tooltip("Closest a sub-camera may sit to the POI, as a fraction of the main " +
                 "camera's distance.")]
        [Range(0.05f, 1f)]
        [SerializeField] float _minRadiusFraction = 0.25f;

        [Tooltip("Furthest a sub-camera may sit from the POI, as a fraction of the " +
                 "main camera's distance. Kept under 1 so no view outruns the master shot.")]
        [Range(0.05f, 1f)]
        [SerializeField] float _maxRadiusFraction = 0.85f;

        [Tooltip("Half-angle of the placement dome, measured from the axis running " +
                 "POI -> main camera. 90 is the full hemisphere (never behind the " +
                 "subject). Lower values cluster the views near the master shot's axis.")]
        [Range(10f, 90f)]
        [SerializeField] float _maxAngle = 90f;

        [Tooltip("True 180-degree rule: keep every camera on ONE side of the axis " +
                 "line, so screen direction stays consistent and the subject does not " +
                 "flip left-right between cells. Off allows the flip, for more variety.")]
        [SerializeField] bool _enforceAxisSide = false;

        [Tooltip("Never place a camera below this world Y, so views don't sink " +
                 "through the floor.")]
        [SerializeField] float _minWorldY = 0.5f;

        [Tooltip("Seed for camera placement, separate from the layout seed so you can " +
                 "reroll angles without changing the cell arrangement.")]
        [SerializeField] int _placementSeed = 2024;

        [Tooltip("Re-place the cameras when the POI moves further than this.")]
        [SerializeField] float _repositionThreshold = 2f;

        // ── performance ──────────────────────────────────────────────────────

        [Header("Performance")]
        [Tooltip("Post-processing on the sub-cameras. Off is usually the single " +
                 "biggest win — the stack runs per camera and does not shrink with " +
                 "cell size.")]
        [SerializeField] bool _subCameraPostProcessing = false;

        [Tooltip("Shadows on the sub-cameras.")]
        [SerializeField] bool _subCameraShadows = false;

        [Tooltip("Far clip for sub-cameras. They always sit closer to the POI than " +
                 "the main camera, so they rarely need its full range.")]
        [SerializeField] float _subCameraFarClip = 200f;

        [Tooltip("Field of view for sub-cameras.")]
        [Range(10f, 90f)]
        [SerializeField] float _subCameraFov = 45f;

        // ── runtime ──────────────────────────────────────────────────────────

        readonly List<Rect>   _cells   = new List<Rect>();
        readonly List<Camera> _cameras = new List<Camera>();

        Camera _backgroundCamera;
        Camera _mainCamera;
        Rect   _mainCameraOriginalRect;
        bool   _mainRectCaptured;

        PointOfInterestFinder _poi;
        Vector3 _lastPlacementPoint;
        bool    _placed;

        /// <summary>Cells in the current layout.</summary>
        public IReadOnlyList<Rect> Cells => _cells;

        void Awake()
        {
            _poi = GetComponent<PointOfInterestFinder>();
            _mainCamera = Camera.main;
            CaptureMainRect();
        }

        void OnEnable()
        {
            if (_poi != null) _poi.OnPointChanged += HandlePoiChanged;
            Rebuild();
        }

        void OnDisable()
        {
            if (_poi != null) _poi.OnPointChanged -= HandlePoiChanged;
            Teardown();
        }

        void LateUpdate()
        {
            if (_poi == null || !_poi.HasPoint) return;

            // Aim is cheap, so it tracks every frame. Position is not, so it only
            // changes when the subject has genuinely moved.
            Vector3 p = _poi.Point;
            for (int i = 0; i < _cameras.Count; i++)
                if (_cameras[i] != null) _cameras[i].transform.LookAt(p);

            if (!_placed || Vector3.Distance(p, _lastPlacementPoint) > _repositionThreshold)
                PlaceCameras();
        }

        void HandlePoiChanged(Vector3 p)
        {
            if (Vector3.Distance(p, _lastPlacementPoint) > _repositionThreshold)
                PlaceCameras();
        }

        // ── build ────────────────────────────────────────────────────────────

        /// <summary>Rebuild the layout, the camera pool, and the placement.</summary>
        public void Rebuild()
        {
            CaptureMainRect();
            MosaicLayout.Build(_cellCount, _layoutMode, _layoutSeed,
                               _minSplitRatio, _squarenessBias, CurrentAspect, _cells);
            EnsureBackgroundCamera();
            EnsureCameraPool();
            ApplyViewports();
            PlaceCameras();
        }

        /// <summary>New placement seed, then re-place. Layout is untouched.</summary>
        public void ResetCameraPositions()
        {
            _placementSeed = new System.Random().Next(int.MinValue, int.MaxValue);
            PlaceCameras();
        }

        /// <summary>New layout seed, then rebuild. The placement seed is untouched,
        /// so camera angles regenerate from the same distribution.</summary>
        public void RerollLayout()
        {
            _layoutSeed = new System.Random().Next(int.MinValue, int.MaxValue);
            Rebuild();
        }

        /// <summary>Reroll both the cell arrangement and the camera angles in one
        /// call — the "everything changes" action, for binding to a single pad.
        /// Rerolls placement first so Rebuild's placement pass uses the new seed
        /// and the cameras are not positioned twice.</summary>
        public void RerollAll()
        {
            _placementSeed = new System.Random().Next(int.MinValue, int.MaxValue);
            _layoutSeed    = new System.Random().Next(int.MinValue, int.MaxValue);
            Rebuild();
        }

        /// <summary>Display aspect used to judge which side of a cell is longer.</summary>
        public static float CurrentAspect =>
            Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;

        /// <summary>Set the number of rectangles and rebuild. Clamped to the legal range.</summary>
        public void SetCellCount(int cellCount)
        {
            int v = Mathf.Clamp(cellCount, MosaicLayout.MinCells, MosaicLayout.MaxCells);
            if (v == _cellCount) return;
            _cellCount = v;
            Rebuild();
        }

        /// <summary>Back-compat alias: treats the value as a cell count.</summary>
        public void SetSubdivisions(int subdivisions) => SetCellCount(subdivisions);

        /// <summary>Border thickness in pixels. Applies without a rebuild.</summary>
        public void SetBorderThickness(float pixels)
        {
            _borderThickness = Mathf.Max(0f, pixels);
            ApplyViewports();
        }

        void CaptureMainRect()
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera != null && !_mainRectCaptured)
            {
                _mainCameraOriginalRect = _mainCamera.rect;
                _mainRectCaptured = true;
            }
        }

        int SubCameraCount =>
            Mathf.Max(0, _cells.Count - (UseMainCamera ? 1 : 0));

        bool UseMainCamera => _mainCameraTakesLargestCell && _mainCamera != null;

        void EnsureBackgroundCamera()
        {
            if (_backgroundCamera == null)
            {
                var go = new GameObject("SplitScreen_Background");
                go.transform.SetParent(transform, false);
                // Deliberately NOT hidden: these are inspectable in the hierarchy so
                // individual views can be examined and tuned while the rig runs.
                _backgroundCamera = go.AddComponent<Camera>();
            }

            // Renders nothing; exists purely to clear the whole screen to the border
            // colour so the inset gaps read as borders.
            _backgroundCamera.clearFlags      = CameraClearFlags.SolidColor;
            _backgroundCamera.backgroundColor = _borderColor;
            _backgroundCamera.cullingMask     = 0;
            _backgroundCamera.rect            = new Rect(0f, 0f, 1f, 1f);
            _backgroundCamera.depth           = -100f;
            _backgroundCamera.useOcclusionCulling = false;

            var data = _backgroundCamera.GetComponent<UniversalAdditionalCameraData>()
                    ?? _backgroundCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderType            = CameraRenderType.Base;
            data.renderPostProcessing  = false;
            data.renderShadows         = false;
            data.requiresColorOption   = CameraOverrideOption.Off;
            data.requiresDepthOption   = CameraOverrideOption.Off;
        }

        void EnsureCameraPool()
        {
            int want = SubCameraCount;

            while (_cameras.Count < want)
            {
                var go = new GameObject($"SplitScreen_Cam_{_cameras.Count}");
                go.transform.SetParent(transform, false);

                var cam = go.AddComponent<Camera>();
                // Deliberately no CinemachineBrain: only the main camera is driven
                // by the floor / close-up virtual cameras.
                _cameras.Add(cam);
            }

            for (int i = 0; i < _cameras.Count; i++)
            {
                var cam = _cameras[i];
                if (cam == null) continue;

                bool active = i < want;
                cam.gameObject.SetActive(active);
                if (!active) continue;

                cam.clearFlags      = _mainCamera != null ? _mainCamera.clearFlags : CameraClearFlags.Skybox;
                cam.backgroundColor = _mainCamera != null ? _mainCamera.backgroundColor : Color.black;
                cam.cullingMask     = _mainCamera != null ? _mainCamera.cullingMask : ~0;
                cam.fieldOfView     = _subCameraFov;
                cam.nearClipPlane   = 0.05f;
                cam.farClipPlane    = _subCameraFarClip;
                cam.depth           = i;

                var data = cam.GetComponent<UniversalAdditionalCameraData>()
                        ?? cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                data.renderType           = CameraRenderType.Base;
                data.renderPostProcessing = _subCameraPostProcessing;
                data.renderShadows        = _subCameraShadows;
            }
        }

        void ApplyViewports()
        {
            if (_cells.Count == 0) return;

            // Pixel thickness -> normalized, per axis, so the gap looks square.
            float bx = Screen.width  > 0 ? _borderThickness / Screen.width  : 0f;
            float by = Screen.height > 0 ? _borderThickness / Screen.height : 0f;

            int largest = 0;
            float bestArea = -1f;
            for (int i = 0; i < _cells.Count; i++)
            {
                float a = _cells[i].width * _cells[i].height;
                if (a > bestArea) { bestArea = a; largest = i; }
            }

            int sub = 0;
            for (int i = 0; i < _cells.Count; i++)
            {
                Rect r = MosaicLayout.Inset(_cells[i], bx, by);

                if (UseMainCamera && i == largest)
                {
                    _mainCamera.rect = r;
                    continue;
                }

                if (sub < _cameras.Count && _cameras[sub] != null)
                    _cameras[sub].rect = r;
                sub++;
            }

            if (_backgroundCamera != null)
            {
                _backgroundCamera.backgroundColor = _borderColor;
                _backgroundCamera.rect = new Rect(0f, 0f, 1f, 1f);
            }
        }

        // ── placement ────────────────────────────────────────────────────────

        void PlaceCameras()
        {
            if (_poi == null || !_poi.HasPoint || _mainCamera == null) return;

            Vector3 poi = _poi.Point;
            Vector3 toPoi = poi - _mainCamera.transform.position;
            float   dist  = toPoi.magnitude;
            if (dist < 0.001f) return;

            Vector3 forward = toPoi / dist;          // main camera -> POI
            Vector3 axis    = -forward;              // POI -> main camera: dome centre

            // Side reference for the true 180-degree constraint.
            Vector3 up    = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            Vector3 right = Vector3.Normalize(Vector3.Cross(up, axis));

            var rng = new System.Random(_placementSeed);
            float cosMax = Mathf.Cos(_maxAngle * Mathf.Deg2Rad);

            float rMin = Mathf.Min(_minRadiusFraction, _maxRadiusFraction) * dist;
            float rMax = Mathf.Max(_minRadiusFraction, _maxRadiusFraction) * dist;

            for (int i = 0; i < _cameras.Count; i++)
            {
                var cam = _cameras[i];
                if (cam == null || !cam.gameObject.activeSelf) continue;

                // Even sampling over the spherical cap around `axis`. Rejection
                // sampling would bias toward the pole and waste draws.
                float z   = Mathf.Lerp(cosMax, 1f, (float)rng.NextDouble());
                float phi = (float)(rng.NextDouble() * System.Math.PI * 2.0);
                float s   = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));

                Vector3 local = new Vector3(s * Mathf.Cos(phi), s * Mathf.Sin(phi), z);
                Vector3 dir   = Quaternion.FromToRotation(Vector3.forward, axis) * local;

                // Keep every camera on one side of the axis line so screen
                // direction is consistent across cells.
                if (_enforceAxisSide && Vector3.Dot(dir, right) < 0f)
                    dir = Vector3.Reflect(dir, right);

                float radius = Mathf.Lerp(rMin, rMax, (float)rng.NextDouble());
                Vector3 pos  = poi + dir * radius;

                if (pos.y < _minWorldY) pos.y = _minWorldY;

                cam.transform.position = pos;
                cam.transform.LookAt(poi);
            }

            _lastPlacementPoint = poi;
            _placed = true;
        }

        void Teardown()
        {
            if (_mainCamera != null && _mainRectCaptured)
                _mainCamera.rect = _mainCameraOriginalRect;

            for (int i = 0; i < _cameras.Count; i++)
                if (_cameras[i] != null) DestroyCam(_cameras[i].gameObject);
            _cameras.Clear();

            if (_backgroundCamera != null) DestroyCam(_backgroundCamera.gameObject);
            _backgroundCamera = null;
            _placed = false;
        }

        static void DestroyCam(GameObject go)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        void OnValidate()
        {
            if (_minRadiusFraction > _maxRadiusFraction)
                _minRadiusFraction = _maxRadiusFraction;

            if (!Application.isPlaying) return;
            ApplyViewports();
            if (_backgroundCamera != null) _backgroundCamera.backgroundColor = _borderColor;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            var poi = GetComponent<PointOfInterestFinder>();
            var main = _mainCamera != null ? _mainCamera : Camera.main;
            if (poi == null || !poi.HasPoint || main == null) return;

            Vector3 p = poi.Point;
            Vector3 toPoi = p - main.transform.position;
            float dist = toPoi.magnitude;
            if (dist < 0.001f) return;

            Vector3 axis = -toPoi / dist;

            // The master-shot axis.
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(main.transform.position, p);

            // The perpendicular plane at the POI — nothing may sit beyond it.
            Vector3 up    = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            Vector3 right = Vector3.Normalize(Vector3.Cross(up, axis));
            Vector3 planeUp = Vector3.Cross(axis, right);
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.5f);
            float e = dist * 0.5f;
            Vector3 a = p + right * e + planeUp * e;
            Vector3 b = p - right * e + planeUp * e;
            Vector3 c = p - right * e - planeUp * e;
            Vector3 d = p + right * e - planeUp * e;
            Gizmos.DrawLine(a, b); Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, d); Gizmos.DrawLine(d, a);

            // Radius shells the cameras are sampled between.
            Gizmos.color = new Color(0.3f, 1f, 0.6f, 0.35f);
            Gizmos.DrawWireSphere(p, _minRadiusFraction * dist);
            Gizmos.DrawWireSphere(p, _maxRadiusFraction * dist);

            Gizmos.color = Color.green;
            for (int i = 0; i < _cameras.Count; i++)
            {
                if (_cameras[i] == null || !_cameras[i].gameObject.activeSelf) continue;
                Gizmos.DrawLine(_cameras[i].transform.position, p);
                Gizmos.DrawWireSphere(_cameras[i].transform.position, 0.2f);
            }
        }
#endif
    }
}
