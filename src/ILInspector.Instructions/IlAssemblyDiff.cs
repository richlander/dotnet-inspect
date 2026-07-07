using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Instructions;

public sealed record IlDiffBucket(string Name, int Count);

public sealed record IlAssemblyDiffExample(string Method, IlBodyDiffResult Diff);

public sealed record IlAssemblyDiffResult(
    int ComparedBodyCount,
    int SelfDiffExactCount,
    int PairExactCount,
    int ChangedBodyCount,
    int FailureCount,
    ImmutableArray<IlDiffBucket> FailureBuckets,
    ImmutableArray<IlDiffBucket> TopHunkKinds,
    ImmutableArray<IlDiffBucket> TopOpcodeFamilies,
    ImmutableArray<IlAssemblyDiffExample> Examples);

/// <summary>
/// Product-owned IL/body diff producer over two metadata-backed assemblies.
/// </summary>
public static class IlAssemblyDiff
{
    public static IlAssemblyDiffResult Compare(
        PEReader oldPe,
        MetadataReader oldReader,
        PEReader newPe,
        MetadataReader newReader,
        int maxExamples = 5)
    {
        ArgumentNullException.ThrowIfNull(oldPe);
        ArgumentNullException.ThrowIfNull(oldReader);
        ArgumentNullException.ThrowIfNull(newPe);
        ArgumentNullException.ThrowIfNull(newReader);
        if (maxExamples < 0)
            throw new ArgumentOutOfRangeException(nameof(maxExamples), maxExamples, "Example count must be non-negative.");

        var oldMethods = MethodMap(oldReader);
        var newMethods = MethodMap(newReader);
        var keys = oldMethods.Keys.Union(newMethods.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var failures = new Dictionary<string, int>(StringComparer.Ordinal);
        var hunkKinds = new Dictionary<string, int>(StringComparer.Ordinal);
        var opcodeFamilies = new Dictionary<string, int>(StringComparer.Ordinal);
        var examples = ImmutableArray.CreateBuilder<IlAssemblyDiffExample>();

        int compared = 0;
        int pairExact = 0;
        int changed = 0;
        int selfDiffExact = 0;

        foreach (string key in keys)
        {
            if (!oldMethods.TryGetValue(key, out var oldHandle))
            {
                IncrementFailure(failures, IlBodyDiffResult.OldBodyMissing());
                continue;
            }

            if (!newMethods.TryGetValue(key, out var newHandle))
            {
                IncrementFailure(failures, IlBodyDiffResult.NewBodyMissing());
                continue;
            }

            var oldMethod = oldReader.GetMethodDefinition(oldHandle);
            var newMethod = newReader.GetMethodDefinition(newHandle);
            if (oldMethod.RelativeVirtualAddress == 0 && newMethod.RelativeVirtualAddress == 0)
                continue;
            if (oldMethod.RelativeVirtualAddress == 0)
            {
                IncrementFailure(failures, IlBodyDiffResult.OldBodyMissing("method has no body"));
                continue;
            }
            if (newMethod.RelativeVirtualAddress == 0)
            {
                IncrementFailure(failures, IlBodyDiffResult.NewBodyMissing("method has no body"));
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
            {
                selfDiffExact++;
            }
            else
            {
                IncrementFailure(failures, self.FailureRows.IsDefaultOrEmpty
                    ? IlBodyDiffResult.UnsupportedBoundary(self.Failure ?? "self-diff not exact")
                    : self);
            }

            var diff = IlBodyDiff.Compare(oldReader, oldBody, newReader, newBody);
            if (!diff.FailureRows.IsDefaultOrEmpty || diff.Failure is { Length: > 0 })
            {
                IncrementFailure(failures, diff);
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
                examples.Add(new IlAssemblyDiffExample(key, diff));
        }

        return new IlAssemblyDiffResult(
            compared,
            selfDiffExact,
            pairExact,
            changed,
            failures.Values.Sum(),
            Buckets(failures),
            Buckets(hunkKinds),
            Buckets(opcodeFamilies),
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

    static ImmutableArray<IlDiffBucket> Buckets(Dictionary<string, int> counts)
        => [.. counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new IlDiffBucket(pair.Key, pair.Value))];

    static void Increment(Dictionary<string, int> counts, string key)
        => counts[key] = counts.TryGetValue(key, out int count) ? count + 1 : 1;

    static void IncrementFailure(Dictionary<string, int> counts, IlBodyDiffResult result)
    {
        if (!result.FailureRows.IsDefaultOrEmpty)
        {
            foreach (var failure in result.FailureRows)
                Increment(counts, failure.Message);
            return;
        }

        Increment(counts, result.Failure ?? "unknown failure");
    }
}

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
    public string GetGenericInstantiation(string genericType, System.Collections.Immutable.ImmutableArray<string> typeArguments)
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
