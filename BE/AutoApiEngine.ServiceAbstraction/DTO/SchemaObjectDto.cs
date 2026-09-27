namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// Represents a single schema object (table, view, stored procedure, or function).
    /// </summary>
    public class SchemaObjectDto
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // "Table", "View", "StoredProcedure", "Function"
        public int? ColumnCount { get; set; } // Only populated for tables

        /// <summary>
        /// The single HTTP verb that reaches this object, or null when the object is not
        /// verb-restricted / no verb applies.
        ///
        /// <para>
        /// Populated for every object type, matching the verb table the API-key permission filter
        /// and the dynamic CRUD controller both use:
        /// <list type="bullet">
        ///   <item><description>Table → <c>GET</c>, <c>POST</c>, <c>PUT</c>, <c>DELETE</c> all reach it, so null.</description></item>
        ///   <item><description>View / Function → only <c>GET</c>.</description></item>
        ///   <item><description>StoredProcedure → exactly one verb, classified from its definition by <c>SpVerbClassifier</c>.</description></item>
        /// </list>
        /// </para>
        ///
        /// <para>
        /// This is additive: existing consumers ignore it. It exists so a client can render one
        /// verb column per object type (and disable the ones no verb can reach) without issuing a
        /// per-object metadata call.
        /// </para>
        /// </summary>
        public string? Verb { get; set; }
    }
}
