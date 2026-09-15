namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Grants classic-async mutation authority only to an authenticated declared
/// kickoff request.
/// </summary>
internal enum ClassicAsyncStageApplicationKind
{
    PreserveImportedBody,
    EvaluateDeclaredKickoff,
}

internal static class ClassicAsyncStageApplication
{
    internal static ClassicAsyncStageApplicationKind Decide(
        ClassicAsyncRequestAdapterResult? request)
        => request is ClassicAsyncRequestAdapterResult.RequestAvailable
            ? ClassicAsyncStageApplicationKind.EvaluateDeclaredKickoff
            : ClassicAsyncStageApplicationKind.PreserveImportedBody;
}
