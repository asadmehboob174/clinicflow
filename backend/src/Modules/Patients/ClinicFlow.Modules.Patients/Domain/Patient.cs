using ClinicFlow.BuildingBlocks.Domain;

namespace ClinicFlow.Modules.Patients.Domain;

public sealed class Patient : AggregateRoot<Guid>
{
    private Patient()
    {
    }

    public string FullName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public DateTime RegisteredAtUtc { get; private set; }

    public static Patient Register(string fullName, string email, string phone, DateTime nowUtc) => new()
    {
        Id = Guid.CreateVersion7(),
        FullName = fullName.Trim(),
        Email = NormalizeEmail(email),
        Phone = phone.Trim(),
        RegisteredAtUtc = nowUtc,
    };

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
