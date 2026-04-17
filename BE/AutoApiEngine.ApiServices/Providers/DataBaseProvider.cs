using AutoApiEngine.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace AutoApiEngine.ApiServices.Providers
{
    public static class DataBaseProvider
    {
        public static void AddDatabaseContext(this WebApplicationBuilder builder, string databaseProvider)
        {
            if (databaseProvider == "MySql")
            {   //ToDo: till now mysql is not supported 
                //builder.Services.AddDbContext<ApplicationDbContext>(options =>
                //    options.UseMySql(builder.Configuration.GetConnectionString("MySqlConnection"),
                //        new MySqlServerVersion(new Version(8, 0, 32))));
                throw new InvalidOperationException("Till now mysql is not supported.");
            }
            else if (databaseProvider == "SqlServer")
            {
                builder.Services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseSqlServer(builder.Configuration.GetConnectionString("SqlServerConnection"),
                    b=>b.MigrationsAssembly("AutoApiEngine.ApiServices")
                    ));
            }
            else
            {
                throw new InvalidOperationException("Invalid database provider specified in configuration.");
            }
        }
    }
}
