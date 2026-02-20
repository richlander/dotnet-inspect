using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DotnetInspector.Metadata;

/// <summary>
/// Information about a method with a notable classification (unsafe, P/Invoke, etc.).
/// </summary>
public record ClassifiedMethodInfo(
    string MethodName,
    string DeclaringType,
    string? Namespace,
    string Signature,
    MethodClassification Classification,
    string? ModuleName = null);

/// <summary>
/// Classification of a method based on its metadata characteristics.
/// </summary>
public enum MethodClassification
{
    Unsafe,
    PInvoke
}

/// <summary>
/// Scans assemblies for methods with notable classifications: unsafe signatures and P/Invoke imports.
/// </summary>
public static class MethodClassificationScanner
{
    /// <summary>
    /// Finds all unsafe and P/Invoke methods in an assembly.
    /// </summary>
    public static List<ClassifiedMethodInfo> Scan(Stream peStream)
    {
        using var peReader = new PEReader(peStream);
        return Scan(peReader);
    }

    /// <summary>
    /// Finds all unsafe and P/Invoke methods in an assembly.
    /// </summary>
    public static List<ClassifiedMethodInfo> Scan(PEReader peReader)
    {
        List<ClassifiedMethodInfo> results = [];

        if (!peReader.HasMetadata)
            return results;

        var reader = peReader.GetMetadataReader();

        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);

            // Skip compiler-generated types
            string typeName = reader.GetString(typeDef.Name);
            if (typeName.StartsWith("<", StringComparison.Ordinal))
                continue;

            string ns = reader.GetString(typeDef.Namespace);
            string fullTypeName = reader.GetFullTypeName(typeDef);

            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                    continue;

                string methodName = reader.GetString(method.Name);

                // Skip accessors and constructors
                if (methodName.StartsWith("get_", StringComparison.Ordinal) ||
                    methodName.StartsWith("set_", StringComparison.Ordinal) ||
                    methodName.StartsWith("add_", StringComparison.Ordinal) ||
                    methodName.StartsWith("remove_", StringComparison.Ordinal))
                    continue;

                // Check P/Invoke
                if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0)
                {
                    string? moduleName = GetPInvokeModuleName(reader, methodHandle);
                    var signature = FormatSignature(reader, typeDef, method);
                    results.Add(new ClassifiedMethodInfo(
                        methodName, fullTypeName, ns, signature,
                        MethodClassification.PInvoke, moduleName));
                    continue; // P/Invoke methods are also "unsafe" but classify as P/Invoke
                }

                // Check unsafe (pointer types in signature)
                try
                {
                    var context = GenericContext.ForMethod(reader, typeDef, method);
                    var sig = method.DecodeSignature(SignatureDecoder.Instance, context);
                    if (HasPointerType(sig))
                    {
                        var signature = FormatDecodedSignature(reader, method, methodName, sig);
                        results.Add(new ClassifiedMethodInfo(
                            methodName, fullTypeName, ns, signature,
                            MethodClassification.Unsafe));
                    }
                }
                catch
                {
                    // Skip methods with unresolvable signatures
                }
            }
        }

        return results;
    }

    private static bool HasPointerType(MethodSignature<string> signature)
    {
        if (signature.ReturnType.Contains('*'))
            return true;

        foreach (var paramType in signature.ParameterTypes)
        {
            if (paramType.Contains('*'))
                return true;
        }

        return false;
    }

    private static string? GetPInvokeModuleName(MetadataReader reader, MethodDefinitionHandle methodHandle)
    {
        var import = reader.GetMethodDefinition(methodHandle).GetImport();
        if (import.Module.IsNil)
            return null;

        var moduleRef = reader.GetModuleReference(import.Module);
        return reader.GetString(moduleRef.Name);
    }

    private static string FormatSignature(MetadataReader reader, TypeDefinition typeDef, MethodDefinition method)
    {
        try
        {
            var context = GenericContext.ForMethod(reader, typeDef, method);
            var sig = method.DecodeSignature(SignatureDecoder.Instance, context);
            string methodName = reader.GetString(method.Name);
            return FormatDecodedSignature(reader, method, methodName, sig);
        }
        catch
        {
            return reader.GetString(method.Name) + "(...)";
        }
    }

    private static string FormatDecodedSignature(
        MetadataReader reader, MethodDefinition method, string name, MethodSignature<string> signature)
    {
        var paramHandles = method.GetParameters();
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
            parameters.Add($"{type} {paramName ?? $"arg{i}"}");
        }

        return $"{signature.ReturnType} {name}({string.Join(", ", parameters)})";
    }
}
