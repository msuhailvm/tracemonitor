using System.Security.Claims;
using TraceMonitor.Api.Auth;
using TraceMonitor.Api.Data;
using TraceMonitor.Api.Models;

namespace TraceMonitor.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth");

        group.MapPost("/login", async (
            LoginRequest request,
            TraceRepository repository,
            PasswordHasherService hasher,
            JwtTokenService jwt,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new { error = "Username and password are required." });
            }

            var user = await repository.GetUserByUsernameAsync(request.Username.Trim(), ct);
            if (user is null || !user.IsActive || !hasher.Verify(user.Username, user.PasswordHash, request.Password))
            {
                return Results.Unauthorized();
            }

            var (token, expiresAt) = jwt.CreateToken(user.Id, user.Username, user.Role);
            return Results.Ok(new LoginResponse
            {
                Token = token,
                ExpiresAt = expiresAt,
                Username = user.Username,
                Role = user.Role
            });
        }).AllowAnonymous();

        group.MapGet("/me", (ClaimsPrincipal user) =>
        {
            return Results.Ok(new
            {
                username = user.Identity?.Name,
                role = user.FindFirstValue(ClaimTypes.Role)
            });
        }).RequireAuthorization();
    }
}
