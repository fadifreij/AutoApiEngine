using AutoApiEngine.Persistence.Context;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using AutoApiEngine.Services.AuthServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Runtime;

namespace AutoApiEngine.Presentation.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {

        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterRequest request, [FromServices] IAuthService authService)
        {
            var result = await authService.RegisterAsync(request);

            if (!result.Success)
                return BadRequest(new { success = false, error = result.Error });

            return Ok(result);
        }
        
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequest request, [FromServices] KeycloakService keycloakService)
        {
           if (request is null ||  request is { Code: null, RedirectUri: null })
                return BadRequest(new { success = false, error = "Code and redirectUri are required" });
            
            var result = await keycloakService.LoginAsync(request);
            if (!result.Success)
                return Unauthorized(new { success = false, error = result.Error });
            // ✅ set refresh token cookie
            AppendRefreshTokenCookie(result.Response!.Refresh_Token);
            return Ok(new { success = true, token = result.Response!.Access_Token  , refreshToken = result.Response!.Refresh_Token , idToken = result.Response!.Id_Token });
        }

        [HttpPost("refresh-token")]
        public async Task<IActionResult> Refresh([FromServices] KeycloakService keycloakService)
        {
            var refreshToken = Request.Cookies["refresh_token"];

            if (string.IsNullOrEmpty(refreshToken))
                return Unauthorized(new { success = false, error = "No refresh token found" });

            try
            {
                // ✅ reuse generic method
                var tokens = await keycloakService.GetRefreshToken(refreshToken);

                if (tokens is null || tokens.Response is null)
                {
                    ClearRefreshTokenCookie();
                    return Unauthorized(new { success = false, error = "Invalid refresh token" });
                }

                AppendRefreshTokenCookie(tokens.Response!.Refresh_Token);

                return Ok(new { success = true, token = tokens.Response!.Access_Token , refreshToken = tokens.Response!.Refresh_Token, idToken = tokens.Response!.Id_Token });
            }
            catch (Exception ex)
            {
                return Unauthorized(new { success = false, error = ex.Message });
            }
        }


        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] LogoutRequest request, [FromServices] KeycloakService keycloakService)
        {
            // Must delete with same options the cookie was set with, otherwise browser won't clear it
            Response.Cookies.Delete("refresh_token", new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Path = "/"
            });
            var logoutUrl = await keycloakService.LogoutAsync(request.PostLogoutRedirectUri, request.IdTokenHint);

            return Ok(new { logoutUrl });
            
        }

        // ✅ shared helper to avoid repeating cookie options
        private void AppendRefreshTokenCookie(string refreshToken)
        {
            Response.Cookies.Append("refresh_token", refreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Path = "/",
                Expires = DateTimeOffset.UtcNow.AddDays(7)
            });
        }

        private void ClearRefreshTokenCookie()
        {
            // Must delete with the same options the cookie was set with.
            Response.Cookies.Delete("refresh_token", new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Path = "/"
            });
        }


    }

    
}
