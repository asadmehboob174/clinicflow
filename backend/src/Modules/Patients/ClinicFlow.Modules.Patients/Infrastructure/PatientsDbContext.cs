using ClinicFlow.BuildingBlocks.Persistence;
using ClinicFlow.Modules.Patients.Contracts;
using ClinicFlow.Modules.Patients.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicFlow.Modules.Patients.Infrastructure;

public sealed class PatientsDbContext(DbContextOptions<PatientsDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "patients";

    protected override string Schema => SchemaName;

    public DbSet<Patient> Patients => Set<Patient>();
}

internal sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> patient)
    {
        patient.ToTable("Patients");
        patient.HasKey(p => p.Id);
        patient.Property(p => p.FullName).HasMaxLength(120);
        patient.Property(p => p.Email).HasMaxLength(200);
        patient.Property(p => p.Phone).HasMaxLength(30);
        patient.HasIndex(p => p.Email).IsUnique();
        patient.Ignore(p => p.DomainEvents);
    }
}

internal sealed class PatientsModuleApi(PatientsDbContext db) : IPatientsModule
{
    public async Task<PatientContact?> GetContact(Guid patientId, CancellationToken ct) =>
        await db.Patients.AsNoTracking()
            .Where(p => p.Id == patientId)
            .Select(p => new PatientContact(p.Id, p.FullName, p.Email, p.Phone))
            .FirstOrDefaultAsync(ct);

    public async Task<PatientContact?> FindByEmail(string email, CancellationToken ct)
    {
        var normalized = Patient.NormalizeEmail(email);
        return await db.Patients.AsNoTracking()
            .Where(p => p.Email == normalized)
            .Select(p => new PatientContact(p.Id, p.FullName, p.Email, p.Phone))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, PatientContact>> GetContacts(IEnumerable<Guid> patientIds, CancellationToken ct)
    {
        var ids = patientIds.Distinct().ToList();
        return await db.Patients.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new PatientContact(p.Id, p.FullName, p.Email, p.Phone))
            .ToDictionaryAsync(p => p.PatientId, ct);
    }
}
