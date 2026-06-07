using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoApiEngine.Presentation.Controllers
{
    /// <summary>
    /// Controller for executing arbitrary SQL queries against workspace databases.
    /// Supports SELECT (returns tabular results) and DML (returns rows affected).
    /// </summary>
    [ApiController]
    [Route("api/query")]
    [Authorize]
    public class QueryController : BaseController
    {
        private readonly IQueryExecutionService _queryExecutionService;

        public QueryController(IQueryExecutionService queryExecutionService)
        {
            _queryExecutionService = queryExecutionService;
        }

        /// <summary>
        /// Executes a SQL query against the specified workspace's database.
        /// The response adapts based on query type:
        /// - SELECT / WITH / EXEC: returns Columns + Rows
        /// - INSERT / UPDATE / DELETE: returns RowsAffected
        /// </summary>
        /// <param name="request">Workspace ID and SQL query text.</param>
        /// <param name="cancellationToken">Cancellation token (used by Stop button on frontend).</param>
        [HttpPost("execute")]
        public async Task<IActionResult> Execute(
            [FromBody] QueryExecutionRequest request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Sql))
                return BadRequest(new { message = "SQL query cannot be empty." });

            if (string.IsNullOrWhiteSpace(request.WorkspaceId))
                return BadRequest(new { message = "Workspace ID is required." });

            var result = await _queryExecutionService.ExecuteAsync(request, cancellationToken);
            return Ok(result);
        }
    }
}
