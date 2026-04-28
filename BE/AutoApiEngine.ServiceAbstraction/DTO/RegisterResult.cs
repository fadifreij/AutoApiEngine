using System;
using System.Collections.Generic;
using System.Text;

namespace AutoApiEngine.ServiceAbstraction.DTO
{
    public class RegisterResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
    }
}
