using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using ClinicFlow.BuildingBlocks.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace ClinicFlow.Api;

public static class AuthSetup
{
    public static void AddClinicAuth(this WebApplicationBuilder builder)
    {
        var authority = builder.Configuration["Auth:Authority"];

        if (!string.IsNullOrEmpty(authority))
        {
            builder.Services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.Authority = authority;
                    options.Audience = builder.Configuration["Auth:Audience"];
                    options.RequireHttpsMetadata = builder.Configuration.GetValue("Auth:RequireHttps", true);
                    options.MapInboundClaims = false;
                    options.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;

                    // In Docker the API reaches Keycloak at a different host than the browser does.
                    if (builder.Configuration["Auth:MetadataAddress"] is { Length: > 0 } metadata)
                    {
                        options.MetadataAddress = metadata;
                        options.TokenValidationParameters.ValidIssuer = builder.Configuration["Auth:ValidIssuer"];
                    }

                    options.Events = new JwtBearerEvents
                    {
                        // Browsers cannot set headers on a WebSocket, so SignalR sends the token in the query.
                        OnMessageReceived = context =>
                        {
                            if (context.Request.Path.StartsWithSegments("/hubs"))
                            {
                                context.Token = context.Request.Query["access_token"];
                            }

                            return Task.CompletedTask;
                        },
                    };
                });
            builder.Services.AddTransient<IClaimsTransformation, KeycloakRolesTransformation>();
        }
        else if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
        {
            builder.Services
                .AddAuthentication(DevAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevAuthHandler.SchemeName, null);
        }
        else
        {
            throw new InvalidOperationException("Auth:Authority must be set outside Development.");
        }

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Admin, policy => policy.RequireRole(Roles.Admin))
            .AddPolicy(Policies.Staff, policy => policy.RequireRole(Roles.Admin, Roles.Reception));
    }
}

/// <summary>Keycloak puts roles inside the realm_access claim. ASP.NET wants one claim per role.</summary>
public sealed class KeycloakRolesTransformation : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var realmAccess = principal.FindFirstValue("realm_access");
        if (realmAccess is null || principal.Identity is not ClaimsIdentity identity || identity.HasClaim(c => c.Type == ClaimTypes.Role))
        {
            return Task.FromResult(principal);
        }

        using var json = JsonDocument.Parse(realmAccess);
        if (json.RootElement.TryGetProperty("roles", out var roles))
        {
            foreach (var role in roles.EnumerateArray())
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, role.GetString()!));
            }
        }

        return Task.FromResult(principal);
    }
}

/// <summary>
/// Local development only, so the app runs without Keycloak. A token like "dev:admin" or
/// "dev:patient:sara@example.com" signs you in. It is never registered outside Development and Testing.
/// </summary>
public sealed class DevAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Dev";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..]
            : Request.Query["access_token"].ToString();

        var parts = token.Split(':');
        if (parts is not ["dev", var role, ..])
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var email = parts.Length > 2 ? parts[2] : $"{role}@clinicflow.test";
        var identity = new ClaimsIdentity(
            [new Claim("email", email), new Claim(ClaimTypes.Name, email), new Claim(ClaimTypes.Role, role)],
            SchemeName);

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
