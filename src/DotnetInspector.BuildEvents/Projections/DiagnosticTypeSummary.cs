namespace DotnetInspector.BuildEvents.Projections;

public sealed record DiagnosticTypeSummary(
    string Severity,
    string Code,
    int Count)
{
    public static IReadOnlyList<DiagnosticTypeSummary> Create(BuildEventLog log, int limit)
    {
        return log.Diagnostics
            .Where(static diagnostic => IsCountedDiagnostic(diagnostic))
            .GroupBy(
                static diagnostic => new DiagnosticTypeKey(
                    diagnostic.Severity.ToLowerInvariant(),
                    diagnostic.Code ?? string.Empty))
            .Select(static group => new DiagnosticTypeSummary(group.Key.Severity, group.Key.Code, group.Count()))
            .OrderByDescending(static summary => summary.Count)
            .ThenBy(static summary => SeverityPriority(summary.Severity))
            .ThenBy(static summary => summary.Code, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    private static bool IsCountedDiagnostic(DiagnosticEvent diagnostic)
        => IsSeverity(diagnostic, "error") || IsSeverity(diagnostic, "warning");

    private static bool IsSeverity(DiagnosticEvent diagnostic, string severity)
        => string.Equals(diagnostic.Severity, severity, StringComparison.OrdinalIgnoreCase);

    private static int SeverityPriority(string severity)
        => string.Equals(severity, "error", StringComparison.OrdinalIgnoreCase) ? 0 : 1;

    private readonly record struct DiagnosticTypeKey(string Severity, string Code);
}
