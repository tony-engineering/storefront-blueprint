using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Storefront.Api.Features.Auth;

/// <summary>The <c>Auth</c> configuration section (ARCHITECTURE.md §9).</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// HMAC key that signs and checks the JWTs, at least 32 characters. Never committed for real deployments:
    /// supply it through user secrets or the <c>Auth__SigningKey</c> environment variable.
    /// </summary>
    public string? SigningKey { get; init; }

    public string Issuer { get; init; } = "storefront-api";

    public string Audience { get; init; } = "storefront-client";

    public TimeSpan TokenLifetime { get; init; } = TimeSpan.FromHours(1);

    /// <summary>Turns on <c>POST /api/auth/token</c> outside Development (the integration tests do).</summary>
    public bool DevTokenEndpoint { get; init; }

    public SymmetricSecurityKey CreateKey()
    {
        if (string.IsNullOrWhiteSpace(SigningKey) || Encoding.UTF8.GetByteCount(SigningKey) < 32)
        {
            throw new InvalidOperationException(
                $"Auth:SigningKey must be set to at least 32 characters (environment variable Auth__SigningKey).");
        }

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
    }
}
