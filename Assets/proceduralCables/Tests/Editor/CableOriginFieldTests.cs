using NUnit.Framework;
using UnityEngine;

public class CableOriginFieldTests
{
    private const float Eps = 1e-4f;

    private static Vector3 At(int id, CableOriginShape shape, float radius = 4f)
    {
        var p = CableParameters.Default;
        p.originShape = shape;
        p.originColumns = 5;
        p.originRows = 3;
        p.originColumnSpacing = 2f;
        p.originRowSpacing = 1.5f;
        p.originRadius = radius;
        return CableOriginField.OriginLocal(id, p.seed, p);
    }

    [Test]
    public void Point_PutsEveryCableOnTheEmitter()
    {
        // The default, so adding origin shapes cannot move an existing rig.
        for (int id = 0; id < 50; id++)
            Assert.Less(At(id, CableOriginShape.Point).magnitude, Eps, $"id {id}");
    }

    [Test]
    public void IsDeterministicForTheSameId()
    {
        foreach (var shape in new[] { CableOriginShape.Grid, CableOriginShape.Disc, CableOriginShape.Sphere })
            Assert.AreEqual(At(7, shape), At(7, shape), shape.ToString());
    }

    [Test]
    public void Grid_LandsOnlyOnCellCentres()
    {
        var p = CableParameters.Default;
        p.originColumns = 5; p.originRows = 3;
        p.originColumnSpacing = 2f; p.originRowSpacing = 1.5f;

        for (int id = 0; id < 200; id++)
        {
            var got = At(id, CableOriginShape.Grid);

            bool onACell = false;
            for (int cell = 0; cell < 15; cell++)
                if (Vector3.Distance(got, CablePatchBay.PortLocal(cell, 5, 3, 2f, 1.5f)) < Eps) { onACell = true; break; }

            Assert.IsTrue(onACell, $"id {id} landed between cells at {got}");
        }
    }

    [Test]
    public void Grid_ReachesEveryCell()
    {
        var seen = new System.Collections.Generic.HashSet<int>();
        for (int id = 0; id < 400; id++)
        {
            var got = At(id, CableOriginShape.Grid);
            for (int cell = 0; cell < 15; cell++)
                if (Vector3.Distance(got, CablePatchBay.PortLocal(cell, 5, 3, 2f, 1.5f)) < Eps) { seen.Add(cell); break; }
        }
        Assert.AreEqual(15, seen.Count, "some cells never fired a cable");
    }

    [Test]
    public void Grid_IsCentredOnTheEmitter()
    {
        var sum = Vector3.zero;
        for (int id = 0; id < 600; id++) sum += At(id, CableOriginShape.Grid);
        Assert.Less((sum / 600f).magnitude, 0.35f, "the grid is not centred on its transform");
    }

    [Test]
    public void Disc_StaysInsideItsRadiusAndInPlane()
    {
        for (int id = 0; id < 300; id++)
        {
            var got = At(id, CableOriginShape.Disc, 4f);
            Assert.LessOrEqual(got.magnitude, 4f + Eps, $"id {id} escaped the disc");
            Assert.AreEqual(0f, got.z, Eps, $"id {id} left the disc plane");
        }
    }

    [Test]
    public void Disc_SpreadsByAreaRatherThanBunchingInTheMiddle()
    {
        // Sampling radius uniformly is the classic bug: half the points end up
        // in the inner quarter of the area. Uniform by area puts half of them
        // inside r/sqrt(2).
        int inner = 0, n = 2000;
        for (int id = 0; id < n; id++)
            if (At(id, CableOriginShape.Disc, 4f).magnitude < 4f / Mathf.Sqrt(2f)) inner++;

        float frac = inner / (float)n;
        Assert.Greater(frac, 0.42f, $"clustered at the rim ({frac:F2})");
        Assert.Less(frac, 0.58f, $"clustered in the middle ({frac:F2})");
    }

    [Test]
    public void Sphere_PutsEveryCableOnTheSurface()
    {
        for (int id = 0; id < 300; id++)
            Assert.AreEqual(4f, At(id, CableOriginShape.Sphere, 4f).magnitude, 1e-3f, $"id {id} left the surface");
    }

    [Test]
    public void Sphere_DoesNotBunchAtThePoles()
    {
        // The same correction the split-screen rig needs: sampling the polar
        // angle uniformly crowds the poles. Done right, height is uniform.
        int high = 0, n = 2000;
        for (int id = 0; id < n; id++)
            if (Mathf.Abs(At(id, CableOriginShape.Sphere, 4f).y) > 2f) high++;

        float frac = high / (float)n;
        Assert.Greater(frac, 0.40f, $"crowded around the equator ({frac:F2})");
        Assert.Less(frac, 0.60f, $"crowded at the poles ({frac:F2})");
    }

    [Test]
    public void DifferentCablesLeaveFromDifferentPlaces()
    {
        foreach (var shape in new[] { CableOriginShape.Grid, CableOriginShape.Disc, CableOriginShape.Sphere })
        {
            int same = 0;
            for (int id = 0; id < 100; id++)
                if (Vector3.Distance(At(id, shape), At(id + 1, shape)) < 1e-5f) same++;

            Assert.Less(same, 30, $"{shape} fired most cables from the same point");
        }
    }
}
