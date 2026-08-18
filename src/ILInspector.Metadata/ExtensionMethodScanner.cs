using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using CSharpText;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

/// <summary>
/// Information about a discovered extension method.
/// </summary>
public record ExtensionMethodInfo(
    string MethodName,
    string ExtensionClass,
    string? Namespace,
    string ExtendedType,
    string Signature,
    string? Assembly = null,
    string Kind = "method")
{
    public MemberAnchor? Anchor { get; init; }
    public string? ReturnType { get; init; }
    public string? CanonicalExtendedType { get; init; }
    internal MetadataNamedTypeReference? ReturnTypeReference { get; init; }
    internal MetadataNamedTypeReference? ExtendedTypeReference { get; init; }

    public MetadataNamedTypeReference? GetReturnTypeReference() =>
        ReturnTypeReference;

    public MetadataNamedTypeReference? GetExtendedTypeReference() =>
        ExtendedTypeReference;

    // Preserve the original seven-field record contract. The structured fields
    // are derived from the same metadata and intentionally do not affect equality.
    public virtual bool Equals(ExtensionMethodInfo? other)
        => ReferenceEquals(this, other)
        || other is not null
        && EqualityContract == other.EqualityContract
        && string.Equals(MethodName, other.MethodName, StringComparison.Ordinal)
        && string.Equals(ExtensionClass, other.ExtensionClass, StringComparison.Ordinal)
        && string.Equals(Namespace, other.Namespace, StringComparison.Ordinal)
        && string.Equals(ExtendedType, other.ExtendedType, StringComparison.Ordinal)
        && string.Equals(Signature, other.Signature, StringComparison.Ordinal)
        && string.Equals(Assembly, other.Assembly, StringComparison.Ordinal)
        && string.Equals(Kind, other.Kind, StringComparison.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(EqualityContract);
        hash.Add(MethodName, StringComparer.Ordinal);
        hash.Add(ExtensionClass, StringComparer.Ordinal);
        hash.Add(Namespace, StringComparer.Ordinal);
        hash.Add(ExtendedType, StringComparer.Ordinal);
        hash.Add(Signature, StringComparer.Ordinal);
        hash.Add(Assembly, StringComparer.Ordinal);
        hash.Add(Kind, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Image-local address used to resolve one type during extension reachability.
/// </summary>
public sealed record ExtensionReachabilityType(
    string FullName,
    string SimpleName,
    int MetadataToken);

/// <summary>One public-member edge used by extension reachability.</summary>
public sealed record ExtensionReachabilityEdge(
    string Type,
    string PathSegment);

/// <summary>
/// Scans assemblies for extension methods targeting a specific type.
/// </summary>
public static class ExtensionMethodScanner
{
    /// <summary>
    /// Finds extension methods in an assembly that extend the specified target type.
    /// </summary>
    public static IEnumerable<ExtensionMethodInfo> FindExtensions(
        PEReader peReader, string targetType, bool includeAll = false)
    {
        if (!peReader.HasMetadata)
            yield break;

        var reader = peReader.GetMetadataReader();
        var normalizedTarget = FqnParser.NormalizeTypeName(targetType);

        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            var typeAttrs = typeDef.Attributes;

            // Extension methods must be in static classes
            bool isStatic = (typeAttrs & TypeAttributes.Sealed) != 0 &&
                           (typeAttrs & TypeAttributes.Abstract) != 0;
            if (!isStatic) continue;

            // Class must have [Extension] attribute
            if (!AttributeReader.HasExtensionAttribute(reader, typeDef.GetCustomAttributes())) continue;

            // Skip hidden types unless includeAll
            if (!includeAll && AttributeReader.HasHiddenAttribute(reader, typeDef.GetCustomAttributes()))
                continue;

            string classNs = reader.GetString(typeDef.Namespace);
            string fullClassName = TypeResolver.FormatDisplayName(reader.GetFullTypeName(typeDef));

            foreach (var property in FindDeclaredExtensionProperties(
                reader,
                typeDefHandle,
                typeDef,
                fullClassName,
                classNs,
                includeAll))
            {
                if (TypeMatcher.Matches(
                    FqnParser.NormalizeTypeName(property.ExtendedType),
                    normalizedTarget))
                {
                    yield return property;
                }
            }

            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (!includeAll
                    && (method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                {
                    continue;
                }
                if ((method.Attributes & MethodAttributes.Static) == 0) continue;

                string methodName = reader.GetString(method.Name);

                bool hasExtension = AttributeReader.HasExtensionAttribute(reader, method.GetCustomAttributes());
                if (!hasExtension) continue;

                // Skip hidden methods unless includeAll
                if (!includeAll && AttributeReader.HasHiddenAttribute(reader, method.GetCustomAttributes()))
                    continue;

                // Decode signature to get first parameter type
                {
                    var context = GenericContext.ForMethod(reader, typeDef, method);
                    if (!GuardedSignatureText.MethodText(reader, method, context)
                        .TryGetValue(out var signature))
                    {
                        continue;
                    }
                    if (signature.ParameterTypes.Length == 0) continue;

                    var extendedType = signature.ParameterTypes[0];
                    var normalizedExtended = FqnParser.NormalizeTypeName(extendedType);

                    // Match against target
                    if (TypeMatcher.Matches(normalizedExtended, normalizedTarget))
                    {
                        var anchorInfo = ApiMemberIdentity.CreateExtensionMethodAnchorInfo(
                            reader, typeDefHandle, method);
                        yield return new ExtensionMethodInfo(
                            MethodName: methodName,
                            ExtensionClass: fullClassName,
                            Namespace: classNs,
                            ExtendedType: extendedType,
                            Signature: FormatMethodSignature(reader, typeDef, method, context))
                        {
                            Anchor = anchorInfo.Anchor,
                            ReturnType = anchorInfo.ReturnType,
                            CanonicalExtendedType = anchorInfo.ExtendedType,
                            ReturnTypeReference =
                                anchorInfo.ReturnTypeReference,
                            ExtendedTypeReference =
                                anchorInfo.ExtendedTypeReference,
                        };
                    }
                }
            }
        }
    }

    /// <summary>
    /// Finds all extension methods in an assembly (no target type filter).
    /// </summary>
    public static IEnumerable<ExtensionMethodInfo> FindAllExtensions(
        Stream peStream, bool includeAll = false)
    {
        using var peReader = new PEReader(peStream);
        return FindAllExtensions(peReader, includeAll).ToList();
    }

    /// <summary>
    /// Finds all extension methods in an assembly (no target type filter).
    /// </summary>
    public static IEnumerable<ExtensionMethodInfo> FindAllExtensions(
        PEReader peReader, bool includeAll = false)
    {
        if (!peReader.HasMetadata)
            yield break;

        var reader = peReader.GetMetadataReader();

        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            var typeAttrs = typeDef.Attributes;

            bool isStatic = (typeAttrs & TypeAttributes.Sealed) != 0 &&
                           (typeAttrs & TypeAttributes.Abstract) != 0;
            if (!isStatic) continue;

            if (!AttributeReader.HasExtensionAttribute(reader, typeDef.GetCustomAttributes())) continue;

            if (!includeAll && AttributeReader.HasHiddenAttribute(reader, typeDef.GetCustomAttributes()))
                continue;

            string classNs = reader.GetString(typeDef.Namespace);
            string fullClassName = TypeResolver.FormatDisplayName(reader.GetFullTypeName(typeDef));

            foreach (var property in FindDeclaredExtensionProperties(
                reader,
                typeDefHandle,
                typeDef,
                fullClassName,
                classNs,
                includeAll))
            {
                yield return property;
            }

            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (!includeAll
                    && (method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                {
                    continue;
                }
                if ((method.Attributes & MethodAttributes.Static) == 0) continue;

                string methodName = reader.GetString(method.Name);

                bool hasExtension = AttributeReader.HasExtensionAttribute(reader, method.GetCustomAttributes());
                if (!hasExtension) continue;

                if (!includeAll && AttributeReader.HasHiddenAttribute(reader, method.GetCustomAttributes()))
                    continue;

                {
                    var context = GenericContext.ForMethod(reader, typeDef, method);
                    if (!GuardedSignatureText.MethodText(reader, method, context)
                        .TryGetValue(out var signature))
                    {
                        continue;
                    }
                    if (signature.ParameterTypes.Length == 0) continue;

                    var extendedType = signature.ParameterTypes[0];

                    var anchorInfo = ApiMemberIdentity.CreateExtensionMethodAnchorInfo(
                        reader, typeDefHandle, method);
                    yield return new ExtensionMethodInfo(
                        MethodName: methodName,
                        ExtensionClass: fullClassName,
                        Namespace: classNs,
                        ExtendedType: extendedType,
                        Signature: FormatMethodSignature(reader, typeDef, method, context))
                    {
                        Anchor = anchorInfo.Anchor,
                        ReturnType = anchorInfo.ReturnType,
                        CanonicalExtendedType = anchorInfo.ExtendedType,
                        ReturnTypeReference =
                            anchorInfo.ReturnTypeReference,
                        ExtendedTypeReference =
                            anchorInfo.ExtendedTypeReference,
                    };
                }
            }
        }
    }

    /// <summary>
    /// Finds extension methods in a stream containing a PE image.
    /// </summary>
    public static IEnumerable<ExtensionMethodInfo> FindExtensions(
        Stream peStream, string targetType, bool includeAll = false)
    {
        using var peReader = new PEReader(peStream);
        // Must materialize results before PEReader is disposed
        return FindExtensions(peReader, targetType, includeAll).ToList();
    }

    /// <summary>
    /// Indexes type names without decoding their member signatures.
    /// </summary>
    public static List<ExtensionReachabilityType> IndexReachableTypes(
        PEReader peReader)
    {
        if (!peReader.HasMetadata)
            return [];

        MetadataReader reader = peReader.GetMetadataReader();
        var types = new List<ExtensionReachabilityType>(
            reader.TypeDefinitions.Count);
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(handle);
            types.Add(
                new ExtensionReachabilityType(
                    reader.GetFullTypeName(type),
                    reader.GetString(type.Name),
                    MetadataTokens.GetToken(handle)));
        }

        return types;
    }

    /// <summary>
    /// Decodes the reachable public-member edges of one image-local type.
    /// </summary>
    public static List<ExtensionReachabilityEdge> FindReachableEdges(
        PEReader peReader,
        int metadataToken)
    {
        if (!peReader.HasMetadata)
            return [];

        EntityHandle entity = MetadataTokens.EntityHandle(metadataToken);
        if (entity.Kind != HandleKind.TypeDefinition)
        {
            throw new ArgumentException(
                "Extension reachability requires a TypeDef token.",
                nameof(metadataToken));
        }

        MetadataReader reader = peReader.GetMetadataReader();
        return FindReachableEdges(
            reader,
            (TypeDefinitionHandle)entity);
    }

    /// <summary>
    /// Finds types reachable from a target type by traversing public members.
    /// Used for the --reachable flag to discover transitive extension methods.
    /// </summary>
    public static List<(string Type, string Path)> FindReachableTypes(
        string targetType,
        IEnumerable<string> assemblyPaths,
        int maxDepth)
    {
        List<(string Type, string Path)> results = [];
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toProcess = new Queue<(string TypeName, string Path, int Depth)>();

        toProcess.Enqueue((targetType, "", maxDepth));
        visited.Add(targetType);

        // Open every assembly once and index its types, rather than re-opening and re-scanning all
        // assemblies for each BFS node. Readers/streams stay alive for the whole walk (signatures
        // are decoded lazily) and are disposed at the end.
        var disposables = new List<IDisposable>();
        var byFullName = new Dictionary<string, (MetadataReader Reader, TypeDefinitionHandle Handle)>(StringComparer.OrdinalIgnoreCase);
        var bySimpleName = new Dictionary<string, (MetadataReader Reader, TypeDefinitionHandle Handle)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var assemblyPath in assemblyPaths)
            {
                try
                {
                    var stream = File.OpenRead(assemblyPath);
                    var peReader = new PEReader(stream);
                    if (!peReader.HasMetadata)
                    {
                        peReader.Dispose();
                        stream.Dispose();
                        continue;
                    }
                    disposables.Add(peReader);
                    disposables.Add(stream);

                    var reader = peReader.GetMetadataReader();
                    foreach (var typeDefHandle in reader.TypeDefinitions)
                    {
                        var typeDef = reader.GetTypeDefinition(typeDefHandle);
                        // TryAdd preserves "first assembly / first type wins".
                        byFullName.TryAdd(reader.GetFullTypeName(typeDef), (reader, typeDefHandle));
                        bySimpleName.TryAdd(reader.GetString(typeDef.Name), (reader, typeDefHandle));
                    }
                }
                // Skip assemblies with unreadable metadata
                catch { }
            }

            while (toProcess.Count > 0)
            {
                var (currentType, currentPath, remainingDepth) = toProcess.Dequeue();
                if (remainingDepth <= 0) continue;

                // Match by full name, then simple name, then the last segment of a qualified name.
                if (!byFullName.TryGetValue(currentType, out var found)
                    && !bySimpleName.TryGetValue(currentType, out found)
                    && !bySimpleName.TryGetValue(currentType.Split('.').Last(), out found))
                    continue;

                var (reader, typeDefHandle) = found;
                foreach (ExtensionReachabilityEdge edge
                    in FindReachableEdges(reader, typeDefHandle))
                {
                    if (!visited.Add(edge.Type))
                        continue;

                    var path = currentPath + edge.PathSegment;
                    results.Add((edge.Type, path));
                    toProcess.Enqueue(
                        (edge.Type, path, remainingDepth - 1));
                }
            }
        }
        finally
        {
            foreach (var d in disposables)
                d.Dispose();
        }

        return results;
    }

    private static List<ExtensionReachabilityEdge> FindReachableEdges(
        MetadataReader reader,
        TypeDefinitionHandle typeDefHandle)
    {
        var results = new List<ExtensionReachabilityEdge>();
        TypeDefinition typeDef =
            reader.GetTypeDefinition(typeDefHandle);
        GenericContext context =
            GenericContext.ForType(reader, typeDef);

        foreach (PropertyDefinitionHandle propHandle
            in typeDef.GetProperties())
        {
            PropertyDefinition prop =
                reader.GetPropertyDefinition(propHandle);
            string propName = reader.GetString(prop.Name);

            try
            {
                var signature =
                    GuardedSignatureText.PropertyText(
                            reader,
                            prop,
                            context)
                        .GetValueOrThrow();
                string propType =
                    UnwrapAsyncType(signature.ReturnType);

                if (!string.IsNullOrEmpty(propType)
                    && !IsPrimitiveType(propType))
                {
                    results.Add(
                        new ExtensionReachabilityEdge(
                            propType,
                            $".{propName}"));
                }
            }
            catch (Exception ex) when (IsUndecodableSignature(ex))
            {
            }
        }

        foreach (MethodDefinitionHandle methodHandle
            in typeDef.GetMethods())
        {
            MethodDefinition method =
                reader.GetMethodDefinition(methodHandle);
            if ((method.Attributes & MethodAttributes.MemberAccessMask)
                != MethodAttributes.Public)
            {
                continue;
            }

            string methodName = reader.GetString(method.Name);
            if (methodName.StartsWith("get_", StringComparison.Ordinal)
                || methodName.StartsWith("set_", StringComparison.Ordinal)
                || methodName.StartsWith("add_", StringComparison.Ordinal)
                || methodName.StartsWith("remove_", StringComparison.Ordinal)
                || methodName.StartsWith(".", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                GenericContext methodContext =
                    GenericContext.ForMethod(
                        reader,
                        typeDef,
                        method);
                var signature =
                    GuardedSignatureText.MethodText(
                            reader,
                            method,
                            methodContext)
                        .GetValueOrThrow();
                string returnType =
                    UnwrapAsyncType(signature.ReturnType);

                if (!string.IsNullOrEmpty(returnType)
                    && returnType != "void"
                    && !IsPrimitiveType(returnType))
                {
                    results.Add(
                        new ExtensionReachabilityEdge(
                            returnType,
                            $".{methodName}()"));
                }
            }
            catch (Exception ex) when (IsUndecodableSignature(ex))
            {
            }
        }

        return results;
    }

    private static bool IsUndecodableSignature(Exception exception)
        => exception is BadImageFormatException
            or InvalidOperationException
            or ArgumentException
            or NotSupportedException
            or OverflowException
            or IndexOutOfRangeException;

    private static string UnwrapAsyncType(string type)
    {
        // Strip namespace prefix if present
        const string taskNs = "System.Threading.Tasks.";
        var t = type.StartsWith(taskNs, StringComparison.Ordinal) ? type[taskNs.Length..] : type;

        if (t.StartsWith("Task<", StringComparison.Ordinal) && t.EndsWith(">"))
            return t[5..^1];
        if (t.StartsWith("ValueTask<", StringComparison.Ordinal) && t.EndsWith(">"))
            return t[10..^1];
        if (t is "Task" or "ValueTask")
            return "";
        return type;
    }

    private static bool IsPrimitiveType(string type) => type switch
    {
        "string" or "int" or "long" or "bool" or "byte" or "void" or
        "double" or "float" or "decimal" or "char" or "object" or
        "TimeSpan" or "DateTime" or "DateTimeOffset" or "Guid" or
        "Uri" or "Version" or "CancellationToken" => true,
        _ when type.EndsWith("[]") => true,
        _ => false
    };

    private static IEnumerable<ExtensionMethodInfo> FindDeclaredExtensionProperties(
        MetadataReader reader,
        TypeDefinitionHandle extensionClassHandle,
        TypeDefinition extensionClass,
        string extensionClassName,
        string? extensionClassNamespace,
        bool includeAll)
    {
        var seenProperties = new HashSet<string>(StringComparer.Ordinal);

        foreach (var groupingTypeHandle in extensionClass.GetNestedTypes())
        {
            var groupingType = reader.GetTypeDefinition(groupingTypeHandle);
            if (!AttributeReader.HasExtensionAttribute(reader, groupingType.GetCustomAttributes()))
                continue;

            foreach (var propertyHandle in groupingType.GetProperties())
            {
                var property = reader.GetPropertyDefinition(propertyHandle);
                var accessors = property.GetAccessors();
                bool includeGetter = IsIncludedAccessor(reader, accessors.Getter, includeAll);
                bool includeSetter = IsIncludedAccessor(reader, accessors.Setter, includeAll);
                if (!includeGetter && !includeSetter)
                    continue;

                if (!TryGetDeclaredExtensionProperty(
                        reader,
                        groupingType,
                        property,
                        accessors,
                        beforeMaterialize: null,
                        out var markerType,
                        out var markerMethod))
                    continue;

                if (!includeAll
                    && AttributeReader.HasHiddenAttribute(reader, property.GetCustomAttributes()))
                {
                    continue;
                }

                if (!includeAll)
                {
                    if (includeGetter && IsHiddenAccessor(reader, accessors.Getter))
                        includeGetter = false;
                    if (includeSetter && IsHiddenAccessor(reader, accessors.Setter))
                        includeSetter = false;
                    if (!includeGetter && !includeSetter)
                        continue;
                }

                var anchorInfo =
                    ApiMemberIdentity.CreateExtensionPropertyDeclarationAnchorInfo(
                        reader,
                        extensionClassHandle,
                        markerType,
                        markerMethod,
                        property);
                if (!seenProperties.Add(anchorInfo.Anchor.CanonicalSignature))
                    continue;

                var displayContext = GenericContext.ForType(reader, markerType);
                if (!GuardedSignatureText.MethodText(
                    reader,
                    markerMethod,
                    displayContext).TryGetValue(out var markerSignature))
                {
                    continue;
                }
                if (markerSignature.ParameterTypes.Length != 1)
                    throw new BadImageFormatException(
                        "An extension marker must have exactly one receiver parameter.");

                if (!GuardedSignatureText.PropertyText(
                    reader,
                    property,
                    displayContext).TryGetValue(out var propertySignature))
                {
                    continue;
                }
                string propertyName = reader.GetString(property.Name);
                string parameterText = propertySignature.ParameterTypes.Length == 0
                    ? ""
                    : $"[{string.Join(", ", propertySignature.ParameterTypes)}]";
                string accessorText = includeGetter && includeSetter
                    ? "get; set;"
                    : includeGetter ? "get;" : "set;";
                yield return new ExtensionMethodInfo(
                    MethodName: propertyName,
                    ExtensionClass: extensionClassName,
                    Namespace: extensionClassNamespace,
                    ExtendedType: markerSignature.ParameterTypes[0],
                    Signature: $"{propertySignature.ReturnType} {propertyName}{parameterText} {{ {accessorText} }}",
                    Kind: "property")
                {
                    Anchor = anchorInfo.Anchor,
                    ReturnType = anchorInfo.ReturnType,
                    CanonicalExtendedType = anchorInfo.ExtendedType,
                };
            }
        }
    }

    internal static HashSet<MethodDefinitionHandle>
        GetDeclaredExtensionPropertyImplementationMethods(
            MetadataReader reader,
            TypeDefinition extensionClass,
            Action<int>? beforeMaterialize = null)
    {
        HashSet<MethodDefinitionHandle> implementations = [];
        if (!AttributeReader.HasExtensionAttribute(
                reader,
                extensionClass.GetCustomAttributes(),
                beforeMaterialize))
        {
            return implementations;
        }

        Dictionary<string, List<MethodDefinitionHandle>>? candidatesByName = null;
        GenericContext? extensionClassContext = null;
        int extensionClassGenericCount = extensionClass.GetGenericParameters().Count;

        foreach (var groupingTypeHandle in extensionClass.GetNestedTypes())
        {
            var groupingType = reader.GetTypeDefinition(groupingTypeHandle);
            if (!AttributeReader.HasExtensionAttribute(
                    reader,
                    groupingType.GetCustomAttributes(),
                    beforeMaterialize))
            {
                continue;
            }

            foreach (var propertyHandle in groupingType.GetProperties())
            {
                var property = reader.GetPropertyDefinition(propertyHandle);
                var accessors = property.GetAccessors();
                if (!TryGetDeclaredExtensionProperty(
                        reader,
                        groupingType,
                        property,
                        accessors,
                        beforeMaterialize,
                        out var markerType,
                        out var markerMethod))
                {
                    continue;
                }

                var markerContext = GenericContext.ForType(
                    reader,
                    markerType,
                    beforeMaterialize);
                if (!TryReadMethodSignature(
                        reader,
                        markerMethod,
                        markerContext,
                        beforeMaterialize,
                        out var markerSignature))
                {
                    continue;
                }
                if (markerSignature.ParameterTypes.Length != 1)
                {
                    throw new BadImageFormatException(
                        "An extension marker must have exactly one receiver parameter.");
                }

                int expectedGenericCount =
                    markerType.GetGenericParameters().Count - extensionClassGenericCount;
                if (expectedGenericCount < 0)
                    continue;

                foreach (var accessorHandle in new[]
                {
                    accessors.Getter,
                    accessors.Setter,
                })
                {
                    if (accessorHandle.IsNil)
                        continue;

                    var accessor = reader.GetMethodDefinition(accessorHandle);
                    if (!TryReadMethodSignature(
                            reader,
                            accessor,
                            markerContext,
                            beforeMaterialize,
                            out var accessorSignature))
                    {
                        continue;
                    }

                    string accessorName = ReadStructuralMethodName(
                        reader,
                        accessor,
                        beforeMaterialize);
                    candidatesByName ??= BuildImplementationCandidateMap(
                        reader,
                        extensionClass,
                        beforeMaterialize);
                    if (!candidatesByName.TryGetValue(
                            accessorName,
                            out var candidates))
                    {
                        continue;
                    }

                    MethodDefinitionHandle matched = default;
                    int matchCount = 0;
                    foreach (var candidateHandle in candidates)
                    {
                        var candidate = reader.GetMethodDefinition(candidateHandle);
                        extensionClassContext ??= GenericContext.ForType(
                            reader,
                            extensionClass,
                            beforeMaterialize);
                        var candidateContext = GenericContext.ForMethod(
                            reader,
                            extensionClassContext,
                            candidate,
                            beforeMaterialize);
                        if (!TryReadMethodSignature(
                                reader,
                                candidate,
                                candidateContext,
                                beforeMaterialize,
                                out var candidateSignature)
                            || !MatchesExtensionPropertyImplementation(
                                markerSignature,
                                accessor,
                                accessorSignature,
                                candidate,
                                candidateSignature,
                                expectedGenericCount))
                        {
                            continue;
                        }

                        matched = candidateHandle;
                        matchCount++;
                        if (matchCount > 1)
                            break;
                    }

                    if (matchCount == 1)
                        implementations.Add(matched);
                }
            }
        }

        return implementations;
    }

    private static Dictionary<string, List<MethodDefinitionHandle>>
        BuildImplementationCandidateMap(
            MetadataReader reader,
            TypeDefinition extensionClass,
            Action<int>? beforeMaterialize)
    {
        var candidates = new Dictionary<string, List<MethodDefinitionHandle>>(
            StringComparer.Ordinal);
        foreach (var handle in extensionClass.GetMethods())
        {
            var method = reader.GetMethodDefinition(handle);
            if ((method.Attributes & MethodAttributes.Static) == 0)
                continue;

            string name = ReadStructuralMethodName(
                reader,
                method,
                beforeMaterialize);
            if (!candidates.TryGetValue(name, out var sameNamed))
            {
                sameNamed = [];
                candidates.Add(name, sameNamed);
            }
            sameNamed.Add(handle);
        }

        return candidates;
    }

    private static bool MatchesExtensionPropertyImplementation(
        MethodSignature<string> markerSignature,
        MethodDefinition accessor,
        MethodSignature<string> accessorSignature,
        MethodDefinition candidate,
        MethodSignature<string> candidateSignature,
        int expectedGenericCount)
    {
        if ((candidate.Attributes & MethodAttributes.Static) == 0
            || candidate.RelativeVirtualAddress == 0
            || (candidate.Attributes & MethodAttributes.MemberAccessMask)
                != (accessor.Attributes & MethodAttributes.MemberAccessMask)
            || candidate.GetGenericParameters().Count != expectedGenericCount
            || !string.Equals(
                candidateSignature.ReturnType,
                accessorSignature.ReturnType,
                StringComparison.Ordinal))
        {
            return false;
        }

        bool staticAccessor = (accessor.Attributes & MethodAttributes.Static) != 0;
        int receiverCount = staticAccessor ? 0 : 1;
        if (candidateSignature.ParameterTypes.Length
            != accessorSignature.ParameterTypes.Length + receiverCount)
        {
            return false;
        }

        int candidateIndex = 0;
        if (!staticAccessor)
        {
            if (!string.Equals(
                    candidateSignature.ParameterTypes[0],
                    markerSignature.ParameterTypes[0],
                    StringComparison.Ordinal))
            {
                return false;
            }
            candidateIndex = 1;
        }

        for (int i = 0; i < accessorSignature.ParameterTypes.Length; i++)
        {
            if (!string.Equals(
                    candidateSignature.ParameterTypes[candidateIndex + i],
                    accessorSignature.ParameterTypes[i],
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadMethodSignature(
        MetadataReader reader,
        MethodDefinition method,
        GenericContext context,
        Action<int>? beforeMaterialize,
        out MethodSignature<string> signature)
    {
        beforeMaterialize?.Invoke(reader.GetBlobReader(method.Signature).Length);
        return GuardedSignatureText.MethodText(reader, method, context)
            .TryGetValue(out signature);
    }

    private static string ReadStructuralMethodName(
        MetadataReader reader,
        MethodDefinition method,
        Action<int>? beforeMaterialize)
    {
        beforeMaterialize?.Invoke(reader.GetBlobReader(method.Name).Length);
        return MetadataSafetyPolicy.ReadStructuralString(reader, method.Name);
    }

    private static bool IsIncludedAccessor(
        MetadataReader reader,
        MethodDefinitionHandle accessorHandle,
        bool includeAll)
        => !accessorHandle.IsNil
        && (includeAll
            || (reader.GetMethodDefinition(accessorHandle).Attributes
                & MethodAttributes.MemberAccessMask) == MethodAttributes.Public);

    private static bool IsHiddenAccessor(
        MetadataReader reader,
        MethodDefinitionHandle accessorHandle)
        => AttributeReader.HasHiddenAttribute(
            reader,
            reader.GetMethodDefinition(accessorHandle).GetCustomAttributes());

    private static bool TryGetExtensionMarkerName(
        MetadataReader reader,
        PropertyDefinition property,
        PropertyAccessors accessors,
        out string? markerName,
        Action<int>? beforeMaterialize = null)
        => AttributeReader.TryGetExtensionMarkerName(
                reader,
                property.GetCustomAttributes(),
                out markerName,
                beforeMaterialize)
            || !accessors.Getter.IsNil
            && AttributeReader.TryGetExtensionMarkerName(
                reader,
                reader.GetMethodDefinition(accessors.Getter).GetCustomAttributes(),
                out markerName,
                beforeMaterialize)
            || !accessors.Setter.IsNil
            && AttributeReader.TryGetExtensionMarkerName(
                reader,
                reader.GetMethodDefinition(accessors.Setter).GetCustomAttributes(),
                out markerName,
                beforeMaterialize);

    private static bool TryGetDeclaredExtensionProperty(
        MetadataReader reader,
        TypeDefinition groupingType,
        PropertyDefinition property,
        PropertyAccessors accessors,
        Action<int>? beforeMaterialize,
        out TypeDefinition markerType,
        out MethodDefinition markerMethod)
    {
        markerType = default;
        markerMethod = default;
        if (!TryGetExtensionMarkerName(
                reader,
                property,
                accessors,
                out var markerName,
                beforeMaterialize))
        {
            return false;
        }

        var markerTypeHandle = groupingType.GetNestedTypes().FirstOrDefault(handle =>
            reader.StringComparer.Equals(
                reader.GetTypeDefinition(handle).Name,
                markerName!));
        if (markerTypeHandle.IsNil)
            return false;

        markerType = reader.GetTypeDefinition(markerTypeHandle);
        var markerMethodHandle = markerType.GetMethods().FirstOrDefault(handle =>
            reader.StringComparer.Equals(
                reader.GetMethodDefinition(handle).Name,
                "<Extension>$"));
        if (markerMethodHandle.IsNil)
            return false;

        markerMethod = reader.GetMethodDefinition(markerMethodHandle);
        return true;
    }

    private static string FormatMethodSignature(
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinition method,
        GenericContext? context)
    {
        string name = reader.GetString(method.Name);
        var signature = GuardedSignatureText.MethodText(reader, method, context)
            .GetValueOrThrow();

        // First parameter is the extension receiver, rendered with a 'this ' prefix.
        return SignatureRenderer.RenderDecodedSignature(reader, method, name, signature, extensionThis: true);
    }
}
