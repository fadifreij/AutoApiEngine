using System;
using System.Collections.Generic;
using System.Text;

namespace AutoApiEngine.ServiceAbstraction.DTO
{
    public class LogoutRequest
    {
        public string PostLogoutRedirectUri { get; set; }  = string.Empty;
        public string? IdTokenHint { get; set; }
    }
}
