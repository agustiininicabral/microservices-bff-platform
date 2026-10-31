using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
var builder = WebApplication.CreateBuilder(args);
var key = builder.Configuration["Jwt:SigningKey"] ?? throw new InvalidOperationException("Jwt:SigningKey must be configured.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters {
    ValidateIssuer = true, ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "microservices-bff-platform",
    ValidateAudience = true, ValidAudience = "microservices-bff-platform", ValidateIssuerSigningKey = true,
    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30)
});
builder.Services.AddAuthorization();
var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization();
var products = new[] {
    new Product(Guid.Parse("10000000-0000-0000-0000-000000000001"), "Mechanical Keyboard", "Peripherals", 89.90m, 25),
    new Product(Guid.Parse("10000000-0000-0000-0000-000000000002"), "Wireless Mouse", "Peripherals", 39.50m, 40),
    new Product(Guid.Parse("10000000-0000-0000-0000-000000000003"), "27-inch Monitor", "Displays", 249.00m, 12)
};
app.MapGet("/health/live", () => Results.Ok(new { status = "live", service = "catalog" }));
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready", service = "catalog" }));
app.MapGet("/api/catalog/products", (string? category) => Results.Ok(string.IsNullOrWhiteSpace(category) ? products : products.Where(p => p.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToArray())).RequireAuthorization();
app.MapGet("/api/catalog/products/{id:guid}", (Guid id) => products.FirstOrDefault(p => p.Id == id) is { } p ? Results.Ok(p) : Results.NotFound()).RequireAuthorization();
app.Run();
public sealed record Product(Guid Id, string Name, string Category, decimal Price, int Stock);
