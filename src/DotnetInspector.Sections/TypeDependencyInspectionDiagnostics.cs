using DotnetInspector.Core;
using DotnetInspector.RowSelection;

namespace DotnetInspector.Sections;

public static class TypeDependencyInspectionDiagnostics
{
    public static InspectionDiagnostic ParticipantRejected(
        string correspondence) =>
        new(
            "type-dependency.participant-rejected",
            InspectionDiagnosticSeverity.Warning,
            $"Type dependency participant '{correspondence}' was rejected.",
            correspondence);

    public static InspectionDiagnostic Unavailable() =>
        new(
            "type-dependency.unavailable",
            InspectionDiagnosticSeverity.Error,
            "Workspace type dependencies are unavailable because every participant was rejected.");

    public static InspectionDiagnostic RowSelectionFailed(
        RowsCohortSemanticFailure<TypeDependencyRowSet> failure) =>
        new(
            "type-dependency.row-selection-failed",
            InspectionDiagnosticSeverity.Error,
            $"Type dependency row selection stage "
                + $"{failure.Failure.StageNumber} requires row "
                + $"{failure.Failure.RequiredPosition}, but "
                + $"{failure.Identity} has "
                + $"{failure.Failure.AvailableCount} rows.",
            failure.Identity.ToString());
}
