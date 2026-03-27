using System.Text.Json;
using System.Text.Json.Serialization;

namespace Seed.Market.Trading;

/// <summary>
/// Appends each closed trade to a JSONL file and writes a session summary on dispose.
/// Thread-safe for concurrent use from paper trading and backtest modes.
/// </summary>
public sealed class TradeLogger : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly StreamWriter _writer;
    private readonly object _lock = new();
    private readonly DateTimeOffset _sessionStart;
    private int _tradeCount;
    private decimal _totalPnl;
    private int _wins;

    public TradeLogger(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        _writer = new StreamWriter(path, append: true) { AutoFlush = true };
        _sessionStart = DateTimeOffset.UtcNow;

        _writer.WriteLine(JsonSerializer.Serialize(new
        {
            type = "session_start",
            timestamp = _sessionStart
        }, JsonOpts));
    }

    public void LogTrade(ClosedTrade trade)
    {
        lock (_lock)
        {
            _tradeCount++;
            _totalPnl += trade.Pnl;
            if (trade.Pnl > 0) _wins++;

            var entry = new
            {
                type = "trade",
                trade.Symbol,
                trade.Direction,
                trade.EntryPrice,
                trade.ExitPrice,
                trade.Size,
                trade.Pnl,
                trade.Fee,
                trade.HoldingTicks,
                trade.OpenTime,
                trade.CloseTime
            };
            _writer.WriteLine(JsonSerializer.Serialize(entry, JsonOpts));
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            var summary = new
            {
                type = "session_end",
                timestamp = DateTimeOffset.UtcNow,
                durationMinutes = (DateTimeOffset.UtcNow - _sessionStart).TotalMinutes,
                totalTrades = _tradeCount,
                totalPnl = _totalPnl,
                winRate = _tradeCount > 0 ? (float)_wins / _tradeCount : 0f,
                wins = _wins,
                losses = _tradeCount - _wins
            };
            _writer.WriteLine(JsonSerializer.Serialize(summary, JsonOpts));
            _writer.Dispose();
        }
    }
}
