using Xunit;

namespace Identity.Tests;

public sealed class CatalogAndOrdersStoreTests
{
    [Fact]
    public async Task CatalogStore_SeedsProducts_AndFiltersByCategory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"catalog-tests-{Guid.NewGuid():N}.db");
        try
        {
            var store = new CatalogStore($"Data Source={path}");
            await store.InitializeAsync();

            var all = await store.ListAsync(null, CancellationToken.None);
            var peripherals = await store.ListAsync("peripherals", CancellationToken.None);
            var product = await store.FindAsync(Guid.Parse("10000000-0000-0000-0000-000000000001"), CancellationToken.None);

            Assert.Equal(3, all.Count);
            Assert.Equal(2, peripherals.Count);
            Assert.NotNull(product);
            Assert.Equal("Mechanical Keyboard", product!.Name);
            Assert.True(await store.PingAsync());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task OrdersStore_PersistsOrders_AndScopesReadsToCustomer()
    {
        var path = Path.Combine(Path.GetTempPath(), $"orders-tests-{Guid.NewGuid():N}.db");
        try
        {
            var store = new OrdersStore($"Data Source={path}");
            await store.InitializeAsync();
            var order = new Order(Guid.NewGuid(), "alice@example.com",
                [new OrderItem(Guid.Parse("10000000-0000-0000-0000-000000000001"), 2)],
                DateTimeOffset.UtcNow, "Pending");

            await store.CreateAsync(order, CancellationToken.None);

            var aliceOrders = await store.ListByCustomerAsync("alice@example.com", CancellationToken.None);
            var bobOrders = await store.ListByCustomerAsync("bob@example.com", CancellationToken.None);
            var aliceOrder = await store.FindByIdAsync(order.Id, "alice@example.com", CancellationToken.None);
            var bobOrder = await store.FindByIdAsync(order.Id, "bob@example.com", CancellationToken.None);

            Assert.Single(aliceOrders);
            Assert.Empty(bobOrders);
            Assert.Equal(order.Id, aliceOrder?.Id);
            Assert.Null(bobOrder);
            Assert.Equal(2, aliceOrder?.Items[0].Quantity);
            Assert.True(await store.PingAsync());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
