using ClinicFlow.BuildingBlocks.Messaging;

namespace ClinicFlow.Modules.Appointments.Contracts;

public sealed record AppointmentBooked(Guid AppointmentId, Guid PatientId, Guid DoctorId, DateTime StartsAtUtc) : IntegrationEvent;

public sealed record AppointmentConfirmed(Guid AppointmentId, Guid PatientId, Guid DoctorId, DateTime StartsAtUtc) : IntegrationEvent;

public sealed record AppointmentCancelled(Guid AppointmentId, Guid PatientId, Guid DoctorId, DateTime StartsAtUtc, string Reason) : IntegrationEvent;
