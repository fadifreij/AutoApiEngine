using AutoApiEngine.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AutoApiEngine.ApiServices
{
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();

            var databaseProvider = configuration["DatabaseProvider"] ?? "";
            var builder = new DbContextOptionsBuilder<ApplicationDbContext>();

            if (databaseProvider == "MySql")
            {
                var connectionString = configuration.GetConnectionString("MySqlConnection");
                builder.UseMySQL(connectionString, b => b.MigrationsAssembly("AutoApiEngine.ApiServices"));
            }
            else
            {
                var connectionString = configuration.GetConnectionString("SqlServerConnection");
                builder.UseSqlServer(connectionString, b => b.MigrationsAssembly("AutoApiEngine.ApiServices"));
            }

            return new ApplicationDbContext(builder.Options);
        }
    }
}