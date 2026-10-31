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
builder.Services.AddHttpClient("identity", client => { client.BaseAddress = new Uri(builder.Configuration["Services:Identity"] ?? "http://localhost:5100"); client.Timeout = TimeSpan.FromSeconds(3); });
builder.Services.AddHttpClient("catalog", client => { client.BaseAddress = new Uri(builder.Configuration["Services:Catalog"] ?? "http://localhost:5101"); client.Timeout = TimeSpan.FromSeconds(3); });
builder.Services.AddHttpClient("orders", client => { client.BaseAddress = new Uri(builder.Configuration["Services:Orders"] ?? "http://localhost:5102"); client.Timeout = TimeSpan.FromSeconds(3); });
builder.Services.AddHttpClient("web-bff", client => { client.BaseAddress = new Uri(builder.Configuration["Services:WebBff"] ?? "http://localhost:5103"); client.Timeout = TimeSpan.FromSeconds(3); });
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization();
app.MapGet("/health/live", () => Results.Ok(new { status = "live", service = "api-gateway" }));
app.MapGet("/health/ready", async (IHttpClientFactory clients, CancellationToken ct) => {
    try {
        var names = new[] { "identity", "catalog", "orders", "web-bff" };
        var checks = await Task.WhenAll(names.Select(name => clients.CreateClient(name).GetAsync("/health/ready", ct)));
        return checks.All(response => response.IsSuccessStatusCode)
            ? Results.Ok(new { status = "ready", service = "api-gateway" })
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
    catch (HttpRequestException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
});
app.MapReverseProxy(); app.Run();
