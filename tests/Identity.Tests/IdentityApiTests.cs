using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Identity.Tests;

public sealed class IdentityApiTests
{
    [Fact]
    public async Task RegisterAndLogin_ReturnsSignedAccessToken_AndRejectsInvalidCredentials()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"identity-api-tests-{Guid.NewGuid():N}.db");
        var previousKey = Environment.GetEnvironmentVariable("Jwt__SigningKey");
        var previousConnection = Environment.GetEnvironmentVariable("ConnectionStrings__Identity");
        Environment.SetEnvironmentVariable("Jwt__SigningKey", new string('K', 48));
        Environment.SetEnvironmentVariable("ConnectionStrings__Identity", $"Data Source={databasePath}");

        try
        {
            using var factory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>();
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
        finally
        {
            Environment.SetEnvironmentVariable("Jwt__SigningKey", previousKey);
            Environment.SetEnvironmentVariable("ConnectionStrings__Identity", previousConnection);
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    private sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);
}
