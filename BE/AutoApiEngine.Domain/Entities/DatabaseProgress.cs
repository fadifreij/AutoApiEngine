using System;
using System.Collections.Generic;
using System.Text;

namespace AutoApiEngine.Domain.Entities
{
    public class DatabaseProgress
    {
        public int Percentage { get; init; }
        public string Message { get; init; } = string.Empty;
    }
}
