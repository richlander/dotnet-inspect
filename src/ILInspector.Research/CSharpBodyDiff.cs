using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Security.Cryptography;
using System.Text;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Research;

public enum CSharpDiffKind
{
    Remove,
    Add,
    Changed,
}

public enum CSharpDiffOperationKind
{
    Line,
    Method,
    MethodBody,
    DecompileFailure,
    BodyDiffSkipped,
    SwitchCase,
    ReturnExpression,
    Invocation,
}

public sealed record CSharpDiffOperation(CSharpDiffOperationKind Kind, string Value)
{
    public string Display => Kind switch
    {
        CSharpDiffOperationKind.SwitchCase => $"case {Value}",
        CSharpDiffOperationKind.ReturnExpression => $"return {Value}",
        CSharpDiffOperationKind.Invocation => Value,
        _ => Value,
    };
}

public sealed record CSharpDiffRow(
    string AssemblyIdentity,
    string StableMemberKey,
    MemberAnchor Anchor,
    MetadataTypeRef TypeRef,
    MetadataMemberRef MemberRef,
    string Member,
    string ChangeId,
    string Message,
    int HunkId,
    CSharpDiffKind Kind,
    int? Line,
    string? SourceCoordinate,
    string Fidelity,
    string Text,
    string? OldValue = null,
    string? NewValue = null,
    CSharpDiffOperation? OldOperation = null,
    CSharpDiffOperation? NewOperation = null)
{
    public CSharpDiffRow(
        string assemblyIdentity,
        string stableMemberKey,
        MemberAnchor anchor,
        string member,
        string changeId,
        string message,
        int hunkId,
        CSharpDiffKind kind,
        int? line,
        string? sourceCoordinate,
        string fidelity,
        string text)
        : this(
            assemblyIdentity,
            stableMemberKey,
            anchor,
            UnknownTypeRef(assemblyIdentity),
            UnknownMemberRef(assemblyIdentity),
            member,
            changeId,
            message,
            hunkId,
            kind,
            line,
            sourceCoordinate,
            fidelity,
            text,
            OldValue: null,
            NewValue: null,
            OldOperation: null,
            NewOperation: null)
    {
    }

    public CSharpDiffRow(
        string assemblyIdentity,
        string stableMemberKey,
        MemberAnchor anchor,
        string member,
        string changeId,
        string message,
        int hunkId,
        CSharpDiffKind kind,
        int? line,
        string? sourceCoordinate,
        string fidelity,
        string text,
        string? oldValue,
        string? newValue)
        : this(
            assemblyIdentity,
            stableMemberKey,
            anchor,
            UnknownTypeRef(assemblyIdentity),
            UnknownMemberRef(assemblyIdentity),
            member,
            changeId,
            message,
            hunkId,
            kind,
            line,
            sourceCoordinate,
            fidelity,
            text,
            oldValue,
            newValue,
            OldOperation: null,
            NewOperation: null)
    {
    }

    static MetadataTypeRef UnknownTypeRef(string assemblyIdentity)
        => new(assemblyIdentity, Guid.Empty, 0);

    static MetadataMemberRef UnknownMemberRef(string assemblyIdentity)
        => new(assemblyIdentity, Guid.Empty, 0);
}

public enum CSharpDiffFailureKind
{
    OldBodyMissing,
    NewBodyMissing,
    OldDecompileFailure,
    NewDecompileFailure,
    BodyDiffSkipped,
}

public sealed record CSharpDiffFailureRow(
    string AssemblyIdentity,
    string StableMemberKey,
    MemberAnchor Anchor,
    MetadataTypeRef TypeRef,
    MetadataMemberRef MemberRef,
    string Member,
    CSharpDiffFailureKind Kind,
    string Message,
    string? Side = null,
    string? Detail = null,
    int? HunkId = null);

internal sealed record CSharpSemanticOperation(
    CSharpDiffOperationKind Kind,
    int Line,
    string Value,
    string Text);

public sealed record CSharpBodyDiffResult(
    ImmutableArray<CSharpDiffRow> Rows,
    ImmutableArray<CSharpDiffFailureRow> FailureRows = default)
{
    public CSharpBodyDiffResult(ImmutableArray<CSharpDiffRow> rows)
        : this(rows, FailureRows: [])
    {
    }

    public bool IsExact => Rows.IsEmpty && (FailureRows.IsDefaultOrEmpty);
}

/// <summary>
/// Research-owned C# body diff over the shipped decompiler output for matched
/// method bodies.
/// </summary>
public static class CSharpBodyDiff
{
    internal const int MaxLcsLines = 4096;

    public static CSharpBodyDiffResult CompareAssemblies(string oldPath, string newPath, bool includeNonPublic = false, IReadOnlySet<string>? typeFilters = null)
        => CompareAssemblies([oldPath], [newPath], includeNonPublic, typeFilters);

    public static CSharpBodyDiffResult CompareAssemblies(
        IReadOnlyList<string> oldPaths,
        IReadOnlyList<string> newPaths,
        bool includeNonPublic = false,
        IReadOnlySet<string>? typeFilters = null,
        Func<string, IReadOnlyDictionary<int, MemberAnchor>>? memberAnchorsByToken = null)
    {
        ArgumentNullException.ThrowIfNull(oldPaths);
        ArgumentNullException.ThrowIfNull(newPaths);

        memberAnchorsByToken ??= MemberAnchorsByToken;
        var oldMethods = BuildMethodIndex(oldPaths, includeNonPublic, typeFilters, memberAnchorsByToken);
        var newMethods = BuildMethodIndex(newPaths, includeNonPublic, typeFilters, memberAnchorsByToken);
        var rows = ImmutableArray.CreateBuilder<CSharpDiffRow>();
        var failureRows = ImmutableArray.CreateBuilder<CSharpDiffFailureRow>();
        var sources = new SourceCache();
        int hunkId = 0;

        try
        {
            foreach (var key in oldMethods.Keys.Union(newMethods.Keys).OrderBy(key => key, StringComparer.Ordinal))
            {
                oldMethods.TryGetValue(key, out var oldMethod);
                newMethods.TryGetValue(key, out var newMethod);

                if (oldMethod is null)
                {
                    AddWholeMethod(rows, newMethod!, ref hunkId, CSharpDiffKind.Add);
                    continue;
                }

                if (newMethod is null)
                {
                    AddWholeMethod(rows, oldMethod, ref hunkId, CSharpDiffKind.Remove);
                    continue;
                }

                if (oldMethod.BodyFingerprint == newMethod.BodyFingerprint)
                    continue;

                AddLineDiffRows(rows, failureRows, oldMethod, newMethod, Decompile(oldMethod, sources), Decompile(newMethod, sources), ref hunkId);
            }
        }
        finally
        {
            sources.Dispose();
        }

        return new CSharpBodyDiffResult(rows.ToImmutable(), failureRows.ToImmutable());
    }

    static Dictionary<string, CSharpMethodEntry> BuildMethodIndex(
        IReadOnlyList<string> paths,
        bool includeNonPublic,
        IReadOnlySet<string>? typeFilters,
        Func<string, IReadOnlyDictionary<int, MemberAnchor>> memberAnchorsByToken)
    {
        var entries = new List<CSharpMethodEntry>();
        var assemblyOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var path in paths.Distinct(StringComparer.Ordinal))
        {
            using var source = MetadataSource.OpenWithoutSymbols(path);
            string assemblyKey = StableAssemblyKey(source);
            int occurrence = assemblyOccurrences.GetValueOrDefault(assemblyKey);
            assemblyOccurrences[assemblyKey] = occurrence + 1;
            string occurrenceKey = $"{assemblyKey}#{occurrence}";
            entries.AddRange(EnumerateMethods(source, path, occurrenceKey, includeNonPublic, typeFilters, memberAnchorsByToken(path)));
        }

        return entries
            .GroupBy(entry => $"{entry.StableAssemblyKey}|{entry.RawKey}", StringComparer.Ordinal)
            .SelectMany(group => group
                .GroupBy(entry => entry.DuplicateDiscriminator, StringComparer.Ordinal)
                .OrderBy(discriminatorGroup => discriminatorGroup.Key, StringComparer.Ordinal)
                .SelectMany(discriminatorGroup => discriminatorGroup
                    .OrderBy(entry => entry.Path, StringComparer.Ordinal)
                    .ThenBy(entry => entry.OverloadIndex)
                    .Select((entry, index) => entry with
                    {
                        StableMemberKey = discriminatorGroup.Count() == 1
                            ? $"{entry.StableAssemblyKey}|{entry.RawKey}#{entry.DuplicateDiscriminator}"
                            : $"{entry.StableAssemblyKey}|{entry.RawKey}#{entry.DuplicateDiscriminator}:{index}"
                    }))
                .Select(entry => (Key: entry.StableMemberKey, Entry: entry)))
            .ToDictionary(pair => pair.Key, pair => pair.Entry, StringComparer.Ordinal);
    }

    static CSharpMethodRender Decompile(CSharpMethodEntry entry, SourceCache sources)
    {
        if (!entry.HasBody)
            return new CSharpMethodRender(CSharpMethodRenderState.NoBody, ["/* no method body */"], DecompilationFidelity.IlOnly);

        var source = sources.Open(entry.Path);
        var function = IrImporter.Import(source, entry.TypeFullName, entry.MethodName, entry.OverloadIndex, publicOnly: false);
        if (function is null)
            return new CSharpMethodRender(CSharpMethodRenderState.NoBody, ["/* no method body */"], DecompilationFidelity.IlOnly);

        var result = CSharpPrinter.PrintRaised(function);
        return result.Succeeded
            ? new CSharpMethodRender(CSharpMethodRenderState.Body, SplitLines(result.Output), result.Fidelity)
            : new CSharpMethodRender(
                CSharpMethodRenderState.Failed,
                [$"/* decompile failed: {string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message))} */"],
                result.Fidelity);
    }

    static IEnumerable<CSharpMethodEntry> EnumerateMethods(
        MetadataSource source,
        string path,
        string stableAssemblyKey,
        bool includeNonPublic,
        IReadOnlySet<string>? typeFilters,
        IReadOnlyDictionary<int, MemberAnchor> anchorsByToken)
    {
        var reader = source.Reader;
        var typeDefinitionsByName = BuildTypeDefinitionMap(reader);
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            string typeFullName = reader.GetFullTypeName(type);
            string typeKey = TypeIdentityKey(reader, typeHandle);
            string typeDisplay = typeFullName;
            if (!includeNonPublic && !IsVisibleSurfaceType(reader, typeHandle, typeDefinitionsByName))
                continue;
            if (!MatchesTypeFilters(typeFullName, typeFilters))
                continue;

            var explicitImplementationBodies = GetExplicitImplementationBodies(reader, type, typeDefinitionsByName);
            var nameOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                string methodName = reader.GetString(method.Name);
                int overloadIndex = nameOrdinals.GetValueOrDefault(methodName);
                nameOrdinals[methodName] = overloadIndex + 1;

                if (!includeNonPublic && !IsPublicSurface(method) && !explicitImplementationBodies.Contains(methodHandle))
                    continue;

                var signature = method.DecodeSignature(TypeRefDecoder.Instance, GenericScope.Empty);
                var apiSignature = method.DecodeSignature(
                    TypeRefDecoder.Instance,
                    new GenericScope(TypeParameterNames(reader, type), MethodParameterNames(reader, method)));
                string returnType = CanonicalTypeName(signature.ReturnType);
                var parameters = signature.ParameterTypes.Select(CanonicalTypeName).ToImmutableArray();
                int genericArity = method.GetGenericParameters().Count;
                string methodGeneric = GenericParameterList(genericArity, isMethod: true);
                string selectorName = ResearchDiff.ResearchMemberSelector.ForMetadataName(methodName, IsExtensionMethod(reader, type, method));
                string returnSuffix = IsConversionOperator(methodName) ? $"~{returnType}" : "";
                string canonicalName = CanonicalMemberName(methodName);
                string rawKey = $"M:{typeKey}.{canonicalName}{methodGeneric}({string.Join(",", parameters)}){returnSuffix}";
                string apiMemberName = ApiMemberName(reader, methodName, method);
                string anchorCanonical = $"M:{ApiTypeName(reader, typeHandle)}.{apiMemberName}{ApiParameterList(apiSignature.ParameterTypes)}";
                int methodToken = MetadataTokens.GetToken(methodHandle);
                var moduleVersionId = ModuleVersionId(reader);
                var anchor = anchorsByToken.TryGetValue(methodToken, out var metadataAnchor)
                    ? metadataAnchor
                    : CreateMemberAnchor(ApiTypeName(reader, typeHandle), selectorName, apiMemberName, anchorCanonical);
                string displayName = methodName == ".ctor" ? "#ctor" : methodName;
                string display = $"{typeDisplay}.{displayName}{GenericAritySuffix(genericArity)}({string.Join(", ", parameters)})";
                yield return new CSharpMethodEntry(
                    path,
                    source.AssemblyName,
                    stableAssemblyKey,
                    anchor,
                    new MetadataTypeRef(source.AssemblyName, moduleVersionId, MetadataTokens.GetToken(typeHandle)),
                    new MetadataMemberRef(source.AssemblyName, moduleVersionId, methodToken),
                    rawKey,
                    $"{stableAssemblyKey}|{rawKey}",
                    DuplicateDiscriminator(reader, method),
                    display,
                    typeFullName,
                    methodName,
                    overloadIndex,
                    method.RelativeVirtualAddress != 0,
                    BodyFingerprint(source, method));
            }
        }
    }

    static IReadOnlyDictionary<int, MemberAnchor> MemberAnchorsByToken(string path)
    {
        var surface = AssemblyReader.ExtractApiSurface(path, includeAll: true);
        if (surface is null)
            return new Dictionary<int, MemberAnchor>();

        var anchors = new Dictionary<int, MemberAnchor>();
        foreach (var type in surface.Types)
        {
            foreach (var member in type.Members)
            {
                var anchor = ApiMemberIdentity.GetMemberAnchor(type, member);
                if (member.MetadataToken is { } token)
                    anchors.TryAdd(token, anchor);
                if (member.GetterToken is { } getter)
                    anchors.TryAdd(getter, anchor);
                if (member.SetterToken is { } setter)
                    anchors.TryAdd(setter, anchor);
            }
        }

        return anchors;
    }

    static string GenericAritySuffix(int arity)
        => arity == 0 ? "" : $"`{arity}";

    static Guid ModuleVersionId(MetadataReader reader)
    {
        var module = reader.GetModuleDefinition();
        return reader.GetGuid(module.Mvid);
    }

    static string CanonicalMemberName(string methodName)
        => methodName == ".ctor" ? "#ctor" : methodName;

    static string ApiTypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var genericNames = type.GetGenericParameters()
            .Select(parameter => reader.GetString(reader.GetGenericParameter(parameter).Name))
            .ToArray();
        string name = reader.GetString(type.Name);
        int tick = name.IndexOf('`');
        string simple = tick < 0 ? name : name[..tick];
        if (genericNames.Length > 0)
            simple += $"<{string.Join(",", genericNames)}>";
        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil)
            return $"{ApiTypeName(reader, declaring)}.{simple}";
        string ns = reader.GetString(type.Namespace);
        return string.IsNullOrEmpty(ns) ? simple : $"{ns}.{simple}";
    }

    static string ApiMemberName(MetadataReader reader, string methodName, MethodDefinition method)
    {
        if (methodName == ".ctor")
            return "#ctor";
        var genericNames = MethodParameterNames(reader, method)
            .ToArray();
        return genericNames.Length == 0 ? methodName : $"{methodName}<{string.Join(",", genericNames)}>";
    }

    static string ApiParameterList(IEnumerable<TypeRef> parameterTypes)
        => $"({string.Join(",", parameterTypes.Select(ApiTypeName))})";

    static string ApiTypeName(TypeRef type)
        => type.Kind switch
        {
            TypeRefKind.Definition => type.Namespace.Length == 0
                ? type.Name.Replace("+", ".", StringComparison.Ordinal)
                : $"{type.Namespace}.{type.Name.Replace("+", ".", StringComparison.Ordinal)}",
            TypeRefKind.GenericInstance => $"{ApiTypeName(type.ElementType!)}<{string.Join(",", type.TypeArguments.Select(ApiTypeName))}>",
            TypeRefKind.SzArray => $"{ApiTypeName(type.ElementType!)}[]",
            TypeRefKind.Array => $"{ApiTypeName(type.ElementType!)}[{(type.Rank == 1 ? "*" : new string(',', type.Rank - 1))}]",
            TypeRefKind.ByRef => $"{ApiTypeName(type.ElementType!)}&",
            TypeRefKind.Pointer => $"{ApiTypeName(type.ElementType!)}*",
            TypeRefKind.Pinned => $"pinned {ApiTypeName(type.ElementType!)}",
            TypeRefKind.GenericParameter or TypeRefKind.MethodGenericParameter
                => type.GenericParameterName.Length == 0 ? $"!{type.GenericParameterIndex}" : type.GenericParameterName,
            TypeRefKind.FunctionPointer => $"delegate*<{string.Join(",", type.TypeArguments.Select(ApiTypeName).Append(ApiTypeName(type.ElementType!)))}>",
            _ => $"<unsupported:{type.UnsupportedReason}>",
        };

    static ImmutableArray<string> TypeParameterNames(MetadataReader reader, TypeDefinition type)
        => ParameterNames(reader, type.GetGenericParameters());

    static ImmutableArray<string> MethodParameterNames(MetadataReader reader, MethodDefinition method)
        => ParameterNames(reader, method.GetGenericParameters());

    static ImmutableArray<string> ParameterNames(MetadataReader reader, GenericParameterHandleCollection handles)
    {
        if (handles.Count == 0)
            return [];
        var names = ImmutableArray.CreateBuilder<string>(handles.Count);
        foreach (var handle in handles)
            names.Add(reader.GetString(reader.GetGenericParameter(handle).Name));
        return names.MoveToImmutable();
    }

    static bool IsExtensionMethod(MetadataReader reader, TypeDefinition type, MethodDefinition method)
        => type.Attributes.HasFlag(TypeAttributes.Abstract)
           && type.Attributes.HasFlag(TypeAttributes.Sealed)
           && method.Attributes.HasFlag(MethodAttributes.Static)
           && AttributeReader.HasExtensionAttribute(reader, type.GetCustomAttributes())
           && AttributeReader.HasExtensionAttribute(reader, method.GetCustomAttributes());

    static bool IsConversionOperator(string methodName)
        => methodName is "op_Implicit" or "op_Explicit" or "op_CheckedExplicit";

    static string TypeIdentityKey(MetadataReader reader, TypeDefinitionHandle handle)
        => reader.GetFullTypeName(reader.GetTypeDefinition(handle));

    static string CanonicalTypeName(TypeRef type)
        => type.Kind switch
        {
            TypeRefKind.Definition => type.Namespace.Length == 0
                ? type.Name.Replace("+", ".", StringComparison.Ordinal)
                : $"{type.Namespace}.{type.Name.Replace("+", ".", StringComparison.Ordinal)}",
            TypeRefKind.GenericInstance => $"{CanonicalTypeName(type.ElementType!)}<{string.Join(",", type.TypeArguments.Select(CanonicalTypeName))}>",
            TypeRefKind.SzArray => $"{CanonicalTypeName(type.ElementType!)}[]",
            TypeRefKind.Array => $"{CanonicalTypeName(type.ElementType!)}[{(type.Rank == 1 ? "*" : new string(',', type.Rank - 1))}]",
            TypeRefKind.ByRef => $"{CanonicalTypeName(type.ElementType!)}&",
            TypeRefKind.Pointer => $"{CanonicalTypeName(type.ElementType!)}*",
            TypeRefKind.Pinned => $"pinned {CanonicalTypeName(type.ElementType!)}",
            TypeRefKind.GenericParameter => $"!{type.GenericParameterIndex}",
            TypeRefKind.MethodGenericParameter => $"!!{type.GenericParameterIndex}",
            TypeRefKind.FunctionPointer => CanonicalFunctionPointer(type),
            _ => $"<unsupported:{type.UnsupportedReason}>",
        };

    static string CanonicalFunctionPointer(TypeRef type)
        => $"delegate*<{string.Join(",", type.TypeArguments.Select(CanonicalTypeName).Append(CanonicalTypeName(type.ElementType!)))}>";

    static string GenericParameterList(int arity, bool isMethod)
    {
        if (arity == 0)
            return "";
        var prefix = isMethod ? "!!" : "!";
        return $"<{string.Join(",", Enumerable.Range(0, arity).Select(index => $"{prefix}{index}"))}>";
    }

    static MemberAnchor CreateMemberAnchor(string typeFullName, string selectorName, string memberName, string canonicalSignature)
        => new(
            $"{selectorName}~{MemberAnchor.ComputeFingerprint(canonicalSignature)}",
            canonicalSignature,
            MemberAnchor.ComputeFingerprint(canonicalSignature),
            typeFullName,
            memberName);

    static string DuplicateDiscriminator(MetadataReader reader, MethodDefinition method)
    {
        var builder = new StringBuilder();
        builder.Append("sig:").Append(System.Convert.ToHexString(reader.GetBlobBytes(method.Signature)));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return System.Convert.ToHexString(hash).ToLowerInvariant()[..10];
    }

    static string StableAssemblyKey(MetadataSource source)
    {
        var reader = source.Reader;
        if (!reader.IsAssembly)
            return source.AssemblyName;

        var assembly = reader.GetAssemblyDefinition();
        string name = reader.GetString(assembly.Name);
        string culture = reader.GetString(assembly.Culture);
        string publicKeyToken = PublicKeyToken(reader.GetBlobBytes(assembly.PublicKey));
        return $"{name}|{(string.IsNullOrEmpty(culture) ? "neutral" : culture)}|{publicKeyToken}";
    }

    static string PublicKeyToken(byte[] publicKey)
    {
        if (publicKey.Length == 0)
            return "";
        Span<byte> hash = stackalloc byte[20];
        SHA1.HashData(publicKey, hash);
        Span<byte> token = stackalloc byte[8];
        hash[^8..].CopyTo(token);
        token.Reverse();
        return System.Convert.ToHexString(token).ToLowerInvariant();
    }

    static string BodyFingerprint(MetadataSource source, MethodDefinition method)
    {
        var reader = source.Reader;
        var builder = new StringBuilder();
        builder.Append((int)method.Attributes).Append('|')
            .Append((int)method.ImplAttributes).Append('|')
            .Append(MethodSignatureFingerprint(reader, method)).Append('|');

        if (method.RelativeVirtualAddress == 0)
        {
            var noBodyHash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.Append("<no-body>").ToString()));
            return System.Convert.ToHexString(noBodyHash);
        }

        var body = source.Pe.GetMethodBody(method.RelativeVirtualAddress);
        builder
            .Append(body.MaxStack).Append('|')
            .Append(body.LocalVariablesInitialized).Append('|')
            .Append(StandaloneSignatureFingerprint(reader, body.LocalSignature)).Append('|');
        var decoded = MethodInstructions.Decode(body);
        if (decoded.IsComplete)
        {
            foreach (var instruction in decoded.Instructions)
            {
                builder.Append(instruction.Offset).Append(':')
                    .Append(instruction.OpCode).Append(':')
                    .Append(instruction.Operand).Append(':')
                    .Append(OperandFingerprint(reader, instruction)).Append(';');
            }
        }
        else
        {
            builder.Append(System.Convert.ToHexString(body.GetILBytes() ?? []));
        }

        foreach (var region in body.ExceptionRegions)
        {
            builder.Append('|')
                .Append(region.Kind).Append(':')
                .Append(region.TryOffset).Append(':')
                .Append(region.TryLength).Append(':')
                .Append(region.HandlerOffset).Append(':')
                .Append(region.HandlerLength).Append(':')
                .Append(region.FilterOffset).Append(':')
                .Append(EntityFingerprint(reader, region.CatchType));
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return System.Convert.ToHexString(hash);
    }

    static string OperandFingerprint(MetadataReader reader, DecodedInstruction instruction)
        => instruction.Operand switch
        {
            OperandKind.InlineString => $"string:{reader.GetUserString(MetadataTokens.UserStringHandle((int)instruction.OperandValue))}",
            OperandKind.InlineMethod or OperandKind.InlineField or OperandKind.InlineType or OperandKind.InlineTok
                => EntityFingerprint(reader, MetadataTokens.EntityHandle((int)instruction.OperandValue)),
            OperandKind.InlineSig => StandaloneSignatureFingerprint(reader, (StandaloneSignatureHandle)MetadataTokens.EntityHandle((int)instruction.OperandValue)),
            OperandKind.InlineSwitch => string.Join(",", instruction.BranchTargets),
            _ => instruction.OperandValue.ToString(CultureInfo.InvariantCulture),
        };

    static string EntityFingerprint(MetadataReader reader, EntityHandle handle)
        => handle.Kind switch
        {
            HandleKind.TypeDefinition => $"type-def:{reader.GetFullTypeName(reader.GetTypeDefinition((TypeDefinitionHandle)handle))}",
            HandleKind.TypeReference => $"type-ref:{reader.GetFullTypeName(reader.GetTypeReference((TypeReferenceHandle)handle))}",
            HandleKind.TypeSpecification => $"type-spec:{CanonicalTypeName(reader.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(TypeRefDecoder.Instance, GenericScope.Empty))}",
            HandleKind.MethodDefinition => MethodDefinitionFingerprint(reader, (MethodDefinitionHandle)handle),
            HandleKind.MemberReference => MemberReferenceFingerprint(reader, (MemberReferenceHandle)handle),
            HandleKind.MethodSpecification => MethodSpecificationFingerprint(reader, (MethodSpecificationHandle)handle),
            HandleKind.FieldDefinition => FieldDefinitionFingerprint(reader, (FieldDefinitionHandle)handle),
            _ => $"{handle.Kind}:{MetadataTokens.GetToken(handle).ToString(CultureInfo.InvariantCulture)}",
        };

    static string MethodDefinitionFingerprint(MetadataReader reader, MethodDefinitionHandle handle)
    {
        var method = reader.GetMethodDefinition(handle);
        return $"method-def:{reader.GetFullTypeName(reader.GetTypeDefinition(method.GetDeclaringType()))}.{reader.GetString(method.Name)}:{MethodSignatureFingerprint(reader, method)}";
    }

    static string MemberReferenceFingerprint(MetadataReader reader, MemberReferenceHandle handle)
    {
        var member = reader.GetMemberReference(handle);
        string signature = member.GetKind() == MemberReferenceKind.Method
            ? MethodSignatureFingerprint(member.DecodeMethodSignature(TypeRefDecoder.Instance, GenericScope.Empty))
            : CanonicalTypeName(member.DecodeFieldSignature(TypeRefDecoder.Instance, GenericScope.Empty));
        return $"member-ref:{MemberParentFingerprint(reader, member.Parent)}.{reader.GetString(member.Name)}:{signature}";
    }

    static string MethodSpecificationFingerprint(MetadataReader reader, MethodSpecificationHandle handle)
    {
        var spec = reader.GetMethodSpecification(handle);
        var typeArguments = spec.DecodeSignature(TypeRefDecoder.Instance, GenericScope.Empty);
        return $"method-spec:{EntityFingerprint(reader, spec.Method)}<{string.Join(",", typeArguments.Select(CanonicalTypeName))}>";
    }

    static string FieldDefinitionFingerprint(MetadataReader reader, FieldDefinitionHandle handle)
    {
        var field = reader.GetFieldDefinition(handle);
        return $"field-def:{reader.GetFullTypeName(reader.GetTypeDefinition(field.GetDeclaringType()))}.{reader.GetString(field.Name)}:{CanonicalTypeName(field.DecodeSignature(TypeRefDecoder.Instance, GenericScope.Empty))}";
    }

    static string MemberParentFingerprint(MetadataReader reader, EntityHandle handle)
        => handle.Kind switch
        {
            HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification => EntityFingerprint(reader, handle),
            _ => $"{handle.Kind}:{MetadataTokens.GetToken(handle).ToString(CultureInfo.InvariantCulture)}",
        };

    static string MethodSignatureFingerprint(MetadataReader reader, MethodDefinition method)
        => MethodSignatureFingerprint(method.DecodeSignature(TypeRefDecoder.Instance, GenericScope.Empty));

    static string MethodSignatureFingerprint(System.Reflection.Metadata.MethodSignature<TypeRef> signature)
        => $"{(signature.Header.IsInstance ? "instance" : "static")}:{signature.GenericParameterCount}:{CanonicalTypeName(signature.ReturnType)}({string.Join(",", signature.ParameterTypes.Select(CanonicalTypeName))})";

    static string StandaloneSignatureFingerprint(MetadataReader reader, StandaloneSignatureHandle handle)
    {
        if (handle.IsNil)
            return "";

        var signature = reader.GetStandaloneSignature(handle);
        try
        {
            var locals = signature.DecodeLocalSignature(TypeRefDecoder.Instance, GenericScope.Empty);
            return $"locals({string.Join(",", locals.Select(CanonicalTypeName))})";
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            try
            {
                return $"method({MethodSignatureFingerprint(signature.DecodeMethodSignature(TypeRefDecoder.Instance, GenericScope.Empty))})";
            }
            catch (Exception inner) when (inner is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                return System.Convert.ToHexString(reader.GetBlobBytes(signature.Signature));
            }
        }
    }

    static IReadOnlyDictionary<string, TypeDefinitionHandle> BuildTypeDefinitionMap(MetadataReader reader)
    {
        var map = new Dictionary<string, TypeDefinitionHandle>(StringComparer.Ordinal);
        foreach (var handle in reader.TypeDefinitions)
            map.TryAdd(reader.GetFullTypeName(reader.GetTypeDefinition(handle)), handle);
        return map;
    }

    static bool IsVisibleSurfaceType(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        IReadOnlyDictionary<string, TypeDefinitionHandle> typeDefinitionsByName)
    {
        var type = reader.GetTypeDefinition(handle);
        bool visible = (type.Attributes & TypeAttributes.VisibilityMask) is
            TypeAttributes.Public or TypeAttributes.NestedPublic or TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem;
        if (!visible)
            return false;
        var declaring = type.GetDeclaringType();
        return declaring.IsNil || IsVisibleSurfaceType(reader, declaring, typeDefinitionsByName);
    }

    static bool IsPublicSurface(MethodDefinition method)
        => (method.Attributes & MethodAttributes.MemberAccessMask) is
            MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem;

    static HashSet<MethodDefinitionHandle> GetExplicitImplementationBodies(
        MetadataReader reader,
        TypeDefinition type,
        IReadOnlyDictionary<string, TypeDefinitionHandle> typeDefinitionsByName)
    {
        HashSet<MethodDefinitionHandle> handles = [];
        foreach (var implementationHandle in type.GetMethodImplementations())
        {
            var implementation = reader.GetMethodImplementation(implementationHandle);
            if (implementation.MethodBody.Kind == HandleKind.MethodDefinition
                && IsVisibleImplementedMember(reader, implementation.MethodDeclaration, typeDefinitionsByName))
            {
                handles.Add((MethodDefinitionHandle)implementation.MethodBody);
            }
        }

        return handles;
    }

    static bool IsVisibleImplementedMember(
        MetadataReader reader,
        EntityHandle declaration,
        IReadOnlyDictionary<string, TypeDefinitionHandle> typeDefinitionsByName)
        => declaration.Kind switch
        {
            HandleKind.MethodDefinition => IsVisibleImplementedMethodDefinition(reader, (MethodDefinitionHandle)declaration, typeDefinitionsByName),
            HandleKind.MemberReference => IsVisibleMemberReferenceParent(reader, reader.GetMemberReference((MemberReferenceHandle)declaration).Parent, typeDefinitionsByName),
            _ => false,
        };

    static bool IsVisibleImplementedMethodDefinition(
        MetadataReader reader,
        MethodDefinitionHandle handle,
        IReadOnlyDictionary<string, TypeDefinitionHandle> typeDefinitionsByName)
    {
        var method = reader.GetMethodDefinition(handle);
        var declaring = method.GetDeclaringType();
        return IsVisibleSurfaceType(reader, declaring, typeDefinitionsByName)
            && (reader.GetTypeDefinition(declaring).Attributes & TypeAttributes.ClassSemanticsMask) == TypeAttributes.Interface;
    }

    static bool IsVisibleMemberReferenceParent(
        MetadataReader reader,
        EntityHandle parent,
        IReadOnlyDictionary<string, TypeDefinitionHandle> typeDefinitionsByName)
        => parent.Kind switch
        {
            HandleKind.TypeDefinition => IsVisibleSurfaceType(reader, (TypeDefinitionHandle)parent, typeDefinitionsByName)
                && (reader.GetTypeDefinition((TypeDefinitionHandle)parent).Attributes & TypeAttributes.ClassSemanticsMask) == TypeAttributes.Interface,
            HandleKind.TypeReference => IsVisibleTypeReferenceParent(reader, (TypeReferenceHandle)parent, typeDefinitionsByName),
            HandleKind.TypeSpecification => IsVisibleTypeSpecificationParent(reader, (TypeSpecificationHandle)parent, typeDefinitionsByName),
            _ => false,
        };

    static bool IsVisibleTypeReferenceParent(
        MetadataReader reader,
        TypeReferenceHandle handle,
        IReadOnlyDictionary<string, TypeDefinitionHandle> typeDefinitionsByName)
    {
        var reference = reader.GetTypeReference(handle);
        if (reference.ResolutionScope.Kind == HandleKind.AssemblyReference)
            return true;

        string metadataName = reader.GetFullTypeName(reference);
        if (typeDefinitionsByName.TryGetValue(metadataName, out var typeHandle))
            return IsVisibleInterfaceDefinition(reader, typeHandle, typeDefinitionsByName);

        return false;
    }

    static bool IsVisibleTypeSpecificationParent(
        MetadataReader reader,
        TypeSpecificationHandle handle,
        IReadOnlyDictionary<string, TypeDefinitionHandle> typeDefinitionsByName)
    {
        var type = reader.GetTypeSpecification(handle).DecodeSignature(TypeRefDecoder.Instance, GenericScope.Empty);
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType : type;
        if (definition is not { Kind: TypeRefKind.Definition })
            return true;

        string currentAssembly = reader.IsAssembly ? reader.GetString(reader.GetAssemblyDefinition().Name) : "";
        if (definition.Assembly.Length > 0 && definition.Assembly != currentAssembly)
            return true;

        string metadataName = definition.Namespace.Length == 0
            ? definition.Name.Replace("+", ".", StringComparison.Ordinal)
            : $"{definition.Namespace}.{definition.Name.Replace("+", ".", StringComparison.Ordinal)}";
        if (typeDefinitionsByName.TryGetValue(metadataName, out var typeHandle))
            return IsVisibleInterfaceDefinition(reader, typeHandle, typeDefinitionsByName);

        return true;
    }

    static bool IsVisibleInterfaceDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        IReadOnlyDictionary<string, TypeDefinitionHandle> typeDefinitionsByName)
    {
        var type = reader.GetTypeDefinition(handle);
        return IsVisibleSurfaceType(reader, handle, typeDefinitionsByName)
            && (type.Attributes & TypeAttributes.ClassSemanticsMask) == TypeAttributes.Interface;
    }

    static bool MatchesTypeFilters(string typeFullName, IReadOnlySet<string>? filters)
    {
        if (filters is null || filters.Count == 0)
            return true;
        foreach (string filter in filters)
        {
            if (MatchesTypeFilter(typeFullName, filter))
                return true;
        }

        return false;
    }

    static bool MatchesTypeFilter(string typeFullName, string filter)
    {
        if (filter.Contains('*') || filter.Contains('?'))
            return GlobMatches(typeFullName, filter);
        return string.Equals(typeFullName, filter, StringComparison.OrdinalIgnoreCase)
            || typeFullName.EndsWith("." + filter, StringComparison.OrdinalIgnoreCase)
            || typeFullName.Contains("." + filter + ".", StringComparison.OrdinalIgnoreCase);
    }

    static bool GlobMatches(string value, string pattern)
    {
        int valueIndex = 0;
        int patternIndex = 0;
        int starIndex = -1;
        int retryIndex = 0;

        while (valueIndex < value.Length)
        {
            if (patternIndex < pattern.Length
                && (pattern[patternIndex] == '?' || char.ToUpperInvariant(pattern[patternIndex]) == char.ToUpperInvariant(value[valueIndex])))
            {
                valueIndex++;
                patternIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex++;
                retryIndex = valueIndex;
            }
            else if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                valueIndex = ++retryIndex;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            patternIndex++;
        return patternIndex == pattern.Length;
    }

    static void AddWholeMethod(
        ImmutableArray<CSharpDiffRow>.Builder rows,
        CSharpMethodEntry entry,
        ref int hunkId,
        CSharpDiffKind kind)
    {
        int hunk = hunkId++;
        string text = kind == CSharpDiffKind.Add ? "/* method added */" : "/* method removed */";
        string changeId = kind == CSharpDiffKind.Add ? "csharp.method.added" : "csharp.method.removed";
        string message = kind == CSharpDiffKind.Add ? "Added C# method." : "Removed C# method.";
        rows.Add(CreateRow(entry, hunk, kind, null, "NotRun", text, changeId, message, operationKind: CSharpDiffOperationKind.Method));
    }

    static void AddLineDiffRows(
        ImmutableArray<CSharpDiffRow>.Builder rows,
        ImmutableArray<CSharpDiffFailureRow>.Builder failureRows,
        CSharpMethodEntry oldEntry,
        CSharpMethodEntry newEntry,
        CSharpMethodRender oldRender,
        CSharpMethodRender newRender,
        ref int hunkId)
    {
        var oldLines = oldRender.Lines;
        var newLines = newRender.Lines;
        if (oldRender.State == newRender.State && oldLines.SequenceEqual(newLines))
            return;

        if (oldRender.State != CSharpMethodRenderState.Body || newRender.State != CSharpMethodRenderState.Body)
        {
            AddRenderStateRows(rows, failureRows, oldEntry, newEntry, oldRender, newRender, ref hunkId);
            return;
        }

        if (oldLines.Length > MaxLcsLines || newLines.Length > MaxLcsLines)
        {
            int hunk = hunkId++;
            string message = $"/* C# diff skipped: old body has {oldLines.Length} lines, new body has {newLines.Length} lines; limit is {MaxLcsLines} */";
            failureRows.Add(CreateFailureRow(
                newEntry,
                CSharpDiffFailureKind.BodyDiffSkipped,
                "Skipped C# body diff.",
                side: null,
                detail: $"old body has {oldLines.Length} lines, new body has {newLines.Length} lines; limit is {MaxLcsLines}",
                hunkId: hunk));
            rows.Add(CreateRow(oldEntry, hunk, CSharpDiffKind.Remove, null, oldRender.Fidelity.ToString(), message, "csharp.body-diff.skipped", "Skipped C# body diff; old body exceeds line limit.", operationKind: CSharpDiffOperationKind.BodyDiffSkipped));
            rows.Add(CreateRow(newEntry, hunk, CSharpDiffKind.Add, null, newRender.Fidelity.ToString(), message, "csharp.body-diff.skipped", "Skipped C# body diff; new body exceeds line limit.", operationKind: CSharpDiffOperationKind.BodyDiffSkipped));
            return;
        }

        var lcs = LongestCommonSubsequence(oldLines, newLines);
        int oldIndex = 0;
        int newIndex = 0;
        foreach (var (nextOld, nextNew) in lcs)
        {
            AddUnmatched(rows, oldEntry, newEntry, oldRender, oldIndex, nextOld, newRender, newIndex, nextNew, ref hunkId);
            oldIndex = nextOld + 1;
            newIndex = nextNew + 1;
        }

        AddUnmatched(rows, oldEntry, newEntry, oldRender, oldIndex, oldLines.Length, newRender, newIndex, newLines.Length, ref hunkId);
    }

    static void AddUnmatched(
        ImmutableArray<CSharpDiffRow>.Builder rows,
        CSharpMethodEntry oldEntry,
        CSharpMethodEntry newEntry,
        CSharpMethodRender oldRender,
        int oldStart,
        int oldEnd,
        CSharpMethodRender newRender,
        int newStart,
        int newEnd,
        ref int hunkId)
    {
        if (oldStart == oldEnd && newStart == newEnd)
            return;

        int hunk = hunkId++;
        var oldOperations = BuildSemanticOperations(oldRender.Lines, oldStart, oldEnd);
        var newOperations = BuildSemanticOperations(newRender.Lines, newStart, newEnd);
        AddSemanticRows(rows, oldEntry, newEntry, hunk, oldRender, newRender, oldOperations, newOperations);
        foreach (var operation in oldOperations.Where(operation => operation.Kind == CSharpDiffOperationKind.Line))
            rows.Add(CreateRow(oldEntry, hunk, CSharpDiffKind.Remove, operation.Line, oldRender.Fidelity.ToString(), operation.Text));
        foreach (var operation in newOperations.Where(operation => operation.Kind == CSharpDiffOperationKind.Line))
            rows.Add(CreateRow(newEntry, hunk, CSharpDiffKind.Add, operation.Line, newRender.Fidelity.ToString(), operation.Text));
    }

    static void AddRenderStateRows(
        ImmutableArray<CSharpDiffRow>.Builder rows,
        ImmutableArray<CSharpDiffFailureRow>.Builder failureRows,
        CSharpMethodEntry oldEntry,
        CSharpMethodEntry newEntry,
        CSharpMethodRender oldRender,
        CSharpMethodRender newRender,
        ref int hunkId)
    {
        int hunk = hunkId++;
        if (oldRender.State is CSharpMethodRenderState.Body && newRender.State is CSharpMethodRenderState.NoBody)
        {
            failureRows.Add(CreateFailureRow(
                newEntry,
                CSharpDiffFailureKind.NewBodyMissing,
                "New method has no C# body.",
                side: "new",
                detail: newRender.Lines[0],
                hunkId: hunk));
            rows.Add(CreateRow(oldEntry, hunk, CSharpDiffKind.Remove, null, oldRender.Fidelity.ToString(), "/* method body removed */", "csharp.method.body-removed", "Removed C# method body.", operationKind: CSharpDiffOperationKind.MethodBody));
            return;
        }

        if (oldRender.State is CSharpMethodRenderState.NoBody && newRender.State is CSharpMethodRenderState.Body)
        {
            failureRows.Add(CreateFailureRow(
                oldEntry,
                CSharpDiffFailureKind.OldBodyMissing,
                "Old method has no C# body.",
                side: "old",
                detail: oldRender.Lines[0],
                hunkId: hunk));
            rows.Add(CreateRow(newEntry, hunk, CSharpDiffKind.Add, null, newRender.Fidelity.ToString(), "/* method body added */", "csharp.method.body-added", "Added C# method body.", operationKind: CSharpDiffOperationKind.MethodBody));
            return;
        }

        if (oldRender.State is CSharpMethodRenderState.Failed)
        {
            failureRows.Add(CreateFailureRow(
                oldEntry,
                CSharpDiffFailureKind.OldDecompileFailure,
                "Old method body decompilation failed.",
                side: "old",
                detail: oldRender.Lines[0],
                hunkId: hunk));
            rows.Add(CreateRow(oldEntry, hunk, CSharpDiffKind.Remove, null, oldRender.Fidelity.ToString(), oldRender.Lines[0], "csharp.decompile.failed", "Old method body decompilation failed.", operationKind: CSharpDiffOperationKind.DecompileFailure));
        }

        if (newRender.State is CSharpMethodRenderState.Failed)
        {
            failureRows.Add(CreateFailureRow(
                newEntry,
                CSharpDiffFailureKind.NewDecompileFailure,
                "New method body decompilation failed.",
                side: "new",
                detail: newRender.Lines[0],
                hunkId: hunk));
            rows.Add(CreateRow(newEntry, hunk, CSharpDiffKind.Add, null, newRender.Fidelity.ToString(), newRender.Lines[0], "csharp.decompile.failed", "New method body decompilation failed.", operationKind: CSharpDiffOperationKind.DecompileFailure));
        }

        if (oldRender.State is CSharpMethodRenderState.NoBody)
        {
            failureRows.Add(CreateFailureRow(
                oldEntry,
                CSharpDiffFailureKind.OldBodyMissing,
                "Old method has no C# body.",
                side: "old",
                detail: oldRender.Lines[0],
                hunkId: hunk));
            rows.Add(CreateRow(oldEntry, hunk, CSharpDiffKind.Remove, null, oldRender.Fidelity.ToString(), oldRender.Lines[0], "csharp.method.no-body", "Old method has no C# body.", operationKind: CSharpDiffOperationKind.MethodBody));
        }

        if (newRender.State is CSharpMethodRenderState.NoBody)
        {
            failureRows.Add(CreateFailureRow(
                newEntry,
                CSharpDiffFailureKind.NewBodyMissing,
                "New method has no C# body.",
                side: "new",
                detail: newRender.Lines[0],
                hunkId: hunk));
            rows.Add(CreateRow(newEntry, hunk, CSharpDiffKind.Add, null, newRender.Fidelity.ToString(), newRender.Lines[0], "csharp.method.no-body", "New method has no C# body.", operationKind: CSharpDiffOperationKind.MethodBody));
        }

        if (oldRender.State is CSharpMethodRenderState.Body)
            rows.Add(CreateRow(oldEntry, hunk, CSharpDiffKind.Remove, null, oldRender.Fidelity.ToString(), "/* method body removed */", "csharp.method.body-removed", "Removed C# method body.", operationKind: CSharpDiffOperationKind.MethodBody));
        if (newRender.State is CSharpMethodRenderState.Body)
            rows.Add(CreateRow(newEntry, hunk, CSharpDiffKind.Add, null, newRender.Fidelity.ToString(), "/* method body added */", "csharp.method.body-added", "Added C# method body.", operationKind: CSharpDiffOperationKind.MethodBody));
    }

    static CSharpDiffFailureRow CreateFailureRow(
        CSharpMethodEntry entry,
        CSharpDiffFailureKind kind,
        string message,
        string? side,
        string? detail,
        int hunkId)
        => new(
            entry.StableAssemblyKey,
            entry.StableMemberKey,
            entry.Anchor,
            entry.TypeRef,
            entry.MemberRef,
            entry.Display,
            kind,
            message,
            side,
            detail,
            hunkId);

    static CSharpDiffRow CreateRow(
        CSharpMethodEntry entry,
        int hunk,
        CSharpDiffKind kind,
        int? line,
        string fidelity,
        string text,
        string? changeId = null,
        string? message = null,
        CSharpDiffOperationKind operationKind = CSharpDiffOperationKind.Line)
        => CreateRow(entry, hunk, kind, line, fidelity, text, changeId, message, oldValue: null, newValue: null, operationKind);

    static CSharpDiffRow CreateRow(
        CSharpMethodEntry entry,
        int hunk,
        CSharpDiffKind kind,
        int? line,
        string fidelity,
        string text,
        string? changeId,
        string? message,
        string? oldValue,
        string? newValue,
        CSharpDiffOperationKind operationKind = CSharpDiffOperationKind.Line)
    {
        changeId ??= kind switch
        {
            CSharpDiffKind.Add => "csharp.line.added",
            CSharpDiffKind.Remove => "csharp.line.removed",
            _ => "csharp.line.changed",
        };
        message ??= kind switch
        {
            CSharpDiffKind.Add => $"Added C# line '{text}'",
            CSharpDiffKind.Remove => $"Removed C# line '{text}'",
            _ => $"Changed C# line '{text}'",
        };
        var oldOperation = kind is CSharpDiffKind.Remove or CSharpDiffKind.Changed
            ? new CSharpDiffOperation(operationKind, oldValue ?? text)
            : null;
        var newOperation = kind is CSharpDiffKind.Add or CSharpDiffKind.Changed
            ? new CSharpDiffOperation(operationKind, newValue ?? text)
            : null;
        return new CSharpDiffRow(
            entry.StableAssemblyKey,
            entry.StableMemberKey,
            entry.Anchor,
            entry.TypeRef,
            entry.MemberRef,
            entry.Display,
            changeId,
            message,
            hunk,
            kind,
            line,
            line is null ? null : $"line:{line.Value}",
            fidelity,
            text,
            oldValue,
            newValue,
            oldOperation,
            newOperation);
    }

    static void AddSemanticRows(
        ImmutableArray<CSharpDiffRow>.Builder rows,
        CSharpMethodEntry oldEntry,
        CSharpMethodEntry newEntry,
        int hunk,
        CSharpMethodRender oldRender,
        CSharpMethodRender newRender,
        ImmutableArray<CSharpSemanticOperation> oldOperations,
        ImmutableArray<CSharpSemanticOperation> newOperations)
    {
        if (oldRender.Fidelity != DecompilationFidelity.Full || newRender.Fidelity != DecompilationFidelity.Full)
            return;

        foreach (var operation in oldOperations.Where(operation => operation.Kind == CSharpDiffOperationKind.SwitchCase))
        {
            rows.Add(CreateRow(
                oldEntry,
                hunk,
                CSharpDiffKind.Remove,
                operation.Line,
                oldRender.Fidelity.ToString(),
                operation.Text,
                "csharp.switch.case.removed",
                $"Removed switch case '{operation.Value}'",
                oldValue: operation.Value,
                newValue: null,
                operationKind: CSharpDiffOperationKind.SwitchCase));
        }

        foreach (var operation in newOperations.Where(operation => operation.Kind == CSharpDiffOperationKind.SwitchCase))
        {
            rows.Add(CreateRow(
                newEntry,
                hunk,
                CSharpDiffKind.Add,
                operation.Line,
                newRender.Fidelity.ToString(),
                operation.Text,
                "csharp.switch.case.added",
                $"Added switch case '{operation.Value}'",
                oldValue: null,
                newValue: operation.Value,
                operationKind: CSharpDiffOperationKind.SwitchCase));
        }

        AddChangedSemanticRows(
            rows,
            newEntry,
            hunk,
            newRender,
            oldOperations,
            newOperations,
            CSharpDiffOperationKind.ReturnExpression,
            "csharp.return-expression.changed",
            static (oldValue, newValue) => $"Changed return expression from '{oldValue}' to '{newValue}'");

        AddChangedSemanticRows(
            rows,
            newEntry,
            hunk,
            newRender,
            oldOperations,
            newOperations,
            CSharpDiffOperationKind.Invocation,
            "csharp.call.changed",
            static (oldValue, newValue) => $"Changed call from '{oldValue}' to '{newValue}'");
    }

    static ImmutableArray<CSharpSemanticOperation> BuildSemanticOperations(string[] lines, int start, int end)
    {
        var operations = ImmutableArray.CreateBuilder<CSharpSemanticOperation>();
        for (int i = start; i < end; i++)
        {
            string line = lines[i];
            int lineNumber = i + 1;
            operations.Add(new CSharpSemanticOperation(CSharpDiffOperationKind.Line, lineNumber, line, line));
            if (TryParseSwitchCase(line, out var label))
                operations.Add(new CSharpSemanticOperation(CSharpDiffOperationKind.SwitchCase, lineNumber, label, line));
            if (TryParseReturnExpression(line, out var expression))
                operations.Add(new CSharpSemanticOperation(CSharpDiffOperationKind.ReturnExpression, lineNumber, expression, line));
            if (TryParseCallExpression(line, out var call))
                operations.Add(new CSharpSemanticOperation(CSharpDiffOperationKind.Invocation, lineNumber, call, line));
        }

        return operations.ToImmutable();
    }

    static void AddChangedSemanticRows(
        ImmutableArray<CSharpDiffRow>.Builder rows,
        CSharpMethodEntry newEntry,
        int hunk,
        CSharpMethodRender newRender,
        ImmutableArray<CSharpSemanticOperation> oldOperations,
        ImmutableArray<CSharpSemanticOperation> newOperations,
        CSharpDiffOperationKind operationKind,
        string changeId,
        Func<string, string, string> messageFactory)
    {
        var oldValues = oldOperations.Where(operation => operation.Kind == operationKind).ToArray();
        var newValues = newOperations.Where(operation => operation.Kind == operationKind).ToArray();
        if (oldValues.Length == 0 || oldValues.Length != newValues.Length)
            return;

        for (int i = 0; i < oldValues.Length; i++)
        {
            var oldValue = oldValues[i];
            var newValue = newValues[i];
            if (oldValue.Value == newValue.Value)
                continue;

            rows.Add(CreateRow(
                newEntry,
                hunk,
                CSharpDiffKind.Changed,
                newValue.Line,
                newRender.Fidelity.ToString(),
                newValue.Text,
                changeId,
                messageFactory(oldValue.Value, newValue.Value),
                oldValue.Value,
                newValue.Value,
                operationKind));
        }
    }

    static bool TryParseSwitchCase(string line, out string label)
    {
        var trimmed = line.Trim();
        if (!trimmed.EndsWith(":", StringComparison.Ordinal))
        {
            label = "";
            return false;
        }

        if (trimmed == "default:")
        {
            label = "default";
            return true;
        }

        if (trimmed.StartsWith("case ", StringComparison.Ordinal))
        {
            label = trimmed["case ".Length..^1].Trim();
            return label.Length > 0;
        }

        label = "";
        return false;
    }

    static bool TryParseReturnExpression(string line, out string expression)
    {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith("return ", StringComparison.Ordinal) || !trimmed.EndsWith(";", StringComparison.Ordinal))
        {
            expression = "";
            return false;
        }

        expression = trimmed["return ".Length..^1].Trim();
        return expression.Length > 0;
    }

    static bool TryParseCallExpression(string line, out string call)
    {
        call = "";
        var trimmed = line.Trim();
        if (trimmed.StartsWith("if ", StringComparison.Ordinal)
            || trimmed.StartsWith("if(", StringComparison.Ordinal)
            || trimmed.StartsWith("for ", StringComparison.Ordinal)
            || trimmed.StartsWith("for(", StringComparison.Ordinal)
            || trimmed.StartsWith("while ", StringComparison.Ordinal)
            || trimmed.StartsWith("while(", StringComparison.Ordinal)
            || trimmed.StartsWith("switch ", StringComparison.Ordinal)
            || trimmed.StartsWith("switch(", StringComparison.Ordinal)
            || trimmed.StartsWith("catch ", StringComparison.Ordinal)
            || trimmed.StartsWith("catch(", StringComparison.Ordinal)
            || !trimmed.EndsWith(";", StringComparison.Ordinal))
        {
            return false;
        }

        var expression = trimmed[..^1].Trim();
        if (expression.StartsWith("return ", StringComparison.Ordinal))
        {
            expression = expression["return ".Length..].Trim();
        }
        else
        {
            int assignment = LastTopLevelAssignment(expression);
            if (assignment >= 0)
                expression = expression[(assignment + 1)..].Trim();
        }

        if (expression.StartsWith("await ", StringComparison.Ordinal))
            expression = expression["await ".Length..].Trim();
        if (expression.StartsWith("new ", StringComparison.Ordinal) || expression.Length == 0)
            return false;

        var invocations = ExtractInvocations(expression);
        if (invocations.Count == 0)
            return false;

        call = string.Join(" | ", invocations);
        return true;
    }

    static List<string> ExtractInvocations(string expression)
    {
        var invocations = new List<string>();
        for (int i = 0; i < expression.Length; i++)
        {
            if (expression[i] != '(' || IsQuoted(expression, i))
                continue;

            int close = FindMatchingCloseParen(expression, i);
            if (close < 0)
                continue;

            int nameEnd = PreviousNonWhitespace(expression, i - 1);
            if (nameEnd < 0 || !IsInvocationNameEnd(expression[nameEnd]))
            {
                i = close;
                continue;
            }

            int nameStart = FindNameStart(expression, nameEnd);
            if (nameStart < 0)
            {
                i = close;
                continue;
            }

            var name = expression[nameStart..(nameEnd + 1)].Trim();
            if (name is "new" or "return" or "typeof" or "sizeof" or "nameof" or "default" or "checked" or "unchecked")
            {
                i = close;
                continue;
            }

            int start = FindInvocationStart(expression, nameStart);
            var invocation = expression[start..(close + 1)].Trim();
            if (invocation.StartsWith("new ", StringComparison.Ordinal))
            {
                i = close;
                continue;
            }

            invocations.Add(invocation);
            i = close;
        }

        return invocations;
    }

    static bool IsInvocationNameEnd(char c)
        => char.IsLetterOrDigit(c) || c == '_' || c == '>' || c == '`';

    static int FindNameStart(string expression, int nameEnd)
    {
        int i = nameEnd;
        if (expression[i] == '>')
        {
            int genericDepth = 1;
            i--;
            while (i >= 0)
            {
                if (expression[i] == '>')
                    genericDepth++;
                else if (expression[i] == '<' && --genericDepth == 0)
                {
                    i--;
                    break;
                }

                i--;
            }
        }

        while (i >= 0 && (char.IsLetterOrDigit(expression[i]) || expression[i] == '_' || expression[i] == '`'))
            i--;

        int start = i + 1;
        return start <= nameEnd ? start : -1;
    }

    static int FindInvocationStart(string expression, int nameStart)
    {
        int start = nameStart;
        int previous = PreviousNonWhitespace(expression, start - 1);
        if (previous < 0 || expression[previous] != '.')
            return start;

        int depth = 0;
        bool inString = false;
        bool inChar = false;
        bool escape = false;
        for (int i = previous - 1; i >= 0; i--)
        {
            char c = expression[i];
            if (escape)
            {
                escape = false;
                continue;
            }

            if (inString || inChar)
            {
                if (c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (inString && c == '"')
                    inString = false;
                else if (inChar && c == '\'')
                    inChar = false;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '\'')
            {
                inChar = true;
                continue;
            }

            if (c is ')' or ']' or '}')
            {
                depth++;
                continue;
            }

            if (c is '(' or '[' or '{')
            {
                if (depth > 0)
                {
                    depth--;
                    continue;
                }

                start = i + 1;
                break;
            }

            if (depth == 0 && IsInvocationBoundary(c))
            {
                start = i + 1;
                break;
            }

            start = i;
        }

        return start;
    }

    static bool IsInvocationBoundary(char c)
        => c is ' ' or '\t' or '+' or '-' or '*' or '/' or '%' or '=' or '!' or '<' or '>' or '&' or '|' or '^' or '?' or ':' or ',' or ';';

    static int PreviousNonWhitespace(string text, int index)
    {
        for (int i = index; i >= 0; i--)
            if (!char.IsWhiteSpace(text[i]))
                return i;
        return -1;
    }

    static int FindMatchingCloseParen(string text, int open)
    {
        int depth = 0;
        bool inString = false;
        bool inChar = false;
        bool escape = false;

        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];
            if (escape)
            {
                escape = false;
                continue;
            }

            if (inString || inChar)
            {
                if (c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (inString && c == '"')
                    inString = false;
                else if (inChar && c == '\'')
                    inChar = false;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '\'')
            {
                inChar = true;
                continue;
            }

            if (c == '(')
                depth++;
            else if (c == ')' && --depth == 0)
                return i;
        }

        return -1;
    }

    static bool IsQuoted(string text, int index)
    {
        bool inString = false;
        bool inChar = false;
        bool escape = false;

        for (int i = 0; i < index; i++)
        {
            char c = text[i];
            if (escape)
            {
                escape = false;
                continue;
            }

            if (inString || inChar)
            {
                if (c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (inString && c == '"')
                    inString = false;
                else if (inChar && c == '\'')
                    inChar = false;
                continue;
            }

            if (c == '"')
                inString = true;
            else if (c == '\'')
                inChar = true;
        }

        return inString || inChar;
    }

    static int LastTopLevelAssignment(string text)
    {
        int depth = 0;
        int last = -1;
        bool inString = false;
        bool inChar = false;
        bool escape = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (escape)
            {
                escape = false;
                continue;
            }

            if (inString || inChar)
            {
                if (c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (inString && c == '"')
                    inString = false;
                else if (inChar && c == '\'')
                    inChar = false;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '\'')
            {
                inChar = true;
                continue;
            }

            if (c is '(' or '[' or '{')
            {
                depth++;
                continue;
            }

            if (c is ')' or ']' or '}')
            {
                if (depth > 0)
                    depth--;
                continue;
            }

            if (depth == 0
                && c == '='
                && (i == 0 || text[i - 1] is not ('=' or '!' or '<' or '>' or '='))
                && (i + 1 == text.Length || text[i + 1] is not ('=' or '>')))
            {
                last = i;
            }
        }

        return last;
    }

    static int IndexOfUnquoted(string text, char target)
    {
        bool inString = false;
        bool inChar = false;
        bool escape = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (escape)
            {
                escape = false;
                continue;
            }

            if (inString || inChar)
            {
                if (c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (inString && c == '"')
                    inString = false;
                else if (inChar && c == '\'')
                    inChar = false;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '\'')
            {
                inChar = true;
                continue;
            }

            if (c == target)
                return i;
        }

        return -1;
    }

    static List<(int OldIndex, int NewIndex)> LongestCommonSubsequence(IReadOnlyList<string> oldLines, IReadOnlyList<string> newLines)
    {
        var lengths = new int[oldLines.Count + 1, newLines.Count + 1];
        for (int oldIndex = oldLines.Count - 1; oldIndex >= 0; oldIndex--)
        {
            for (int newIndex = newLines.Count - 1; newIndex >= 0; newIndex--)
            {
                lengths[oldIndex, newIndex] = string.Equals(oldLines[oldIndex], newLines[newIndex], StringComparison.Ordinal)
                    ? lengths[oldIndex + 1, newIndex + 1] + 1
                    : Math.Max(lengths[oldIndex + 1, newIndex], lengths[oldIndex, newIndex + 1]);
            }
        }

        var pairs = new List<(int OldIndex, int NewIndex)>();
        int i = 0;
        int j = 0;
        while (i < oldLines.Count && j < newLines.Count)
        {
            if (string.Equals(oldLines[i], newLines[j], StringComparison.Ordinal))
            {
                pairs.Add((i, j));
                i++;
                j++;
            }
            else if (lengths[i + 1, j] >= lengths[i, j + 1])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return pairs;
    }

    static string[] SplitLines(string? text)
    {
        string normalized = (text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        while (normalized.EndsWith('\n'))
            normalized = normalized[..^1];
        return normalized.Length == 0 ? [] : normalized.Split('\n');
    }

    sealed record CSharpMethodEntry(
        string Path,
        string AssemblyName,
        string StableAssemblyKey,
        MemberAnchor Anchor,
        MetadataTypeRef TypeRef,
        MetadataMemberRef MemberRef,
        string RawKey,
        string StableMemberKey,
        string DuplicateDiscriminator,
        string Display,
        string TypeFullName,
        string MethodName,
        int OverloadIndex,
        bool HasBody,
        string BodyFingerprint);

    enum CSharpMethodRenderState
    {
        Body,
        NoBody,
        Failed,
    }

    sealed record CSharpMethodRender(CSharpMethodRenderState State, string[] Lines, DecompilationFidelity Fidelity);

    sealed class SourceCache : IDisposable
    {
        readonly Dictionary<string, MetadataSource> _sources = new(StringComparer.Ordinal);

        public MetadataSource Open(string path)
        {
            if (!_sources.TryGetValue(path, out var source))
            {
                source = MetadataSource.OpenWithoutSymbols(path);
                _sources.Add(path, source);
            }

            return source;
        }

        public void Dispose()
        {
            foreach (var source in _sources.Values)
                source.Dispose();
        }
    }
}
