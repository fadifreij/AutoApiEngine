using System;
using System.Collections.Generic;
using System.Text;

namespace AutoApiEngine.ServiceAbstraction.DTO
{
    // This class is used to bind the Keycloak settings from the configuration file
    public class KeyclockSettings
    {
        public string Url { get; set; } = string.Empty;
        public string Realm { get; set; } = string.Empty;
        public string  ClientId { get; set; } = string.Empty;
        public string ApiClientId { get; set; } = string.Empty;
        public string ApiClientSecret { get; set; } = string.Empty;
        public ClientJwtKeySettings? ClientJwtKey { get; set; }
    }

    public class ClientJwtKeySettings
    {
        public string Kid { get; set; } = string.Empty;
        public string N { get; set; } = string.Empty;
        public string E { get; set; } = string.Empty;
        public string D { get; set; } = string.Empty;
        public string P { get; set; } = string.Empty;
        public string Q { get; set; } = string.Empty;
        public string DP { get; set; } = string.Empty;
        public string DQ { get; set; } = string.Empty;
        public string QI { get; set; } = string.Empty;
    }
}
