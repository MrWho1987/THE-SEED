namespace Seed.Worlds;

internal sealed class SpatialGrid
{
    private readonly float _cellSize;
    private readonly int _cols, _rows;
    private int[] _indices;
    private readonly int[] _cellStart;
    private readonly int[] _cellCount;
    private int _agentCapacity;

    public int Cols => _cols;
    public int Rows => _rows;
    public float CellSize => _cellSize;

    public SpatialGrid(float worldWidth, float worldHeight, float cellSize)
    {
        _cellSize = cellSize;
        _cols = Math.Max(1, (int)MathF.Ceiling(worldWidth / cellSize));
        _rows = Math.Max(1, (int)MathF.Ceiling(worldHeight / cellSize));
        int cellCount = _cols * _rows;
        _cellStart = new int[cellCount];
        _cellCount = new int[cellCount];
        _indices = Array.Empty<int>();
        _agentCapacity = 0;
    }

    public void Rebuild(ReadOnlySpan<ArenaAgent> agents)
    {
        int n = agents.Length;
        if (n > _agentCapacity)
        {
            _indices = new int[n];
            _agentCapacity = n;
        }

        int cellCount = _cols * _rows;
        Array.Clear(_cellCount, 0, cellCount);

        for (int i = 0; i < n; i++)
        {
            int cellId = CellIdOf(agents[i].X, agents[i].Y);
            _cellCount[cellId]++;
        }

        _cellStart[0] = 0;
        for (int c = 1; c < cellCount; c++)
            _cellStart[c] = _cellStart[c - 1] + _cellCount[c - 1];

        // Temporary copy of starts for placement (use cellCount as write cursors)
        Span<int> writeCursor = stackalloc int[cellCount];
        _cellStart.AsSpan(0, cellCount).CopyTo(writeCursor);

        for (int i = 0; i < n; i++)
        {
            int cellId = CellIdOf(agents[i].X, agents[i].Y);
            _indices[writeCursor[cellId]] = i;
            writeCursor[cellId]++;
        }
    }

    public (int col, int row) CellOf(float x, float y)
    {
        int col = Math.Clamp((int)(x / _cellSize), 0, _cols - 1);
        int row = Math.Clamp((int)(y / _cellSize), 0, _rows - 1);
        return (col, row);
    }

    public ReadOnlySpan<int> GetCellContents(int col, int row)
    {
        if (col < 0 || col >= _cols || row < 0 || row >= _rows)
            return ReadOnlySpan<int>.Empty;
        int cellId = row * _cols + col;
        return _indices.AsSpan(_cellStart[cellId], _cellCount[cellId]);
    }

    public (int minCol, int maxCol, int minRow, int maxRow) GetRayBounds(
        float ox, float oy, float dx, float dy, float maxDist)
    {
        float endX = ox + dx * maxDist;
        float endY = oy + dy * maxDist;

        float minX = MathF.Min(ox, endX);
        float maxX = MathF.Max(ox, endX);
        float minY = MathF.Min(oy, endY);
        float maxY = MathF.Max(oy, endY);

        int minCol = Math.Clamp((int)(minX / _cellSize), 0, _cols - 1);
        int maxCol = Math.Clamp((int)(maxX / _cellSize), 0, _cols - 1);
        int minRow = Math.Clamp((int)(minY / _cellSize), 0, _rows - 1);
        int maxRow = Math.Clamp((int)(maxY / _cellSize), 0, _rows - 1);

        return (minCol, maxCol, minRow, maxRow);
    }

    private int CellIdOf(float x, float y)
    {
        int col = Math.Clamp((int)(x / _cellSize), 0, _cols - 1);
        int row = Math.Clamp((int)(y / _cellSize), 0, _rows - 1);
        return row * _cols + col;
    }
}
