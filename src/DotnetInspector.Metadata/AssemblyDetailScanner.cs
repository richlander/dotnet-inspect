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
/// Metadata audit signals found in assembly/module/member metadata.
/// </summary>
public record AssemblyAuditMetadata
{
    public bool? IsTrimmable { get; init; }
    public bool? IsAotCompatible { get; init; }
    public int? MemorySafetyRulesVersion { get; init; }
    public bool HasDisableRuntimeMarshalling { get; init; }
    public int RequiresUnsafeCount { get; init; }
    public int RequiresUnreferencedCodeCount { get; init; }
    public int RequiresDynamicCodeCount { get; init; }
    public int RequiresAssemblyFilesCount { get; init; }
    public int DynamicDependencyCount { get; init; }
    public int PInvokeMethodCount { get; init; }
}

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

        const string assemblyMetadataAttributeName = "System.Reflection.AssemblyMetadataAttribute";

        void addAttributes(CustomAttributeHandleCollection handles, string target)
        {
            foreach (var attrHandle in handles)
            {
                var attr = reader.GetCustomAttribute(attrHandle);
                string? name = AssemblyInspector.GetAttributeName(reader, attr);
                if (name == null || IsWellKnownMetadataAttribute(name))
                    continue;

                if (name == assemblyMetadataAttributeName)
                {
                    var metadata = TryGetAssemblyMetadataValue(reader, attr);
                    if (metadata is { } kv)
                        results.Add(new AssemblyAttributeInfo($"AssemblyMetadata({kv.Key})", target, kv.Value));
                    continue;
                }

                string shortName = GetShortAttributeName(name);
                string? value = TryGetAttributeDisplayValue(reader, attr);
                results.Add(new AssemblyAttributeInfo(shortName, target, value));
            }
        }

        // Assembly-level attributes
        if (reader.IsAssembly)
            addAttributes(reader.GetAssemblyDefinition().GetCustomAttributes(), "Assembly");

        // Module-level attributes
        addAttributes(reader.GetModuleDefinition().GetCustomAttributes(), "Module");

        return results;
    }

    /// <summary>
    /// Extracts audit-relevant assembly, module, and member metadata.
    /// </summary>
    public static AssemblyAuditMetadata ScanAuditMetadata(PEReader peReader)
    {
        if (!peReader.HasMetadata)
            return new AssemblyAuditMetadata();

        var reader = peReader.GetMetadataReader();
        bool? isTrimmable = null;
        bool? isAotCompatible = null;
        int? memorySafetyRulesVersion = null;
        bool hasDisableRuntimeMarshalling = false;
        int requiresUnsafeCount = 0;
        int requiresUnreferencedCodeCount = 0;
        int requiresDynamicCodeCount = 0;
        int requiresAssemblyFilesCount = 0;
        int dynamicDependencyCount = 0;
        int pInvokeMethodCount = 0;

        void inspect(CustomAttributeHandleCollection handles)
        {
            foreach (var handle in handles)
            {
                var attr = reader.GetCustomAttribute(handle);
                var name = AssemblyInspector.GetAttributeName(reader, attr);
                if (name == null)
                    continue;

                switch (name)
                {
                    case "System.Reflection.AssemblyMetadataAttribute":
                        var metadata = TryGetAssemblyMetadataValue(reader, attr);
                        if (metadata is { } metadataValue)
                        {
                            if (metadataValue.Key == "IsTrimmable")
                                isTrimmable = ParseBool(metadataValue.Value);
                            else if (metadataValue.Key == "IsAotCompatible")
                                isAotCompatible = ParseBool(metadataValue.Value);
                        }
                        break;

                    case "System.Runtime.CompilerServices.MemorySafetyRulesAttribute":
                        memorySafetyRulesVersion = TryGetInt32CtorArgument(reader, attr);
                        break;

                    case "System.Runtime.CompilerServices.DisableRuntimeMarshallingAttribute":
                        hasDisableRuntimeMarshalling = true;
                        break;

                    case "System.Diagnostics.CodeAnalysis.RequiresUnsafeAttribute":
                        requiresUnsafeCount++;
                        break;

                    case "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute":
                        requiresUnreferencedCodeCount++;
                        break;

                    case "System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute":
                        requiresDynamicCodeCount++;
                        break;

                    case "System.Diagnostics.CodeAnalysis.RequiresAssemblyFilesAttribute":
                        requiresAssemblyFilesCount++;
                        break;

                    case "System.Diagnostics.CodeAnalysis.DynamicDependencyAttribute":
                        dynamicDependencyCount++;
                        break;
                }
            }
        }

        if (reader.IsAssembly)
            inspect(reader.GetAssemblyDefinition().GetCustomAttributes());

        inspect(reader.GetModuleDefinition().GetCustomAttributes());

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            inspect(type.GetCustomAttributes());
            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0)
                    pInvokeMethodCount++;
                inspect(method.GetCustomAttributes());
            }
            foreach (var fieldHandle in type.GetFields())
                inspect(reader.GetFieldDefinition(fieldHandle).GetCustomAttributes());
            foreach (var propertyHandle in type.GetProperties())
                inspect(reader.GetPropertyDefinition(propertyHandle).GetCustomAttributes());
            foreach (var eventHandle in type.GetEvents())
                inspect(reader.GetEventDefinition(eventHandle).GetCustomAttributes());
        }

        return new AssemblyAuditMetadata
        {
            IsTrimmable = isTrimmable,
            IsAotCompatible = isAotCompatible,
            MemorySafetyRulesVersion = memorySafetyRulesVersion,
            HasDisableRuntimeMarshalling = hasDisableRuntimeMarshalling,
            RequiresUnsafeCount = requiresUnsafeCount,
            RequiresUnreferencedCodeCount = requiresUnreferencedCodeCount,
            RequiresDynamicCodeCount = requiresDynamicCodeCount,
            RequiresAssemblyFilesCount = requiresAssemblyFilesCount,
            DynamicDependencyCount = dynamicDependencyCount,
            PInvokeMethodCount = pInvokeMethodCount
        };
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
            var fullName = TypeResolver.FormatDisplayName(string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}");

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
    /// Attributes surfaced in Library Info or that are compiler/runtime infrastructure noise.
    /// </summary>
    private static bool IsWellKnownMetadataAttribute(string name) => name switch
    {
        // Already surfaced in Library Info
        "System.Runtime.Versioning.TargetFrameworkAttribute" => true,
        "System.Reflection.AssemblyFileVersionAttribute" => true,
        "System.Reflection.AssemblyInformationalVersionAttribute" => true,
        "System.Reflection.AssemblyProductAttribute" => true,
        "System.Reflection.AssemblyCompanyAttribute" => true,
        "System.Reflection.AssemblyCopyrightAttribute" => true,
        "System.Reflection.AssemblyDescriptionAttribute" => true,
        "System.Reflection.AssemblyConfigurationAttribute" => true,
        "System.Reflection.AssemblyTitleAttribute" => true,
        // Compiler-generated noise
        "System.Runtime.CompilerServices.CompilationRelaxationsAttribute" => true,
        "System.Runtime.CompilerServices.RuntimeCompatibilityAttribute" => true,
        "System.Diagnostics.DebuggableAttribute" => true,
        "System.Runtime.CompilerServices.RefSafetyRulesAttribute" => true,
        "System.Runtime.CompilerServices.NullablePublicOnlyAttribute" => true,
        "System.Runtime.CompilerServices.NullableContextAttribute" => true,
        "System.Runtime.CompilerServices.NullableAttribute" => true,
        // Runtime/compiler infrastructure — not developer decisions
        "System.Runtime.CompilerServices.ExtensionAttribute" => true,
        "System.Runtime.CompilerServices.SkipLocalsInitAttribute" => true,
        "System.Runtime.InteropServices.DefaultDllImportSearchPathsAttribute" => true,
        "System.Reflection.Metadata.MetadataUpdateHandlerAttribute" => true,
        "System.CLSCompliantAttribute" => true,
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

    private static (string Key, string Value)? TryGetAssemblyMetadataValue(MetadataReader reader, CustomAttribute attr)
    {
        try
        {
            var blob = reader.GetBlobReader(attr.Value);
            if (blob.Length < 2) return null;
            blob.ReadUInt16();
            var key = blob.ReadSerializedString();
            var value = blob.ReadSerializedString();
            return key != null && value != null ? (key, value) : null;
        }
        catch
        {
            return null;
        }
    }

    private static int? TryGetInt32CtorArgument(MetadataReader reader, CustomAttribute attr)
    {
        try
        {
            var blob = reader.GetBlobReader(attr.Value);
            if (blob.Length < 6) return null;
            blob.ReadUInt16();
            return blob.ReadInt32();
        }
        catch
        {
            return null;
        }
    }

    private static bool? ParseBool(string value)
    {
        if (bool.TryParse(value, out var result))
            return result;
        return null;
    }

    /// <summary>
    /// Cheap presence flags for section discovery. Uses metadata table scans
    /// and short-circuits at first match for each flag where practical.
    /// </summary>
    public static PresenceFlags ScanPresenceFlags(PEReader peReader)
    {
        var reader = peReader.GetMetadataReader();
        var flags = new PresenceFlags();

        // Resources: cheapest check — just a count
        flags.HasManifestResources = reader.GetTableRowCount(TableIndex.ManifestResource) > 0;
        flags.HasSwitches = SwitchScanner.HasSupport(peReader);
        var integrationPresence = EcosystemIntegrationScanner.ScanPresence(reader);
        flags.HasOpenTelemetrySupport = integrationPresence.HasOpenTelemetrySupport;
        flags.HasAspNetCoreSupport = integrationPresence.HasAspNetCoreSupport;
        flags.HasAspireSupport = integrationPresence.HasAspireSupport;
        flags.HasAISupport = integrationPresence.HasAISupport;
        flags.HasAuthenticationSupport = integrationPresence.HasAuthenticationSupport;
        flags.HasDependencyInjectionSupport = integrationPresence.HasDependencyInjectionSupport;
        flags.HasLoggingSupport = integrationPresence.HasLoggingSupport;
        flags.HasOptionsSupport = integrationPresence.HasOptionsSupport;
        flags.HasHostingSupport = integrationPresence.HasHostingSupport;
        flags.HasHealthChecksSupport = integrationPresence.HasHealthChecksSupport;
        flags.HasHttpClientSupport = integrationPresence.HasHttpClientSupport;
        flags.HasOpenApiSupport = integrationPresence.HasOpenApiSupport;

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
    public bool HasSwitches { get; set; }
    public bool HasOpenTelemetrySupport { get; set; }
    public bool HasAspNetCoreSupport { get; set; }
    public bool HasAspireSupport { get; set; }
    public bool HasAISupport { get; set; }
    public bool HasAuthenticationSupport { get; set; }
    public bool HasDependencyInjectionSupport { get; set; }
    public bool HasLoggingSupport { get; set; }
    public bool HasOptionsSupport { get; set; }
    public bool HasHostingSupport { get; set; }
    public bool HasHealthChecksSupport { get; set; }
    public bool HasHttpClientSupport { get; set; }
    public bool HasOpenApiSupport { get; set; }

    /// <summary>Whether the assembly has any public runtime-async methods (impl flag 0x2000).</summary>
    public bool HasRuntimeAsync { get; set; }

    /// <summary>Whether the assembly has any public classic state-machine async methods.</summary>
    public bool HasStateMachineAsync { get; set; }
}
