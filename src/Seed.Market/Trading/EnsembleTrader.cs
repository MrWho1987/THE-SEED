using Seed.Market.Agents;
using Seed.Market.Signals;

namespace Seed.Market.Trading;

/// <summary>
/// Deploys top K genomes from distinct species with weighted consensus signal.
/// Each member maintains a shadow portfolio for brain state updates.
/// The consensus signal is executed on a shared portfolio by the caller.
/// </summary>
public sealed class EnsembleTrader
{
    private readonly List<(MarketAgent Agent, float Weight)> _members;

    public EnsembleTrader(List<(MarketAgent Agent, float Weight)> members)
    {
        _members = members;
    }

    public int MemberCount => _members.Count;

    public TradingSignal ComputeConsensus(SignalSnapshot snapshot, TickContext ctx)
    {
        if (_members.Count == 0)
            return new TradingSignal(TradeDirection.Flat, 0f, 0f, false);

        float dirSum = 0f, sizeSum = 0f, urgencySum = 0f;
        float exitVotes = 0f, totalWeight = 0f;

        foreach (var (agent, weight) in _members)
        {
            agent.ProcessTick(snapshot, ctx);
            var signal = agent.LastGeneratedSignal;

            dirSum += (float)signal.Direction * weight;
            sizeSum += signal.SizePct * weight;
            urgencySum += signal.Urgency * weight;
            exitVotes += signal.ExitCurrent ? weight : 0f;
            totalWeight += weight;
        }

        if (totalWeight <= 0f)
            return new TradingSignal(TradeDirection.Flat, 0f, 0f, false);

        float avgDir = dirSum / totalWeight;
        var dir = avgDir > 0.15f ? TradeDirection.Long
                : avgDir < -0.15f ? TradeDirection.Short
                : TradeDirection.Flat;

        return new TradingSignal(
            dir,
            Math.Clamp(sizeSum / totalWeight, 0f, 1f),
            Math.Clamp(urgencySum / totalWeight, 0f, 1f),
            exitVotes / totalWeight > 0.5f);
    }

    public VoteSummary GetVoteSummary()
    {
        int longVotes = 0, shortVotes = 0, flatVotes = 0;
        float maxWeight = 0f, totalWeight = 0f;

        foreach (var (agent, weight) in _members)
        {
            var signal = agent.LastGeneratedSignal;
            switch (signal.Direction)
            {
                case TradeDirection.Long: longVotes++; break;
                case TradeDirection.Short: shortVotes++; break;
                default: flatVotes++; break;
            }
            if (weight > maxWeight) maxWeight = weight;
            totalWeight += weight;
        }

        return new VoteSummary(longVotes, shortVotes, flatVotes,
            totalWeight > 0 ? maxWeight / totalWeight : 0f);
    }
}

public readonly record struct VoteSummary(
    int LongVotes,
    int ShortVotes,
    int FlatVotes,
    float MaxWeightFraction
);
