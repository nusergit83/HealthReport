namespace HealthReport.Core.Models;

/// <summary>
/// Perfil demográfico extraído del elemento &lt;Me&gt; del export.xml.
/// </summary>
public sealed class UserProfile
{
    public DateOnly? DateOfBirth { get; init; }
    public string BiologicalSex { get; init; } = string.Empty;
    public double? HeightMeters { get; init; }
    public double? WeightKg { get; init; }

    public int? AgeYears => DateOfBirth.HasValue
        ? (int)((DateTime.Today - DateOfBirth.Value.ToDateTime(TimeOnly.MinValue)).TotalDays / 365.25)
        : null;

    public double? Bmi => HeightMeters is > 0 && WeightKg.HasValue
        ? Math.Round(WeightKg.Value / (HeightMeters.Value * HeightMeters.Value), 1)
        : null;
}
