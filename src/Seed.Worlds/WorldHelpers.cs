namespace Seed.Worlds;

internal static class WorldHelpers
{
    public static float RayCircleIntersection(
        float ox, float oy, float dx, float dy,
        float cx, float cy, float r, float maxDist)
    {
        float fx = ox - cx;
        float fy = oy - cy;

        float a = dx * dx + dy * dy;
        float b = 2f * (fx * dx + fy * dy);
        float c = fx * fx + fy * fy - r * r;

        float discriminant = b * b - 4f * a * c;
        if (discriminant < 0) return float.MaxValue;

        float sqrtD = MathF.Sqrt(discriminant);
        float t = (-b - sqrtD) / (2f * a);

        if (t > 0 && t < maxDist) return t;

        t = (-b + sqrtD) / (2f * a);
        if (t > 0 && t < maxDist) return t;

        return float.MaxValue;
    }

    public static bool AABBsOverlap(AABB a, AABB b, float margin = 0f)
    {
        return !(a.MaxX + margin < b.MinX || a.MinX - margin > b.MaxX ||
                 a.MaxY + margin < b.MinY || a.MinY - margin > b.MaxY);
    }

    public static float RayWallDistance(
        float ox, float oy, float dx, float dy,
        float worldWidth, float worldHeight, float maxDist)
    {
        float tMin = maxDist;

        if (dx < -1e-8f)
        {
            float t = -ox / dx;
            if (t > 0 && t < tMin) tMin = t;
        }

        if (dx > 1e-8f)
        {
            float t = (worldWidth - ox) / dx;
            if (t > 0 && t < tMin) tMin = t;
        }

        if (dy < -1e-8f)
        {
            float t = -oy / dy;
            if (t > 0 && t < tMin) tMin = t;
        }

        if (dy > 1e-8f)
        {
            float t = (worldHeight - oy) / dy;
            if (t > 0 && t < tMin) tMin = t;
        }

        return tMin;
    }
}
