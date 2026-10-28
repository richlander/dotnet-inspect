using System.Reflection.Metadata;

using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.Research;
using Inspector.Findings;

namespace DotnetInspector.Queries;

/// <summary>
/// One Library assembly an endpoint compares: an extracted file, or an
/// implementation participant read in the group that holds it.
/// </summary>
public abstract record UnsafeMemberComparisonAssembly
{
    private UnsafeMemberComparisonAssembly(string assemblyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        AssemblyName = assemblyName;
    }

    public string AssemblyName { get; }

    public static UnsafeMemberComparisonAssembly FromPath(
        string assemblyName,
        string path)
        => new File(assemblyName, path);

    public static UnsafeMemberComparisonAssembly FromParticipant(
        string assemblyName,
        AssemblyContextGroup group,
        AssemblyContextParticipant participant)
        => new GroupParticipant(assemblyName, group, participant);

    internal sealed record File : UnsafeMemberComparisonAssembly
    {
        public File(string assemblyName, string path)
            : base(assemblyName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            Path = path;
        }

        public string Path { get; }
    }

    internal sealed record GroupParticipant : UnsafeMemberComparisonAssembly
    {
        public GroupParticipant(
            string assemblyName,
            AssemblyContextGroup group,
            AssemblyContextParticipant participant)
            : base(assemblyName)
        {
            ArgumentNullException.ThrowIfNull(group);
            ArgumentNullException.ThrowIfNull(participant);
            Group = group;
            Participant = participant;
        }

        public AssemblyContextGroup Group { get; }

        public AssemblyContextParticipant Participant { get; }
    }
}

/// <summary>One Type a Type-surface comparison selects, by name and exact definition name.</summary>
public sealed record UnsafeMemberComparisonType(
    string TypeName,
    MetadataTypeDefinitionName DefinitionName);

/// <summary>
/// Compares the unsafe member census across two endpoints at Library or Type
/// scope, as docs/design/unsafe-members-section.md#diff defines. Each side
/// runs the census over its own image; the comparison is
/// <see cref="AnalysisFindings.CompareUnsafeMembers"/>.
/// </summary>
public static class UnsafeMemberComparisonQuery
{
    /// <summary>Compares the census of one Library assembly at each endpoint as one subject.</summary>
    public static RetainedFindingComparisonSet ExecuteLibrary(
        UnsafeMemberComparisonAssembly before,
        UnsafeMemberComparisonAssembly after,
        string subjectId,
        string subjectDisplay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var subject = LibrarySubject(subjectId, subjectDisplay);
        var findingSubject = new FindingSubject(subject.Id, subject.Display);
        return new(
            [
                Comparison(
                    subject,
                    InspectLibrary(before, "before", findingSubject, cancellationToken),
                    InspectLibrary(after, "after", findingSubject, cancellationToken)),
            ]);
    }

    /// <summary>
    /// Compares each Library assembly the endpoints share by case-insensitive
    /// file name; an assembly on only one side compares against no members.
    /// </summary>
    public static RetainedFindingComparisonSet ExecuteLibrarySets(
        IReadOnlyList<UnsafeMemberComparisonAssembly> before,
        IReadOnlyList<UnsafeMemberComparisonAssembly> after,
        string subjectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        Dictionary<string, UnsafeMemberComparisonAssembly> beforeByName =
            Index(before, nameof(before));
        Dictionary<string, UnsafeMemberComparisonAssembly> afterByName =
            Index(after, nameof(after));
        if (beforeByName.Count == 0 || afterByName.Count == 0)
        {
            throw new ArgumentException(
                "Each endpoint must supply at least one Library assembly.");
        }

        var comparisons = new List<RetainedFindingComparison>();
        foreach (string name in beforeByName.Keys.Concat(afterByName.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static name => name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var subject = LibrarySubject($"{subjectId}/{name}", name);
            var findingSubject = new FindingSubject(subject.Id, subject.Display);
            comparisons.Add(Comparison(
                subject,
                beforeByName.TryGetValue(name, out var beforeAssembly)
                    ? InspectLibrary(beforeAssembly, "before", findingSubject, cancellationToken)
                    : NoMembers(),
                afterByName.TryGetValue(name, out var afterAssembly)
                    ? InspectLibrary(afterAssembly, "after", findingSubject, cancellationToken)
                    : NoMembers()));
        }
        return new RetainedFindingComparisonSet(comparisons);
    }

    /// <summary>
    /// Compares the census of each selected Type: in each endpoint, the
    /// assembly that defines it, or no members where no assembly does.
    /// </summary>
    public static RetainedFindingComparisonSet ExecuteTypes(
        IReadOnlyList<UnsafeMemberComparisonAssembly> before,
        IReadOnlyList<UnsafeMemberComparisonAssembly> after,
        IReadOnlyList<UnsafeMemberComparisonType> types,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(types);
        var comparisons = new List<RetainedFindingComparison>(types.Count);
        foreach (UnsafeMemberComparisonType type in types)
        {
            ArgumentNullException.ThrowIfNull(type);
            cancellationToken.ThrowIfCancellationRequested();
            var subject = new ResearchSubjectKey(
                ResearchSubjectKind.Type,
                type.TypeName,
                type.TypeName,
                type.TypeName);
            var findingSubject = new FindingSubject(subject.Id, subject.Display);
            comparisons.Add(Comparison(
                subject,
                InspectType(before, type, "before", findingSubject, cancellationToken),
                InspectType(after, type, "after", findingSubject, cancellationToken)));
        }
        return new RetainedFindingComparisonSet(comparisons);
    }

    static UnsafeMemberFindingInspection InspectLibrary(
        UnsafeMemberComparisonAssembly assembly,
        string side,
        FindingSubject subject,
        CancellationToken cancellationToken) =>
        Inspect(
            assembly,
            side,
            subject,
            session => Project(
                UnsafeMemberCensusQuery.Execute(session, assembly.AssemblyName),
                subject),
            cancellationToken)!;

    static UnsafeMemberFindingInspection InspectType(
        IReadOnlyList<UnsafeMemberComparisonAssembly> assemblies,
        UnsafeMemberComparisonType type,
        string side,
        FindingSubject subject,
        CancellationToken cancellationToken)
    {
        foreach (UnsafeMemberComparisonAssembly assembly in assemblies)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            UnsafeMemberFindingInspection? inspection = Inspect(
                assembly,
                side,
                subject,
                session => FindType(session, type.DefinitionName) is { IsNil: false } handle
                    ? Project(
                        UnsafeMemberCensusQuery.ExecuteExactTypes(
                            session,
                            assembly.AssemblyName,
                            handle),
                        subject)
                    : null,
                cancellationToken);
            if (inspection is not null)
                return inspection;
        }
        return NoMembers();
    }

    // Opens one endpoint assembly for a callback; an unreadable or rejected
    // image is a failed inspection, never an empty one.
    static UnsafeMemberFindingInspection? Inspect(
        UnsafeMemberComparisonAssembly assembly,
        string side,
        FindingSubject subject,
        Func<AssemblyInspectionSession, UnsafeMemberFindingInspection?> inspect,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (assembly is UnsafeMemberComparisonAssembly.File { Path: var path })
        {
            try
            {
                using AssemblyInspectionSession session =
                    AssemblyInspectionSession.Open(path);
                return inspect(session);
            }
            catch (BadImageFormatException exception)
            {
                return Failed(
                    subject,
                    $"Unsafe member {side} endpoint image was rejected: {exception.Message}");
            }
            catch (IOException exception)
            {
                return Failed(
                    subject,
                    $"Unsafe member {side} endpoint image could not be read: {exception.Message}");
            }
            catch (UnauthorizedAccessException exception)
            {
                return Failed(
                    subject,
                    $"Unsafe member {side} endpoint image could not be read: {exception.Message}");
            }
        }

        var held = (UnsafeMemberComparisonAssembly.GroupParticipant)assembly;
        AssemblyImageAccessResult<UnsafeMemberFindingInspection?> access =
            held.Group.UseSnapshot(
                held.Participant,
                cancellationToken,
                snapshot =>
                {
                    try
                    {
                        using AssemblyInspectionSession session =
                            AssemblyInspectionSession.Open(snapshot);
                        return inspect(session);
                    }
                    catch (BadImageFormatException exception)
                    {
                        return Failed(
                            subject,
                            $"Unsafe member {side} endpoint image was rejected: {exception.Message}");
                    }
                });
        return access switch
        {
            AssemblyImageAccessResult<UnsafeMemberFindingInspection?>.Available available =>
                available.Value,
            AssemblyImageAccessResult<UnsafeMemberFindingInspection?>.Rejected rejected =>
                Failed(
                    subject,
                    $"Unsafe member {side} endpoint acquisition was rejected: {rejected.Failure}."),
            _ => throw new InvalidOperationException(
                "Unknown assembly image access result."),
        };
    }

    static UnsafeMemberFindingInspection Project(
        UnsafeMemberCensusQueryResult result,
        FindingSubject subject) =>
        result switch
        {
            UnsafeMemberCensusQueryResult.Inspected inspected =>
                AnalysisFindings.InspectUnsafeMembers(inspected.Census, subject),
            UnsafeMemberCensusQueryResult.NoMetadata => NoMembers(),
            UnsafeMemberCensusQueryResult.Failed failed =>
                Failed(subject, failed.Error.Message),
            _ => throw new InvalidOperationException(
                $"Unknown unsafe member census result '{result.GetType().Name}'."),
        };

    // The one Type definition the exact name selects; nil when the image
    // defines none, or more than one.
    static TypeDefinitionHandle FindType(
        AssemblyInspectionSession session,
        MetadataTypeDefinitionName name) =>
        session.InspectImage(image =>
        {
            MetadataReader reader = MetadataFormatAdmission.GetMetadataReader(image);
            TypeDefinitionHandle match = default;
            foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
            {
                if (MetadataTypeDefinitionName.Matches(reader, handle, name, out _)
                    != MetadataTypeDefinitionNameMatchResult.Match)
                {
                    continue;
                }
                if (!match.IsNil)
                    return default;
                match = handle;
            }
            return match;
        });

    static RetainedFindingComparison Comparison(
        ResearchSubjectKey subject,
        UnsafeMemberFindingInspection before,
        UnsafeMemberFindingInspection after) =>
        new RetainedFindingComparison<UnsafeMemberFinding>(
            subject,
            AnalysisFindings.UnsafeMemberDescriptor,
            AnalysisFindings.CompareUnsafeMembers(before, after));

    static UnsafeMemberFindingInspection NoMembers() =>
        new UnsafeMemberFindingInspection.Complete(
            new FindingInspection<UnsafeMemberFinding>.Complete([]));

    static UnsafeMemberFindingInspection Failed(
        FindingSubject subject,
        string reason) =>
        new UnsafeMemberFindingInspection.Failed(
            new InspectionError(
                subject,
                AnalysisFindings.UnsafeMemberDescriptor,
                reason));

    static Dictionary<string, UnsafeMemberComparisonAssembly> Index(
        IReadOnlyList<UnsafeMemberComparisonAssembly> assemblies,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(assemblies, parameterName);
        var indexed = new Dictionary<string, UnsafeMemberComparisonAssembly>(
            StringComparer.OrdinalIgnoreCase);
        foreach (UnsafeMemberComparisonAssembly assembly in assemblies)
        {
            ArgumentNullException.ThrowIfNull(assembly, parameterName);
            if (!indexed.TryAdd(assembly.AssemblyName, assembly))
            {
                throw new ArgumentException(
                    $"Duplicate Library assembly '{assembly.AssemblyName}'.",
                    parameterName);
            }
        }
        return indexed;
    }

    static ResearchSubjectKey LibrarySubject(
        string subjectId,
        string subjectDisplay)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectDisplay);
        return new(
            ResearchSubjectKind.Library,
            subjectId,
            subjectDisplay);
    }
}
