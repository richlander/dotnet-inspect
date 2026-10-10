#:project ../src/DotnetInspector.Queries/DotnetInspector.Queries.csproj
#:project ../src/DotnetInspector.Services/DotnetInspector.Services.csproj
#:project ../src/ILInspector.Metadata/ILInspector.Metadata.csproj
#:property IsPublishable=true
#:property PublishAot=true
#:property OptimizationPreference=Speed
#:property InvariantGlobalization=true

using System.Collections.Immutable;
using System.Globalization;

using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

if (args.Length != 4)
{
    Console.Error.WriteLine(
        "Usage: measure-workspace-extension-candidates "
            + "<count|rows-N> <zero|small|dense> <corelib> <system-linq>");
    return 2;
}

Terminal terminal = Terminal.Parse(args[0]);
ReceiverScenario scenario = ReceiverScenario.Parse(args[1]);
string coreLibraryPath = Path.GetFullPath(args[2]);
string systemLinqPath = Path.GetFullPath(args[3]);

MetadataExtensionReceiverSelection receiver =
    CreateReceiver(systemLinqPath, scenario);
await using var workspace = new InspectionWorkspace();
using AssemblyContextGroup group =
    CreateGroup(workspace, coreLibraryPath, systemLinqPath);

#if CANDIDATE
ImmutableArray<ParticipantMeasurement> measurements =
    MeasureCandidate(group, receiver, terminal);
const string lane = "candidate";
#else
ImmutableArray<ParticipantMeasurement> measurements =
    MeasureBaseline(group, receiver, terminal);
const string lane = "baseline";
#endif

ulong checksum = 14695981039346656037;
int cardinality = 0;
foreach (ParticipantMeasurement measurement in measurements)
{
    cardinality += measurement.Cardinality;
    checksum = Add(checksum, measurement.Subject);
    checksum = AddInt32(checksum, measurement.Cardinality);
    foreach (string identity in measurement.Identities)
        checksum = Add(checksum, identity);
}

Console.WriteLine(
    string.Join(
        '\t',
        lane,
        scenario.Name,
        terminal.Name,
        measurements.Length,
        cardinality,
        checksum.ToString("x16", CultureInfo.InvariantCulture),
        string.Join(
            ',',
            measurements.Select(static measurement =>
                $"{measurement.Subject}:{measurement.Cardinality}"))));
return 0;

#if CANDIDATE
static ImmutableArray<ParticipantMeasurement> MeasureCandidate(
    AssemblyContextGroup group,
    MetadataExtensionReceiverSelection receiver,
    Terminal terminal)
{
    AssemblyContextResult<MetadataExtensionRelationPopulationOutcome> result =
        AssemblyContextExtensionCandidatesQuery.Execute(
            group,
            new(
                receiver,
                MetadataOperationPolicy.Unbounded,
                count: terminal.IsCount ? new() : null,
                rows: terminal.IsCount
                    ? null
                    : new(terminal.MaximumRows)));
    var measurements =
        ImmutableArray.CreateBuilder<ParticipantMeasurement>(
            result.Assemblies.Length);
    foreach (AssemblyContextEntry<
        MetadataExtensionRelationPopulationOutcome> entry
        in result.Assemblies)
    {
        MetadataExtensionRelationPopulationOutcome outcome =
            entry is AssemblyContextEntry<
                MetadataExtensionRelationPopulationOutcome>.Available available
                    ? available.Value
                    : throw new InvalidOperationException(
                        $"Participant {entry.Subject.Identity.Name} "
                            + "was not available.");
        MetadataExtensionRelationPopulationResult population =
            outcome is MetadataExtensionRelationPopulationOutcome.Available
                populationAvailable
                    ? populationAvailable.Result
                    : throw new InvalidOperationException(
                        $"Participant {entry.Subject.Identity.Name} "
                            + "did not produce extension relations.");
        if (terminal.IsCount)
        {
            int count =
                population.Count
                    is MetadataExtensionRelationPopulationCountOutcome.Counted
                        counted
                            ? counted.Value
                            : throw new InvalidOperationException(
                                $"Participant {entry.Subject.Identity.Name} "
                                    + "did not produce exact Count.");
            measurements.Add(
                new(entry.Subject.Identity.Name, count, []));
            continue;
        }

        MetadataExtensionRelationPopulationRowsOutcome.Read rows =
            population.Rows
                as MetadataExtensionRelationPopulationRowsOutcome.Read
                ?? throw new InvalidOperationException(
                    $"Participant {entry.Subject.Identity.Name} "
                        + "did not produce Rows.");
        measurements.Add(
            new(
                entry.Subject.Identity.Name,
                rows.Items.Length,
                [
                    .. rows.Items.Select(row =>
                        LogicalIdentity(
                            entry.Subject.Identity.Name,
                            row.Occurrences[0]
                                .DeclaringTypeName
                                .ToMetadataFullName(),
                            row.Occurrences[0].Member)),
                ]));
    }
    return measurements.MoveToImmutable();
}
#else
static ImmutableArray<ParticipantMeasurement> MeasureBaseline(
    AssemblyContextGroup group,
    MetadataExtensionReceiverSelection receiver,
    Terminal terminal)
{
    AssemblyContextResult<ImmutableArray<ExtensionMethodInfo>> result =
        AssemblyContextExtensionMethodsQuery.Execute(group);
    var measurements =
        ImmutableArray.CreateBuilder<ParticipantMeasurement>(
            result.Assemblies.Length);
    foreach (AssemblyContextEntry<ImmutableArray<ExtensionMethodInfo>> entry
        in result.Assemblies)
    {
        ImmutableArray<ExtensionMethodInfo> methods =
            entry
                is AssemblyContextEntry<
                    ImmutableArray<ExtensionMethodInfo>>.Available available
                        ? available.Value
                        : throw new InvalidOperationException(
                            $"Participant {entry.Subject.Identity.Name} "
                                + "was not available.");
        List<string> identities = methods
            .Where(method => Matches(method, receiver))
            .Select(method =>
            {
                MetadataTypeDefinitionName declaringType =
                    method.GetDeclaringTypeDefinition()
                    ?? throw new InvalidOperationException(
                        "An extension declaration has no declaring Type.");
                MemberAnchor member = method.Anchor
                    ?? throw new InvalidOperationException(
                        "An extension declaration has no Member anchor.");
                return LogicalIdentity(
                    entry.Subject.Identity.Name,
                    declaringType.ToMetadataFullName(),
                    member);
            })
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (!terminal.IsCount)
            identities = identities.Take(terminal.MaximumRows).ToList();
        measurements.Add(
            new(
                entry.Subject.Identity.Name,
                identities.Count,
                terminal.IsCount ? [] : [.. identities]));
    }
    return measurements.MoveToImmutable();
}

static bool Matches(
    ExtensionMethodInfo method,
    MetadataExtensionReceiverSelection receiver)
{
    MetadataNamedTypeReference? extendedType =
        method.GetExtendedTypeReference();
    return extendedType?.Scope
            is MetadataTypeReferenceScope.AssemblyReference assembly
        && assembly.Assembly.IsEquivalentTo(receiver.Assembly)
        && extendedType.Type == receiver.Type;
}
#endif

static string LogicalIdentity(
    string assembly,
    string declaringType,
    MemberAnchor member) =>
    $"{assembly}|{declaringType}|{member.CanonicalSignature}";

static MetadataExtensionReceiverSelection CreateReceiver(
    string systemLinqPath,
    ReceiverScenario scenario)
{
    using AssemblyInspectionSession session =
        AssemblyInspectionSession.Open(systemLinqPath);
    AssemblyReferenceIdentity assembly =
        session.AssemblyReferenceIdentities().Single(
            static candidate => candidate.Name == "System.Runtime");
    MetadataTypeDefinitionNameResult typeResult =
        MetadataTypeDefinitionName.Create(
            scenario.Namespace,
            [scenario.TypeName]);
    MetadataTypeDefinitionName type =
        typeResult is MetadataTypeDefinitionNameResult.Valid valid
            ? valid.Name
            : throw new InvalidOperationException(
                $"Receiver Type {scenario.Namespace}.{scenario.TypeName} "
                    + "is invalid.");
    return new(assembly, type);
}

static AssemblyContextGroup CreateGroup(
    InspectionWorkspace workspace,
    params string[] paths)
{
    var policy = new NoSelectionBindingPolicy();
    return workspace.CreateAssemblyContextGroup(
        [
            .. paths.Select(path =>
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.CreateFromPath(
                        path,
                        AssemblyResolutionProvenance.Local(
                            "extension candidate measurement")),
                    policy)),
        ]);
}

static ulong Add(ulong hash, string value)
{
    const ulong prime = 1099511628211;
    foreach (char character in value)
        hash = (hash ^ character) * prime;
    return (hash ^ 0xff) * prime;
}

static ulong AddInt32(ulong hash, int value)
{
    const ulong prime = 1099511628211;
    return (hash ^ (uint)value) * prime;
}

sealed record ParticipantMeasurement(
    string Subject,
    int Cardinality,
    ImmutableArray<string> Identities);

sealed record Terminal(string Name, bool IsCount, int MaximumRows)
{
    public static Terminal Parse(string value)
    {
        if (value == "count")
            return new(value, IsCount: true, MaximumRows: 0);
        const string prefix = "rows-";
        if (value.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(
                value.AsSpan(prefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int maximumRows)
            && maximumRows > 0)
        {
            return new(value, IsCount: false, maximumRows);
        }
        throw new ArgumentException(
            "Terminal must be count or rows-N.",
            nameof(value));
    }
}

sealed record ReceiverScenario(
    string Name,
    string Namespace,
    string TypeName)
{
    public static ReceiverScenario Parse(string value) =>
        value switch
        {
            "zero" => new(value, "System", "IDisposable"),
            "small" => new(
                value,
                "System.Collections",
                "IEnumerable"),
            "dense" => new(
                value,
                "System.Collections.Generic",
                "IEnumerable`1"),
            _ => throw new ArgumentException(
                "Scenario must be zero, small, or dense.",
                nameof(value)),
        };
}

sealed class NoSelectionBindingPolicy : IAssemblyBindingPolicy
{
    public AssemblyBindingPolicyVersion Version { get; } = new();

    public AssemblyBindingSelectionSnapshot Select(
        AssemblyBindingRequest request) =>
        new(
            Version,
            AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.CandidateUnavailable)));
}
