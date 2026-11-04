using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace ClinicFlow.BuildingBlocks.Web;

public static class Roles
{
    public const string Admin = "admin";
    public const string Reception = "reception";
    public const string Patient = "patient";
}

public static class Policies
{
    public const string Admin = "admin";
    public const string Staff = "staff";
}

public interface ICurrentUser
{
    string? Email { get; }
    bool IsStaff { get; }
}

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public string? Email => (User?.FindFirstValue("email") ?? User?.FindFirstValue(ClaimTypes.Email))?.ToLowerInvariant();

    public bool IsStaff => User is { } user && (user.IsInRole(Roles.Admin) || user.IsInRole(Roles.Reception));
}
