using System.Collections.Immutable;
using System.Globalization;

using DotnetInspector.Libraries;
using CSharpText;
using DotnetInspector.DocumentationHouse;
using DotnetInspector.LibraryMetadata;
using ILInspector.Metadata;
using ILInspector.Research;
using ILInspector.SourceLink;

using DocumentationHouseService = DotnetInspector.DocumentationHouse.DocumentationHouse;

namespace DotnetInspector.Sections;

/// <summary>
/// A section a Library symbol document can carry
/// (docs/design/library-symbol-document.md#request-and-admission).
/// Declarations are always present.
/// </summary>
public enum LibrarySymbolDocumentSection
{
    Declarations,
    NameFamilies,
    Documentation,
    SourceLink,
}

/// <summary>
/// The host's evidence for the documentation section: the source adapter's
/// Library-level compiled-XML contributions for the leased Library
/// (docs/design/documentation-house.md#whole-library-compiled-summaries),
/// the read limits, and the time the read may take.
/// </summary>
public sealed record LibrarySymbolDocumentDocumentation
{
    public LibrarySymbolDocumentDocumentation(
        IReadOnlyList<CompiledXmlContribution> contributions,
        DocumentationHouseLimits limits,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        Contributions = [.. contributions];
        Limits = limits;
        Timeout = timeout;
    }

    public ImmutableArray<CompiledXmlContribution> Contributions { get; }
    public DocumentationHouseLimits Limits { get; }
    public TimeSpan Timeout { get; }
}

/// <summary>
/// The host's evidence for the source_link section
/// (docs/design/library-symbol-document.md#source-and-documentation): what
/// Portable PDB settlement (docs/pdb-acquisition.md) found for the leased
/// Library's API assembly under the host's network policy. Acquisition is
/// the host's; the producer reads only what settlement already returned.
/// </summary>
public abstract record LibrarySymbolDocumentSourceLink
{
    private protected LibrarySymbolDocumentSourceLink()
    {
    }

    /// <summary>
    /// Settlement returned Portable PDB content. <paramref name="Open"/>
    /// opens it as a readable, seekable stream the producer owns; the
    /// producer checks it against the image's CodeView identity.
    /// <paramref name="Limits"/> bounds the SourceLink map read.
    /// </summary>
    public sealed record Settled(Func<Stream> Open, SourceLinkReadLimits Limits)
        : LibrarySymbolDocumentSourceLink
    {
        public Func<Stream> Open { get; init; } =
            Open ?? throw new ArgumentNullException(nameof(Open));

        public SourceLinkReadLimits Limits { get; init; } =
            Limits ?? throw new ArgumentNullException(nameof(Limits));
    }

    /// <summary>Settlement found no Portable PDB in any configured location.</summary>
    public sealed record Unavailable : LibrarySymbolDocumentSourceLink;

    /// <summary>The request forbade the network work settlement needed.</summary>
    public sealed record Offline : LibrarySymbolDocumentSourceLink;

    /// <summary>Settlement failed or stopped incomplete.</summary>
    public sealed record Failed(string Detail) : LibrarySymbolDocumentSourceLink
    {
        public string Detail { get; init; } =
            Detail ?? throw new ArgumentNullException(nameof(Detail));
    }
}

/// <summary>The tool that wrote a document, recorded in its header.</summary>
public sealed record LibrarySymbolDocumentGenerator
{
    public LibrarySymbolDocumentGenerator(string name, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        Name = name;
        Version = version;
    }

    public string Name { get; }
    public string Version { get; }
}

/// <summary>
/// Where a Library came from, as the host knows it. Each value is
/// <c>""</c> when it does not apply, such as the package fields of a local
/// file. The file carries no path outside the package.
/// </summary>
public sealed record LibrarySymbolDocumentSource
{
    public LibrarySymbolDocumentSource(
        string fileName,
        string sourceKind,
        string packageId,
        string packageVersion,
        string packagePath,
        string targetFramework)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKind);
        FileName = fileName;
        SourceKind = sourceKind;
        PackageId = packageId ?? throw new ArgumentNullException(nameof(packageId));
        PackageVersion = packageVersion ?? throw new ArgumentNullException(nameof(packageVersion));
        PackagePath = packagePath ?? throw new ArgumentNullException(nameof(packagePath));
        TargetFramework = targetFramework ?? throw new ArgumentNullException(nameof(targetFramework));
    }

    public string FileName { get; }
    public string SourceKind { get; }
    public string PackageId { get; }
    public string PackageVersion { get; }
    public string PackagePath { get; }
    public string TargetFramework { get; }
}

public sealed record LibrarySymbolDocumentRequest
{
    /// <param name="sections">
    /// The sections to produce. <c>null</c> requests the default: every
    /// section this producer offers, with documentation and source_link only
    /// when <paramref name="documentation"/> and
    /// <paramref name="sourceLink"/> supply their evidence.
    /// <see cref="LibrarySymbolDocumentSection.Declarations"/> is always
    /// included.
    /// </param>
    public LibrarySymbolDocumentRequest(
        MetadataLibrarySymbolAdmission admission,
        LibrarySymbolDocumentGenerator generator,
        LibrarySymbolDocumentSource source,
        ApiSurfaceExtractionBounds bounds,
        IEnumerable<LibrarySymbolDocumentSection>? sections = null,
        LibrarySymbolDocumentDocumentation? documentation = null,
        LibrarySymbolDocumentSourceLink? sourceLink = null)
    {
        if (!Enum.IsDefined(admission))
            throw new ArgumentOutOfRangeException(nameof(admission));
        Admission = admission;
        Generator = generator ?? throw new ArgumentNullException(nameof(generator));
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Bounds = bounds ?? throw new ArgumentNullException(nameof(bounds));
        LibrarySymbolDocumentSection[] requested =
            sections?.ToArray()
            ??
            [
                .. Enum.GetValues<LibrarySymbolDocumentSection>()
                    .Where(section => section switch
                    {
                        LibrarySymbolDocumentSection.Documentation => documentation is not null,
                        LibrarySymbolDocumentSection.SourceLink => sourceLink is not null,
                        _ => true,
                    }),
            ];
        foreach (LibrarySymbolDocumentSection section in requested)
        {
            if (!Enum.IsDefined(section))
                throw new ArgumentOutOfRangeException(nameof(sections));
        }
        Sections =
        [
            .. requested
                .Append(LibrarySymbolDocumentSection.Declarations)
                .Distinct()
                .Order(),
        ];
        if (Sections.Contains(LibrarySymbolDocumentSection.Documentation)
            && documentation is null)
        {
            throw new ArgumentException(
                "The documentation section requires the host's compiled-XML evidence.",
                nameof(documentation));
        }
        Documentation = documentation;
        if (Sections.Contains(LibrarySymbolDocumentSection.SourceLink)
            && sourceLink is null)
        {
            throw new ArgumentException(
                "The source_link section requires the host's Portable PDB settlement.",
                nameof(sourceLink));
        }
        SourceLink = sourceLink;
    }

    public MetadataLibrarySymbolAdmission Admission { get; }
    public ImmutableArray<LibrarySymbolDocumentSection> Sections { get; }
    public LibrarySymbolDocumentDocumentation? Documentation { get; }
    public LibrarySymbolDocumentSourceLink? SourceLink { get; }
    public LibrarySymbolDocumentGenerator Generator { get; }
    public LibrarySymbolDocumentSource Source { get; }
    public ApiSurfaceExtractionBounds Bounds { get; }
}

public enum LibrarySymbolSectionStatus
{
    Complete,
    Partial,
    Failed,
    Offline,
    NotRequested,
}

public sealed record LibrarySymbolSectionOutcome(
    string Name,
    LibrarySymbolSectionStatus Status,
    string Detail);

public enum LibrarySymbolDocumentAbort
{
    UnreadableLibrary,
    MetadataRowBudget,
    TypeBudget,
    TypeForwarderBudget,
    MemberBudget,
    InterfaceClosureBudget,
    RetainedTextBudget,
    DocumentationBudget,
    SourceLinkBudget,
}

public abstract record LibrarySymbolDocumentOutcome
{
    private protected LibrarySymbolDocumentOutcome()
    {
    }

    /// <summary>
    /// The whole document was written. It is <c>partial</c> when a
    /// requested section did not settle; the host discloses that and exits
    /// nonzero, but the document is still whole.
    /// </summary>
    public sealed record Written(
        bool IsComplete,
        int Types,
        int Members,
        ImmutableArray<LibrarySymbolSectionOutcome> Sections)
        : LibrarySymbolDocumentOutcome;

    /// <summary>
    /// The write was abandoned: the bytes already written are not a
    /// document and the host must not publish them.
    /// </summary>
    public sealed record Aborted(LibrarySymbolDocumentAbort Reason, string Detail)
        : LibrarySymbolDocumentOutcome;
}

/// <summary>
/// Writes a Library symbol document for one Library to a stream
/// (docs/design/library-symbol-document.md). The producer streams: it holds
/// the Type records and the lookups' identity lists, never the Member
/// records. Atomic publication to a file belongs to the host.
/// </summary>
public static class LibrarySymbolDocumentOperation
{
    public const string SchemaVersion = "1";

    public static LibrarySymbolDocumentOutcome Execute(
        LibrarySymbolDocumentRequest request,
        LibraryOperationLease lease,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(destination);
            cancellationToken.ThrowIfCancellationRequested();

            // Summaries are read through the same lease before the metadata
            // scan borrows the API content, so the two never nest.
            Summaries summaries = Summaries.NotRequested;
            if (request.Sections.Contains(LibrarySymbolDocumentSection.Documentation))
            {
                switch (Summaries.Read(request.Documentation!, lease, cancellationToken))
                {
                    case Summaries.Outcome.Read read:
                        summaries = read.Summaries;
                        break;
                    case Summaries.Outcome.Exhausted exhausted:
                        return new LibrarySymbolDocumentOutcome.Aborted(
                            LibrarySymbolDocumentAbort.DocumentationBudget,
                            exhausted.Detail);
                    default:
                        throw new InvalidOperationException("Unknown documentation outcome.");
                }
            }

            LibrarySymbolInspectionOutcome<LibrarySymbolDocumentOutcome> outcome =
                LibrarySymbolInspection.Execute(
                    new LibrarySymbolInspectionRequest(
                        request.Admission,
                        request.Bounds,
                        request.Sections.Contains(LibrarySymbolDocumentSection.SourceLink)
                            && request.SourceLink is LibrarySymbolDocumentSourceLink.Settled settled
                            ? settled.Open
                            : null),
                    lease,
                    (context, token) => Write(request, summaries, context, destination, token),
                    cancellationToken);
            return outcome switch
            {
                LibrarySymbolInspectionOutcome<LibrarySymbolDocumentOutcome>.Completed completed =>
                    completed.Result,
                LibrarySymbolInspectionOutcome<LibrarySymbolDocumentOutcome>.Incomplete incomplete =>
                    new LibrarySymbolDocumentOutcome.Aborted(
                        LibrarySymbolDocumentAbort.MetadataRowBudget,
                        $"The image has {incomplete.Measured.ToString(CultureInfo.InvariantCulture)} metadata rows; the limit is {request.Bounds.MaxMetadataRows.ToString(CultureInfo.InvariantCulture)}."),
                LibrarySymbolInspectionOutcome<LibrarySymbolDocumentOutcome>.Failed failed =>
                    new LibrarySymbolDocumentOutcome.Aborted(
                        LibrarySymbolDocumentAbort.UnreadableLibrary,
                        failed.Reason.ToString()),
                _ => throw new InvalidOperationException(
                    "Unknown Library symbol inspection outcome."),
            };
        }
        finally
        {
            lease.Dispose();
        }
    }

    static LibrarySymbolDocumentOutcome Write(
        LibrarySymbolDocumentRequest request,
        Summaries summaries,
        LibrarySymbolInspectionContext context,
        Stream destination,
        CancellationToken cancellationToken)
    {
        MetadataLibrarySymbolScan scan = context.Scan;
        ImmutableArray<MetadataSymbolType> types;
        switch (scan.ReadTypes(cancellationToken))
        {
            case MetadataLibrarySymbolTypesOutcome.Available available:
                types = available.Types;
                break;
            case MetadataLibrarySymbolTypesOutcome.Incomplete incomplete:
                return Budget(incomplete.Bound, incomplete.Limit, incomplete.Measured);
            case MetadataLibrarySymbolTypesOutcome.Failed failed:
                return new LibrarySymbolDocumentOutcome.Aborted(
                    LibrarySymbolDocumentAbort.UnreadableLibrary,
                    failed.Detail);
            default:
                throw new InvalidOperationException("Unknown symbol Types outcome.");
        }

        // Conforming metadata cannot repeat a Type's documentation identity
        // (ECMA-335 II.22.14, II.22.37); a later repeat keeps its record in
        // type_collisions (#identity).
        var entries = new List<MetadataSymbolType>(types.Length);
        var collisions = new List<MetadataSymbolType>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (MetadataSymbolType type in types)
        {
            if (seen.Add(type.DocumentationId))
                entries.Add(type);
            else
                collisions.Add(type);
        }

        NameFamilies? families = null;
        LibrarySymbolSectionOutcome nameFamiliesOutcome =
            new("name_families", LibrarySymbolSectionStatus.NotRequested, "");
        if (request.Sections.Contains(LibrarySymbolDocumentSection.NameFamilies))
        {
            switch (NameFamilies.Read(scan, context.ModuleVersionId))
            {
                case NameFamilies.Outcome.Read read:
                    families = read.Families;
                    string[] unassigned =
                    [
                        .. types
                            .Where(type => read.Families.For(type) is null)
                            .Select(type => type.DocumentationId)
                            .Distinct(StringComparer.Ordinal),
                    ];
                    nameFamiliesOutcome = unassigned.Length == 0
                        ? nameFamiliesOutcome with { Status = LibrarySymbolSectionStatus.Complete }
                        : nameFamiliesOutcome with
                        {
                            Status = LibrarySymbolSectionStatus.Partial,
                            Detail = "Types absent from the Type declaration inventory: "
                                + string.Join(", ", unassigned),
                        };
                    break;
                case NameFamilies.Outcome.Failed failed:
                    nameFamiliesOutcome = nameFamiliesOutcome with
                    {
                        Status = LibrarySymbolSectionStatus.Failed,
                        Detail = failed.Detail,
                    };
                    break;
                case NameFamilies.Outcome.Incomplete incomplete:
                    return Budget(
                        MetadataLibrarySymbolBound.RetainedTextCharacters,
                        request.Bounds.MaxRetainedTextCharacters,
                        incomplete.Measured);
                default:
                    throw new InvalidOperationException("Unknown name-family outcome.");
            }
        }
        TypeFamilies Facts(MetadataSymbolType type) =>
            families?.For(type) ?? TypeFamilies.Empty;

        Sources sources;
        switch (Sources.Read(request, context.DebugInformation))
        {
            case Sources.Outcome.Read read:
                sources = read.Sources;
                break;
            case Sources.Outcome.Exhausted exhausted:
                return new LibrarySymbolDocumentOutcome.Aborted(
                    LibrarySymbolDocumentAbort.SourceLinkBudget,
                    exhausted.Detail);
            default:
                throw new InvalidOperationException("Unknown source outcome.");
        }
        using Sources ownedSources = sources;

        var lookups = new Lookups();
        foreach (MetadataSymbolType type in types)
            lookups.AddType(type, Facts(type));

        var writer = new LibrarySymbolDocumentWriter(destination);
        writer.Raw("{");
        writer.Raw($"\"schema_version\": {LibrarySymbolDocumentWriter.Quote(SchemaVersion)},");
        writer.Raw(
            $"\"generator\": {{\"name\": {LibrarySymbolDocumentWriter.Quote(request.Generator.Name)}, "
            + $"\"version\": {LibrarySymbolDocumentWriter.Quote(request.Generator.Version)}}},");
        LibrarySymbolDocumentSource source = request.Source;
        writer.Raw(
            "\"library\": {"
            + $"\"name\": {LibrarySymbolDocumentWriter.Quote(context.AssemblyIdentity.Name)}, "
            + $"\"file_name\": {LibrarySymbolDocumentWriter.Quote(source.FileName)}, "
            + $"\"assembly_version\": {LibrarySymbolDocumentWriter.Quote(context.AssemblyIdentity.Version?.ToString() ?? "")}, "
            + $"\"module_version_id\": {LibrarySymbolDocumentWriter.Quote(context.ModuleVersionId.ToString("D", CultureInfo.InvariantCulture))}, "
            + $"\"source_kind\": {LibrarySymbolDocumentWriter.Quote(source.SourceKind)}, "
            + $"\"package_id\": {LibrarySymbolDocumentWriter.Quote(source.PackageId)}, "
            + $"\"package_version\": {LibrarySymbolDocumentWriter.Quote(source.PackageVersion)}, "
            + $"\"package_path\": {LibrarySymbolDocumentWriter.Quote(source.PackagePath)}, "
            + $"\"target_framework\": {LibrarySymbolDocumentWriter.Quote(source.TargetFramework)}}},");
        writer.Raw(
            $"\"request\": {{\"admission\": {LibrarySymbolDocumentWriter.Quote(Admission(request.Admission))}, "
            + $"\"sections\": [{string.Join(", ", request.Sections.Select(section => LibrarySymbolDocumentWriter.Quote(SectionName(section))))}]}},");

        writer.Raw("\"types\": {");
        for (int index = 0; index < entries.Count; index++)
            writer.TypeLine(entries[index], Facts(entries[index]), summaries.For(entries[index]), sources.For(entries[index]), last: index == entries.Count - 1);
        writer.Raw("},");
        writer.Raw("\"type_collisions\": [");
        for (int index = 0; index < collisions.Count; index++)
            writer.CollisionLine(collisions[index], Facts(collisions[index]), summaries.For(collisions[index]), sources.For(collisions[index]), last: index == collisions.Count - 1);
        writer.Raw("],");

        // Members follow their declaring Type's position, then metadata
        // order. Each record is written as the scan delivers it; only the
        // previous record is held, until its successor settles its comma.
        writer.Raw("\"members\": [");
        int memberCount = 0;
        (MetadataSymbolMember Member, MetadataSymbolType Type)? pending = null;
        foreach (MetadataSymbolType type in types)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MetadataLibrarySymbolMembersOutcome members;
            try
            {
                members = scan.ReadMembers(
                    type,
                    member =>
                    {
                        // MaxMembers bounds the whole Library; each Type's
                        // population also bounds the Members it scans.
                        if (memberCount >= request.Bounds.MaxMembers)
                            throw new MemberBudgetExceededException(memberCount + 1L);
                        if (pending is var (previous, previousType))
                            writer.MemberLine(previous, previousType, Facts(previousType), summaries.For(previous), sources.For(previous), last: false);
                        pending = (member, type);
                        lookups.AddMember(member);
                        memberCount++;
                    },
                    cancellationToken);
            }
            catch (MemberBudgetExceededException exceeded)
            {
                return Budget(
                    MetadataLibrarySymbolBound.Members,
                    request.Bounds.MaxMembers,
                    exceeded.Measured);
            }
            switch (members)
            {
                case MetadataLibrarySymbolMembersOutcome.Available:
                    break;
                case MetadataLibrarySymbolMembersOutcome.Incomplete incomplete:
                    return Budget(incomplete.Bound, incomplete.Limit, incomplete.Measured);
                case MetadataLibrarySymbolMembersOutcome.Failed failed:
                    return new LibrarySymbolDocumentOutcome.Aborted(
                        LibrarySymbolDocumentAbort.UnreadableLibrary,
                        failed.Detail);
                default:
                    throw new InvalidOperationException("Unknown symbol Members outcome.");
            }
        }
        if (pending is var (lastMember, lastType))
            writer.MemberLine(lastMember, lastType, Facts(lastType), summaries.For(lastMember), sources.For(lastMember), last: true);
        writer.Raw("],");

        lookups.Write(writer);

        var sections = ImmutableArray.Create(
            new LibrarySymbolSectionOutcome(
                "declarations",
                collisions.Count == 0
                    ? LibrarySymbolSectionStatus.Complete
                    : LibrarySymbolSectionStatus.Partial,
                collisions.Count == 0
                    ? ""
                    : "Repeated Type documentation identities: "
                        + string.Join(
                            ", ",
                            collisions
                                .Select(type => type.DocumentationId)
                                .Distinct(StringComparer.Ordinal))),
            nameFamiliesOutcome,
            summaries.Section,
            sources.Section,
            new LibrarySymbolSectionOutcome("findings", LibrarySymbolSectionStatus.NotRequested, ""));
        // A requested section settles complete, offline, or not requested
        // (#completion-and-failure); partial or failed leaves the document
        // partial.
        bool complete = sections.All(
            section => section.Status
                is LibrarySymbolSectionStatus.Complete
                or LibrarySymbolSectionStatus.Offline
                or LibrarySymbolSectionStatus.NotRequested);
        writer.Raw(
            "\"section_outcomes\": ["
            + string.Join(
                ", ",
                sections.Select(section =>
                    $"{{\"name\": {LibrarySymbolDocumentWriter.Quote(section.Name)}, "
                    + $"\"status\": {LibrarySymbolDocumentWriter.Quote(Status(section.Status))}, "
                    + $"\"detail\": {LibrarySymbolDocumentWriter.Quote(section.Detail)}}}"))
            + "],");
        writer.Raw(
            $"\"counts\": {{\"types\": {types.Length.ToString(CultureInfo.InvariantCulture)}, "
            + $"\"members\": {memberCount.ToString(CultureInfo.InvariantCulture)}}},");
        writer.Raw($"\"outcome\": {LibrarySymbolDocumentWriter.Quote(complete ? "complete" : "partial")}");
        writer.Raw("}");
        writer.Flush();
        return new LibrarySymbolDocumentOutcome.Written(complete, types.Length, memberCount, sections);
    }

    sealed class MemberBudgetExceededException(long measured) : Exception
    {
        public long Measured { get; } = measured;
    }

    /// <summary>
    /// Each record's documentation (#source-and-documentation), from one
    /// whole-Library compiled-XML summary read of the leased Library. A
    /// summary is joined by the record's own documentation ID, the compiler
    /// XML ID the scan issued for that declaration.
    /// </summary>
    sealed class Summaries
    {
        readonly IReadOnlyDictionary<string, string>? _byId;
        readonly string _missingStatus;

        Summaries(
            IReadOnlyDictionary<string, string>? byId,
            string missingStatus,
            LibrarySymbolSectionOutcome section)
        {
            _byId = byId;
            _missingStatus = missingStatus;
            Section = section;
        }

        internal static Summaries NotRequested { get; } = new(
            null,
            "not_requested",
            new("documentation", LibrarySymbolSectionStatus.NotRequested, ""));

        internal LibrarySymbolSectionOutcome Section { get; }

        internal abstract record Outcome
        {
            internal sealed record Read(Summaries Summaries) : Outcome;

            /// <summary>A documentation budget was exhausted; the write aborts.</summary>
            internal sealed record Exhausted(string Detail) : Outcome;
        }

        internal RecordDocumentation For(MetadataSymbolType type) =>
            type.DeclarationKind is MetadataSymbolDeclarationKind.Forwarder
                ? RecordDocumentation.Forwarder
                : For(type.DocumentationId);

        internal RecordDocumentation For(MetadataSymbolMember member) =>
            For(member.DocumentationId);

        RecordDocumentation For(string documentationId) =>
            _byId is not null
                && _byId.TryGetValue(documentationId, out string? summary)
                && summary.Length > 0
                ? new("complete", summary)
                : new(_missingStatus, "");

        internal static Outcome Read(
            LibrarySymbolDocumentDocumentation documentation,
            LibraryOperationLease lease,
            CancellationToken cancellationToken)
        {
            DocumentationLibrarySummaryOutcome outcome =
                DocumentationHouseService.ReadLibrarySummaries(
                    new DocumentationLibrarySummaryRequest(
                        documentation.Contributions,
                        documentation.Limits,
                        DateTimeOffset.UtcNow.Add(documentation.Timeout)),
                    lease,
                    cancellationToken);
            return outcome switch
            {
                DocumentationLibrarySummaryOutcome.Available available =>
                    Settled(available.Summaries, ""),
                // No companion, proven or not: every record settles as
                // having no documentation.
                DocumentationLibrarySummaryOutcome.Absent
                    or DocumentationLibrarySummaryOutcome.Unavailable =>
                    Settled(null, ""),
                DocumentationLibrarySummaryOutcome.Incomplete
                {
                    Boundary: DocumentationIncompleteBoundary.CompanionSelectionPartial,
                } =>
                    Unsettled(
                        LibrarySymbolSectionStatus.Partial,
                        "The compiled XML companion selection was partial."),
                DocumentationLibrarySummaryOutcome.Incomplete incomplete =>
                    new Outcome.Exhausted(
                        $"The documentation read stopped at {incomplete.Boundary}."),
                DocumentationLibrarySummaryOutcome.Ambiguous =>
                    Unsettled(
                        LibrarySymbolSectionStatus.Failed,
                        "Several compiled XML companions without precedence."),
                DocumentationLibrarySummaryOutcome.Rejected rejected =>
                    Unsettled(
                        LibrarySymbolSectionStatus.Failed,
                        "Compiled XML evidence was rejected: "
                            + string.Join(
                                ", ",
                                rejected.Rejections
                                    .Select(rejection => rejection.Kind.ToString())
                                    .Distinct(StringComparer.Ordinal))),
                DocumentationLibrarySummaryOutcome.Failed failed =>
                    Unsettled(
                        LibrarySymbolSectionStatus.Failed,
                        $"The compiled XML companion could not be read ({failed.Kind})."),
                _ => throw new InvalidOperationException(
                    "Unknown Library summary outcome."),
            };

            static Outcome Settled(IReadOnlyDictionary<string, string>? byId, string detail) =>
                new Outcome.Read(
                    new Summaries(
                        byId,
                        "no_documentation",
                        new("documentation", LibrarySymbolSectionStatus.Complete, detail)));

            static Outcome Unsettled(LibrarySymbolSectionStatus status, string detail) =>
                new Outcome.Read(
                    new Summaries(
                        null,
                        "failed",
                        new("documentation", status, detail)));
        }
    }

    /// <summary>
    /// Each record's source (#source-and-documentation), read from the
    /// host-settled Portable PDB loaded into the very image the scan reads.
    /// A Member's line is its primary document's lowest visible
    /// sequence-point line, through the PDB owner member Source Locations
    /// reads, with a classic async method's line taken from its state
    /// machine; a Type's URLs are the documents Type Source Files lists for
    /// it, as the SourceLink map assigns them. Nothing is held per record:
    /// each is mapped when its line is written.
    /// </summary>
    sealed class Sources : IDisposable
    {
        readonly PdbContext? _pdb;
        readonly SourceLinkService? _sourceLink;
        readonly bool _hasMap;
        readonly string _settledStatus;
        readonly LibrarySymbolSectionOutcome _section;
        int _failures;
        string _firstFailure = "";

        Sources(
            string settledStatus,
            LibrarySymbolSectionOutcome section,
            PdbContext? pdb = null,
            SourceLinkService? sourceLink = null,
            bool hasMap = false)
        {
            _settledStatus = settledStatus;
            _section = section;
            _pdb = pdb;
            _sourceLink = sourceLink;
            _hasMap = hasMap;
        }

        internal abstract record Outcome
        {
            internal sealed record Read(Sources Sources) : Outcome;

            /// <summary>A SourceLink map read limit was exhausted; the write aborts.</summary>
            internal sealed record Exhausted(string Detail) : Outcome;
        }

        /// <summary>
        /// The section outcome, final once every record has been mapped: a
        /// record whose mapping failed leaves a mapped section partial.
        /// </summary>
        internal LibrarySymbolSectionOutcome Section =>
            _failures == 0
                ? _section
                : _section with
                {
                    Status = LibrarySymbolSectionStatus.Partial,
                    Detail = $"Source mapping failed for {_failures.ToString(CultureInfo.InvariantCulture)} records, first {_firstFailure}.",
                };

        internal static Outcome Read(
            LibrarySymbolDocumentRequest request,
            LibrarySymbolDebugInformation debugInformation)
        {
            if (!request.Sections.Contains(LibrarySymbolDocumentSection.SourceLink))
                return Uniform("not_requested", LibrarySymbolSectionStatus.NotRequested, "");

            switch (request.SourceLink)
            {
                case LibrarySymbolDocumentSourceLink.Offline:
                    return Uniform("offline", LibrarySymbolSectionStatus.Offline, "");
                case LibrarySymbolDocumentSourceLink.Unavailable:
                    return Uniform("no_pdb", LibrarySymbolSectionStatus.Complete, "");
                case LibrarySymbolDocumentSourceLink.Failed failed:
                    return Uniform(
                        "failed",
                        LibrarySymbolSectionStatus.Failed,
                        "Portable PDB settlement failed: " + failed.Detail);
                case LibrarySymbolDocumentSourceLink.Settled settled:
                    break;
                default:
                    throw new InvalidOperationException("Unknown Portable PDB settlement evidence.");
            }

            PdbContext pdb;
            switch (debugInformation)
            {
                case LibrarySymbolDebugInformation.Loaded loaded:
                    pdb = loaded.Context;
                    break;
                case LibrarySymbolDebugInformation.Rejected rejected:
                    return Uniform(
                        "failed",
                        LibrarySymbolSectionStatus.Failed,
                        "The settled Portable PDB was rejected: " + rejected.Detail);
                default:
                    throw new InvalidOperationException(
                        "Settled Portable PDB evidence was not loaded.");
            }

            var sourceLink = SourceLinkService.Borrow(
                pdb,
                ((LibrarySymbolDocumentSourceLink.Settled)request.SourceLink).Limits);
            SourceLinkMapLimitKind limit = sourceLink.SourceLinkMapLimit;
            if (limit is not SourceLinkMapLimitKind.None)
            {
                sourceLink.Dispose();
                return new Outcome.Exhausted(
                    $"The SourceLink map read stopped at its {limit} limit.");
            }
            SourceLinkMapInspection map = sourceLink.SourceLinkMap;
            if (map.Status is SourceLinkMapStatus.Unusable)
            {
                sourceLink.Dispose();
                return Uniform(
                    "failed",
                    LibrarySymbolSectionStatus.Failed,
                    "The SourceLink map is unusable: " + map.Error);
            }
            return new Outcome.Read(
                new Sources(
                    "",
                    new("source_link", LibrarySymbolSectionStatus.Complete, ""),
                    pdb,
                    sourceLink,
                    hasMap: map.Status is not SourceLinkMapStatus.Absent));

            static Outcome Uniform(string status, LibrarySymbolSectionStatus section, string detail) =>
                new Outcome.Read(new Sources(status, new("source_link", section, detail)));
        }

        internal TypeSource For(MetadataSymbolType type)
        {
            if (type.DeclarationKind is MetadataSymbolDeclarationKind.Forwarder)
                return TypeSource.Of("forwarder");
            if (_pdb is null)
                return TypeSource.Of(_settledStatus);

            PdbTypeDocuments documents;
            try
            {
                documents = _pdb.ReadTypeDocuments(type.MetadataToken);
            }
            catch (Exception exception) when (IsMappingFailure(exception))
            {
                Fail(type.DocumentationId);
                return TypeSource.Of("failed");
            }

            if (!documents.HasMethodBody)
                return TypeSource.Of("no_method_body");
            if (documents.Documents.Count == 0)
                return TypeSource.Of("no_sequence_points");
            if (!_hasMap)
                return TypeSource.Of("no_source_link");
            ImmutableArray<string> urls =
            [
                .. documents.Documents
                    .Select(document => _sourceLink!.ResolveDocumentUrl(document.FilePath))
                    .OfType<string>()
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal),
            ];
            return urls.IsEmpty
                ? TypeSource.Of("unmapped_document")
                : new TypeSource("complete", urls);
        }

        internal MemberSource For(MetadataSymbolMember member)
        {
            if (_pdb is null)
                return MemberSource.Of(_settledStatus);
            if (member.BodyMethodTokens.IsEmpty)
                return MemberSource.Of("no_method_body");

            // The best-ranked body that maps wins, as in member Source
            // Locations: a later accessor is consulted only when a preferred
            // one carries no visible sequence point.
            PdbMemberDocumentInfo? mapping = null;
            try
            {
                foreach (int token in member.BodyMethodTokens)
                {
                    mapping = _pdb
                        .EnumerateMemberDocuments(
                            new HashSet<int> { token },
                            includeStateMachineImplementations: true)
                        .OrderByDescending(static candidate => candidate.IsPrimaryDocument)
                        .ThenBy(static candidate => candidate.DocumentRowId)
                        .FirstOrDefault();
                    if (mapping is not null)
                        break;
                }
            }
            catch (Exception exception) when (IsMappingFailure(exception))
            {
                Fail(member.DocumentationId);
                return MemberSource.Of("failed");
            }

            if (mapping is null)
                return MemberSource.Of("no_sequence_points");
            if (!_hasMap)
                return MemberSource.Of("no_source_link");
            return _sourceLink!.ResolveDocumentUrl(mapping.FilePath) is { } url
                ? new MemberSource("complete", url, mapping.StartLine)
                : MemberSource.Of("unmapped_document");
        }

        void Fail(string documentationId)
        {
            if (_failures++ == 0)
                _firstFailure = documentationId;
        }

        static bool IsMappingFailure(Exception exception) =>
            exception is BadImageFormatException or InvalidOperationException;

        public void Dispose() => _sourceLink?.Dispose();
    }

    /// <summary>
    /// The Library name-family summary's families and name words for each
    /// Type (#name-families), computed from the Type inventory of the image
    /// the scan already holds, so they equal the summary's. A forwarder has
    /// name words only.
    /// </summary>
    sealed class NameFamilies
    {
        readonly Dictionary<int, TypeFamilies> _byToken;

        NameFamilies(Dictionary<int, TypeFamilies> byToken) => _byToken = byToken;

        internal abstract record Outcome
        {
            internal sealed record Read(NameFamilies Families) : Outcome;
            internal sealed record Incomplete(long Measured) : Outcome;
            internal sealed record Failed(string Detail) : Outcome;
        }

        internal TypeFamilies? For(MetadataSymbolType type) =>
            _byToken.GetValueOrDefault(type.MetadataToken);

        internal static Outcome Read(MetadataLibrarySymbolScan scan, Guid moduleVersionId)
        {
            AssemblyTypeDeclarationInventory inventory;
            switch (scan.ReadTypeDeclarations())
            {
                case AssemblyTypeDeclarationInventoryOutcome.Read read:
                    inventory = read.Inventory;
                    break;
                case AssemblyTypeDeclarationInventoryOutcome.Incomplete incomplete:
                    return new Outcome.Incomplete(incomplete.MeasuredRetainedTextCharacters);
                case AssemblyTypeDeclarationInventoryOutcome.Rejected rejected:
                    return new Outcome.Failed(
                        "The Type declaration inventory was rejected: "
                            + rejected.Failure.ToString());
                default:
                    throw new InvalidOperationException("Unknown Type declaration inventory outcome.");
            }

            LibraryNameFamilyAssignment assignment;
            switch (LibraryNameFamilySummary.Assign(inventory, moduleVersionId))
            {
                case LibraryNameFamilyAssignmentOutcome.Assigned assigned:
                    assignment = assigned.Assignment;
                    break;
                case LibraryNameFamilyAssignmentOutcome.Rejected rejected:
                    return new Outcome.Failed(rejected.Detail);
                default:
                    throw new InvalidOperationException("Unknown name-family assignment outcome.");
            }

            var byToken = new Dictionary<int, TypeFamilies>(
                assignment.Types.Length + assignment.Forwarders.Length);
            foreach (LibraryNameFamilyTypeRow row in assignment.Types)
            {
                byToken[row.Type.Definition.Value] = new TypeFamilies(
                    row.OneWordSuffix is { } one ? one.Words[0] : "",
                    row.TwoWordSuffix is { } two
                        ? string.Concat(two.Words[0], two.Separator, two.Words[1])
                        : "",
                    Words(row.Spans));
            }
            foreach (LibraryNameFamilyForwarderRow row in assignment.Forwarders)
            {
                int token = row.Declaration.ExportedTypeToken?.Value
                    ?? throw new InvalidOperationException(
                        "A forwarder declaration carries no ExportedType token.");
                byToken[token] = TypeFamilies.Empty with { NameWords = Words(row.Spans) };
            }
            return new Outcome.Read(new NameFamilies(byToken));
        }

        /// <summary>
        /// The name's word sequence: every span the grammar resolves, an
        /// unresolved run kept whole, separators dropped.
        /// </summary>
        static ImmutableArray<string> Words(ImmutableArray<IdentifierWordSpan> spans) =>
        [
            .. spans
                .Where(static span =>
                    span.Classification is not IdentifierWordSpanClassification.Separator)
                .Select(static span => span.Text),
        ];
    }

    static LibrarySymbolDocumentOutcome.Aborted Budget(
        MetadataLibrarySymbolBound bound,
        long limit,
        long measured) =>
        new(
            bound switch
            {
                MetadataLibrarySymbolBound.Types => LibrarySymbolDocumentAbort.TypeBudget,
                MetadataLibrarySymbolBound.TypeForwarders => LibrarySymbolDocumentAbort.TypeForwarderBudget,
                MetadataLibrarySymbolBound.Members => LibrarySymbolDocumentAbort.MemberBudget,
                MetadataLibrarySymbolBound.InterfaceClosure => LibrarySymbolDocumentAbort.InterfaceClosureBudget,
                _ => LibrarySymbolDocumentAbort.RetainedTextBudget,
            },
            $"Measured {measured.ToString(CultureInfo.InvariantCulture)}; the limit is {limit.ToString(CultureInfo.InvariantCulture)}.");

    static string Admission(MetadataLibrarySymbolAdmission admission) => admission switch
    {
        MetadataLibrarySymbolAdmission.PublicApi => "public_api",
        MetadataLibrarySymbolAdmission.All => "all",
        _ => throw new ArgumentOutOfRangeException(nameof(admission)),
    };

    static string SectionName(LibrarySymbolDocumentSection section) => section switch
    {
        LibrarySymbolDocumentSection.Declarations => "declarations",
        LibrarySymbolDocumentSection.NameFamilies => "name_families",
        LibrarySymbolDocumentSection.Documentation => "documentation",
        LibrarySymbolDocumentSection.SourceLink => "source_link",
        _ => throw new ArgumentOutOfRangeException(nameof(section)),
    };

    static string Status(LibrarySymbolSectionStatus status) => status switch
    {
        LibrarySymbolSectionStatus.Complete => "complete",
        LibrarySymbolSectionStatus.Partial => "partial",
        LibrarySymbolSectionStatus.Failed => "failed",
        LibrarySymbolSectionStatus.Offline => "offline",
        LibrarySymbolSectionStatus.NotRequested => "not_requested",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    /// <summary>
    /// The lookups (#lookups): dictionaries from a known key to sorted
    /// documentation identities, derived only from the records in the file,
    /// Type collisions included, each identity listed once.
    /// </summary>
    sealed class Lookups
    {
        readonly SortedDictionary<string, SortedSet<string>> _parameterTypeMembers = new(StringComparer.Ordinal);
        readonly SortedDictionary<string, SortedSet<string>> _returnTypeMembers = new(StringComparer.Ordinal);
        readonly SortedDictionary<string, SortedSet<string>> _producedTypeMembers = new(StringComparer.Ordinal);
        readonly SortedDictionary<string, SortedSet<string>> _attributeTypeMembers = new(StringComparer.Ordinal);
        readonly SortedDictionary<string, SortedSet<string>> _suffixFamilyTypes = new(StringComparer.Ordinal);
        readonly SortedDictionary<string, SortedSet<string>> _nameWordTypes = new(StringComparer.Ordinal);
        readonly SortedDictionary<string, SortedSet<string>> _baseTypeDerivedTypes = new(StringComparer.Ordinal);
        readonly SortedDictionary<string, SortedSet<string>> _interfaceImplementingTypes = new(StringComparer.Ordinal);

        internal void AddType(MetadataSymbolType type, TypeFamilies families)
        {
            if (families.SuffixFamily.Length > 0)
                Add(_suffixFamilyTypes, families.SuffixFamily, type.DocumentationId);
            foreach (string word in families.NameWords)
                Add(_nameWordTypes, word, type.DocumentationId);
            if (type.BaseType.Length > 0)
                Add(_baseTypeDerivedTypes, type.BaseType, type.DocumentationId);
            foreach (string implemented in type.Interfaces)
                Add(_interfaceImplementingTypes, implemented, type.DocumentationId);
        }

        internal void AddMember(MetadataSymbolMember member)
        {
            foreach (MetadataSymbolParameter parameter in member.Parameters)
            {
                if (parameter.Modifier == "out")
                    continue;
                foreach (string mention in parameter.TypeMentions)
                    Add(_parameterTypeMembers, mention, member.DocumentationId);
            }
            foreach (string mention in member.ReturnTypeMentions)
                Add(_returnTypeMembers, mention, member.DocumentationId);
            foreach (string produced in member.ProducedTypes)
                Add(_producedTypeMembers, produced, member.DocumentationId);
            foreach (string attribute in member.AttributeTypes)
                Add(_attributeTypeMembers, attribute, member.DocumentationId);
        }

        internal void Write(LibrarySymbolDocumentWriter writer)
        {
            Write(writer, "parameter_type_members", _parameterTypeMembers);
            Write(writer, "return_type_members", _returnTypeMembers);
            Write(writer, "produced_type_members", _producedTypeMembers);
            Write(writer, "attribute_type_members", _attributeTypeMembers);
            Write(writer, "suffix_family_types", _suffixFamilyTypes);
            Write(writer, "name_word_types", _nameWordTypes);
            Write(writer, "base_type_derived_types", _baseTypeDerivedTypes);
            Write(writer, "interface_implementing_types", _interfaceImplementingTypes);
        }

        static void Write(
            LibrarySymbolDocumentWriter writer,
            string name,
            SortedDictionary<string, SortedSet<string>> lookup)
        {
            writer.Raw($"{LibrarySymbolDocumentWriter.Quote(name)}: {{");
            int index = 0;
            foreach ((string key, SortedSet<string> values) in lookup)
                writer.LookupLine(key, values, last: ++index == lookup.Count);
            writer.Raw("},");
        }

        static void Add(
            SortedDictionary<string, SortedSet<string>> lookup,
            string key,
            string value)
        {
            if (!lookup.TryGetValue(key, out SortedSet<string>? values))
            {
                values = new SortedSet<string>(StringComparer.Ordinal);
                lookup[key] = values;
            }
            values.Add(value);
        }
    }
}
