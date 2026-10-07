using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Analysis.Planning;

internal sealed class MethodDefinitionGeneratedExpansionWork
{
    readonly MethodDefinitionGeneratedExpansionLimits _limits;
    readonly MethodDefinitionHandleCoverageBuilder _candidates = new();
    readonly MethodDefinitionHandleCoverageBuilder _probeBodies = new();
    readonly HashSet<int> _candidateRows = [];
    readonly HashSet<int> _generatedRows = [];
    readonly HashSet<int> _probeRows = [];
    readonly HashSet<MethodDefinitionGeneratedExpansionOrigin> _origins = [];
    long _probeEncodedIlBytes;
    int _relationshipNodes;

    internal MethodDefinitionGeneratedExpansionWork(
        MethodDefinitionGeneratedExpansionLimits limits) =>
        _limits = limits;

    internal void RecordCandidateDefinition(
        MethodDefinitionHandle handle)
    {
        int row = MetadataTokens.GetRowNumber(handle);
        if (!_candidateRows.Add(row))
            return;
        if (_candidateRows.Count > _limits.MaximumCandidateDefinitions)
        {
            throw LimitExceeded(
                handle,
                "generated candidate-definition");
        }
        _candidates.Add(handle);
    }

    internal void RecordGeneratedMethod(
        MethodDefinitionGeneratedExpansionOrigin origin)
    {
        if (!_origins.Add(origin))
            return;

        int row = MetadataTokens.GetRowNumber(origin.Method);
        if (_generatedRows.Add(row)
            && _generatedRows.Count > _limits.MaximumGeneratedMethods)
        {
            throw LimitExceeded(
                origin.Method,
                "generated physical-method");
        }
    }

    internal void RecordProbeBody(MethodDefinitionHandle handle)
    {
        int row = MetadataTokens.GetRowNumber(handle);
        if (!_probeRows.Add(row))
            return;
        if (_probeRows.Count > _limits.MaximumProbeBodies)
            throw LimitExceeded(handle, "generated-discovery probe-body");
        _probeBodies.Add(handle);
    }

    internal void RecordProbeEncodedIlBytes(
        MethodDefinitionHandle handle,
        int bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        _probeEncodedIlBytes = checked(_probeEncodedIlBytes + bytes);
        if (_probeEncodedIlBytes
            > _limits.MaximumProbeEncodedIlBytes)
        {
            throw LimitExceeded(
                handle,
                "generated-discovery probe encoded-IL-byte");
        }
    }

    internal void RecordRelationshipNode(
        EntityHandle handle)
    {
        _relationshipNodes++;
        if (_relationshipNodes > _limits.MaximumRelationshipNodes)
            throw LimitExceeded(handle, "generated relationship-node");
    }

    internal MethodDefinitionGeneratedExpansionCoverage Build() =>
        new(
            _candidates.Build(),
            _probeBodies.Build(),
            _probeEncodedIlBytes,
            _relationshipNodes,
            [.. _origins
                .OrderBy(static origin =>
                    MetadataTokens.GetRowNumber(origin.Method))
                .ThenBy(static origin => origin.Kind)
                .ThenBy(static origin =>
                    MetadataTokens.GetRowNumber(
                        origin.DeclaredOwner))]);

    static InvalidOperationException LimitExceeded(
        EntityHandle subject,
        string dimension) =>
        new(
            $"Method generated-body expansion {dimension} limit was "
            + "exhausted at "
            + $"0x{MetadataTokens.GetToken(subject):X8}.");
}

internal sealed record MethodDefinitionGeneratedExpansionResult(
    ImmutableArray<MethodDefinitionHandle> Methods,
    MethodDefinitionGeneratedExpansionCoverage Coverage);
