using System.Text.Json;
using Microsoft.Data.Sqlite;

public sealed record OrderItem(Guid ProductId, int Quantity);
public sealed record CreateOrderRequest(IReadOnlyList<OrderItem>? Items);
public sealed record Order(Guid Id, string CustomerEmail, IReadOnlyList<OrderItem> Items, DateTimeOffset CreatedAt, string Status);

public sealed class OrdersStore(string connectionString)
{
    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    public async Task InitializeAsync()
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Orders (
                Id TEXT PRIMARY KEY,
                CustomerEmail TEXT NOT NULL,
                ItemsJson TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                Status TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Orders_CustomerEmail_CreatedAt
                ON Orders(CustomerEmail, CreatedAt DESC);
            """;
        await command.ExecuteNonQueryAsync();
    }

    public async Task CreateAsync(Order order, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Orders (Id, CustomerEmail, ItemsJson, CreatedAt, Status)
            VALUES ($id, $email, $items, $created, $status);
            """;
        command.Parameters.AddWithValue("$id", order.Id.ToString());
        command.Parameters.AddWithValue("$email", order.CustomerEmail);
        command.Parameters.AddWithValue("$items", JsonSerializer.Serialize(order.Items));
        command.Parameters.AddWithValue("$created", order.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$status", order.Status);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> ListByCustomerAsync(string email, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, CustomerEmail, ItemsJson, CreatedAt, Status FROM Orders WHERE CustomerEmail = $email ORDER BY CreatedAt DESC;";
        command.Parameters.AddWithValue("$email", email);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var orders = new List<Order>();
        while (await reader.ReadAsync(cancellationToken))
            orders.Add(ReadOrder(reader));
        return orders;
    }

    public async Task<Order?> FindByIdAsync(Guid id, string customerEmail, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, CustomerEmail, ItemsJson, CreatedAt, Status FROM Orders WHERE Id = $id AND CustomerEmail = $email;";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$email", customerEmail);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadOrder(reader) : null;
    }

    public async Task<bool> PingAsync()
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static Order ReadOrder(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        JsonSerializer.Deserialize<IReadOnlyList<OrderItem>>(reader.GetString(2)) ?? [],
        DateTimeOffset.Parse(reader.GetString(3), System.Globalization.CultureInfo.InvariantCulture),
        reader.GetString(4));
}
