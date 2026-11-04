namespace ClinicFlow.Modules.Patients.Contracts;

public sealed record PatientContact(Guid PatientId, string FullName, string Email, string Phone);

public interface IPatientsModule
{
    Task<PatientContact?> GetContact(Guid patientId, CancellationToken ct);
    Task<PatientContact?> FindByEmail(string email, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, PatientContact>> GetContacts(IEnumerable<Guid> patientIds, CancellationToken ct);
}
