namespace Seed.Core;

/// <summary>
/// Deterministic seed derivation for creating independent PRNG streams.
/// Each domain + parameter combination produces a unique, well-distributed seed.
/// </summary>
public static class SeedDerivation
{
    // Domain constants (fixed forever, do not change)
    public const ulong DOMAIN_RUN        = 0x52554E0000000001UL;  // "RUN\0...\1"
    public const ulong DOMAIN_GENERATION = 0x47454E0000000001UL;  // "GEN\0...\1"
    public const ulong DOMAIN_WORLD      = 0x574F524C44000001UL;  // "WORLD\0\1"
    public const ulong DOMAIN_AGENT      = 0x4147454E54000001UL;  // "AGENT\0\1"
    public const ulong DOMAIN_DEVELOP    = 0x444556454C4F5001UL;  // "DEVELOP\1"
    public const ulong DOMAIN_MUTATION   = 0x4D55544154450001UL;  // "MUTATE\0\1"
    public const ulong DOMAIN_TIEBREAK   = 0x5449454252454B01UL;  // "TIEBREK\1"

    /// <summary>
    /// Derive a deterministic seed from a base seed, domain, and up to 3 parameters.
    /// The result is well-mixed and suitable for initializing an Rng64.
    /// </summary>
    public static ulong DeriveSeed(ulong runSeed, ulong domain, ulong a = 0, ulong b = 0, ulong c = 0)
    {
        ulong x = runSeed;
        x ^= domain * 0xD6E8FEB86659FD93UL;
        x ^= a * 0xA5A3564E27F2D3A5UL;
        x ^= b * 0x3C79AC492BA7B653UL;
        x ^= c * 0x1C69B3F74AC4AE35UL;
        return Rng64.SplitMix64(ref x);
    }

    /// <summary>
    /// Derive generation seed for a specific generation.
    /// </summary>
    public static ulong GenerationSeed(ulong runSeed, int generationIndex)
        => DeriveSeed(runSeed, DOMAIN_GENERATION, (ulong)generationIndex);

    /// <summary>
    /// Derive world seed for a specific world in a generation.
    /// </summary>
    public static ulong WorldSeed(ulong runSeed, int generationIndex, int worldIndex, int worldBundleKey = 0)
        => DeriveSeed(runSeed, DOMAIN_WORLD, (ulong)generationIndex, (ulong)worldIndex, (ulong)worldBundleKey);

    /// <summary>
    /// Derive agent seed for a specific genome evaluation.
    /// </summary>
    public static ulong AgentSeed(ulong runSeed, int generationIndex, int genomeOrdinal, int evaluationReplica = 0)
        => DeriveSeed(runSeed, DOMAIN_AGENT, (ulong)generationIndex, (ulong)genomeOrdinal, (ulong)evaluationReplica);

    /// <summary>
    /// Derive mutation seed for offspring creation.
    /// </summary>
    public static ulong MutationSeed(ulong runSeed, int generationIndex, int parentOrdinal, int childOrdinal)
        => DeriveSeed(runSeed, DOMAIN_MUTATION, (ulong)generationIndex, (ulong)parentOrdinal, (ulong)childOrdinal);

    /// <summary>
    /// Derive tie-break seed for deterministic ranking.
    /// </summary>
    public static ulong TieBreakSeed(ulong runSeed, int generationIndex)
        => DeriveSeed(runSeed, DOMAIN_TIEBREAK, (ulong)generationIndex);

    /// <summary>
    /// Derive development seed for graph compilation.
    /// </summary>
    public static ulong DevelopmentSeed(ulong runSeed, int nodeId)
        => DeriveSeed(runSeed, DOMAIN_DEVELOP, (ulong)nodeId);

    /// <summary>
    /// Compute deterministic tie-break hash for a genome.
    /// Used when sorting genomes with equal fitness scores.
    /// </summary>
    public static ulong TieBreakHash(Guid genomeId, ulong tieSeed)
    {
        Span<byte> bytes = stackalloc byte[16];
        genomeId.TryWriteBytes(bytes);
        
        ulong guidLo = BitConverter.ToUInt64(bytes[..8]);
        ulong guidHi = BitConverter.ToUInt64(bytes[8..]);
        
        return DeriveSeed(tieSeed, DOMAIN_TIEBREAK, guidLo, guidHi, 0);
    }
}


