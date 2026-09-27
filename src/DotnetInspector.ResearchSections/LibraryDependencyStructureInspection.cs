using System.Text.Json;

using DotnetInspector.Sections;

using ILInspector.Research;

namespace DotnetInspector.ResearchSections;

/// <summary>
/// Carries the complete Research-owned Library Dependency Structure document as
/// envelope Content. Share is non-projectable until Inspect Web can restore the
/// exact inspection.
/// </summary>
public static class LibraryDependencyStructureInspection
{
    public static InspectionEnvelope<LibraryDependencyStructureDocument> Execute(
        LibraryDependencyStructureDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new(
            document,
            new InspectionShare.NonProjectable(
                "library-dependency-structure/share",
                "Inspect Web cannot yet restore an exact Library Dependency Structure inspection."),
            []);
    }
}

/// <summary>Complete JSON Content for the Library Dependency Structure document.</summary>
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
        LibraryMetricsInspectionJson.WriteAnalysisReceipt(writer, document.AnalysisReceipt);
        writer.WriteString("methodologyVersion", document.MethodologyVersion);
        writer.WriteString("completeness", document.Completeness switch
        {
            LibraryDependencyCompleteness.Complete => "complete",
            _ => "qualified",
        });
        writer.WritePropertyName("population");
        WritePopulation(writer, document.Population);

        writer.WriteStartArray("types");
        foreach (LibraryDependencyTypeNode node in document.Types)
        {
            writer.WriteStartObject();
            writer.WriteString("typeKey", node.TypeKey);
            writer.WriteString("namespace", node.Namespace);
            writer.WriteNumber("intraTypeRelationshipCount", node.IntraTypeRelationshipCount);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteStartArray("externalNodes");
        foreach (LibraryDependencyExternalNode node in document.ExternalNodes)
        {
            writer.WriteStartObject();
            writer.WriteString("key", node.Key);
            if (node.Assembly is { } assembly)
            {
                writer.WriteStartObject("assembly");
                writer.WriteString("name", assembly.Name);
                if (assembly.Version is { } version)
                    writer.WriteString("version", version.ToString());
                else
                    writer.WriteNull("version");
                writer.WriteString("culture", assembly.Culture);
                writer.WriteString("publicKeyToken", assembly.PublicKeyToken);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNull("assembly");
            }
            writer.WriteBoolean("isIntrinsicCoreLibrary", node.IsIntrinsicCoreLibrary);
            writer.WriteString("namespace", node.Namespace);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteStartArray("typeEdges");
        foreach (LibraryDependencyTypeEdge edge in document.TypeEdges)
            WriteTypeEdge(writer, edge);
        writer.WriteEndArray();

        writer.WriteStartArray("externalTypeEdges");
        foreach (LibraryDependencyExternalTypeEdge edge in document.ExternalTypeEdges)
            WriteExternalTypeEdge(writer, edge);
        writer.WriteEndArray();

        writer.WriteStartArray("namespaces");
        foreach (LibraryDependencyNamespaceNode node in document.Namespaces)
        {
            writer.WriteStartObject();
            writer.WriteString("namespace", node.Namespace);
            writer.WriteBoolean("isGlobalNamespace", node.IsGlobalNamespace);
            writer.WriteNumber("typeCount", node.TypeCount);
            writer.WriteNumber("intraNamespaceRelationshipCount", node.IntraNamespaceRelationshipCount);
            if (node.CycleIndex is int cycle)
                writer.WriteNumber("cycleIndex", cycle);
            else
                writer.WriteNull("cycleIndex");
            writer.WriteNumber("level", node.Level);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteStartArray("namespaceEdges");
        foreach (LibraryDependencyNamespaceEdge edge in document.NamespaceEdges)
        {
            writer.WriteStartObject();
            writer.WriteString("sourceNamespace", edge.SourceNamespace);
            writer.WriteString("targetNamespace", edge.TargetNamespace);
            WriteCounts(writer, edge.Counts);
            writer.WriteNumber("contributingTypeEdgeCount", edge.ContributingTypeEdgeCount);
            writer.WriteStartArray("explainingTypeEdges");
            foreach (LibraryDependencyTypeEdge explaining in edge.ExplainingTypeEdges)
                WriteTypeEdge(writer, explaining);
            writer.WriteEndArray();
            writer.WriteNumber("remainingContributorCount", edge.RemainingContributorCount);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteStartArray("externalNamespaceEdges");
        foreach (LibraryDependencyExternalNamespaceEdge edge in document.ExternalNamespaceEdges)
        {
            writer.WriteStartObject();
            writer.WriteString("sourceNamespace", edge.SourceNamespace);
            writer.WriteString("externalKey", edge.ExternalKey);
            WriteCounts(writer, edge.Counts);
            writer.WriteNumber("contributingTypeEdgeCount", edge.ContributingTypeEdgeCount);
            writer.WriteStartArray("explainingTypeEdges");
            foreach (LibraryDependencyExternalTypeEdge explaining in edge.ExplainingTypeEdges)
                WriteExternalTypeEdge(writer, explaining);
            writer.WriteEndArray();
            writer.WriteNumber("remainingContributorCount", edge.RemainingContributorCount);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteStartArray("cycles");
        foreach (LibraryDependencyNamespaceCycle cycle in document.Cycles)
        {
            writer.WriteStartArray();
            foreach (string ns in cycle.Namespaces)
                writer.WriteStringValue(ns);
            writer.WriteEndArray();
        }
        writer.WriteEndArray();

        writer.WritePropertyName("diagnostics");
        LibraryMetricsInspectionJson.WriteDiagnostics(writer, document.Diagnostics);
        writer.WriteEndObject();
    }

    static void WritePopulation(
        Utf8JsonWriter writer,
        LibraryDependencyPopulationReceipt population)
    {
        writer.WriteStartObject();
        writer.WriteNumber("examinedCallCount", population.ExaminedCallCount);
        writer.WriteNumber("internalCallCount", population.InternalCallCount);
        writer.WriteNumber("sameTypeCallCount", population.SameTypeCallCount);
        writer.WriteNumber("externalCallCount", population.ExternalCallCount);
        writer.WriteNumber("runtimeProvidedCallCount", population.RuntimeProvidedCallCount);
        writer.WriteNumber("unresolvedCallCount", population.UnresolvedCallCount);
        writer.WriteStartArray("unresolvedReasons");
        foreach (LibraryDependencyUnresolvedCount reason in population.UnresolvedReasons)
        {
            writer.WriteStartObject();
            writer.WriteString("reason", ReasonName(reason.Reason));
            writer.WriteNumber("count", reason.Count);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteNumber("incompleteBodyCount", population.IncompleteBodyCount);
        writer.WriteNumber("typeCount", population.TypeCount);
        writer.WriteNumber("namespaceCount", population.NamespaceCount);
        writer.WriteNumber("externalNodeCount", population.ExternalNodeCount);
        writer.WriteEndObject();
    }

    static void WriteTypeEdge(Utf8JsonWriter writer, LibraryDependencyTypeEdge edge)
    {
        writer.WriteStartObject();
        writer.WriteString("sourceTypeKey", edge.SourceTypeKey);
        writer.WriteString("targetTypeKey", edge.TargetTypeKey);
        WriteCounts(writer, edge.Counts);
        writer.WriteEndObject();
    }

    static void WriteExternalTypeEdge(Utf8JsonWriter writer, LibraryDependencyExternalTypeEdge edge)
    {
        writer.WriteStartObject();
        writer.WriteString("sourceTypeKey", edge.SourceTypeKey);
        writer.WriteString("externalKey", edge.ExternalKey);
        WriteCounts(writer, edge.Counts);
        writer.WriteEndObject();
    }

    static void WriteCounts(Utf8JsonWriter writer, LibraryDependencyCounts counts)
    {
        writer.WriteNumber("invocations", counts.Invocations);
        writer.WriteNumber("functionReferences", counts.FunctionReferences);
    }

    internal static string ReasonName(LibraryDependencyUnresolvedReason reason) =>
        reason switch
        {
            LibraryDependencyUnresolvedReason.Indirect => "indirect",
            LibraryDependencyUnresolvedReason.UnsupportedSignature => "unsupported-signature",
            LibraryDependencyUnresolvedReason.MalformedSignature => "malformed-signature",
            LibraryDependencyUnresolvedReason.InvalidGenericDeclaration => "invalid-generic-declaration",
            LibraryDependencyUnresolvedReason.Unmatched => "unmatched",
            LibraryDependencyUnresolvedReason.Ambiguous => "ambiguous",
            LibraryDependencyUnresolvedReason.ModuleReference => "module-reference",
            _ => throw new ArgumentOutOfRangeException(nameof(reason)),
        };
}
