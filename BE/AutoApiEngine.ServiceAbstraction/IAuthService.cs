using AutoApiEngine.Domain.Entities;
using AutoApiEngine.ServiceAbstraction.DTO;
using System;
using System.Collections.Generic;
using System.Text;

namespace AutoApiEngine.ServiceAbstraction
{
    public interface IAuthService
    {
        Task<AuthResult> RegisterAsync(RegisterRequest request);
        Task<AuthResult> LoginAsync(string email, string password);
    }
}
