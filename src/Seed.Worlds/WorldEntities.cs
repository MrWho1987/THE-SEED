namespace Seed.Worlds;

/// <summary>
/// Axis-aligned bounding box for obstacles and hazards.
/// </summary>
public readonly record struct AABB(
    int Id,
    float MinX,
    float MinY,
    float MaxX,
    float MaxY
)
{
    public float Width => MaxX - MinX;
    public float Height => MaxY - MinY;
    public float CenterX => (MinX + MaxX) * 0.5f;
    public float CenterY => (MinY + MaxY) * 0.5f;

    /// <summary>
    /// Check if a circle overlaps this AABB.
    /// </summary>
    public bool OverlapsCircle(float cx, float cy, float radius)
    {
        // Find closest point on AABB to circle center
        float closestX = Math.Clamp(cx, MinX, MaxX);
        float closestY = Math.Clamp(cy, MinY, MaxY);

        float dx = cx - closestX;
        float dy = cy - closestY;

        return (dx * dx + dy * dy) <= (radius * radius);
    }

    /// <summary>
    /// Get the penetration depth and push-out direction for a circle.
    /// Returns (0,0,0) if no overlap.
    /// </summary>
    public (float depth, float pushX, float pushY) CirclePenetration(float cx, float cy, float radius)
    {
        // Find closest point on AABB to circle center
        float closestX = Math.Clamp(cx, MinX, MaxX);
        float closestY = Math.Clamp(cy, MinY, MaxY);

        float dx = cx - closestX;
        float dy = cy - closestY;
        float distSq = dx * dx + dy * dy;

        if (distSq >= radius * radius)
            return (0, 0, 0); // No overlap

        if (distSq < 1e-10f)
        {
            // Circle center is inside AABB - push out along minimum axis
            float overlapLeft = cx - MinX + radius;
            float overlapRight = MaxX - cx + radius;
            float overlapBottom = cy - MinY + radius;
            float overlapTop = MaxY - cy + radius;

            float minOverlap = Math.Min(Math.Min(overlapLeft, overlapRight), 
                                       Math.Min(overlapBottom, overlapTop));

            // Stable tie-break: prefer X axis, then negative direction
            if (minOverlap == overlapLeft)
                return (overlapLeft, -1, 0);
            if (minOverlap == overlapRight)
                return (overlapRight, 1, 0);
            if (minOverlap == overlapBottom)
                return (overlapBottom, 0, -1);
            return (overlapTop, 0, 1);
        }

        float dist = MathF.Sqrt(distSq);
        float penetration = radius - dist;
        float normX = dx / dist;
        float normY = dy / dist;

        return (penetration, normX, normY);
    }

    /// <summary>
    /// Ray-AABB intersection. Returns distance to intersection or float.MaxValue if no hit.
    /// </summary>
    public float RayIntersection(float originX, float originY, float dirX, float dirY, float maxDist)
    {
        float tMin = 0f;
        float tMax = maxDist;

        // X slab
        if (MathF.Abs(dirX) < 1e-8f)
        {
            if (originX < MinX || originX > MaxX)
                return float.MaxValue;
        }
        else
        {
            float invD = 1f / dirX;
            float t1 = (MinX - originX) * invD;
            float t2 = (MaxX - originX) * invD;

            if (t1 > t2) (t1, t2) = (t2, t1);

            tMin = Math.Max(tMin, t1);
            tMax = Math.Min(tMax, t2);

            if (tMin > tMax)
                return float.MaxValue;
        }

        // Y slab
        if (MathF.Abs(dirY) < 1e-8f)
        {
            if (originY < MinY || originY > MaxY)
                return float.MaxValue;
        }
        else
        {
            float invD = 1f / dirY;
            float t1 = (MinY - originY) * invD;
            float t2 = (MaxY - originY) * invD;

            if (t1 > t2) (t1, t2) = (t2, t1);

            tMin = Math.Max(tMin, t1);
            tMax = Math.Min(tMax, t2);

            if (tMin > tMax)
                return float.MaxValue;
        }

        return tMin;
    }

    /// <summary>
    /// Check if a point is inside the AABB (with optional margin).
    /// </summary>
    public bool Contains(float x, float y, float margin = 0f)
    {
        return x >= MinX - margin && x <= MaxX + margin &&
               y >= MinY - margin && y <= MaxY + margin;
    }
}

/// <summary>
/// Food item in the world.
/// </summary>
public record struct FoodItem(
    int Id,
    float X,
    float Y,
    float Radius,
    float EnergyValue,
    bool Consumed,
    int RespawnTick = -1,
    bool IsCorpse = false
);

/// <summary>
/// Entity type for raycast results.
/// </summary>
public enum EntityType
{
    None = 0,
    Wall = 1,
    Obstacle = 2,
    Hazard = 3,
    Food = 4,
    Agent = 5
}

