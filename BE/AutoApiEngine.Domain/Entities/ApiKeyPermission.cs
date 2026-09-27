using AutoApiEngine.Domain.Common;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoApiEngine.Domain.Entities
{
    /// <summary>
    /// One granted (or denied) scope for an <see cref="ApiKey"/>: object + verb + workspace + database.
    /// A <c>NULL</c> <see cref="ObjectName"/> or <see cref="Verb"/> is the wildcard for that dimension
    /// (D2) — the wildcard is <c>NULL</c>, never an empty string and never <c>"*"</c>.
    /// </summary>
    public class ApiKeyPermission : BaseEntity
    {
        public Guid ApiKeyId { get; set; }

        /// <summary>Owning key. Relationship is config-only (no back-navigation on <see cref="ApiKey"/>).</summary>
        public ApiKey ApiKey { get; set; } = null!;

        /// <summary>
        /// Object (table / view / stored procedure / function) name. <c>NULL</c> = any object.
        /// Deliberately NOT <c>[Required]</c> — nullability is the wildcard, and a stray
        /// <c>[Required]</c> would both break the wildcard and trip the nullable-reference-type analyzer.
        /// </summary>
        /// <remarks>
        /// Length is 128, not the 250 the sibling <see cref="ApiKey.Name"/> uses, because this column is
        /// part of a 5-column composite UNIQUE index. At 250 the utf8mb4 index would need
        /// 3288 bytes and InnoDB caps a key at 3072 (ERROR 1071) on MySQL, while SQL Server caps at 1700.
        /// 128 is also the real SQL Server maximum identifier length, so nothing legitimate is lost.
        /// </remarks>
        [Column(TypeName = "VARCHAR")]
        [StringLength(128)]
        public string? ObjectName { get; set; }

        /// <summary>
        /// HTTP verb (GET | POST | PUT | DELETE). <c>NULL</c> = any verb.
        /// Length is 32 (not 250) for the composite-index reason documented on <see cref="ObjectName"/>;
        /// the longest real value is "DELETE".
        /// </summary>
        [Column(TypeName = "VARCHAR")]
        [StringLength(32)]
        public string? Verb { get; set; }

        public Guid WorkspaceId { get; set; }

        /// <summary>
        /// Denormalized from <c>Workspace.DatabaseName</c> at grant time. Length 128 for the same
        /// composite-index reason as <see cref="ObjectName"/>.
        /// </summary>
        [Column(TypeName = "VARCHAR")]
        [StringLength(128)]
        [Required]
        public string DatabaseName { get; set; } = string.Empty;

        /// <summary>
        /// True = deny this scope. D12: only valid when <see cref="ObjectName"/> is non-null
        /// (L1 = object+verb, L2 = object+all verbs). Enforced at the repository/controller layer,
        /// not here. Deliberately excluded from the unique index — see ApplicationDbContext.
        /// </summary>
        public bool IsDeny { get; set; }
    }
}
