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

                var listResult = await _dynamicApiService.GetListAsync(workspaceId, objectName, query, cancellationToken);

                if (listResult.Paging != null)
                    Response.Headers["X-Total-Count"] = listResult.Paging.TotalCount.ToString();

                return Ok(listResult);
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
                var result = await _dynamicApiService.CreateAsync(workspaceId, objectName, data, cancellationToken);
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
    }
}
