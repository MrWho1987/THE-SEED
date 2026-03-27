using Seed.Market.Signals;

namespace Seed.Market.Indicators;

/// <summary>
/// Cyclical time encoding using sin/cos for continuous temporal features.
/// Also tracks proximity to major economic events (FOMC, CPI) across 2018-2026.
/// </summary>
public static class TimeEncoding
{
    private static readonly DateTimeOffset[] FomcDates = BuildFomcDates();
    private static readonly DateTimeOffset[] CpiDates = BuildCpiDates();

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

        foreach (var d in FomcDates)
        {
            float days = (float)Math.Abs((d - now).TotalDays);
            if (days < min) min = days;
        }
        foreach (var d in CpiDates)
        {
            float days = (float)Math.Abs((d - now).TotalDays);
            if (days < min) min = days;
        }

        return min;
    }

    /// <summary>
    /// FOMC decision dates 2018-2026 (month, day pairs per year).
    /// </summary>
    private static DateTimeOffset[] BuildFomcDates()
    {
        var dates = new List<DateTimeOffset>();
        int[][] schedule =
        [
            [2018, 1,31, 3,21, 5,2,  6,13, 8,1,  9,26, 11,8, 12,19],
            [2019, 1,30, 3,20, 5,1,  6,19, 7,31, 9,18, 10,30, 12,11],
            [2020, 1,29, 3,3,  3,15, 4,29, 6,10, 7,29, 9,16, 11,5, 12,16],
            [2021, 1,27, 3,17, 4,28, 6,16, 7,28, 9,22, 11,3, 12,15],
            [2022, 1,26, 3,16, 5,4,  6,15, 7,27, 9,21, 11,2, 12,14],
            [2023, 2,1,  3,22, 5,3,  6,14, 7,26, 9,20, 11,1, 12,13],
            [2024, 1,31, 3,20, 5,1,  6,12, 7,31, 9,18, 11,7, 12,18],
            [2025, 1,29, 3,19, 5,7,  6,18, 7,30, 9,17, 10,29, 12,17],
            [2026, 1,29, 3,18, 5,6,  6,17, 7,29, 9,16, 11,4, 12,16],
        ];

        foreach (var row in schedule)
        {
            int year = row[0];
            for (int i = 1; i < row.Length; i += 2)
                dates.Add(new DateTimeOffset(year, row[i], row[i + 1], 18, 0, 0, TimeSpan.Zero));
        }

        return dates.ToArray();
    }

    /// <summary>
    /// CPI release dates 2018-2026 approximated to second Wednesday of each month,
    /// 8:30 AM ET (13:30 UTC, or 12:30 during DST).
    /// </summary>
    private static DateTimeOffset[] BuildCpiDates()
    {
        var dates = new List<DateTimeOffset>();
        for (int year = 2018; year <= 2026; year++)
        {
            for (int month = 1; month <= 12; month++)
            {
                var first = new DateTimeOffset(year, month, 1, 13, 30, 0, TimeSpan.Zero);
                int daysToWed = ((int)DayOfWeek.Wednesday - (int)first.DayOfWeek + 7) % 7;
                var secondWed = first.AddDays(daysToWed + 7);
                dates.Add(secondWed);
            }
        }

        return dates.ToArray();
    }
}
