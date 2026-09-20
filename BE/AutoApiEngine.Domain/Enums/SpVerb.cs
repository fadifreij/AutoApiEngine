using System;
using System.Collections.Generic;
using System.Text;

namespace AutoApiEngine.Domain.Enums
{
    /// <summary>
    /// HTTP verb a stored procedure is exposed as, derived from its definition body:
    /// <see cref="Get"/> when the last statement returns a result set, <see cref="Post"/> otherwise.
    /// </summary>
    public enum SpVerb
    {
        Get,
        Post
    }
}