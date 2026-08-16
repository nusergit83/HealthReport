using System.Globalization;
using System.Xml;
using HealthReport.Core.Models;

namespace HealthReport.Core.Parsing;

/// <summary>
/// Parsea export.xml de Apple Health usando XmlReader en streaming
/// para mantener el consumo de memoria bajo incluso con archivos de cientos de MB.
/// </summary>
public sealed class AppleHealthXmlParser : IHealthParser
{
    private const int ProgressReportInterval = 5000;
    private const string DateOfBirthAttribute = "HKCharacteristicTypeIdentifierDateOfBirth";
    private const string HeightType = "HKQuantityTypeIdentifierHeight";
    private const string WeightType = "HKQuantityTypeIdentifierBodyMass";
    private const string SleepType = "HKCategoryTypeIdentifierSleepAnalysis";

    // Formatos de fecha que Apple Health usa según región y versión del export
    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd HH:mm:ss zzz",
        "yyyy-MM-dd HH:mm:ss Z",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd"
    ];

    public async Task<(UserProfile Profile, List<HealthRecord> Records, List<WorkoutRecord> Workouts)> ParseAsync(
        Stream xmlStream,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var records = new List<HealthRecord>(capacity: 50_000);
        var workouts = new List<WorkoutRecord>();
        UserProfile profile = new();
        int count = 0;

        var settings = new XmlReaderSettings
        {
            Async = true,
            IgnoreWhitespace = true,
            IgnoreComments = true,
            // No validar DTD/esquema — el export.xml de Apple no tiene DTD externo
            DtdProcessing = DtdProcessing.Ignore
        };

        using var reader = XmlReader.Create(xmlStream, settings);

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.NodeType != XmlNodeType.Element)
                continue;

            switch (reader.Name)
            {
                case "Me":
                    profile = ParseProfile(reader);
                    break;

                case "Record":
                    var record = ParseRecord(reader);
                    if (record is not null)
                        records.Add(record);
                    break;

                case "Workout":
                    var workout = ParseWorkout(reader);
                    if (workout is not null)
                        workouts.Add(workout);
                    break;
                // Ignorar silenciosamente: ActivitySummary, ClinicalRecord, Audiogram, etc.
            }

            count++;
            if (count % ProgressReportInterval == 0)
                progress?.Report(count);
        }

        progress?.Report(count);
        return (EnrichProfile(profile, records), records, workouts);
    }

    private static UserProfile ParseProfile(XmlReader reader)
    {
        DateOnly? dob = null;
        var dobStr = reader.GetAttribute(DateOfBirthAttribute) ?? reader.GetAttribute("DateOfBirth");
        if (!string.IsNullOrEmpty(dobStr) && DateOnly.TryParse(dobStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            dob = parsed;

        double? height = null;
        if (TryParseDouble(reader.GetAttribute("HeightInMeters"), out var h))
            height = h;

        double? weight = null;
        if (TryParseDouble(reader.GetAttribute("WeightInKilograms"), out var w))
            weight = w;

        return new UserProfile
        {
            DateOfBirth = dob,
            BiologicalSex = SanitizeSex(reader.GetAttribute("HKCharacteristicTypeIdentifierBiologicalSex")),
            HeightMeters = height,
            WeightKg = weight
        };
    }

    private static HealthRecord? ParseRecord(XmlReader reader)
    {
        var type = reader.GetAttribute("type");
        if (string.IsNullOrEmpty(type)) return null;

        var valueStr = reader.GetAttribute("value");
        var hasNumericValue = TryParseDouble(valueStr, out var value);
        if (!hasNumericValue && type != SleepType)
            return null;

        var startDate = ParseDate(reader.GetAttribute("startDate"));
        if (startDate == DateTime.MinValue) return null; // registro inválido

        return new HealthRecord
        {
            Type = type,
            SourceName = reader.GetAttribute("sourceName") ?? string.Empty,
            Unit = reader.GetAttribute("unit") ?? string.Empty,
            RawValue = valueStr ?? string.Empty,
            Value = value,
            StartDate = startDate,
            EndDate = ParseDate(reader.GetAttribute("endDate"))
        };
    }

    private static WorkoutRecord? ParseWorkout(XmlReader reader)
    {
        var activityType = reader.GetAttribute("workoutActivityType");
        if (string.IsNullOrEmpty(activityType)) return null;

        TryParseDouble(reader.GetAttribute("duration"), out var duration);
        TryParseDouble(reader.GetAttribute("totalEnergyBurned"), out var energy);
        TryParseDouble(reader.GetAttribute("totalDistance"), out var distance);

        var startDate = ParseDate(reader.GetAttribute("startDate"));
        if (startDate == DateTime.MinValue) return null;

        return new WorkoutRecord
        {
            ActivityType = activityType,
            DurationMinutes = duration,
            TotalEnergyKcal = energy,
            TotalDistanceKm = distance,
            StartDate = startDate,
            EndDate = ParseDate(reader.GetAttribute("endDate"))
        };
    }

    private static DateTime ParseDate(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr)) return DateTime.MinValue;

        // Apple Health usa formato: "2026-08-07 10:23:11 +0200"
        if (DateTimeOffset.TryParseExact(dateStr, DateFormats,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
            return dto.LocalDateTime;

        // Fallback: parse libre
        if (DateTimeOffset.TryParse(dateStr, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dto2))
            return dto2.LocalDateTime;

        return DateTime.MinValue;
    }

    private static bool TryParseDouble(string? s, out double value)
    {
        value = 0;
        if (string.IsNullOrEmpty(s)) return false;
        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private static UserProfile EnrichProfile(UserProfile profile, List<HealthRecord> records)
    {
        var latestHeight = records
            .Where(r => r.Type == HeightType)
            .OrderByDescending(r => r.StartDate)
            .Select(r => (double?)r.Value)
            .FirstOrDefault();

        var latestWeight = records
            .Where(r => r.Type == WeightType)
            .OrderByDescending(r => r.StartDate)
            .Select(r => (double?)r.Value)
            .FirstOrDefault();

        return new UserProfile
        {
            DateOfBirth = profile.DateOfBirth,
            BiologicalSex = profile.BiologicalSex,
            HeightMeters = latestHeight ?? profile.HeightMeters,
            WeightKg = latestWeight ?? profile.WeightKg
        };
    }

    private static string SanitizeSex(string? raw) => raw switch
    {
        "HKBiologicalSexMale" => "Masculino",
        "HKBiologicalSexFemale" => "Femenino",
        "HKBiologicalSexOther" => "Otro",
        _ => "No especificado"
    };
}
