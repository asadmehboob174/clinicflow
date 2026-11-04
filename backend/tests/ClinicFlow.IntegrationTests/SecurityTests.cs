using System.Net;
using System.Net.Http.Json;

namespace ClinicFlow.IntegrationTests;

[Collection(ApiCollection.Name)]
public class SecurityTests(ClinicApi api)
{
    private static readonly DateOnly NextWeek = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));

    [Theory]
    [InlineData("/api/patients")]
    [InlineData("/api/notifications")]
    [InlineData("/api/appointments/mine")]
    public async Task Private_endpoints_need_a_login(string url)
    {
        var response = await api.CreateClient().GetAsync(url);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/api/patients")]
    [InlineData("/api/notifications")]
    public async Task Patients_cannot_open_staff_endpoints(string url)
    {
        var response = await api.Patient("someone@example.com").GetAsync(url);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reception_cannot_create_doctors()
    {
        var response = await api.Reception().PostAsJsonAsync("/api/doctors", new { fullName = "Dr. No", specialty = "None" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Patient_cannot_book_for_someone_else()
    {
        var doctor = await api.CreateDoctor();
        var treatment = await api.CreateTreatment();
        var (victim, _) = await api.CreatePatient();
        var (_, attackerEmail) = await api.CreatePatient();
        var slot = (await api.Slots(doctor, treatment, NextWeek))[0];

        var response = await api.Patient(attackerEmail).PostAsJsonAsync(
            "/api/appointments", new { patientId = victim, doctorId = doctor, treatmentId = treatment, startsAtUtc = slot });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Patient_cannot_cancel_someone_elses_visit()
    {
        var doctor = await api.CreateDoctor();
        var treatment = await api.CreateTreatment();
        var (patient, _) = await api.CreatePatient();
        var (_, otherEmail) = await api.CreatePatient();
        var slot = (await api.Slots(doctor, treatment, NextWeek))[0];
        var booked = await api.Reception().PostAsJsonAsync(
            "/api/appointments", new { patientId = patient, doctorId = doctor, treatmentId = treatment, startsAtUtc = slot });
        var id = await booked.Content.ReadFromJsonAsync<Guid>();

        var response = await api.Patient(otherEmail).PostAsJsonAsync($"/api/appointments/{id}/cancel", new { reason = "Not mine" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Bad_input_returns_field_errors()
    {
        var response = await api.Reception().PostAsJsonAsync(
            "/api/patients", new { fullName = "", email = "not-an-email", phone = "x" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("errors");
        body.ShouldContain("Email");
    }

    [Fact]
    public async Task Duplicate_email_is_a_conflict()
    {
        var (_, email) = await api.CreatePatient();

        var response = await api.Reception().PostAsJsonAsync(
            "/api/patients", new { fullName = "Copy", email, phone = "+971 50 000 0000" });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Health_endpoints_answer()
    {
        (await api.CreateClient().GetAsync("/health/live")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await api.CreateClient().GetAsync("/health/ready")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
