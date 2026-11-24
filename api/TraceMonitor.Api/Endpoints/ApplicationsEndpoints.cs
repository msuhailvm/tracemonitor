using System.Text.RegularExpressions;
using Npgsql;
using TraceMonitor.Api.Auth;
using TraceMonitor.Api.Data;
using TraceMonitor.Api.Models;

namespace TraceMonitor.Api.Endpoints;

public static partial class ApplicationsEndpoints
{
    [GeneratedRegex("[^a-z0-9-]+", RegexOptions.Compiled)]
    private static partial Regex SlugCleanupRegex();

    public static void MapApplicationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/applications").RequireAuthorization();

        group.MapGet("/", async (TraceRepository repository, CancellationToken ct) =>
        {
            var apps = await repository.GetApplicationsAsync(ct);
            return Results.Ok(apps);
        });

        group.MapGet("/health", async (TraceRepository repository, CancellationToken ct) =>
        {
            var health = await repository.GetApplicationsHealthAsync(ct);
            return Results.Ok(health);
        });

        group.MapPost("/", async (
            CreateApplicationRequest req,
            TraceRepository repository,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
            {
                return Results.BadRequest(new { error = "Application name is required." });
            }

            var slugSource = string.IsNullOrWhiteSpace(req.Slug) ? req.Name : req.Slug;
            var slug = SlugCleanupRegex().Replace(slugSource.Trim().ToLowerInvariant().Replace(' ', '-'), "").Trim('-');
            if (string.IsNullOrWhiteSpace(slug))
            {
                return Results.BadRequest(new { error = "Could not derive a valid slug." });
            }

            var environment = string.IsNullOrWhiteSpace(req.Environment) ? "Production" : req.Environment.Trim();
            var (plainKey, keyHash, prefix) = ApiKeyHelper.GenerateApiKey();

            try
            {
                var created = await repository.CreateApplicationAsync(req.Name.Trim(), slug, environment, keyHash, prefix, ct);
                return Results.Created($"/api/v1/applications/{created.Id}", new CreateApplicationResponse
                {
                    Application = created,
                    ApiKey = plainKey
                });
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return Results.Conflict(new { error = $"An application with slug '{slug}' already exists." });
            }
        });

        group.MapPost("/{id:guid}/rotate-key", async (
            Guid id,
            TraceRepository repository,
            ApiKeyValidator apiKeyValidator,
            CancellationToken ct) =>
        {
            var (plainKey, keyHash, prefix) = ApiKeyHelper.GenerateApiKey();
            var (updated, oldHash) = await repository.RotateApiKeyAsync(id, keyHash, prefix, ct);
            if (updated is null)
            {
                return Results.NotFound();
            }

            if (!string.IsNullOrWhiteSpace(oldHash))
            {
                apiKeyValidator.InvalidateHash(oldHash);
            }

            return Results.Ok(new CreateApplicationResponse
            {
                Application = updated,
                ApiKey = plainKey
            });
        });
    }
}
