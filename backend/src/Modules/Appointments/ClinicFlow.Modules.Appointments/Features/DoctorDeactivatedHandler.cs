using ClinicFlow.BuildingBlocks.Messaging;
using ClinicFlow.BuildingBlocks.Time;
using ClinicFlow.Modules.Appointments.Domain;
using ClinicFlow.Modules.Appointments.Infrastructure;
using ClinicFlow.Modules.Doctors.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ClinicFlow.Modules.Appointments.Features;

/// <summary>Safe to run twice: cancelled appointments no longer match the filter.</summary>
internal sealed class DoctorDeactivatedHandler(AppointmentsDbContext db, ClinicClock clock)
    : IIntegrationEventHandler<DoctorDeactivated>
{
    public async Task Handle(DoctorDeactivated integrationEvent, CancellationToken ct)
    {
        var now = clock.UtcNow;

        var upcoming = await db.Appointments
            .Where(a => a.DoctorId == integrationEvent.DoctorId && a.StartsAtUtc > now)
            .Where(a => a.Status == AppointmentStatus.Booked || a.Status == AppointmentStatus.Confirmed)
            .ToListAsync(ct);

        foreach (var appointment in upcoming)
        {
            appointment.Cancel("The doctor is no longer available.", byStaff: true, now);
        }

        await db.SaveChangesAsync(ct);
    }
}
