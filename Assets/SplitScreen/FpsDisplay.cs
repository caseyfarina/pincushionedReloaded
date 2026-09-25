using UnityEngine;

namespace Pincushioned.Diagnostics
{
    /// <summary>
    /// An on-screen frame rate readout that works in a player build.
    ///
    /// ProfilerProbe already attributes frame cost, but it reports to the log,
    /// which you cannot read while the thing is running in front of you. This is
    /// the number you watch live.
    ///
    /// Drawn with OnGUI deliberately: it needs no Canvas, no TextMeshPro asset
    /// and no scene setup, so it can be dropped onto any object in any scene and
    /// survive into a build. OnGUI costs a fraction of a millisecond, which is
    /// worth it for a readout you only enable when measuring.
    ///
    /// Editor frame times are not build frame times on this project - the
    /// measured inflation is 5.2x - so this exists mainly to be read in a build.
    /// </summary>
    public class FpsDisplay : MonoBehaviour
    {
        [Tooltip("Seconds of history the average and the worst frame are taken over.")]
        [Min(0.1f)] public float window = 0.5f;

        [Tooltip("Corner to draw in.")]
        public TextAnchor corner = TextAnchor.UpperLeft;

        [Tooltip("Text size in points.")]
        [Min(8)] public int fontSize = 22;

        [Tooltip("Toggles the readout at runtime. None to leave it always on.")]
        public KeyCode toggleKey = KeyCode.F1;

        [Tooltip("Show the worst frame in the window as well as the average. The spike is usually what matters, not the mean.")]
        public bool showWorst = true;

        [Tooltip("Visible at startup.")]
        public bool visible = true;

        private float _elapsed;
        private int _frames;
        private float _worstMs;

        private float _fps;
        private float _avgMs;
        private float _reportedWorstMs;

        private GUIStyle _style;

        private void Update()
        {
            if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey)) visible = !visible;

            // Unscaled, so pausing or slow-motion does not flatter the number.
            float dt = Time.unscaledDeltaTime;

            _elapsed += dt;
            _frames++;
            _worstMs = Mathf.Max(_worstMs, dt * 1000f);

            if (_elapsed < window) return;

            _fps = _frames / _elapsed;
            _avgMs = (_elapsed / _frames) * 1000f;
            _reportedWorstMs = _worstMs;

            _elapsed = 0f;
            _frames = 0;
            _worstMs = 0f;
        }

        private void OnGUI()
        {
            if (!visible || _frames < 0) return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.UpperLeft,
                    fontStyle = FontStyle.Bold,
                };
            }
            _style.fontSize = fontSize;

            string text = showWorst
                ? $"{_fps:F0} fps   {_avgMs:F2} ms   worst {_reportedWorstMs:F2} ms"
                : $"{_fps:F0} fps   {_avgMs:F2} ms";

            var size = _style.CalcSize(new GUIContent(text));
            float pad = fontSize * 0.5f;

            float x = corner == TextAnchor.UpperRight || corner == TextAnchor.LowerRight
                ? Screen.width - size.x - pad
                : pad;

            float y = corner == TextAnchor.LowerLeft || corner == TextAnchor.LowerRight
                ? Screen.height - size.y - pad
                : pad;

            var rect = new Rect(x, y, size.x, size.y);

            // A dark plate behind it, or the text is unreadable against a pale
            // floor - which is exactly the shot you want to measure.
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(rect.x - pad * 0.5f, rect.y - pad * 0.25f,
                                     rect.width + pad, rect.height + pad * 0.5f), Texture2D.whiteTexture);

            GUI.color = Color.white;
            GUI.Label(rect, text, _style);
            GUI.color = prev;
        }
    }
}
