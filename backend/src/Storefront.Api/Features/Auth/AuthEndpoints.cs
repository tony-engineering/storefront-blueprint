using System.ComponentModel;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Storefront.Api.Features.Auth;

public static class AuthPolicies
{
    public const string Admin = "Admin";
    public const string RoleClaim = "role";
    public const string AdminRole = "Admin";
    public const string CustomerRole = "Customer";
}

public static class AuthEndpoints
{
    /// <summary>The hardcoded users of the exercise. A real system would use an identity provider.</summary>
    private static readonly Dictionary<string, (string Password, string Role)> Users = new(StringComparer.Ordinal)
    {
        ["admin"] = ("admin", AuthPolicies.AdminRole),
        ["customer"] = ("customer", AuthPolicies.CustomerRole),
    };

    public static IServiceCollection AddAuth(this IServiceCollection services)
    {
        services.AddOptions<AuthOptions>().BindConfiguration(AuthOptions.SectionName);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Read when the first token is checked, so configuration supplied late (environment, tests) still applies.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthOptions>>((jwt, auth) =>
            {
                var settings = auth.Value;
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = settings.Issuer,
                    ValidAudience = settings.Audience,
                    IssuerSigningKey = settings.CreateKey(),
                    NameClaimType = "name",
                    RoleClaimType = AuthPolicies.RoleClaim,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.Admin, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(AuthPolicies.RoleClaim, AuthPolicies.AdminRole));

        return services;
    }

    /// <summary>
    /// <c>POST /api/auth/token</c>: swaps a hardcoded user for a JWT. Only answers in Development (or when
    /// <c>Auth:DevTokenEndpoint</c> is on, as in the integration tests); elsewhere it does not exist (404).
    /// </summary>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/token", IssueToken)
            .WithTags("Auth (development only)")
            .WithName("IssueToken")
            .WithSummary("Get a token for a demo user")
            .WithDescription(
                "Development only. Users: `admin` / `admin` (can manage products) and `customer` / `customer`. " +
                "Paste the returned `accessToken` into **Authorize**.")
            .Produces<TokenResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static IResult IssueToken(
        TokenRequest request,
        IHostEnvironment environment,
        IOptions<AuthOptions> options)
    {
        var settings = options.Value;
        if (!environment.IsDevelopment() && !settings.DevTokenEndpoint)
        {
            return Problems.Result(StatusCodes.Status404NotFound, "not-found", "Not found", "There is no such endpoint.");
        }

        if (request.Username is null || !Users.TryGetValue(request.Username, out var user) || user.Password != request.Password)
        {
            return Problems.Result(StatusCodes.Status401Unauthorized, "invalid-credentials", "Invalid credentials.",
                "Unknown user or wrong password.");
        }

        // Real clock, not the injected TimeProvider: the token is checked against real time by the JWT middleware.
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", request.Username),
                new Claim("name", request.Username),
                new Claim(AuthPolicies.RoleClaim, user.Role),
            ]),
            NotBefore = now,
            Expires = now + settings.TokenLifetime,
            SigningCredentials = new SigningCredentials(settings.CreateKey(), SecurityAlgorithms.HmacSha256),
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return TypedResults.Ok(new TokenResponse(token, "Bearer", (int)settings.TokenLifetime.TotalSeconds));
    }
}

public sealed record TokenRequest(
    [property: DefaultValue("admin")] string? Username,
    [property: DefaultValue("admin")] string? Password);

public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);
