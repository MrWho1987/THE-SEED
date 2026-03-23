using System.Text.Json;
using System.Text.Json.Serialization;
using Seed.Core;

namespace Seed.Genetics;

/// <summary>
/// Reserved fields for V2 compatibility.
/// </summary>
public sealed record ReservedGenomeFields(
    string[] CppnOutputNames,
    float[] ReservedMutationScales
)
{
    public static ReservedGenomeFields Default => new(
        CppnOutputNames: ["c", "w", "delay", "tau", "module_tag", "gate"],
        ReservedMutationScales: [1.0f, 1.0f, 1.0f, 1.0f]
    );
}

/// <summary>
/// The complete genome: CPPN + development/learning/stability parameters.
/// </summary>
public sealed record SeedGenome(
    Guid GenomeId,
    CppnNetwork Cppn,
    DevelopmentParams Dev,
    LearningParams Learn,
    StabilityParams Stable,
    ReservedGenomeFields Reserved
) : IGenome
{
    public string GenomeType => "SeedGenome.CPPN.NEAT";

    /// <summary>
    /// Create a minimal random genome.
    /// </summary>
    public static SeedGenome CreateRandom(Rng64 rng)
    {
        var cppn = CppnNetwork.CreateMinimal(CppnInputIndex.Count, CppnOutputIndex.Count, rng);
        return new SeedGenome(
            GenomeId: Guid.NewGuid(),
            Cppn: cppn,
            Dev: DevelopmentParams.Default,
            Learn: LearningParams.Default,
            Stable: StabilityParams.Default,
            Reserved: ReservedGenomeFields.Default
        );
    }

    public IGenome CloneGenome(Guid? newId = null)
    {
        return this with
        {
            GenomeId = newId ?? GenomeId,
            Cppn = Cppn.DeepCopy()
        };
    }

    public IGenome Mutate(in MutationContext ctx)
    {
        var rng = ctx.Rng;
        var cfg = ctx.Config;
        var tracker = ctx.Innovations;

        var newCppn = Cppn.DeepCopy();
        var newDev = Dev;
        var newLearn = Learn;
        var newStable = Stable;

        // Weight mutation
        for (int i = 0; i < newCppn.Connections.Count; i++)
        {
            if (rng.NextFloat01() < cfg.PWeightMutate)
            {
                var conn = newCppn.Connections[i];
                float newWeight;

                if (rng.NextFloat01() < cfg.PWeightReset)
                {
                    newWeight = rng.NextFloat(-cfg.WeightResetMax, cfg.WeightResetMax);
                }
                else
                {
                    newWeight = conn.Weight + rng.NextGaussian(0f, cfg.SigmaWeight);
                }

                newCppn.Connections[i] = conn with { Weight = newWeight };
            }
        }

        // Bias mutation
        for (int i = 0; i < newCppn.Nodes.Count; i++)
        {
            var node = newCppn.Nodes[i];
            if (node.Type != CppnNodeType.Input && rng.NextFloat01() < cfg.PBiasMutate)
            {
                float newBias = node.Bias + rng.NextGaussian(0f, cfg.SigmaBias);
                newCppn.Nodes[i] = node with { Bias = newBias };
            }
        }

        // Add connection mutation
        if (rng.NextFloat01() < cfg.PAddConn)
        {
            TryAddConnection(newCppn, tracker, ref rng, cfg);
        }

        // Add node mutation
        if (rng.NextFloat01() < cfg.PAddNode)
        {
            TryAddNode(newCppn, tracker, ref rng);
        }

        // Parameter mutations
        if (rng.NextFloat01() < cfg.PParamMutate)
        {
            newDev = MutateDevelopmentParams(newDev, ref rng, cfg);
        }
        if (rng.NextFloat01() < cfg.PParamMutate)
        {
            newLearn = MutateLearningParams(newLearn, ref rng, cfg);
        }
        if (rng.NextFloat01() < cfg.PParamMutate)
        {
            newStable = MutateStabilityParams(newStable, ref rng, cfg);
        }

        return new SeedGenome(
            GenomeId: Guid.NewGuid(),
            Cppn: newCppn,
            Dev: newDev,
            Learn: newLearn,
            Stable: newStable,
            Reserved: Reserved
        );
    }

    private static void TryAddConnection(
        CppnNetwork cppn, 
        IInnovationTracker tracker,
        ref Rng64 rng, 
        MutationConfig cfg)
    {
        // Get all possible (src, dst) pairs
        var nonInputNodes = cppn.Nodes.Where(n => n.Type != CppnNodeType.Input).ToList();
        var allNodes = cppn.Nodes;

        if (nonInputNodes.Count == 0) return;

        // Try to find a valid new connection
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var src = allNodes[rng.NextInt(allNodes.Count)];
            var dst = nonInputNodes[rng.NextInt(nonInputNodes.Count)];

            if (src.NodeId == dst.NodeId) continue;

            // Check if connection already exists
            var existing = cppn.Connections.FirstOrDefault(c => 
                c.SrcNodeId == src.NodeId && c.DstNodeId == dst.NodeId);

            if (existing != null)
            {
                if (!existing.Enabled)
                {
                    // Enable existing disabled connection
                    var idx = cppn.Connections.IndexOf(existing);
                    cppn.Connections[idx] = existing with { Enabled = true };
                }
                continue;
            }

            // Create new connection
            int innovId = tracker.GetOrCreateConnectionInnovation(src.NodeId, dst.NodeId);
            float weight = rng.NextFloat(-cfg.WInitMax, cfg.WInitMax);
            cppn.Connections.Add(new CppnConnection(innovId, src.NodeId, dst.NodeId, weight, true));
            return;
        }
    }

    private static void TryAddNode(CppnNetwork cppn, IInnovationTracker tracker, ref Rng64 rng)
    {
        var enabledConns = cppn.Connections.Where(c => c.Enabled).ToList();
        if (enabledConns.Count == 0) return;

        var conn = enabledConns[rng.NextInt(enabledConns.Count)];
        var connIdx = cppn.Connections.IndexOf(conn);

        var (newNodeId, innovSrcToNew, innovNewToDst) = 
            tracker.GetOrCreateSplitInnovation(conn.InnovationId);

        // Guard: if a re-enabled connection is split again, the tracker returns the
        // same node ID that was created the first time. Skip to avoid duplicate nodes.
        if (cppn.Nodes.Any(n => n.NodeId == newNodeId))
            return;

        cppn.Connections[connIdx] = conn with { Enabled = false };

        cppn.Nodes.Add(new CppnNode(newNodeId, CppnNodeType.Hidden, ActivationFn.Tanh, 0f));
        cppn.Connections.Add(new CppnConnection(innovSrcToNew, conn.SrcNodeId, newNodeId, 1.0f, true));
        cppn.Connections.Add(new CppnConnection(innovNewToDst, newNodeId, conn.DstNodeId, conn.Weight, true));
    }

    private static DevelopmentParams MutateDevelopmentParams(
        DevelopmentParams p, ref Rng64 rng, MutationConfig cfg)
    {
        return p with
        {
            ConnectionThreshold = DeterministicHelpers.Clamp(
                p.ConnectionThreshold + rng.NextGaussian(0f, cfg.SigmaParam), 0.01f, 0.9f),
            InitialWeightScale = DeterministicHelpers.Clamp(
                p.InitialWeightScale + rng.NextGaussian(0f, cfg.SigmaParam), 0.1f, 3f),
            GlobalSampleRate = DeterministicHelpers.Clamp(
                p.GlobalSampleRate + rng.NextGaussian(0f, cfg.SigmaParam * 0.1f), 0.001f, 0.1f)
        };
    }

    private static LearningParams MutateLearningParams(
        LearningParams p, ref Rng64 rng, MutationConfig cfg)
    {
        return p with
        {
            Eta = DeterministicHelpers.Clamp(
                p.Eta + rng.NextGaussian(0f, cfg.SigmaParam * 0.1f), 0.0001f, 0.1f),
            EligibilityDecay = DeterministicHelpers.Clamp(
                p.EligibilityDecay + rng.NextGaussian(0f, cfg.SigmaParam * 0.1f), 0.8f, 0.999f),
            AlphaCuriosity = DeterministicHelpers.Clamp(
                p.AlphaCuriosity + rng.NextGaussian(0f, cfg.SigmaParam), 0f, 1f)
        };
    }

    private static StabilityParams MutateStabilityParams(
        StabilityParams p, ref Rng64 rng, MutationConfig cfg)
    {
        return p with
        {
            WeightMaxAbs = DeterministicHelpers.Clamp(
                p.WeightMaxAbs + rng.NextGaussian(0f, cfg.SigmaParam), 1f, 10f),
            HomeostasisStrength = DeterministicHelpers.Clamp(
                p.HomeostasisStrength + rng.NextGaussian(0f, cfg.SigmaParam * 0.1f), 0f, 0.1f),
            ActivationTarget = DeterministicHelpers.Clamp(
                p.ActivationTarget + rng.NextGaussian(0f, cfg.SigmaParam), 0.05f, 0.5f)
        };
    }

    /// <summary>
    /// NEAT crossover: align genes by InnovationId, inherit from fitter parent.
    /// </summary>
    public static SeedGenome Crossover(SeedGenome fitter, SeedGenome other, ref Rng64 rng)
    {
        var genesF = fitter.Cppn.Connections.OrderBy(c => c.InnovationId).ToList();
        var genesO = other.Cppn.Connections.OrderBy(c => c.InnovationId).ToList();

        var childConns = new List<CppnConnection>();
        var referencedNodeIds = new HashSet<int>();

        int iF = 0, iO = 0;
        while (iF < genesF.Count && iO < genesO.Count)
        {
            var gF = genesF[iF];
            var gO = genesO[iO];

            if (gF.InnovationId == gO.InnovationId)
            {
                // Matching gene: randomly pick from either parent
                var chosen = rng.NextFloat01() < 0.5f ? gF : gO;

                // If either parent's copy is disabled, 75% chance child gene is disabled
                bool enabled = chosen.Enabled;
                if (!gF.Enabled || !gO.Enabled)
                    enabled = rng.NextFloat01() >= 0.75f;

                childConns.Add(chosen with { Enabled = enabled });
                referencedNodeIds.Add(chosen.SrcNodeId);
                referencedNodeIds.Add(chosen.DstNodeId);
                iF++;
                iO++;
            }
            else if (gF.InnovationId < gO.InnovationId)
            {
                // Disjoint from fitter: always inherit
                childConns.Add(gF);
                referencedNodeIds.Add(gF.SrcNodeId);
                referencedNodeIds.Add(gF.DstNodeId);
                iF++;
            }
            else
            {
                // Disjoint from weaker: discard
                iO++;
            }
        }

        // Excess from fitter: always inherit
        while (iF < genesF.Count)
        {
            childConns.Add(genesF[iF]);
            referencedNodeIds.Add(genesF[iF].SrcNodeId);
            referencedNodeIds.Add(genesF[iF].DstNodeId);
            iF++;
        }
        // Excess from weaker: discard

        // Build child nodes: all input/output from fitter, hidden nodes referenced by selected connections.
        // Use first-wins grouping to tolerate any duplicate NodeIds from legacy genomes.
        var fitterNodeMap = fitter.Cppn.Nodes.GroupBy(n => n.NodeId).ToDictionary(g => g.Key, g => g.First());
        var otherNodeMap = other.Cppn.Nodes.GroupBy(n => n.NodeId).ToDictionary(g => g.Key, g => g.First());

        var childNodes = new List<CppnNode>();
        foreach (var node in fitter.Cppn.Nodes)
        {
            if (node.Type == CppnNodeType.Input || node.Type == CppnNodeType.Output)
            {
                childNodes.Add(node);
            }
            else if (referencedNodeIds.Contains(node.NodeId))
            {
                // Hidden node from fitter; if also in other, randomly pick bias/activation
                if (otherNodeMap.TryGetValue(node.NodeId, out var otherNode) && rng.NextFloat01() < 0.5f)
                    childNodes.Add(otherNode);
                else
                    childNodes.Add(node);
            }
        }

        // Hidden nodes only in other that are referenced but not in fitter
        foreach (var nodeId in referencedNodeIds)
        {
            if (!fitterNodeMap.ContainsKey(nodeId) && otherNodeMap.TryGetValue(nodeId, out var oNode))
                childNodes.Add(oNode);
        }

        // Prune connections that reference nodes not present in the child
        var childNodeIds = new HashSet<int>(childNodes.Select(n => n.NodeId));
        childConns.RemoveAll(c => !childNodeIds.Contains(c.SrcNodeId) || !childNodeIds.Contains(c.DstNodeId));

        int nextNodeId = Math.Max(fitter.Cppn.NextNodeId, other.Cppn.NextNodeId);

        var childCppn = new CppnNetwork(childNodes, childConns, nextNodeId);

        // Parameters: Dev from fitter, Learn and Stable randomly from either
        var childDev = fitter.Dev;
        var childLearn = rng.NextFloat01() < 0.5f ? fitter.Learn : other.Learn;
        var childStable = rng.NextFloat01() < 0.5f ? fitter.Stable : other.Stable;

        return new SeedGenome(
            GenomeId: Guid.NewGuid(),
            Cppn: childCppn,
            Dev: childDev,
            Learn: childLearn,
            Stable: childStable,
            Reserved: fitter.Reserved
        );
    }

    public float DistanceTo(IGenome other, in SpeciationConfig cfg)
    {
        if (other is not SeedGenome sg)
            return float.MaxValue;

        return ComputeNeatDistance(Cppn, sg.Cppn, cfg);
    }

    private static float ComputeNeatDistance(CppnNetwork a, CppnNetwork b, SpeciationConfig cfg)
    {
        // Sort connections by innovation ID
        var genesA = a.Connections.OrderBy(c => c.InnovationId).ToList();
        var genesB = b.Connections.OrderBy(c => c.InnovationId).ToList();

        int excess = 0;
        int disjoint = 0;
        float sumDiff = 0f;
        int matchCount = 0;

        int idxA = 0, idxB = 0;

        while (idxA < genesA.Count && idxB < genesB.Count)
        {
            int innovA = genesA[idxA].InnovationId;
            int innovB = genesB[idxB].InnovationId;

            if (innovA == innovB)
            {
                // Matching gene
                sumDiff += MathF.Abs(genesA[idxA].Weight - genesB[idxB].Weight);
                matchCount++;
                idxA++;
                idxB++;
            }
            else if (innovA < innovB)
            {
                disjoint++;
                idxA++;
            }
            else
            {
                disjoint++;
                idxB++;
            }
        }

        // Remaining genes are excess
        excess = (genesA.Count - idxA) + (genesB.Count - idxB);

        float avgWeight = matchCount > 0 ? sumDiff / matchCount : 0f;
        int n = Math.Max(genesA.Count, genesB.Count);
        if (n < 20) n = 1;

        return cfg.C1 * excess / n + cfg.C2 * disjoint / n + cfg.C3 * avgWeight;
    }

    // JSON serialization
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string ToJson() => JsonSerializer.Serialize(ToSerializable(), JsonOptions);

    public static SeedGenome FromJson(string json)
    {
        var dto = JsonSerializer.Deserialize<GenomeDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize genome");
        return FromSerializable(dto);
    }

    private GenomeDto ToSerializable() => new()
    {
        Schema = "Seed.Genome",
        SchemaVersion = 1,
        GenomeType = GenomeType,
        GenomeId = GenomeId.ToString(),
        Cppn = new CppnDto
        {
            NextNodeId = Cppn.NextNodeId,
            Nodes = Cppn.Nodes
                .OrderBy(n => n.Type)
                .ThenBy(n => n.NodeId)
                .Select(n => new CppnNodeDto
                {
                    NodeId = n.NodeId,
                    Type = n.Type.ToString(),
                    Activation = n.Activation.ToString(),
                    Bias = n.Bias
                }).ToList(),
            Connections = Cppn.Connections
                .OrderBy(c => c.InnovationId)
                .Select(c => new CppnConnectionDto
                {
                    InnovationId = c.InnovationId,
                    SrcNodeId = c.SrcNodeId,
                    DstNodeId = c.DstNodeId,
                    Weight = c.Weight,
                    Enabled = c.Enabled
                }).ToList()
        },
        Params = new ParamsDto
        {
            Development = Dev,
            Learning = Learn,
            Stability = Stable
        },
        Reserved = Reserved
    };

    private static SeedGenome FromSerializable(GenomeDto dto)
    {
        var nodes = dto.Cppn.Nodes.Select(n => new CppnNode(
            n.NodeId,
            Enum.Parse<CppnNodeType>(n.Type),
            Enum.Parse<ActivationFn>(n.Activation),
            n.Bias
        )).ToList();

        var connections = dto.Cppn.Connections.Select(c => new CppnConnection(
            c.InnovationId,
            c.SrcNodeId,
            c.DstNodeId,
            c.Weight,
            c.Enabled
        )).ToList();

        return new SeedGenome(
            GenomeId: Guid.Parse(dto.GenomeId),
            Cppn: new CppnNetwork(nodes, connections, dto.Cppn.NextNodeId),
            Dev: dto.Params.Development,
            Learn: dto.Params.Learning,
            Stable: dto.Params.Stability,
            Reserved: dto.Reserved
        );
    }
}

// DTO classes for JSON serialization
internal class GenomeDto
{
    public string Schema { get; set; } = "";
    public int SchemaVersion { get; set; }
    public string GenomeType { get; set; } = "";
    public string GenomeId { get; set; } = "";
    public CppnDto Cppn { get; set; } = new();
    public ParamsDto Params { get; set; } = new();
    public ReservedGenomeFields Reserved { get; set; } = ReservedGenomeFields.Default;
}

internal class CppnDto
{
    public int NextNodeId { get; set; }
    public List<CppnNodeDto> Nodes { get; set; } = new();
    public List<CppnConnectionDto> Connections { get; set; } = new();
}

internal class CppnNodeDto
{
    public int NodeId { get; set; }
    public string Type { get; set; } = "";
    public string Activation { get; set; } = "";
    public float Bias { get; set; }
}

internal class CppnConnectionDto
{
    public int InnovationId { get; set; }
    public int SrcNodeId { get; set; }
    public int DstNodeId { get; set; }
    public float Weight { get; set; }
    public bool Enabled { get; set; }
}

internal class ParamsDto
{
    public DevelopmentParams Development { get; set; } = DevelopmentParams.Default;
    public LearningParams Learning { get; set; } = LearningParams.Default;
    public StabilityParams Stability { get; set; } = StabilityParams.Default;
}

