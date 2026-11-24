using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using TraceMonitor.Api.Auth;
using TraceMonitor.Api.Configuration;
using TraceMonitor.Api.Data;
using TraceMonitor.Api.Endpoints;
using TraceMonitor.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Configuration options
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<SeedAdminOptions>(builder.Configuration.GetSection(SeedAdminOptions.SectionName));
builder.Services.Configure<RetentionOptions>(builder.Configuration.GetSection(RetentionOptions.SectionName));
builder.Services.Configure<CorsOptions>(builder.Configuration.GetSection(CorsOptions.SectionName));

// Core services
builder.Services.AddMemoryCache();
builder.Services.AddRequestDecompression();
builder.Services.AddSingleton<DbConnectionFactory>();
builder.Services.AddSingleton<TraceRepository>();
builder.Services.AddSingleton<PasswordHasherService>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<ApiKeyValidator>();
builder.Services.AddHostedService<LogRetentionBackgroundService>();

// Forwarded headers for Cloudflare / SharkASP.Net reverse proxy
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// CORS for Netlify UI & local Vite dev server
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5190"];
builder.Services.AddCors(options =>
{
    options.AddPolicy("UiCors", policy =>
    {
        policy.WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// JWT Bearer authentication
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwtOptions.SecretKey) || jwtOptions.SecretKey.Length < 32)
{
    throw new InvalidOperationException("Jwt:SecretKey must be configured and at least 32 characters long.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseCors("UiCors");
app.UseRequestDecompression();

if (builder.Configuration.GetValue<bool>("UseHttpsRedirection"))
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow })).AllowAnonymous();

// API endpoints
app.MapAuthEndpoints();
app.MapIngestEndpoints();
app.MapLogsEndpoints();
app.MapApplicationsEndpoints();

// Bootstrap default admin user if users table is empty
try
{
    var seed = builder.Configuration.GetSection(SeedAdminOptions.SectionName).Get<SeedAdminOptions>() ?? new SeedAdminOptions();
    var repo = app.Services.GetRequiredService<TraceRepository>();
    var hasher = app.Services.GetRequiredService<PasswordHasherService>();
    await repo.EnsureSeedAdminAsync(seed.Username, hasher.Hash(seed.Username, seed.Password));
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Could not run initial admin seed check (verify PostgreSQL connection and schema scripts).");
}

app.Run();
