using ClinicFlow.BuildingBlocks.Domain;

namespace ClinicFlow.Modules.Appointments.Domain;

public enum AppointmentStatus
{
    Booked,
    Confirmed,
    Completed,
    Cancelled,
}

public sealed record AppointmentBookedDomainEvent(Appointment Appointment) : IDomainEvent;
public sealed record AppointmentConfirmedDomainEvent(Appointment Appointment) : IDomainEvent;
public sealed record AppointmentCancelledDomainEvent(Appointment Appointment, string Reason) : IDomainEvent;

/// <summary>
/// One 15 minute piece of a doctor's time. A unique index on (DoctorId, StartsAtUtc) means two
/// overlapping appointments can never both be saved, even when two requests race each other.
/// </summary>
public sealed class SlotBlock
{
    private SlotBlock()
    {
    }

    public SlotBlock(Guid doctorId, DateTime startsAtUtc)
    {
        DoctorId = doctorId;
        StartsAtUtc = startsAtUtc;
    }

    public Guid DoctorId { get; private set; }
    public DateTime StartsAtUtc { get; private set; }
}

public sealed class Appointment : AggregateRoot<Guid>
{
    public const int BlockMinutes = 15;
    public static readonly TimeSpan PatientCancelNotice = TimeSpan.FromHours(2);

    private readonly List<SlotBlock> _blocks = [];

    private Appointment()
    {
    }

    public Guid PatientId { get; private set; }
    public Guid DoctorId { get; private set; }
    public Guid TreatmentId { get; private set; }
    public DateTime StartsAtUtc { get; private set; }
    public DateTime EndsAtUtc { get; private set; }
    public AppointmentStatus Status { get; private set; }
    public string? CancelReason { get; private set; }
    public IReadOnlyList<SlotBlock> Blocks => _blocks;

    public static Result<Appointment> Book(
        Guid patientId, Guid doctorId, Guid treatmentId, DateTime startsAtUtc, int durationMinutes, DateTime nowUtc)
    {
        if (startsAtUtc <= nowUtc)
        {
            return Error.Validation("appointment.in_past", "The start time must be in the future.");
        }

        if (startsAtUtc.Minute % BlockMinutes != 0 || startsAtUtc.Second != 0 || startsAtUtc.Millisecond != 0)
        {
            return Error.Validation("appointment.off_grid", $"Start times must be on a {BlockMinutes} minute step.");
        }

        var appointment = new Appointment
        {
            Id = Guid.CreateVersion7(),
            PatientId = patientId,
            DoctorId = doctorId,
            TreatmentId = treatmentId,
            StartsAtUtc = startsAtUtc,
            EndsAtUtc = startsAtUtc.AddMinutes(durationMinutes),
            Status = AppointmentStatus.Booked,
        };

        for (var start = startsAtUtc; start < appointment.EndsAtUtc; start = start.AddMinutes(BlockMinutes))
        {
            appointment._blocks.Add(new SlotBlock(doctorId, start));
        }

        appointment.Raise(new AppointmentBookedDomainEvent(appointment));
        return appointment;
    }

    public Result Confirm()
    {
        if (Status != AppointmentStatus.Booked)
        {
            return Error.Conflict("appointment.bad_status", $"A {Status} appointment cannot be confirmed.");
        }

        Status = AppointmentStatus.Confirmed;
        Raise(new AppointmentConfirmedDomainEvent(this));
        return Result.Success();
    }

    public Result Complete(DateTime nowUtc)
    {
        if (Status != AppointmentStatus.Confirmed)
        {
            return Error.Conflict("appointment.bad_status", $"A {Status} appointment cannot be completed.");
        }

        if (nowUtc < StartsAtUtc)
        {
            return Error.Conflict("appointment.not_started", "This appointment has not started yet.");
        }

        Status = AppointmentStatus.Completed;
        return Result.Success();
    }

    public Result Cancel(string reason, bool byStaff, DateTime nowUtc)
    {
        if (Status is AppointmentStatus.Cancelled or AppointmentStatus.Completed)
        {
            return Error.Conflict("appointment.bad_status", $"A {Status} appointment cannot be cancelled.");
        }

        if (!byStaff && StartsAtUtc - nowUtc < PatientCancelNotice)
        {
            return Error.Conflict("appointment.too_late", "Please call the clinic to cancel within 2 hours of your visit.");
        }

        Status = AppointmentStatus.Cancelled;
        CancelReason = reason;
        _blocks.Clear();
        Raise(new AppointmentCancelledDomainEvent(this, reason));
        return Result.Success();
    }
}
