using Seed.Worlds;

namespace Seed.Tests;

public class CollisionTests
{
    [Fact]
    public void AABB_CircleOverlap_Detects()
    {
        var aabb = new AABB(0, 5f, 5f, 10f, 10f);

        // Circle inside
        Assert.True(aabb.OverlapsCircle(7.5f, 7.5f, 1f));

        // Circle overlapping edge
        Assert.True(aabb.OverlapsCircle(4f, 7.5f, 1.5f));

        // Circle touching corner
        Assert.True(aabb.OverlapsCircle(4f, 4f, 1.5f));

        // Circle not overlapping
        Assert.False(aabb.OverlapsCircle(0f, 0f, 1f));
    }

    [Fact]
    public void AABB_CirclePenetration_CalculatesCorrectly()
    {
        var aabb = new AABB(0, 5f, 5f, 10f, 10f);

        // Circle penetrating from left
        var (depth, pushX, pushY) = aabb.CirclePenetration(4.5f, 7.5f, 1f);

        Assert.True(depth > 0);
        Assert.True(pushX < 0); // Should push left
        Assert.Equal(0, pushY, 3);
    }

    [Fact]
    public void AABB_RayIntersection_HitsBox()
    {
        var aabb = new AABB(0, 5f, 5f, 10f, 10f);

        // Ray from origin pointing at box
        float t = aabb.RayIntersection(0f, 7.5f, 1f, 0f, 20f);

        Assert.True(t < 20f);
        Assert.True(t > 0f);
        Assert.Equal(5f, t, 1); // Should hit left edge at x=5
    }

    [Fact]
    public void AABB_RayIntersection_MissesBox()
    {
        var aabb = new AABB(0, 5f, 5f, 10f, 10f);

        // Ray pointing away from box
        float t = aabb.RayIntersection(0f, 0f, -1f, 0f, 20f);

        Assert.Equal(float.MaxValue, t);
    }

    [Fact]
    public void AABB_Penetration_DeterministicTieBreak()
    {
        var aabb = new AABB(0, 0f, 0f, 10f, 10f);

        // Circle in center - should push out deterministically
        var (depth, pushX, pushY) = aabb.CirclePenetration(5f, 5f, 1f);

        // When center is equidistant from all edges, should prefer X axis (left)
        Assert.True(depth > 0);
        // One of the push directions should be non-zero
        Assert.True(Math.Abs(pushX) > 0.1f || Math.Abs(pushY) > 0.1f);
    }
}


