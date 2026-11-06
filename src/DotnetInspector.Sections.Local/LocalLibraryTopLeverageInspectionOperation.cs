using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections.Local;

/// <summary>
/// Executes Library Top Leverage with the bounded Method-definition capability
/// available to local desktop hosts.
/// </summary>
public static class LocalLibraryTopLeverageInspectionOperation
{
    public static InspectionEnvelope<TopLeverageResult> Execute(
        AssemblyInspectionSession session,
        string sourceName,
        AssemblyReferenceBindingAccess? referenceBinding = null) =>
        LibraryTopLeverageInspectionOperation.Execute(
            session,
            sourceName,
            referenceBinding,
            LocalMethodDefinitionParallelExecution.Instance);
}
