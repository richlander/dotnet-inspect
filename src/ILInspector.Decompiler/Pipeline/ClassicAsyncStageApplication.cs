using ILInspector.Metadata;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Selects whether the classic async pass may edit the imported host method.
/// </summary>
internal enum ClassicAsyncStageApplicationKind
{
    PreserveImportedBody,
    EvaluateDeclaredKickoff,
    NoOpinion,
}

internal static class ClassicAsyncStageApplication
{
    internal static ClassicAsyncStageApplicationKind Decide(
        ClassicAsyncRequestAdapterResult? request)
        => request switch
        {
            ClassicAsyncRequestAdapterResult.RequestAvailable
            {
                Evidence.HostRole:
                    ClassicAsyncHostRole.DeclaredKickoff,
            } => ClassicAsyncStageApplicationKind.EvaluateDeclaredKickoff,

            ClassicAsyncRequestAdapterResult.Filtered
            {
                Evidence:
                {
                    HostRole:
                        ClassicAsyncHostRole.Execution
                            or ClassicAsyncHostRole.Support,
                    Relationship:
                    StateMachineRelationshipResult.Resolved
                    {
                        Relationship.Kind:
                            StateMachineClaimKind.ClassicAsync,
                    },
                },
            } => ClassicAsyncStageApplicationKind.PreserveImportedBody,

            _ => ClassicAsyncStageApplicationKind.NoOpinion,
        };
}
