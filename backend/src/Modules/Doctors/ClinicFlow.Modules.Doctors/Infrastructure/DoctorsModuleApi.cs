using ClinicFlow.Modules.Doctors.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ClinicFlow.Modules.Doctors.Infrastructure;

internal sealed class DoctorsModuleApi(DoctorsDbContext db) : IDoctorsModule
{
    public async Task<DoctorSchedule?> GetSchedule(Guid doctorId, CancellationToken ct)
    {
        var doctor = await db.Doctors.AsNoTracking().FirstOrDefaultAsync(d => d.Id == doctorId, ct);
        return doctor is null
            ? null
            : new DoctorSchedule(
                doctor.Id,
                doctor.FullName,
                doctor.IsActive,
                doctor.WorkingHours.Select(h => new WorkingHoursDto(h.Day, h.Start, h.End)).ToList());
    }

    public async Task<TreatmentInfo?> GetTreatment(Guid treatmentId, CancellationToken ct)
    {
        var treatment = await db.Treatments.AsNoTracking().FirstOrDefaultAsync(t => t.Id == treatmentId, ct);
        return treatment is null
            ? null
            : new TreatmentInfo(treatment.Id, treatment.Name, treatment.DurationMinutes, treatment.Price);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetDoctorNames(IEnumerable<Guid> doctorIds, CancellationToken ct)
    {
        var ids = doctorIds.Distinct().ToList();
        return await db.Doctors.Where(d => ids.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.FullName, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetTreatmentNames(IEnumerable<Guid> treatmentIds, CancellationToken ct)
    {
        var ids = treatmentIds.Distinct().ToList();
        return await db.Treatments.Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
    }
}
