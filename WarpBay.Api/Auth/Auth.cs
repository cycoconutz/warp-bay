using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using WarpBay.Api.Models;

namespace WarpBay.Api.Auth;

public class JwtOptions
{
    public string Key { get; set; } = "warp-bay-demo-key-change-me-32chars-minimum!!";
    public string Issuer { get; set; } = "warpbay";
    public string Audience { get; set; } = "warpbay-client";
    public int ExpiryMinutes { get; set; } = 720;
}

public class TokenService
{
    private readonly JwtOptions _opt;
    public TokenService(Microsoft.Extensions.Options.IOptions<JwtOptions> opt) => _opt = opt.Value;

    public string Issue(AppUser u)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, u.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new Claim(ClaimTypes.Email, u.Email),
            new Claim(ClaimTypes.Name, u.DisplayName),
            new Claim(ClaimTypes.Role, u.Role),
        };
        var tok = new JwtSecurityToken(_opt.Issuer, _opt.Audience, claims,
            expires: DateTime.UtcNow.AddMinutes(_opt.ExpiryMinutes), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(tok);
    }
}

public static class Passwords
{
    private static readonly PasswordHasher<AppUser> Hasher = new();
    public static string Hash(AppUser u, string password) => Hasher.HashPassword(u, password);
    public static bool Verify(AppUser u, string password) =>
        Hasher.VerifyHashedPassword(u, u.PasswordHash, password) != PasswordVerificationResult.Failed;
}

public record LoginRequest(string Email, string Password);
public record DemoRequest(string Role);

public static class AuthEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/auth/login", async (LoginRequest req, Data.WarpBayDb db, TokenService tokens) =>
        {
            var email = req.Email.Trim().ToLowerInvariant();
            var user = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstOrDefaultAsync(db.Users, u => u.Email == email);
            if (user is null || !Passwords.Verify(user, req.Password))
                return Results.Unauthorized();
            return Results.Ok(new { token = tokens.Issue(user), user = new { user.Id, user.Email, user.Role, user.DisplayName } });
        }).WithOpenApi();

        // One-click demo login (mirrors Deadwax demo UX). Enabled when Demo:Enabled=true.
        app.MapPost("/api/auth/demo", async (DemoRequest req, Data.WarpBayDb db, TokenService tokens, IConfiguration cfg) =>
        {
            if (cfg.GetValue("Demo:Enabled", true) is false) return Results.NotFound();
            var email = req.Role.ToLowerInvariant() switch
            {
                "admin" => "admin@warp-bay.demo",
                "manager" => "manager@warp-bay.demo",
                "tech" => "tech@warp-bay.demo",
                _ => "customer@warp-bay.demo",
            };
            var user = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstOrDefaultAsync(db.Users, u => u.Email == email);
            if (user is null) return Results.NotFound();
            return Results.Ok(new { token = tokens.Issue(user), user = new { user.Id, user.Email, user.Role, user.DisplayName } });
        }).WithOpenApi();

        app.MapGet("/api/me", (ClaimsPrincipal me) =>
        {
            if (me.Identity?.IsAuthenticated is not true) return Results.Unauthorized();
            return Results.Ok(new
            {
                id = me.FindFirstValue(ClaimTypes.NameIdentifier),
                email = me.FindFirstValue(ClaimTypes.Email),
                name = me.FindFirstValue(ClaimTypes.Name),
                role = me.FindFirstValue(ClaimTypes.Role),
            });
        }).RequireAuthorization().WithOpenApi();
    }
}
