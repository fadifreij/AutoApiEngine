using System;
using System.Collections.Generic;
using System.Text;

namespace AutoApiEngine.ServiceAbstraction.DTO
{
    public class LoginRequest
    {
        public  string Code { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = string.Empty;
    }
}
