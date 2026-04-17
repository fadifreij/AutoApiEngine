using AutoApiEngine.ServiceAbstraction;
using System;
using System.Collections.Generic;
using System.Text;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    public class SqlServerConnectionStringBuilder : IConnectionStringBuilder
    {
        public string Build(string server, string database, string? username = null, string? password = null, bool useWindowsAuth = false)
        {
            if (useWindowsAuth)
            {
                return $"Server={server};Database={database};Trusted_Connection=True;TrustServerCertificate=True;";
            }

            return $"Server={server};Database={database};User Id={username};Password={password};TrustServerCertificate=True;";
        }
    }
}
