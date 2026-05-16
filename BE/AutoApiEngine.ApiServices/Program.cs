using AutoApiEngine.ApiServices.Providers;
using AutoApiEngine.Persistence.Context;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.Services.AuthServices;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
      .AddJsonOptions(options =>
      {
          options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
          options.JsonSerializerOptions.WriteIndented = true;
          options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
      });

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:4200", "https://localhost:7002")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
              .SetIsOriginAllowed(_ => true);
    });
});

builder.Services.AddOpenApi();
builder.Services.AddApiServices(builder.Configuration);

var keyclockSection = builder.Configuration.GetSection("KeyClock");
var realmUrl = $"{keyclockSection["Url"]}/realms/{keyclockSection["Realm"]}";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = realmUrl;
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = realmUrl,
            ValidateAudience = false,
            ValidateLifetime = true
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = context =>
            {
                if (context.AuthenticateFailure != null)
                {
                    context.HandleResponse();
                }
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

var databaseProvider = builder.Configuration["DatabaseProvider"] ?? "";
builder.AddDatabaseContext(databaseProvider);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
