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

public sealed record IlAssemblyDiffPairResult(
    string Old,
    string New,
    IlAssemblyDiffResult Diff);

public sealed record IlMemberDiffSubject(
    string Identity,
    string Label);

public sealed record IlMemberDiffResult(
    IlMemberDiffSubject Old,
    IlMemberDiffSubject New,
    IlBodyDiffResult Diff);

/// <summary>
/// Product-owned IL/body diff producer over two metadata-backed assemblies.
/// </summary>
public static class IlAssemblyDiff
{
    readonly record struct MethodMapKey(string Identity, int Occurrence)
    {
        public string Display => Occurrence == 1 ? Identity : $"{Identity}#occurrence:{Occurrence}";
    }

    public static IlAssemblyDiffPairResult CompareFiles(
        string oldPath,
        string newPath,
        int maxExamples = 5)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPath);

        using var oldStream = File.OpenRead(oldPath);
        using var newStream = File.OpenRead(newPath);
        return CompareStreams(oldStream, oldPath, newStream, newPath, maxExamples);
    }

    public static IlAssemblyDiffPairResult CompareStreams(
        Stream oldStream,
        string oldName,
        Stream newStream,
        string newName,
        int maxExamples = 5)
    {
        ArgumentNullException.ThrowIfNull(oldStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(oldName);
        ArgumentNullException.ThrowIfNull(newStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        using var oldPe = new PEReader(oldStream, PEStreamOptions.LeaveOpen);
        using var newPe = new PEReader(newStream, PEStreamOptions.LeaveOpen);
        var result = Compare(oldPe, oldPe.GetMetadataReader(), newPe, newPe.GetMetadataReader(), maxExamples);
        return new IlAssemblyDiffPairResult(oldName, newName, result);
    }

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
        var keys = oldMethods.Keys
            .Union(newMethods.Keys)
            .OrderBy(key => key.Identity, StringComparer.Ordinal)
            .ThenBy(key => key.Occurrence)
            .ToArray();
        var failures = new Dictionary<string, int>(StringComparer.Ordinal);
        var hunkKinds = new Dictionary<string, int>(StringComparer.Ordinal);
        var opcodeFamilies = new Dictionary<string, int>(StringComparer.Ordinal);
        var examples = ImmutableArray.CreateBuilder<IlAssemblyDiffExample>();

        int compared = 0;
        int pairExact = 0;
        int changed = 0;
        int selfDiffExact = 0;

        foreach (var key in keys)
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
                examples.Add(new IlAssemblyDiffExample(key.Display, diff));
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

    public static IlMemberDiffResult CompareMembers(
        PEReader oldPe,
        MetadataReader oldReader,
        MethodDefinitionHandle oldMethod,
        PEReader newPe,
        MetadataReader newReader,
        MethodDefinitionHandle newMethod,
        string? oldLabel = null,
        string? newLabel = null,
        IlBodyDiffProfile profile = IlBodyDiffProfile.Default)
    {
        ArgumentNullException.ThrowIfNull(oldPe);
        ArgumentNullException.ThrowIfNull(oldReader);
        ArgumentNullException.ThrowIfNull(newPe);
        ArgumentNullException.ThrowIfNull(newReader);
        if (oldMethod.IsNil)
            throw new ArgumentException("Old method handle must not be nil.", nameof(oldMethod));
        if (newMethod.IsNil)
            throw new ArgumentException("New method handle must not be nil.", nameof(newMethod));

        var oldDefinition = oldReader.GetMethodDefinition(oldMethod);
        var newDefinition = newReader.GetMethodDefinition(newMethod);
        var oldIdentity = MethodKey(oldReader, oldDefinition);
        var newIdentity = MethodKey(newReader, newDefinition);
        var oldSubject = new IlMemberDiffSubject(oldIdentity, oldLabel ?? oldIdentity);
        var newSubject = new IlMemberDiffSubject(newIdentity, newLabel ?? newIdentity);

        var oldBody = TryGetBody(oldPe, oldDefinition, "old");
        var newBody = TryGetBody(newPe, newDefinition, "new");
        var diff = oldBody.Result ?? newBody.Result
            ?? IlBodyDiff.Compare(oldReader, oldBody.Body!, newReader, newBody.Body!, profile);
        return new IlMemberDiffResult(oldSubject, newSubject, diff);
    }

    static (MethodBodyBlock? Body, IlBodyDiffResult? Result) TryGetBody(PEReader pe, MethodDefinition method, string side)
    {
        if (method.RelativeVirtualAddress == 0)
        {
            var result = side == "old"
                ? IlBodyDiffResult.OldBodyMissing("method has no body")
                : IlBodyDiffResult.NewBodyMissing("method has no body");
            return (null, result);
        }

        try
        {
            return (pe.GetMethodBody(method.RelativeVirtualAddress), null);
        }
        catch (BadImageFormatException ex)
        {
            return (null, IlBodyDiffResult.Failed(
                IlDiffFailureKind.DecodeFailure,
                "body read failed",
                side,
                ex.Message));
        }
    }

    static Dictionary<MethodMapKey, MethodDefinitionHandle> MethodMap(MetadataReader reader)
    {
        var methods = new Dictionary<MethodMapKey, MethodDefinitionHandle>();
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);
            string identity = MethodKey(reader, method);
            int occurrence = occurrences.TryGetValue(identity, out int count) ? count + 1 : 1;
            occurrences[identity] = occurrence;
            methods.Add(new MethodMapKey(identity, occurrence), handle);
        }

        return methods;
    }

    static string MethodKey(MetadataReader reader, MethodDefinition method)
    {
        string type = TypeName(reader, method.GetDeclaringType());
        string name = reader.GetString(method.Name);
        if (!GuardedProviderDecode.TryMethod(
            reader,
            method,
            SignatureIdentityProvider.Instance,
            context: null,
            out var signature))
        {
            return $"{type}::{name}#{GuardedProviderDecode.RejectedIdentity(reader, method.Signature)}";
        }
        string instance = signature.Header.IsInstance ? "instance" : "static";
        string genericArity = signature.GenericParameterCount > 0 ? $"<{signature.GenericParameterCount}>" : "";
        string signatureText = $"{instance} {signature.ReturnType}({string.Join(", ", signature.ParameterTypes)})";
        return $"{type}::{name}{genericArity}#{signatureText}";
    }

    static string TypeName(MetadataReader reader, TypeDefinitionHandle handle)
        => BoundedMetadataName.TypeDefinition(reader, handle, includeAssembly: false);

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
        => BoundedMetadataName.TypeDefinition(reader, handle, includeAssembly: true);

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        => BoundedMetadataName.TypeReference(reader, handle);

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        var specification = reader.GetTypeSpecification(handle);
        return GuardedProviderDecode.TryTypeSpec(
            reader,
            handle,
            this,
            genericContext,
            out var decoded)
            ? decoded
            : GuardedProviderDecode.RejectedIdentity(reader, specification.Signature);
    }

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

}
