using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AgentPowerShell.Core;
using Microsoft.IdentityModel.Tokens;

namespace AgentPowerShell.Daemon;

public sealed class AuthenticationManager(AgentPowerShellConfig config) : IAuthenticationService
{
    private readonly JwtSecurityTokenHandler _handler = new();

    public Task<AuthenticationResult> AuthenticateAsync(AuthenticationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(config.Auth.Mode.ToLowerInvariant() switch
        {
            "apikey" => AuthenticateApiKey(request.ApiKey),
            "oidc" => AuthenticateBearerToken(request.BearerToken),
            "hybrid" => AuthenticateHybrid(request),
            _ => new AuthenticationResult(true, "none", "anonymous", "agent", "Authentication disabled.")
        });
    }

    public Task<TokenPair> IssueTokenPairAsync(string subject, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(config.Auth.Oidc.AccessTokenLifetimeMinutes);
        var accessToken = CreateToken(subject, roles, expiresAt, "access");
        var refreshToken = CreateToken(
            subject,
            roles,
            DateTimeOffset.UtcNow.AddMinutes(config.Auth.Oidc.RefreshTokenLifetimeMinutes),
            "refresh");
        return Task.FromResult(new TokenPair(accessToken, refreshToken, expiresAt));
    }

    public Task<TokenPair> RefreshTokenPairAsync(string refreshToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var principal = ValidateToken(refreshToken, validateLifetime: true, expectedTokenType: "refresh");
        var subject = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new SecurityTokenException("Refresh token is missing the subject.");
        var roles = principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray();
        return IssueTokenPairAsync(subject, roles, cancellationToken);
    }

    private AuthenticationResult AuthenticateApiKey(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(config.Auth.ApiKey))
        {
            return new AuthenticationResult(false, "apikey", null, null, "API key mode is enabled but no configured key exists.");
        }

        return string.Equals(apiKey, config.Auth.ApiKey, StringComparison.Ordinal)
            ? new AuthenticationResult(true, "apikey", "api-key-user", "admin")
            : new AuthenticationResult(false, "apikey", null, null, "Invalid API key.");
    }

    private AuthenticationResult AuthenticateBearerToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new AuthenticationResult(false, "oidc", null, null, "Missing bearer token.");
        }

        try
        {
            var principal = ValidateToken(token, validateLifetime: true, expectedTokenType: "access");
            var subject = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return new AuthenticationResult(
                true,
                "oidc",
                subject,
                principal.FindFirst(ClaimTypes.Role)?.Value ?? "agent");
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            return new AuthenticationResult(false, "oidc", null, null, ex.Message);
        }
    }

    private AuthenticationResult AuthenticateHybrid(AuthenticationRequest request)
    {
        var apiKeyResult = AuthenticateApiKey(request.ApiKey);
        if (!apiKeyResult.Authenticated)
        {
            return apiKeyResult with { Mode = "hybrid" };
        }

        var oidcResult = AuthenticateBearerToken(request.BearerToken);
        return oidcResult.Authenticated
            ? oidcResult with { Mode = "hybrid" }
            : oidcResult with { Mode = "hybrid" };
    }

    private string CreateToken(string subject, IReadOnlyCollection<string> roles, DateTimeOffset expiresAt, string tokenType)
    {
        var credentials = new SigningCredentials(GetSigningKey(), SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new("token_use", tokenType)
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var token = new JwtSecurityToken(
            issuer: config.Auth.Oidc.Issuer,
            audience: ResolveAudience(),
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return _handler.WriteToken(token);
    }

    private ClaimsPrincipal ValidateToken(string token, bool validateLifetime, string expectedTokenType)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrWhiteSpace(config.Auth.Oidc.Issuer),
            ValidIssuer = config.Auth.Oidc.Issuer,
            ValidateAudience = !string.IsNullOrWhiteSpace(ResolveAudience()),
            ValidAudience = ResolveAudience(),
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = GetSigningKey(),
            ValidateLifetime = validateLifetime,
            ClockSkew = TimeSpan.FromMinutes(1),
            RoleClaimType = ClaimTypes.Role
        };

        var principal = _handler.ValidateToken(token, parameters, out _);
        var tokenUse = principal.FindFirst("token_use")?.Value;
        if (!string.Equals(tokenUse, expectedTokenType, StringComparison.Ordinal))
        {
            throw new SecurityTokenException($"Unexpected token type '{tokenUse}'.");
        }

        return principal;
    }

    private SymmetricSecurityKey GetSigningKey()
    {
        var secret = config.Auth.Oidc.ClientSecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("OIDC client secret is required.");
        }

        return new SymmetricSecurityKey(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }

    private string ResolveAudience() =>
        string.IsNullOrWhiteSpace(config.Auth.Oidc.Audience)
            ? config.Auth.Oidc.ClientId
            : config.Auth.Oidc.Audience;
}
