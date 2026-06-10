using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DotnetInspector.Metadata;

/// <summary>
/// Metadata evidence that an assembly participates in OpenTelemetry-style observability.
/// </summary>
public record OpenTelemetrySignalInfo(
    string Area,
    string Signal,
    string Value,
    string Evidence);

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
        var assemblyReferences = GetTelemetryAssemblyReferences(reader);
        var openTelemetryTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        var tracingTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        var metricsTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        var diagnosticSourceTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        var microsoftTelemetryTypes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var handle in reader.TypeReferences)
            AddTypeMatches(reader.GetFullTypeName(reader.GetTypeReference(handle)), "TypeRef");

        foreach (var handle in reader.TypeDefinitions)
            AddTypeMatches(reader.GetFullTypeName(reader.GetTypeDefinition(handle)), "TypeDef");

        List<OpenTelemetrySignalInfo> results = [];
        AddAssemblyReferenceRow(results, assemblyReferences);
        AddTypeRow(results, "OpenTelemetry", "OpenTelemetry API types", openTelemetryTypes);
        AddTypeRow(results, "Tracing", "Activity APIs", tracingTypes);
        AddTypeRow(results, "Metrics", "Metrics APIs", metricsTypes);
        AddTypeRow(results, "Diagnostics", "DiagnosticSource APIs", diagnosticSourceTypes);
        AddTypeRow(results, "Telemetry", "Microsoft.Extensions.Telemetry APIs", microsoftTelemetryTypes);
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
    }

    public static bool HasSupport(PEReader peReader)
        => peReader.HasMetadata && HasSupport(peReader.GetMetadataReader());

    internal static bool HasSupport(MetadataReader reader)
    {
        foreach (var handle in reader.AssemblyReferences)
        {
            var assemblyReference = reader.GetAssemblyReference(handle);
            var name = reader.GetString(assemblyReference.Name);
            if (IsTelemetryAssembly(name))
                return true;
        }

        foreach (var handle in reader.TypeReferences)
        {
            if (IsTelemetryType(reader.GetFullTypeName(reader.GetTypeReference(handle))))
                return true;
        }

        foreach (var handle in reader.TypeDefinitions)
        {
            if (IsTelemetryType(reader.GetFullTypeName(reader.GetTypeDefinition(handle))))
                return true;
        }

        return false;
    }

    private static SortedSet<string> GetTelemetryAssemblyReferences(MetadataReader reader)
    {
        SortedSet<string> references = new(StringComparer.OrdinalIgnoreCase);
        foreach (var handle in reader.AssemblyReferences)
        {
            var assemblyReference = reader.GetAssemblyReference(handle);
            var name = reader.GetString(assemblyReference.Name);
            if (IsTelemetryAssembly(name))
                references.Add(name);
        }

        return references;
    }

    private static bool IsTelemetryAssembly(string name)
        => IsOpenTelemetryAssembly(name)
           || name.StartsWith("Microsoft.Extensions.Telemetry", StringComparison.OrdinalIgnoreCase)
           || name.Equals("System.Diagnostics.DiagnosticSource", StringComparison.OrdinalIgnoreCase);

    private static bool IsOpenTelemetryAssembly(string name)
        => name.Equals("OpenTelemetry", StringComparison.OrdinalIgnoreCase)
           || name.StartsWith("OpenTelemetry.", StringComparison.OrdinalIgnoreCase);

    private static bool IsTelemetryType(string typeName)
        => IsOpenTelemetryType(typeName)
           || IsTracingPrimitive(typeName)
           || IsMetricsPrimitive(typeName)
           || IsDiagnosticSourcePrimitive(typeName)
           || IsMicrosoftTelemetryType(typeName);

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

    private static void AddAssemblyReferenceRow(List<OpenTelemetrySignalInfo> results, SortedSet<string> references)
    {
        if (references.Count == 0)
            return;

        results.Add(new OpenTelemetrySignalInfo(
            "Dependencies",
            "Telemetry assembly references",
            references.Count.ToString(),
            $"AssemblyRef: {FormatEvidence(references)}"));
    }

    private static void AddTypeRow(
        List<OpenTelemetrySignalInfo> results,
        string area,
        string signal,
        Dictionary<string, string> types)
    {
        if (types.Count == 0)
            return;

        results.Add(new OpenTelemetrySignalInfo(
            area,
            signal,
            types.Count.ToString(),
            FormatTypeEvidence(types)));
    }

    private static string FormatTypeEvidence(Dictionary<string, string> types)
        => FormatEvidence(types
            .OrderBy(kv => GetEvidenceRank(kv.Key))
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Value}: {kv.Key}"));

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

    private static string FormatEvidence(IEnumerable<string> values)
    {
        var ordered = values.ToArray();
        var visible = ordered.Take(EvidenceLimit).ToArray();
        var suffix = ordered.Length > EvidenceLimit ? $" (+{ordered.Length - EvidenceLimit} more)" : "";
        return string.Join(", ", visible) + suffix;
    }
}
