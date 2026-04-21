using System;
using System.Collections.Generic;
using System.Text;

namespace AutoApiEngine.ServiceAbstraction.DTO
{
    public class RefreshRequest
    {
        public string RefreshToken { get; set; } = null!;
    }
}
