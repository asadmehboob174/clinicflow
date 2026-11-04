using Microsoft.Extensions.Configuration;

namespace ClinicFlow.BuildingBlocks.Time;

/// <summary>Working hours are clinic local time. The database stores UTC.</summary>
public sealed class ClinicClock(TimeProvider time, IConfiguration configuration)
{
    private readonly TimeZoneInfo _zone =
        TimeZoneInfo.FindSystemTimeZoneById(configuration["Clinic:TimeZone"] ?? "Asia/Dubai");

    public DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _zone));

    public DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zone);

    public DateTime ToUtc(DateOnly date, TimeOnly localTime) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(localTime, DateTimeKind.Unspecified), _zone);
}
