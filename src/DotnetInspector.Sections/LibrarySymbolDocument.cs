using System.Collections.Immutable;
using System.Globalization;

using DotnetInspector.Libraries;
using CSharpText;
using DotnetInspector.LibraryMetadata;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Sections;

/// <summary>
/// A section a Library symbol document can carry
/// (docs/design/library-symbol-document.md#request-and-admission).
/// Declarations are always present; documentation and source sections
/// arrive with their slices.
/// </summary>
public enum LibrarySymbolDocumentSection
{
    Declarations,
    NameFamilies,
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
    /// The sections to produce. <c>null</c> requests the default, every
    /// section this producer offers; <see cref="LibrarySymbolDocumentSection.Declarations"/>
    /// is always included.
    /// </param>
    public LibrarySymbolDocumentRequest(
        MetadataLibrarySymbolAdmission admission,
        LibrarySymbolDocumentGenerator generator,
        LibrarySymbolDocumentSource source,
        ApiSurfaceExtractionBounds bounds,
        IEnumerable<LibrarySymbolDocumentSection>? sections = null)
    {
        if (!Enum.IsDefined(admission))
            throw new ArgumentOutOfRangeException(nameof(admission));
        Admission = admission;
        Generator = generator ?? throw new ArgumentNullException(nameof(generator));
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Bounds = bounds ?? throw new ArgumentNullException(nameof(bounds));
        LibrarySymbolDocumentSection[] requested =
            sections?.ToArray() ?? Enum.GetValues<LibrarySymbolDocumentSection>();
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
    }

    public MetadataLibrarySymbolAdmission Admission { get; }
    public ImmutableArray<LibrarySymbolDocumentSection> Sections { get; }
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
            LibrarySymbolInspectionOutcome<LibrarySymbolDocumentOutcome> outcome =
                LibrarySymbolInspection.Execute(
                    new LibrarySymbolInspectionRequest(
                        request.Admission,
                        request.Bounds),
                    lease,
                    (context, token) => Write(request, context, destination, token),
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
            families?.For(type)
            ?? (type.DeclarationKind is MetadataSymbolDeclarationKind.Forwarder
                ? TypeFamilies.Forwarder
                : TypeFamilies.NotRequested);

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
            writer.TypeLine(entries[index], Facts(entries[index]), last: index == entries.Count - 1);
        writer.Raw("},");
        writer.Raw("\"type_collisions\": [");
        for (int index = 0; index < collisions.Count; index++)
            writer.CollisionLine(collisions[index], Facts(collisions[index]), last: index == collisions.Count - 1);
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
                            writer.MemberLine(previous, previousType, Facts(previousType), last: false);
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
            writer.MemberLine(lastMember, lastType, Facts(lastType), last: true);
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
            new LibrarySymbolSectionOutcome("documentation", LibrarySymbolSectionStatus.NotRequested, ""),
            new LibrarySymbolSectionOutcome("source_link", LibrarySymbolSectionStatus.NotRequested, ""),
            new LibrarySymbolSectionOutcome("findings", LibrarySymbolSectionStatus.NotRequested, ""));
        bool complete = sections.All(
            section => section.Status
                is LibrarySymbolSectionStatus.Complete
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
                    Words(row.Spans),
                    TypeFamilies.NotRequested.DocumentationStatus,
                    TypeFamilies.NotRequested.SourceStatus);
            }
            foreach (LibraryNameFamilyForwarderRow row in assignment.Forwarders)
            {
                int token = row.Declaration.ExportedTypeToken?.Value
                    ?? throw new InvalidOperationException(
                        "A forwarder declaration carries no ExportedType token.");
                byToken[token] = TypeFamilies.Forwarder with { NameWords = Words(row.Spans) };
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
