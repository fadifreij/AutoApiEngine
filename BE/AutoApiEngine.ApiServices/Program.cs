using AutoApiEngine.ApiServices.Providers;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
      .AddJsonOptions(options =>
      {
          options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
          options.JsonSerializerOptions.WriteIndented = true;
      }
      ); ;

builder.Services.AddOpenApi();


// Read the database provider from configuration
var databaseProvider = builder.Configuration["DatabaseProvider"] ?? "";
builder.AddDatabaseContext(databaseProvider);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
