using System.Collections.Immutable;
using System.Globalization;

using DotnetInspector.Libraries;
using DotnetInspector.LibraryMetadata;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

/// <summary>
/// A section a Library symbol document can carry
/// (docs/design/library-symbol-document.md#request-and-admission).
/// Declarations are always present; the remaining sections arrive with
/// their slices.
/// </summary>
public enum LibrarySymbolDocumentSection
{
    Declarations,
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
    public LibrarySymbolDocumentRequest(
        LibraryReference library,
        MetadataLibrarySymbolAdmission admission,
        LibrarySymbolDocumentGenerator generator,
        LibrarySymbolDocumentSource source,
        ApiSurfaceExtractionBounds bounds)
    {
        Library = library ?? throw new ArgumentNullException(nameof(library));
        if (!Enum.IsDefined(admission))
            throw new ArgumentOutOfRangeException(nameof(admission));
        Admission = admission;
        Generator = generator ?? throw new ArgumentNullException(nameof(generator));
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Bounds = bounds ?? throw new ArgumentNullException(nameof(bounds));
    }

    public LibraryReference Library { get; }
    public MetadataLibrarySymbolAdmission Admission { get; }
    public ImmutableArray<LibrarySymbolDocumentSection> Sections { get; } =
        [LibrarySymbolDocumentSection.Declarations];
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
    LeaseReferenceMismatch,
    AssemblyIdentityMismatch,
    UnreadableLibrary,
    MetadataRowBudget,
    TypeBudget,
    MemberBudget,
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
                        request.Library,
                        request.Admission,
                        request.Bounds),
                    lease,
                    (context, token) => Write(request, context, destination, token),
                    cancellationToken);
            return outcome switch
            {
                LibrarySymbolInspectionOutcome<LibrarySymbolDocumentOutcome>.Completed completed =>
                    completed.Result,
                LibrarySymbolInspectionOutcome<LibrarySymbolDocumentOutcome>.Rejected rejected =>
                    new LibrarySymbolDocumentOutcome.Aborted(
                        rejected.Reason is LibrarySymbolInspectionRejection.LeaseReferenceMismatch
                            ? LibrarySymbolDocumentAbort.LeaseReferenceMismatch
                            : LibrarySymbolDocumentAbort.AssemblyIdentityMismatch,
                        ""),
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

        var lookups = new Lookups();
        foreach (MetadataSymbolType type in types)
            lookups.AddType(type);

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
        // order. Each Type's Members are read and written before the next
        // Type's, so only one Type's records are held at a time; a record is
        // written once its successor is known, which settles its comma.
        writer.Raw("\"members\": [");
        int memberCount = 0;
        (MetadataSymbolMember Member, MetadataSymbolType Type)? pending = null;
        foreach (MetadataSymbolType type in types)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImmutableArray<MetadataSymbolMember> members;
            switch (scan.ReadMembers(type, cancellationToken))
            {
                case MetadataLibrarySymbolMembersOutcome.Available available:
                    members = available.Members;
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
            foreach (MetadataSymbolMember member in members)
            {
                if (pending is var (previous, previousType))
                    writer.MemberLine(previous, previousType, Facts(previousType), last: false);
                pending = (member, type);
                lookups.AddMember(member);
                memberCount++;
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
            new LibrarySymbolSectionOutcome("name_families", LibrarySymbolSectionStatus.NotRequested, ""),
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

    static TypeFamilies Facts(MetadataSymbolType type) =>
        type.DeclarationKind is MetadataSymbolDeclarationKind.Forwarder
            ? TypeFamilies.Forwarder
            : TypeFamilies.NotRequested;

    static LibrarySymbolDocumentOutcome.Aborted Budget(
        MetadataLibrarySymbolBound bound,
        long limit,
        long measured) =>
        new(
            bound switch
            {
                MetadataLibrarySymbolBound.Types => LibrarySymbolDocumentAbort.TypeBudget,
                MetadataLibrarySymbolBound.Members => LibrarySymbolDocumentAbort.MemberBudget,
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

        internal void AddType(MetadataSymbolType type)
        {
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
