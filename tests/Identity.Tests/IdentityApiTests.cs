using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Identity.Tests;

public sealed class IdentityApiTests
{
    [Fact]
    public async Task RegisterAndLogin_ReturnsSignedAccessToken_AndRejectsInvalidCredentials()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"identity-api-tests-{Guid.NewGuid():N}.db");
        using var factory = new IdentityApiFactory(databasePath);
        using var client = factory.CreateClient();

        var registration = await client.PostAsJsonAsync("/api/auth/register",
            new { email = "integration@example.com", password = "StrongPassword123" });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        var duplicate = await client.PostAsJsonAsync("/api/auth/register",
            new { email = "INTEGRATION@example.com", password = "StrongPassword123" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var weakPassword = await client.PostAsJsonAsync("/api/auth/register",
            new { email = "weak@example.com", password = "weak" });
        Assert.Equal(HttpStatusCode.BadRequest, weakPassword.StatusCode);

        var invalidLogin = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "integration@example.com", password = "WrongPassword123" });
        Assert.Equal(HttpStatusCode.Unauthorized, invalidLogin.StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "integration@example.com", password = "StrongPassword123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var payload = await login.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.False(string.IsNullOrWhiteSpace(payload?.AccessToken));
        Assert.Equal("Bearer", payload?.TokenType);
        Assert.Equal(1800, payload?.ExpiresIn);
    }

    private sealed class IdentityApiFactory(string databasePath) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Identity"] = $"Data Source={databasePath}",
                    ["Jwt:Issuer"] = "microservices-bff-platform",
                    ["Jwt:SigningKey"] = new string('K', 48)
                }));
        }
    }

    private sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);
}
