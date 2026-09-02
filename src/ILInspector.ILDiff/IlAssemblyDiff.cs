using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using ILInspector.Findings;
using ILInspector.Metadata;

namespace ILInspector.Instructions;

public sealed record IlDiffBucket(string Name, int Count);

public sealed record IlAssemblyDiffExample(string Method, IlBodyDiffResult Diff);

public sealed record IlAssemblyDiffResult(
    int ComparedBodyCount,
    int SelfDiffExactCount,
    int PairExactCount,
    int PairOperandDiffCount,
    int PairOpcodeDiffCount,
    int PairUnavailableCount,
    int ChangedBodyCount,
    int FailureCount,
    ImmutableArray<IlDiffBucket> FailureBuckets,
    ImmutableArray<IlDiffBucket> TopHunkKinds,
    ImmutableArray<IlDiffBucket> TopOpcodeFamilies,
    ImmutableArray<IlAssemblyDiffExample> Examples,
    ImmutableArray<IlIdentityResolutionFailure> IdentityFailures = default);

public sealed record IlIdentityResolutionFailure(
    string Side,
    int SubjectToken,
    MetadataTypeNameFailureMechanism Mechanism,
    string Kind,
    string Detail);

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
    IlBodyDiffResult Diff,
    ImmutableArray<IlIdentityResolutionFailure> IdentityFailures = default);

/// <summary>
/// One explicitly admitted endpoint for an IL member comparison. A present endpoint identifies an
/// exact method definition; subject absence is separate evidence and is never inferred from a null
/// reader, handle, or body.
/// </summary>
public abstract record IlMemberDiffEndpoint
{
    IlMemberDiffEndpoint()
    {
    }

    public sealed record Present : IlMemberDiffEndpoint
    {
        public Present(
            IlMemberDiffSubject subject,
            PEReader pe,
            MetadataReader reader,
            MethodDefinitionHandle method)
        {
            Subject = ValidateSubject(subject);
            Pe = pe ?? throw new ArgumentNullException(nameof(pe));
            Reader = reader ?? throw new ArgumentNullException(nameof(reader));
            if (method.IsNil)
                throw new ArgumentException("Method handle must not be nil.", nameof(method));
            Method = method;
        }

        public IlMemberDiffSubject Subject { get; }
        public PEReader Pe { get; }
        public MetadataReader Reader { get; }
        public MethodDefinitionHandle Method { get; }
    }

    public sealed record SubjectAbsent : IlMemberDiffEndpoint
    {
        public SubjectAbsent(IlMemberDiffSubject subject, string? detail = null)
        {
            Subject = ValidateSubject(subject);
            Detail = detail;
        }

        public IlMemberDiffSubject Subject { get; }
        public string? Detail { get; }
    }

    static IlMemberDiffSubject ValidateSubject(IlMemberDiffSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject.Identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject.Label);
        return subject;
    }
}

/// <summary>
/// The total IL-owned result for two explicitly admitted endpoints. <see cref="MemberDiff"/> is
/// present exactly when both endpoint inspections completed and the pair-dependent IL differ ran.
/// </summary>
public sealed record IlMemberEndpointComparison
{
    internal IlMemberEndpointComparison(
        IlMemberDiffSubject old,
        IlMemberDiffSubject @new,
        FindingComparison<CanonicalIlOperation> findings,
        IlMemberDiffResult? memberDiff)
    {
        Old = old ?? throw new ArgumentNullException(nameof(old));
        New = @new ?? throw new ArgumentNullException(nameof(@new));
        Findings = findings ?? throw new ArgumentNullException(nameof(findings));

        bool isCompletePair = findings.Value
            is FindingComparison<CanonicalIlOperation>.Complete
            {
                Transition:
                {
                    Old: FindingInspectionState.Complete,
                    New: FindingInspectionState.Complete,
                },
            };
        if (isCompletePair != (memberDiff is not null))
        {
            throw new ArgumentException(
                "A native member diff must be present exactly for a complete/complete endpoint pair.",
                nameof(memberDiff));
        }

        if (memberDiff is not null
            && (memberDiff.Old != old || memberDiff.New != @new))
        {
            throw new ArgumentException(
                "The native member diff must retain the admitted endpoint subjects.",
                nameof(memberDiff));
        }

        MemberDiff = memberDiff;
    }

    public IlMemberDiffSubject Old { get; }
    public IlMemberDiffSubject New { get; }
    public FindingComparison<CanonicalIlOperation> Findings { get; }
    public IlMemberDiffResult? MemberDiff { get; }
}

/// <summary>
/// Product-owned IL/body diff producer over two metadata-backed assemblies.
/// </summary>
public static class IlAssemblyDiff
{
    readonly record struct MethodMapKey(string Identity, int Occurrence)
    {
        public string Display => Occurrence == 1 ? Identity : $"{Identity}#occurrence:{Occurrence}";
    }

    sealed record MethodMapResult(
        Dictionary<MethodMapKey, MethodDefinitionHandle> Methods,
        ImmutableArray<IlIdentityResolutionFailure> Failures);

    readonly record struct MethodIdentityResult(
        string? Identity,
        MetadataTypeNameFailure? Failure);

    readonly record struct InspectedEndpoint(
        IlMemberDiffSubject Subject,
        FindingInspection<CanonicalIlOperation> Inspection,
        MethodInstructions? Body,
        MethodBodyBlock? MethodBody);

    /// <summary>
    /// Compares two explicitly admitted endpoints without performing selector resolution or
    /// cross-version correspondence. The pair-dependent IL body differ runs only when both
    /// endpoint inspections complete.
    /// </summary>
    public static IlMemberEndpointComparison CompareMemberEndpoints(
        IlMemberDiffEndpoint oldEndpoint,
        IlMemberDiffEndpoint newEndpoint,
        IlBodyDiffNormalization normalization = IlBodyDiffNormalization.None)
    {
        ArgumentNullException.ThrowIfNull(oldEndpoint);
        ArgumentNullException.ThrowIfNull(newEndpoint);

        var old = InspectEndpoint(oldEndpoint);
        var @new = InspectEndpoint(newEndpoint);
        var findings = IlFindings.CompareInspections(
            old.Inspection,
            @new.Inspection,
            old.Body,
            @new.Body,
            acceptanceThreshold: 100);

        IlMemberDiffResult? memberDiff = null;
        if (old.MethodBody is not null
            && @new.MethodBody is not null
            && findings.Value
                is FindingComparison<CanonicalIlOperation>.Complete
                {
                    Transition:
                    {
                        Old: FindingInspectionState.Complete,
                        New: FindingInspectionState.Complete,
                    },
                })
        {
            var oldPresent = (IlMemberDiffEndpoint.Present)oldEndpoint;
            var newPresent = (IlMemberDiffEndpoint.Present)newEndpoint;
            memberDiff = new IlMemberDiffResult(
                old.Subject,
                @new.Subject,
                IlBodyDiff.Compare(
                    oldPresent.Reader,
                    old.MethodBody,
                    newPresent.Reader,
                    @new.MethodBody,
                    normalization),
                []);
        }

        return new IlMemberEndpointComparison(
            old.Subject,
            @new.Subject,
            findings,
            memberDiff);
    }

    static InspectedEndpoint InspectEndpoint(IlMemberDiffEndpoint endpoint)
        => endpoint switch
        {
            IlMemberDiffEndpoint.Present present => InspectPresentEndpoint(present),
            IlMemberDiffEndpoint.SubjectAbsent absent => new(
                absent.Subject,
                new FindingInspection<CanonicalIlOperation>.Absent(
                    FindingInspectionAbsenceKind.SubjectAbsent,
                    absent.Detail),
                Body: null,
                MethodBody: null),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint)),
        };

    static InspectedEndpoint InspectPresentEndpoint(IlMemberDiffEndpoint.Present endpoint)
    {
        var subject = new FindingSubject(endpoint.Subject.Identity, endpoint.Subject.Label);
        var inspection = IlFindings.InspectMethod(
            endpoint.Pe,
            endpoint.Reader,
            endpoint.Method,
            subject,
            out var body,
            out var methodBody);
        return new InspectedEndpoint(endpoint.Subject, inspection, body, methodBody);
    }

    public static IlAssemblyDiffPairResult CompareFiles(
        string oldPath,
        string newPath,
        int maxExamples = 5,
        IlBodyDiffNormalization normalization = IlBodyDiffNormalization.None)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPath);

        using var oldStream = File.OpenRead(oldPath);
        using var newStream = File.OpenRead(newPath);
        return CompareStreams(
            oldStream,
            oldPath,
            newStream,
            newPath,
            maxExamples,
            normalization);
    }

    public static IlAssemblyDiffPairResult CompareStreams(
        Stream oldStream,
        string oldName,
        Stream newStream,
        string newName,
        int maxExamples = 5,
        IlBodyDiffNormalization normalization = IlBodyDiffNormalization.None)
    {
        ArgumentNullException.ThrowIfNull(oldStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(oldName);
        ArgumentNullException.ThrowIfNull(newStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        using var oldPe = new PEReader(oldStream, PEStreamOptions.LeaveOpen);
        using var newPe = new PEReader(newStream, PEStreamOptions.LeaveOpen);
        var result = Compare(
            oldPe,
            oldPe.GetMetadataReader(),
            newPe,
            newPe.GetMetadataReader(),
            maxExamples,
            normalization);
        return new IlAssemblyDiffPairResult(oldName, newName, result);
    }

    public static IlAssemblyDiffResult Compare(
        PEReader oldPe,
        MetadataReader oldReader,
        PEReader newPe,
        MetadataReader newReader,
        int maxExamples = 5,
        IlBodyDiffNormalization normalization = IlBodyDiffNormalization.None)
    {
        ArgumentNullException.ThrowIfNull(oldPe);
        ArgumentNullException.ThrowIfNull(oldReader);
        ArgumentNullException.ThrowIfNull(newPe);
        ArgumentNullException.ThrowIfNull(newReader);
        if (maxExamples < 0)
            throw new ArgumentOutOfRangeException(nameof(maxExamples), maxExamples, "Example count must be non-negative.");

        var oldIndex = MethodMap(oldReader, "old");
        var newIndex = MethodMap(newReader, "new");
        var keys = oldIndex.Methods.Keys
            .Union(newIndex.Methods.Keys)
            .OrderBy(key => key.Identity, StringComparer.Ordinal)
            .ThenBy(key => key.Occurrence)
            .ToArray();
        var failures = new Dictionary<string, int>(StringComparer.Ordinal);
        var hunkKinds = new Dictionary<string, int>(StringComparer.Ordinal);
        var opcodeFamilies = new Dictionary<string, int>(StringComparer.Ordinal);
        var examples = ImmutableArray.CreateBuilder<IlAssemblyDiffExample>();
        var identityFailures = ImmutableArray.CreateBuilder<IlIdentityResolutionFailure>();
        identityFailures.AddRange(oldIndex.Failures);
        identityFailures.AddRange(newIndex.Failures);
        foreach (var failure in identityFailures)
            Increment(failures, $"identity resolution failed: {failure.Mechanism}/{failure.Kind}");

        int compared = 0;
        int pairExact = 0;
        int pairOperandDiff = 0;
        int pairOpcodeDiff = 0;
        int pairUnavailable = 0;
        int changed = 0;
        int selfDiffExact = 0;

        foreach (var key in keys)
        {
            if (!oldIndex.Methods.TryGetValue(key, out var oldHandle))
            {
                pairUnavailable++;
                IncrementFailure(failures, IlBodyDiffResult.OldBodyMissing());
                continue;
            }

            if (!newIndex.Methods.TryGetValue(key, out var newHandle))
            {
                pairUnavailable++;
                IncrementFailure(failures, IlBodyDiffResult.NewBodyMissing());
                continue;
            }

            var oldMethod = oldReader.GetMethodDefinition(oldHandle);
            var newMethod = newReader.GetMethodDefinition(newHandle);
            if (oldMethod.RelativeVirtualAddress == 0 && newMethod.RelativeVirtualAddress == 0)
                continue;
            if (oldMethod.RelativeVirtualAddress == 0)
            {
                pairUnavailable++;
                IncrementFailure(failures, IlBodyDiffResult.OldBodyMissing("method has no body"));
                continue;
            }
            if (newMethod.RelativeVirtualAddress == 0)
            {
                pairUnavailable++;
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
                pairUnavailable++;
                Increment(failures, "body read failed");
                continue;
            }

            var self = IlBodyDiff.Compare(
                oldReader,
                oldBody,
                oldReader,
                oldBody,
                normalization);
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

            var diff = IlBodyDiff.Compare(
                oldReader,
                oldBody,
                newReader,
                newBody,
                normalization);
            if (!diff.IsAvailable)
            {
                pairUnavailable++;
                IncrementFailure(failures, diff);
                continue;
            }

            if (diff.Outcome == IlBodyDiffOutcome.Exact)
            {
                pairExact++;
                continue;
            }

            changed++;
            if (diff.Outcome == IlBodyDiffOutcome.OperandDiff)
                pairOperandDiff++;
            else
                pairOpcodeDiff++;
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
            pairOperandDiff,
            pairOpcodeDiff,
            pairUnavailable,
            changed,
            failures.Values.Sum(),
            Buckets(failures),
            Buckets(hunkKinds),
            Buckets(opcodeFamilies),
            examples.ToImmutable(),
            identityFailures.ToImmutable());
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
        IlBodyDiffNormalization normalization = IlBodyDiffNormalization.None)
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
        var oldIdentity = MethodKey(oldReader, oldMethod, oldDefinition);
        var newIdentity = MethodKey(newReader, newMethod, newDefinition);
        var identityFailures = ImmutableArray.CreateBuilder<IlIdentityResolutionFailure>();
        AddIdentityFailure(identityFailures, "old", oldMethod, oldIdentity.Failure);
        AddIdentityFailure(identityFailures, "new", newMethod, newIdentity.Failure);
        string oldIdentityText = oldIdentity.Identity ?? TokenSubject(oldMethod);
        string newIdentityText = newIdentity.Identity ?? TokenSubject(newMethod);
        var oldSubject = new IlMemberDiffSubject(oldIdentityText, oldLabel ?? oldIdentityText);
        var newSubject = new IlMemberDiffSubject(newIdentityText, newLabel ?? newIdentityText);

        var oldBody = TryGetBody(oldPe, oldDefinition, "old");
        var newBody = TryGetBody(newPe, newDefinition, "new");
        var diff = identityFailures.Count > 0
            ? IlBodyDiffResult.Failed(
                IlDiffFailureKind.IdentityResolutionFailure,
                "method identity resolution failed",
                detail: string.Join("; ", identityFailures.Select(FormatIdentityFailure)))
            : oldBody.Result ?? newBody.Result
                ?? IlBodyDiff.Compare(
                    oldReader,
                    oldBody.Body!,
                    newReader,
                    newBody.Body!,
                    normalization);
        return new IlMemberDiffResult(
            oldSubject,
            newSubject,
            diff,
            identityFailures.ToImmutable());
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

    static MethodMapResult MethodMap(MetadataReader reader, string side)
    {
        var methods = new Dictionary<MethodMapKey, MethodDefinitionHandle>();
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var failures = ImmutableArray.CreateBuilder<IlIdentityResolutionFailure>();
        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);
            var result = MethodKey(reader, handle, method);
            if (result.Failure is not null)
            {
                AddIdentityFailure(failures, side, handle, result.Failure);
                continue;
            }

            string identity = result.Identity!;
            int occurrence = occurrences.TryGetValue(identity, out int count) ? count + 1 : 1;
            occurrences[identity] = occurrence;
            methods.Add(new MethodMapKey(identity, occurrence), handle);
        }

        return new MethodMapResult(methods, failures.ToImmutable());
    }

    static MethodIdentityResult MethodKey(
        MetadataReader reader,
        MethodDefinitionHandle handle,
        MethodDefinition method)
    {
        var type = MetadataIdentityName.TypeDefinition(
            reader,
            method.GetDeclaringType(),
            includeAssembly: false);
        if (type is MetadataTypeNameResult.Rejected rejectedType)
            return new MethodIdentityResult(null, rejectedType.Failure);
        if (type is not MetadataTypeNameResult.Resolved resolvedType)
        {
            return new MethodIdentityResult(
                null,
                MetadataTypeNameFailure.ForMechanism(
                    MetadataTypeNameFailureMechanism.Relationship,
                    method.GetDeclaringType(),
                    "The method declaring type has no resolvable identity."));
        }

        string name = reader.GetString(method.Name);
        var context = new SignatureIdentityContext();
        var provider = new SignatureIdentityProvider(context);
        if (!GuardedProviderDecode.TryMethod(
            reader,
            method,
            provider,
            context,
            out var signature))
        {
            return new MethodIdentityResult(
                null,
                MetadataTypeNameFailure.ForMechanism(
                    MetadataTypeNameFailureMechanism.Signature,
                    handle,
                    "The method signature was rejected before an identity could be produced."));
        }
        if (context.Failure is not null)
            return new MethodIdentityResult(null, context.Failure);

        string instance = signature.Header.IsInstance ? "instance" : "static";
        string genericArity = signature.GenericParameterCount > 0 ? $"<{signature.GenericParameterCount}>" : "";
        string signatureText = $"{instance} {signature.ReturnType}({string.Join(", ", signature.ParameterTypes)})";
        return new MethodIdentityResult(
            $"{resolvedType.Value}::{name}{genericArity}#{signatureText}",
            null);
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

    static void AddIdentityFailure(
        ImmutableArray<IlIdentityResolutionFailure>.Builder failures,
        string side,
        EntityHandle subject,
        MetadataTypeNameFailure? failure)
    {
        if (failure is null)
            return;
        failures.Add(new IlIdentityResolutionFailure(
            side,
            failure.SubjectToken ?? MetadataTokens.GetToken(subject),
            failure.Mechanism,
            failure.Kind,
            failure.Detail));
    }

    static string FormatIdentityFailure(IlIdentityResolutionFailure failure)
        => $"{failure.Side} 0x{failure.SubjectToken:X8} "
            + $"{failure.Mechanism}/{failure.Kind}: {failure.Detail}";

    static string TokenSubject(EntityHandle handle)
        => $"token 0x{MetadataTokens.GetToken(handle):X8}";
}

sealed class SignatureIdentityContext
{
    public MetadataTypeNameFailure? Failure { get; private set; }

    public void Reject(MetadataTypeNameFailure failure)
        => Failure ??= failure;
}

static class RejectedArrayShapeIdentity
{
    public static string Format(string elementType, ArrayShape shape)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, shape.Rank);
        Append(hash, shape.Sizes.Length);
        foreach (int size in shape.Sizes)
            Append(hash, size);
        Append(hash, shape.LowerBounds.Length);
        foreach (int lowerBound in shape.LowerBounds)
            Append(hash, lowerBound);

        Span<byte> digest = stackalloc byte[32];
        if (!hash.TryGetHashAndReset(digest, out int written) || written != digest.Length)
            throw new CryptographicException("Could not hash the rejected array shape.");
        return $"{elementType}[<unsupported-array-shape:{Convert.ToHexString(digest)}>]";
    }

    static void Append(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }
}

sealed class SignatureIdentityProvider : ISignatureTypeProvider<string, SignatureIdentityContext>
{
    readonly SignatureIdentityContext _context;

    public SignatureIdentityProvider(SignatureIdentityContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

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

    public string GetTypeFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        byte rawTypeKind)
        => Resolve(
            MetadataIdentityName.TypeDefinition(reader, handle, includeAssembly: true),
            handle,
            _context);

    public string GetTypeFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        byte rawTypeKind)
        => Resolve(
            MetadataIdentityName.TypeReference(reader, handle),
            handle,
            _context);

    public string GetTypeFromSpecification(
        MetadataReader reader,
        SignatureIdentityContext genericContext,
        TypeSpecificationHandle handle,
        byte rawTypeKind)
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
    public string GetArrayType(string elementType, ArrayShape shape)
    {
        if (ArrayShapeText.TryFormat(elementType, shape, out string text))
            return text;

        return RejectedArrayShapeIdentity.Format(elementType, shape);
    }
    public string GetByReferenceType(string elementType) => $"{elementType}&";
    public string GetPointerType(string elementType) => $"{elementType}*";
    public string GetPinnedType(string elementType) => $"{elementType} pinned";
    public string GetGenericInstantiation(string genericType, System.Collections.Immutable.ImmutableArray<string> typeArguments)
        => $"{genericType}<{string.Join(", ", typeArguments)}>";
    public string GetGenericTypeParameter(SignatureIdentityContext genericContext, int index) => $"!{index}";
    public string GetGenericMethodParameter(SignatureIdentityContext genericContext, int index) => $"!!{index}";
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
        => $"{(isRequired ? "modreq" : "modopt")}({modifier}) {unmodifiedType}";
    public string GetFunctionPointerType(MethodSignature<string> signature)
        => $"method {signature.ReturnType} *({string.Join(", ", signature.ParameterTypes)})";

    static string Resolve(
        MetadataTypeNameResult result,
        EntityHandle subject,
        SignatureIdentityContext? context)
    {
        if (result is MetadataTypeNameResult.Resolved resolved)
            return resolved.Value;

        var failure = result is MetadataTypeNameResult.Rejected rejected
            ? rejected.Failure
            : MetadataTypeNameFailure.ForMechanism(
                MetadataTypeNameFailureMechanism.Relationship,
                subject,
                "The signature type has no resolvable identity.");
        context?.Reject(failure);
        return $"<identity-rejected:0x{MetadataTokens.GetToken(subject):X8}>";
    }
}
