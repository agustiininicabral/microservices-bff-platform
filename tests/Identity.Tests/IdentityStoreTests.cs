using Microsoft.AspNetCore.Identity;
using Xunit;

namespace Identity.Tests;

public sealed class IdentityStoreTests
{
    [Fact]
    public async Task CreateUserAsync_PersistsPasswordHash_AndCanRetrieveIt()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"identity-tests-{Guid.NewGuid():N}.db");
        try
        {
            var store = new IdentityStore($"Data Source={databasePath}");
            await store.InitializeAsync();
            const string email = "test@example.com";
            const string password = "StrongPassword123";
            var hasher = new PasswordHasher<string>();
            var hash = hasher.HashPassword(email, password);

            Assert.True(await store.CreateUserAsync(email, hash));
            var persistedHash = await store.FindPasswordHashAsync(email);

            Assert.NotNull(persistedHash);
            Assert.NotEqual(password, persistedHash);
            Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(email, persistedHash!, password));
            Assert.True(await store.PingAsync());
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task CreateUserAsync_RejectsDuplicateEmailCaseInsensitively()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"identity-tests-{Guid.NewGuid():N}.db");
        try
        {
            var store = new IdentityStore($"Data Source={databasePath}");
            await store.InitializeAsync();

            Assert.True(await store.CreateUserAsync("test@example.com", "hash-one"));
            Assert.False(await store.CreateUserAsync("TEST@example.com", "hash-two"));
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }
}
