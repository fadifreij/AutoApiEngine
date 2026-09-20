using AutoApiEngine.Domain.Enums;
using AutoApiEngine.ServiceAbstraction;
using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;

namespace AutoApiEngine.Services.DatabaseManagementServices
{
    /// <summary>
    /// SQL Server implementation of <see cref="ISchemaExplorerService"/>.
    /// Uses INFORMATION_SCHEMA views and sys.dm_* DMFs to enumerate objects and columns.
    /// </summary>
    public class SqlSchemaExplorer : ISchemaExplorerService
    {
        private readonly ILogger<SqlSchemaExplorer> _logger;
        private readonly string _appConnectionString;

        public SqlSchemaExplorer(ILogger<SqlSchemaExplorer> logger, IConfiguration configuration)
        {
            _logger = logger;
            _appConnectionString = configuration.GetConnectionString("SqlServerConnection")
                ?? throw new InvalidOperationException("SqlServerConnection is not configured.");
        }

        public async Task<SchemaExplorerResponse> ExploreAsync(
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            string? searchFilter = null,
            CancellationToken cancellationToken = default)
        {
            var targetConn = string.IsNullOrWhiteSpace(connectionString) ? _appConnectionString : connectionString;
            var csb = new SqlConnectionStringBuilder(targetConn)
            {
                InitialCatalog = databaseName
            };

            await using var connection = new SqlConnection(csb.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            var response = new SchemaExplorerResponse();

            // --- Counts ---
            await using (var cmd = new SqlCommand("SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'", connection))
                response.TablesCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));

            await using (var cmd = new SqlCommand("SELECT COUNT(*) FROM INFORMATION_SCHEMA.VIEWS", connection))
                response.ViewsCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));

            await using (var cmd = new SqlCommand("SELECT COUNT(*) FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE = 'FUNCTION'", connection))
                response.FunctionsCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));

            await using (var cmd = new SqlCommand("SELECT COUNT(*) FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE = 'PROCEDURE'", connection))
                response.StoredProceduresCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));

            await using (var cmd = new SqlCommand("SELECT ISNULL(SUM(CAST(size AS BIGINT) * 8 * 1024), 0) FROM sys.database_files WHERE type = 0", connection))
            {
                var val = await cmd.ExecuteScalarAsync(cancellationToken);
                response.DatabaseSizeBytes = val is DBNull or null ? 0L : Convert.ToInt64(val);
            }

            // --- Objects ---
            var tablesSql = @"
                SELECT 
                    t.TABLE_NAME,
                    (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS c WHERE c.TABLE_NAME = t.TABLE_NAME AND c.TABLE_SCHEMA = t.TABLE_SCHEMA) AS COLUMN_COUNT
                FROM INFORMATION_SCHEMA.TABLES t
                WHERE t.TABLE_TYPE = 'BASE TABLE'";

            var viewsSql = @"
                SELECT TABLE_NAME
                FROM INFORMATION_SCHEMA.VIEWS";

            var routinesSql = @"
                SELECT SPECIFIC_NAME, ROUTINE_TYPE
                FROM INFORMATION_SCHEMA.ROUTINES";

            if (!string.IsNullOrWhiteSpace(searchFilter))
            {
                tablesSql += $" AND t.TABLE_NAME LIKE @filter";
                viewsSql += $" WHERE TABLE_NAME LIKE @filter";
                routinesSql += $" WHERE SPECIFIC_NAME LIKE @filter";
            }

            // Tables with column counts
            await using (var cmd = new SqlCommand(tablesSql, connection))
            {
                if (!string.IsNullOrWhiteSpace(searchFilter))
                    cmd.Parameters.AddWithValue("@filter", $"%{searchFilter}%");

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    response.Objects.Add(new SchemaObjectDto
                    {
                        Name = reader.GetString(0),
                        Type = "Table",
                        ColumnCount = reader.IsDBNull(1) ? null : Convert.ToInt32(reader[1])
                    });
                }
            }

            // Views
            await using (var cmd = new SqlCommand(viewsSql, connection))
            {
                if (!string.IsNullOrWhiteSpace(searchFilter))
                    cmd.Parameters.AddWithValue("@filter", $"%{searchFilter}%");

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    response.Objects.Add(new SchemaObjectDto
                    {
                        Name = reader.GetString(0),
                        Type = "View"
                    });
                }
            }

            // Routines (functions + stored procedures)
            await using (var cmd = new SqlCommand(routinesSql, connection))
            {
                if (!string.IsNullOrWhiteSpace(searchFilter))
                    cmd.Parameters.AddWithValue("@filter", $"%{searchFilter}%");

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var routineType = reader.GetString(1);
                    response.Objects.Add(new SchemaObjectDto
                    {
                        Name = reader.GetString(0),
                        Type = routineType == "FUNCTION" ? "Function" : "StoredProcedure"
                    });
                }
            }

            return response;
        }

        public async Task<List<TableSchemaDto>> GetTableColumnsAsync(
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            CancellationToken cancellationToken = default)
        {
            var targetConn = string.IsNullOrWhiteSpace(connectionString) ? _appConnectionString : connectionString;
            var csb = new SqlConnectionStringBuilder(targetConn)
            {
                InitialCatalog = databaseName
            };

            await using var connection = new SqlConnection(csb.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            const string sql = @"
                SELECT c.TABLE_NAME,
                       c.COLUMN_NAME,
                       c.DATA_TYPE,
                       c.CHARACTER_MAXIMUM_LENGTH,
                       c.IS_NULLABLE,
                       CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS IS_PK
                FROM INFORMATION_SCHEMA.COLUMNS c
                JOIN INFORMATION_SCHEMA.TABLES t
                    ON t.TABLE_NAME = c.TABLE_NAME AND t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_TYPE = 'BASE TABLE'
                LEFT JOIN (
                    SELECT ku.TABLE_NAME, ku.COLUMN_NAME
                    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                    JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                        ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
                    WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
                ) pk ON pk.TABLE_NAME = c.TABLE_NAME AND pk.COLUMN_NAME = c.COLUMN_NAME
                ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION";

            var tables = new Dictionary<string, TableSchemaDto>(StringComparer.OrdinalIgnoreCase);

            await using var cmd = new SqlCommand(sql, connection);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var tableName = reader.GetString(0);
                if (!tables.TryGetValue(tableName, out var table))
                {
                    table = new TableSchemaDto { TableName = tableName };
                    tables[tableName] = table;
                }

                var dataType = reader.GetString(2);
                if (!reader.IsDBNull(3))
                {
                    var maxLen = reader.GetInt32(3);
                    dataType += maxLen == -1 ? "(max)" : $"({maxLen})";
                }

                table.Columns.Add(new TableColumnDto
                {
                    Name = reader.GetString(1),
                    DataType = dataType,
                    IsNullable = string.Equals(reader.GetString(4), "YES", StringComparison.OrdinalIgnoreCase),
                    IsPrimaryKey = reader.GetInt32(5) == 1
                });
            }

            return tables.Values.ToList();
        }

        public async Task<List<RoutineDefinitionDto>> GetRoutineDefinitionsAsync(
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            CancellationToken cancellationToken = default)
        {
            var targetConn = string.IsNullOrWhiteSpace(connectionString) ? _appConnectionString : connectionString;
            var csb = new SqlConnectionStringBuilder(targetConn)
            {
                InitialCatalog = databaseName
            };

            await using var connection = new SqlConnection(csb.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            const string sql = @"
                SELECT o.name,
                       o.type,
                       m.definition
                FROM sys.sql_modules m
                JOIN sys.objects o ON o.object_id = m.object_id
                WHERE o.type IN ('V', 'P', 'FN', 'IF', 'TF')
                ORDER BY o.name";

            var result = new List<RoutineDefinitionDto>();

            await using var cmd = new SqlCommand(sql, connection);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var objType = reader.GetString(1).Trim();
                var type = objType switch
                {
                    "V" => "View",
                    "P" => "StoredProcedure",
                    _ => "Function" // FN, IF, TF
                };

                result.Add(new RoutineDefinitionDto
                {
                    Name = reader.GetString(0),
                    Type = type,
                    Definition = reader.IsDBNull(2) ? string.Empty : reader.GetString(2)
                });
            }

            return result;
        }

        public async Task<string> GetObjectDdlAsync(
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            string objectName,
            string objectType,
            CancellationToken cancellationToken = default)
        {
            var targetConn = string.IsNullOrWhiteSpace(connectionString) ? _appConnectionString : connectionString;
            var csb = new SqlConnectionStringBuilder(targetConn)
            {
                InitialCatalog = databaseName
            };

            await using var connection = new SqlConnection(csb.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            return objectType switch
            {
                "Table" => await GetTableDdlAsync(connection, objectName, cancellationToken),
                "View" or "StoredProcedure" or "Function" => await GetRoutineDdlAsync(connection, objectName, objectType, cancellationToken),
                _ => throw new ArgumentException($"Unsupported object type: {objectType}")
            };
        }

        /// <summary>
        /// Retrieves the declared parameters of a stored procedure or function from
        /// sys.parameters (joined with sys.types for the type name). Views have no
        /// sys.parameters rows, so they naturally return an empty list. The synthetic
        /// @RETURN_VALUE row (parameter_id = 0) is skipped.
        /// </summary>
        public async Task<List<RoutineParameterDto>> GetRoutineParametersAsync(
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            string schema,
            string objectName,
            CancellationToken cancellationToken = default)
        {
            var targetConn = string.IsNullOrWhiteSpace(connectionString) ? _appConnectionString : connectionString;
            var csb = new SqlConnectionStringBuilder(targetConn)
            {
                InitialCatalog = databaseName
            };

            await using var connection = new SqlConnection(csb.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            const string sql = @"
                SELECT p.name AS Name,
                       t.name AS DataType,
                       p.is_output AS IsOutput,
                       p.has_default_value AS HasDefault,
                       p.default_value AS DefaultValue,
                       p.parameter_id AS OrdinalPosition
                FROM sys.parameters p
                JOIN sys.objects o ON o.object_id = p.object_id
                JOIN sys.schemas s ON s.schema_id = o.schema_id
                LEFT JOIN sys.types t ON t.user_type_id = p.user_type_id
                WHERE s.name = @Schema AND o.name = @ObjectName
                ORDER BY p.parameter_id;";

            var result = new List<RoutineParameterDto>();

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Schema", schema);
            cmd.Parameters.AddWithValue("@ObjectName", objectName);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var ordinalPosition = reader.GetInt32(5);

                // Skip the synthetic @RETURN_VALUE row (parameter_id = 0).
                if (ordinalPosition <= 0)
                    continue;

                // SQL Server has no true INOUT parameters; is_output -> OUT, else IN.
                var isOutput = !reader.IsDBNull(2) && reader.GetBoolean(2);

                result.Add(new RoutineParameterDto
                {
                    Name = reader.GetString(0),
                    DataType = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    ParameterMode = isOutput ? "OUT" : "IN",
                    HasDefault = !reader.IsDBNull(3) && reader.GetBoolean(3),
                    DefaultValue = reader.IsDBNull(4) ? null : Convert.ToString(reader[4]),
                    OrdinalPosition = ordinalPosition
                });
            }

            return result;
        }

        /// <summary>
        /// Returns the SQL definition (body) of a single stored procedure via
        /// OBJECT_DEFINITION. Empty string when the object is not a procedure or
        /// its definition is unavailable (e.g. encrypted).
        /// </summary>
        public async Task<string> GetRoutineDefinitionAsync(
            string databaseName,
            DatabaseEngine engine,
            string connectionString,
            string schema,
            string objectName,
            CancellationToken cancellationToken = default)
        {
            var targetConn = string.IsNullOrWhiteSpace(connectionString) ? _appConnectionString : connectionString;
            var csb = new SqlConnectionStringBuilder(targetConn)
            {
                InitialCatalog = databaseName
            };

            await using var connection = new SqlConnection(csb.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            const string sql = @"
                SELECT OBJECT_DEFINITION(o.object_id)
                FROM sys.objects o
                JOIN sys.schemas s ON s.schema_id = o.schema_id
                WHERE s.name = @Schema AND o.name = @ObjectName AND o.type = 'P';";

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Schema", schema);
            cmd.Parameters.AddWithValue("@ObjectName", objectName);

            return (await cmd.ExecuteScalarAsync(cancellationToken) as string) ?? string.Empty;
        }

        // ---------------------------------------------------------------
        //  Helpers — Schema resolution
        // ---------------------------------------------------------------

        /// <summary>
        /// Resolves the schema for an unqualified table/view name.
        /// Returns "dbo" if not found.
        /// </summary>
        private static async Task<string> ResolveTableSchemaAsync(
            SqlConnection connection,
            string objectName,
            string tableType,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                SELECT TOP 1 TABLE_SCHEMA
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_NAME = @name AND TABLE_TYPE = @type";

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@name", objectName);
            cmd.Parameters.AddWithValue("@type", tableType);
            return (await cmd.ExecuteScalarAsync(cancellationToken) as string) ?? "dbo";
        }

        /// <summary>
        /// Resolves the schema for an unqualified routine (procedure / function) name.
        /// Returns "dbo" if not found.
        /// </summary>
        private static async Task<string> ResolveRoutineSchemaAsync(
            SqlConnection connection,
            string objectName,
            string routineType,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                SELECT TOP 1 ROUTINE_SCHEMA
                FROM INFORMATION_SCHEMA.ROUTINES
                WHERE ROUTINE_NAME = @name AND ROUTINE_TYPE = @type";

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@name", objectName);
            cmd.Parameters.AddWithValue("@type", routineType);
            return (await cmd.ExecuteScalarAsync(cancellationToken) as string) ?? "dbo";
        }

        // ---------------------------------------------------------------
        //  Helpers — Routine DDL (VIEW / PROCEDURE / FUNCTION)
        // ---------------------------------------------------------------

        /// <summary>
        /// Returns the full CREATE DDL for a view, stored procedure, or function
        /// via OBJECT_DEFINITION.
        /// </summary>
        private async Task<string> GetRoutineDdlAsync(
            SqlConnection connection,
            string objectName,
            string objectType,
            CancellationToken cancellationToken)
        {
            var routineType = objectType switch
            {
                "StoredProcedure" => "PROCEDURE",
                "Function" => "FUNCTION",
                _ => "VIEW"
            };

            var schema = await ResolveRoutineSchemaAsync(connection, objectName, routineType, cancellationToken);
            var fullName = $"[{schema}].[{objectName}]";

            const string sql = "SELECT OBJECT_DEFINITION(OBJECT_ID(@fullName))";
            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@fullName", fullName);

            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            return result as string ?? string.Empty;
        }

        // ---------------------------------------------------------------
        //  Helpers — Table DDL (full CREATE TABLE reconstruction)
        // ---------------------------------------------------------------

        /// <summary>
        /// Reconstructs a full CREATE TABLE DDL for a single table by querying
        /// SQL Server system catalog views.  Supports columns (identity, computed,
        /// defaults, collation), all constraint types, and non-clustered indexes.
        /// </summary>
        private async Task<string> GetTableDdlAsync(
            SqlConnection connection,
            string objectName,
            CancellationToken cancellationToken)
        {
            var schema = await ResolveTableSchemaAsync(connection, objectName, "BASE TABLE", cancellationToken);
            var fullName = $"[{schema}].[{objectName}]";

            // ── Column definitions ──────────────────────────────────
            var columnDefs = await GetColumnDefinitionsAsync(connection, schema, objectName, fullName, cancellationToken);

            if (columnDefs.Count == 0)
                return string.Empty; // table doesn't exist

            // ── Constraint definitions ──────────────────────────────
            var constraintDefs = await GetConstraintDefinitionsAsync(connection, schema, objectName, fullName, cancellationToken);

            // ── Index definitions ───────────────────────────────────
            var indexDefs = await GetIndexDefinitionsAsync(connection, schema, objectName, fullName, cancellationToken);

            // ── Build the output ────────────────────────────────────
            var sb = new StringBuilder();
            sb.Append($"CREATE TABLE {fullName}");
            sb.AppendLine();
            sb.AppendLine("(");

            var allLines = new List<string>(columnDefs);
            allLines.AddRange(constraintDefs);

            for (int i = 0; i < allLines.Count; i++)
            {
                sb.Append("    ");
                sb.Append(allLines[i]);
                if (i < allLines.Count - 1)
                    sb.Append(',');
                sb.AppendLine();
            }

            sb.AppendLine(");");
            sb.AppendLine();

            foreach (var idx in indexDefs)
            {
                sb.AppendLine(idx);
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Queries column metadata and returns column-definition strings
        /// (e.g. "[Id] INT IDENTITY(1,1) NOT NULL").
        /// </summary>
        private static async Task<List<string>> GetColumnDefinitionsAsync(
            SqlConnection connection,
            string schema,
            string tableName,
            string fullName,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                SELECT
                    c.name,
                    TYPE_NAME(c.user_type_id)                                               AS type_name,
                    c.max_length,
                    c.precision,
                    c.scale,
                    c.is_nullable,
                    c.is_identity,
                    c.is_computed,
                    c.collation_name,
                    ic.seed_value,
                    ic.increment_value,
                    cc.definition                                                           AS computed_definition,
                    cc.is_persisted,
                    dc.definition                                                           AS default_definition
                FROM sys.columns c
                LEFT JOIN sys.identity_columns  ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                LEFT JOIN sys.computed_columns  cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
                LEFT JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
                WHERE c.object_id = OBJECT_ID(@fullName)
                ORDER BY c.column_id";

            var results = new List<string>();

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@fullName", fullName);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(BuildColumnDefinitionLine(reader));
            }

            return results;
        }

        /// <summary>
        /// Builds a single column-definition string from a SqlDataReader row.
        /// Expected column order (0-based):
        ///   0: name, 1: type_name, 2: max_length, 3: precision, 4: scale,
        ///   5: is_nullable, 6: is_identity, 7: is_computed, 8: collation_name,
        ///   9: seed_value, 10: increment_value, 11: computed_definition,
        ///   12: is_persisted, 13: default_definition
        /// </summary>
        private static string BuildColumnDefinitionLine(SqlDataReader reader)
        {
            var name = reader.GetString(0);
            var isComputed = reader.GetBoolean(7);

            var sb = new StringBuilder();
            sb.Append('[').Append(name).Append("] ");

            // ── Computed column ────────────────────────────────────────
            if (isComputed)
            {
                var computedDef = reader.IsDBNull(11) ? string.Empty : reader.GetString(11);
                var isPersisted = !reader.IsDBNull(12) && reader.GetBoolean(12);
                sb.Append("AS ").Append(computedDef);
                if (isPersisted) sb.Append(" PERSISTED");
                return sb.ToString();
            }

            // ── Data type ──────────────────────────────────────────────
            var typeName = reader.GetString(1);
            var maxLength = reader.GetInt16(2);
            var precision = reader.GetByte(3);
            var scale = reader.GetByte(4);

            sb.Append(FormatDataType(typeName, maxLength, precision, scale));

            // ── Identity ───────────────────────────────────────────────
            var isIdentity = reader.GetBoolean(6);
            if (isIdentity)
            {
                var seed = reader.IsDBNull(9) ? 1L : reader.GetInt64(9);
                var increment = reader.IsDBNull(10) ? 1L : reader.GetInt64(10);
                sb.Append(" IDENTITY(").Append(seed).Append(',').Append(increment).Append(')');
            }

            // ── Collation (character types only) ──────────────────────
            if (!reader.IsDBNull(8))
            {
                sb.Append(" COLLATE ").Append(reader.GetString(8));
            }

            // ── Nullability ────────────────────────────────────────────
            var isNullable = reader.GetBoolean(5);
            sb.Append(isNullable ? " NULL" : " NOT NULL");

            // ── Default (inline) ───────────────────────────────────────
            if (!reader.IsDBNull(13))
            {
                // Strip the outer parentheses that SQL Server wraps around default definitions
                var defaultDef = reader.GetString(13);
                sb.Append(" DEFAULT ").Append(defaultDef);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Formats a SQL Server data-type string with its parameters.
        /// </summary>
        private static string FormatDataType(string typeName, short maxLength, byte precision, byte scale)
        {
            var lower = typeName.ToLowerInvariant();

            // Types with a length parameter
            if (lower is "nvarchar" or "varchar" or "nchar" or "char" or "binary" or "varbinary")
            {
                if (maxLength == -1)
                    return $"{typeName}(MAX)";
                var len = (lower is "nvarchar" or "nchar") ? maxLength / 2 : maxLength;
                return $"{typeName}({len})";
            }

            // Types with precision and scale
            if (lower is "numeric" or "decimal")
                return scale > 0 ? $"{typeName}({precision},{scale})" : $"{typeName}({precision})";

            // Types with scale only
            if (lower is "datetime2" or "time" or "datetimeoffset")
                return scale > 0 ? $"{typeName}({scale})" : typeName;

            // float
            if (lower is "float" or "real")
                return (precision > 0 && precision != 53) ? $"float({precision})" : typeName;

            // All other types: int, bigint, bit, date, datetime, smalldatetime,
            // money, smallmoney, uniqueidentifier, xml, hierarchyid, geometry, geography, etc.
            return typeName;
        }

        /// <summary>
        /// Returns constraint-definition strings for the table (PK, FK, UQ, CK, named defaults).
        /// Each line is formatted as a constraint clause (e.g. "CONSTRAINT [PK_Name] PRIMARY KEY CLUSTERED ([Id])").
        /// </summary>
        private static async Task<List<string>> GetConstraintDefinitionsAsync(
            SqlConnection connection,
            string schema,
            string tableName,
            string fullName,
            CancellationToken cancellationToken)
        {
            var lines = new List<string>();

            // ── Primary key ──────────────────────────────────────────
            await using (var cmd = new SqlCommand(@"
                SELECT
                    kc.name            AS constraint_name,
                    i.type_desc        AS index_type,
                    COL_NAME(ic.object_id, ic.column_id) AS column_name,
                    ic.key_ordinal
                FROM sys.key_constraints kc
                JOIN sys.indexes i       ON i.object_id = kc.parent_object_id AND i.index_id = kc.unique_index_id
                JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
                WHERE kc.parent_object_id = OBJECT_ID(@fullName) AND kc.type = 'PK'
                ORDER BY kc.name, ic.key_ordinal", connection))
            {
                cmd.Parameters.AddWithValue("@fullName", fullName);
                var pkColumns = new List<(string Name, string Cols)>();
                string? pkName = null;
                string? pkIndexType = null;

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    pkName = reader.GetString(0);
                    pkIndexType = reader.GetString(1);
                    var colName = reader.GetString(2);
                    var existing = pkColumns.FindIndex(x => x.Name == pkName);
                    if (existing >= 0)
                        pkColumns[existing] = (pkName, pkColumns[existing].Cols + ", " + colName);
                    else
                        pkColumns.Add((pkName, $"[{colName}]"));
                }

                if (pkName != null && pkColumns.Count > 0)
                {
                    lines.Add($"CONSTRAINT [{pkName}] PRIMARY KEY {pkIndexType} ({pkColumns[0].Cols})");
                }
            }

            // ── Foreign keys ─────────────────────────────────────────
            await using (var cmd = new SqlCommand(@"
                SELECT
                    fk.name                                     AS constraint_name,
                    COL_NAME(fkc.parent_object_id, fkc.parent_column_id)    AS column_name,
                    OBJECT_SCHEMA_NAME(fkc.referenced_object_id)            AS ref_schema,
                    OBJECT_NAME(fkc.referenced_object_id)                   AS ref_table,
                    COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS ref_column,
                    fkc.constraint_column_id
                FROM sys.foreign_keys fk
                JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
                WHERE fk.parent_object_id = OBJECT_ID(@fullName)
                ORDER BY fk.name, fkc.constraint_column_id", connection))
            {
                cmd.Parameters.AddWithValue("@fullName", fullName);
                var fkGroups = new Dictionary<string, (string RefSchema, string RefTable, List<string> Cols, List<string> RefCols)>(StringComparer.OrdinalIgnoreCase);

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var name = reader.GetString(0);
                    var colName = reader.GetString(1);
                    var refSchema = reader.GetString(2);
                    var refTable = reader.GetString(3);
                    var refCol = reader.GetString(4);

                    if (!fkGroups.TryGetValue(name, out var entry))
                    {
                        entry = (refSchema, refTable, new List<string>(), new List<string>());
                        fkGroups[name] = entry;
                    }
                    entry.Cols.Add($"[{colName}]");
                    entry.RefCols.Add($"[{refCol}]");
                }

                foreach (var kvp in fkGroups)
                {
                    lines.Add($"CONSTRAINT [{kvp.Key}] FOREIGN KEY ({string.Join(", ", kvp.Value.Cols)}) REFERENCES [{kvp.Value.RefSchema}].[{kvp.Value.RefTable}] ({string.Join(", ", kvp.Value.RefCols)})");
                }
            }

            // ── Unique constraints ───────────────────────────────────
            await using (var cmd = new SqlCommand(@"
                SELECT
                    tc.CONSTRAINT_NAME,
                    kcu.COLUMN_NAME,
                    kcu.ORDINAL_POSITION
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                    ON kcu.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
                    AND kcu.TABLE_SCHEMA = tc.TABLE_SCHEMA
                    AND kcu.TABLE_NAME = tc.TABLE_NAME
                WHERE tc.TABLE_SCHEMA = @schema
                  AND tc.TABLE_NAME = @name
                  AND tc.CONSTRAINT_TYPE = 'UNIQUE'
                ORDER BY tc.CONSTRAINT_NAME, kcu.ORDINAL_POSITION", connection))
            {
                cmd.Parameters.AddWithValue("@schema", schema);
                cmd.Parameters.AddWithValue("@name", tableName);

                var uqGroups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var name = reader.GetString(0);
                    var colName = reader.GetString(1);
                    if (!uqGroups.TryGetValue(name, out var cols))
                    {
                        cols = new List<string>();
                        uqGroups[name] = cols;
                    }
                    cols.Add($"[{colName}]");
                }

                foreach (var kvp in uqGroups)
                {
                    lines.Add($"CONSTRAINT [{kvp.Key}] UNIQUE ({string.Join(", ", kvp.Value)})");
                }
            }

            // ── Check constraints ────────────────────────────────────
            await using (var cmd = new SqlCommand(@"
                SELECT
                    cc.CONSTRAINT_NAME,
                    cc.CHECK_CLAUSE
                FROM INFORMATION_SCHEMA.CHECK_CONSTRAINTS cc
                JOIN INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                    ON tc.CONSTRAINT_NAME = cc.CONSTRAINT_NAME
                WHERE tc.TABLE_SCHEMA = @schema
                  AND tc.TABLE_NAME = @name", connection))
            {
                cmd.Parameters.AddWithValue("@schema", schema);
                cmd.Parameters.AddWithValue("@name", tableName);

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var name = reader.GetString(0);
                    var clause = reader.GetString(1);
                    lines.Add($"CONSTRAINT [{name}] CHECK ({clause})");
                }
            }

            return lines;
        }

        /// <summary>
        /// Returns CREATE INDEX statements for non-clustered, non-PK, non-unique-constraint
        /// indexes on the table.
        /// </summary>
        private static async Task<List<string>> GetIndexDefinitionsAsync(
            SqlConnection connection,
            string schema,
            string tableName,
            string fullName,
            CancellationToken cancellationToken)
        {
            var indexLines = new List<string>();

            await using (var cmd = new SqlCommand(@"
                SELECT
                    i.name,
                    i.type_desc,
                    i.is_unique,
                    i.has_filter,
                    i.filter_definition,
                    COL_NAME(ic.object_id, ic.column_id) AS column_name,
                    ic.is_descending_key,
                    ic.key_ordinal
                FROM sys.indexes i
                JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                WHERE i.object_id = OBJECT_ID(@fullName)
                  AND i.is_primary_key = 0
                  AND i.type = 2   -- NONCLUSTERED only
                  AND i.name IS NOT NULL
                ORDER BY i.name, ic.key_ordinal", connection))
            {
                cmd.Parameters.AddWithValue("@fullName", fullName);

                var idxGroups = new Dictionary<string, (string Type, bool IsUnique, bool HasFilter, string? FilterDef, List<(string Col, bool Desc)>)>(StringComparer.OrdinalIgnoreCase);

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var name = reader.GetString(0);
                    var typeDesc = reader.GetString(1);
                    var isUnique = reader.GetBoolean(2);
                    var hasFilter = reader.GetBoolean(3);
                    var filterDef = reader.IsDBNull(4) ? null : reader.GetString(4);
                    var colName = reader.GetString(5);
                    var isDesc = reader.GetBoolean(6);

                    if (!idxGroups.TryGetValue(name, out var entry))
                    {
                        entry = (typeDesc, isUnique, hasFilter, filterDef, new List<(string, bool)>());
                        idxGroups[name] = entry;
                    }
                    entry.Item5.Add((colName, isDesc));
                }

                foreach (var kvp in idxGroups)
                {
                    var sb = new StringBuilder();
                    sb.Append("CREATE ");
                    if (kvp.Value.IsUnique) sb.Append("UNIQUE ");
                    sb.Append("NONCLUSTERED INDEX [").Append(kvp.Key).Append("] ON [").Append(schema).Append("].[").Append(tableName).Append("] (");
                    sb.Append(string.Join(", ", kvp.Value.Item5.Select(c => $"[{c.Col}]" + (c.Desc ? " DESC" : ""))));
                    sb.Append(')');
                    if (kvp.Value.HasFilter && kvp.Value.FilterDef != null)
                    {
                        sb.Append(" WHERE ").Append(kvp.Value.FilterDef);
                    }
                    indexLines.Add(sb.ToString());
                }
            }

            return indexLines;
        }
    }
}
