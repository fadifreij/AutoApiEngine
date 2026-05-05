using System;
using System.Collections.Generic;
using System.Text;

namespace AutoApiEngine.ServiceAbstraction.DTO
{
    public class SetRefreshTokenRequest
    {
        public string RefreshToken { get; set; } = string.Empty;
    }
}
