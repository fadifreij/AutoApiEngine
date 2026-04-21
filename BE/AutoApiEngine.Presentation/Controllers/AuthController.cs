using AutoApiEngine.Persistence.Context;
using AutoApiEngine.Persistence.models;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using AutoApiEngine.Services.AuthServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace AutoApiEngine.Presentation.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        //  private readonly IAuthService _authService;
        private readonly ApplicationDbContext _context;
        private readonly JwtService _jwtService;
        public AuthController(ApplicationDbContext context, JwtService jwtService)
        {
            _context = context;  
            _jwtService = jwtService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterRequest request, [FromServices] IAuthService authService)
        {
            var result = await authService.RegisterAsync(request);

            if (!result.Success)
                return BadRequest(result.Error);

            return Ok(result);
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequest request, [FromServices] IAuthService authService)
        {
            var result = await authService.LoginAsync(request.Email, request.Password);

            if (!result.Success)
                return Unauthorized(result.Error);

            return Ok(result);
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshRequest request)
        {
            var refreshToken = await _context.RefreshTokens
                .Include(r => r.User)
                .FirstOrDefaultAsync(r => r.Token == request.RefreshToken);

            if (refreshToken == null)
                return Unauthorized("Invalid refresh token");

            if (!refreshToken.IsActive)
                return Unauthorized("Refresh token is expired or revoked");

            var user = refreshToken.User;

            // 🔁 Generate new JWT
            var newAccessToken = _jwtService.GenerateToken(user);

            // 🔄 Rotate refresh token (important security step)
            var newRefreshTokenValue = _jwtService.GenerateRefreshToken();

            var newRefreshToken = new RefreshToken
            {
                Token = newRefreshTokenValue,
                UserId = user.Id,
                ExpiresAt = DateTime.UtcNow.AddDays(7)
               
            };

            // Revoke old token
            refreshToken.RevokedAt = DateTime.UtcNow;
           

            _context.RefreshTokens.Add(newRefreshToken);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                accessToken = newAccessToken,
                refreshToken = newRefreshTokenValue
            });
        }

    }
}
