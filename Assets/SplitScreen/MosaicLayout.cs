using System.Collections.Generic;
using UnityEngine;

namespace Pincushioned.SplitScreen
{
    /// <summary>How the screen is carved up.</summary>
    public enum LayoutMode
    {
        /// <summary>Guillotine partition: repeatedly cut an existing rectangle at a
        /// random ratio. Every cell is a different size and they tile exactly, with
        /// no gaps or overlaps. Sizes are not restricted to halves and quarters.</summary>
        RandomRectangles = 0,

        /// <summary>Strict quadtree: each step replaces one cell with its four
        /// quadrants, so every edge lands on a half or a quarter.</summary>
        Quadtree = 1,
    }

    /// <summary>
    /// Divides the viewport into rectangles of varied size that tile it exactly.
    ///
    /// The default mode is a <b>guillotine partition</b>: pick an existing
    /// rectangle, cut it once along a chosen axis at a random position, repeat.
    /// N cuts always produce exactly N+1 cells, so cell count is directly
    /// controllable rather than jumping in steps of three.
    ///
    /// Two things keep the result from degenerating into slivers:
    ///
    /// <list type="bullet">
    /// <item><b>Split ratio is clamped</b> away from the edges, so a cut never
    /// shaves a hairline off one side.</item>
    /// <item><b>Cuts prefer the longer axis</b>, measured in <i>screen</i> space
    /// rather than normalized space. A cell that is 0.5 x 0.5 in viewport units is
    /// not square on a 16:9 display — it is 16:9 — so the aspect has to be folded
    /// in or every cell drifts wide.</item>
    /// </list>
    ///
    /// Deterministic for a given (cellCount, seed, ratio, bias): the same inputs
    /// always rebuild the same mosaic, so a composition you like is reproducible.
    /// </summary>
    public static class MosaicLayout
    {
        public const int MinCells = 2;
        public const int MaxCells = 32;

        /// <summary>
        /// Build a partition of the unit rect into <paramref name="cellCount"/> cells.
        /// </summary>
        /// <param name="cellCount">Number of rectangles. Clamped to [2, 32].</param>
        /// <param name="mode">Guillotine partition or strict quadtree.</param>
        /// <param name="seed">Deterministic seed.</param>
        /// <param name="minSplitRatio">
        /// Closest a cut may land to a cell's edge, as a fraction of that cell.
        /// 0.5 always cuts dead centre; 0.15 allows strongly uneven cuts.
        /// </param>
        /// <param name="squarenessBias">
        /// 0 = axis chosen at random. 1 = always cut across the longer screen-space
        /// axis, which keeps cells closest to square.
        /// </param>
        /// <param name="aspect">Display aspect (width / height), e.g. 16f/9f.</param>
        /// <param name="results">Output list; cleared first so it can be reused.</param>
        public static void Build(
            int cellCount,
            LayoutMode mode,
            int seed,
            float minSplitRatio,
            float squarenessBias,
            float aspect,
            List<Rect> results)
        {
            if (results == null) return;

            if (mode == LayoutMode.Quadtree)
            {
                // Quadtree cell counts are 1 + 3N, so map the requested count to the
                // nearest achievable number of subdivisions.
                int subdivisions = Mathf.Clamp(Mathf.RoundToInt((cellCount - 1) / 3f),
                                               0, QuadtreeLayout.MaxSubdivisions);
                QuadtreeLayout.Build(subdivisions, QuadtreeSplitStrategy.AreaWeighted, seed, results);
                return;
            }

            results.Clear();
            results.Add(new Rect(0f, 0f, 1f, 1f));

            cellCount      = Mathf.Clamp(cellCount, MinCells, MaxCells);
            minSplitRatio  = Mathf.Clamp(minSplitRatio, 0.05f, 0.5f);
            squarenessBias = Mathf.Clamp01(squarenessBias);
            if (aspect <= 0f) aspect = 16f / 9f;

            var rng = new System.Random(seed);

            while (results.Count < cellCount)
            {
                int index = PickCell(results, rng);
                Rect cell = results[index];

                // Screen-space extents: a viewport-square cell is not visually square.
                float screenW = cell.width * aspect;
                float screenH = cell.height;

                bool cutVertically;   // a vertical cut splits width into left|right
                if ((float)rng.NextDouble() < squarenessBias)
                    cutVertically = screenW >= screenH;   // cut across the longer side
                else
                    cutVertically = rng.Next(2) == 0;

                float t = Mathf.Lerp(minSplitRatio, 1f - minSplitRatio, (float)rng.NextDouble());

                if (cutVertically)
                {
                    float w = cell.width * t;
                    results[index] = new Rect(cell.x, cell.y, w, cell.height);
                    results.Add(new Rect(cell.x + w, cell.y, cell.width - w, cell.height));
                }
                else
                {
                    float h = cell.height * t;
                    results[index] = new Rect(cell.x, cell.y, cell.width, h);
                    results.Add(new Rect(cell.x, cell.y + h, cell.width, cell.height - h));
                }
            }
        }

        /// <summary>
        /// Choose which cell to cut next, weighted by area so large rectangles are
        /// broken up before small ones. Without this the partition keeps slicing an
        /// already-tiny cell and the result reads as noise rather than composition.
        /// </summary>
        static int PickCell(List<Rect> cells, System.Random rng)
        {
            float total = 0f;
            for (int i = 0; i < cells.Count; i++)
                total += cells[i].width * cells[i].height;

            if (total <= 0f) return rng.Next(cells.Count);

            double r = rng.NextDouble() * total;
            for (int i = 0; i < cells.Count; i++)
            {
                r -= cells[i].width * cells[i].height;
                if (r <= 0d) return i;
            }
            return cells.Count - 1;
        }

        /// <summary>
        /// Shrink a cell by the given normalized border on each side, producing the
        /// gap the border colour shows through. Clamped so a thick border on a small
        /// cell collapses to zero rather than inverting.
        /// </summary>
        public static Rect Inset(Rect cell, float borderX, float borderY)
            => QuadtreeLayout.Inset(cell, borderX, borderY);
    }
}
