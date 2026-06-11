using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DotnetInspector.Metadata;

/// <summary>
/// Metadata evidence that an assembly participates in OpenTelemetry-style observability.
/// </summary>
public record OpenTelemetrySignalInfo(
    string Kind,
    string Name,
    string Shape = IntegrationSignalShape.Type);

/// <summary>
/// Scans assembly metadata for OpenTelemetry packages and .NET diagnostics primitives.
/// </summary>
public static class OpenTelemetryScanner
{
    private const int EvidenceLimit = 6;

    public static List<OpenTelemetrySignalInfo> Scan(PEReader peReader)
    {
        if (!peReader.HasMetadata)
            return [];

        var reader = peReader.GetMetadataReader();
        var openTelemetryTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        var tracingTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        var metricsTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        var diagnosticSourceTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        var microsoftTelemetryTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        var openTelemetryApis = new Dictionary<string, string>(StringComparer.Ordinal);
        var loggingApis = new Dictionary<string, string>(StringComparer.Ordinal);
        var tracingApis = new Dictionary<string, string>(StringComparer.Ordinal);
        var metricsApis = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var handle in reader.TypeDefinitions)
        {
            var typeDefinition = reader.GetTypeDefinition(handle);
            if (!typeDefinition.IsPublic)
                continue;

            var typeName = reader.GetFullTypeName(typeDefinition);
            AddTypeMatches(typeName, "TypeDef");
            AddTelemetryControlProperties(reader, typeDefinition, typeName);
            AddTelemetryBuilderMethods(reader, typeDefinition, typeName);
        }
        List<OpenTelemetrySignalInfo> results = [];
        AddApiRows(results, "OpenTelemetry", openTelemetryApis);
        AddApiRows(results, "Logging", loggingApis);
        AddApiRows(results, "Metrics", metricsApis);
        AddTypeRows(results, "OpenTelemetry", openTelemetryTypes);
        AddTypeRows(results, "Tracing", tracingTypes);
        AddApiRows(results, "Tracing", tracingApis);
        AddTypeRows(results, "Metrics", metricsTypes);
        AddTypeRows(results, "DiagnosticSource", diagnosticSourceTypes);
        AddTypeRows(results, "Microsoft.Extensions.Telemetry", microsoftTelemetryTypes);
        return results;

        void AddTypeMatches(string typeName, string source)
        {
            if (IsOpenTelemetryType(typeName))
                AddType(openTelemetryTypes, typeName, source);
            if (IsTracingPrimitive(typeName))
                AddType(tracingTypes, typeName, source);
            if (IsMetricsPrimitive(typeName))
                AddType(metricsTypes, typeName, source);
            if (IsDiagnosticSourcePrimitive(typeName))
                AddType(diagnosticSourceTypes, typeName, source);
            if (IsMicrosoftTelemetryType(typeName))
                AddType(microsoftTelemetryTypes, typeName, source);
        }

        void AddTelemetryControlProperties(MetadataReader metadataReader, TypeDefinition typeDefinition, string typeName)
        {
            var context = GenericContext.ForType(metadataReader, typeDefinition);
            foreach (var propertyHandle in typeDefinition.GetProperties())
            {
                var property = metadataReader.GetPropertyDefinition(propertyHandle);
                var accessors = property.GetAccessors();
                if (accessors.Setter.IsNil)
                    continue;

                var setter = metadataReader.GetMethodDefinition(accessors.Setter);
                if ((setter.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                    continue;

                try
                {
                    var signature = property.DecodeSignature(SignatureDecoder.Instance, context);
                    if (signature.ReturnType != "bool")
                        continue;
                }
                catch (BadImageFormatException)
                {
                    continue;
                }

                var propertyName = metadataReader.GetString(property.Name);
                var api = $"{TypeResolver.FormatDisplayName(typeName)}.{propertyName}";
                if (propertyName == "DisableTracing")
                    AddType(tracingApis, api, "Property");
                else if (propertyName == "DisableMetrics")
                    AddType(metricsApis, api, "Property");
            }
        }

        void AddTelemetryBuilderMethods(MetadataReader metadataReader, TypeDefinition typeDefinition, string typeName)
        {
            var attributes = typeDefinition.Attributes;
            var isStatic = (attributes & TypeAttributes.Sealed) != 0
                           && (attributes & TypeAttributes.Abstract) != 0;
            if (!isStatic || !AttributeReader.HasExtensionAttribute(metadataReader, typeDefinition.GetCustomAttributes()))
                return;

            foreach (var methodHandle in typeDefinition.GetMethods())
            {
                var method = metadataReader.GetMethodDefinition(methodHandle);
                if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public
                    || (method.Attributes & MethodAttributes.Static) == 0
                    || !AttributeReader.HasExtensionAttribute(metadataReader, method.GetCustomAttributes()))
                    continue;

                var methodName = metadataReader.GetString(method.Name);
                if (!methodName.StartsWith("Add", StringComparison.Ordinal)
                    && !methodName.StartsWith("Use", StringComparison.Ordinal))
                    continue;

                MethodSignature<string> signature;
                try
                {
                    var context = GenericContext.ForMethod(metadataReader, typeDefinition, method);
                    signature = method.DecodeSignature(SignatureDecoder.Instance, context);
                }
                catch (BadImageFormatException)
                {
                    continue;
                }

                if (!TryClassifyTelemetryBuilderMethod(signature, out var kind))
                    continue;

                var api = $"{TypeResolver.FormatDisplayName(typeName)}.{methodName}(...)";
                AddType(GetTelemetryApiBucket(kind), api, "Method");
            }
        }

        Dictionary<string, string> GetTelemetryApiBucket(string kind) => kind switch
        {
            "Logging" => loggingApis,
            "Metrics" => metricsApis,
            "Tracing" => tracingApis,
            _ => openTelemetryApis
        };
    }

    public static bool HasSupport(PEReader peReader)
        => peReader.HasMetadata && HasSupport(peReader.GetMetadataReader());

    internal static bool HasSupport(MetadataReader reader)
    {
        foreach (var handle in reader.TypeDefinitions)
        {
            var typeDefinition = reader.GetTypeDefinition(handle);
            if (!typeDefinition.IsPublic)
                continue;

            if (IsTelemetryType(reader.GetFullTypeName(typeDefinition))
                || HasTelemetryControlProperty(reader, typeDefinition))
                return true;
            if (HasTelemetryBuilderMethod(reader, typeDefinition))
                return true;
        }

        return false;
    }

    private static bool IsTelemetryType(string typeName)
        => !typeName.Contains(".Internal.", StringComparison.Ordinal)
           && (IsOpenTelemetryType(typeName)
           || IsTracingPrimitive(typeName)
           || IsMetricsPrimitive(typeName)
           || IsDiagnosticSourcePrimitive(typeName)
           || IsMicrosoftTelemetryType(typeName));

    private static bool IsOpenTelemetryType(string typeName)
        => typeName.Equals("OpenTelemetry", StringComparison.Ordinal)
           || typeName.StartsWith("OpenTelemetry.", StringComparison.Ordinal);

    private static bool IsTracingPrimitive(string typeName)
        => typeName.StartsWith("System.Diagnostics.Activity", StringComparison.Ordinal)
           || typeName.Equals("System.Diagnostics.DistributedContextPropagator", StringComparison.Ordinal);

    private static bool IsMetricsPrimitive(string typeName)
        => typeName.StartsWith("System.Diagnostics.Metrics.", StringComparison.Ordinal);

    private static bool IsDiagnosticSourcePrimitive(string typeName)
        => typeName.Equals("System.Diagnostics.DiagnosticSource", StringComparison.Ordinal)
           || typeName.Equals("System.Diagnostics.DiagnosticListener", StringComparison.Ordinal);

    private static bool IsMicrosoftTelemetryType(string typeName)
        => typeName.Equals("Microsoft.Extensions.Telemetry", StringComparison.Ordinal)
           || typeName.StartsWith("Microsoft.Extensions.Telemetry.", StringComparison.Ordinal);

    private static void AddType(Dictionary<string, string> matches, string typeName, string source)
    {
        if (matches.TryGetValue(typeName, out var existing))
        {
            if (!existing.Split('/').Contains(source, StringComparer.Ordinal))
                matches[typeName] = $"{existing}/{source}";
            return;
        }

        matches.Add(typeName, source);
    }

    private static void AddTypeRows(
        List<OpenTelemetrySignalInfo> results,
        string kind,
        Dictionary<string, string> types)
    {
        foreach (var type in OrderTypes(types))
            results.Add(new OpenTelemetrySignalInfo(kind, TypeResolver.FormatDisplayName(type)));
    }

    private static void AddApiRows(
        List<OpenTelemetrySignalInfo> results,
        string kind,
        Dictionary<string, string> apis)
    {
        foreach (var api in OrderTypes(apis))
            results.Add(new OpenTelemetrySignalInfo(kind, api, IntegrationSignalShape.Api));
    }

    private static bool HasTelemetryControlProperty(MetadataReader reader, TypeDefinition typeDefinition)
    {
        var context = GenericContext.ForType(reader, typeDefinition);
        foreach (var propertyHandle in typeDefinition.GetProperties())
        {
            var property = reader.GetPropertyDefinition(propertyHandle);
            var propertyName = reader.GetString(property.Name);
            if (propertyName is not ("DisableTracing" or "DisableMetrics"))
                continue;

            var accessors = property.GetAccessors();
            if (accessors.Setter.IsNil)
                continue;

            var setter = reader.GetMethodDefinition(accessors.Setter);
            if ((setter.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                continue;

            try
            {
                if (property.DecodeSignature(SignatureDecoder.Instance, context).ReturnType == "bool")
                    return true;
            }
            catch (BadImageFormatException)
            {
            }
        }

        return false;
    }

    private static bool HasTelemetryBuilderMethod(MetadataReader reader, TypeDefinition typeDefinition)
    {
        var attributes = typeDefinition.Attributes;
        var isStatic = (attributes & TypeAttributes.Sealed) != 0
                       && (attributes & TypeAttributes.Abstract) != 0;
        if (!isStatic || !AttributeReader.HasExtensionAttribute(reader, typeDefinition.GetCustomAttributes()))
            return false;

        foreach (var methodHandle in typeDefinition.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public
                || (method.Attributes & MethodAttributes.Static) == 0
                || !AttributeReader.HasExtensionAttribute(reader, method.GetCustomAttributes()))
                continue;

            var methodName = reader.GetString(method.Name);
            if (!methodName.StartsWith("Add", StringComparison.Ordinal)
                && !methodName.StartsWith("Use", StringComparison.Ordinal))
                continue;

            try
            {
                var context = GenericContext.ForMethod(reader, typeDefinition, method);
                var signature = method.DecodeSignature(SignatureDecoder.Instance, context);
                if (TryClassifyTelemetryBuilderMethod(signature, out _))
                    return true;
            }
            catch (BadImageFormatException)
            {
            }
        }

        return false;
    }

    private static bool TryClassifyTelemetryBuilderMethod(MethodSignature<string> signature, out string kind)
    {
        kind = "";
        if (signature.ParameterTypes.Length == 0)
            return false;

        var receiver = signature.ParameterTypes[0];
        if (receiver == "OpenTelemetry.Trace.TracerProviderBuilder"
            || signature.ReturnType == "OpenTelemetry.Trace.TracerProviderBuilder")
            kind = "Tracing";
        else if (receiver == "OpenTelemetry.Metrics.MeterProviderBuilder"
                 || signature.ReturnType == "OpenTelemetry.Metrics.MeterProviderBuilder")
            kind = "Metrics";
        else if (receiver == "OpenTelemetry.Logs.OpenTelemetryLoggerOptions"
                 || signature.ReturnType == "OpenTelemetry.Logs.OpenTelemetryLoggerOptions")
            kind = "Logging";
        else if (receiver is "OpenTelemetry.IOpenTelemetryBuilder" or "OpenTelemetry.OpenTelemetryBuilder"
                 || signature.ReturnType is "OpenTelemetry.IOpenTelemetryBuilder" or "OpenTelemetry.OpenTelemetryBuilder")
            kind = "OpenTelemetry";

        return kind.Length > 0;
    }

    private static IEnumerable<string> OrderTypes(Dictionary<string, string> types)
        => types
            .OrderBy(kv => GetEvidenceRank(kv.Key))
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Key);

    private static int GetEvidenceRank(string typeName)
        => typeName switch
        {
            "System.Diagnostics.ActivitySource" => 0,
            "System.Diagnostics.Metrics.Meter" => 0,
            "System.Diagnostics.DiagnosticSource" => 0,
            "System.Diagnostics.Activity" => 1,
            "System.Diagnostics.ActivityContext" => 2,
            "System.Diagnostics.DiagnosticListener" => 2,
            "System.Diagnostics.Metrics.Counter`1" => 2,
            "System.Diagnostics.Metrics.Histogram`1" => 3,
            "System.Diagnostics.Metrics.UpDownCounter`1" => 4,
            _ => 10,
        };

}
