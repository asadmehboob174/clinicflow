using ClinicFlow.BuildingBlocks.Cqrs;
using ClinicFlow.BuildingBlocks.Domain;
using ClinicFlow.BuildingBlocks.Time;
using ClinicFlow.BuildingBlocks.Web;
using ClinicFlow.Modules.Appointments.Domain;
using ClinicFlow.Modules.Appointments.Infrastructure;
using ClinicFlow.Modules.Doctors.Contracts;
using ClinicFlow.Modules.Patients.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ClinicFlow.Modules.Appointments.Features;

public sealed record AppointmentDto(
    Guid Id,
    Guid PatientId,
    string PatientName,
    Guid DoctorId,
    string DoctorName,
    string TreatmentName,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    string Status,
    string? CancelReason);

public sealed record DayStats(int Total, int Booked, int Confirmed, int Completed, int Cancelled);

internal sealed class AppointmentReader(IDoctorsModule doctors, IPatientsModule patients)
{
    /// <summary>Adds names from other modules with one batch call each, not one call per row.</summary>
    public async Task<IReadOnlyList<AppointmentDto>> ToDtos(IReadOnlyList<Appointment> appointments, CancellationToken ct)
    {
        var patientContacts = await patients.GetContacts(appointments.Select(a => a.PatientId), ct);
        var doctorNames = await doctors.GetDoctorNames(appointments.Select(a => a.DoctorId), ct);
        var treatmentNames = await doctors.GetTreatmentNames(appointments.Select(a => a.TreatmentId), ct);

        return appointments
            .Select(a => new AppointmentDto(
                a.Id,
                a.PatientId,
                patientContacts.GetValueOrDefault(a.PatientId)?.FullName ?? "Unknown",
                a.DoctorId,
                doctorNames.GetValueOrDefault(a.DoctorId, "Unknown"),
                treatmentNames.GetValueOrDefault(a.TreatmentId, "Unknown"),
                DateTime.SpecifyKind(a.StartsAtUtc, DateTimeKind.Utc),
                DateTime.SpecifyKind(a.EndsAtUtc, DateTimeKind.Utc),
                a.Status.ToString(),
                a.CancelReason))
            .ToList();
    }
}

// Day schedule for reception

public sealed record GetDaySchedule(DateOnly Date) : IQuery<IReadOnlyList<AppointmentDto>>;

internal sealed class GetDayScheduleHandler(AppointmentsDbContext db, AppointmentReader reader, ClinicClock clock)
    : IQueryHandler<GetDaySchedule, IReadOnlyList<AppointmentDto>>
{
    public async Task<Result<IReadOnlyList<AppointmentDto>>> Handle(GetDaySchedule query, CancellationToken ct)
    {
        var from = clock.ToUtc(query.Date, TimeOnly.MinValue);
        var to = clock.ToUtc(query.Date.AddDays(1), TimeOnly.MinValue);

        var appointments = await db.Appointments.AsNoTracking()
            .Where(a => a.StartsAtUtc >= from && a.StartsAtUtc < to)
            .OrderBy(a => a.StartsAtUtc)
            .ToListAsync(ct);

        return Result<IReadOnlyList<AppointmentDto>>.From(await reader.ToDtos(appointments, ct));
    }
}

// Dashboard numbers

public sealed record GetDayStats(DateOnly Date) : IQuery<DayStats>;

internal sealed class GetDayStatsHandler(AppointmentsDbContext db, ClinicClock clock) : IQueryHandler<GetDayStats, DayStats>
{
    public async Task<Result<DayStats>> Handle(GetDayStats query, CancellationToken ct)
    {
        var from = clock.ToUtc(query.Date, TimeOnly.MinValue);
        var to = clock.ToUtc(query.Date.AddDays(1), TimeOnly.MinValue);

        var counts = await db.Appointments.AsNoTracking()
            .Where(a => a.StartsAtUtc >= from && a.StartsAtUtc < to)
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, ct);

        int Count(AppointmentStatus status) => counts.GetValueOrDefault(status);

        return new DayStats(
            counts.Values.Sum(),
            Count(AppointmentStatus.Booked),
            Count(AppointmentStatus.Confirmed),
            Count(AppointmentStatus.Completed),
            Count(AppointmentStatus.Cancelled));
    }
}

// Patient portal

public sealed record GetMyAppointments : IQuery<IReadOnlyList<AppointmentDto>>;

internal sealed class GetMyAppointmentsHandler(
    AppointmentsDbContext db, AppointmentReader reader, IPatientsModule patients, ICurrentUser user)
    : IQueryHandler<GetMyAppointments, IReadOnlyList<AppointmentDto>>
{
    public async Task<Result<IReadOnlyList<AppointmentDto>>> Handle(GetMyAppointments query, CancellationToken ct)
    {
        var patient = user.Email is null ? null : await patients.FindByEmail(user.Email, ct);
        if (patient is null)
        {
            return Error.NotFound("patient.no_profile", "You have no patient profile yet.");
        }

        var appointments = await db.Appointments.AsNoTracking()
            .Where(a => a.PatientId == patient.PatientId)
            .OrderByDescending(a => a.StartsAtUtc)
            .Take(50)
            .ToListAsync(ct);

        return Result<IReadOnlyList<AppointmentDto>>.From(await reader.ToDtos(appointments, ct));
    }
}
