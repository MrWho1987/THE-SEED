using Seed.Market.Signals;

namespace Seed.Market.Indicators;

/// <summary>
/// Cyclical time encoding using sin/cos for continuous temporal features.
/// Also tracks proximity to major economic events (FOMC, CPI).
/// </summary>
public static class TimeEncoding
{
    private static readonly DateTimeOffset[] FomcDates2026 =
    [
        new(2026, 1, 29, 19, 0, 0, TimeSpan.Zero),
        new(2026, 3, 18, 18, 0, 0, TimeSpan.Zero),
        new(2026, 5, 6, 18, 0, 0, TimeSpan.Zero),
        new(2026, 6, 17, 18, 0, 0, TimeSpan.Zero),
        new(2026, 7, 29, 18, 0, 0, TimeSpan.Zero),
        new(2026, 9, 16, 18, 0, 0, TimeSpan.Zero),
        new(2026, 11, 4, 18, 0, 0, TimeSpan.Zero),
        new(2026, 12, 16, 19, 0, 0, TimeSpan.Zero),
    ];

    private static readonly DateTimeOffset[] CpiDates2026 =
    [
        new(2026, 1, 14, 13, 30, 0, TimeSpan.Zero),
        new(2026, 2, 12, 13, 30, 0, TimeSpan.Zero),
        new(2026, 3, 12, 12, 30, 0, TimeSpan.Zero),
        new(2026, 4, 10, 12, 30, 0, TimeSpan.Zero),
        new(2026, 5, 13, 12, 30, 0, TimeSpan.Zero),
        new(2026, 6, 11, 12, 30, 0, TimeSpan.Zero),
        new(2026, 7, 15, 12, 30, 0, TimeSpan.Zero),
        new(2026, 8, 12, 12, 30, 0, TimeSpan.Zero),
        new(2026, 9, 11, 12, 30, 0, TimeSpan.Zero),
        new(2026, 10, 14, 12, 30, 0, TimeSpan.Zero),
        new(2026, 11, 12, 13, 30, 0, TimeSpan.Zero),
        new(2026, 12, 10, 13, 30, 0, TimeSpan.Zero),
    ];

    public static (int Index, float Value)[] Compute(DateTimeOffset now)
    {
        float hourAngle = 2f * MathF.PI * now.Hour / 24f;
        float dayAngle = 2f * MathF.PI * (int)now.DayOfWeek / 7f;
        float monthAngle = 2f * MathF.PI * (now.Month - 1) / 12f;

        float eventProximity = DaysToNearestEvent(now);
        float normalizedProximity = MathF.Max(-1f, MathF.Min(1f, 1f - eventProximity / 15f));

        return
        [
            (SignalIndex.HourSin, MathF.Sin(hourAngle)),
            (SignalIndex.HourCos, MathF.Cos(hourAngle)),
            (SignalIndex.DayOfWeekSin, MathF.Sin(dayAngle)),
            (SignalIndex.DayOfWeekCos, MathF.Cos(dayAngle)),
            (SignalIndex.MonthSin, MathF.Sin(monthAngle)),
            (SignalIndex.MonthCos, MathF.Cos(monthAngle)),
            (SignalIndex.EventProximity, normalizedProximity)
        ];
    }

    private static float DaysToNearestEvent(DateTimeOffset now)
    {
        float min = float.MaxValue;

        foreach (var d in FomcDates2026)
        {
            float days = (float)Math.Abs((d - now).TotalDays);
            if (days < min) min = days;
        }
        foreach (var d in CpiDates2026)
        {
            float days = (float)Math.Abs((d - now).TotalDays);
            if (days < min) min = days;
        }

        return min;
    }
}
