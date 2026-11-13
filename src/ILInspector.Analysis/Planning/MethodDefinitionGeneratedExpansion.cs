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
    int _traversalReferenceBodies;
    int _traversalReferenceBodiesAcquired;
    long _traversalReferenceEncodedIlBytes;
    MethodDefinitionGeneratedExpansionBoundException? _reached;
    bool _settled;

    internal MethodDefinitionGeneratedExpansionWork(
        MethodDefinitionGeneratedExpansionLimits limits) =>
        _limits = limits;

    // The work totals, encoded IL bytes and relationship nodes, include the
    // attempt that reached the bound, because that work was done. The
    // candidate and probe handle sets record only handles admitted within
    // the limit, because the check runs before that work. An outcome
    // recorder checks its bound before it records, so a reached bound leaves
    // only outcomes whose bodies are in breadth: the retained prefix.

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
                MethodDefinitionGeneratedExpansionLimitKind
                    .CandidateDefinitions,
                "generated candidate-definition");
        }
        _candidates.Add(handle);
    }

    internal void RecordGeneratedMethod(
        MethodDefinitionGeneratedExpansionOrigin origin)
    {
        if (_origins.Contains(origin))
            return;

        ReserveGeneratedRow(origin.Method);
        _origins.Add(origin);
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
        if (_unauthenticatedRows.Contains(row))
            return;
        if (joinsBreadth)
            ReserveGeneratedRow(handle);
        _unauthenticatedRows.Add(row);
    }

    internal void RecordProbeBody(MethodDefinitionHandle handle)
    {
        int row = MetadataTokens.GetRowNumber(handle);
        if (!_probeRows.Add(row))
            return;
        if (_probeRows.Count > _limits.MaximumProbeBodies)
        {
            throw LimitExceeded(
                handle,
                MethodDefinitionGeneratedExpansionLimitKind.ProbeBodies,
                "generated-discovery probe-body");
        }
        _probeBodies.Add(handle);
    }

    internal void RecordProbeEncodedIlBytes(
        MethodDefinitionHandle handle,
        int bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        _probeEncodedIlBytes = checked(_probeEncodedIlBytes + bytes);
        if (_probeEncodedIlBytes > _limits.MaximumProbeEncodedIlBytes)
        {
            throw LimitExceeded(
                handle,
                MethodDefinitionGeneratedExpansionLimitKind
                    .ProbeEncodedIlBytes,
                "generated-discovery probe encoded-IL-byte");
        }
    }

    /// <summary>
    /// Records one body whose call and function-pointer operands the relation
    /// scanned during traversal, and whether the relation read that body
    /// itself because no producer had.
    /// </summary>
    internal void RecordTraversalReferenceBody(
        int encodedIlBytes,
        bool acquiredByRelation)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(encodedIlBytes);
        _traversalReferenceBodies++;
        if (acquiredByRelation)
            _traversalReferenceBodiesAcquired++;
        _traversalReferenceEncodedIlBytes = checked(
            _traversalReferenceEncodedIlBytes + encodedIlBytes);
    }

    internal void RecordRelationshipNode(
        EntityHandle handle)
    {
        _relationshipNodes++;
        if (_relationshipNodes > _limits.MaximumRelationshipNodes)
        {
            throw LimitExceeded(
                handle,
                MethodDefinitionGeneratedExpansionLimitKind.RelationshipNodes,
                "generated relationship-node");
        }
    }

    /// <summary>
    /// Ends expansion. A module lookup that shares this work's attribution can
    /// still charge it during unit visits, and a resolver cache can replay a
    /// bound it stored during expansion. Either is then that unit's
    /// recoverable failure, as before expansion kept a prefix.
    /// </summary>
    internal void Settle()
    {
        _settled = true;
        _reached?.Settle();
    }

    /// <summary>
    /// The first bound a recorder raised during expansion, even if a handler
    /// caught it. A raised bound always bounds the relation.
    /// </summary>
    internal MethodDefinitionGeneratedExpansionBoundException? Reached =>
        _reached;

    /// <summary>
    /// Settles the coverage at <paramref name="bound"/>. The bound must be the
    /// one this work raised, so a caller cannot report a bound that no
    /// recorder reached.
    /// </summary>
    internal MethodDefinitionGeneratedExpansionCoverage BuildBounded(
        MethodDefinitionGeneratedExpansionBoundException bound)
    {
        if (!ReferenceEquals(bound, _reached))
        {
            throw new InvalidOperationException(
                "The expansion bound was not raised by this work.");
        }
        return Build() with
        {
            ReachedLimit = bound.Limit,
            ReachedAtToken = bound.SubjectToken,
        };
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
                    MetadataTokens.MethodDefinitionHandle(row))])
        {
            TraversalReferenceBodies = _traversalReferenceBodies,
            TraversalReferenceBodiesAcquired =
                _traversalReferenceBodiesAcquired,
            TraversalReferenceEncodedIlBytes =
                _traversalReferenceEncodedIlBytes,
        };

    void ReserveGeneratedRow(MethodDefinitionHandle handle)
    {
        int row = MetadataTokens.GetRowNumber(handle);
        if (_generatedRows.Contains(row))
            return;
        if (_generatedRows.Count == _limits.MaximumGeneratedMethods)
        {
            throw LimitExceeded(
                handle,
                MethodDefinitionGeneratedExpansionLimitKind.GeneratedMethods,
                "generated physical-method");
        }
        _generatedRows.Add(row);
    }

    static ImmutableArray<MethodDefinitionGeneratedExpansionOrigin> Ordered(
        IEnumerable<MethodDefinitionGeneratedExpansionOrigin> origins) =>
        [.. origins
            .OrderBy(static origin =>
                MetadataTokens.GetRowNumber(origin.Method))
            .ThenBy(static origin => origin.Kind)
            .ThenBy(static origin =>
                MetadataTokens.GetRowNumber(origin.DeclaredOwner))
            .ThenBy(static origin => origin.Role)];

    Exception LimitExceeded(
        EntityHandle subject,
        MethodDefinitionGeneratedExpansionLimitKind limit,
        string dimension)
    {
        string message =
            $"Method generated-body expansion {dimension} limit was "
            + "exhausted at "
            + $"0x{MetadataTokens.GetToken(subject):X8}.";
        if (_settled)
            return new InvalidOperationException(message);

        // The first reached bound ends expansion; a later recorder call
        // reports the same bound.
        _reached ??= new(limit, MetadataTokens.GetToken(subject), message);
        return _reached;
    }
}

/// <summary>
/// A declared generated-expansion bound was reached. During expansion it is
/// not a recoverable method failure, so no resolver's recoverable-failure
/// handler can turn it into an unauthenticated or rejected outcome; only the
/// expansion driver settles it into the retained prefix. Once its work
/// settles, a resolver cache that stored it may replay it, and it is then a
/// recoverable failure of the unit that read the cache.
/// </summary>
internal sealed class MethodDefinitionGeneratedExpansionBoundException(
    MethodDefinitionGeneratedExpansionLimitKind limit,
    int subjectToken,
    string message)
    : Exception(message)
{
    internal MethodDefinitionGeneratedExpansionLimitKind Limit { get; } = limit;

    internal int SubjectToken { get; } = subjectToken;

    /// <summary>Whether the expansion that raised this bound has settled.</summary>
    internal bool IsSettled { get; private set; }

    internal void Settle() => IsSettled = true;
}

/// <param name="Bound">
/// The reached bound when <paramref name="Methods"/> is a retained prefix.
/// </param>
internal sealed record MethodDefinitionGeneratedExpansionResult(
    ImmutableArray<MethodDefinitionHandle> Methods,
    MethodDefinitionGeneratedExpansionCoverage Coverage,
    MethodDefinitionGeneratedExpansionBoundException? Bound = null);

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
