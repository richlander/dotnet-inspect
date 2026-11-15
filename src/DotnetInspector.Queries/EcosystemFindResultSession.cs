using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.SourceSelection;

namespace DotnetInspector.Queries;

public enum EcosystemFindMembershipBasisKind
{
    PlatformPopulation,
    CorePackage,
    PackagePrefix,
}

/// <summary>One selected Ecosystem association for an exact producer source.</summary>
public sealed record EcosystemFindMembership
{
    private EcosystemFindMembership(
        WorkspaceEcosystemRegistrationId ecosystem,
        EcosystemFindMembershipBasisKind basis,
        PlatformLibraryPopulationDeclaration? platformPopulation,
        PackageCoordinate? corePackage,
        PackagePrefixDeclaration? packagePrefix)
    {
        Ecosystem = ecosystem ?? throw new ArgumentNullException(nameof(ecosystem));
        Basis = basis;
        PlatformPopulation = platformPopulation;
        CorePackage = corePackage;
        PackagePrefix = packagePrefix;
    }

    public WorkspaceEcosystemRegistrationId Ecosystem { get; }
    public EcosystemFindMembershipBasisKind Basis { get; }
    public PlatformLibraryPopulationDeclaration? PlatformPopulation { get; }
    public PackageCoordinate? CorePackage { get; }
    public PackagePrefixDeclaration? PackagePrefix { get; }

    public static EcosystemFindMembership ForPlatform(
        WorkspaceEcosystemRegistrationId ecosystem,
        PlatformLibraryPopulationDeclaration declaration) =>
        new(
            ecosystem,
            EcosystemFindMembershipBasisKind.PlatformPopulation,
            declaration ?? throw new ArgumentNullException(nameof(declaration)),
            corePackage: null,
            packagePrefix: null);

    public static EcosystemFindMembership ForCorePackage(
        WorkspaceEcosystemRegistrationId ecosystem,
        PackageCoordinate declaration) =>
        new(
            ecosystem,
            EcosystemFindMembershipBasisKind.CorePackage,
            platformPopulation: null,
            declaration ?? throw new ArgumentNullException(nameof(declaration)),
            packagePrefix: null);

    public static EcosystemFindMembership ForPackagePrefix(
        WorkspaceEcosystemRegistrationId ecosystem,
        PackagePrefixDeclaration declaration) =>
        new(
            ecosystem,
            EcosystemFindMembershipBasisKind.PackagePrefix,
            platformPopulation: null,
            corePackage: null,
            declaration ?? throw new ArgumentNullException(nameof(declaration)));
}

/// <summary>
/// One complete, detached Ecosystem Find report value.
/// </summary>
public sealed record EcosystemFindFinding
{
    private EcosystemFindFinding(
        FindSemanticFinding finding,
        IEnumerable<EcosystemFindMembership> memberships)
    {
        Finding = finding ?? throw new ArgumentNullException(nameof(finding));
        ArgumentNullException.ThrowIfNull(memberships);
        Memberships = [.. memberships];
        if (Memberships.IsEmpty
            || Memberships.Any(static membership => membership is null)
            || Memberships.Select(static membership => membership.Ecosystem)
                .Distinct()
                .Count() != Memberships.Length)
        {
            throw new ArgumentException(
                "An Ecosystem finding requires distinct, non-null memberships.",
                nameof(memberships));
        }
    }

    public FindSemanticFinding Finding { get; }
    public ImmutableArray<EcosystemFindMembership> Memberships { get; }

    public static EcosystemFindFinding Create(
        TypeFindSemanticMatch match,
        IEnumerable<EcosystemFindMembership> memberships) =>
        new(new FindSemanticFinding.Type(match), memberships);

    public static EcosystemFindFinding Create(
        MemberFindSemanticMatch match,
        IEnumerable<EcosystemFindMembership> memberships) =>
        new(new FindSemanticFinding.Member(match), memberships);
}

/// <summary>Receives complete Ecosystem findings in final producer order.</summary>
public interface IEcosystemFindFindingSink
{
    void Publish(EcosystemFindFinding finding);
}

public enum EcosystemFindFindingPublication
{
    Published,
    Skipped,
    Duplicate,
    RowLimitReached,
}

public sealed record EcosystemFindResult(
    ImmutableArray<EcosystemFindFinding> Findings,
    EcosystemFindCompletion Completion,
    ImmutableArray<EcosystemFindSourceFailure> Failures,
    ImmutableArray<string> Unsearched)
{
    public bool Incomplete => Completion is not EcosystemFindCompletion.Exhausted;
}

/// <summary>
/// Owns complete-finding duplicate suppression, row admission, sink
/// publication, and identical terminal retention.
/// </summary>
public sealed class EcosystemFindResultSession
{
    private readonly IEcosystemFindFindingSink? _sink;
    private readonly FindInputRowSelection? _selection;
    private readonly HashSet<FindSemanticFindingIdentity> _identities = [];
    private readonly ImmutableArray<EcosystemFindFinding>.Builder _findings =
        ImmutableArray.CreateBuilder<EcosystemFindFinding>();
    private bool _completed;
    private int _uniqueCandidates;

    public EcosystemFindResultSession(
        FindInputRowSelection? selection = null,
        IEcosystemFindFindingSink? sink = null)
    {
        _selection = selection;
        _sink = sink;
    }

    public int Count => _findings.Count;
    public int UniqueCandidateCount => _uniqueCandidates;
    public int? CandidateCapacity => _selection?.End;
    public bool IsSatisfied =>
        _selection is { End: int end }
        && _uniqueCandidates >= end;
    public int? RemainingCandidateCapacity =>
        _selection is { End: int end }
            ? Math.Max(0, end - _uniqueCandidates)
            : null;

    public EcosystemFindFindingPublication Publish(
        EcosystemFindFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        if (_completed)
        {
            throw new InvalidOperationException(
                "The Ecosystem Find result session is already complete.");
        }
        if (_identities.Contains(finding.Finding.Identity))
            return EcosystemFindFindingPublication.Duplicate;
        if (IsSatisfied)
            return EcosystemFindFindingPublication.RowLimitReached;

        int position = checked(_uniqueCandidates + 1);
        if (_selection is { Start: int start } && position < start)
        {
            _identities.Add(finding.Finding.Identity);
            _uniqueCandidates = position;
            return EcosystemFindFindingPublication.Skipped;
        }

        _sink?.Publish(finding);
        _identities.Add(finding.Finding.Identity);
        _uniqueCandidates = position;
        _findings.Add(finding);
        return EcosystemFindFindingPublication.Published;
    }

    public EcosystemFindResult Complete(
        EcosystemFindCompletion completion,
        IEnumerable<EcosystemFindSourceFailure>? failures = null,
        IEnumerable<string>? unsearched = null)
    {
        if (_completed)
        {
            throw new InvalidOperationException(
                "The Ecosystem Find result session is already complete.");
        }

        ImmutableArray<EcosystemFindSourceFailure> retainedFailures =
            failures is null ? [] : [.. failures];
        ImmutableArray<string> retainedUnsearched =
            unsearched is null ? [] : [.. unsearched];
        if (retainedFailures.Any(static failure => failure is null))
        {
            throw new ArgumentException(
                "Ecosystem Find failures cannot contain null.",
                nameof(failures));
        }
        if (retainedUnsearched.Any(static source =>
                string.IsNullOrWhiteSpace(source)))
        {
            throw new ArgumentException(
                "Unsearched Ecosystem Find sources must be named.",
                nameof(unsearched));
        }

        _completed = true;
        return new(
            _findings.DrainToImmutable(),
            completion,
            retainedFailures,
            retainedUnsearched);
    }
}
