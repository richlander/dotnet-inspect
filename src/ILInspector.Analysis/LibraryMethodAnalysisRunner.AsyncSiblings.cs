using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal sealed partial class LibraryMethodAnalysisRunner
{
    /// <summary>
    /// Finds the synchronous calls with async siblings in one method body when
    /// the method executes an async source; any other method yields no rows.
    /// </summary>
    internal ImmutableArray<AsyncSiblingRow> AnalyzeAsyncSiblings(
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
            return [];
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
        foreach ((DirectCall call, MemberRef sibling)
            in _infrastructure.AsyncSiblingAnalyzer.FindMatches(
                calls,
                asyncSource))
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
        return rows.ToImmutable();
    }
}
