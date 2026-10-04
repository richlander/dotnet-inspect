using System.Text.Json;

using CSharpText;

using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.ResearchSections;

public static class LibraryNameFamilyInspectionJson
{
    public static void Write(
        Utf8JsonWriter writer,
        LibraryNameFamilyDocument document)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(document);

        writer.WriteStartObject();
        writer.WritePropertyName("binding");
        WriteBinding(writer, document.Binding);
        writer.WritePropertyName("methodology");
        WriteMethodology(writer, document.Methodology);
        writer.WritePropertyName("receipt");
        WriteReceipt(writer, document.Receipt);
        writer.WritePropertyName("provenance");
        WriteProvenance(writer, document.Provenance);
        writer.WritePropertyName("types");
        writer.WriteStartArray();
        foreach (LibraryNameFamilyTypeRow type in document.Types)
            WriteType(writer, type);
        writer.WriteEndArray();
        writer.WritePropertyName("populations");
        writer.WriteStartArray();
        foreach (LibraryNameFamilyPopulation population
            in document.Populations)
        {
            WritePopulation(writer, population);
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteBinding(
        Utf8JsonWriter writer,
        LibraryNameFamilyBinding binding)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("artifactBound", true);
        writer.WritePropertyName("assembly");
        WriteAssembly(writer, binding.Assembly);
        writer.WriteString("moduleVersionId", binding.ModuleVersionId);
        writer.WriteEndObject();
    }

    private static void WriteAssembly(
        Utf8JsonWriter writer,
        AssemblyReferenceIdentity assembly)
    {
        writer.WriteStartObject();
        writer.WriteString("name", assembly.Name);
        WriteString(writer, "version", assembly.Version?.ToString());
        WriteString(writer, "culture", assembly.Culture);
        WriteString(
            writer,
            "publicKeyToken",
            assembly.PublicKeyToken);
        writer.WriteEndObject();
    }

    private static void WriteMethodology(
        Utf8JsonWriter writer,
        LibraryNameFamilyMethodology methodology)
    {
        writer.WriteStartObject();
        writer.WriteString("version", methodology.Version);
        writer.WritePropertyName("wordOracle");
        WriteOracle(writer, methodology.WordOracle);
        writer.WriteEndObject();
    }

    private static void WriteReceipt(
        Utf8JsonWriter writer,
        LibraryNameFamilyReceipt receipt)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("limits");
        writer.WriteStartObject();
        writer.WriteNumber(
            "maximumRetainedTypes",
            receipt.Limits.MaximumRetainedTypes);
        writer.WriteNumber(
            "maximumRetainedNameCharacters",
            receipt.Limits.MaximumRetainedNameCharacters);
        writer.WriteEndObject();
        writer.WriteNumber("typeCount", receipt.TypeCount);
        writer.WriteNumber(
            "typeNameCharacterCount",
            receipt.TypeNameCharacterCount);
        writer.WriteNumber(
            "typeWithUnresolvedSpanCount",
            receipt.TypeWithUnresolvedSpanCount);
        writer.WritePropertyName("oracle");
        WriteOracle(writer, receipt.Oracle);
        writer.WritePropertyName("numberedFamilyPopulation");
        writer.WriteStartObject();
        writer.WriteString(
            "grammarVersion",
            receipt.NumberedFamilyPopulation.GrammarVersion);
        writer.WriteString(
            "digest",
            receipt.NumberedFamilyPopulation.Digest);
        writer.WriteNumber(
            "identifierCount",
            receipt.NumberedFamilyPopulation.IdentifierCount);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteOracle(
        Utf8JsonWriter writer,
        IdentifierWordOracleReceipt oracle)
    {
        writer.WriteStartObject();
        writer.WriteString("grammarVersion", oracle.GrammarVersion);
        writer.WriteString(
            "vocabularyVersion",
            oracle.VocabularyVersion);
        writer.WriteString("digest", oracle.Digest);
        writer.WriteString(
            "sourceCoordinate",
            oracle.SourceCoordinate);
        writer.WriteString(
            "reviewSetVersion",
            oracle.ReviewSetVersion);
        writer.WriteNumber("entryCount", oracle.EntryCount);
        writer.WriteEndObject();
    }

    private static void WriteProvenance(
        Utf8JsonWriter writer,
        LibraryNameFamilyProvenanceQualification provenance)
    {
        writer.WriteStartObject();
        writer.WriteString("state", provenance.State.ToString());
        writer.WritePropertyName("binding");
        if (provenance.Binding is { } binding)
            WriteProvenanceBinding(writer, binding);
        else
            writer.WriteNullValue();
        WriteEnum(
            writer,
            "unavailableReason",
            provenance.UnavailableReason);
        WriteEnum(
            writer,
            "incompleteReason",
            provenance.IncompleteReason);
        WriteEnum(writer, "rejection", provenance.Rejection);
        WriteString(writer, "detail", provenance.Detail);
        writer.WriteEndObject();
    }

    private static void WriteProvenanceBinding(
        Utf8JsonWriter writer,
        PdbSourceProvenanceBinding binding)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("artifactBound", true);
        writer.WritePropertyName("assembly");
        WriteAssembly(writer, binding.Assembly);
        writer.WriteString(
            "moduleVersionId",
            binding.ModuleVersionId);
        writer.WriteString(
            "portablePdbContentId",
            Convert.ToHexString(
                binding.PortablePdbContentId.AsSpan())
                .ToLowerInvariant());
        writer.WriteNumber(
            "pdbGeneration",
            binding.PdbGeneration);
        writer.WriteString(
            "pathProfile",
            binding.PathProfile.ToString());
        writer.WriteEndObject();
    }

    private static void WriteType(
        Utf8JsonWriter writer,
        LibraryNameFamilyTypeRow type)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("type");
        WriteTypeAddress(writer, type.Type);
        writer.WritePropertyName("name");
        WriteTypeName(writer, type.Name);
        writer.WriteString(
            "metadataSimpleName",
            type.MetadataSimpleName);
        writer.WriteString("nameStem", type.NameStem);
        writer.WritePropertyName("spans");
        writer.WriteStartArray();
        foreach (IdentifierWordSpan span in type.Spans)
            WriteSpan(writer, span);
        writer.WriteEndArray();
        writer.WritePropertyName("oneWordSuffix");
        WriteFamilyIdentity(writer, type.OneWordSuffix);
        writer.WritePropertyName("twoWordSuffix");
        WriteFamilyIdentity(writer, type.TwoWordSuffix);
        WriteEnum(
            writer,
            "oneWordResidual",
            type.OneWordResidual);
        WriteEnum(
            writer,
            "twoWordResidual",
            type.TwoWordResidual);
        writer.WriteBoolean(
            "isDefinitionPublic",
            type.IsDefinitionPublic);
        writer.WriteBoolean(
            "isPublicSurface",
            type.IsPublicSurface);
        writer.WriteString(
            "definitionKind",
            type.DefinitionKind.ToString());
        writer.WritePropertyName("sourceEvidence");
        if (type.SourceEvidence is { } source)
            WriteSourceEvidence(writer, source);
        else
            writer.WriteNullValue();
        writer.WriteEndObject();
    }

    private static void WriteSpan(
        Utf8JsonWriter writer,
        IdentifierWordSpan span)
    {
        writer.WriteStartObject();
        writer.WriteNumber("start", span.Start);
        writer.WriteNumber("length", span.Length);
        writer.WriteString("text", span.Text);
        writer.WriteString(
            "classification",
            span.Classification.ToString());
        writer.WritePropertyName("evidence");
        writer.WriteStartObject();
        writer.WriteString("kind", span.Evidence.Kind.ToString());
        WriteString(
            writer,
            "oracleEntry",
            span.Evidence.OracleEntry);
        WriteEnum(
            writer,
            "oracleEntryKind",
            span.Evidence.OracleEntryKind);
        WriteEnum(
            writer,
            "unicodeCategory",
            span.Evidence.UnicodeCategory);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteSourceEvidence(
        Utf8JsonWriter writer,
        PdbTypeSourceEvidence source)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("type");
        WriteTypeAddress(writer, source.Type);
        writer.WriteString(
            "metadataName",
            source.MetadataName.ToString());
        writer.WritePropertyName("documentRowIds");
        writer.WriteStartArray();
        foreach (int rowId in source.DocumentRowIds)
            writer.WriteNumberValue(rowId);
        writer.WriteEndArray();
        writer.WritePropertyName("contributions");
        writer.WriteStartArray();
        foreach (PdbTypeSourceContribution contribution
            in source.Contributions)
        {
            writer.WriteStartObject();
            writer.WriteString(
                "kind",
                contribution.Kind.ToString());
            writer.WritePropertyName("method");
            if (contribution.Method is { } method)
            {
                writer.WriteStartObject();
                writer.WriteString(
                    "moduleVersionId",
                    method.ModuleVersionId);
                writer.WriteNumber(
                    "metadataToken",
                    method.MetadataToken);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNullValue();
            }
            WriteNumber(
                writer,
                "documentRowId",
                contribution.DocumentRowId);
            writer.WritePropertyName("marker");
            if (contribution.Marker is { } marker)
                WriteMarker(writer, marker);
            else
                writer.WriteNullValue();
            writer.WriteBoolean(
                "inherited",
                contribution.Inherited);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WritePropertyName("directMarkers");
        WriteMarkers(writer, source.DirectMarkers);
        writer.WritePropertyName("inheritedMarkers");
        WriteMarkers(writer, source.InheritedMarkers);
        writer.WriteString(
            "disposition",
            source.Disposition.ToString());
        writer.WriteEndObject();
    }

    private static void WriteMarkers(
        Utf8JsonWriter writer,
        IEnumerable<PdbGenerationMarkerEvidence> markers)
    {
        writer.WriteStartArray();
        foreach (PdbGenerationMarkerEvidence marker in markers)
            WriteMarker(writer, marker);
        writer.WriteEndArray();
    }

    private static void WriteMarker(
        Utf8JsonWriter writer,
        PdbGenerationMarkerEvidence marker)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", marker.Kind.ToString());
        writer.WriteString(
            "disposition",
            marker.Disposition.ToString());
        WriteString(
            writer,
            "declaredTool",
            marker.DeclaredTool?.ToString());
        WriteString(
            writer,
            "declaredVersion",
            marker.DeclaredVersion?.ToString());
        writer.WriteEndObject();
    }

    private static void WritePopulation(
        Utf8JsonWriter writer,
        LibraryNameFamilyPopulation population)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", population.Kind.ToString());
        writer.WriteNumber("typeCount", population.TypeCount);
        writer.WritePropertyName("families");
        writer.WriteStartArray();
        foreach (LibraryNameFamilyRow family in population.Families)
            WriteFamily(writer, family);
        writer.WriteEndArray();
        writer.WritePropertyName("oneWord");
        WriteOneWordReceipt(writer, population.OneWord);
        writer.WritePropertyName("twoWord");
        WriteTwoWordReceipt(writer, population.TwoWord);
        writer.WriteEndObject();
    }

    private static void WriteFamily(
        Utf8JsonWriter writer,
        LibraryNameFamilyRow family)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("identity");
        WriteFamilyIdentity(writer, family.Identity);
        writer.WriteNumber("typeCount", family.TypeCount);
        writer.WriteNumber(
            "publicTypeCount",
            family.PublicTypeCount);
        writer.WriteNumber(
            "distinctNamespaceCount",
            family.DistinctNamespaceCount);
        writer.WritePropertyName("definitionKinds");
        writer.WriteStartArray();
        foreach (LibraryNameFamilyDefinitionKindCount count
            in family.DefinitionKinds)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", count.Kind.ToString());
            writer.WriteNumber("count", count.Count);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WritePropertyName("sourceDispositions");
        writer.WriteStartArray();
        foreach (LibraryNameFamilySourceDispositionCount count
            in family.SourceDispositions)
        {
            writer.WriteStartObject();
            writer.WriteString(
                "disposition",
                count.Disposition.ToString());
            writer.WriteNumber("count", count.Count);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WritePropertyName("types");
        writer.WriteStartArray();
        foreach (MetadataTypeDefinitionAddress type in family.Types)
            WriteTypeAddress(writer, type);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteFamilyIdentity(
        Utf8JsonWriter writer,
        LibraryNameFamilyIdentity? identity)
    {
        if (identity is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString(
            "methodologyVersion",
            identity.Methodology.Version);
        writer.WriteString("kind", identity.Kind.ToString());
        writer.WritePropertyName("words");
        writer.WriteStartArray();
        foreach (string word in identity.Words)
            writer.WriteStringValue(word);
        writer.WriteEndArray();
        WriteString(writer, "separator", identity.Separator);
        writer.WriteEndObject();
    }

    private static void WriteOneWordReceipt(
        Utf8JsonWriter writer,
        LibraryNameFamilyOneWordPartitionReceipt receipt)
    {
        writer.WriteStartObject();
        WritePartitionCounts(
            writer,
            receipt.TotalTypeCount,
            receipt.EligibleTypeCount,
            receipt.ResidualTypeCount);
        writer.WritePropertyName("residuals");
        WriteResiduals(writer, receipt.Residuals);
        writer.WriteEndObject();
    }

    private static void WriteTwoWordReceipt(
        Utf8JsonWriter writer,
        LibraryNameFamilyTwoWordPartitionReceipt receipt)
    {
        writer.WriteStartObject();
        WritePartitionCounts(
            writer,
            receipt.TotalTypeCount,
            receipt.EligibleTypeCount,
            receipt.ResidualTypeCount);
        writer.WritePropertyName("residuals");
        WriteResiduals(writer, receipt.Residuals);
        writer.WriteEndObject();
    }

    private static void WritePartitionCounts(
        Utf8JsonWriter writer,
        int total,
        int eligible,
        int residual)
    {
        writer.WriteNumber("totalTypeCount", total);
        writer.WriteNumber("eligibleTypeCount", eligible);
        writer.WriteNumber("residualTypeCount", residual);
    }

    private static void WriteResiduals<TReason>(
        Utf8JsonWriter writer,
        IEnumerable<LibraryNameFamilyResidualCount<TReason>> residuals)
        where TReason : struct, Enum
    {
        writer.WriteStartArray();
        foreach (LibraryNameFamilyResidualCount<TReason> residual
            in residuals)
        {
            writer.WriteStartObject();
            writer.WriteString(
                "reason",
                residual.Reason.ToString());
            writer.WriteNumber("count", residual.Count);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteTypeAddress(
        Utf8JsonWriter writer,
        MetadataTypeDefinitionAddress address)
    {
        writer.WriteStartObject();
        writer.WriteString(
            "moduleVersionId",
            address.ModuleVersionId);
        writer.WriteNumber(
            "definitionToken",
            address.Definition.Value);
        writer.WriteEndObject();
    }

    private static void WriteTypeName(
        Utf8JsonWriter writer,
        MetadataTypeDefinitionName name)
    {
        writer.WriteStartObject();
        writer.WriteString("namespace", name.Namespace);
        writer.WritePropertyName("segments");
        writer.WriteStartArray();
        foreach (string segment in name.Segments)
            writer.WriteStringValue(segment);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteString(
        Utf8JsonWriter writer,
        string propertyName,
        string? value)
    {
        if (value is null)
            writer.WriteNull(propertyName);
        else
            writer.WriteString(propertyName, value);
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
