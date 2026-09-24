using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class CableRibbonBuilderTests
{
    private static Vector3[] Line(int n, float spacing = 1f)
    {
        var a = new Vector3[n];
        for (int i = 0; i < n; i++) a[i] = new Vector3(i * spacing, 0f, 0f);
        return a;
    }

    private class Sink
    {
        public readonly List<Vector3> pos = new List<Vector3>();
        public readonly List<Vector3> tan = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<Color> col = new List<Color>();
        public readonly List<Vector4> emit = new List<Vector4>();
        public readonly List<int> idx = new List<int>();

        public void Append(Vector3[] nodes, int count, Color c, float tiling) =>
            CableRibbonBuilder.Append(nodes, count, c, Color.black, tiling, pos, tan, uv, col, emit, idx);

        public void Append(Vector3[] nodes, int count, Color c, Color emission, float tiling) =>
            CableRibbonBuilder.Append(nodes, count, c, emission, tiling, pos, tan, uv, col, emit, idx);
    }

    [Test]
    public void Counts_MatchTheDeclaredFormulas()
    {
        Assert.AreEqual(48, CableRibbonBuilder.VertexCount(24));
        Assert.AreEqual(138, CableRibbonBuilder.IndexCount(24));
        Assert.AreEqual(0, CableRibbonBuilder.IndexCount(1), "a single node forms no segment");
    }

    [Test]
    public void Append_EmitsTwoVerticesPerNodeAndSixIndicesPerSegment()
    {
        var s = new Sink();
        s.Append(Line(10), 10, Color.red, 1f);

        Assert.AreEqual(20, s.pos.Count);
        Assert.AreEqual(20, s.tan.Count);
        Assert.AreEqual(20, s.uv.Count);
        Assert.AreEqual(20, s.col.Count);
        Assert.AreEqual(54, s.idx.Count);
    }

    [Test]
    public void Append_LeavesPositionsUnoffset()
    {
        // Widening happens in the vertex shader so one mesh serves all 32
        // split-screen cameras. If the CPU offsets here, the ribbon is correct
        // for one camera and a sliver in every other cell.
        var s = new Sink();
        var nodes = Line(4);
        s.Append(nodes, 4, Color.white, 1f);

        for (int i = 0; i < 4; i++)
        {
            Assert.AreEqual(nodes[i], s.pos[i * 2]);
            Assert.AreEqual(nodes[i], s.pos[i * 2 + 1]);
        }
    }

    [Test]
    public void Append_PairsEachNodeAsSideZeroAndSideOne()
    {
        var s = new Sink();
        s.Append(Line(6), 6, Color.white, 1f);

        for (int i = 0; i < 6; i++)
        {
            Assert.AreEqual(0f, s.uv[i * 2].y, 1e-5f, $"node {i} low side");
            Assert.AreEqual(1f, s.uv[i * 2 + 1].y, 1e-5f, $"node {i} high side");
        }
    }

    [Test]
    public void Append_ScalesUByWorldLengthNotByT()
    {
        // Otherwise a short patch cable and a long run get identical repeat
        // counts, and the texel-density mismatch is the most obvious tell that
        // the cable is a stretched ribbon.
        var shortSink = new Sink();
        shortSink.Append(Line(5, 1f), 5, Color.white, 1f);

        var longSink = new Sink();
        longSink.Append(Line(5, 4f), 5, Color.white, 1f);

        float shortU = shortSink.uv[shortSink.uv.Count - 1].x;
        float longU = longSink.uv[longSink.uv.Count - 1].x;

        Assert.AreEqual(4f, shortU, 1e-4f);
        Assert.AreEqual(16f, longU, 1e-4f);
        Assert.AreEqual(4f, longU / shortU, 1e-3f, "u must be proportional to world length");
    }

    [Test]
    public void Append_UIsMonotonicAndStartsAtZero()
    {
        var s = new Sink();
        s.Append(Line(8, 1.7f), 8, Color.white, 2f);

        Assert.AreEqual(0f, s.uv[0].x, 1e-5f);
        for (int i = 1; i < 8; i++)
            Assert.Greater(s.uv[i * 2].x, s.uv[(i - 1) * 2].x, $"u went backwards at node {i}");
    }

    [Test]
    public void Append_GivesEveryVertexTheCableColor()
    {
        var s = new Sink();
        var c = new Color(0.2f, 0.4f, 0.6f, 1f);
        s.Append(Line(5), 5, c, 1f);
        foreach (var v in s.col) Assert.AreEqual(c, v);
    }

    [Test]
    public void Append_TangentsPointAlongTheCableAndAreUnitLength()
    {
        var s = new Sink();
        s.Append(Line(6), 6, Color.white, 1f);
        foreach (var t in s.tan)
        {
            Assert.AreEqual(1f, t.magnitude, 1e-3f);
            Assert.Less(Vector3.Distance(t, Vector3.right), 1e-3f, "a straight cable along +x should tangent along +x");
        }
    }

    [Test]
    public void Append_SurvivesDuplicateNodesWithoutNaNTangents()
    {
        // Two coincident nodes give a zero-length difference. Unguarded, that
        // normalises to NaN and silently destroys the whole mesh - Unity drops
        // a mesh containing any NaN vertex rather than erroring.
        var nodes = new[] { Vector3.zero, Vector3.zero, Vector3.zero, new Vector3(1f, 0f, 0f) };
        var s = new Sink();
        s.Append(nodes, 4, Color.white, 1f);

        foreach (var t in s.tan)
            Assert.IsFalse(float.IsNaN(t.x) || float.IsNaN(t.y) || float.IsNaN(t.z), "NaN tangent");
        foreach (var u in s.uv)
            Assert.IsFalse(float.IsNaN(u.x), "NaN uv");
    }

    [Test]
    public void Append_IndicesStayInRangeAcrossMultipleCables()
    {
        // The second cable's indices must be offset by the first cable's vertex
        // count, or every cable after the first draws on top of cable zero.
        var s = new Sink();
        s.Append(Line(5), 5, Color.red, 1f);
        s.Append(Line(5), 5, Color.blue, 1f);

        Assert.AreEqual(20, s.pos.Count);
        foreach (int i in s.idx)
        {
            Assert.GreaterOrEqual(i, 0);
            Assert.Less(i, s.pos.Count);
        }

        int maxFirst = 0;
        for (int i = 0; i < 24; i++) maxFirst = Mathf.Max(maxFirst, s.idx[i]);
        Assert.Less(maxFirst, 10, "the first cable's triangles reach into the second cable's vertices");

        int minSecond = int.MaxValue;
        for (int i = 24; i < s.idx.Count; i++) minSecond = Mathf.Min(minSecond, s.idx[i]);
        Assert.GreaterOrEqual(minSecond, 10, "the second cable's triangles were not offset");
    }

    [Test]
    public void Append_EachSegmentsTrianglesShareItsDiagonal()
    {
        var s = new Sink();
        s.Append(Line(3), 3, Color.white, 1f);

        // Segment 0 spans vertices 0,1 (node 0) and 2,3 (node 1).
        var t1 = new[] { s.idx[0], s.idx[1], s.idx[2] };
        var t2 = new[] { s.idx[3], s.idx[4], s.idx[5] };

        int shared = 0;
        foreach (int a in t1) foreach (int b in t2) if (a == b) shared++;
        Assert.AreEqual(2, shared, "a quad's two triangles must share exactly one edge");
    }

    [Test]
    public void Append_IgnoresDegenerateNodeCounts()
    {
        var s = new Sink();
        s.Append(Line(4), 1, Color.white, 1f);
        s.Append(Line(4), 0, Color.white, 1f);
        Assert.AreEqual(0, s.pos.Count, "fewer than two nodes cannot form a ribbon");
        Assert.AreEqual(0, s.idx.Count);
    }

    [Test]
    public void Append_GivesEveryVertexTheCablesFlashColour()
    {
        // Flash lives in its own channel because vertex colour alpha is already
        // the per-cable width multiplier.
        var s = new Sink();
        var flash = new Color(2f, 0.5f, 0.25f, 1f);
        s.Append(Line(5), 5, Color.white, flash, 1f);

        Assert.AreEqual(10, s.emit.Count, "emission must stay in lockstep with the vertices");
        foreach (var e in s.emit)
        {
            Assert.AreEqual(flash.r, e.x, 1e-5f);
            Assert.AreEqual(flash.g, e.y, 1e-5f);
            Assert.AreEqual(flash.b, e.z, 1e-5f);
        }
    }

    [Test]
    public void Append_KeepsEachCablesFlashSeparate()
    {
        var s = new Sink();
        s.Append(Line(4), 4, Color.white, new Color(1f, 0f, 0f), 1f);
        s.Append(Line(4), 4, Color.white, new Color(0f, 0f, 1f), 1f);

        Assert.AreEqual(16, s.emit.Count);
        for (int i = 0; i < 8; i++) Assert.AreEqual(1f, s.emit[i].x, 1e-5f, $"first cable vertex {i}");
        for (int i = 8; i < 16; i++) Assert.AreEqual(1f, s.emit[i].z, 1e-5f, $"second cable vertex {i}");
    }

    [Test]
    public void Append_EmitsNoEmissionForADegenerateCable()
    {
        var s = new Sink();
        s.Append(Line(4), 1, Color.white, Color.red, 1f);
        Assert.AreEqual(0, s.emit.Count);
    }
}
