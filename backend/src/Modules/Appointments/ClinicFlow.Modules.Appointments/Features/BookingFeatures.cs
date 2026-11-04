using ClinicFlow.BuildingBlocks.Cqrs;
using ClinicFlow.BuildingBlocks.Domain;
using ClinicFlow.BuildingBlocks.Time;
using ClinicFlow.BuildingBlocks.Web;
using ClinicFlow.Modules.Appointments.Domain;
using ClinicFlow.Modules.Appointments.Infrastructure;
using ClinicFlow.Modules.Doctors.Contracts;
using ClinicFlow.Modules.Patients.Contracts;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ClinicFlow.Modules.Appointments.Features;

// Free slots

public sealed record GetAvailableSlots(Guid DoctorId, Guid TreatmentId, DateOnly Date) : IQuery<IReadOnlyList<DateTime>>;

internal sealed class GetAvailableSlotsHandler(AppointmentsDbContext db, IDoctorsModule doctors, ClinicClock clock)
    : IQueryHandler<GetAvailableSlots, IReadOnlyList<DateTime>>
{
    public async Task<Result<IReadOnlyList<DateTime>>> Handle(GetAvailableSlots query, CancellationToken ct)
    {
        var schedule = await doctors.GetSchedule(query.DoctorId, ct);
        if (schedule is null)
        {
            return Error.NotFound("doctor.not_found", "Doctor not found.");
        }

        var treatment = await doctors.GetTreatment(query.TreatmentId, ct);
        if (treatment is null)
        {
            return Error.NotFound("treatment.not_found", "Treatment not found.");
        }

        if (!schedule.IsActive)
        {
            return Array.Empty<DateTime>();
        }

        var shifts = schedule.WorkingHours
            .Where(h => h.Day == query.Date.DayOfWeek)
            .Select(h => new Shift(clock.ToUtc(query.Date, h.Start), clock.ToUtc(query.Date, h.End)))
            .ToList();

        if (shifts.Count == 0)
        {
            return Array.Empty<DateTime>();
        }

        var from = shifts.Min(s => s.StartUtc);
        var to = shifts.Max(s => s.EndUtc);

        var busy = await db.Appointments.AsNoTracking()
            .Where(a => a.DoctorId == query.DoctorId && a.StartsAtUtc < to && a.EndsAtUtc > from)
            .SelectMany(a => a.Blocks)
            .Select(b => b.StartsAtUtc)
            .ToListAsync(ct);

        var slots = SlotFinder.Find(shifts, busy.Select(AsUtc).ToHashSet(), treatment.DurationMinutes, clock.UtcNow);
        return Result<IReadOnlyList<DateTime>>.From(slots);
    }

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}

// Book

public sealed record BookAppointment(Guid PatientId, Guid DoctorId, Guid TreatmentId, DateTime StartsAtUtc) : ICommand<Guid>;

internal sealed class BookAppointmentValidator : AbstractValidator<BookAppointment>
{
    public BookAppointmentValidator()
    {
        RuleFor(c => c.PatientId).NotEmpty();
        RuleFor(c => c.DoctorId).NotEmpty();
        RuleFor(c => c.TreatmentId).NotEmpty();
        RuleFor(c => c.StartsAtUtc).NotEmpty();
    }
}

internal sealed class BookAppointmentHandler(
    AppointmentsDbContext db,
    IDoctorsModule doctors,
    IPatientsModule patients,
    ICurrentUser user,
    ClinicClock clock) : ICommandHandler<BookAppointment, Guid>
{
    public async Task<Result<Guid>> Handle(BookAppointment command, CancellationToken ct)
    {
        var patient = await patients.GetContact(command.PatientId, ct);
        if (patient is null)
        {
            return Error.NotFound("patient.not_found", "Patient not found.");
        }

        if (!user.IsStaff && patient.Email != user.Email)
        {
            return Error.Forbidden("appointment.not_yours", "You can only book for yourself.");
        }

        var schedule = await doctors.GetSchedule(command.DoctorId, ct);
        if (schedule is not { IsActive: true })
        {
            return Error.NotFound("doctor.not_found", "Doctor not found.");
        }

        var treatment = await doctors.GetTreatment(command.TreatmentId, ct);
        if (treatment is null)
        {
            return Error.NotFound("treatment.not_found", "Treatment not found.");
        }

        var startsAtUtc = command.StartsAtUtc.ToUniversalTime();
        if (!IsInsideShift(schedule, startsAtUtc, treatment.DurationMinutes))
        {
            return Error.Validation("appointment.outside_hours", "The doctor does not work at this time.");
        }

        var booked = Appointment.Book(
            command.PatientId, command.DoctorId, command.TreatmentId, startsAtUtc, treatment.DurationMinutes, clock.UtcNow);

        if (!booked.IsSuccess)
        {
            return booked.Error!;
        }

        db.Appointments.Add(booked.Value);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The unique index on SlotBlocks refused it. Someone else got this time first.
            return Error.Conflict("appointment.slot_taken", "This time was just taken. Please pick another.");
        }

        return booked.Value.Id;
    }

    private bool IsInsideShift(DoctorSchedule schedule, DateTime startsAtUtc, int durationMinutes)
    {
        var endsAtUtc = startsAtUtc.AddMinutes(durationMinutes);

        // A UTC time can fall on the next or previous local day, so check around it.
        var date = DateOnly.FromDateTime(startsAtUtc);
        var days = new[] { date.AddDays(-1), date, date.AddDays(1) };

        return days.Any(day => schedule.WorkingHours
            .Where(h => h.Day == day.DayOfWeek)
            .Any(h => clock.ToUtc(day, h.Start) <= startsAtUtc && endsAtUtc <= clock.ToUtc(day, h.End)));
    }
}

// Status changes

public sealed record ConfirmAppointment(Guid AppointmentId) : ICommand;

internal sealed class ConfirmAppointmentHandler(AppointmentsDbContext db) : ICommandHandler<ConfirmAppointment>
{
    public async Task<Result> Handle(ConfirmAppointment command, CancellationToken ct)
    {
        var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == command.AppointmentId, ct);
        if (appointment is null)
        {
            return AppointmentErrors.NotFound;
        }

        var result = appointment.Confirm();
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(ct);
        }

        return result;
    }
}

public sealed record CompleteAppointment(Guid AppointmentId) : ICommand;

internal sealed class CompleteAppointmentHandler(AppointmentsDbContext db, ClinicClock clock) : ICommandHandler<CompleteAppointment>
{
    public async Task<Result> Handle(CompleteAppointment command, CancellationToken ct)
    {
        var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == command.AppointmentId, ct);
        if (appointment is null)
        {
            return AppointmentErrors.NotFound;
        }

        var result = appointment.Complete(clock.UtcNow);
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(ct);
        }

        return result;
    }
}

public sealed record CancelAppointment(Guid AppointmentId, string Reason) : ICommand;

internal sealed class CancelAppointmentValidator : AbstractValidator<CancelAppointment>
{
    public CancelAppointmentValidator() => RuleFor(c => c.Reason).NotEmpty().MaximumLength(300);
}

internal sealed class CancelAppointmentHandler(
    AppointmentsDbContext db, IPatientsModule patients, ICurrentUser user, ClinicClock clock)
    : ICommandHandler<CancelAppointment>
{
    public async Task<Result> Handle(CancelAppointment command, CancellationToken ct)
    {
        var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == command.AppointmentId, ct);
        if (appointment is null)
        {
            return AppointmentErrors.NotFound;
        }

        if (!user.IsStaff)
        {
            var patient = await patients.GetContact(appointment.PatientId, ct);
            if (patient?.Email != user.Email)
            {
                return AppointmentErrors.NotFound;
            }
        }

        var result = appointment.Cancel(command.Reason, user.IsStaff, clock.UtcNow);
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(ct);
        }

        return result;
    }
}

internal static class AppointmentErrors
{
    public static readonly Error NotFound = Error.NotFound("appointment.not_found", "Appointment not found.");
}
