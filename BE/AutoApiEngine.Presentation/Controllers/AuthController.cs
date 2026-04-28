using AutoApiEngine.Persistence.Context;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using AutoApiEngine.Services.AuthServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;

namespace AutoApiEngine.Presentation.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        
        public AuthController(ApplicationDbContext context)
        {
            _context = context;  
            
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterRequest request, [FromServices] IAuthService authService)
        {
            var result = await authService.RegisterAsync(request);

            if (!result.Success)
                return BadRequest(new { success = false, error = result.Error });

            return Ok(result);
        }
         
     
    }

    
}