using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CitizenServicesCopilot.Api.DTOs;
using Microsoft.IdentityModel.Tokens;

namespace CitizenServicesCopilot.Api.Endpoints;

/// <summary>
/// Issues short-lived JWT bearer tokens for the two application roles.
/// Role membership is asserted from the request body (the demo has no user
/// store); the token then carries the claim the authorization policies rely on.
/// </summary>
public static class AuthEndpoints
{
    public const string CitizenRole = "Citizen";
    public const string OfficerRole = "Officer";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app, IConfiguration config)
    {
        app.MapPost("/api/auth/login", (LoginRequest request, IConfiguration cfg) =>
            {
                var role = request.Role?.Trim();
                if (role is not (CitizenRole or OfficerRole))
                {
                    return Results.BadRequest(new { message = $"role must be '{CitizenRole}' or '{OfficerRole}'." });
                }

                var issuer = cfg["Jwt:Issuer"] ?? "CitizenServicesCopilot";
                var audience = cfg["Jwt:Audience"] ?? "CitizenServicesCopilot.Api";
                var key = cfg["Jwt:Key"]!;

                var expiresUtc = DateTimeOffset.UtcNow.AddHours(1);
                var token = new JwtSecurityToken(
                    issuer: issuer,
                    audience: audience,
                    claims:
                    [
                        new Claim(JwtRegisteredClaimNames.Sub, request.UserId),
                        new Claim(ClaimTypes.NameIdentifier, request.UserId),
                        new Claim(ClaimTypes.Role, role)
                    ],
                    notBefore: DateTime.UtcNow,
                    expires: expiresUtc.UtcDateTime,
                    signingCredentials: new SigningCredentials(
                        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                        SecurityAlgorithms.HmacSha256));

                var tokenText = new JwtSecurityTokenHandler().WriteToken(token);
                return Results.Ok(new LoginResponse(tokenText, expiresUtc));
            })
            .WithName("Login")
            .WithTags("Auth")
            .AllowAnonymous();

        return app;
    }
}