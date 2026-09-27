using AutoApiEngine.Domain.Entities;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AutoApiEngine.Presentation.Controllers
{
    /// <summary>
    /// Management API for API key scopes (plan §5). <b>JWT-only</b> — this controller is how an
    /// administrator grants and revokes scopes; the runtime enforcement on the dynamic CRUD endpoints
    /// is the job of <c>ApiKeyAuthFilter</c>, not of this controller.
    ///
    /// Three invariants shape most of the code here:
    /// <list type="bullet">
    /// <item><b>D12 — deny always wins</b>, so only <i>additive</i> changes are accepted. A naive create
    /// could silently override a grant or a deny, so every non-additive create is rejected up front.</item>
    /// <item><b>D2 — the wildcard is <c>null</c></b>, never <c>""</c> and never <c>"*"</c>. An empty
    /// string is a real (unmatched) object name, so accepting it would silently create a dead scope.</item>
    /// <item><b>D10 — org containment</b>: the route key, the body workspace and the JWT claim must all
    /// resolve to the same organization.</item>
    /// </list>
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/keys/{apiKeyId:guid}/permissions")]
    public class ApiKeyPermissionController : BaseController
    {
        private readonly IApiKeyPermissionRepository _permissionRepository;
        private readonly IApiKeyRepository _apiKeyRepository;
        private readonly IWorkspaceRepository _workspaceRepository;
        private readonly IOrganizationRepository _organizationRepository;
        private readonly IDynamicApiMetadataService _metadataService;

        public ApiKeyPermissionController(
            IApiKeyPermissionRepository permissionRepository,
            IApiKeyRepository apiKeyRepository,
            IWorkspaceRepository workspaceRepository,
            IOrganizationRepository organizationRepository,
            IDynamicApiMetadataService metadataService)
        {
            _permissionRepository = permissionRepository;
            _apiKeyRepository = apiKeyRepository;
            _workspaceRepository = workspaceRepository;
            _organizationRepository = organizationRepository;
            _metadataService = metadataService;
        }

        // ─────────────────────────────────────────────────────────────
        //  Reads
        // ─────────────────────────────────────────────────────────────

        /// <summary>All scopes granted on one API key.</summary>
        [HttpGet]
        public async Task<IActionResult> GetByApiKey(Guid apiKeyId, CancellationToken cancellationToken = default)
        {
            return await HandleAsync(async () =>
            {
                var org = await ResolveCallerOrganizationAsync(cancellationToken);
                await ResolveOwnedKeyAsync(apiKeyId, org, cancellationToken);

                var rows = await _permissionRepository.GetByApiKeyIdAsync(apiKeyId, cancellationToken);
                return await ProjectAsync(rows, cancellationToken);
            });
        }

        /// <summary>
        /// Every scope visible to one organization — the feed for the Scopes tab. Scoped by
        /// <c>ApiKey.OrganizationId</c> <i>or</i> the owning workspace's <c>OrganizationId</c>, so a
        /// cross-org row is at least visible to the org that owns the workspace (a recovery path, not
        /// an authorization mechanism).
        /// </summary>
        [HttpGet("~/api/permissions")]
        public async Task<IActionResult> GetByOrganization([FromQuery] string organizationId, CancellationToken cancellationToken = default)
        {
            return await HandleAsync(async () =>
            {
                var org = await ResolveCallerOrganizationAsync(cancellationToken);

                if (!Guid.TryParse(organizationId, out var requestedOrgId))
                    throw new ArgumentException("Invalid organization id format.");

                // A caller may only ever read their own organization's scopes. (The pre-existing
                // IDOR in ApiKeyController.GetByOrganizationId is a separate, already-documented gap
                // and is deliberately NOT fixed here — but this new endpoint does not repeat it.)
                if (requestedOrgId != org.Id)
                    throw new OrgAccessDeniedException("Access denied. You can only list scopes for your own organization.");

                var rows = await _permissionRepository.GetByOrganizationAsync(org.Id, cancellationToken);
                return await ProjectAsync(rows, cancellationToken);
            });
        }

        // ─────────────────────────────────────────────────────────────
        //  Create / Delete
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Grants or denies one scope. Rejects every non-additive change — see the class remarks.
        /// Order matters: org containment (403) runs before any field or existence probing, so a
        /// cross-org caller learns nothing about which scopes exist.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create(Guid apiKeyId, [FromBody] CreateApiKeyPermissionDto dto, CancellationToken cancellationToken = default)
        {
            return await HandleAsync<object>(async () =>
            {
                // ── 1. Org containment (403). Before anything that could leak existence. ──
                var org = await ResolveCallerOrganizationAsync(cancellationToken);
                var apiKey = await ResolveOwnedKeyAsync(apiKeyId, org, cancellationToken);

                // ── 2. Field validation (400) ──
                if (dto is null)
                    throw new ArgumentException("A request body is required.");

                if (dto.WorkspaceId == Guid.Empty)
                    throw new ArgumentException("WorkspaceId is required.");

                var objectName = NormalizeObjectName(dto.ObjectName);
                var verb = NormalizeVerb(dto.Verb);

                if (dto.IsDeny && objectName is null)
                    throw new ArgumentException("A deny must name a specific object, so ObjectName is required when IsDeny is true.");

                var workspace = await ResolveOwnedWorkspaceAsync(dto.WorkspaceId, org, cancellationToken);

                if (string.IsNullOrWhiteSpace(workspace.DatabaseName))
                    throw new ArgumentException("That workspace has no database associated with it, so a scope cannot be recorded against it.");

                var databaseName = workspace.DatabaseName!;
                if (!string.IsNullOrWhiteSpace(dto.DatabaseName) &&
                    !string.Equals(dto.DatabaseName.Trim(), databaseName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"DatabaseName must match the workspace's database ('{databaseName}').");
                }

                if (objectName is not null)
                {
                    // Existence check against the target database. A typo'd object name would otherwise
                    // create a scope that can never match any request. Note this needs a live connection
                    // to the workspace's database, so an unreachable target surfaces as a 500 rather than
                    // silently accepting an unverified name.
                    await _metadataService.GetObjectMetadataAsync(workspace.Id.ToString(), objectName, cancellationToken);
                }

                // ── 3. Exact duplicate (409) ──
                // Checked explicitly rather than left to the unique index: SQL Server treats NULL = NULL
                // inside a UNIQUE index, but MySQL treats NULLs as DISTINCT, so on MySQL the index
                // happily accepts a second (key, NULL, NULL, ws, db) row (verified against the dev DB).
                // IsDeny is part of the test because the index deliberately excludes it, so a grant and
                // a deny at the same 5-tuple cannot coexist and one is not a duplicate of the other.
                if (await _permissionRepository.ExistsExactScopeAsync(apiKeyId, objectName, verb, workspace.Id, databaseName, dto.IsDeny, cancellationToken))
                    throw new DuplicateScopeException($"This exact scope already exists for \"{apiKey.Name}\": {FormatScope(objectName, verb)}.");

                // ── 4. Redundancy / deny conflict (400) — additive-only enforcement ──
                if (!dto.IsDeny)
                {
                    // A broader grant of the same key already covers this scope. Redundancy is semantic,
                    // not structural: the unique index cannot see (k, NULL, GET, …) vs (k, Orders, GET, …),
                    // so this check is the only guard against a meaningless duplicate grant.
                    if (await _permissionRepository.ExistsBroaderScopeAsync(apiKeyId, objectName, verb, workspace.Id, databaseName, isDeny: false, cancellationToken))
                        throw new ArgumentException($"This scope is already covered by a broader grant: {FormatScope(objectName, verb)}.");

                    // Deny wins over grant, so a grant on a denied object is a non-additive change.
                    // ExistsDenyAsync covers both levels: L1 when verb is supplied, L1-or-L2 when null.
                    if (await _permissionRepository.ExistsDenyAsync(apiKeyId, objectName!, verb, workspace.Id, databaseName, cancellationToken))
                        throw new ArgumentException($"{objectName} is denied for this key. Remove the deny before granting.");
                }
                else
                {
                    if (await _permissionRepository.ExistsBroaderScopeAsync(apiKeyId, objectName, verb, workspace.Id, databaseName, isDeny: true, cancellationToken))
                        throw new ArgumentException($"This deny is already covered by a broader deny: {FormatScope(objectName, verb)}.");

                    // Only an L2 deny (verb = null, i.e. "deny all verbs on this object") can collide with
                    // a narrower grant. An L1 deny names the same 5-tuple as a grant, which the unique
                    // index already forbids, so it needs no extra check here.
                    if (verb is null)
                    {
                        var explicitGrants = await _permissionRepository.GetExplicitVerbGrantsAsync(apiKeyId, objectName!, workspace.Id, databaseName, cancellationToken);
                        var verbs = explicitGrants.Select(g => g.Verb!).Where(v => !string.IsNullOrEmpty(v)).Distinct().ToList();
                        if (verbs.Count > 0)
                            throw new ArgumentException($"{objectName} already has an explicit {string.Join(", ", verbs)} grant. Remove it before denying all verbs.");
                    }
                }

                // ── 5. Insert ──
                var permission = new ApiKeyPermission
                {
                    Id = Guid.NewGuid(),
                    ApiKeyId = apiKeyId,
                    ObjectName = objectName,
                    Verb = verb,
                    WorkspaceId = workspace.Id,
                    DatabaseName = databaseName,
                    IsDeny = dto.IsDeny
                };

                await _permissionRepository.AddAsync(permission, cancellationToken);

                return (object)new
                {
                    permission.Id,
                    permission.ApiKeyId,
                    apiKey.Name,
                    permission.ObjectName,
                    permission.Verb,
                    permission.IsDeny,
                    permission.WorkspaceId,
                    WorkspaceName = workspace.Name,
                    permission.DatabaseName,
                    permission.CreatedAt
                };
            });
        }

        /// <summary>Revokes one scope. Narrowing a broad grant re-opens what it covered — see the FE warning.</summary>
        [HttpDelete("{permissionId:guid}")]
        public async Task<IActionResult> Delete(Guid apiKeyId, Guid permissionId, CancellationToken cancellationToken = default)
        {
            return await HandleAsync<object>(async () =>
            {
                var org = await ResolveCallerOrganizationAsync(cancellationToken);
                await ResolveOwnedKeyAsync(apiKeyId, org, cancellationToken);

                var permission = await _permissionRepository.GetByIdAsync(permissionId.ToString(), cancellationToken);

                // The permission must belong to the key named in the route, otherwise the URL is lying
                // about which key is being modified.
                if (permission.ApiKeyId != apiKeyId)
                    throw new KeyNotFoundException($"Permission {permissionId} does not belong to API key {apiKeyId}.");

                await _permissionRepository.DeleteAsync(permissionId.ToString(), cancellationToken);
                return (object)new { message = "Permission removed." };
            });
        }

        // ─────────────────────────────────────────────────────────────
        //  Org containment (D10)
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves the caller's organization from the JWT <c>organization</c> claim. The claim carries an
        /// organization <b>Name</b>, so it is resolved to an Id once and compared as a Guid from then on —
        /// more robust than string-comparing names, which breaks if an org is renamed.
        /// A missing or unresolvable claim is 403, matching <c>DynamicApiController.VerifyWorkspaceAccessAsync</c>.
        /// </summary>
        private async Task<Organization> ResolveCallerOrganizationAsync(CancellationToken cancellationToken)
        {
            var orgName = User.FindFirst("organization")?.Value;
            if (string.IsNullOrWhiteSpace(orgName))
                throw new OrgAccessDeniedException("Organization claim not found in token.");

            var organization = (await _organizationRepository.FindAsync(o => o.Name == orgName, cancellationToken)).FirstOrDefault();
            if (organization is null)
                throw new OrgAccessDeniedException("Access denied. Your organization could not be resolved.");

            return organization;
        }

        /// <summary>
        /// Loads the route's API key and asserts it belongs to the caller's organization. A key that exists
        /// but is foreign is 403 (not 404) — the same distinction the workspace helper makes, and the
        /// duplication is deliberate because the two lookups fail differently.
        /// </summary>
        private async Task<ApiKey> ResolveOwnedKeyAsync(Guid apiKeyId, Organization organization, CancellationToken cancellationToken)
        {
            var apiKey = await _apiKeyRepository.GetByIdWithOrganizationAsync(apiKeyId.ToString(), cancellationToken);

            if (apiKey.OrganizationId != organization.Id)
                throw new OrgAccessDeniedException("Access denied. This API key does not belong to your organization.");

            return apiKey;
        }

        /// <summary>
        /// Loads the body workspace and asserts it belongs to the caller's organization. Existence alone is
        /// not enough — that is the whole point of D10. A missing workspace is a 400 (it is an invalid
        /// field value); a foreign one is a 403.
        /// </summary>
        private async Task<Workspace> ResolveOwnedWorkspaceAsync(Guid workspaceId, Organization organization, CancellationToken cancellationToken)
        {
            Workspace workspace;
            try
            {
                workspace = await _workspaceRepository.GetByIdWithOrganizationAsync(workspaceId.ToString(), cancellationToken);
            }
            catch (KeyNotFoundException)
            {
                throw new ArgumentException($"Workspace with id {workspaceId} not found.");
            }

            if (workspace.OrganizationId != organization.Id)
                throw new OrgAccessDeniedException("Access denied. That workspace does not belong to your organization.");

            return workspace;
        }

        // ─────────────────────────────────────────────────────────────
        //  Field normalization (D2 — the wildcard is null, never "" and never "*")
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// <c>null</c> means "any object" and is returned as-is. Everything else is trimmed and must be a
        /// real object name: an empty/whitespace string is <b>not</b> a wildcard (it would create a scope
        /// that matches nothing), and <c>"*"</c> is rejected outright so the only spelling of the
        /// wildcard stays <c>null</c>.
        /// </summary>
        private static string? NormalizeObjectName(string? objectName)
        {
            if (objectName is null)
                return null;

            var trimmed = objectName.Trim();

            if (trimmed.Length == 0)
                throw new ArgumentException("ObjectName must be null (or omitted) to mean any object. An empty string is not a wildcard.");

            if (trimmed == "*")
                throw new ArgumentException("\"*\" is not a wildcard. Send ObjectName as null (or omit it) to mean any object.");

            return trimmed;
        }

        /// <summary>
        /// <c>null</c> means "any verb" and is returned as-is. A supplied verb is upper-cased, because the
        /// filter compares it against <c>Request.Method.ToUpperInvariant()</c> — storing a lower-case verb
        /// would create a row that never matches.
        /// </summary>
        private static string? NormalizeVerb(string? verb)
        {
            if (verb is null)
                return null;

            var trimmed = verb.Trim().ToUpperInvariant();

            if (trimmed.Length == 0)
                throw new ArgumentException("Verb must be null (or omitted) to mean any verb. An empty string is not a wildcard.");

            if (trimmed != "GET" && trimmed != "POST" && trimmed != "PUT" && trimmed != "DELETE")
                throw new ArgumentException($"'{verb}' is not a supported verb. Use GET, POST, PUT, DELETE, or null to mean any verb.");

            return trimmed;
        }

        /// <summary>Human-readable scope label for the 400/409 messages the FE shows verbatim.</summary>
        private static string FormatScope(string? objectName, string? verb)
            => $"{(string.IsNullOrEmpty(objectName) ? "All objects" : objectName)} · {(string.IsNullOrEmpty(verb) ? "All verbs" : verb)}";

        // ─────────────────────────────────────────────────────────────
        //  Projection
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Projects permission rows to <see cref="ApiKeyPermissionDto"/>, resolving workspace names in one
        /// extra query rather than one per row. Nullable object/verb round-trip as <c>null</c> so the
        /// wildcard is not flattened into an empty string on the way to the UI.
        /// </summary>
        private async Task<List<ApiKeyPermissionDto>> ProjectAsync(IEnumerable<ApiKeyPermission> rows, CancellationToken cancellationToken)
        {
            var list = rows.ToList();
            if (list.Count == 0)
                return new List<ApiKeyPermissionDto>();

            var workspaceIds = list.Select(p => p.WorkspaceId).Distinct().ToList();
            var workspaces = await _workspaceRepository.GetByIdsAsync(workspaceIds, cancellationToken);
            var namesById = workspaces.ToDictionary(w => w.Id, w => w.Name);

            return list.Select(p => new ApiKeyPermissionDto
            {
                Id = p.Id,
                ApiKeyId = p.ApiKeyId,
                ApiKeyName = p.ApiKey?.Name,
                ObjectName = p.ObjectName,
                Verb = p.Verb,
                IsDeny = p.IsDeny,
                WorkspaceId = p.WorkspaceId,
                WorkspaceName = namesById.TryGetValue(p.WorkspaceId, out var name) ? name : null,
                DatabaseName = p.DatabaseName,
                CreatedAt = p.CreatedAt
            }).ToList();
        }

        // ─────────────────────────────────────────────────────────────
        //  Error mapping
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 403 — the caller is authenticated but not entitled to this row (D10).
        /// </summary>
        private sealed class OrgAccessDeniedException : Exception
        {
            public OrgAccessDeniedException(string message) : base(message) { }
        }

        /// <summary>
        /// 409 — an identical row already exists. Separate from the unique index because MySQL does not
        /// enforce uniqueness for NULL-bearing rows.
        /// </summary>
        private sealed class DuplicateScopeException : Exception
        {
            public DuplicateScopeException(string message) : base(message) { }
        }

        /// <summary>
        /// Same shape as <see cref="BaseController.HandleRequestAsync{T}"/>, but it must also express
        /// <b>403</b> and <b>409</b>, which the base helper cannot — it maps only 404/400/500, so a denial
        /// or a conflict raised through it would surface as a 500. The base helper is left untouched
        /// because every other controller depends on its current behaviour.
        /// </summary>
        private async Task<IActionResult> HandleAsync<T>(Func<Task<T>> action)
        {
            try
            {
                return Ok(await action());
            }
            catch (OrgAccessDeniedException ex)
            {
                return StatusCode(403, new { code = "ORG_ACCESS_DENIED", message = ex.Message });
            }
            catch (DuplicateScopeException ex)
            {
                return StatusCode(409, new { code = "DUPLICATE_SCOPE", message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { code = "INVALID_REQUEST", message = ex.Message });
            }
            catch (DbUpdateException)
            {
                // Backstop only: the non-NULL half of the 5-tuple is also enforced by the unique index,
                // so a concurrent insert of an identical non-wildcard scope lands here. Wildcard
                // duplicates never reach the database as an error — see Create step 3.
                return Conflict(new { code = "DUPLICATE_SCOPE", message = "This exact scope already exists." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", details = ex.Message });
            }
        }
    }
}
