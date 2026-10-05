#:project ../src/ILInspector.Metadata/ILInspector.Metadata.csproj
#:property IsPublishable=true
#:property PublishAot=true
#:property OptimizationPreference=Speed
#:property InvariantGlobalization=true

using System.Globalization;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

if (args.Length != 3)
{
    Console.Error.WriteLine(
        "Usage: measure-extension-population-single-walk "
            + "<count|rows-N> <zero|small|dense> <system-linq>");
    return 2;
}

Terminal terminal = Terminal.Parse(args[0]);
ReceiverScenario scenario = ReceiverScenario.Parse(args[1]);
string systemLinqPath = Path.GetFullPath(args[2]);

using AssemblyInspectionSession session =
    AssemblyInspectionSession.Open(systemLinqPath);
AssemblyReferenceIdentity receiverAssembly =
    session.AssemblyReferenceIdentities().Single(
        static candidate => candidate.Name == "System.Runtime");
MetadataTypeDefinitionNameResult receiverTypeResult =
    MetadataTypeDefinitionName.Create(
        scenario.Namespace,
        [scenario.TypeName]);
MetadataTypeDefinitionName receiverType =
    receiverTypeResult is MetadataTypeDefinitionNameResult.Valid valid
        ? valid.Name
        : throw new InvalidOperationException(
            $"Receiver Type {scenario.Namespace}.{scenario.TypeName} "
                + "is invalid.");
var receiver =
    new MetadataExtensionReceiverSelection(
        receiverAssembly,
        receiverType);

var available =
    session.ExtensionRelations(
        new(
            receiver,
            MetadataOperationPolicy.Unbounded,
            count: terminal.IsCount ? new() : null,
            rows: terminal.IsCount
                ? null
                : new(
                    startOrdinal: 0,
                    maximumRows: terminal.MaximumRows)));
MetadataExtensionRelationPopulationResult result =
    available is MetadataExtensionRelationPopulationOutcome.Available value
        ? value.Result
        : throw new InvalidOperationException(
            "Extension relation population was unavailable.");

ulong checksum = 14695981039346656037;
int cardinality;
if (terminal.IsCount)
{
    cardinality =
        result.Count
            is MetadataExtensionRelationPopulationCountOutcome.Counted counted
                ? counted.Value
                : throw new InvalidOperationException(
                    "Extension relation population did not produce Count.");
    checksum = AddInt32(checksum, cardinality);
}
else
{
    MetadataExtensionRelationPopulationRowsOutcome.Read rows =
        result.Rows
            as MetadataExtensionRelationPopulationRowsOutcome.Read
            ?? throw new InvalidOperationException(
                "Extension relation population did not produce Rows.");
    cardinality = rows.Items.Length;
    foreach (MetadataExtensionRelationPopulationRow row in rows.Items)
    {
        MetadataExtensionRelationEvidence first = row.Occurrences[0];
        checksum = Add(
            checksum,
            first.DeclaringTypeName.ToMetadataFullName());
        checksum = Add(checksum, first.Member.CanonicalSignature);
        checksum = AddInt32(checksum, row.Occurrences.Length);
        foreach (MetadataExtensionRelationEvidence occurrence
            in row.Occurrences)
        {
            checksum = AddInt32(
                checksum,
                occurrence.DeclarationMetadataToken);
        }
    }
}

#if CANDIDATE
const string lane = "candidate";
#else
const string lane = "baseline";
#endif

Console.WriteLine(
    string.Join(
        '\t',
        lane,
        scenario.Name,
        terminal.Name,
        cardinality,
        checksum.ToString("x16", CultureInfo.InvariantCulture)));
return 0;

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
