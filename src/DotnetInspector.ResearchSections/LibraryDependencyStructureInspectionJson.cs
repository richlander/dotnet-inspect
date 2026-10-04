using System.Text.Json;

using DotnetInspector.Sections;

using ILInspector.Research;

namespace DotnetInspector.ResearchSections;

public static class LibraryDependencyStructureInspectionJson
{
    public static void Write(
        Utf8JsonWriter writer,
        LibraryDependencyStructureDocument document)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(document);

        writer.WriteStartObject();
        writer.WritePropertyName("analysisReceipt");
        AnalysisExecutionJson.WriteReceipt(
            writer,
            document.AnalysisReceipt);
        writer.WriteString(
            "methodologyVersion",
            document.MethodologyVersion);
        writer.WritePropertyName("population");
        WritePopulation(writer, document.Population);
        writer.WriteString(
            "completeness",
            document.Completeness.ToString());
        writer.WritePropertyName("types");
        WriteArray(writer, document.Types, WriteTypeNode);
        writer.WritePropertyName("externalNodes");
        WriteArray(writer, document.ExternalNodes, WriteExternalNode);
        writer.WritePropertyName("typeEdges");
        WriteArray(writer, document.TypeEdges, WriteTypeEdge);
        writer.WritePropertyName("externalTypeEdges");
        WriteArray(
            writer,
            document.ExternalTypeEdges,
            WriteExternalTypeEdge);
        writer.WritePropertyName("namespaces");
        WriteArray(writer, document.Namespaces, WriteNamespaceNode);
        writer.WritePropertyName("namespaceEdges");
        WriteArray(writer, document.NamespaceEdges, WriteNamespaceEdge);
        writer.WritePropertyName("externalNamespaceEdges");
        WriteArray(
            writer,
            document.ExternalNamespaceEdges,
            WriteExternalNamespaceEdge);
        writer.WritePropertyName("cycles");
        WriteArray(writer, document.Cycles, WriteCycle);
        writer.WritePropertyName("diagnostics");
        AnalysisExecutionJson.WriteDiagnostics(
            writer,
            document.Diagnostics);
        writer.WriteEndObject();
    }

    private static void WritePopulation(
        Utf8JsonWriter writer,
        LibraryDependencyPopulationReceipt population)
    {
        writer.WriteStartObject();
        writer.WriteNumber(
            "examinedCallCount",
            population.ExaminedCallCount);
        writer.WriteNumber(
            "internalCallCount",
            population.InternalCallCount);
        writer.WriteNumber(
            "sameTypeCallCount",
            population.SameTypeCallCount);
        writer.WriteNumber(
            "externalCallCount",
            population.ExternalCallCount);
        writer.WriteNumber(
            "runtimeProvidedCallCount",
            population.RuntimeProvidedCallCount);
        writer.WriteNumber(
            "unresolvedCallCount",
            population.UnresolvedCallCount);
        writer.WritePropertyName("unresolvedReasons");
        WriteArray(
            writer,
            population.UnresolvedReasons,
            static (json, reason) =>
            {
                json.WriteStartObject();
                json.WriteString("reason", reason.Reason.ToString());
                json.WriteNumber("count", reason.Count);
                json.WriteEndObject();
            });
        writer.WriteNumber(
            "incompleteBodyCount",
            population.IncompleteBodyCount);
        writer.WriteNumber("typeCount", population.TypeCount);
        writer.WriteNumber(
            "namespaceCount",
            population.NamespaceCount);
        writer.WriteNumber(
            "externalNodeCount",
            population.ExternalNodeCount);
        writer.WriteEndObject();
    }

    private static void WriteTypeNode(
        Utf8JsonWriter writer,
        LibraryDependencyTypeNode node)
    {
        writer.WriteStartObject();
        writer.WriteString("typeKey", node.TypeKey);
        writer.WritePropertyName("type");
        AnalysisIdentityJson.WriteType(writer, node.Type);
        writer.WriteString("namespace", node.Namespace);
        writer.WriteNumber(
            "intraTypeRelationshipCount",
            node.IntraTypeRelationshipCount);
        writer.WriteEndObject();
    }

    private static void WriteExternalNode(
        Utf8JsonWriter writer,
        LibraryDependencyExternalNode node)
    {
        writer.WriteStartObject();
        writer.WriteString("key", node.Key);
        writer.WritePropertyName("assembly");
        if (node.Assembly is { } assembly)
        {
            AnalysisExecutionJson.WriteAssemblyIdentity(
                writer,
                assembly);
        }
        else
            writer.WriteNullValue();
        writer.WriteBoolean(
            "isIntrinsicCoreLibrary",
            node.IsIntrinsicCoreLibrary);
        writer.WriteString("namespace", node.Namespace);
        writer.WriteEndObject();
    }

    private static void WriteTypeEdge(
        Utf8JsonWriter writer,
        LibraryDependencyTypeEdge edge)
    {
        writer.WriteStartObject();
        writer.WriteString("sourceTypeKey", edge.SourceTypeKey);
        writer.WriteString("targetTypeKey", edge.TargetTypeKey);
        writer.WritePropertyName("counts");
        WriteCounts(writer, edge.Counts);
        writer.WriteEndObject();
    }

    private static void WriteExternalTypeEdge(
        Utf8JsonWriter writer,
        LibraryDependencyExternalTypeEdge edge)
    {
        writer.WriteStartObject();
        writer.WriteString("sourceTypeKey", edge.SourceTypeKey);
        writer.WriteString("externalKey", edge.ExternalKey);
        writer.WritePropertyName("counts");
        WriteCounts(writer, edge.Counts);
        writer.WriteEndObject();
    }

    private static void WriteNamespaceNode(
        Utf8JsonWriter writer,
        LibraryDependencyNamespaceNode node)
    {
        writer.WriteStartObject();
        writer.WriteString("namespace", node.Namespace);
        writer.WriteBoolean(
            "isGlobalNamespace",
            node.IsGlobalNamespace);
        writer.WriteNumber("typeCount", node.TypeCount);
        writer.WriteNumber(
            "intraNamespaceRelationshipCount",
            node.IntraNamespaceRelationshipCount);
        WriteNumber(writer, "cycleIndex", node.CycleIndex);
        writer.WriteNumber("level", node.Level);
        writer.WriteEndObject();
    }

    private static void WriteNamespaceEdge(
        Utf8JsonWriter writer,
        LibraryDependencyNamespaceEdge edge)
    {
        writer.WriteStartObject();
        writer.WriteString("sourceNamespace", edge.SourceNamespace);
        writer.WriteString("targetNamespace", edge.TargetNamespace);
        writer.WritePropertyName("counts");
        WriteCounts(writer, edge.Counts);
        writer.WriteNumber(
            "contributingTypeEdgeCount",
            edge.ContributingTypeEdgeCount);
        writer.WritePropertyName("explainingTypeEdges");
        WriteArray(writer, edge.ExplainingTypeEdges, WriteTypeEdge);
        writer.WriteNumber(
            "remainingContributorCount",
            edge.RemainingContributorCount);
        writer.WriteEndObject();
    }

    private static void WriteExternalNamespaceEdge(
        Utf8JsonWriter writer,
        LibraryDependencyExternalNamespaceEdge edge)
    {
        writer.WriteStartObject();
        writer.WriteString("sourceNamespace", edge.SourceNamespace);
        writer.WriteString("externalKey", edge.ExternalKey);
        writer.WritePropertyName("counts");
        WriteCounts(writer, edge.Counts);
        writer.WriteNumber(
            "contributingTypeEdgeCount",
            edge.ContributingTypeEdgeCount);
        writer.WritePropertyName("explainingTypeEdges");
        WriteArray(
            writer,
            edge.ExplainingTypeEdges,
            WriteExternalTypeEdge);
        writer.WriteNumber(
            "remainingContributorCount",
            edge.RemainingContributorCount);
        writer.WriteEndObject();
    }

    private static void WriteCycle(
        Utf8JsonWriter writer,
        LibraryDependencyNamespaceCycle cycle)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("namespaces");
        writer.WriteStartArray();
        foreach (string @namespace in cycle.Namespaces)
            writer.WriteStringValue(@namespace);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteCounts(
        Utf8JsonWriter writer,
        LibraryDependencyCounts counts)
    {
        writer.WriteStartObject();
        writer.WriteNumber("invocations", counts.Invocations);
        writer.WriteNumber(
            "functionReferences",
            counts.FunctionReferences);
        writer.WriteNumber("total", counts.Total);
        writer.WriteEndObject();
    }

    private static void WriteArray<T>(
        Utf8JsonWriter writer,
        IEnumerable<T> items,
        Action<Utf8JsonWriter, T> write)
    {
        writer.WriteStartArray();
        foreach (T item in items)
            write(writer, item);
        writer.WriteEndArray();
    }

    private static void WriteNumber(
        Utf8JsonWriter writer,
        string propertyName,
        int? value)
    {
        if (value is { } number)
            writer.WriteNumber(propertyName, number);
        else
            writer.WriteNull(propertyName);
    }
}
