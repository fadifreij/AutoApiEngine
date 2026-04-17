using System;
using System.Collections.Generic;
using System.Text;

namespace AutoApiEngine.Domain.Entities
{
    public class DatabaseOptions
    {
        public string ConnectionString { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = string.Empty;
    }
}
