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
    }
}
