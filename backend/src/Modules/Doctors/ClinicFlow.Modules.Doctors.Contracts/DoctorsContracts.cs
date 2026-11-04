using ClinicFlow.BuildingBlocks.Messaging;

namespace ClinicFlow.Modules.Doctors.Contracts;

public sealed record WorkingHoursDto(DayOfWeek Day, TimeOnly Start, TimeOnly End);

public sealed record DoctorSchedule(Guid DoctorId, string FullName, bool IsActive, IReadOnlyList<WorkingHoursDto> WorkingHours);

public sealed record TreatmentInfo(Guid TreatmentId, string Name, int DurationMinutes, decimal Price);

/// <summary>The only way other modules can read Doctors data. Runs in-process.</summary>
public interface IDoctorsModule
{
    Task<DoctorSchedule?> GetSchedule(Guid doctorId, CancellationToken ct);
    Task<TreatmentInfo?> GetTreatment(Guid treatmentId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, string>> GetDoctorNames(IEnumerable<Guid> doctorIds, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, string>> GetTreatmentNames(IEnumerable<Guid> treatmentIds, CancellationToken ct);
}

public sealed record DoctorDeactivated(Guid DoctorId) : IntegrationEvent;
