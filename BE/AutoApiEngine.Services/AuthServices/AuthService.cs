using AutoApiEngine.Domain.Entities;
using AutoApiEngine.Persistence.Context;
using AutoApiEngine.Persistence.models;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace AutoApiEngine.Services.AuthServices
{
    public class AuthService : IAuthService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly JwtService _jwtService;

        public AuthService(
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            JwtService jwtService)
        {
            _userManager = userManager;
            _context = context;
            _jwtService = jwtService;
        }


        public async Task<AuthResult> RegisterAsync(RegisterRequest request)
        {
            // 1. Create or resolve Organization
            Organization? organization;
            // Todo : need to change the logic and to create organization with code
            if (!string.IsNullOrEmpty(request.RegistrationCode))
            {
                organization = await _context.Organizations
                    .FirstOrDefaultAsync(o => o.RegistrationCode == request.RegistrationCode);

                if (organization == null)
                    return new AuthResult { Success = false, Error = "Invalid registration code" };
            }
            else
            {
                var slug = ToSlug(request.OrganizationName);
                // 🔴 Check if organization already exists
                var exists = await _context.Organizations
                    .AnyAsync(o => o.RegistrationCode == slug);

                if (exists)
                {
                    return new AuthResult
                    {
                        Success = false,
                        Error = "Organization already exists. Please choose a different name."
                    };
                }

                organization = new Organization
                {
                    Name = request.OrganizationName,
                    RegistrationCode = slug,
                    TrialEndsAt = DateTime.UtcNow.AddDays(14)
                };

                _context.Organizations.Add(organization);
                await _context.SaveChangesAsync();
            }

            // 2. Create User
            // Check if user already exists by email
            var existingUser = await _userManager.FindByEmailAsync(request.Email);

            if (existingUser != null)
            {
                return new AuthResult
                {
                    Success = false,
                    Error = "A user with this email already exists."
                };
            }
            var user = new ApplicationUser
            {
                Email = request.Email,
                UserName = request.Email,
                OrganizationId = organization.Id
            };

            var createUser = await _userManager.CreateAsync(user, request.Password);

            if (!createUser.Succeeded)
            {
                return new AuthResult
                {
                    Success = false,
                    Error = string.Join(", ", createUser.Errors.Select(e => e.Description))
                };
            }

            // 3. Resolve Plan (HYBRID LOGIC)
            Plan? plan;

            if (request.PlanId.HasValue)
            {
                plan = await _context.Plans.FindAsync(request.PlanId.Value);
            }
            else
            {
                plan = await _context.Plans.FirstAsync(p => p.Name == "Free");
            }

            // 4. Create Subscription (trial or paid starter)
            var subscription = new Subscription
            {
                OrganizationId = organization.Id,
                PlanId = plan!.Id,
                StartDate = DateTime.UtcNow,
                EndDate = DateTime.UtcNow.AddDays(plan.DurationInDays),
                AmountPaid = plan.Price
            };

            _context.Subscriptions.Add(subscription);
            await _context.SaveChangesAsync();

            // 5. Generate JWT + Refresh Token
            var accessToken = _jwtService.GenerateToken(user);
            var refreshToken = _jwtService.GenerateRefreshToken();

            // Store refresh token in DB
            var refreshTokenEntity = new RefreshToken
            {
                Token = refreshToken,
                UserId = user.Id,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                CreatedAt = DateTime.UtcNow
            };

            _context.RefreshTokens.Add(refreshTokenEntity);
            await _context.SaveChangesAsync();

            return new AuthResult
            {
                Success = true,
                Token = accessToken,
                RefreshToken = refreshToken
            };
        }

        public async Task<AuthResult> LoginAsync(string email, string password)
        {
            var user = await _userManager.FindByEmailAsync(email);

            if (user == null)
                return new AuthResult { Success = false, Error = "Invalid credentials" };

            var valid = await _userManager.CheckPasswordAsync(user, password);

            if (!valid)
                return new AuthResult { Success = false, Error = "Invalid credentials" };

            var accessToken = _jwtService.GenerateToken(user);
            var refreshToken = _jwtService.GenerateRefreshToken();

            var refreshTokenEntity = new RefreshToken
            {
                Token = refreshToken,
                UserId = user.Id,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                CreatedAt = DateTime.UtcNow
            };

            _context.RefreshTokens.Add(refreshTokenEntity);
            await _context.SaveChangesAsync();

            return new AuthResult
            {
                Success = true,
                Token = accessToken,
                RefreshToken = refreshToken
            };
        }

        

         private string ToSlug(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                  return string.Empty;

            // Convert to lowercase
            string slug = input.ToLowerInvariant();

            // Remove invalid chars
            slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");

            // Replace multiple spaces with one
            slug = Regex.Replace(slug, @"\s+", " ").Trim();

            // Replace spaces with hyphens
            slug = slug.Replace(" ", "-");

            return slug;
    }
}
}
