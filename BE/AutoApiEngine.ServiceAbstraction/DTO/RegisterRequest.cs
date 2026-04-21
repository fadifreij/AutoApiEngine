using System;
using System.Collections.Generic;
using System.Text;

namespace AutoApiEngine.ServiceAbstraction.DTO
{
    public class RegisterRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        public string OrganizationName { get; set; } = string.Empty;

        // optional: if user registers with a code (invite flow)
        public string? RegistrationCode { get; set; }

        // optional: if user selects plan during signup
        public Guid? PlanId { get; set; }
    }
}
