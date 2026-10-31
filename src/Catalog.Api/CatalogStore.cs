using Microsoft.Data.Sqlite;

public sealed record Product(Guid Id, string Name, string Category, decimal Price, int Stock);

public sealed class CatalogStore(string connectionString)
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
            CREATE TABLE IF NOT EXISTS Products (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Category TEXT NOT NULL,
                Price TEXT NOT NULL,
                Stock INTEGER NOT NULL CHECK (Stock >= 0)
            );
            INSERT OR IGNORE INTO Products (Id, Name, Category, Price, Stock) VALUES
            ('10000000-0000-0000-0000-000000000001', 'Mechanical Keyboard', 'Peripherals', '89.90', 25),
            ('10000000-0000-0000-0000-000000000002', 'Wireless Mouse', 'Peripherals', '39.50', 40),
            ('10000000-0000-0000-0000-000000000003', '27-inch Monitor', 'Displays', '249.00', 12);
            """;
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<Product>> ListAsync(string? category, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = string.IsNullOrWhiteSpace(category)
            ? "SELECT Id, Name, Category, Price, Stock FROM Products ORDER BY Name;"
            : "SELECT Id, Name, Category, Price, Stock FROM Products WHERE Category = $category COLLATE NOCASE ORDER BY Name;";
        if (!string.IsNullOrWhiteSpace(category))
            command.Parameters.AddWithValue("$category", category.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var products = new List<Product>();
        while (await reader.ReadAsync(cancellationToken))
            products.Add(new Product(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2),
                decimal.Parse(reader.GetString(3), System.Globalization.CultureInfo.InvariantCulture), reader.GetInt32(4)));
        return products;
    }

    public async Task<Product?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Category, Price, Stock FROM Products WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new Product(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2),
            decimal.Parse(reader.GetString(3), System.Globalization.CultureInfo.InvariantCulture), reader.GetInt32(4));
    }

    public async Task<bool> PingAsync()
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }
}
