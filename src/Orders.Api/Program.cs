using System.Security.Claims;
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
builder.Services.AddSingleton(new OrdersStore(builder.Configuration.GetConnectionString("Orders") ?? "Data Source=orders.db"));
var app = builder.Build();
await app.Services.GetRequiredService<OrdersStore>().InitializeAsync();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health/live", () => Results.Ok(new { status = "live", service = "orders" }));
app.MapGet("/health/ready", async (OrdersStore store) => await store.PingAsync() ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));
app.MapGet("/api/orders", async (ClaimsPrincipal user, OrdersStore store, CancellationToken ct) => {
    var email = user.FindFirstValue(ClaimTypes.Name) ?? user.FindFirstValue("sub");
    return string.IsNullOrWhiteSpace(email) ? Results.Unauthorized() : Results.Ok(await store.ListByCustomerAsync(email, ct));
}).RequireAuthorization();
app.MapPost("/api/orders", async (CreateOrderRequest request, ClaimsPrincipal user, OrdersStore store, CancellationToken ct) => {
    if (request.Items is null || request.Items.Count == 0 || request.Items.Count > 50 || request.Items.Any(i => i.Quantity is < 1 or > 100 || i.ProductId == Guid.Empty))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["items"] = ["Provide between 1 and 50 valid items; each quantity must be between 1 and 100."] });
    var email = user.FindFirstValue(ClaimTypes.Name) ?? user.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(email)) return Results.Unauthorized();
    var order = new Order(Guid.NewGuid(), email, request.Items, DateTimeOffset.UtcNow, "Pending");
    await store.CreateAsync(order, ct);
    return Results.Created($"/api/orders/{order.Id}", order);
}).RequireAuthorization();
app.MapGet("/api/orders/{id:guid}", async (Guid id, ClaimsPrincipal user, OrdersStore store, CancellationToken ct) => {
    var email = user.FindFirstValue(ClaimTypes.Name) ?? user.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(email)) return Results.Unauthorized();
    var order = await store.FindByIdAsync(id, email, ct);
    return order is null ? Results.NotFound() : Results.Ok(order);
}).RequireAuthorization();
app.Run();
