#:project ../src/ILInspector.Metadata/ILInspector.Metadata.csproj
#:property IsPublishable=true
#:property PublishAot=true
#:property OptimizationPreference=Speed
#:property InvariantGlobalization=true
#:property EnablePreviewFeatures=true
#:property NoWarn=CA2252

// Measures one exact Metadata declaration session without command or document
// costs. "probe" separates first, distinct, and cached posts; "all" publishes
// every MethodDef in the selected image and reports per-post percentiles.
//
// Usage:
//   dotnet run eng/measure-method-declaration-allocation.cs -c Release -- \
//     <assembly> <probe|all>

using System.Diagnostics;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

if (args.Length != 2
    || args[1] is not ("probe" or "all"))
{
    Console.Error.WriteLine(
        "Usage: measure-method-declaration-allocation "
        + "<assembly> <probe|all>");
    return 2;
}

string assemblyPath = Path.GetFullPath(args[0]);
if (!File.Exists(assemblyPath))
{
    Console.Error.WriteLine(
        $"Assembly not found: {assemblyPath}");
    return 2;
}

byte[] image = File.ReadAllBytes(assemblyPath);
using var peReader =
    new PEReader(new MemoryStream(image, writable: false));
MetadataReader reader = peReader.GetMetadataReader();
Request[] requests = CreateRequests(reader);
int customAttributes =
    reader.GetTableRowCount(TableIndex.CustomAttribute);
int genericParameters =
    reader.GetTableRowCount(TableIndex.GenericParam);
int genericConstraints =
    reader.GetTableRowCount(TableIndex.GenericParamConstraint);
long ownerOrderingEdges =
    customAttributes + genericParameters + genericConstraints;

Console.WriteLine(
    "record\tsha256\timage_bytes\tmethod_defs\tcustom_attributes"
    + "\tgeneric_parameters\tgeneric_constraints\towner_order_rows");
Console.WriteLine(
    string.Join(
        '\t',
        "identity",
        Convert.ToHexStringLower(SHA256.HashData(image)),
        image.Length.ToString(CultureInfo.InvariantCulture),
        requests.Length.ToString(CultureInfo.InvariantCulture),
        customAttributes.ToString(CultureInfo.InvariantCulture),
        genericParameters.ToString(CultureInfo.InvariantCulture),
        genericConstraints.ToString(CultureInfo.InvariantCulture),
        ownerOrderingEdges.ToString(CultureInfo.InvariantCulture)));
Console.WriteLine(
    "scenario\trequests\tposted\ttotal_allocated_b"
    + "\tmedian_allocated_b\tp95_allocated_b\telapsed_ms"
    + "\trelationship_edges\texpected_owner_order_edges");

if (args[1] == "probe")
{
    RunProbe(assemblyPath, requests, ownerOrderingEdges);
}
else
{
    RunAll(assemblyPath, requests, ownerOrderingEdges);
}

return 0;

static Request[] CreateRequests(MetadataReader reader)
{
    var requests =
        new Request[reader.GetTableRowCount(TableIndex.MethodDef)];
    int index = 0;
    foreach (MethodDefinitionHandle methodHandle
        in reader.MethodDefinitions)
    {
        MethodDefinition definition =
            reader.GetMethodDefinition(methodHandle);
        TypeDefinitionHandle typeHandle =
            definition.GetDeclaringType();
        MetadataMethodAddress method =
            MetadataMethodAddress.Create(reader, methodHandle);
        requests[index++] =
            new(
                MetadataTypeDefinitionAddress.FromToken(
                    method.ModuleVersionId,
                    MetadataTokens.GetToken(typeHandle)),
                method);
    }

    return requests;
}

static void RunProbe(
    string assemblyPath,
    Request[] requests,
    long ownerOrderingEdges)
{
    using var assembly =
        AssemblyInspectionSession.Open(assemblyPath);
    using var operation =
        new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
    using var session =
        assembly.CreateDeclarationSession(operation);

    MeasureOne(
        "first-distinct",
        requests[0],
        session,
        operation,
        ownerOrderingEdges);
    MeasureOne(
        "second-distinct",
        requests[1],
        session,
        operation,
        ownerOrderingEdges);
    MeasureOne(
        "second-cached",
        requests[1],
        session,
        operation,
        ownerOrderingEdges);
}

static void MeasureOne(
    string scenario,
    Request request,
    MetadataDeclarationSession session,
    MetadataOperationContext operation,
    long ownerOrderingEdges)
{
    MetadataOperationCounters beforeCounters =
        operation.Counters;
    long before = GC.GetTotalAllocatedBytes(precise: true);
    long started = Stopwatch.GetTimestamp();

    MetadataMethodDeclarationResult result =
        session.PostMethodDeclaration(
            request.Type,
            request.Method,
            CancellationToken.None);

    TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
    long allocated =
        GC.GetTotalAllocatedBytes(precise: true) - before;
    MetadataOperationCounters afterCounters =
        operation.Counters;

    WriteResult(
        scenario,
        1,
        allocated,
        allocated,
        elapsed,
        afterCounters.RelationshipEdges
            - beforeCounters.RelationshipEdges,
        ownerOrderingEdges,
        result is MetadataMethodDeclarationResult.Posted ? 1 : 0);
}

static void RunAll(
    string assemblyPath,
    Request[] requests,
    long ownerOrderingEdges)
{
    using var assembly =
        AssemblyInspectionSession.Open(assemblyPath);
    using var operation =
        new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
    using var session =
        assembly.CreateDeclarationSession(operation);

    var perPost = new long[requests.Length];
    int posted = 0;
    long before = GC.GetTotalAllocatedBytes(precise: true);
    long started = Stopwatch.GetTimestamp();
    long previous = before;

    for (int index = 0; index < requests.Length; index++)
    {
        Request request = requests[index];
        MetadataMethodDeclarationResult result =
            session.PostMethodDeclaration(
                request.Type,
                request.Method,
                CancellationToken.None);
        long current =
            GC.GetTotalAllocatedBytes(precise: true);
        perPost[index] = current - previous;
        previous = current;
        if (result is MetadataMethodDeclarationResult.Posted)
            posted++;
    }

    TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
    long allocated =
        GC.GetTotalAllocatedBytes(precise: true) - before;
    Array.Sort(perPost);
    MetadataOperationCounters counters = operation.Counters;

    WriteResult(
        "all-distinct",
        requests.Length,
        allocated,
        Percentile(perPost, 0.50),
        elapsed,
        counters.RelationshipEdges,
        checked(ownerOrderingEdges * requests.Length),
        posted,
        Percentile(perPost, 0.95));
}

static void WriteResult(
    string scenario,
    int requests,
    long allocated,
    long medianAllocated,
    TimeSpan elapsed,
    long relationshipEdges,
    long expectedOwnerOrderingEdges,
    int posted,
    long? p95Allocated = null)
{
    Console.WriteLine(
        string.Join(
            '\t',
            scenario,
            requests.ToString(CultureInfo.InvariantCulture),
            posted.ToString(CultureInfo.InvariantCulture),
            allocated.ToString(CultureInfo.InvariantCulture),
            medianAllocated.ToString(CultureInfo.InvariantCulture),
            (p95Allocated ?? medianAllocated)
                .ToString(CultureInfo.InvariantCulture),
            elapsed.TotalMilliseconds
                .ToString("F3", CultureInfo.InvariantCulture),
            relationshipEdges.ToString(
                CultureInfo.InvariantCulture),
            expectedOwnerOrderingEdges.ToString(
                CultureInfo.InvariantCulture)));
}

static long Percentile(
    IReadOnlyList<long> sorted,
    double percentile)
{
    int index = Math.Min(
        sorted.Count - 1,
        (int)Math.Ceiling(sorted.Count * percentile) - 1);
    return sorted[index];
}

readonly record struct Request(
    MetadataTypeDefinitionAddress Type,
    MetadataMethodAddress Method);
