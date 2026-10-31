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
builder.Services.AddAuthorization(); builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization();
app.MapGet("/health/live", () => Results.Ok(new { status = "live", service = "api-gateway" }));
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready", service = "api-gateway" }));
app.MapReverseProxy(); app.Run();
