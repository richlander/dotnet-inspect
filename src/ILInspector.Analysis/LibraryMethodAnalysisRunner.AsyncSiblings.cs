using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal readonly record struct AsyncSiblingBodyResult(
    ImmutableArray<AsyncSiblingRow> Rows,
    ImmutableArray<AnalysisDiagnostic> Diagnostics);

internal sealed partial class LibraryMethodAnalysisRunner
{
    /// <summary>
    /// Finds the synchronous calls with async siblings in one method body when
    /// the method executes an async source; any other method yields no rows.
    /// </summary>
    internal AsyncSiblingBodyResult AnalyzeAsyncSiblings(
        TypeDefinitionHandle typeHandle,
        TypeDefinition typeDefinition,
        MethodDefinitionHandle methodHandle,
        MethodDefinition methodDefinition,
        MethodBodyBlock body,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GenericScope scope = _infrastructure.CreateScope(
            typeDefinition,
            methodDefinition);
        MethodIdentity caller = _infrastructure.CreateMethodIdentity(
            typeHandle,
            methodHandle,
            methodDefinition,
            scope);
        bool typeSourceGenerated =
            _infrastructure.IsSourceGeneratedTypeOrEnclosing(typeHandle);
        MethodIdentity? asyncSource = null;
        if (!_infrastructure.TryResolveAsyncSiblingSource(
                caller,
                methodDefinition,
                typeSourceGenerated,
                ref asyncSource))
        {
            return new([], []);
        }

        MethodBodyData metadataBody = RequireMethodBody(
            _infrastructure.PeReader,
            caller.MetadataToken);
        LocalTypeDecodeResult localTypes =
            DecodeLocalTypesWithStatus(body, scope);
        MethodBodyAnalysisContext context =
            MethodBodyAnalysisContext.Create(
                caller,
                metadataBody,
                localTypes.Types,
                localTypes.DeclaredCount,
                localTypes.IncompleteReason,
                body.LocalVariablesInitialized);
        MethodAllocationFacts allocationFacts =
            MethodAllocationFacts.Create(context);
        var calls = ImmutableArray.CreateBuilder<DirectCall>();
        MethodCallAnalysis.Collect(
            context,
            _infrastructure.CreateCallResolver(scope, caller),
            offset => allocationFacts.MultiplicityAt(offset),
            calls,
            ImmutableArray.CreateBuilder<UnsafeEvidence>(),
            includeIndirectOpcodes: false,
            includeCallValueFlow: false);

        var rows = ImmutableArray.CreateBuilder<AsyncSiblingRow>();
        ImmutableArray<AsyncSiblingMatch> matches =
            _infrastructure.AsyncSiblingAnalyzer.FindMatches(
                calls,
                asyncSource,
                out ImmutableArray<DirectCall> unresolvedCalls);
        foreach ((DirectCall call, MemberRef sibling) in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(new(
                asyncSource,
                call.Callee,
                sibling,
                call.Callee.Name == "Dispose"
                    && sibling.Name == "DisposeAsync"
                    ? AsyncSiblingPairKind.Dispose
                    : AsyncSiblingPairKind.Operation,
                call.ILOffset,
                call.InLoop,
                caller.MetadataToken));
        }
        var diagnostics = ImmutableArray.CreateBuilder<AnalysisDiagnostic>();
        foreach (DirectCall call in unresolvedCalls)
        {
            diagnostics.Add(new(
                caller.MetadataToken,
                caller.Name,
                $"The declaring type of '{call.Callee.Name}' could not be resolved at IL offset {call.ILOffset}, so an async sibling was neither found nor ruled out.",
                caller.MetadataToken,
                caller.DeclaringType,
                caller.DeclaringType));
        }
        return new(rows.ToImmutable(), diagnostics.ToImmutable());
    }
}
