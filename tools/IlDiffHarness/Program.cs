using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

using ILInspector.Instructions;

const string Usage =
    """
    il-diff-harness <old-assembly> <new-assembly> [--max-examples N]

      Emits a small IL Diff card over paired assemblies:
      - compared body count and self-diff empty count;
      - pair exact-empty and changed-body counts;
      - failure count and buckets;
      - top hunk kinds and opcode families;
      - capped examples rendered through IlDiffPrinter.
    """;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.Error.WriteLine(Usage);
    return 0;
}

if (args.Length < 2)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

string oldAssembly = args[0];
string newAssembly = args[1];
int maxExamples = 5;
for (int i = 2; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--max-examples":
            if (i + 1 >= args.Length || !int.TryParse(args[++i], out maxExamples) || maxExamples < 0)
            {
                Console.Error.WriteLine("--max-examples requires a non-negative integer.");
                return 2;
            }
            break;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            Console.Error.WriteLine(Usage);
            return 2;
    }
}

if (!File.Exists(oldAssembly))
{
    Console.Error.WriteLine($"Old assembly not found: {oldAssembly}");
    return 2;
}

if (!File.Exists(newAssembly))
{
    Console.Error.WriteLine($"New assembly not found: {newAssembly}");
    return 2;
}

try
{
    using var oldFile = File.OpenRead(oldAssembly);
    using var newFile = File.OpenRead(newAssembly);
    using var oldPe = new PEReader(oldFile);
    using var newPe = new PEReader(newFile);
    var oldReader = oldPe.GetMetadataReader();
    var newReader = newPe.GetMetadataReader();

    var card = BuildCard(oldPe, oldReader, newPe, newReader, maxExamples);
    Console.Write(FormatCard(Path.GetFileName(oldAssembly), Path.GetFileName(newAssembly), card));
    return 0;
}
catch (Exception ex) when (ex is BadImageFormatException or IOException or InvalidOperationException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

static IlDiffCard BuildCard(
    PEReader oldPe,
    MetadataReader oldReader,
    PEReader newPe,
    MetadataReader newReader,
    int maxExamples)
{
    var oldMethods = MethodMap(oldReader);
    var newMethods = MethodMap(newReader);
    var keys = oldMethods.Keys.Union(newMethods.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    var failures = new Dictionary<string, int>(StringComparer.Ordinal);
    var hunkKinds = new Dictionary<string, int>(StringComparer.Ordinal);
    var opcodeFamilies = new Dictionary<string, int>(StringComparer.Ordinal);
    var examples = ImmutableArray.CreateBuilder<IlDiffExample>();

    int compared = 0;
    int pairExact = 0;
    int changed = 0;
    int selfDiffExact = 0;

    foreach (string key in keys)
    {
        if (!oldMethods.TryGetValue(key, out var oldHandle))
        {
            Increment(failures, "old body missing");
            continue;
        }

        if (!newMethods.TryGetValue(key, out var newHandle))
        {
            Increment(failures, "new body missing");
            continue;
        }

        var oldMethod = oldReader.GetMethodDefinition(oldHandle);
        var newMethod = newReader.GetMethodDefinition(newHandle);
        if (oldMethod.RelativeVirtualAddress == 0 && newMethod.RelativeVirtualAddress == 0)
            continue;
        if (oldMethod.RelativeVirtualAddress == 0)
        {
            Increment(failures, "old body missing");
            continue;
        }
        if (newMethod.RelativeVirtualAddress == 0)
        {
            Increment(failures, "new body missing");
            continue;
        }

        compared++;
        MethodBodyBlock oldBody;
        MethodBodyBlock newBody;
        try
        {
            oldBody = oldPe.GetMethodBody(oldMethod.RelativeVirtualAddress);
            newBody = newPe.GetMethodBody(newMethod.RelativeVirtualAddress);
        }
        catch (BadImageFormatException)
        {
            Increment(failures, "body read failed");
            continue;
        }

        var self = IlBodyDiff.Compare(oldReader, oldBody, oldReader, oldBody);
        if (self.IsExact)
            selfDiffExact++;
        else
            Increment(failures, self.Failure ?? "self-diff not exact");

        var diff = IlBodyDiff.Compare(oldReader, oldBody, newReader, newBody);
        if (diff.Failure is { Length: > 0 } failure)
        {
            Increment(failures, failure);
            continue;
        }

        if (diff.IsExact)
        {
            pairExact++;
            continue;
        }

        changed++;
        foreach (var row in diff.Rows)
        {
            Increment(hunkKinds, row.Kind.ToString());
            Increment(opcodeFamilies, row.Operation.OpcodeFamily);
        }

        if (examples.Count < maxExamples)
            examples.Add(new IlDiffExample(key, IlDiffPrinter.RenderUnified(diff)));
    }

    return new IlDiffCard(
        compared,
        selfDiffExact,
        pairExact,
        changed,
        failures.Values.Sum(),
        Top(failures),
        Top(hunkKinds),
        Top(opcodeFamilies),
        examples.ToImmutable());
}

static Dictionary<string, MethodDefinitionHandle> MethodMap(MetadataReader reader)
{
    var methods = new Dictionary<string, MethodDefinitionHandle>(StringComparer.Ordinal);
    foreach (var handle in reader.MethodDefinitions)
    {
        var method = reader.GetMethodDefinition(handle);
        string key = MethodKey(reader, method);
        methods.TryAdd(key, handle);
    }

    return methods;
}

static string MethodKey(MetadataReader reader, MethodDefinition method)
{
    string type = TypeName(reader, method.GetDeclaringType());
    string name = reader.GetString(method.Name);
    var signature = method.DecodeSignature(SignatureIdentityProvider.Instance, genericContext: null);
    string instance = signature.Header.IsInstance ? "instance" : "static";
    string genericArity = signature.GenericParameterCount > 0 ? $"<{signature.GenericParameterCount}>" : "";
    string signatureText = $"{instance} {signature.ReturnType}({string.Join(", ", signature.ParameterTypes)})";
    return $"{type}::{name}{genericArity}#{signatureText}";
}

static string TypeName(MetadataReader reader, TypeDefinitionHandle handle)
{
    var type = reader.GetTypeDefinition(handle);
    string name = reader.GetString(type.Name);
    var declaring = type.GetDeclaringType();
    if (!declaring.IsNil)
        return $"{TypeName(reader, declaring)}+{name}";
    string ns = reader.GetString(type.Namespace);
    return ns.Length == 0 ? name : $"{ns}.{name}";
}

static ImmutableArray<CardBucket> Top(Dictionary<string, int> counts)
    => [.. counts
        .OrderByDescending(pair => pair.Value)
        .ThenBy(pair => pair.Key, StringComparer.Ordinal)
        .Take(10)
        .Select(pair => new CardBucket(pair.Key, pair.Value))];

static void Increment(Dictionary<string, int> counts, string key)
    => counts[key] = counts.TryGetValue(key, out int count) ? count + 1 : 1;

static string FormatCard(string oldName, string newName, IlDiffCard card)
{
    var builder = new StringBuilder();
    builder.AppendLine("# IL Diff Card");
    builder.AppendLine();
    builder.AppendLine($"Old: `{oldName}`");
    builder.AppendLine($"New: `{newName}`");
    builder.AppendLine();
    builder.AppendLine("| Metric | Count |");
    builder.AppendLine("| --- | ---: |");
    builder.AppendLine($"| Compared bodies | {card.ComparedBodyCount} |");
    builder.AppendLine($"| Self-diff empty | {card.SelfDiffExactCount} |");
    builder.AppendLine($"| Pair exact empty | {card.PairExactCount} |");
    builder.AppendLine($"| Changed bodies | {card.ChangedBodyCount} |");
    builder.AppendLine($"| Failures | {card.FailureCount} |");
    AppendBuckets(builder, "Failure buckets", card.FailureBuckets);
    AppendBuckets(builder, "Top hunk kinds", card.TopHunkKinds);
    AppendBuckets(builder, "Top opcode families", card.TopOpcodeFamilies);
    if (!card.Examples.IsDefaultOrEmpty)
    {
        builder.AppendLine();
        builder.AppendLine("## Examples");
        foreach (var example in card.Examples)
        {
            builder.AppendLine();
            builder.AppendLine($"### `{example.Method}`");
            builder.AppendLine();
            builder.AppendLine("```diff");
            builder.AppendLine(example.UnifiedDiff);
            builder.AppendLine("```");
        }
    }

    return builder.ToString();
}

static void AppendBuckets(StringBuilder builder, string title, ImmutableArray<CardBucket> buckets)
{
    builder.AppendLine();
    builder.AppendLine($"## {title}");
    builder.AppendLine();
    if (buckets.IsDefaultOrEmpty)
    {
        builder.AppendLine("_None_");
        return;
    }

    builder.AppendLine("| Bucket | Count |");
    builder.AppendLine("| --- | ---: |");
    foreach (var bucket in buckets)
        builder.AppendLine($"| `{bucket.Name}` | {bucket.Count} |");
}

sealed record IlDiffCard(
    int ComparedBodyCount,
    int SelfDiffExactCount,
    int PairExactCount,
    int ChangedBodyCount,
    int FailureCount,
    ImmutableArray<CardBucket> FailureBuckets,
    ImmutableArray<CardBucket> TopHunkKinds,
    ImmutableArray<CardBucket> TopOpcodeFamilies,
    ImmutableArray<IlDiffExample> Examples);

sealed record CardBucket(string Name, int Count);

sealed record IlDiffExample(string Method, string UnifiedDiff);

sealed class SignatureIdentityProvider : ISignatureTypeProvider<string, object?>
{
    public static SignatureIdentityProvider Instance { get; } = new();

    public string GetPrimitiveType(PrimitiveTypeCode typeCode)
        => typeCode switch
        {
            PrimitiveTypeCode.Void => "void",
            PrimitiveTypeCode.Boolean => "bool",
            PrimitiveTypeCode.Char => "char",
            PrimitiveTypeCode.SByte => "int8",
            PrimitiveTypeCode.Byte => "uint8",
            PrimitiveTypeCode.Int16 => "int16",
            PrimitiveTypeCode.UInt16 => "uint16",
            PrimitiveTypeCode.Int32 => "int32",
            PrimitiveTypeCode.UInt32 => "uint32",
            PrimitiveTypeCode.Int64 => "int64",
            PrimitiveTypeCode.UInt64 => "uint64",
            PrimitiveTypeCode.Single => "float32",
            PrimitiveTypeCode.Double => "float64",
            PrimitiveTypeCode.String => "string",
            PrimitiveTypeCode.Object => "object",
            PrimitiveTypeCode.IntPtr => "native int",
            PrimitiveTypeCode.UIntPtr => "native uint",
            PrimitiveTypeCode.TypedReference => "typedref",
            _ => typeCode.ToString(),
        };

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        => TypeDefinitionName(reader, handle);

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeReference(handle);
        string name = reader.GetString(type.Name);
        string ns = reader.GetString(type.Namespace);
        string fullName = ns.Length == 0 ? name : $"{ns}.{name}";
        return type.ResolutionScope.Kind switch
        {
            HandleKind.AssemblyReference =>
                $"[{reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name)}]{fullName}",
            HandleKind.TypeReference =>
                $"{GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, rawTypeKind)}+{fullName}",
            _ => fullName,
        };
    }

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public string GetSZArrayType(string elementType) => $"{elementType}[]";
    public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[{new string(',', Math.Max(shape.Rank - 1, 0))}]";
    public string GetByReferenceType(string elementType) => $"{elementType}&";
    public string GetPointerType(string elementType) => $"{elementType}*";
    public string GetPinnedType(string elementType) => $"{elementType} pinned";
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        => $"{genericType}<{string.Join(", ", typeArguments)}>";
    public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";
    public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
        => $"{(isRequired ? "modreq" : "modopt")}({modifier}) {unmodifiedType}";
    public string GetFunctionPointerType(MethodSignature<string> signature)
        => $"method {signature.ReturnType} *({string.Join(", ", signature.ParameterTypes)})";

    static string TypeDefinitionName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        string name = reader.GetString(type.Name);
        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil)
            return $"{TypeDefinitionName(reader, declaring)}+{name}";
        string ns = reader.GetString(type.Namespace);
        string assembly = reader.IsAssembly ? reader.GetString(reader.GetAssemblyDefinition().Name) : "";
        return $"[{assembly}]{(ns.Length == 0 ? name : $"{ns}.{name}")}";
    }
}
