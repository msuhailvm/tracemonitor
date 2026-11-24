using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TraceMonitor.Api.Configuration;
using TraceMonitor.Api.Data;

namespace TraceMonitor.Api.Auth;

public sealed class PasswordHasherService
{
    private readonly PasswordHasher<string> _hasher = new();

    public string Hash(string username, string password) =>
        _hasher.HashPassword(username, password);

    public bool Verify(string username, string hash, string password)
    {
        var result = _hasher.VerifyHashedPassword(username, hash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}

public sealed class JwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public (string Token, DateTimeOffset ExpiresAt) CreateToken(Guid userId, string username, string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(Math.Max(15, _options.ExpirationMinutes));

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}

public static class ApiKeyHelper
{
    public static (string PlainTextKey, string KeyHash, string Prefix) GenerateApiKey()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(24);
        var token = Convert.ToHexString(randomBytes).ToLowerInvariant();
        var plainText = $"tm_live_{token}";
        var prefix = plainText[..14];
        return (plainText, HashKey(plainText), prefix);
    }

    public static string HashKey(string apiKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed class ApiKeyValidator
{
    private readonly TraceRepository _repository;
    private readonly IMemoryCache _cache;

    public ApiKeyValidator(TraceRepository repository, IMemoryCache cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task<Guid?> ValidateAsync(string? apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var hash = ApiKeyHelper.HashKey(apiKey.Trim());
        var cacheKey = $"apikey:{hash}";

        if (_cache.TryGetValue(cacheKey, out Guid? cachedAppId))
        {
            return cachedAppId;
        }

        var appId = await _repository.FindActiveApplicationIdByKeyHashAsync(hash, ct).ConfigureAwait(false);
        _cache.Set(cacheKey, appId, TimeSpan.FromMinutes(appId.HasValue ? 5 : 1));
        return appId;
    }

    public void InvalidateHash(string oldHash) => _cache.Remove($"apikey:{oldHash}");
}
