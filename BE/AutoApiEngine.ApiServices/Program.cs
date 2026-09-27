using AutoApiEngine.ApiServices.Providers;
using AutoApiEngine.Persistence.Context;
using AutoApiEngine.Presentation.HubServices;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.Services.AuthServices;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Local, git-ignored overrides (e.g. Ai:ApiKey). Loaded last so it wins over appsettings.json.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddControllers()
      .AddJsonOptions(options =>
      {
          options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
          options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
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
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },
            OnChallenge = async context =>
            {
                // A failed token validation used to be swallowed silently: HandleResponse()
                // suppresses the framework's default 401, and the handler then wrote nothing in
                // its place. Nothing ever touched the response, so Kestrel finalised its untouched
                // default - 200 OK, zero bytes, no Content-Type - and the action never ran.
                // A bad token now gets a real, diagnosable 401.
                if (context.AuthenticateFailure == null)
                {
                    // No credentials at all: keep the framework default so the
                    // WWW-Authenticate challenge header is still emitted.
                    return;
                }

                context.HandleResponse();

                if (context.Response.HasStarted)
                {
                    // Too late to change the status; whatever is on the wire has to stand.
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "UNAUTHORIZED",
                    message = "Invalid, expired or inactive access token."
                });
            },
            OnForbidden = async context =>
            {
                // The token was accepted but the authorization policy/claim check failed.
                // Answer with a body instead of the framework's bare, bodyless 403.
                if (context.Response.HasStarted)
                {
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "FORBIDDEN",
                    message = "You do not have permission to access this resource."
                });
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSignalR();

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

app.MapHub<ProgressHub>("/hubs/progress").AllowAnonymous();
app.MapControllers();

app.Run();
