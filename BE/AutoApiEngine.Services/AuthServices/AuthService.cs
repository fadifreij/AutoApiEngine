using AutoApiEngine.Domain.Entities;
using AutoApiEngine.Persistence.Context;
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
       
        private readonly ApplicationDbContext _context;
        private readonly KeycloakService _keycloakService;

        public AuthService(
            
            ApplicationDbContext context,
           
            KeycloakService keycloakService)
        {
          
            _context = context;
            _keycloakService = keycloakService;
        }


        public async Task<RegisterResult> RegisterAsync(RegisterRequest request)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {                
                // 1.Check if user already exists by email
                var existingUserId = await _keycloakService.GetUserIdByUsername(request.Email);

                if (!String.IsNullOrEmpty(existingUserId) )
                {
                    return new RegisterResult
                    {
                        Success = false,
                        Error = "A user with this email already exists."
                    };
                }

                // 3. Create or resolve Organization
                Organization? organization;
                
                if (!string.IsNullOrEmpty(request.RegistrationCode))
                {
                    organization = await _context.Organizations
                       .FirstOrDefaultAsync(o => o.RegistrationCode == request.RegistrationCode);

                    if (organization == null)
                        return new RegisterResult { Success = false, Error = "Invalid registration code" };
                }
                else
                {
                    var slug = ToSlug(request.OrganizationName);
                    // 🔴 Check if organization already exists
                    var exists = await _context.Organizations
                        .AnyAsync(o => o.RegistrationCode == slug);

                    if (exists)
                    {
                        return new RegisterResult
                        {
                            Success = false,
                            Error = "Organization already exists. Please choose a different name."
                        };
                    }

                    organization = new Organization
                    {
                        Name = request.OrganizationName,
                        RegistrationCode = slug,
                        TrialEndsAt = DateTime.UtcNow.AddDays(14),
                        IsActive = true
                    };

                    _context.Organizations.Add(organization);
                    await _context.SaveChangesAsync();
                }

                // 4. Create Group in Keycloak if not exists
                string groupId;
                var existingGroup = await _keycloakService.GetGroupIdByName(organization.Name, null);
                if (!string.IsNullOrEmpty(existingGroup))
                {
                    groupId = existingGroup;
                }
                else
                {
                    var group = await _keycloakService.CreateGroup(organization.Name);
                    groupId = group.Id;
                }

                // 5. Create User in Keycloak
                var keycloakUser = await _keycloakService.CreateUser(request.Email, request.Password, organization.Name);

                               
                // 6. Send verification email
                await _keycloakService.SendVerificationEmail(keycloakUser.Id);

                // 7. Add user to group
                await _keycloakService.AddUserToGroup(keycloakUser.Id, groupId);
                Plan? plan;

                if (request.PlanId.HasValue)
                {
                    plan = await _context.Plans.FindAsync(request.PlanId.Value);
                }
                else
                {
                    plan = await _context.Plans.FirstAsync(p => p.Name == "Free");
                }

                // 6. Create Subscription (trial or paid starter)
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

                

                await transaction.CommitAsync();

                return new RegisterResult
                {
                    Success = true,
                    Error = string.Empty,
                    OrganizationId = organization.Id.ToString()
                };
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        
     
        private string ToSlug(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                  return string.Empty;

            string slug = input.ToLowerInvariant();
            slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
            slug = Regex.Replace(slug, @"\s+", " ").Trim();
            slug = slug.Replace(" ", "-");

            return slug;
        }
    }
}
