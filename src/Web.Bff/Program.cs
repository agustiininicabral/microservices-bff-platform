using System.Net.Http.Headers;
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
builder.Services.AddHttpClient("catalog", c => { c.BaseAddress = new Uri(builder.Configuration["Services:Catalog"] ?? "http://localhost:5101"); c.Timeout = TimeSpan.FromSeconds(5); });
builder.Services.AddHttpClient("orders", c => { c.BaseAddress = new Uri(builder.Configuration["Services:Orders"] ?? "http://localhost:5102"); c.Timeout = TimeSpan.FromSeconds(5); });
var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization();
app.MapGet("/health/live", () => Results.Ok(new { status = "live", service = "web-bff" }));
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready", service = "web-bff" }));
app.MapGet("/api/web/dashboard", async (HttpContext context, IHttpClientFactory clients, CancellationToken ct) => {
    var token = context.Request.Headers.Authorization.ToString();
    using var catalogRequest = new HttpRequestMessage(HttpMethod.Get, "/api/catalog/products");
    using var ordersRequest = new HttpRequestMessage(HttpMethod.Get, "/api/orders");
    if (AuthenticationHeaderValue.TryParse(token, out var auth)) { catalogRequest.Headers.Authorization = auth; ordersRequest.Headers.Authorization = auth; }
    var catalogTask = clients.CreateClient("catalog").SendAsync(catalogRequest, ct);
    var ordersTask = clients.CreateClient("orders").SendAsync(ordersRequest, ct);
    try { await Task.WhenAll(catalogTask, ordersTask); }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Results.Problem("A downstream service timed out.", statusCode: StatusCodes.Status504GatewayTimeout); }
    catch (HttpRequestException) { return Results.Problem("A downstream service is unavailable.", statusCode: StatusCodes.Status502BadGateway); }
    using var catalogResponse = catalogTask.Result; using var ordersResponse = ordersTask.Result;
    if (!catalogResponse.IsSuccessStatusCode || !ordersResponse.IsSuccessStatusCode)
        return Results.Problem("A downstream service failed.", statusCode: StatusCodes.Status502BadGateway);
    var catalog = await catalogResponse.Content.ReadFromJsonAsync<object>(cancellationToken: ct);
    var orders = await ordersResponse.Content.ReadFromJsonAsync<object>(cancellationToken: ct);
    return Results.Ok(new { products = catalog, orders });
}).RequireAuthorization();
app.Run();
