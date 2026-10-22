#:project ../src/ILInspector.ILDiff/ILInspector.ILDiff.csproj

// Measures the exhaustive IL diff paths over one Library image pair under the
// Body policy: the whole-assembly canonical IL comparison, the per-method
// census of each image through IlFindings.Inspect, the per-pair census
// comparison through IlFindings.Compare, and the Member Body Diff endpoint
// path through IlAssemblyDiff.CompareMemberEndpoints. Methods are paired by
// their declaring Type, name, and decoded signature, so a renumbered token in
// a signature does not unpair them. Reports the median and p95 wall time and
// the median bytes allocated per run, and every sample so runs can be pooled,
// so a change to the census producer
// (docs/design/il-diff-canonicalization.md#census-cursor) is compared base
// against head on the same pair.
// Usage: measure-il-diff-exhaustive <before.dll> <after.dll> [iterations]

using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.ILDiff;
using Inspector.Findings;

if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("Usage: measure-il-diff-exhaustive <before.dll> <after.dll> [iterations]");
    return 2;
}

string beforePath = args[0];
string afterPath = args[1];
int iterations = args.Length > 2 ? int.Parse(args[2]) : 10;
byte[] beforeBytes = File.ReadAllBytes(beforePath);
byte[] afterBytes = File.ReadAllBytes(afterPath);
using var before = new PEReader(new MemoryStream(beforeBytes, writable: false));
using var after = new PEReader(new MemoryStream(afterBytes, writable: false));
MetadataReader beforeReader = before.GetMetadataReader();
MetadataReader afterReader = after.GetMetadataReader();

// Subjects and pairing are inputs, prepared once outside every timed region.
Method[] beforeMethods = Methods(beforeReader);
Method[] afterMethods = Methods(afterReader);
var afterByKey = new Dictionary<string, Method>(StringComparer.Ordinal);
foreach (Method method in afterMethods)
    afterByKey.TryAdd(method.Key, method);
var pairs = new List<(Method Before, Method After)>();
foreach (Method method in beforeMethods)
{
    if (afterByKey.TryGetValue(method.Key, out Method? other))
        pairs.Add((method, other));
}

Console.WriteLine($"pair\t{Path.GetFileName(beforePath)}\tmethods={beforeMethods.Length}/{afterMethods.Length}\tpaired={pairs.Count}\titerations={iterations}");

// 1. Whole-assembly canonical IL comparison (IlBodyDiff.Compare per paired body).
IlAssemblyDiffResult assembly = Measure("assembly", () => IlAssemblyDiff.CompareStreams(
    new MemoryStream(beforeBytes, writable: false), "before",
    new MemoryStream(afterBytes, writable: false), "after",
    maxExamples: 0,
    normalization: IlBodyDiffPolicy.Body).Diff);
Console.WriteLine(
    $"  compared={assembly.ComparedBodyCount}\texact={assembly.PairExactCount}\tchanged={assembly.ChangedBodyCount}"
        + $"\tunavailable={assembly.PairUnavailableCount}\tfailures={assembly.FailureCount}");

// 2. The per-method census of each image (the cursor over raw IL at head).
foreach ((string side, PEReader pe, MetadataReader reader, Method[] methods) in new[]
{
    ("inspect-before", before, beforeReader, beforeMethods),
    ("inspect-after", after, afterReader, afterMethods),
})
{
    int[] counts = Measure(side, () =>
    {
        int complete = 0;
        int absent = 0;
        int failed = 0;
        foreach (Method method in methods)
        {
            switch (IlFindings.Inspect(pe, reader, method.Handle, method.Subject, IlBodyDiffPolicy.Body).Value)
            {
                case FindingInspection<CanonicalIlOperation>.Complete:
                    complete++;
                    break;
                case FindingInspection<CanonicalIlOperation>.Absent:
                    absent++;
                    break;
                default:
                    failed++;
                    break;
            }
        }
        return new[] { complete, absent, failed };
    });
    Console.WriteLine($"  complete={counts[0]}\tabsent={counts[1]}\tfailed={counts[2]}");
}

// 3. The per-pair census comparison: two censuses, the ordered matcher, target validation.
int[] compared = Measure("compare", () =>
{
    int exact = 0;
    int changed = 0;
    int failed = 0;
    foreach ((Method x, Method y) in pairs)
    {
        FindingComparison<CanonicalIlOperation> comparison = IlFindings.Compare(
            before, beforeReader, x.Handle, after, afterReader, y.Handle, x.Subject, normalization: IlBodyDiffPolicy.Body);
        if (comparison is FindingComparison<CanonicalIlOperation>.Failed)
            failed++;
        else if (comparison.IsExact)
            exact++;
        else
            changed++;
    }
    return new[] { exact, changed, failed };
});
Console.WriteLine($"  exact={compared[0]}\tchanged={compared[1]}\tfailed={compared[2]}");

// 4. The Member Body Diff endpoint path the hosts call.
int[] endpoints = Measure("endpoints", () =>
{
    int exact = 0;
    int changed = 0;
    int withoutDiff = 0;
    foreach ((Method x, Method y) in pairs)
    {
        IlMemberEndpointComparison view = IlAssemblyDiff.CompareMemberEndpoints(
            new IlMemberDiffEndpoint.Present(x.MemberSubject, before, beforeReader, x.Handle),
            new IlMemberDiffEndpoint.Present(y.MemberSubject, after, afterReader, y.Handle),
            IlBodyDiffPolicy.Body);
        if (view.MemberDiff is null)
            withoutDiff++;
        else if (view.MemberDiff.Diff.IsExact)
            exact++;
        else
            changed++;
    }
    return new[] { exact, changed, withoutDiff };
});
Console.WriteLine($"  exact={endpoints[0]}\tchanged={endpoints[1]}\twithoutDiff={endpoints[2]}");
return 0;

T Measure<T>(string name, Func<T> run)
{
    T result = run();
    run();
    var times = new List<double>(iterations);
    var allocations = new List<long>(iterations);
    for (int i = 0; i < iterations; i++)
    {
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        run();
        times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        allocations.Add(GC.GetAllocatedBytesForCurrentThread() - allocated);
    }
    times.Sort();
    allocations.Sort();
    Console.WriteLine(
        $"{name}\tmedian={times[times.Count / 2]:F2}ms\tp95={times[(int)(times.Count * 0.95)]:F2}ms"
            + $"\tmin={times[0]:F2}ms\tallocMedian={allocations[allocations.Count / 2] / 1_048_576.0:F1}MB");
    Console.WriteLine($"  samples-ms\t{name}\t{string.Join(",", times.Select(t => t.ToString("F2")))}");
    Console.WriteLine($"  samples-bytes\t{name}\t{string.Join(",", allocations)}");
    return result;
}

static Method[] Methods(MetadataReader reader)
{
    var methods = new List<Method>();
    foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
    {
        string typeName = TypeName(reader, typeHandle);
        TypeDefinition type = reader.GetTypeDefinition(typeHandle);
        foreach (MethodDefinitionHandle handle in type.GetMethods())
        {
            MethodDefinition method = reader.GetMethodDefinition(handle);
            string name = reader.GetString(method.Name);
            MethodSignature<string> signature = method.DecodeSignature(NameProvider.Instance, genericContext: null);
            string key = $"{typeName}::{name}<{signature.GenericParameterCount}>({string.Join(",", signature.ParameterTypes)}):{signature.ReturnType}";
            string display = $"{typeName}::{name}";
            methods.Add(new Method(
                handle,
                key,
                new FindingSubject($"0x{MetadataTokens.GetToken(handle):X8}", display),
                new IlMemberDiffSubject(display, name)));
        }
    }
    return [.. methods];
}

static string TypeName(MetadataReader reader, TypeDefinitionHandle handle)
{
    TypeDefinition type = reader.GetTypeDefinition(handle);
    string name = reader.GetString(type.Name);
    TypeDefinitionHandle declaring = type.GetDeclaringType();
    if (!declaring.IsNil)
        return $"{TypeName(reader, declaring)}/{name}";
    string ns = reader.GetString(type.Namespace);
    return ns.Length == 0 ? name : $"{ns}.{name}";
}

sealed record Method(MethodDefinitionHandle Handle, string Key, FindingSubject Subject, IlMemberDiffSubject MemberSubject);

/// <summary>
/// Names signature types by namespace and name without their token or
/// assembly, so two versions pair by what the signature says.
/// </summary>
sealed class NameProvider : ISignatureTypeProvider<string, object?>
{
    public static NameProvider Instance { get; } = new();

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        string name = reader.GetString(type.Name);
        TypeDefinitionHandle declaring = type.GetDeclaringType();
        if (!declaring.IsNil)
            return $"{GetTypeFromDefinition(reader, declaring, rawTypeKind)}/{name}";
        string ns = reader.GetString(type.Namespace);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        TypeReference type = reader.GetTypeReference(handle);
        string name = reader.GetString(type.Name);
        if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            return $"{GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, rawTypeKind)}/{name}";
        string ns = reader.GetString(type.Namespace);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public string GetSZArrayType(string elementType) => $"{elementType}[]";

    public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[{new string(',', shape.Rank - 1)}]";

    public string GetByReferenceType(string elementType) => $"{elementType}&";

    public string GetPointerType(string elementType) => $"{elementType}*";

    public string GetPinnedType(string elementType) => $"{elementType} pinned";

    public string GetGenericInstantiation(string genericType, System.Collections.Immutable.ImmutableArray<string> typeArguments)
        => $"{genericType}<{string.Join(",", typeArguments)}>";

    public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";

    public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
        => $"{unmodifiedType} {(isRequired ? "modreq" : "modopt")}({modifier})";

    public string GetFunctionPointerType(MethodSignature<string> signature)
        => $"delegate*<{string.Join(",", signature.ParameterTypes)}:{signature.ReturnType}>";
}
