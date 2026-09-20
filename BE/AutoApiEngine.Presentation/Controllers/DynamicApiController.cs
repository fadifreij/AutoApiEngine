using AutoApiEngine.Domain.Entities;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoApiEngine.Presentation.Controllers
{
    /// <summary>
    /// Generic dynamic API controller — exposes any database table, view,
    /// stored procedure, or function as RESTful endpoints with filtering,
    /// paging, column selection, and related-table expansion.
    ///
    /// URL pattern:  /api/{workspaceId}/{objectName}[/{id}]
    /// Object name is the actual database table/view/sp/function name.
    /// All configuration (select, include, filter, sort, page) is via query params.
    /// </summary>
    [ApiController]
    //[Authorize]
    public class DynamicApiController : ControllerBase
    {
        private readonly IDynamicApiService _dynamicApiService;
        private readonly IDynamicApiMetadataService _metadataService;
        private readonly IWorkspaceRepository _workspaceRepository;

        public DynamicApiController(
            IDynamicApiService dynamicApiService,
            IDynamicApiMetadataService metadataService,
            IWorkspaceRepository workspaceRepository)
        {
            _dynamicApiService = dynamicApiService;
            _metadataService = metadataService;
            _workspaceRepository = workspaceRepository;
        }

        /// <summary>
        /// Verifies that the authenticated user's organization owns the specified workspace.
        /// Extracts the <c>organization</c> claim from the JWT and compares it against
        /// <c>workspace.Organization.Name</c> (eager-loaded). Returns 403 if the workspace
        /// does not belong to the user's organization.
        /// </summary>
        private async Task<IActionResult?> VerifyWorkspaceAccessAsync(string workspaceId, CancellationToken ct)
        {
            var orgClaim = User.Claims.FirstOrDefault(c => c.Type == "organization")?.Value;

            if (string.IsNullOrEmpty(orgClaim))
                return StatusCode(403, new { message = "Organization claim not found in token." });

            Workspace workspace;
            try
            {
                workspace = await _workspaceRepository.GetByIdWithOrganizationAsync(workspaceId, ct);
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { message = $"Workspace with id {workspaceId} not found." });
            }

            if (workspace.Organization?.Name != orgClaim)
                return StatusCode(403, new { message = "Access denied. Workspace does not belong to your organization." });

            return null; // access granted
        }

        // ─────────────────────────────────────────────────────────────
        //  Metadata endpoints (for UI)
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns column metadata + FK info for a specific database object.
        /// Used by the frontend to populate the column selector UI.
        /// </summary>
        [HttpGet("api/schema/{workspaceId:guid}/columns")]
        public async Task<IActionResult> GetObjectColumns(
            string workspaceId,
            [FromQuery] string table,
            CancellationToken cancellationToken)
        {
            try
            {
                var accessCheck = await VerifyWorkspaceAccessAsync(workspaceId, cancellationToken);
                if (accessCheck != null) return accessCheck;

                var result = await _metadataService.GetObjectMetadataAsync(workspaceId, table, cancellationToken);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", details = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  CRUD Endpoints
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// List records with filtering, sorting, paging, and column selection.
        ///
        /// Query parameters:
        ///   select=col1,col2,related.col3    — columns to return (dot-notation for related)
        ///   include=table(col1,col2)         — include related table data
        ///   filter=col:op:val                — filter expression (repeatable, AND-ed)
        ///   filter_or=col:op:val             — OR filter expression
        ///   sort=col:asc,col2:desc           — sort columns
        ///   page=1&amp;pageSize=20              — paging
        ///
        /// Examples:
        ///   GET /api/{ws}/employees?select=id,firstName,department.name
        ///   GET /api/{ws}/employees?filter=age:gte:25&amp;page=1&amp;pageSize=20
        /// </summary>
        [HttpGet("api/{workspaceId:guid}/{objectName}")]
        public async Task<IActionResult> GetList(
            string workspaceId,
            string objectName,
            [FromQuery] DynamicApiQueryRequest query,
            CancellationToken cancellationToken)
        {
            try
            {
               // Handle composite PK lookup: ?pk1=v1&pk2=v2
                if (HttpContext.Request.Query.ContainsKey("pk1"))
                {
                    var pkValues = new Dictionary<string, string>();
                    var i = 1;
                    while (HttpContext.Request.Query[$"pk{i}"] is var pk && pk.Count > 0)
                    {
                        var colName = HttpContext.Request.Query[$"pk{i}_name"].FirstOrDefault() ?? $"pk{i}";
                        pkValues[colName] = pk.ToString();
                        i++;
                    }
                    var result = await _dynamicApiService.GetByCompositeKeyAsync(workspaceId, objectName, pkValues, query, cancellationToken);
                    return Ok(result);
                }

                var meta = await GetClassificationAsync(workspaceId, objectName, cancellationToken);

                // Table — unchanged list behavior (filters, paging, joins, X-Total-Count).
                if (IsObjectType(meta, "TABLE"))
                {
                    var listResult = await _dynamicApiService.GetListAsync(workspaceId, objectName, query, cancellationToken);

                    if (listResult.Paging != null)
                        Response.Headers["X-Total-Count"] = listResult.Paging.TotalCount.ToString();

                    return Ok(listResult);
                }

                // View — list, with arbitrary query-string params bound as equality WHERE
                // conditions (e.g. ?departmentId=5 becomes filter "departmentId:eq:5").
                if (IsObjectType(meta, "VIEW"))
                {
                    var viewFilters = GetNonReservedQueryParams()
                        .Select(kvp => $"{kvp.Key}:eq:{kvp.Value}")
                        .ToList();

                    if (viewFilters.Count > 0)
                    {
                        var existing = query.Filter ?? new List<string>();
                        query.Filter = existing.Concat(viewFilters).ToList();
                    }

                    var listResult = await _dynamicApiService.GetListAsync(workspaceId, objectName, query, cancellationToken);

                    if (listResult.Paging != null)
                        Response.Headers["X-Total-Count"] = listResult.Paging.TotalCount.ToString();

                    return Ok(listResult);
                }

                // Function, or stored procedure classified GET — run through the routine executor.
                // (Stored procedures classified POST must be invoked via POST.)
                if (!string.Equals(meta.Verb, "GET", StringComparison.OrdinalIgnoreCase))
                    return StatusCode(405, new
                    {
                        message = $"Stored procedure '{objectName}' only supports POST. Use POST /api/{workspaceId}/{objectName}.",
                        code = "METHOD_NOT_ALLOWED"
                    });

                var parameters = BindQueryParameters();

                var missing = GetMissingRequiredParameters(meta, parameters);
                if (missing.Count > 0)
                    return BadRequest(new
                    {
                        message = $"Missing required parameter(s): {string.Join(", ", missing)}",
                        code = "MISSING_PARAMETER"
                    });

                var routineResult = await _dynamicApiService.ExecuteRoutineAsync(workspaceId, objectName, parameters, cancellationToken);
                return Ok(routineResult);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message, code = "INVALID_PARAMETER" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", details = ex.Message });
            }
        }

        /// <summary>
        /// Get a single record by primary key.
        /// </summary>
        [HttpGet("api/{workspaceId:guid}/{objectName}/{id}")]
        public async Task<IActionResult> GetById(
            string workspaceId,
            string objectName,
            string id,
            [FromQuery] DynamicApiQueryRequest query,
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await _dynamicApiService.GetByIdAsync(workspaceId, objectName, id, query, cancellationToken);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message, code = "INVALID_PARAMETER" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", details = ex.Message });
            }
        }

        /// <summary>
        /// Create a new record. Request body is a JSON object of column-value pairs.
        /// </summary>
        [HttpPost("api/{workspaceId:guid}/{objectName}")]
        public async Task<IActionResult> Create(
            string workspaceId,
            string objectName,
            [FromBody] Dictionary<string, object?> data,
            CancellationToken cancellationToken)
        {
            try
            {
                var meta = await GetClassificationAsync(workspaceId, objectName, cancellationToken);

                // Table — unchanged create behavior.
                if (IsObjectType(meta, "TABLE"))
                {
                    var result = await _dynamicApiService.CreateAsync(workspaceId, objectName, data, cancellationToken);
                    return Ok(result);
                }

                // Views, functions, and GET-classified stored procedures are read-only.
                if (!string.Equals(meta.Verb, "POST", StringComparison.OrdinalIgnoreCase))
                    return StatusCode(405, new
                    {
                        message = $"Object '{objectName}' only supports GET.",
                        code = "METHOD_NOT_ALLOWED"
                    });

                // Stored procedure classified POST — the body is the routine's input
                // parameters (matched by name).
                var parameters = NormalizeRoutineParameters(data);

                var missing = GetMissingRequiredParameters(meta, parameters);
                if (missing.Count > 0)
                    return BadRequest(new
                    {
                        message = $"Missing required parameter(s): {string.Join(", ", missing)}",
                        code = "MISSING_PARAMETER"
                    });

                var routineResult = await _dynamicApiService.ExecuteRoutineAsync(workspaceId, objectName, parameters, cancellationToken);
                return Ok(routineResult);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message, code = "INVALID_PARAMETER" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", details = ex.Message });
            }
        }

        /// <summary>
        /// Update a record by primary key. Request body is a JSON object of column-value pairs.
        /// Supports partial update (PATCH semantics) — only include columns that should change.
        /// </summary>
        [HttpPut("api/{workspaceId:guid}/{objectName}/{id}")]
        public async Task<IActionResult> Update(
            string workspaceId,
            string objectName,
            string id,
            [FromBody] Dictionary<string, object?> data,
            CancellationToken cancellationToken)
        {
            try
            {
               var result = await _dynamicApiService.UpdateAsync(workspaceId, objectName, id, data, cancellationToken);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message, code = "INVALID_PARAMETER" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", details = ex.Message });
            }
        }

        /// <summary>
        /// Delete a record by primary key.
        /// </summary>
        [HttpDelete("api/{workspaceId:guid}/{objectName}/{id}")]
        public async Task<IActionResult> Delete(
            string workspaceId,
            string objectName,
            string id,
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await _dynamicApiService.DeleteAsync(workspaceId, objectName, id, cancellationToken);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message, code = "INVALID_PARAMETER" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", details = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Object classification + routine param binding
        //  (Step 8: GET/POST branching by object type)
        // ─────────────────────────────────────────────────────────────

        private DynamicApiObjectMetadataDto? _classificationCache;

        /// <summary>
        /// Loads the database object's metadata (type, classified verb, routine parameters)
        /// once per request and caches it for any subsequent branch in the same action.
        /// </summary>
        private async Task<DynamicApiObjectMetadataDto> GetClassificationAsync(
            string workspaceId, string objectName, CancellationToken ct)
            => _classificationCache ??= await _metadataService.GetObjectMetadataAsync(workspaceId, objectName, ct);

        /// <summary>
        /// Case-insensitive object-type check on the resolved metadata type. The resolver
        /// returns uppercase types: "TABLE"/"BASE TABLE", "VIEW", "FUNCTION", "PROCEDURE".
        /// </summary>
        private static bool IsObjectType(DynamicApiObjectMetadataDto meta, string expected)
            => meta.ObjectType.Contains(expected, StringComparison.OrdinalIgnoreCase);

        /// <summary>Query-string keys owned by the dynamic API framework (never routine params / view filters).</summary>
        private static readonly HashSet<string> ReservedQueryKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "select", "include", "filter", "filter_or", "sort", "page", "pageSize"
        };

        private static bool IsReservedQueryKey(string key)
        {
            // Composite-PK keys: pk1, pk2, ..., plus their pk{n}_name column overrides.
            if (key.StartsWith("pk", StringComparison.OrdinalIgnoreCase)) return true;
            return ReservedQueryKeys.Contains(key);
        }

        /// <summary>
        /// Enumerates the request's non-reserved query-string params. Used both to translate
        /// arbitrary params into view equality filters and to bind routine arguments.
        /// </summary>
        private IEnumerable<KeyValuePair<string, string>> GetNonReservedQueryParams()
        {
            foreach (var kvp in HttpContext.Request.Query)
            {
                if (IsReservedQueryKey(kvp.Key)) continue;
                yield return new KeyValuePair<string, string>(kvp.Key, kvp.Value.ToString());
            }
        }

        /// <summary>
        /// Binds non-reserved query-string params into a routine-parameter dictionary.
        /// Each bare key is also aliased with an '@' prefix so both engine conventions
        /// resolve: SQL Server routine names from sys.parameters carry a leading '@',
        /// MySQL information_schema names are bare. The service matches by name.
        /// </summary>
        private Dictionary<string, object?> BindQueryParameters()
        {
            var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in GetNonReservedQueryParams())
            {
                parameters[kvp.Key] = kvp.Value;
                if (!kvp.Key.StartsWith('@'))
                    parameters["@" + kvp.Key] = kvp.Value;
            }
            return parameters;
        }

        /// <summary>
        /// Copies a POST body into a routine-parameter dictionary, aliasing '@'-prefixed
        /// keys exactly like <see cref="BindQueryParameters"/> so SQL Server and MySQL
        /// both resolve the values by parameter name.
        /// </summary>
        private static Dictionary<string, object?> NormalizeRoutineParameters(Dictionary<string, object?> source)
        {
            var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in source)
            {
                parameters[kvp.Key] = kvp.Value;
                if (!kvp.Key.StartsWith('@'))
                    parameters["@" + kvp.Key] = kvp.Value;
            }
            return parameters;
        }

        /// <summary>
        /// Returns the routine's required parameters (IN/INOUT, no default) that are missing
        /// from the bound dictionary. Matching is case-insensitive and ignores the leading
        /// '@' SQL Server adds to catalog parameter names. OUT-only routines yield no
        /// required parameters, so they never block a call.
        /// </summary>
        private static List<string> GetMissingRequiredParameters(
            DynamicApiObjectMetadataDto meta, Dictionary<string, object?> parameters)
        {
            var missing = new List<string>();
            foreach (var p in meta.Parameters)
            {
                var isInput = p.ParameterMode.Equals("IN", StringComparison.OrdinalIgnoreCase)
                           || p.ParameterMode.Equals("INOUT", StringComparison.OrdinalIgnoreCase);
                if (!isInput || p.HasDefault) continue;

                var bareName = p.Name.TrimStart('@');
                var bound = parameters.ContainsKey(p.Name)
                         || parameters.ContainsKey(bareName)
                         || parameters.ContainsKey("@" + bareName);
                if (!bound)
                    missing.Add(bareName);
            }
            return missing;
        }
    }
}
