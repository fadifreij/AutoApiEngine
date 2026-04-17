using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Text;

namespace AutoApiEngine.Presentation
{
    public abstract class BaseController : ControllerBase
    {
        protected async Task<IActionResult> HandleRequestAsync<T>(Func<Task<T>> action)
        {
            try
            {

                var result = await action();
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
    }
}
