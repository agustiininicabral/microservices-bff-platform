using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var key = builder.Configuration["Jwt:SigningKey"] ?? throw new InvalidOperationException("Jwt:SigningKey must be configured.");
if (Encoding.UTF8.GetByteCount(key) < 32) throw new InvalidOperationException("Jwt:SigningKey must contain at least 32 bytes.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters {
    ValidateIssuer = true, ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "microservices-bff-platform",
    ValidateAudience = true, ValidAudience = "microservices-bff-platform", ValidateIssuerSigningKey = true,
    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30)
});
builder.Services.AddAuthorization();
builder.Services.AddSingleton(new CatalogStore(builder.Configuration.GetConnectionString("Catalog") ?? "Data Source=catalog.db"));
var app = builder.Build();
await app.Services.GetRequiredService<CatalogStore>().InitializeAsync();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health/live", () => Results.Ok(new { status = "live", service = "catalog" }));
app.MapGet("/health/ready", async (CatalogStore store) => await store.PingAsync() ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));
app.MapGet("/api/catalog/products", async (string? category, CatalogStore store, CancellationToken ct) => Results.Ok(await store.ListAsync(category, ct))).RequireAuthorization();
app.MapGet("/api/catalog/products/{id:guid}", async (Guid id, CatalogStore store, CancellationToken ct) =>
    await store.FindAsync(id, ct) is { } product ? Results.Ok(product) : Results.NotFound()).RequireAuthorization();
app.Run();
