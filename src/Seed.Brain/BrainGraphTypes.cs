using System.Text.Json;
using Seed.Core;

namespace Seed.Brain;

/// <summary>
/// Brain node type.
/// </summary>
public enum BrainNodeType
{
    Input = 0,
    Hidden = 1,
    Output = 2
}

/// <summary>
/// Edge type (V2 may append values).
/// </summary>
public enum EdgeType
{
    Normal = 0,
    Modulatory = 1,
    Memory = 2
}

/// <summary>
/// Reserved node metadata for V2.
/// </summary>
public sealed record NodeMetadata(
    int RegionId = 0,
    int ModuleId = 0,
    float TimeConstant = 0f,
    int PlasticityProfileId = 0
);

/// <summary>
/// Reserved edge metadata for V2.
/// </summary>
public sealed record EdgeMetadata(
    EdgeType EdgeType = EdgeType.Normal,
    int Delay = 0,
    int PlasticityProfileId = 0
);

/// <summary>
/// Brain node with coordinates.
/// </summary>
public sealed record BrainNode(
    int NodeId,
    BrainNodeType Type,
    float X,
    float Y,
    int Layer,
    NodeMetadata Meta
);

/// <summary>
/// Brain edge with two-speed weights.
/// </summary>
public sealed record BrainEdge(
    int SrcNodeId,
    int DstNodeId,
    float WSlow,
    float WFast,
    float PlasticityGain,
    EdgeMetadata Meta
);

/// <summary>
/// Reserved fields for V2.
/// </summary>
public sealed record BrainGraphReserved(
    string[] ReservedKeys,
    string[] ReservedValues
)
{
    public static BrainGraphReserved Default => new(
        ReservedKeys: ["v2_placeholder_0"],
        ReservedValues: ["0"]
    );
}

/// <summary>
/// Complete brain graph with sparse incoming adjacency lists.
/// </summary>
public sealed class BrainGraph : IBrainGraph
{
    public List<BrainNode> Nodes { get; }
    public Dictionary<int, List<BrainEdge>> IncomingByDst { get; }
    public int InputCount { get; }
    public int OutputCount { get; }
    public int ModulatorCount { get; }
    public BrainGraphReserved Reserved { get; }

    public int NodeCount => Nodes.Count;
    public int EdgeCount => IncomingByDst.Values.Sum(list => list.Count);

    public BrainGraph(
        List<BrainNode> nodes,
        Dictionary<int, List<BrainEdge>> incomingByDst,
        int inputCount,
        int outputCount,
        int modulatorCount,
        BrainGraphReserved? reserved = null)
    {
        Nodes = nodes;
        IncomingByDst = incomingByDst;
        InputCount = inputCount;
        OutputCount = outputCount;
        ModulatorCount = modulatorCount;
        Reserved = reserved ?? BrainGraphReserved.Default;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string ToJson()
    {
        var dto = new BrainGraphDto
        {
            Schema = "Seed.BrainGraph",
            SchemaVersion = 1,
            InputCount = InputCount,
            OutputCount = OutputCount,
            ModulatorCount = ModulatorCount,
            Nodes = Nodes
                .OrderBy(n => n.NodeId)
                .Select(n => new BrainNodeDto
                {
                    NodeId = n.NodeId,
                    Type = n.Type.ToString(),
                    X = n.X,
                    Y = n.Y,
                    Layer = n.Layer,
                    Meta = n.Meta
                }).ToList(),
            IncomingEdges = IncomingByDst
                .OrderBy(kv => kv.Key)
                .Select(kv => new IncomingEdgesDto
                {
                    DstNodeId = kv.Key,
                    Edges = kv.Value
                        .OrderBy(e => e.SrcNodeId)
                        .Select(e => new BrainEdgeDto
                        {
                            SrcNodeId = e.SrcNodeId,
                            WSlow = e.WSlow,
                            WFast = e.WFast,
                            PlasticityGain = e.PlasticityGain,
                            Meta = e.Meta
                        }).ToList()
                }).ToList(),
            Reserved = Reserved
        };

        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    public static BrainGraph FromJson(string json)
    {
        var dto = JsonSerializer.Deserialize<BrainGraphDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize BrainGraph");

        var nodes = dto.Nodes.Select(n => new BrainNode(
            n.NodeId,
            Enum.Parse<BrainNodeType>(n.Type),
            n.X,
            n.Y,
            n.Layer,
            n.Meta
        )).ToList();

        var incoming = dto.IncomingEdges.ToDictionary(
            ie => ie.DstNodeId,
            ie => ie.Edges.Select(e => new BrainEdge(
                e.SrcNodeId,
                ie.DstNodeId,
                e.WSlow,
                e.WFast,
                e.PlasticityGain,
                e.Meta
            )).ToList()
        );

        return new BrainGraph(
            nodes,
            incoming,
            dto.InputCount,
            dto.OutputCount,
            dto.ModulatorCount,
            dto.Reserved
        );
    }
}

// DTO classes for JSON serialization
internal class BrainGraphDto
{
    public string Schema { get; set; } = "";
    public int SchemaVersion { get; set; }
    public int InputCount { get; set; }
    public int OutputCount { get; set; }
    public int ModulatorCount { get; set; }
    public List<BrainNodeDto> Nodes { get; set; } = new();
    public List<IncomingEdgesDto> IncomingEdges { get; set; } = new();
    public BrainGraphReserved Reserved { get; set; } = BrainGraphReserved.Default;
}

internal class BrainNodeDto
{
    public int NodeId { get; set; }
    public string Type { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public int Layer { get; set; }
    public NodeMetadata Meta { get; set; } = new();
}

internal class IncomingEdgesDto
{
    public int DstNodeId { get; set; }
    public List<BrainEdgeDto> Edges { get; set; } = new();
}

internal class BrainEdgeDto
{
    public int SrcNodeId { get; set; }
    public float WSlow { get; set; }
    public float WFast { get; set; }
    public float PlasticityGain { get; set; }
    public EdgeMetadata Meta { get; set; } = new();
}

