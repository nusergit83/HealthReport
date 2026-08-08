namespace HealthReport.Core.Models;

public sealed class WorkoutRecord
{
    public string ActivityType { get; init; } = string.Empty;
    public double DurationMinutes { get; init; }
    public double TotalEnergyKcal { get; init; }
    public double TotalDistanceKm { get; init; }
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
}
