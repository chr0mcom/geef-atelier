namespace Geef.Atelier.Core.Domain.Dashboard;

/// <summary>Top-of-page greeting strip with streak and craft-mark stats.</summary>
public sealed record WelcomeStrip(
    string Username,
    int TodayCount,
    int StreakDays,
    double CraftMark,   // 0.0 – 100.0
    int TotalManuscripts);
