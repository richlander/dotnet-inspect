const maximumDiagnosticCharacters = 4_096;
const maximumRetainedCharacters = maximumDiagnosticCharacters * 3;

export function diagnosticDetail(error: unknown): string {
  if (!(error instanceof Error)) {
    return String(error).slice(0, maximumDiagnosticCharacters);
  }
  const stack = error.stack?.trim();
  const detail = stack?.includes(error.message)
    ? stack
    : `${error.message}\n${stack ?? ""}`.trim();
  return detail.slice(0, maximumDiagnosticCharacters);
}

export function retainDiagnosticDetail(
  current: string,
  error: unknown,
): string {
  const detail = diagnosticDetail(error);
  if (!current) return detail;
  if (!detail || detail === current) return current;
  return `${current}\n\nSubsequent failure:\n${detail}`
    .slice(0, maximumRetainedCharacters);
}
