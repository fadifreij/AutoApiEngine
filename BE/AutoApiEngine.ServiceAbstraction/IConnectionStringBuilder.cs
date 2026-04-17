using System;
using System.Collections.Generic;
using System.Text;

namespace AutoApiEngine.ServiceAbstraction
{
    public interface IConnectionStringBuilder
    {
        string Build(string server, string database, string? username = null, string? password = null, bool useWindowsAuth = false);
    }
}
