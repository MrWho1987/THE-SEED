using Seed.Core;

namespace Seed.Market.Evolution;

/// <summary>
/// Hall of Fame: stores the best genome ever seen from each species.
/// Used for archive-based reseeding when species stagnate or go extinct.
/// </summary>
public sealed class EliteArchive
{
    private readonly Dictionary<int, (IGenome Genome, float Fitness)> _champions = new();
    private readonly int _maxSize;

    public EliteArchive(int maxSize = 100)
    {
        _maxSize = maxSize;
    }

    public int Count => _champions.Count;

    public void Update(int speciesId, IGenome genome, float fitness)
    {
        if (_champions.TryGetValue(speciesId, out var existing))
        {
            if (fitness > existing.Fitness)
                _champions[speciesId] = (genome.CloneGenome(), fitness);
        }
        else
        {
            if (_champions.Count >= _maxSize)
                EvictLowest();
            _champions[speciesId] = (genome.CloneGenome(), fitness);
        }
    }

    public IReadOnlyList<IGenome> GetDiverseElites(int count)
    {
        return _champions.Values
            .OrderByDescending(e => e.Fitness)
            .Take(count)
            .Select(e => e.Genome)
            .ToList();
    }

    public bool TryGet(int speciesId, out IGenome? genome, out float fitness)
    {
        if (_champions.TryGetValue(speciesId, out var entry))
        {
            genome = entry.Genome;
            fitness = entry.Fitness;
            return true;
        }
        genome = null;
        fitness = 0f;
        return false;
    }

    public float LowestFitness =>
        _champions.Count > 0 ? _champions.Values.Min(e => e.Fitness) : 0f;

    private void EvictLowest()
    {
        if (_champions.Count == 0) return;
        int lowestKey = _champions.MinBy(kv => kv.Value.Fitness).Key;
        _champions.Remove(lowestKey);
    }
}
