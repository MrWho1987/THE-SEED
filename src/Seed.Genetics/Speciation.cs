using Seed.Core;

namespace Seed.Genetics;

/// <summary>
/// Species with a representative genome and member list.
/// </summary>
public sealed class Species
{
    public int SpeciesId { get; }
    public IGenome Representative { get; set; }
    public List<IGenome> Members { get; } = new();
    public float AdjustedFitnessSum { get; set; }
    public int StagnationCounter { get; set; }
    public float BestFitness { get; set; }

    public Species(int id, IGenome representative)
    {
        SpeciesId = id;
        Representative = representative;
        Members.Add(representative);
    }
}

/// <summary>
/// NEAT-style speciation manager.
/// </summary>
public sealed class SpeciationManager
{
    private readonly List<Species> _species = new();
    private int _nextSpeciesId;

    public IReadOnlyList<Species> Species => _species;
    public int NextSpeciesId => _nextSpeciesId;

    /// <summary>
    /// Restore species state from a checkpoint. Each entry provides the representative
    /// genome directly (not by index), along with stagnation counter and best fitness.
    /// </summary>
    public void RestoreFrom(
        IReadOnlyList<(int SpeciesId, IGenome Representative, int StagnationCounter, float BestFitness)> entries,
        int nextSpeciesId)
    {
        _species.Clear();
        foreach (var (speciesId, representative, stagnation, bestFit) in entries)
        {
            var species = new Species(speciesId, representative);
            species.StagnationCounter = stagnation;
            species.BestFitness = bestFit;
            _species.Add(species);
        }
        _nextSpeciesId = nextSpeciesId;
    }

    /// <summary>
    /// Assign genomes to species based on compatibility distance.
    /// Must be called with genomes in a stable order for determinism.
    /// </summary>
    public void Speciate(IReadOnlyList<IGenome> genomes, SpeciationConfig config)
    {
        // Clear member lists but keep species for representative continuity
        foreach (var sp in _species)
        {
            sp.Members.Clear();
        }

        // Assign each genome to a species (stable order)
        foreach (var genome in genomes)
        {
            bool assigned = false;

            // Try existing species in order of ID
            foreach (var sp in _species.OrderBy(s => s.SpeciesId))
            {
                float distance = genome.DistanceTo(sp.Representative, config);
                if (distance < config.CompatibilityThreshold)
                {
                    sp.Members.Add(genome);
                    assigned = true;
                    break;
                }
            }

            if (!assigned)
            {
                // Create new species
                var newSpecies = new Species(_nextSpeciesId++, genome);
                _species.Add(newSpecies);
            }
        }

        // Remove empty species
        _species.RemoveAll(s => s.Members.Count == 0);

        // Update representatives (first member after sorting by genome ID)
        foreach (var sp in _species)
        {
            sp.Representative = sp.Members
                .OrderBy(g => g.GenomeId)
                .First();
        }
    }

    /// <summary>
    /// Compute fitness sharing for a genome.
    /// </summary>
    public float ComputeAdjustedFitness(IGenome genome, float rawFitness, SpeciationConfig config)
    {
        var species = _species.FirstOrDefault(s => s.Members.Contains(genome));
        if (species == null || species.Members.Count == 0)
            return rawFitness;

        // Simple sharing: divide by species size
        return rawFitness / species.Members.Count;
    }

    /// <summary>
    /// Get species ID for a genome.
    /// </summary>
    public int GetSpeciesId(IGenome genome)
    {
        var species = _species.FirstOrDefault(s => s.Members.Contains(genome));
        return species?.SpeciesId ?? -1;
    }

    /// <summary>
    /// Calculate offspring allocation per species based on adjusted fitness.
    /// </summary>
    public Dictionary<int, int> AllocateOffspring(
        IReadOnlyDictionary<Guid, float> rawFitnesses,
        int totalOffspring,
        PopulationBudget budget,
        SpeciationConfig config,
        int minOffspringPerSpecies = 0)
    {
        var allocation = new Dictionary<int, int>();

        // Calculate adjusted fitness sums per species
        float totalAdjusted = 0f;
        foreach (var sp in _species)
        {
            sp.AdjustedFitnessSum = 0f;
            foreach (var genome in sp.Members)
            {
                if (rawFitnesses.TryGetValue(genome.GenomeId, out float rawFit))
                {
                    float adj = rawFit / sp.Members.Count;
                    sp.AdjustedFitnessSum += adj;
                }
            }
            totalAdjusted += sp.AdjustedFitnessSum;
        }

        // Floor allocation: guarantee minimum offspring for viable species
        int floorTotal = 0;
        foreach (var sp in _species)
        {
            int floor = (sp.Members.Count >= 2 && minOffspringPerSpecies > 0) ? minOffspringPerSpecies : 0;
            allocation[sp.SpeciesId] = floor;
            floorTotal += floor;
        }

        int remaining = Math.Max(0, totalOffspring - floorTotal);

        if (totalAdjusted <= 0 || remaining == 0)
        {
            if (remaining > 0)
            {
                int perSpecies = remaining / Math.Max(1, _species.Count);
                foreach (var sp in _species)
                    allocation[sp.SpeciesId] += perSpecies;
            }
            return allocation;
        }

        // Proportional allocation of remaining budget
        int allocated = floorTotal;
        foreach (var sp in _species.OrderBy(s => s.SpeciesId))
        {
            int count = (int)(sp.AdjustedFitnessSum / totalAdjusted * remaining);
            count = Math.Max(0, count);
            allocation[sp.SpeciesId] += count;
            allocated += count;
        }

        var sortedSpecies = _species.OrderByDescending(s => s.AdjustedFitnessSum).ToList();

        // Distribute remainder to top species
        int gap = totalOffspring - allocated;
        for (int i = 0; i < gap && i < sortedSpecies.Count; i++)
        {
            allocation[sortedSpecies[i].SpeciesId]++;
            allocated++;
        }

        // Trim overcap from lowest-fitness species
        while (allocated > totalOffspring)
        {
            for (int i = sortedSpecies.Count - 1; i >= 0 && allocated > totalOffspring; i--)
            {
                int sid = sortedSpecies[i].SpeciesId;
                if (allocation[sid] > (minOffspringPerSpecies > 0 && _species.First(s => s.SpeciesId == sid).Members.Count >= 2 ? minOffspringPerSpecies : 0))
                {
                    allocation[sid]--;
                    allocated--;
                }
            }
        }

        return allocation;
    }
}


