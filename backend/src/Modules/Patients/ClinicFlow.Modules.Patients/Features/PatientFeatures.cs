using ClinicFlow.BuildingBlocks.Cqrs;
using ClinicFlow.BuildingBlocks.Domain;
using ClinicFlow.BuildingBlocks.Time;
using ClinicFlow.BuildingBlocks.Web;
using ClinicFlow.Modules.Patients.Domain;
using ClinicFlow.Modules.Patients.Infrastructure;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ClinicFlow.Modules.Patients.Features;

public sealed record PatientDto(Guid Id, string FullName, string Email, string Phone, DateTime RegisteredAtUtc);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

// Register

public sealed record RegisterPatient(string FullName, string Email, string Phone) : ICommand<Guid>;

internal sealed class RegisterPatientValidator : AbstractValidator<RegisterPatient>
{
    public RegisterPatientValidator()
    {
        RuleFor(c => c.FullName).NotEmpty().MaximumLength(120);
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(c => c.Phone).NotEmpty().MaximumLength(30).Matches(@"^\+?[0-9 ()-]{7,30}$");
    }
}

internal sealed class RegisterPatientHandler(PatientsDbContext db, ClinicClock clock, ICurrentUser user)
    : ICommandHandler<RegisterPatient, Guid>
{
    public async Task<Result<Guid>> Handle(RegisterPatient command, CancellationToken ct)
    {
        var email = Patient.NormalizeEmail(command.Email);

        if (!user.IsStaff && user.Email != email)
        {
            return Error.Forbidden("patient.not_your_email", "You can only register your own email.");
        }

        if (await db.Patients.AnyAsync(p => p.Email == email, ct))
        {
            return Error.Conflict("patient.email_taken", "A patient with this email already exists.");
        }

        var patient = Patient.Register(command.FullName, email, command.Phone, clock.UtcNow);
        db.Patients.Add(patient);
        await db.SaveChangesAsync(ct);
        return patient.Id;
    }
}

// Get one

public sealed record GetPatient(Guid PatientId) : IQuery<PatientDto>;

internal sealed class GetPatientHandler(PatientsDbContext db) : IQueryHandler<GetPatient, PatientDto>
{
    public async Task<Result<PatientDto>> Handle(GetPatient query, CancellationToken ct)
    {
        var patient = await db.Patients.AsNoTracking()
            .Where(p => p.Id == query.PatientId)
            .Select(p => new PatientDto(p.Id, p.FullName, p.Email, p.Phone, p.RegisteredAtUtc))
            .FirstOrDefaultAsync(ct);

        return patient is null ? Error.NotFound("patient.not_found", "Patient not found.") : patient;
    }
}

// Get me

public sealed record GetMyProfile : IQuery<PatientDto>;

internal sealed class GetMyProfileHandler(PatientsDbContext db, ICurrentUser user) : IQueryHandler<GetMyProfile, PatientDto>
{
    public async Task<Result<PatientDto>> Handle(GetMyProfile query, CancellationToken ct)
    {
        var patient = await db.Patients.AsNoTracking()
            .Where(p => p.Email == user.Email)
            .Select(p => new PatientDto(p.Id, p.FullName, p.Email, p.Phone, p.RegisteredAtUtc))
            .FirstOrDefaultAsync(ct);

        return patient is null ? Error.NotFound("patient.no_profile", "You have no patient profile yet.") : patient;
    }
}

// Search

public sealed record SearchPatients(string? Search, int Page = 1, int PageSize = 20) : IQuery<PagedResult<PatientDto>>;

internal sealed class SearchPatientsValidator : AbstractValidator<SearchPatients>
{
    public SearchPatientsValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
    }
}

internal sealed class SearchPatientsHandler(PatientsDbContext db) : IQueryHandler<SearchPatients, PagedResult<PatientDto>>
{
    public async Task<Result<PagedResult<PatientDto>>> Handle(SearchPatients query, CancellationToken ct)
    {
        var patients = db.Patients.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            patients = patients.Where(p => p.FullName.ToLower().Contains(term) || p.Email.Contains(term));
        }

        var total = await patients.CountAsync(ct);
        var items = await patients
            .OrderBy(p => p.FullName)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => new PatientDto(p.Id, p.FullName, p.Email, p.Phone, p.RegisteredAtUtc))
            .ToListAsync(ct);

        return new PagedResult<PatientDto>(items, total, query.Page, query.PageSize);
    }
}
