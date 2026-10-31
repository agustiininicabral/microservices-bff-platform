using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Identity") ?? "Data Source=identity.db";
var signingKey = builder.Configuration["Jwt:SigningKey"] ?? throw new InvalidOperationException("Jwt:SigningKey must be configured.");
if (Encoding.UTF8.GetByteCount(signingKey) < 32) throw new InvalidOperationException("Jwt:SigningKey must contain at least 32 bytes.");
builder.Services.AddSingleton(new PasswordHasher<string>());
builder.Services.AddSingleton(new TokenIssuer(signingKey, builder.Configuration["Jwt:Issuer"] ?? "microservices-bff-platform"));
builder.Services.AddSingleton(new IdentityStore(connectionString));
builder.Services.AddRateLimiter(o => o.AddFixedWindowLimiter("auth", x => { x.PermitLimit = 10; x.Window = TimeSpan.FromMinutes(1); x.QueueLimit = 0; }));
var app = builder.Build();
app.UseRateLimiter();
await app.Services.GetRequiredService<IdentityStore>().InitializeAsync();
app.MapGet("/health/live", () => Results.Ok(new { status = "live", service = "identity" }));
app.MapGet("/health/ready", async (IdentityStore store) => await store.PingAsync() ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));
app.MapPost("/api/auth/register", async (RegisterRequest request, IdentityStore store, PasswordHasher<string> hasher) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || !System.Net.Mail.MailAddress.TryCreate(request.Email, out _))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["A valid email is required."] });
    if (request.Password is null || request.Password.Length < 12 || !request.Password.Any(char.IsLetter) || !request.Password.Any(char.IsDigit))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["password"] = ["Password must contain at least 12 characters, including letters and numbers."] });
    var email = request.Email.Trim().ToLowerInvariant();
    var created = await store.CreateUserAsync(email, hasher.HashPassword(email, request.Password));
    return created ? Results.Created($"/api/users/{Uri.EscapeDataString(email)}", new { email }) : Results.Conflict(new { error = "An account with this email already exists." });
}).RequireRateLimiting("auth");
app.MapPost("/api/auth/login", async (LoginRequest request, IdentityStore store, PasswordHasher<string> hasher, TokenIssuer issuer) =>
{
    var email = request.Email?.Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(request.Password)) return Results.Unauthorized();
    var hash = await store.FindPasswordHashAsync(email);
    if (hash is null || hasher.VerifyHashedPassword(email, hash, request.Password) == PasswordVerificationResult.Failed) return Results.Unauthorized();
    return Results.Ok(issuer.Create(email));
}).RequireRateLimiting("auth");
app.Run();
public partial class Program;
public sealed record RegisterRequest(string? Email, string? Password);
public sealed record LoginRequest(string? Email, string? Password);
public sealed class TokenIssuer(string signingKey, string issuer)
{
    public object Create(string email)
    {
        var now = DateTime.UtcNow;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var token = new JwtSecurityToken(issuer, "microservices-bff-platform", [new Claim(JwtRegisteredClaimNames.Sub, email), new Claim(JwtRegisteredClaimNames.Email, email), new Claim(ClaimTypes.Name, email)], now, now.AddMinutes(30), new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new { accessToken = new JwtSecurityTokenHandler().WriteToken(token), tokenType = "Bearer", expiresIn = 1800 };
    }
}
public sealed class IdentityStore(string connectionString)
{
    private async Task<SqliteConnection> OpenAsync() { var c = new SqliteConnection(connectionString); await c.OpenAsync(); return c; }
    public async Task InitializeAsync()
    {
        await using var c = await OpenAsync(); await using var cmd = c.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS Users (Email TEXT PRIMARY KEY COLLATE NOCASE, PasswordHash TEXT NOT NULL, CreatedAt TEXT NOT NULL);";
        await cmd.ExecuteNonQueryAsync();
    }
    public async Task<bool> CreateUserAsync(string email, string hash)
    {
        try {
            await using var c = await OpenAsync(); await using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO Users(Email, PasswordHash, CreatedAt) VALUES ($email, $hash, $created);";
            cmd.Parameters.AddWithValue("$email", email); cmd.Parameters.AddWithValue("$hash", hash); cmd.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(); return true;
        } catch (SqliteException ex) when (ex.SqliteErrorCode == 19) { return false; }
    }
    public async Task<string?> FindPasswordHashAsync(string email)
    {
        await using var c = await OpenAsync(); await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT PasswordHash FROM Users WHERE Email = $email;"; cmd.Parameters.AddWithValue("$email", email);
        return await cmd.ExecuteScalarAsync() as string;
    }
    public async Task<bool> PingAsync()
    {
        await using var c = await OpenAsync(); await using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT 1;";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
    }
}
