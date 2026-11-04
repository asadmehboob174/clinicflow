using ClinicFlow.Api;
using ClinicFlow.BuildingBlocks.Messaging;
using ClinicFlow.BuildingBlocks.Persistence;
using ClinicFlow.BuildingBlocks.Web;
using ClinicFlow.Modules.Appointments;
using ClinicFlow.Modules.Appointments.Contracts;
using ClinicFlow.Modules.Doctors;
using ClinicFlow.Modules.Doctors.Contracts;
using ClinicFlow.Modules.Notifications;
using ClinicFlow.Modules.Patients;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

IModule[] modules = [new DoctorsModule(), new PatientsModule(), new AppointmentsModule(), new NotificationsModule()];

builder.Services.AddBuildingBlocks();
foreach (var module in modules)
{
    module.Register(builder.Services, builder.Configuration);
}

builder.AddClinicAuth();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var redis = builder.Configuration["Cache:Redis"];
if (string.IsNullOrEmpty(redis))
{
    builder.Services.AddDistributedMemoryCache();
}
else
{
    builder.Services.AddStackExchangeRedisCache(options => options.Configuration = redis);
}

var rabbitMq = builder.Configuration["Messaging:RabbitMq"];
if (string.IsNullOrEmpty(rabbitMq))
{
    builder.Services.AddSingleton<IEventBus, InMemoryEventBus>();
}
else
{
    builder.Services.AddSingleton<RabbitMqConnection>();
    builder.Services.AddSingleton<IEventBus, RabbitMqEventBus>();
    builder.Services.AddHostedService<RabbitMqConsumer>();
}

if (builder.Configuration.GetValue("Outbox:Enabled", true))
{
    builder.Services.AddHostedService<OutboxWorker>();
}

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("clinicflow-api"))
    .WithTracing(trace =>
    {
        trace.AddAspNetCoreInstrumentation();
        if (!string.IsNullOrEmpty(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            trace.AddOtlpExporter();
        }
    });

var app = builder.Build();

var registry = app.Services.GetRequiredService<IntegrationEventRegistry>();
registry.Scan(typeof(AppointmentBooked).Assembly);
registry.Scan(typeof(DoctorDeactivated).Assembly);

app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

foreach (var module in modules)
{
    module.MapEndpoints(app);
}

if (!EF.IsDesignTime && app.Configuration.GetValue("Database:MigrateOnStart", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    var initializers = scope.ServiceProvider.GetServices<IModuleInitializer>().ToList();

    foreach (var initializer in initializers)
    {
        await initializer.Migrate(CancellationToken.None);
    }

    if (app.Configuration.GetValue("Database:Seed", false))
    {
        foreach (var initializer in initializers)
        {
            await initializer.Seed(CancellationToken.None);
        }
    }
}

await app.RunAsync();

public partial class Program;
