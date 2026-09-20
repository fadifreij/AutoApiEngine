namespace AutoApiEngine.ServiceAbstraction.DTO
{
    /// <summary>
    /// Describes a single input/output parameter of a stored procedure or function.
    /// </summary>
    public class RoutineParameterDto
    {
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public string ParameterMode { get; set; } = "IN";   // "IN" | "OUT" | "INOUT"
        public bool HasDefault { get; set; }
        public string? DefaultValue { get; set; }
        public int OrdinalPosition { get; set; }
    }
}