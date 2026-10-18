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
    readonly HashSet<MethodDefinitionGeneratedExpansionOrigin>
        _attributedOutsideRoles = [];
    readonly HashSet<int> _unauthenticatedRows = [];
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

    internal void RecordAttributedOutsideRoles(
        MethodDefinitionGeneratedExpansionOrigin origin) =>
        _attributedOutsideRoles.Add(origin);

    /// <summary>
    /// Records a generated body no edge authenticates. One that joins breadth
    /// counts against the generated physical-method limit.
    /// </summary>
    internal void RecordUnauthenticatedCandidate(
        MethodDefinitionHandle handle,
        bool joinsBreadth)
    {
        int row = MetadataTokens.GetRowNumber(handle);
        if (!_unauthenticatedRows.Add(row) || !joinsBreadth)
            return;
        if (_generatedRows.Add(row)
            && _generatedRows.Count > _limits.MaximumGeneratedMethods)
        {
            throw LimitExceeded(
                handle,
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
            Ordered(_origins),
            Ordered(_attributedOutsideRoles),
            [.. _unauthenticatedRows
                .Order()
                .Select(static row =>
                    MetadataTokens.MethodDefinitionHandle(row))]);

    static ImmutableArray<MethodDefinitionGeneratedExpansionOrigin> Ordered(
        IEnumerable<MethodDefinitionGeneratedExpansionOrigin> origins) =>
        [.. origins
            .OrderBy(static origin =>
                MetadataTokens.GetRowNumber(origin.Method))
            .ThenBy(static origin => origin.Kind)
            .ThenBy(static origin =>
                MetadataTokens.GetRowNumber(origin.DeclaredOwner))
            .ThenBy(static origin => origin.Role)];

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

/// <summary>
/// One visited unit's outcome in the declared-source relation, as
/// <c>docs/design/method-query-source.md#declared-source-relation</c> defines
/// it.
/// </summary>
public abstract record MethodDefinitionDeclaredSource
{
    private protected MethodDefinitionDeclaredSource()
    {
    }

    /// <summary>
    /// The unit entered breadth directly and the relation published no
    /// generated outcome for it.
    /// </summary>
    public sealed record Direct : MethodDefinitionDeclaredSource
    {
        Direct()
        {
        }

        public static Direct Instance { get; } = new();
    }

    /// <summary>The relation authenticated the unit's ultimate declared owner.</summary>
    public sealed record Generated(
        MethodDefinitionGeneratedExpansionOrigin Origin)
        : MethodDefinitionDeclaredSource;

    /// <summary>
    /// The relation examined the unit as generated and no edge authenticates
    /// one owner for it.
    /// </summary>
    public sealed record Unauthenticated : MethodDefinitionDeclaredSource
    {
        Unauthenticated()
        {
        }

        public static Unauthenticated Instance { get; } = new();
    }
}

/// <summary>
/// The settled declared-source relation of one execution, indexed by unit.
/// Reading it does no source work; the relation's work and outcomes are
/// receipted with the expansion.
/// </summary>
internal sealed class MethodDefinitionDeclaredSources
{
    readonly Dictionary<MethodDefinitionHandle, MethodDefinitionDeclaredSource>
        _sources = [];

    internal MethodDefinitionDeclaredSources(
        MethodDefinitionGeneratedExpansionCoverage coverage)
    {
        foreach (MethodDefinitionHandle candidate
            in coverage.UnauthenticatedCandidates)
        {
            _sources[candidate] =
                MethodDefinitionDeclaredSource.Unauthenticated.Instance;
        }

        foreach (MethodDefinitionGeneratedExpansionOrigin origin
            in coverage.Origins)
        {
            if (!_sources.TryGetValue(
                    origin.Method,
                    out MethodDefinitionDeclaredSource? existing))
            {
                _sources.Add(
                    origin.Method,
                    new MethodDefinitionDeclaredSource.Generated(origin));
            }
            else if (existing
                    is MethodDefinitionDeclaredSource.Generated generated
                && generated.Origin.DeclaredOwner != origin.DeclaredOwner)
            {
                // Two owners for one body authenticate neither.
                _sources[origin.Method] =
                    MethodDefinitionDeclaredSource.Unauthenticated.Instance;
            }
        }
    }

    internal MethodDefinitionDeclaredSource Of(MethodDefinitionHandle method) =>
        _sources.TryGetValue(
            method,
            out MethodDefinitionDeclaredSource? source)
            ? source
            : MethodDefinitionDeclaredSource.Direct.Instance;
}
