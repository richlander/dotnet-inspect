using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DotnetInspector.Metadata;

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
    string Kind = "method");

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
        var normalizedTarget = TypeMatcher.Normalize(targetType);

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
            string fullClassName = reader.GetFullTypeName(typeDef);

            // Track property accessors seen (for deduplication of get_/set_ pairs)
            var seenPropertyNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if ((method.Attributes & MethodAttributes.Public) == 0) continue;
                if ((method.Attributes & MethodAttributes.Static) == 0) continue;

                string methodName = reader.GetString(method.Name);

                // C# 14 extension properties: get_/set_ accessors without [Extension] on the method
                bool hasExtension = AttributeReader.HasExtensionAttribute(reader, method.GetCustomAttributes());
                if (!hasExtension &&
                    (methodName.StartsWith("get_", StringComparison.Ordinal) ||
                     methodName.StartsWith("set_", StringComparison.Ordinal)))
                {
                    // Skip hidden methods unless includeAll
                    if (!includeAll && AttributeReader.HasHiddenAttribute(reader, method.GetCustomAttributes()))
                        continue;

                    var context = GenericContext.ForMethod(reader, typeDef, method);
                    var signature = method.DecodeSignature(SignatureDecoder.Instance, context);

                    // Must have at least one parameter (the receiver); skip static extension properties (no receiver)
                    if (signature.ParameterTypes.Length == 0) continue;

                    var extendedType = signature.ParameterTypes[0];
                    var normalizedExtended = TypeMatcher.Normalize(extendedType);

                    if (TypeMatcher.Matches(normalizedExtended, normalizedTarget))
                    {
                        string propertyName = methodName.Substring(4); // strip get_ or set_
                        if (!seenPropertyNames.Add(propertyName)) continue; // deduplicate

                        var propSignature = FormatPropertySignature(signature, extendedType, propertyName);
                        yield return new ExtensionMethodInfo(
                            MethodName: propertyName,
                            ExtensionClass: fullClassName,
                            Namespace: classNs,
                            ExtendedType: extendedType,
                            Signature: propSignature,
                            Kind: "property");
                    }
                    continue;
                }

                if (!hasExtension) continue;

                // Skip hidden methods unless includeAll
                if (!includeAll && AttributeReader.HasHiddenAttribute(reader, method.GetCustomAttributes()))
                    continue;

                // Decode signature to get first parameter type
                {
                    var context = GenericContext.ForMethod(reader, typeDef, method);
                    var signature = method.DecodeSignature(SignatureDecoder.Instance, context);
                    if (signature.ParameterTypes.Length == 0) continue;

                    var extendedType = signature.ParameterTypes[0];
                    var normalizedExtended = TypeMatcher.Normalize(extendedType);

                    // Match against target
                    if (TypeMatcher.Matches(normalizedExtended, normalizedTarget))
                    {
                        yield return new ExtensionMethodInfo(
                            MethodName: methodName,
                            ExtensionClass: fullClassName,
                            Namespace: classNs,
                            ExtendedType: extendedType,
                            Signature: FormatMethodSignature(reader, typeDef, method, context));
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
            string fullClassName = reader.GetFullTypeName(typeDef);

            var seenPropertyNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if ((method.Attributes & MethodAttributes.Public) == 0) continue;
                if ((method.Attributes & MethodAttributes.Static) == 0) continue;

                string methodName = reader.GetString(method.Name);

                bool hasExtension = AttributeReader.HasExtensionAttribute(reader, method.GetCustomAttributes());
                if (!hasExtension &&
                    (methodName.StartsWith("get_", StringComparison.Ordinal) ||
                     methodName.StartsWith("set_", StringComparison.Ordinal)))
                {
                    if (!includeAll && AttributeReader.HasHiddenAttribute(reader, method.GetCustomAttributes()))
                        continue;

                    var context = GenericContext.ForMethod(reader, typeDef, method);
                    var signature = method.DecodeSignature(SignatureDecoder.Instance, context);
                    if (signature.ParameterTypes.Length == 0) continue;

                    var extendedType = signature.ParameterTypes[0];
                    string propertyName = methodName.Substring(4);
                    if (!seenPropertyNames.Add(propertyName)) continue;

                    var propSignature = FormatPropertySignature(signature, extendedType, propertyName);
                    yield return new ExtensionMethodInfo(
                        MethodName: propertyName,
                        ExtensionClass: fullClassName,
                        Namespace: classNs,
                        ExtendedType: extendedType,
                        Signature: propSignature,
                        Kind: "property");
                    continue;
                }

                if (!hasExtension) continue;

                if (!includeAll && AttributeReader.HasHiddenAttribute(reader, method.GetCustomAttributes()))
                    continue;

                {
                    var context = GenericContext.ForMethod(reader, typeDef, method);
                    var signature = method.DecodeSignature(SignatureDecoder.Instance, context);
                    if (signature.ParameterTypes.Length == 0) continue;

                    var extendedType = signature.ParameterTypes[0];

                    yield return new ExtensionMethodInfo(
                        MethodName: methodName,
                        ExtensionClass: fullClassName,
                        Namespace: classNs,
                        ExtendedType: extendedType,
                        Signature: FormatMethodSignature(reader, typeDef, method, context));
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

        while (toProcess.Count > 0)
        {
            var (currentType, currentPath, remainingDepth) = toProcess.Dequeue();
            if (remainingDepth <= 0) continue;

            // Find this type in any assembly
            foreach (var assemblyPath in assemblyPaths)
            {
                try
                {
                    using var stream = File.OpenRead(assemblyPath);
                    using var peReader = new PEReader(stream);
                    if (!peReader.HasMetadata) continue;

                    var reader = peReader.GetMetadataReader();

                    foreach (var typeDefHandle in reader.TypeDefinitions)
                    {
                        var typeDef = reader.GetTypeDefinition(typeDefHandle);
                        string typeName = reader.GetString(typeDef.Name);
                        string fullName = reader.GetFullTypeName(typeDef);

                        // Match by simple name or full name
                        if (!typeName.Equals(currentType, StringComparison.OrdinalIgnoreCase) &&
                            !fullName.Equals(currentType, StringComparison.OrdinalIgnoreCase) &&
                            !typeName.Equals(currentType.Split('.').Last(), StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Found the type - extract reachable types from its members
                        var context = GenericContext.ForType(reader, typeDef);

                        // Properties
                        foreach (var propHandle in typeDef.GetProperties())
                        {
                            var prop = reader.GetPropertyDefinition(propHandle);
                            var propName = reader.GetString(prop.Name);

                            try
                            {
                                var sig = prop.DecodeSignature(SignatureDecoder.Instance, context);
                                var propType = UnwrapAsyncType(sig.ReturnType);

                                if (!string.IsNullOrEmpty(propType) && !IsPrimitiveType(propType) && visited.Add(propType))
                                {
                                    var path = $"{currentPath}.{propName}";
                                    results.Add((propType, path));
                                    toProcess.Enqueue((propType, path, remainingDepth - 1));
                                }
                            }
                            // Skip properties with undecodable signatures
                            catch { }
                        }

                        // Methods (return types)
                        foreach (var methodHandle in typeDef.GetMethods())
                        {
                            var method = reader.GetMethodDefinition(methodHandle);
                            if ((method.Attributes & MethodAttributes.Public) == 0) continue;

                            string methodName = reader.GetString(method.Name);
                            if (methodName.StartsWith("get_") || methodName.StartsWith("set_") ||
                                methodName.StartsWith("add_") || methodName.StartsWith("remove_") ||
                                methodName.StartsWith("."))
                                continue;

                            try
                            {
                                var methodContext = GenericContext.ForMethod(reader, typeDef, method);
                                var sig = method.DecodeSignature(SignatureDecoder.Instance, methodContext);
                                var retType = UnwrapAsyncType(sig.ReturnType);

                                if (!string.IsNullOrEmpty(retType) && retType != "void" &&
                                    !IsPrimitiveType(retType) && visited.Add(retType))
                                {
                                    var path = $"{currentPath}.{methodName}()";
                                    results.Add((retType, path));
                                    toProcess.Enqueue((retType, path, remainingDepth - 1));
                                }
                            }
                            // Skip methods with undecodable signatures
                            catch { }
                        }

                        goto foundType;
                    }
                }
                // Skip assemblies with unreadable metadata
                catch { }
            }
            foundType:;
        }

        return results;
    }

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

    private static string FormatPropertySignature(
        MethodSignature<string> signature, string extendedType, string propertyName)
    {
        // For a getter: ReturnType PropertyName { get; } — receiver is the first param
        // For a setter: void PropertyName { set; } — receiver is the first param, value is the second
        var returnType = signature.ReturnType;
        if (returnType == "void" && signature.ParameterTypes.Length >= 2)
        {
            // This is a setter — the property type is the second parameter
            returnType = signature.ParameterTypes[1];
        }
        return $"{returnType} {propertyName} {{ get; }}";
    }

    private static string FormatMethodSignature(
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinition method,
        GenericContext? context)
    {
        string name = reader.GetString(method.Name);
        var signature = method.DecodeSignature(SignatureDecoder.Instance, context);

        var paramHandles = method.GetParameters().ToList();
        var paramTypes = signature.ParameterTypes;

        List<string> parameters = [];
        for (int i = 0; i < paramTypes.Length; i++)
        {
            string type = paramTypes[i];
            string? paramName = null;
            foreach (var handle in paramHandles)
            {
                var param = reader.GetParameter(handle);
                if (param.SequenceNumber == i + 1)
                {
                    paramName = reader.GetString(param.Name);
                    break;
                }
            }

            // Mark first parameter as 'this' for extension methods
            var prefix = i == 0 ? "this " : "";
            parameters.Add($"{prefix}{type} {paramName ?? $"arg{i}"}");
        }

        return $"{signature.ReturnType} {name}({string.Join(", ", parameters)})";
    }
}
