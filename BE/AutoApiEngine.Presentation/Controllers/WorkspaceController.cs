using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;


namespace AutoApiEngine.Presentation.Controllers
{
    [ApiController]
    [Route("api/Workspaces")]
    public class WorkspaceController :BaseController 
    {
        public async Task<IActionResult> GetWorkSpaces(CancellationToken cancellationToken = default)
        {
            return await HandleRequestAsync( async ()=> "ok this is workspace api" );
        }
    }
}
