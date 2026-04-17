using AutoApiEngine.ServiceAbstraction;
using System;
using System.Collections.Generic;
using System.Text;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    public class MySqlConnectionStringBuilder : IConnectionStringBuilder
    {
        public string Build(string server, string database, string? username = null, string? password = null, bool useWindowsAuth = false)
        {
            // MySQL does not support Windows Authentication like SQL Server
            return $"Server={server};Database={database};Uid={username};Pwd={password};";
        }
    }
}
