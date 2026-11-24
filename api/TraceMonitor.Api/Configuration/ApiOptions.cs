namespace TraceMonitor.Api.Configuration;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string SecretKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "TraceMonitor";
    public string Audience { get; set; } = "TraceMonitor.UI";
    public int ExpirationMinutes { get; set; } = 480;
}

public sealed class SeedAdminOptions
{
    public const string SectionName = "SeedAdmin";
    public string Username { get; set; } = "admin";
    public string Password { get; set; } = "ChangeMe123!";
}

public sealed class RetentionOptions
{
    public const string SectionName = "Retention";
    public int Days { get; set; } = 30;
    public int RunIntervalHours { get; set; } = 6;
}

public sealed class CorsOptions
{
    public const string SectionName = "Cors";
    public string[] AllowedOrigins { get; set; } = ["http://localhost:5190"];
}
