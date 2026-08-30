using System.Collections.Generic;
using UnityEngine;

namespace Pincushioned.SplitScreen
{
    /// <summary>Which cell gets chosen each time the quadtree subdivides.</summary>
    public enum QuadtreeSplitStrategy
    {
        /// <summary>Chance of being picked scales with cell area. Big cells split
        /// first, so the result stays legible instead of degenerating into a few
        /// micro-cells. The best-looking default.</summary>
        AreaWeighted = 0,

        /// <summary>Every cell equally likely. More extreme size contrast, and it
        /// will happily shatter an already-tiny cell.</summary>
        Uniform = 1,

        /// <summary>Always split the largest cell. Nearly regular — closest to a
        /// plain grid.</summary>
        LargestFirst = 2,
    }

    /// <summary>
    /// Pure layout math: turns a subdivision count into normalized viewport cells.
    ///
    /// Each subdivision replaces one cell with its four quadrants, so the cell
    /// count is <c>1 + 3 * subdivisions</c> (2 → 7, 9 → 28). Rects are in Unity
    /// viewport space: origin bottom-left, (0,0)–(1,1).
    ///
    /// No Unity object model, no side effects — deterministic for a given
    /// (subdivisions, strategy, seed), which is what makes a layout you like
    /// reproducible rather than a lucky run.
    /// </summary>
    public static class QuadtreeLayout
    {
        public const int MinSubdivisions = 2;
        public const int MaxSubdivisions = 9;

        /// <summary>Cell count produced by a given subdivision count.</summary>
        public static int CellCount(int subdivisions) => 1 + 3 * Mathf.Max(0, subdivisions);

        /// <summary>
        /// Build the cell list into <paramref name="results"/> (cleared first, so
        /// the caller's list can be reused frame to frame without allocating).
        /// </summary>
        public static void Build(
            int subdivisions,
            QuadtreeSplitStrategy strategy,
            int seed,
            List<Rect> results)
        {
            if (results == null) return;
            results.Clear();

            subdivisions = Mathf.Clamp(subdivisions, 0, MaxSubdivisions);

            results.Add(new Rect(0f, 0f, 1f, 1f));

            var rng = new System.Random(seed);

            for (int i = 0; i < subdivisions; i++)
            {
                int index = PickCell(results, strategy, rng);
                Rect cell = results[index];

                // Replace the chosen cell in place, then append the other three,
                // so ordering stays stable enough that camera N keeps roughly the
                // same screen region as the count changes.
                float w = cell.width  * 0.5f;
                float h = cell.height * 0.5f;

                results[index] = new Rect(cell.x,     cell.y,     w, h); // bottom-left
                results.Add(new Rect(cell.x + w, cell.y,     w, h));     // bottom-right
                results.Add(new Rect(cell.x,     cell.y + h, w, h));     // top-left
                results.Add(new Rect(cell.x + w, cell.y + h, w, h));     // top-right
            }
        }

        static int PickCell(List<Rect> cells, QuadtreeSplitStrategy strategy, System.Random rng)
        {
            switch (strategy)
            {
                case QuadtreeSplitStrategy.Uniform:
                    return rng.Next(cells.Count);

                case QuadtreeSplitStrategy.LargestFirst:
                {
                    int best = 0;
                    float bestArea = -1f;
                    for (int i = 0; i < cells.Count; i++)
                    {
                        float a = cells[i].width * cells[i].height;
                        if (a > bestArea) { bestArea = a; best = i; }
                    }
                    return best;
                }

                default: // AreaWeighted
                {
                    float total = 0f;
                    for (int i = 0; i < cells.Count; i++)
                        total += cells[i].width * cells[i].height;

                    // Areas always sum to 1 for a full subdivision of the unit
                    // square, but guard anyway rather than trusting float drift.
                    if (total <= 0f) return rng.Next(cells.Count);

                    double r = rng.NextDouble() * total;
                    for (int i = 0; i < cells.Count; i++)
                    {
                        r -= cells[i].width * cells[i].height;
                        if (r <= 0d) return i;
                    }
                    return cells.Count - 1;
                }
            }
        }

        /// <summary>
        /// Shrink a cell by <paramref name="border"/> viewport units on every side,
        /// producing the gap that the border colour shows through.
        ///
        /// <paramref name="border"/> is applied per axis by the caller (a pixel
        /// thickness is not square in normalized space on a non-square screen), and
        /// the result is clamped so a thick border on a small cell collapses to a
        /// zero-size rect rather than inverting.
        /// </summary>
        public static Rect Inset(Rect cell, float borderX, float borderY)
        {
            float w = Mathf.Max(0f, cell.width  - borderX * 2f);
            float h = Mathf.Max(0f, cell.height - borderY * 2f);
            float x = cell.x + (cell.width  - w) * 0.5f;
            float y = cell.y + (cell.height - h) * 0.5f;
            return new Rect(x, y, w, h);
        }
    }
}
