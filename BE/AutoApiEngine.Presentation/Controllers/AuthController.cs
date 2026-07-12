using AutoApiEngine.Persistence.Context;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using AutoApiEngine.Services.AuthServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Runtime;

namespace AutoApiEngine.Presentation.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;

        public AuthController(IWebHostEnvironment env)
        {
            _env = env;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterRequest request, [FromServices] IAuthService authService)
        {
            var result = await authService.RegisterAsync(request);

            if (!result.Success)
                return BadRequest(new { success = false, error = result.Error });

            return Ok(result);
        }
        
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequest request, [FromServices] KeycloakService keycloakService, [FromServices] ApplicationDbContext context)
        {
           if (request is null ||  request is { Code: null, RedirectUri: null })
                return BadRequest(new { success = false, error = "Code and redirectUri are required" });
            
            var result = await keycloakService.LoginAsync(request);
            if (!result.Success)
                return Unauthorized(new { success = false, error = result.Error });

            AppendRefreshTokenCookie(result.Response!.Refresh_Token);

            var orgId = await ResolveOrganizationId(result.Response!.Access_Token, context);

            return Ok(new { success = true, token = result.Response!.Access_Token  , refreshToken = result.Response!.Refresh_Token , idToken = result.Response!.Id_Token, organizationId = orgId });
        }

        [HttpPost("refresh-token")]
        public async Task<IActionResult> Refresh([FromServices] KeycloakService keycloakService, [FromServices] ApplicationDbContext context)
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

                var orgId = await ResolveOrganizationId(tokens.Response!.Access_Token, context);

                return Ok(new { success = true, token = tokens.Response!.Access_Token , refreshToken = tokens.Response!.Refresh_Token, idToken = tokens.Response!.Id_Token, organizationId = orgId });
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
            Response.Cookies.Delete("refresh_token", GetCookieOptions(days: 0));
            var logoutUrl = await keycloakService.LogoutAsync(request.PostLogoutRedirectUri, request.IdTokenHint);

            return Ok(new { logoutUrl });
            
        }

        private CookieOptions GetCookieOptions(int days = 7)
        {
            // The SPA (http://localhost:4200) calls this API cross-site (different origin/scheme),
            // so the browser only sends the cookie back when it is SameSite=None + Secure.
            // The backend is served over HTTPS in both dev and prod, so Secure=true is always valid.
            return new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Path = "/",
                Expires = DateTimeOffset.UtcNow.AddDays(days)
            };
        }

        private void AppendRefreshTokenCookie(string refreshToken)
        {
            Response.Cookies.Append("refresh_token", refreshToken, GetCookieOptions());
        }

        private static async Task<string?> ResolveOrganizationId(string accessToken, ApplicationDbContext context)
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var jwt = handler.ReadJwtToken(accessToken);
                var orgName = jwt.Claims.FirstOrDefault(c => c.Type == "organization")?.Value;

                if (string.IsNullOrEmpty(orgName))
                    return null;

                var org = await context.Organizations.FirstOrDefaultAsync(o => o.Name == orgName);
                return org?.Id.ToString();
            }
            catch
            {
                return null;
            }
        }

        private void ClearRefreshTokenCookie()
        {
            Response.Cookies.Delete("refresh_token", GetCookieOptions(days: 0));
        }


    }

    
}
