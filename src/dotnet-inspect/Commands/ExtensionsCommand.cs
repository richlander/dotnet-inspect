using System.Collections.Immutable;
using DotnetInspector.Packages;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Finds extension methods for a target type across packages, assemblies, and platform frameworks.
/// </summary>
public class ExtensionsCommand
{
    public static async Task<int> ExecuteAsync(string targetType, ExtensionsOptions options)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        var tempDirs = new List<string>();

        try
        {
            // Default to runtime framework if no scope specified
            if (!options.HasAnyScope)
            {
                logger.Log("No scope specified, defaulting to --framework runtime");
                options = options with { PlatformFrameworks = ["runtime"] };
            }

            var results = new List<ExtensionMethodResult>();

            // Collect all assembly paths from various sources
            var assemblyInfos = await AssemblyCollector.CollectAsync(context.HttpClient, options, tempDirs, logger, "inspect-ext");

            // Scan assemblies for extension methods
            foreach (var asmInfo in assemblyInfos)
            {
                var extensions = ScanForExtensions(asmInfo.Path, targetType, options.IncludeAll, logger);
                foreach (var ext in extensions)
                {
                    ext.Source = asmInfo.Source;
                    ext.SourceVersion = asmInfo.Version;
                }
                results.AddRange(extensions);
            }

            // If --reachable, find extensions on reachable types
            if (options.Reachable && results.Count > 0)
            {
                var reachableTypes = FindReachableTypes(targetType, assemblyInfos, options.Depth, logger);
                foreach (var (reachableType, path) in reachableTypes)
                {
                    if (reachableType == targetType) continue;
                    
                    foreach (var asmInfo in assemblyInfos)
                    {
                        var extensions = ScanForExtensions(asmInfo.Path, reachableType, options.IncludeAll, logger);
                        foreach (var ext in extensions)
                        {
                            ext.Source = asmInfo.Source;
                            ext.SourceVersion = asmInfo.Version;
                            ext.ReachablePath = path;
                            ext.ReachableFromType = reachableType;
                        }
                        results.AddRange(extensions);
                    }
                }
            }

            // Apply limit
            if (options.Limit.HasValue && results.Count > options.Limit.Value)
            {
                results = results.Take(options.Limit.Value).ToList();
            }

            // Output results
            if (options.JsonOutput)
            {
                WriteJsonOutput(results, options.CompactJson);
            }
            else
            {
                WriteMarkoutOutput(targetType, results);
            }

            return 0;
        }
        finally
        {
            AssemblyCollector.CleanupTempDirs(tempDirs);
        }
    }

    private static List<ExtensionMethodResult> ScanForExtensions(
        string assemblyPath, 
        string targetType,
        bool includeAll,
        VerboseLogger logger)
    {
        var results = new List<ExtensionMethodResult>();
        var normalizedTarget = TypeMatcher.Normalize(targetType);

        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            
            if (!peReader.HasMetadata) return results;
            
            var reader = peReader.GetMetadataReader();
            var provider = new ExtensionSignatureTypeProvider();

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

                string className = reader.GetString(typeDef.Name);
                string classNs = reader.GetString(typeDef.Namespace);
                string fullClassName = string.IsNullOrEmpty(classNs) ? className : $"{classNs}.{className}";

                foreach (var methodHandle in typeDef.GetMethods())
                {
                    var method = reader.GetMethodDefinition(methodHandle);
                    if ((method.Attributes & MethodAttributes.Public) == 0) continue;
                    if ((method.Attributes & MethodAttributes.Static) == 0) continue;
                    if (!AttributeReader.HasExtensionAttribute(reader, method.GetCustomAttributes())) continue;

                    // Decode signature to get first parameter type
                    var signature = method.DecodeSignature(provider, null);
                    if (signature.ParameterTypes.Length == 0) continue;

                    var extendedType = signature.ParameterTypes[0];
                    var normalizedExtended = TypeMatcher.Normalize(extendedType);

                    // Match against target
                    if (TypeMatcher.Matches(normalizedExtended, normalizedTarget))
                    {
                        string methodName = reader.GetString(method.Name);
                        results.Add(new ExtensionMethodResult
                        {
                            MethodName = methodName,
                            ExtensionClass = fullClassName,
                            ExtendedType = extendedType,
                            Assembly = Path.GetFileNameWithoutExtension(assemblyPath),
                            Signature = FormatMethodSignature(reader, typeDef, method, provider)
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning {assemblyPath}: {ex.Message}");
        }

        return results;
    }

    private static List<(string type, string path)> FindReachableTypes(
        string targetType,
        List<AssemblyCollector.AssemblyInfo> assemblyPaths,
        int maxDepth,
        VerboseLogger logger)
    {
        var results = new List<(string type, string path)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var provider = new ExtensionSignatureTypeProvider();
        var toProcess = new Queue<(string typeName, string path, int depth)>();
        
        toProcess.Enqueue((targetType, "", maxDepth));
        visited.Add(targetType);

        while (toProcess.Count > 0)
        {
            var (currentType, currentPath, remainingDepth) = toProcess.Dequeue();
            if (remainingDepth <= 0) continue;

            // Find this type in any assembly
            foreach (var asmInfo in assemblyPaths)
            {
                try
                {
                    using var stream = File.OpenRead(asmInfo.Path);
                    using var peReader = new PEReader(stream);
                    if (!peReader.HasMetadata) continue;
                    
                    var reader = peReader.GetMetadataReader();
                    
                    foreach (var typeDefHandle in reader.TypeDefinitions)
                    {
                        var typeDef = reader.GetTypeDefinition(typeDefHandle);
                        string typeName = reader.GetString(typeDef.Name);
                        string typeNs = reader.GetString(typeDef.Namespace);
                        string fullName = string.IsNullOrEmpty(typeNs) ? typeName : $"{typeNs}.{typeName}";
                        
                        // Match by simple name or full name
                        if (!typeName.Equals(currentType, StringComparison.OrdinalIgnoreCase) &&
                            !fullName.Equals(currentType, StringComparison.OrdinalIgnoreCase) &&
                            !typeName.Equals(currentType.Split('.').Last(), StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Found the type - extract reachable types from its members
                        
                        // Properties
                        foreach (var propHandle in typeDef.GetProperties())
                        {
                            var prop = reader.GetPropertyDefinition(propHandle);
                            var propName = reader.GetString(prop.Name);
                            
                            try
                            {
                                var sig = prop.DecodeSignature(provider, null);
                                var propType = UnwrapAsyncType(sig.ReturnType);
                                
                                if (!string.IsNullOrEmpty(propType) && !IsPrimitiveType(propType) && visited.Add(propType))
                                {
                                    var path = $"{currentPath}.{propName}";
                                    results.Add((propType, path));
                                    toProcess.Enqueue((propType, path, remainingDepth - 1));
                                }
                            }
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
                                var sig = method.DecodeSignature(provider, null);
                                var retType = UnwrapAsyncType(sig.ReturnType);
                                
                                if (!string.IsNullOrEmpty(retType) && retType != "Void" && 
                                    !IsPrimitiveType(retType) && visited.Add(retType))
                                {
                                    var path = $"{currentPath}.{methodName}()";
                                    results.Add((retType, path));
                                    toProcess.Enqueue((retType, path, remainingDepth - 1));
                                }
                            }
                            catch { }
                        }
                        
                        goto foundType; // Break out of both loops once we've processed this type
                    }
                }
                catch { }
            }
            foundType:;
        }

        return results;
    }

    private static string UnwrapAsyncType(string type)
    {
        if (type.StartsWith("Task<") && type.EndsWith(">"))
            return type.Substring(5, type.Length - 6);
        if (type.StartsWith("ValueTask<") && type.EndsWith(">"))
            return type.Substring(10, type.Length - 11);
        if (type.StartsWith("Task") && !type.Contains("<"))
            return "";
        return type;
    }

    private static bool IsPrimitiveType(string type) => type switch
    {
        "String" or "Int32" or "Int64" or "Boolean" or "Byte" or "Void" or
        "Double" or "Single" or "Decimal" or "Char" or "Object" or
        "TimeSpan" or "DateTime" or "DateTimeOffset" or "Guid" or
        "Uri" or "Version" or "CancellationToken" => true,
        _ when type.EndsWith("[]") => true,
        _ => false
    };

    private static string FormatMethodSignature(
        MetadataReader reader, 
        TypeDefinition typeDef, 
        MethodDefinition method,
        ExtensionSignatureTypeProvider provider)
    {
        string name = reader.GetString(method.Name);
        var signature = method.DecodeSignature(provider, null);
        
        var paramHandles = method.GetParameters().ToList();
        var paramTypes = signature.ParameterTypes;
        
        var parameters = new List<string>();
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

    private static void WriteJsonOutput(List<ExtensionMethodResult> results, bool compact)
    {
        var typeInfo = compact
            ? ExtensionsCompactJsonContext.Default.ListExtensionMethodResult
            : ExtensionsJsonContext.Default.ListExtensionMethodResult;
        Console.WriteLine(JsonSerializer.Serialize(results, typeInfo));
    }

    private static void WriteMarkoutOutput(string targetType, List<ExtensionMethodResult> results)
    {
        var writer = new MarkoutWriter();

        writer.WriteHeading(1, $"Extension Methods for {targetType}");

        if (results.Count == 0)
        {
            writer.WriteParagraph("No extension methods found.");
            Console.Write(writer.ToString());
            return;
        }

        // Group by source, then by reachable path
        var directExtensions = results.Where(r => r.ReachablePath == null).ToList();
        var reachableExtensions = results.Where(r => r.ReachablePath != null)
            .GroupBy(r => r.ReachablePath)
            .ToList();

        // Direct extensions
        if (directExtensions.Count > 0)
        {
            writer.WriteHeading(2, $"Direct Extensions ({directExtensions.Count})");
            WriteExtensionTable(writer, directExtensions);
        }

        // Reachable extensions
        foreach (var group in reachableExtensions)
        {
            writer.WriteHeading(2, $"Via {group.Key} ({group.First().ReachableFromType}) ({group.Count()})");
            WriteExtensionTable(writer, group.ToList());
        }

        Console.Write(writer.ToString());
    }

    private static void WriteExtensionTable(MarkoutWriter writer, List<ExtensionMethodResult> results)
    {
        // Group by extension class
        var byClass = results.GroupBy(r => r.ExtensionClass).ToList();

        var headers = new[] { "Method", "Class", "Assembly", "Source" };
        var rows = byClass.SelectMany(classGroup => classGroup.Select(ext =>
        {
            var sourceDisplay = ext.SourceVersion != null 
                ? $"{ext.Source}@{ext.SourceVersion}" 
                : ext.Source;
            return new[] { ext.MethodName, ext.ExtensionClass ?? "", ext.Assembly ?? "", sourceDisplay ?? "" };
        }));
        writer.WriteTable(headers, rows);
    }
}

/// <summary>
/// Result of extension method search.
/// </summary>
public class ExtensionMethodResult
{
    [JsonPropertyName("method")]
    public string MethodName { get; set; } = "";

    [JsonPropertyName("class")]
    public string ExtensionClass { get; set; } = "";

    [JsonPropertyName("extended_type")]
    public string ExtendedType { get; set; } = "";

    [JsonPropertyName("assembly")]
    public string Assembly { get; set; } = "";

    [JsonPropertyName("signature")]
    public string? Signature { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("source_version")]
    public string? SourceVersion { get; set; }

    [JsonPropertyName("reachable_path")]
    public string? ReachablePath { get; set; }

    [JsonPropertyName("reachable_from_type")]
    public string? ReachableFromType { get; set; }
}

/// <summary>
/// Simple signature type provider for extension method scanning.
/// </summary>
internal class ExtensionSignatureTypeProvider : ISignatureTypeProvider<string, object?>
{
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
    
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var typeDef = reader.GetTypeDefinition(handle);
        string ns = reader.GetString(typeDef.Namespace);
        string name = reader.GetString(typeDef.Name);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }
    
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var typeRef = reader.GetTypeReference(handle);
        string ns = reader.GetString(typeRef.Namespace);
        string name = reader.GetString(typeRef.Name);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }
    
    public string GetSZArrayType(string elementType) => $"{elementType}[]";
    
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        => $"{genericType}<{string.Join(", ", typeArguments)}>";
    
    public string GetArrayType(string elementType, ArrayShape shape) 
        => $"{elementType}[{new string(',', shape.Rank - 1)}]";
    
    public string GetByReferenceType(string elementType) => $"ref {elementType}";
    
    public string GetPointerType(string elementType) => $"{elementType}*";
    
    public string GetFunctionPointerType(MethodSignature<string> signature) => "delegate*";
    
    public string GetGenericMethodParameter(object? context, int index) => $"T{index}";
    
    public string GetGenericTypeParameter(object? context, int index) => $"T{index}";
    
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    
    public string GetPinnedType(string elementType) => elementType;
    
    public string GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        var typeSpec = reader.GetTypeSpecification(handle);
        return typeSpec.DecodeSignature(this, context);
    }
}

[JsonSerializable(typeof(List<ExtensionMethodResult>))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class ExtensionsJsonContext : JsonSerializerContext { }

[JsonSerializable(typeof(List<ExtensionMethodResult>))]
[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class ExtensionsCompactJsonContext : JsonSerializerContext { }
