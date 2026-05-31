export interface DdlStatementResult {
  index: number;
  sql: string;
  success: boolean;
  error: string | null;
  rowsAffected: number;
  durationMs: number;
}

export interface DdlExecutionResponse {
  statements: DdlStatementResult[];
  overallSuccess: boolean;
  totalStatements: number;
  succeeded: number;
  failed: number;
}
