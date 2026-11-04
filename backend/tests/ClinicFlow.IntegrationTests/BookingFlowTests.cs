using System.Net;
using System.Net.Http.Json;
using ClinicFlow.Modules.Appointments.Features;
using ClinicFlow.Modules.Notifications;

namespace ClinicFlow.IntegrationTests;

[Collection(ApiCollection.Name)]
public class BookingFlowTests(ClinicApi api)
{
    private static readonly DateOnly NextWeek = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));

    private static object Booking(Guid patient, Guid doctor, Guid treatment, DateTime start) =>
        new { patientId = patient, doctorId = doctor, treatmentId = treatment, startsAtUtc = start };

    private static async Task<Guid> IdOf(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    [Fact]
    public async Task Patient_books_and_staff_get_a_notification()
    {
        var doctor = await api.CreateDoctor();
        var treatment = await api.CreateTreatment();
        var (patient, email) = await api.CreatePatient();
        var slot = (await api.Slots(doctor, treatment, NextWeek))[0];

        var id = await IdOf(await api.Patient(email).PostAsJsonAsync("/api/appointments", Booking(patient, doctor, treatment, slot)));
        await api.FlushOutbox();

        var notifications = await api.Reception().GetFromJsonAsync<List<NotificationDto>>("/api/notifications");
        notifications!.ShouldContain(n => n.AppointmentId == id && n.Title == "New booking");
    }

    [Fact]
    public async Task Booked_time_is_no_longer_offered_and_comes_back_after_cancel()
    {
        var doctor = await api.CreateDoctor();
        var treatment = await api.CreateTreatment();
        var (patient, email) = await api.CreatePatient();
        var slot = (await api.Slots(doctor, treatment, NextWeek))[0];

        var id = await IdOf(await api.Patient(email).PostAsJsonAsync("/api/appointments", Booking(patient, doctor, treatment, slot)));
        (await api.Slots(doctor, treatment, NextWeek)).ShouldNotContain(slot);

        var cancel = await api.Patient(email).PostAsJsonAsync($"/api/appointments/{id}/cancel", new { reason = "Changed plans" });
        cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await api.Slots(doctor, treatment, NextWeek)).ShouldContain(slot);
    }

    [Fact]
    public async Task Overlapping_booking_is_refused_even_when_start_times_differ()
    {
        var doctor = await api.CreateDoctor();
        var longTreatment = await api.CreateTreatment(60);
        var shortTreatment = await api.CreateTreatment(15);
        var (first, _) = await api.CreatePatient();
        var (second, _) = await api.CreatePatient();
        var slot = (await api.Slots(doctor, longTreatment, NextWeek))[0];

        await IdOf(await api.Reception().PostAsJsonAsync("/api/appointments", Booking(first, doctor, longTreatment, slot)));

        var overlap = await api.Reception().PostAsJsonAsync(
            "/api/appointments", Booking(second, doctor, shortTreatment, slot.AddMinutes(30)));

        overlap.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task When_many_patients_race_for_one_time_exactly_one_wins()
    {
        var doctor = await api.CreateDoctor();
        var treatment = await api.CreateTreatment();
        var slot = (await api.Slots(doctor, treatment, NextWeek))[0];

        var patients = new List<Guid>();
        for (var i = 0; i < 8; i++)
        {
            patients.Add((await api.CreatePatient()).Id);
        }

        var client = api.Reception();
        var responses = await Task.WhenAll(patients.Select(patient =>
            client.PostAsJsonAsync("/api/appointments", Booking(patient, doctor, treatment, slot))));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(7);
    }

    [Fact]
    public async Task Booking_outside_working_hours_is_refused()
    {
        var doctor = await api.CreateDoctor();
        var treatment = await api.CreateTreatment();
        var (patient, _) = await api.CreatePatient();
        var midnight = NextWeek.ToDateTime(new TimeOnly(23, 0), DateTimeKind.Utc);

        var response = await api.Reception().PostAsJsonAsync("/api/appointments", Booking(patient, doctor, treatment, midnight));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Deactivating_a_doctor_cancels_upcoming_visits_across_modules()
    {
        var doctor = await api.CreateDoctor();
        var treatment = await api.CreateTreatment();
        var (patient, email) = await api.CreatePatient();
        var slot = (await api.Slots(doctor, treatment, NextWeek))[0];
        var id = await IdOf(await api.Reception().PostAsJsonAsync("/api/appointments", Booking(patient, doctor, treatment, slot)));

        var deactivate = await api.Admin().PostAsync($"/api/doctors/{doctor}/deactivate", null);
        deactivate.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await api.FlushOutbox();

        var mine = await api.Patient(email).GetFromJsonAsync<List<AppointmentDto>>("/api/appointments/mine");
        mine!.Single(a => a.Id == id).Status.ShouldBe("Cancelled");

        var notifications = await api.Reception().GetFromJsonAsync<List<NotificationDto>>("/api/notifications");
        notifications!.ShouldContain(n => n.AppointmentId == id && n.Title == "Booking cancelled");
    }

    [Fact]
    public async Task Staff_day_schedule_shows_names_from_other_modules()
    {
        var doctor = await api.CreateDoctor("Dr. Schedule");
        var treatment = await api.CreateTreatment();
        var (patient, _) = await api.CreatePatient();
        var slot = (await api.Slots(doctor, treatment, NextWeek))[0];
        var id = await IdOf(await api.Reception().PostAsJsonAsync("/api/appointments", Booking(patient, doctor, treatment, slot)));

        var day = await api.Reception().GetFromJsonAsync<List<AppointmentDto>>($"/api/appointments/day?date={NextWeek:yyyy-MM-dd}");

        var row = day!.Single(a => a.Id == id);
        row.DoctorName.ShouldBe("Dr. Schedule");
        row.PatientName.ShouldBe("Test Patient");
    }
}
