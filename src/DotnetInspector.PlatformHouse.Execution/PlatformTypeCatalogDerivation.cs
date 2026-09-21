using System.Collections.Immutable;
using System.Diagnostics;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.Libraries;
using DotnetInspector.Platforms;
using ILInspector.Metadata;

namespace DotnetInspector.PlatformHouse;

/// <summary>Finite limits for one complete Platform type catalog.</summary>
public sealed class PlatformTypeCatalogDerivationBounds
{
    public PlatformTypeCatalogDerivationBounds(
        LibraryTypeDeclarationInventoryInspectionBounds memberInspection,
        int maximumAssemblies,
        long maximumAggregateAssemblyBytes,
        int maximumRetainedEntries,
        TimeSpan maximumDuration)
    {
        ArgumentNullException.ThrowIfNull(memberInspection);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumAssemblies);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumAggregateAssemblyBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRetainedEntries);
        if (maximumDuration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maximumDuration));

        MemberInspection = memberInspection;
        MaximumAssemblies = maximumAssemblies;
        MaximumAggregateAssemblyBytes = maximumAggregateAssemblyBytes;
        MaximumRetainedEntries = maximumRetainedEntries;
        MaximumDuration = maximumDuration;
    }

    public LibraryTypeDeclarationInventoryInspectionBounds MemberInspection
    {
        get;
    }

    public int MaximumAssemblies { get; }
    public long MaximumAggregateAssemblyBytes { get; }
    public int MaximumRetainedEntries { get; }
    public TimeSpan MaximumDuration { get; }
}

/// <summary>Measured work for one complete or terminal catalog derivation.</summary>
public sealed class PlatformTypeCatalogDerivationWork
{
    internal PlatformTypeCatalogDerivationWork(
        int observedAssemblies,
        long observedAssemblyBytes,
        long observedEntries,
        TimeSpan elapsed)
    {
        ObservedAssemblies = observedAssemblies;
        ObservedAssemblyBytes = observedAssemblyBytes;
        ObservedEntries = observedEntries;
        Elapsed = elapsed;
    }

    public int ObservedAssemblies { get; }
    public long ObservedAssemblyBytes { get; }
    public long ObservedEntries { get; }
    public TimeSpan Elapsed { get; }
}

/// <summary>
/// One exact structured declaration candidate in a completed Platform
/// population.
/// </summary>
public sealed class PlatformTypeCatalogEntry
{
    internal PlatformTypeCatalogEntry(
        PlatformPopulationMember member,
        LibraryTypeDeclarationInventoryCorrespondence correspondence,
        AssemblyTypeDeclaration declaration)
    {
        Member = member;
        ApiContent = correspondence.ApiContent;
        ModuleVersionId = correspondence.ModuleVersionId;
        Declaration = declaration;
    }

    public PlatformPopulationMember Member { get; }
    public LibraryContentReference ApiContent { get; }
    public Guid ModuleVersionId { get; }
    public AssemblyTypeDeclaration Declaration { get; }
    public MetadataTypeDefinitionName Name => Declaration.Name;
    public AssemblyTypeDeclarationKind Kind => Declaration.Kind;
}

/// <summary>Exact structured lookup over one completed catalog.</summary>
public abstract class PlatformTypeCatalogLookupOutcome
{
    private protected PlatformTypeCatalogLookupOutcome()
    {
    }

    public sealed class Found : PlatformTypeCatalogLookupOutcome
    {
        internal Found(
            ImmutableArray<PlatformTypeCatalogEntry> candidates) =>
            Candidates = candidates;

        public ImmutableArray<PlatformTypeCatalogEntry> Candidates { get; }
    }

    public sealed class Missing : PlatformTypeCatalogLookupOutcome
    {
        internal Missing()
        {
        }
    }
}

/// <summary>
/// Resource-free complete structured type index for one exact Platform
/// population.
/// </summary>
public sealed class PlatformTypeCatalog
{
    private readonly Dictionary<
        MetadataTypeDefinitionName,
        ImmutableArray<PlatformTypeCatalogEntry>> _entriesByName;

    internal PlatformTypeCatalog(
        PlatformPopulationRealizationValue population,
        PlatformPopulationRealizationReceipt populationReceipt,
        ImmutableArray<PlatformTypeCatalogEntry> entries,
        Dictionary<
            MetadataTypeDefinitionName,
            ImmutableArray<PlatformTypeCatalogEntry>> entriesByName,
        PlatformTypeCatalogDerivationWork work)
    {
        Population = population;
        PopulationReceipt = populationReceipt;
        Entries = entries;
        Work = work;
        Target = populationReceipt.HouseReceipt
            .TargetSettlement.SettledTarget
            ?? throw new ArgumentException(
                "A completed Platform type catalog requires an exact target settlement.",
                nameof(populationReceipt));
        View = ((PlatformHouseOperationSnapshot.Realize)
                populationReceipt.HouseReceipt.Request.Operation)
            .View;
        _entriesByName = entriesByName;
    }

    internal static Dictionary<
        MetadataTypeDefinitionName,
        ImmutableArray<PlatformTypeCatalogEntry>> CreateLookup(
        ImmutableArray<PlatformTypeCatalogEntry> entries) =>
        entries
            .GroupBy(static entry => entry.Name)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToImmutableArray());

    public PlatformPopulationRealizationValue Population { get; }
    public PlatformPopulationRealizationReceipt PopulationReceipt { get; }
    public PlatformFamilyTarget Target { get; }
    public PlatformViewDemand View { get; }
    public ImmutableArray<PlatformTypeCatalogEntry> Entries { get; }
    public PlatformTypeCatalogDerivationWork Work { get; }

    public PlatformTypeCatalogLookupOutcome Lookup(
        MetadataTypeDefinitionName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _entriesByName.TryGetValue(name, out var candidates)
            ? new PlatformTypeCatalogLookupOutcome.Found(candidates)
            : new PlatformTypeCatalogLookupOutcome.Missing();
    }
}

public enum PlatformTypeCatalogDerivationBound
{
    PopulationAssemblies,
    MemberAssemblyBytes,
    MemberRetainedDeclarations,
    AggregateAssemblyBytes,
    RetainedEntries,
    Duration,
}

public enum PlatformTypeCatalogDerivationRejectionKind
{
    InvalidPopulation,
    LibraryAuthorityUnavailable,
    MemberCorrespondence,
}

/// <summary>The closed result of one target-bound catalog derivation.</summary>
public abstract class PlatformTypeCatalogDerivationOutcome
{
    private protected PlatformTypeCatalogDerivationOutcome(
        PlatformPopulationRealizationValue population,
        PlatformPopulationRealizationReceipt populationReceipt,
        PlatformTypeCatalogDerivationWork work) =>
        (Population, PopulationReceipt, Work) =
            (population, populationReceipt, work);

    public PlatformPopulationRealizationValue Population { get; }
    public PlatformPopulationRealizationReceipt PopulationReceipt { get; }
    public PlatformTypeCatalogDerivationWork Work { get; }

    public sealed class Completed : PlatformTypeCatalogDerivationOutcome
    {
        internal Completed(PlatformTypeCatalog catalog)
            : base(
                catalog.Population,
                catalog.PopulationReceipt,
                catalog.Work) =>
            Catalog = catalog;

        public PlatformTypeCatalog Catalog { get; }
    }

    public sealed class Incomplete : PlatformTypeCatalogDerivationOutcome
    {
        internal Incomplete(
            PlatformPopulationRealizationValue population,
            PlatformPopulationRealizationReceipt populationReceipt,
            PlatformTypeCatalogDerivationBound bound,
            PlatformTypeCatalogDerivationWork work,
            int? memberIndex = null,
            PlatformPopulationMember? member = null,
            LibraryTypeDeclarationInventoryInspectionOutcome.Incomplete?
                memberOutcome = null)
            : base(population, populationReceipt, work)
        {
            Bound = bound;
            MemberIndex = memberIndex;
            Member = member;
            MemberOutcome = memberOutcome;
        }

        public PlatformTypeCatalogDerivationBound Bound { get; }
        public int? MemberIndex { get; }
        public PlatformPopulationMember? Member { get; }
        public LibraryTypeDeclarationInventoryInspectionOutcome.Incomplete?
            MemberOutcome
        { get; }
    }

    public sealed class Rejected : PlatformTypeCatalogDerivationOutcome
    {
        internal Rejected(
            PlatformPopulationRealizationValue population,
            PlatformPopulationRealizationReceipt populationReceipt,
            PlatformTypeCatalogDerivationRejectionKind kind,
            PlatformTypeCatalogDerivationWork work,
            int? memberIndex = null,
            PlatformPopulationMember? member = null,
            LibraryTypeDeclarationInventoryInspectionOutcome.Rejected?
                memberOutcome = null)
            : base(population, populationReceipt, work)
        {
            Kind = kind;
            MemberIndex = memberIndex;
            Member = member;
            MemberOutcome = memberOutcome;
        }

        public PlatformTypeCatalogDerivationRejectionKind Kind { get; }
        public int? MemberIndex { get; }
        public PlatformPopulationMember? Member { get; }
        public LibraryTypeDeclarationInventoryInspectionOutcome.Rejected?
            MemberOutcome
        { get; }
    }

    public sealed class Failed : PlatformTypeCatalogDerivationOutcome
    {
        internal Failed(
            PlatformPopulationRealizationValue population,
            PlatformPopulationRealizationReceipt populationReceipt,
            PlatformTypeCatalogDerivationWork work,
            int memberIndex,
            PlatformPopulationMember member,
            LibraryTypeDeclarationInventoryInspectionOutcome.Failed
                memberOutcome)
            : base(population, populationReceipt, work)
        {
            MemberIndex = memberIndex;
            Member = member;
            MemberOutcome = memberOutcome;
        }

        public int MemberIndex { get; }
        public PlatformPopulationMember Member { get; }
        public LibraryTypeDeclarationInventoryInspectionOutcome.Failed
            MemberOutcome
        { get; }
    }
}

/// <summary>
/// Derives one all-or-nothing structured catalog from an exact completed
/// Platform reference population.
/// </summary>
public static class PlatformTypeCatalogDerivation
{
    public static PlatformTypeCatalogDerivationOutcome Execute(
        PlatformPopulationRealizationResult.Completed population,
        PlatformTypeCatalogDerivationBounds bounds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(bounds);
        cancellationToken.ThrowIfCancellationRequested();

        var work = new WorkMeasurement();
        if (!IsCompleteReferencePopulation(population))
        {
            return new PlatformTypeCatalogDerivationOutcome.Rejected(
                population.Value,
                population.Receipt,
                PlatformTypeCatalogDerivationRejectionKind.InvalidPopulation,
                work.Snapshot());
        }

        IReadOnlyList<PlatformPopulationMember> members =
            population.Value.Members;
        if (members.Count > bounds.MaximumAssemblies)
        {
            work.ObserveAssemblies(members.Count);
            return new PlatformTypeCatalogDerivationOutcome.Incomplete(
                population.Value,
                population.Receipt,
                PlatformTypeCatalogDerivationBound.PopulationAssemblies,
                work.Snapshot());
        }
        if (work.Elapsed >= bounds.MaximumDuration)
        {
            return new PlatformTypeCatalogDerivationOutcome.Incomplete(
                population.Value,
                population.Receipt,
                PlatformTypeCatalogDerivationBound.Duration,
                work.Snapshot());
        }

        var entries = ImmutableArray.CreateBuilder<
            PlatformTypeCatalogEntry>();
        for (int index = 0; index < members.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (work.Elapsed >= bounds.MaximumDuration)
            {
                return new PlatformTypeCatalogDerivationOutcome.Incomplete(
                    population.Value,
                    population.Receipt,
                    PlatformTypeCatalogDerivationBound.Duration,
                    work.Snapshot());
            }

            PlatformPopulationMember member = members[index];
            LibraryContentOwner owner = population.Owners[index];
            work.ObserveAssembly();
            long remainingAssemblyBytes =
                bounds.MaximumAggregateAssemblyBytes
                - work.ObservedAssemblyBytes;
            if (remainingAssemblyBytes <= 0)
            {
                return new PlatformTypeCatalogDerivationOutcome.Incomplete(
                    population.Value,
                    population.Receipt,
                    PlatformTypeCatalogDerivationBound
                        .AggregateAssemblyBytes,
                    work.Snapshot(),
                    index,
                    member);
            }
            int maximumMemberAssemblyBytes = (int)Math.Min(
                bounds.MemberInspection.MaximumAssemblyBytes,
                Math.Min(remainingAssemblyBytes, int.MaxValue));
            bool aggregateAssemblyBoundApplies =
                maximumMemberAssemblyBytes
                < bounds.MemberInspection.MaximumAssemblyBytes;
            LibraryTypeDeclarationInventoryInspectionBounds memberBounds =
                aggregateAssemblyBoundApplies
                    ? new(
                        maximumMemberAssemblyBytes,
                        bounds.MemberInspection
                            .MaximumRetainedDeclarations)
                    : bounds.MemberInspection;
            LibraryOperationLeaseIssueOutcome leaseOutcome =
                owner.IssueOperationLease(member.Library);
            if (leaseOutcome
                is not LibraryOperationLeaseIssueOutcome.Issued issued)
            {
                PlatformTypeCatalogDerivationRejectionKind kind =
                    leaseOutcome
                        is LibraryOperationLeaseIssueOutcome.ReferenceMismatch
                            ? PlatformTypeCatalogDerivationRejectionKind
                                .MemberCorrespondence
                            : PlatformTypeCatalogDerivationRejectionKind
                                .LibraryAuthorityUnavailable;
                return new PlatformTypeCatalogDerivationOutcome.Rejected(
                    population.Value,
                    population.Receipt,
                    kind,
                    work.Snapshot(),
                    index,
                    member);
            }

            LibraryTypeDeclarationInventoryInspectionOutcome memberOutcome;
            using (issued.Lease)
            {
                memberOutcome =
                    LibraryTypeDeclarationInventoryInspection.Execute(
                        new(
                            member.Library,
                            memberBounds),
                        issued.Lease,
                        cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();

            switch (memberOutcome)
            {
                case LibraryTypeDeclarationInventoryInspectionOutcome
                    .Incomplete incomplete:
                    work.ObserveAssemblyBytes(
                        incomplete.MeasuredAssemblyBytes);
                    return new PlatformTypeCatalogDerivationOutcome.Incomplete(
                        population.Value,
                        population.Receipt,
                        incomplete.Bound
                            == LibraryTypeDeclarationInventoryInspectionBound
                                .AssemblyBytes
                            ? aggregateAssemblyBoundApplies
                                ? PlatformTypeCatalogDerivationBound
                                    .AggregateAssemblyBytes
                                : PlatformTypeCatalogDerivationBound
                                    .MemberAssemblyBytes
                            : PlatformTypeCatalogDerivationBound
                                .MemberRetainedDeclarations,
                        work.Snapshot(),
                        index,
                        member,
                        incomplete);
                case LibraryTypeDeclarationInventoryInspectionOutcome
                    .Rejected rejected:
                    return new PlatformTypeCatalogDerivationOutcome.Rejected(
                        population.Value,
                        population.Receipt,
                        PlatformTypeCatalogDerivationRejectionKind
                            .MemberCorrespondence,
                        work.Snapshot(),
                        index,
                        member,
                        rejected);
                case LibraryTypeDeclarationInventoryInspectionOutcome
                    .Failed failed:
                    return new PlatformTypeCatalogDerivationOutcome.Failed(
                        population.Value,
                        population.Receipt,
                        work.Snapshot(),
                        index,
                        member,
                        failed);
                case LibraryTypeDeclarationInventoryInspectionOutcome
                    .Completed completed:
                    if (!ReferenceEquals(
                            completed.Correspondence.Library,
                            member.Library)
                        || !ReferenceEquals(
                            completed.Correspondence.ApiContent,
                            member.Library.ApiAssembly))
                    {
                        return new PlatformTypeCatalogDerivationOutcome
                            .Rejected(
                                population.Value,
                                population.Receipt,
                                PlatformTypeCatalogDerivationRejectionKind
                                    .MemberCorrespondence,
                                work.Snapshot(),
                                index,
                                member);
                    }

                    work.ObserveAssemblyBytes(
                        completed.Correspondence.AssemblyBytes);
                    if (work.ObservedAssemblyBytes
                        > bounds.MaximumAggregateAssemblyBytes)
                    {
                        return new PlatformTypeCatalogDerivationOutcome
                            .Incomplete(
                                population.Value,
                                population.Receipt,
                                PlatformTypeCatalogDerivationBound
                                    .AggregateAssemblyBytes,
                                work.Snapshot(),
                                index,
                                member);
                    }

                    AssemblyTypeDeclaration[] declarations =
                    [
                        .. completed.Correspondence.Inventory
                            .GetDeclarations(),
                    ];
                    work.ObserveEntries(declarations.Length);
                    if (work.ObservedEntries
                        > bounds.MaximumRetainedEntries)
                    {
                        return new PlatformTypeCatalogDerivationOutcome
                            .Incomplete(
                                population.Value,
                                population.Receipt,
                                PlatformTypeCatalogDerivationBound
                                    .RetainedEntries,
                                work.Snapshot(),
                                index,
                                member);
                    }
                    if (work.Elapsed >= bounds.MaximumDuration)
                    {
                        return new PlatformTypeCatalogDerivationOutcome
                            .Incomplete(
                                population.Value,
                                population.Receipt,
                                PlatformTypeCatalogDerivationBound.Duration,
                                work.Snapshot(),
                                index,
                                member);
                    }

                    foreach (AssemblyTypeDeclaration declaration
                        in declarations)
                    {
                        entries.Add(
                            new PlatformTypeCatalogEntry(
                                member,
                                completed.Correspondence,
                                declaration));
                    }
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown Library declaration inventory outcome.");
            }
        }

        ImmutableArray<PlatformTypeCatalogEntry> retainedEntries =
            entries.ToImmutable();
        Dictionary<
            MetadataTypeDefinitionName,
            ImmutableArray<PlatformTypeCatalogEntry>> entriesByName =
            PlatformTypeCatalog.CreateLookup(retainedEntries);
        if (work.Elapsed >= bounds.MaximumDuration)
        {
            return new PlatformTypeCatalogDerivationOutcome.Incomplete(
                population.Value,
                population.Receipt,
                PlatformTypeCatalogDerivationBound.Duration,
                work.Snapshot());
        }

        PlatformTypeCatalogDerivationWork completedWork = work.Snapshot();
        var catalog = new PlatformTypeCatalog(
            population.Value,
            population.Receipt,
            retainedEntries,
            entriesByName,
            completedWork);
        return new PlatformTypeCatalogDerivationOutcome.Completed(catalog);
    }

    private static bool IsCompleteReferencePopulation(
        PlatformPopulationRealizationResult.Completed population) =>
        population.Outcome.Receipt.SettlementKind
            == PlatformHouseSettlementKind.Completed
        && population.Outcome.Receipt.Request.Operation
            is PlatformHouseOperationSnapshot.Realize
            {
                Population:
                    PlatformPopulationDemand.CompletePopulation,
                View: PlatformViewDemand.Reference,
            };

    private sealed class WorkMeasurement
    {
        private readonly long _started = Stopwatch.GetTimestamp();
        private int _observedAssemblies;
        private long _observedAssemblyBytes;
        private long _observedEntries;

        internal long ObservedAssemblyBytes => _observedAssemblyBytes;
        internal long ObservedEntries => _observedEntries;
        internal TimeSpan Elapsed => Stopwatch.GetElapsedTime(_started);

        internal void ObserveAssemblies(int count) =>
            _observedAssemblies = count;

        internal void ObserveAssembly() => _observedAssemblies++;

        internal void ObserveAssemblyBytes(int count) =>
            _observedAssemblyBytes += count;

        internal void ObserveEntries(int count) =>
            _observedEntries += count;

        internal PlatformTypeCatalogDerivationWork Snapshot() =>
            new(
                _observedAssemblies,
                _observedAssemblyBytes,
                _observedEntries,
                Elapsed);
    }
}
