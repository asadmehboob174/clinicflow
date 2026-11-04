using System.Reflection;
using ClinicFlow.BuildingBlocks.Cqrs;
using ClinicFlow.Modules.Appointments;
using ClinicFlow.Modules.Doctors;
using ClinicFlow.Modules.Notifications;
using ClinicFlow.Modules.Patients;

namespace ClinicFlow.ArchitectureTests;

/// <summary>A modular monolith only stays modular if the build fails when someone crosses a line.</summary>
public class ModuleBoundaryTests
{
    private static readonly Assembly[] Modules =
    [
        typeof(DoctorsModule).Assembly,
        typeof(PatientsModule).Assembly,
        typeof(AppointmentsModule).Assembly,
        typeof(NotificationsModule).Assembly,
    ];

    public static TheoryData<string> ModuleNames() => new(Modules.Select(m => m.GetName().Name!));

    private static Assembly Find(string name) => Modules.Single(m => m.GetName().Name == name);

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Module_only_uses_other_modules_through_contracts(string moduleName)
    {
        var forbidden = Find(moduleName).GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.StartsWith("ClinicFlow.Modules.") && !name.EndsWith(".Contracts"))
            .ToList();

        forbidden.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Handlers_are_internal_to_their_module(string moduleName)
    {
        var publicHandlers = Find(moduleName).GetTypes()
            .Where(t => t.IsPublic && t.GetInterfaces()
                .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)))
            .Select(t => t.Name)
            .ToList();

        publicHandlers.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("ClinicFlow.Modules.Doctors")]
    [InlineData("ClinicFlow.Modules.Appointments")]
    [InlineData("ClinicFlow.Modules.Patients")]
    public void Domain_does_not_know_about_the_database(string moduleName)
    {
        var domainTypes = Find(moduleName).GetTypes()
            .Where(t => t.Namespace?.EndsWith(".Domain") == true)
            .ToList();

        domainTypes.ShouldNotBeEmpty();

        var usesEf = domainTypes
            .SelectMany(t => t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .Select(f => f.FieldType)
                .Concat(t.GetProperties().Select(p => p.PropertyType)))
            .Where(type => type.Namespace?.StartsWith("Microsoft.EntityFrameworkCore") == true)
            .ToList();

        usesEf.ShouldBeEmpty();
    }
}
