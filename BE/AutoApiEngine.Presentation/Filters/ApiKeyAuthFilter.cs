using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AutoApiEngine.Presentation.Filters
{
    /// <summary>
    /// The runtime authorization gate on the dynamic CRUD endpoints (plan §4).
    ///
    /// It is an <see cref="IAsyncAuthorizationFilter"/> rather than an action filter because authorization
    /// filters run <b>before model binding</b>, so a rejected request never pays for body binding or for
    /// the actual database round-trip the action would have made.
    ///
    /// The decision is default-allow (D1): an endpoint is only gated once a permission row matches the
    /// exact (object, verb, workspace, database) tuple. That is a deliberate, load-bearing property — see
    /// D13 and the class remarks on step 3.
    /// </summary>
    public class ApiKeyAuthFilter : IAsyncAuthorizationFilter
    {
        private readonly IApiKeyPermissionRepository _permissionRepository;
        private readonly IApiKeyRepository _apiKeyRepository;
        private readonly IWorkspaceRepository _workspaceRepository;

        public ApiKeyAuthFilter(
            IApiKeyPermissionRepository permissionRepository,
            IApiKeyRepository apiKeyRepository,
            IWorkspaceRepository workspaceRepository)
        {
            _permissionRepository = permissionRepository;
            _apiKeyRepository = apiKeyRepository;
            _workspaceRepository = workspaceRepository;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            // ── 1. Route context ────────────────────────────────────────────────
            // Only the dynamic CRUD routes carry these. Anything else is not ours to gate.
            if (!context.RouteData.Values.TryGetValue("workspaceId", out var workspaceIdRaw) ||
                !context.RouteData.Values.TryGetValue("objectName", out var objectNameRaw))
            {
                return;
            }

            var objectName = objectNameRaw?.ToString();
            var workspaceIdText = workspaceIdRaw?.ToString();

            if (string.IsNullOrWhiteSpace(objectName) ||
                !Guid.TryParse(workspaceIdText, out var workspaceId))
            {
                // Unparseable route values are the action's problem, not the gate's — let it through and
                // let the action produce its own 404/400 rather than inventing an auth failure here.
                return;
            }

            // The filter compares against Request.Method, which is the same verb the action will see.
            // OPTIONS preflights therefore never match a stored verb row and always pass.
            var verb = context.HttpContext.Request.Method.ToUpperInvariant();
            var cancellationToken = context.HttpContext.RequestAborted;

            // ── 2. Resolve the workspace's database ────────────────────────────
            // Moved ABOVE the row query (it used to sit after it) because dbName is now part of the SQL
            // predicate — there is no in-memory filtering step left to do it down there.
            Domain.Entities.Workspace workspace;
            try
            {
                workspace = await _workspaceRepository.GetByIdWithOrganizationAsync(workspaceIdText!, cancellationToken);
            }
            catch (KeyNotFoundException)
            {
                // No such workspace — the action will 404. Not an authorization decision.
                return;
            }

            var dbName = workspace.DatabaseName;
            if (string.IsNullOrWhiteSpace(dbName))
            {
                // A workspace with no database cannot match any stored scope (DatabaseName is
                // denormalized at grant time and is [Required]), so the predicate would be empty anyway.
                return;
            }

            // ── 3. In-scope rows, wildcards included, resolved in ONE SQL query ──
            var rows = (await _permissionRepository.GetInScopeAsync(objectName!, verb, workspaceId, dbName!, cancellationToken)).ToList();

            // D1 / D13 — the fallback is the EXACT tuple. No row in scope means nothing here was ever
            // gated, so the request passes through untouched. Note this is why a GET-only grant on
            // Orders leaves POST /Orders world-writable: that is the designed behaviour, not a bug, and
            // the predicate must not be widened to "fix" it.
            if (rows.Count == 0)
            {
                return;
            }

            // ── 4. Key required ────────────────────────────────────────────────
            // Reached only for a genuinely gated endpoint, so demanding a key here does not turn an
            // ungated endpoint into a key-required one.
            var keyValue = context.HttpContext.Request.Headers["X-Api-Key"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(keyValue))
            {
                context.Result = Error(401, "API_KEY_REQUIRED",
                    "This endpoint is protected. Provide a key in the X-Api-Key header.");
                return;
            }

            var apiKey = await _apiKeyRepository.FindActiveKeyByHashAsync(ApiKeyHasher.Hash(keyValue!), cancellationToken);

            // Inactive and expired keys resolve to null here, so they are indistinguishable from an
            // unknown key — the response cannot be used to probe which keys exist.
            if (apiKey is null)
            {
                context.Result = Error(401, "API_KEY_INVALID",
                    "The supplied API key is not valid.");
                return;
            }

            // ── 5. Deny wins, then the union of grants ─────────────────────────
            // TWO existence checks and no lattice: IsDeny is capped to object-scoped, so every deny row
            // is maximally specific (L1 = this object+verb, L2 = this object) and no specificity
            // comparator is needed. Deny is evaluated before the grant union and always wins.
            if (await _permissionRepository.ExistsDenyAsync(apiKey.Id, objectName!, verb, workspaceId, dbName!, cancellationToken) ||
                await _permissionRepository.ExistsDenyAsync(apiKey.Id, objectName!, verb: null, workspaceId, dbName!, cancellationToken))
            {
                context.Result = Error(403, "API_KEY_DENIED",
                    $"Access to '{objectName}' is denied for this API key.");
                return;
            }

            // Grants ACCUMULATE: access is the union of every matching grant row. There is no
            // "more specific overrides broader" subtraction — a narrow row never revokes a broad grant,
            // it only adds to it.
            var hasGrant = rows.Any(r => !r.IsDeny && r.ApiKeyId == apiKey.Id);

            if (!hasGrant)
            {
                context.Result = Error(403, "API_KEY_FORBIDDEN",
                    "This API key does not grant access to this endpoint.");
                return;
            }

            // ── 5b. Org containment (D10) ─────────────────────────────────────
            // Deliberately AFTER the 403s, so a rejected key never records LastUsedAt, and BEFORE step 6.
            // Zero extra queries: both Guids are already in hand.
            //
            // ApiKeyPermission has no OrganizationId of its own — org isolation is transitive via
            // ApiKey.OrganizationId — so this check is the backstop that stops a hand-inserted or
            // pre-existing bad row from letting one org read another org's database. Do not remove it.
            if (apiKey.OrganizationId != workspace.OrganizationId)
            {
                context.Result = Error(403, "API_KEY_WRONG_ORGANIZATION",
                    "This API key does not belong to the organization that owns this workspace.");
                return;
            }

            // ── 6. Allow + best-effort usage ──────────────────────────────────
            await RecordUsageBestEffortAsync(apiKey, cancellationToken);
        }

        /// <summary>
        /// Stamps <c>LastUsedAt</c>. Best-effort by design: a failure to record usage must never fail a
        /// request the caller is entitled to, so this is swallowed and only logged by the host's default
        /// logging. Note the entity is loaded with AsNoTracking, hence the explicit Update.
        /// </summary>
        private async Task RecordUsageBestEffortAsync(Domain.Entities.ApiKey apiKey, System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                apiKey.LastUsedAt = DateTime.UtcNow;
                await _apiKeyRepository.UpdateAsync(apiKey, cancellationToken);
            }
            catch (Exception)
            {
                // Intentionally ignored — see remarks.
            }
        }

        /// <summary>
        /// Consistent <c>{ code, message }</c> body for every rejection, so the frontend can branch on
        /// <c>code</c> while showing <c>message</c> verbatim.
        /// </summary>
        private static IActionResult Error(int statusCode, string code, string message)
            => new ObjectResult(new { code, message }) { StatusCode = statusCode };
    }
}
