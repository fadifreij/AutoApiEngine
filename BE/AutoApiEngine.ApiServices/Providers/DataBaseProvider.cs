using AutoApiEngine.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace AutoApiEngine.ApiServices.Providers
{
    public static class DataBaseProvider
    {
        public static void AddDatabaseContext(this WebApplicationBuilder builder, string databaseProvider)
        {
            if (databaseProvider == "MySql")
            {
                builder.Services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseMySQL(builder.Configuration.GetConnectionString("MySqlConnection"),
                        b => b.MigrationsAssembly("AutoApiEngine.ApiServices")));
            }
            else if (databaseProvider == "SqlServer")
            {
                builder.Services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseSqlServer(builder.Configuration.GetConnectionString("SqlServerConnection"),
                    b => b.MigrationsAssembly("AutoApiEngine.ApiServices")));
            }
            else
            {
                throw new InvalidOperationException("Invalid database provider specified in configuration.");
            }
        }
    }
}