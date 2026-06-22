namespace DotnetInspector.BuildEvents.Projections;

public sealed record BuildSummary(
    int Projects,
    int Failed,
    int Errors,
    int Warnings,
    string? EventLogId)
{
    public static BuildSummary Create(BuildEventLog log)
    {
        int projectCount = log.ProjectResults.Count > 0
            ? log.ProjectResults.Count
            : log.Projects.Count;
        int failed = log.ProjectResults.Count(static project => !project.Succeeded);
        int errors = log.Diagnostics.Count(static diagnostic => IsSeverity(diagnostic, "error"));
        int warnings = log.Diagnostics.Count(static diagnostic => IsSeverity(diagnostic, "warning"));

        return new BuildSummary(projectCount, failed, errors, warnings, log.EventLogId);
    }

    private static bool IsSeverity(DiagnosticEvent diagnostic, string severity)
        => string.Equals(diagnostic.Severity, severity, StringComparison.OrdinalIgnoreCase);
}
