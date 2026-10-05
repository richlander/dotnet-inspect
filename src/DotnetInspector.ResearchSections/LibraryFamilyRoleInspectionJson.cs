using System.Text.Json;

using ILInspector.Metadata;
using ILInspector.Research;

using Inspector.Graph;

namespace DotnetInspector.ResearchSections;

public static class LibraryFamilyRoleInspectionJson
{
    public static void Write(
        Utf8JsonWriter writer,
        LibraryFamilyRoleCompositionDocument document)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(document);

        var graphDocuments =
            new Dictionary<GraphDocumentIdentity, string>();
        writer.WriteStartObject();
        writer.WritePropertyName("binding");
        WriteBinding(writer, document.Binding);
        writer.WriteString(
            "methodologyVersion",
            document.MethodologyVersion);
        writer.WritePropertyName("nameFamilyMethodology");
        LibraryNameFamilyInspectionJson.WriteMethodology(
            writer,
            document.NameFamilyMethodology);
        writer.WritePropertyName("provenance");
        LibraryNameFamilyInspectionJson.WriteProvenance(
            writer,
            document.Provenance);
        writer.WritePropertyName("structuralSalience");
        WriteStructuralReceipt(
            writer,
            document.StructuralSalience,
            graphDocuments);
        writer.WritePropertyName("types");
        writer.WriteStartArray();
        foreach (LibraryFamilyRoleTypeRow type in document.Types)
            WriteType(writer, type);
        writer.WriteEndArray();
        writer.WritePropertyName("populations");
        writer.WriteStartArray();
        foreach (LibraryFamilyRolePopulation population
            in document.Populations)
        {
            WritePopulation(writer, population);
        }
        writer.WriteEndArray();
        writer.WritePropertyName("receipt");
        WriteCompositionReceipt(
            writer,
            document.Receipt,
            graphDocuments);
        writer.WriteEndObject();
    }

    private static void WriteBinding(
        Utf8JsonWriter writer,
        LibraryFamilyRoleBinding binding)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("artifactBound", true);
        writer.WritePropertyName("assembly");
        LibraryNameFamilyInspectionJson.WriteAssembly(
            writer,
            binding.Assembly);
        writer.WriteString(
            "moduleVersionId",
            binding.ModuleVersionId);
        writer.WriteEndObject();
    }

    private static void WriteType(
        Utf8JsonWriter writer,
        LibraryFamilyRoleTypeRow type)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("type");
        LibraryNameFamilyInspectionJson.WriteTypeAddress(
            writer,
            type.Type);
        writer.WritePropertyName("name");
        LibraryNameFamilyInspectionJson.WriteTypeName(
            writer,
            type.Name);
        writer.WriteString(
            "definitionKind",
            type.DefinitionKind.ToString());
        writer.WriteString("namespace", type.Namespace);
        writer.WritePropertyName("oneWordSuffix");
        LibraryNameFamilyInspectionJson.WriteFamilyIdentity(
            writer,
            type.OneWordSuffix);
        writer.WritePropertyName("twoWordSuffix");
        LibraryNameFamilyInspectionJson.WriteFamilyIdentity(
            writer,
            type.TwoWordSuffix);
        WriteEnum(
            writer,
            "oneWordResidual",
            type.OneWordResidual);
        WriteEnum(
            writer,
            "twoWordResidual",
            type.TwoWordResidual);
        WriteEnum(
            writer,
            "sourceDisposition",
            type.SourceDisposition);
        WriteNumber(
            writer,
            "signatureIncomingDegree",
            type.SignatureIncomingDegree);
        WriteNumber(
            writer,
            "signatureOutgoingDegree",
            type.SignatureOutgoingDegree);
        WriteEnum(
            writer,
            "structuralClassification",
            type.StructuralClassification);
        WriteEnum(
            writer,
            "structuralRole",
            type.StructuralRole);
        WriteEnum(
            writer,
            "structuralPole",
            type.StructuralPole);
        writer.WriteString(
            "structuralDisposition",
            type.StructuralDisposition.ToString());
        writer.WriteEndObject();
    }

    private static void WritePopulation(
        Utf8JsonWriter writer,
        LibraryFamilyRolePopulation population)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", population.Kind.ToString());
        WriteRoleCounts(
            writer,
            population.TypeCount,
            population.FoundationCount,
            population.HubCount,
            population.OrchestratorCount,
            population.SeaLevelCount,
            population.MountainPeakCount,
            population.NoIssuedStructuralRoleCount);
        writer.WritePropertyName("provenance");
        LibraryNameFamilyInspectionJson.WriteProvenance(
            writer,
            population.Provenance);
        writer.WriteString(
            "structuralDisposition",
            population.StructuralDisposition.ToString());
        writer.WritePropertyName("families");
        writer.WriteStartArray();
        foreach (LibraryFamilyRoleRow family in population.Families)
            WriteFamily(writer, family);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteFamily(
        Utf8JsonWriter writer,
        LibraryFamilyRoleRow family)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("identity");
        LibraryNameFamilyInspectionJson.WriteFamilyIdentity(
            writer,
            family.Identity);
        WriteRoleCounts(
            writer,
            family.TypeCount,
            family.FoundationCount,
            family.HubCount,
            family.OrchestratorCount,
            family.SeaLevelCount,
            family.MountainPeakCount,
            family.NoIssuedStructuralRoleCount);
        writer.WriteNumber(
            "distinctNamespaceCount",
            family.DistinctNamespaceCount);
        writer.WriteString(
            "structuralDisposition",
            family.StructuralDisposition.ToString());
        WriteTypes(writer, "types", family.Types);
        WriteTypes(
            writer,
            "foundations",
            family.Foundations);
        WriteTypes(writer, "hubs", family.Hubs);
        WriteTypes(
            writer,
            "orchestrators",
            family.Orchestrators);
        WriteTypes(
            writer,
            "seaLevels",
            family.SeaLevels);
        WriteTypes(
            writer,
            "mountainPeaks",
            family.MountainPeaks);
        WriteTypes(
            writer,
            "noIssuedStructuralRoles",
            family.NoIssuedStructuralRoles);
        writer.WriteEndObject();
    }

    private static void WriteCompositionReceipt(
        Utf8JsonWriter writer,
        LibraryFamilyRoleCompositionReceipt receipt,
        Dictionary<GraphDocumentIdentity, string> graphDocuments)
    {
        writer.WriteStartObject();
        WriteRoleCounts(
            writer,
            receipt.ExactTypeCount,
            receipt.FoundationCount,
            receipt.HubCount,
            receipt.OrchestratorCount,
            receipt.SeaLevelCount,
            receipt.MountainPeakCount,
            receipt.NoIssuedStructuralRoleCount);
        writer.WriteNumber(
            "oneWordFamilyRowCount",
            receipt.OneWordFamilyRowCount);
        writer.WriteNumber(
            "twoWordFamilyRowCount",
            receipt.TwoWordFamilyRowCount);
        writer.WritePropertyName("populations");
        writer.WriteStartArray();
        foreach (LibraryFamilyRolePopulationReceipt population
            in receipt.Populations)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", population.Kind.ToString());
            WriteRoleCounts(
                writer,
                population.TypeCount,
                population.FoundationCount,
                population.HubCount,
                population.OrchestratorCount,
                population.SeaLevelCount,
                population.MountainPeakCount,
                population.NoIssuedStructuralRoleCount);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WritePropertyName("nameFamilies");
        LibraryNameFamilyInspectionJson.WriteReceipt(
            writer,
            receipt.NameFamilies);
        writer.WritePropertyName("structuralSalience");
        WriteStructuralReceipt(
            writer,
            receipt.StructuralSalience,
            graphDocuments);
        writer.WriteEndObject();
    }

    private static void WriteStructuralReceipt(
        Utf8JsonWriter writer,
        LibraryFamilyRoleStructuralReceipt receipt,
        Dictionary<GraphDocumentIdentity, string> graphDocuments)
    {
        writer.WriteStartObject();
        writer.WriteString(
            "methodologyVersion",
            receipt.MethodologyVersion);
        writer.WriteString(
            "evidenceMode",
            receipt.EvidenceMode.ToString());
        writer.WriteString(
            "namespaceDisposition",
            receipt.NamespaceDisposition.ToString());
        writer.WritePropertyName("signatureUse");
        LibraryMetricsInspectionJson.WriteSignatureUseQualification(
            writer,
            receipt.SignatureUse);
        writer.WritePropertyName("shards");
        writer.WriteStartArray();
        foreach (LibraryFamilyRoleStructuralShardReceipt shard
            in receipt.Shards)
        {
            writer.WriteStartObject();
            writer.WriteString("namespace", shard.Namespace);
            writer.WriteString(
                "roleDisposition",
                shard.RoleDisposition.ToString());
            writer.WritePropertyName("signatureUse");
            LibraryMetricsInspectionJson.WriteSignatureUseQualification(
                writer,
                shard.SignatureUse);
            writer.WritePropertyName("graphWork");
            writer.WriteStartObject();
            writer.WritePropertyName("signatureIncomingDegree");
            LibraryMetricsInspectionJson.WriteGraphWorkReceipt(
                writer,
                shard.GraphWork.SignatureIncomingDegree,
                graphDocuments);
            writer.WritePropertyName("signatureOutgoingDegree");
            LibraryMetricsInspectionJson.WriteGraphWorkReceipt(
                writer,
                shard.GraphWork.SignatureOutgoingDegree,
                graphDocuments);
            writer.WriteEndObject();
            writer.WriteString(
                "seaLevelDisposition",
                shard.SeaLevelDisposition.ToString());
            writer.WriteString(
                "mountainPeakDisposition",
                shard.MountainPeakDisposition.ToString());
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteRoleCounts(
        Utf8JsonWriter writer,
        int typeCount,
        int foundationCount,
        int hubCount,
        int orchestratorCount,
        int seaLevelCount,
        int mountainPeakCount,
        int noRoleCount)
    {
        writer.WriteNumber("typeCount", typeCount);
        writer.WriteNumber("foundationCount", foundationCount);
        writer.WriteNumber("hubCount", hubCount);
        writer.WriteNumber(
            "orchestratorCount",
            orchestratorCount);
        writer.WriteNumber("seaLevelCount", seaLevelCount);
        writer.WriteNumber(
            "mountainPeakCount",
            mountainPeakCount);
        writer.WriteNumber(
            "noIssuedStructuralRoleCount",
            noRoleCount);
    }

    private static void WriteTypes(
        Utf8JsonWriter writer,
        string propertyName,
        IEnumerable<MetadataTypeDefinitionAddress> types)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartArray();
        foreach (MetadataTypeDefinitionAddress type in types)
        {
            LibraryNameFamilyInspectionJson.WriteTypeAddress(
                writer,
                type);
        }
        writer.WriteEndArray();
    }

    private static void WriteNumber(
        Utf8JsonWriter writer,
        string propertyName,
        int? value)
    {
        if (value is null)
            writer.WriteNull(propertyName);
        else
            writer.WriteNumber(propertyName, value.Value);
    }

    private static void WriteEnum<TEnum>(
        Utf8JsonWriter writer,
        string propertyName,
        TEnum? value)
        where TEnum : struct, Enum
    {
        if (value is null)
            writer.WriteNull(propertyName);
        else
            writer.WriteString(propertyName, value.Value.ToString());
    }
}
