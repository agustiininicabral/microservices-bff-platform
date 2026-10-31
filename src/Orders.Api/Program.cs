using System.Collections.Concurrent;
using System.Security.Claims;
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
builder.Services.AddAuthorization(); builder.Services.AddSingleton<ConcurrentDictionary<Guid, Order>>();
var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization();
app.MapGet("/health/live", () => Results.Ok(new { status = "live", service = "orders" }));
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready", service = "orders" }));
app.MapGet("/api/orders", (ClaimsPrincipal user, ConcurrentDictionary<Guid, Order> store) => {
    var email = user.FindFirstValue(ClaimTypes.Name) ?? user.FindFirstValue("sub");
    return Results.Ok(store.Values.Where(o => o.CustomerEmail == email).OrderByDescending(o => o.CreatedAt));
}).RequireAuthorization();
app.MapPost("/api/orders", (CreateOrderRequest request, ClaimsPrincipal user, ConcurrentDictionary<Guid, Order> store) => {
    if (request.Items is null || request.Items.Count == 0 || request.Items.Any(i => i.Quantity is < 1 or > 100 || i.ProductId == Guid.Empty))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["items"] = ["At least one valid item is required; quantities must be between 1 and 100."] });
    var email = user.FindFirstValue(ClaimTypes.Name) ?? user.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(email)) return Results.Unauthorized();
    var order = new Order(Guid.NewGuid(), email, request.Items, DateTimeOffset.UtcNow, "Pending"); store[order.Id] = order;
    return Results.Created($"/api/orders/{order.Id}", order);
}).RequireAuthorization();
app.MapGet("/api/orders/{id:guid}", (Guid id, ClaimsPrincipal user, ConcurrentDictionary<Guid, Order> store) => {
    var email = user.FindFirstValue(ClaimTypes.Name) ?? user.FindFirstValue("sub");
    return store.TryGetValue(id, out var order) && order.CustomerEmail == email ? Results.Ok(order) : Results.NotFound();
}).RequireAuthorization();
app.Run();
public sealed record OrderItem(Guid ProductId, int Quantity);
public sealed record CreateOrderRequest(IReadOnlyList<OrderItem>? Items);
public sealed record Order(Guid Id, string CustomerEmail, IReadOnlyList<OrderItem> Items, DateTimeOffset CreatedAt, string Status);
