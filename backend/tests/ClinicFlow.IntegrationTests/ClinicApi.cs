using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClinicFlow.BuildingBlocks.Persistence;
using ClinicFlow.Modules.Appointments.Infrastructure;
using ClinicFlow.Modules.Doctors.Infrastructure;
using ClinicFlow.Modules.Notifications;
using ClinicFlow.Modules.Patients.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicFlow.IntegrationTests;

/// <summary>The real app and the real HTTP pipeline, with one SQLite file per module and the in-memory bus.</summary>
public sealed class ClinicApi : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "clinicflow-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_folder);
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={Path.Combine(_folder, "{schema}.db")}");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("Outbox:Enabled", "false");
    }

    public async Task InitializeAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DoctorsDbContext>().Database.EnsureCreatedAsync();
        await scope.ServiceProvider.GetRequiredService<PatientsDbContext>().Database.EnsureCreatedAsync();
        await scope.ServiceProvider.GetRequiredService<AppointmentsDbContext>().Database.EnsureCreatedAsync();
        await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.EnsureCreatedAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    public HttpClient As(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public HttpClient Admin() => As("dev:admin");
    public HttpClient Reception() => As("dev:reception");
    public HttpClient Patient(string email) => As($"dev:patient:{email}");

    /// <summary>Publishes until nothing is left, because one event can cause another.</summary>
    public async Task FlushOutbox()
    {
        while (true)
        {
            await using var scope = Services.CreateAsyncScope();
            var published = 0;
            foreach (var publisher in scope.ServiceProvider.GetServices<IOutboxPublisher>())
            {
                published += await publisher.PublishPending(CancellationToken.None);
            }

            if (published == 0)
            {
                return;
            }
        }
    }

    public async Task<Guid> CreateDoctor(string name = "Dr. Test")
    {
        var response = await Admin().PostAsJsonAsync("/api/doctors", new { fullName = name, specialty = "General" });
        response.EnsureSuccessStatusCode();
        var id = await response.Content.ReadFromJsonAsync<Guid>();

        var days = Enum.GetValues<DayOfWeek>().Select(day => new { day, start = "09:00:00", end = "17:00:00" });
        var hours = await Admin().PutAsJsonAsync($"/api/doctors/{id}/working-hours", new { hours = days });
        hours.EnsureSuccessStatusCode();
        return id;
    }

    public async Task<Guid> CreateTreatment(int minutes = 30)
    {
        var response = await Admin().PostAsJsonAsync(
            "/api/treatments", new { name = $"Treatment {Guid.NewGuid():N}", durationMinutes = minutes, price = 50 });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    public async Task<(Guid Id, string Email)> CreatePatient()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        var response = await Reception().PostAsJsonAsync(
            "/api/patients", new { fullName = "Test Patient", email, phone = "+971 50 000 0000" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Guid>(), email);
    }

    public async Task<List<DateTime>> Slots(Guid doctorId, Guid treatmentId, DateOnly date)
    {
        var url = $"/api/appointments/slots?doctorId={doctorId}&treatmentId={treatmentId}&date={date:yyyy-MM-dd}";
        return (await CreateClient().GetFromJsonAsync<List<DateTime>>(url))!;
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ClinicApi>
{
    public const string Name = "api";
}
