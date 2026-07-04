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
}

public sealed record CSharpDiffRow(
    string AssemblyIdentity,
    string StableMemberKey,
    MemberAnchor Anchor,
    string Member,
    string ChangeId,
    string Message,
    int HunkId,
    CSharpDiffKind Kind,
    int? Line,
    string? SourceCoordinate,
    string Fidelity,
    string Text);

public sealed record CSharpBodyDiffResult(ImmutableArray<CSharpDiffRow> Rows)
{
    public bool IsExact => Rows.IsEmpty;
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

    public static CSharpBodyDiffResult CompareAssemblies(IReadOnlyList<string> oldPaths, IReadOnlyList<string> newPaths, bool includeNonPublic = false, IReadOnlySet<string>? typeFilters = null)
    {
        ArgumentNullException.ThrowIfNull(oldPaths);
        ArgumentNullException.ThrowIfNull(newPaths);

        var oldMethods = BuildMethodIndex(oldPaths, includeNonPublic, typeFilters);
        var newMethods = BuildMethodIndex(newPaths, includeNonPublic, typeFilters);
        var rows = ImmutableArray.CreateBuilder<CSharpDiffRow>();
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

                AddLineDiffRows(rows, oldMethod, Decompile(oldMethod, sources), Decompile(newMethod, sources), ref hunkId);
            }
        }
        finally
        {
            sources.Dispose();
        }

        return new CSharpBodyDiffResult(rows.ToImmutable());
    }

    static Dictionary<string, CSharpMethodEntry> BuildMethodIndex(IReadOnlyList<string> paths, bool includeNonPublic, IReadOnlySet<string>? typeFilters)
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
            entries.AddRange(EnumerateMethods(source, path, occurrenceKey, includeNonPublic, typeFilters));
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

    static IEnumerable<CSharpMethodEntry> EnumerateMethods(MetadataSource source, string path, string stableAssemblyKey, bool includeNonPublic, IReadOnlySet<string>? typeFilters)
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
                string returnType = CanonicalTypeName(signature.ReturnType);
                var parameters = signature.ParameterTypes.Select(CanonicalTypeName).ToImmutableArray();
                int genericArity = method.GetGenericParameters().Count;
                string methodGeneric = GenericParameterList(genericArity, isMethod: true);
                string selectorName = MemberSelectorName(methodName);
                string returnSuffix = IsConversionOperator(methodName) ? $"~{returnType}" : "";
                string canonicalName = CanonicalMemberName(methodName);
                string canonicalSignature = $"M:{typeKey}.{canonicalName}{methodGeneric}({string.Join(",", parameters)}){returnSuffix}";
                var anchor = CreateMemberAnchor(typeKey, selectorName, canonicalSignature);
                string displayName = methodName == ".ctor" ? "#ctor" : methodName;
                string display = $"{typeDisplay}.{displayName}{GenericAritySuffix(genericArity)}({string.Join(", ", parameters)})";
                yield return new CSharpMethodEntry(
                    path,
                    source.AssemblyName,
                    stableAssemblyKey,
                    anchor,
                    $"{stableAssemblyKey}|{anchor.CanonicalSignature}",
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

    static string GenericAritySuffix(int arity)
        => arity == 0 ? "" : $"`{arity}";

    static string CanonicalMemberName(string methodName)
        => methodName == ".ctor" ? "#ctor" : methodName;

    static string MemberSelectorName(string methodName)
        => methodName switch
        {
            ".ctor" => ".ctor",
            "op_Implicit" or "op_Explicit" or "op_CheckedExplicit" => $"operator:{methodName}",
            _ when methodName.Contains('.') => $"explicit:{methodName}",
            _ => methodName,
        };

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

    static MemberAnchor CreateMemberAnchor(string typeFullName, string memberName, string canonicalSignature)
        => new(
            $"{memberName}~{MemberAnchor.ComputeFingerprint(canonicalSignature)}",
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
        rows.Add(CreateRow(entry, hunk, kind, null, "NotRun", text, changeId, message));
    }

    static void AddLineDiffRows(
        ImmutableArray<CSharpDiffRow>.Builder rows,
        CSharpMethodEntry entry,
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
            AddRenderStateRows(rows, entry, oldRender, newRender, ref hunkId);
            return;
        }

        if (oldLines.Length > MaxLcsLines || newLines.Length > MaxLcsLines)
        {
            int hunk = hunkId++;
            string message = $"/* C# diff skipped: old body has {oldLines.Length} lines, new body has {newLines.Length} lines; limit is {MaxLcsLines} */";
            rows.Add(CreateRow(entry, hunk, CSharpDiffKind.Remove, null, oldRender.Fidelity.ToString(), message, "csharp.body-diff.skipped", "Skipped C# body diff; old body exceeds line limit."));
            rows.Add(CreateRow(entry, hunk, CSharpDiffKind.Add, null, newRender.Fidelity.ToString(), message, "csharp.body-diff.skipped", "Skipped C# body diff; new body exceeds line limit."));
            return;
        }

        var lcs = LongestCommonSubsequence(oldLines, newLines);
        int oldIndex = 0;
        int newIndex = 0;
        foreach (var (nextOld, nextNew) in lcs)
        {
            AddUnmatched(rows, entry, oldRender, oldIndex, nextOld, newRender, newIndex, nextNew, ref hunkId);
            oldIndex = nextOld + 1;
            newIndex = nextNew + 1;
        }

        AddUnmatched(rows, entry, oldRender, oldIndex, oldLines.Length, newRender, newIndex, newLines.Length, ref hunkId);
    }

    static void AddUnmatched(
        ImmutableArray<CSharpDiffRow>.Builder rows,
        CSharpMethodEntry entry,
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
        for (int i = oldStart; i < oldEnd; i++)
            rows.Add(CreateRow(entry, hunk, CSharpDiffKind.Remove, i + 1, oldRender.Fidelity.ToString(), oldRender.Lines[i]));
        for (int i = newStart; i < newEnd; i++)
            rows.Add(CreateRow(entry, hunk, CSharpDiffKind.Add, i + 1, newRender.Fidelity.ToString(), newRender.Lines[i]));
    }

    static void AddRenderStateRows(
        ImmutableArray<CSharpDiffRow>.Builder rows,
        CSharpMethodEntry entry,
        CSharpMethodRender oldRender,
        CSharpMethodRender newRender,
        ref int hunkId)
    {
        int hunk = hunkId++;
        if (oldRender.State is CSharpMethodRenderState.Body && newRender.State is CSharpMethodRenderState.NoBody)
        {
            rows.Add(CreateRow(entry, hunk, CSharpDiffKind.Remove, null, oldRender.Fidelity.ToString(), "/* method body removed */", "csharp.method.body-removed", "Removed C# method body."));
            return;
        }

        if (oldRender.State is CSharpMethodRenderState.NoBody && newRender.State is CSharpMethodRenderState.Body)
        {
            rows.Add(CreateRow(entry, hunk, CSharpDiffKind.Add, null, newRender.Fidelity.ToString(), "/* method body added */", "csharp.method.body-added", "Added C# method body."));
            return;
        }

        if (oldRender.State is CSharpMethodRenderState.Failed)
            rows.Add(CreateRow(entry, hunk, CSharpDiffKind.Remove, null, oldRender.Fidelity.ToString(), oldRender.Lines[0], "csharp.decompile.failed", "Old method body decompilation failed."));
        if (newRender.State is CSharpMethodRenderState.Failed)
            rows.Add(CreateRow(entry, hunk, CSharpDiffKind.Add, null, newRender.Fidelity.ToString(), newRender.Lines[0], "csharp.decompile.failed", "New method body decompilation failed."));
        if (oldRender.State is CSharpMethodRenderState.NoBody)
            rows.Add(CreateRow(entry, hunk, CSharpDiffKind.Remove, null, oldRender.Fidelity.ToString(), oldRender.Lines[0], "csharp.method.no-body", "Old method has no C# body."));
        if (newRender.State is CSharpMethodRenderState.NoBody)
            rows.Add(CreateRow(entry, hunk, CSharpDiffKind.Add, null, newRender.Fidelity.ToString(), newRender.Lines[0], "csharp.method.no-body", "New method has no C# body."));
        if (oldRender.State is CSharpMethodRenderState.Body)
            rows.Add(CreateRow(entry, hunk, CSharpDiffKind.Remove, null, oldRender.Fidelity.ToString(), "/* method body removed */", "csharp.method.body-removed", "Removed C# method body."));
        if (newRender.State is CSharpMethodRenderState.Body)
            rows.Add(CreateRow(entry, hunk, CSharpDiffKind.Add, null, newRender.Fidelity.ToString(), "/* method body added */", "csharp.method.body-added", "Added C# method body."));
    }

    static CSharpDiffRow CreateRow(
        CSharpMethodEntry entry,
        int hunk,
        CSharpDiffKind kind,
        int? line,
        string fidelity,
        string text,
        string? changeId = null,
        string? message = null)
    {
        changeId ??= kind == CSharpDiffKind.Add ? "csharp.line.added" : "csharp.line.removed";
        message ??= kind == CSharpDiffKind.Add
            ? $"Added C# line '{text}'"
            : $"Removed C# line '{text}'";
        return new CSharpDiffRow(
            entry.StableAssemblyKey,
            entry.StableMemberKey,
            entry.Anchor,
            entry.Display,
            changeId,
            message,
            hunk,
            kind,
            line,
            line is null ? null : $"line:{line.Value}",
            fidelity,
            text);
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
        string StableMemberKey,
        string DuplicateDiscriminator,
        string Display,
        string TypeFullName,
        string MethodName,
        int OverloadIndex,
        bool HasBody,
        string BodyFingerprint)
    {
        public string RawKey => Anchor.CanonicalSignature;
    }

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
