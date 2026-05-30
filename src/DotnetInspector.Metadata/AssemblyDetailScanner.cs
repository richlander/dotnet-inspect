using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace DotnetInspector.Metadata;

/// <summary>
/// Information about an assembly-level or module-level custom attribute.
/// </summary>
public record AssemblyAttributeInfo(
    string Name,
    string Target,
    string? Value);

/// <summary>
/// Information about a type forwarder.
/// </summary>
public record TypeForwarderInfo(
    string TypeName,
    string TargetAssembly);

/// <summary>
/// Scans assemblies for custom attributes and type forwarders.
/// </summary>
public static class AssemblyDetailScanner
{
    /// <summary>
    /// Extracts assembly-level and module-level custom attributes.
    /// Skips well-known metadata attributes that are already surfaced elsewhere.
    /// </summary>
    public static List<AssemblyAttributeInfo> ScanCustomAttributes(PEReader peReader)
    {
        List<AssemblyAttributeInfo> results = [];

        if (!peReader.HasMetadata)
            return results;

        var reader = peReader.GetMetadataReader();

        // Assembly-level attributes
        if (reader.IsAssembly)
        {
            var assemblyDef = reader.GetAssemblyDefinition();
            foreach (var attrHandle in assemblyDef.GetCustomAttributes())
            {
                var attr = reader.GetCustomAttribute(attrHandle);
                string? name = AssemblyInspector.GetAttributeName(reader, attr);
                if (name == null || IsWellKnownMetadataAttribute(name))
                    continue;

                string shortName = GetShortAttributeName(name);
                string? value = TryGetAttributeDisplayValue(reader, attr);
                results.Add(new AssemblyAttributeInfo(shortName, "Assembly", value));
            }
        }

        // Module-level attributes
        var moduleDef = reader.GetModuleDefinition();
        foreach (var attrHandle in moduleDef.GetCustomAttributes())
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            string? name = AssemblyInspector.GetAttributeName(reader, attr);
            if (name == null || IsWellKnownMetadataAttribute(name))
                continue;

            string shortName = GetShortAttributeName(name);
            string? value = TryGetAttributeDisplayValue(reader, attr);
            results.Add(new AssemblyAttributeInfo(shortName, "Module", value));
        }

        return results;
    }

    /// <summary>
    /// Extracts type forwarders from the ExportedTypes table.
    /// </summary>
    public static List<TypeForwarderInfo> ScanTypeForwarders(PEReader peReader)
    {
        List<TypeForwarderInfo> results = [];

        if (!peReader.HasMetadata)
            return results;

        var reader = peReader.GetMetadataReader();

        foreach (var handle in reader.ExportedTypes)
        {
            var exportedType = reader.GetExportedType(handle);

            if (!exportedType.IsForwarder)
                continue;

            var ns = reader.GetString(exportedType.Namespace);
            var name = reader.GetString(exportedType.Name);
            var fullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";

            string targetAssembly = "";
            if (exportedType.Implementation.Kind == HandleKind.AssemblyReference)
            {
                var assemblyRef = reader.GetAssemblyReference((AssemblyReferenceHandle)exportedType.Implementation);
                targetAssembly = reader.GetString(assemblyRef.Name);
            }

            results.Add(new TypeForwarderInfo(fullName, targetAssembly));
        }

        return results;
    }

    /// <summary>
    /// Gets the file size of an assembly.
    /// </summary>
    public static long GetFileSize(string path) => new FileInfo(path).Length;

    /// <summary>
    /// Attributes already surfaced in Library Info — skip in custom attributes section.
    /// </summary>
    private static bool IsWellKnownMetadataAttribute(string name) => name switch
    {
        "System.Runtime.Versioning.TargetFrameworkAttribute" => true,
        "System.Reflection.AssemblyFileVersionAttribute" => true,
        "System.Reflection.AssemblyInformationalVersionAttribute" => true,
        "System.Reflection.AssemblyProductAttribute" => true,
        "System.Reflection.AssemblyCompanyAttribute" => true,
        "System.Reflection.AssemblyCopyrightAttribute" => true,
        "System.Reflection.AssemblyDescriptionAttribute" => true,
        "System.Reflection.AssemblyConfigurationAttribute" => true,
        "System.Reflection.AssemblyTitleAttribute" => true,
        "System.Reflection.AssemblyMetadataAttribute" => true,
        // Compiler-generated noise
        "System.Runtime.CompilerServices.CompilationRelaxationsAttribute" => true,
        "System.Runtime.CompilerServices.RuntimeCompatibilityAttribute" => true,
        "System.Diagnostics.DebuggableAttribute" => true,
        "System.Runtime.CompilerServices.RefSafetyRulesAttribute" => true,
        "System.Runtime.CompilerServices.NullablePublicOnlyAttribute" => true,
        "System.Runtime.CompilerServices.NullableContextAttribute" => true,
        "System.Runtime.CompilerServices.NullableAttribute" => true,
        _ => false
    };

    private static string GetShortAttributeName(string fullName)
    {
        // Remove "Attribute" suffix and namespace
        var name = fullName;
        if (name.EndsWith("Attribute", StringComparison.Ordinal))
            name = name[..^9];

        var lastDot = name.LastIndexOf('.');
        return lastDot >= 0 ? name[(lastDot + 1)..] : name;
    }

    private static string? TryGetAttributeDisplayValue(MetadataReader reader, CustomAttribute attr)
    {
        try
        {
            var blob = reader.GetBlobReader(attr.Value);
            if (blob.Length < 2) return null;
            blob.ReadUInt16(); // prolog

            // Try reading a single string argument
            var value = blob.ReadSerializedString();
            if (value == null) return null;

            // Filter out binary/control character values
            foreach (char c in value)
            {
                if (char.IsControl(c) && c != '\t' && c != '\n' && c != '\r')
                    return null;
            }

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Cheap presence flags for section discovery. Single MetadataReader pass,
    /// short-circuits at first match for each flag.
    /// </summary>
    public static PresenceFlags ScanPresenceFlags(PEReader peReader)
    {
        var reader = peReader.GetMetadataReader();
        var flags = new PresenceFlags();

        // Resources: cheapest check — just a count
        flags.HasManifestResources = reader.GetTableRowCount(TableIndex.ManifestResource) > 0;

        // Type forwarders: iterate ExportedTypes, stop at first forwarder
        foreach (var handle in reader.ExportedTypes)
        {
            if (reader.GetExportedType(handle).IsForwarder)
            {
                flags.HasTypeForwarders = true;
                break;
            }
        }

        // Custom attributes: check assembly + module level for non-well-known
        if (reader.IsAssembly)
        {
            foreach (var attrHandle in reader.GetAssemblyDefinition().GetCustomAttributes())
            {
                var name = AssemblyInspector.GetAttributeName(reader, reader.GetCustomAttribute(attrHandle));
                if (name != null && !IsWellKnownMetadataAttribute(name))
                {
                    flags.HasAssemblyAttributes = true;
                    break;
                }
            }
        }

        if (!flags.HasAssemblyAttributes)
        {
            foreach (var attrHandle in reader.GetModuleDefinition().GetCustomAttributes())
            {
                var name = AssemblyInspector.GetAttributeName(reader, reader.GetCustomAttribute(attrHandle));
                if (name != null && !IsWellKnownMetadataAttribute(name))
                {
                    flags.HasAssemblyAttributes = true;
                    break;
                }
            }
        }

        // Extension types, P/Invoke, unsafe, async: iterate TypeDefs once
        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            if (flags.HasExtensionTypes && flags.HasPInvokeImports && flags.HasUnsafeCode
                && flags.HasRuntimeAsync && flags.HasStateMachineAsync)
                break;

            var typeDef = reader.GetTypeDefinition(typeDefHandle);

            // Extension types: static class with [Extension] attribute
            if (!flags.HasExtensionTypes)
            {
                var attrs = typeDef.Attributes;
                bool isStatic = (attrs & TypeAttributes.Sealed) != 0
                             && (attrs & TypeAttributes.Abstract) != 0;
                if (isStatic && AttributeReader.HasExtensionAttribute(reader, typeDef.GetCustomAttributes()))
                    flags.HasExtensionTypes = true;
            }

            // Async detection matches MethodClassificationScanner's public-surface filter:
            // skip compiler-generated types (names starting with '<').
            bool considerAsync = (!flags.HasRuntimeAsync || !flags.HasStateMachineAsync)
                                 && !reader.GetString(typeDef.Name).StartsWith('<');

            // P/Invoke, unsafe, async: check methods
            if (!flags.HasPInvokeImports || !flags.HasUnsafeCode || considerAsync)
            {
                foreach (var methodHandle in typeDef.GetMethods())
                {
                    if (flags.HasPInvokeImports && flags.HasUnsafeCode
                        && flags.HasRuntimeAsync && flags.HasStateMachineAsync)
                        break;

                    var method = reader.GetMethodDefinition(methodHandle);

                    if (!flags.HasPInvokeImports
                        && (method.Attributes & MethodAttributes.PinvokeImpl) != 0)
                        flags.HasPInvokeImports = true;

                    if (!flags.HasUnsafeCode)
                    {
                        try
                        {
                            var sig = method.DecodeSignature(PointerDetector.Instance, null);
                            if (sig.ReturnType || sig.ParameterTypes.Any(p => p))
                                flags.HasUnsafeCode = true;
                        }
                        // Skip methods with undecodable signatures
                        catch { }
                    }

                    if (considerAsync && (!flags.HasRuntimeAsync || !flags.HasStateMachineAsync)
                        && IsPublicNonAccessor(reader, method))
                    {
                        const MethodImplAttributes AsyncImplFlag = (MethodImplAttributes)0x2000;
                        if (!flags.HasRuntimeAsync && (method.ImplAttributes & AsyncImplFlag) != 0)
                            flags.HasRuntimeAsync = true;
                        else if (!flags.HasStateMachineAsync
                            && (AttributeReader.HasAttribute(reader, method.GetCustomAttributes(), "System.Runtime.CompilerServices.AsyncStateMachineAttribute")
                                || AttributeReader.HasAttribute(reader, method.GetCustomAttributes(), "System.Runtime.CompilerServices.AsyncIteratorStateMachineAttribute")))
                            flags.HasStateMachineAsync = true;
                    }
                }
            }
        }

        return flags;
    }

    private static bool IsPublicNonAccessor(MetadataReader reader, MethodDefinition method)
    {
        if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
            return false;

        string name = reader.GetString(method.Name);
        return !name.StartsWith("get_", StringComparison.Ordinal)
            && !name.StartsWith("set_", StringComparison.Ordinal)
            && !name.StartsWith("add_", StringComparison.Ordinal)
            && !name.StartsWith("remove_", StringComparison.Ordinal);
    }
}

/// <summary>
/// Lightweight presence flags populated from a single MetadataReader pass.
/// </summary>
public class PresenceFlags
{
    public bool HasExtensionTypes { get; set; }
    public bool HasPInvokeImports { get; set; }
    public bool HasUnsafeCode { get; set; }
    public bool HasManifestResources { get; set; }
    public bool HasAssemblyAttributes { get; set; }
    public bool HasTypeForwarders { get; set; }

    /// <summary>Whether the assembly has any public runtime-async methods (impl flag 0x2000).</summary>
    public bool HasRuntimeAsync { get; set; }

    /// <summary>Whether the assembly has any public classic state-machine async methods.</summary>
    public bool HasStateMachineAsync { get; set; }
}


