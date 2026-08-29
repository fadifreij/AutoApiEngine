using AutoApiEngine.Domain.Entities;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoApiEngine.Presentation.Controllers
{
    [ApiController]
    [Route("api/keys")]
    [Authorize]
    public class ApiKeyController : BaseController
    {
        private readonly IApiKeyRepository _apiKeyRepository;
        private readonly IOrganizationRepository _organizationRepository;

        public ApiKeyController(
            IApiKeyRepository apiKeyRepository,
            IOrganizationRepository organizationRepository)
        {
            _apiKeyRepository = apiKeyRepository;
            _organizationRepository = organizationRepository;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(CancellationToken cancellationToken = default)
        {
            return await HandleRequestAsync(async () =>
            {
                var apiKeys = await _apiKeyRepository.GetAllAsync(cancellationToken);
                return apiKeys;
            });
        }

        [HttpGet("organization/{organizationId}")]
        public async Task<IActionResult> GetByOrganizationId(string organizationId, CancellationToken cancellationToken = default)
        {
            return await HandleRequestAsync(async () =>
            {
                if (!Guid.TryParse(organizationId, out var orgId))
                    throw new ArgumentException("Invalid organization id format.");

                var apiKeys = await _apiKeyRepository.GetByOrganizationIdAsync(orgId, cancellationToken);
                return apiKeys;
            });
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(string id, CancellationToken cancellationToken = default)
        {
            return await HandleRequestAsync(async () =>
            {
                var apiKey = await _apiKeyRepository.GetByIdWithOrganizationAsync(id, cancellationToken);
                return apiKey;
            });
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateApiKeyDto dto, CancellationToken cancellationToken = default)
        {
            return await HandleRequestAsync(async () =>
            {
                if (string.IsNullOrWhiteSpace(dto.Name))
                    throw new ArgumentException("API key name is required.");

                // Resolve organization: prefer dto.OrganizationId, fallback to JWT claim
                Organization? organization = null;
                if (dto.OrganizationId != Guid.Empty)
                {
                    organization = (await _organizationRepository.FindAsync(o => o.Id == dto.OrganizationId, cancellationToken)).FirstOrDefault();
                }

                if (organization is null)
                {
                    var orgName = User.FindFirst("organization")?.Value;
                    if (!string.IsNullOrWhiteSpace(orgName))
                        organization = (await _organizationRepository.FindAsync(o => o.Name == orgName, cancellationToken)).FirstOrDefault();
                }

                if (organization is null)
                    throw new ArgumentException("Organization not found.");

                var exists = await _apiKeyRepository.ExistsByNameAndOrganizationAsync(dto.Name, organization.Id, null, cancellationToken);
                if (exists)
                    throw new ArgumentException("An API key with this name already exists in your organization. Please choose a different name.");

                var plainKey = GenerateKey();
                var apiKey = new ApiKey
                {
                    Id = Guid.NewGuid(),
                    Name = dto.Name,
                    Key = HashKey(plainKey),
                    ExpiresAt = dto.ExpiresAt,
                    IsActive = true,
                    OrganizationId = organization.Id,
                    Organization = organization
                };

                await _apiKeyRepository.AddAsync(apiKey, cancellationToken);
                return new
                {
                    apiKey.Id,
                    apiKey.Name,
                    plainKey, // plaintext key returned ONLY at creation
                    apiKey.ExpiresAt,
                    apiKey.IsActive,
                    apiKey.OrganizationId
                };
            });
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(string id, [FromBody] UpdateApiKeyDto dto, CancellationToken cancellationToken = default)
        {
            return await HandleRequestAsync(async () =>
            {
                var apiKey = await _apiKeyRepository.GetByIdAsync(id, cancellationToken);

                if (string.IsNullOrWhiteSpace(dto.Name))
                    throw new ArgumentException("API key name is required.");

                var duplicate = await _apiKeyRepository.ExistsByNameAndOrganizationAsync(dto.Name, apiKey.OrganizationId, apiKey.Id, cancellationToken);
                if (duplicate)
                    throw new ArgumentException("An API key with this name already exists in your organization. Please choose a different name.");

                apiKey.Name = dto.Name;
                apiKey.IsActive = dto.IsActive;
                apiKey.ExpiresAt = dto.ExpiresAt;

                await _apiKeyRepository.UpdateAsync(apiKey, cancellationToken);
                return apiKey;
            });
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken = default)
        {
            return await HandleRequestAsync(async () =>
            {
                await _apiKeyRepository.DeleteAsync(id, cancellationToken);
                return new { message = "API key deleted successfully." };
            });
        }

        private static string GenerateKey()
        {
            // 64 hex chars — cryptographically strong enough for an API key
            return Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        }

        private static string HashKey(string plainKey)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(plainKey));
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
    }
}