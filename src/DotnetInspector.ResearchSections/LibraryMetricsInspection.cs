using System.Text.Json;

using DotnetInspector.Sections;

using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;
using ILInspector.Research;
using Inspector.Graph;

namespace DotnetInspector.ResearchSections;

public static class LibraryMetricsInspection
{
    public static InspectionEnvelope<LibraryStructuralReportDocument> Execute(
        LibraryStructuralReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new(
            document,
            new InspectionShare.NonProjectable(
                "library-metrics/share",
                "Inspect Web cannot yet restore an exact Library Metrics inspection."),
            []);
    }
}

public static class LibraryMetricsInspectionJson
{
    public static void Write(
        Utf8JsonWriter writer,
        LibraryStructuralReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(document);

        writer.WriteStartObject();
        writer.WritePropertyName("analysisReceipt");
        WriteAnalysisReceipt(writer, document.AnalysisReceipt);
        writer.WriteString(
            "methodologyVersion",
            document.MethodologyVersion);
        writer.WritePropertyName("population");
        WritePopulation(writer, document.Population);
        writer.WritePropertyName("distributions");
        writer.WriteStartArray();
        foreach (LibraryStructuralMetricDistribution distribution
            in document.Distributions)
        {
            WriteDistribution(writer, distribution);
        }
        writer.WriteEndArray();
        writer.WritePropertyName("asyncStateMachinePresence");
        WriteBooleanDisposition(
            writer,
            document.AsyncStateMachinePresence);
        writer.WritePropertyName("typeSummaries");
        writer.WriteStartArray();
        foreach (LibraryStructuralTypeSummary summary
            in document.TypeSummaries)
        {
            WriteTypeSummary(writer, summary);
        }
        writer.WriteEndArray();
        writer.WritePropertyName("entangledRelationships");
        writer.WriteStartArray();
        foreach (LibraryStructuralTypeRelationship relationship
            in document.EntangledRelationships)
        {
            WriteRelationship(writer, relationship);
        }
        writer.WriteEndArray();
        writer.WritePropertyName("structuralSalience");
        if (document.StructuralSalience is { } structuralSalience)
            WriteStructuralSalience(writer, structuralSalience);
        else
            writer.WriteNullValue();
        writer.WritePropertyName("diagnostics");
        WriteDiagnostics(writer, document.Diagnostics);
        writer.WriteEndObject();
    }

    private static void WriteAnalysisReceipt(
        Utf8JsonWriter writer,
        LibraryBodyAnalysisReceipt receipt)
    {
        writer.WriteStartObject();
        writer.WriteString("sourceName", receipt.SourceName);
        writer.WritePropertyName("moduleIdentity");
        WriteModuleIdentity(writer, receipt.ModuleIdentity);
        writer.WriteString("features", receipt.Features.ToString());
        writer.WriteNumber("featureMask", (int)receipt.Features);
        writer.WriteBoolean(
            "hasFullMethodEvidenceScope",
            receipt.HasFullMethodEvidenceScope);
        writer.WritePropertyName("diagnostics");
        WriteDiagnostics(writer, receipt.Diagnostics);
        writer.WriteEndObject();
    }

    private static void WriteModuleIdentity(
        Utf8JsonWriter writer,
        LibraryBodyModuleIdentity identity)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("assemblyIdentity");
        if (identity.AssemblyIdentity is { } assembly)
            WriteAssemblyIdentity(writer, assembly);
        else
            writer.WriteNullValue();
        writer.WriteString(
            "moduleVersionId",
            identity.ModuleVersionId);
        writer.WriteEndObject();
    }

    private static void WriteAssemblyIdentity(
        Utf8JsonWriter writer,
        AssemblyReferenceIdentity identity)
    {
        writer.WriteStartObject();
        writer.WriteString("name", identity.Name);
        WriteString(writer, "version", identity.Version?.ToString());
        WriteString(writer, "culture", identity.Culture);
        WriteString(
            writer,
            "publicKeyToken",
            identity.PublicKeyToken);
        writer.WriteEndObject();
    }

    private static void WritePopulation(
        Utf8JsonWriter writer,
        LibraryStructuralPopulationReceipt population)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("coverage");
        WriteCoverage(writer, population.Coverage);
        writer.WriteNumber(
            "physicalEvidenceBodyCount",
            population.PhysicalEvidenceBodyCount);
        writer.WriteNumber(
            "profiledPhysicalEvidenceBodyCount",
            population.ProfiledPhysicalEvidenceBodyCount);
        writer.WriteNumber(
            "logicalOwnerCount",
            population.LogicalOwnerCount);
        writer.WriteNumber(
            "completeProfileCount",
            population.CompleteProfileCount);
        writer.WriteNumber(
            "incompleteProfileCount",
            population.IncompleteProfileCount);
        writer.WritePropertyName("incompleteReasons");
        WriteReasonCounts(writer, population.IncompleteReasons);
        writer.WritePropertyName("unavailableReasons");
        WriteReasonCounts(writer, population.UnavailableReasons);
        writer.WriteEndObject();
    }

    private static void WriteCoverage(
        Utf8JsonWriter writer,
        ImplementationProfilePopulationCoverageReceipt coverage)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("wasRequested", coverage.WasRequested);
        writer.WriteBoolean(
            "hasFullMethodEvidenceScope",
            coverage.HasFullMethodEvidenceScope);
        writer.WriteNumber(
            "declaredMethodCount",
            coverage.DeclaredMethodCount);
        writer.WriteNumber(
            "managedMethodBodyCount",
            coverage.ManagedMethodBodyCount);
        writer.WriteNumber(
            "profiledEvidenceBodyCount",
            coverage.ProfiledEvidenceBodyCount);
        writer.WriteNumber(
            "unavailableBodyCount",
            coverage.UnavailableBodyCount);
        writer.WritePropertyName("declaredMethods");
        WriteMethods(writer, coverage.DeclaredMethods);
        writer.WritePropertyName("managedMethodBodies");
        WriteMethods(writer, coverage.ManagedMethodBodies);
        writer.WritePropertyName("profiledEvidenceBodies");
        WriteMethods(writer, coverage.ProfiledEvidenceBodies);
        writer.WritePropertyName("unavailableBodies");
        writer.WriteStartArray();
        foreach (ImplementationProfileUnavailableBody body
            in coverage.UnavailableBodies)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("evidenceMethod");
            if (body.EvidenceMethod is { } method)
                WriteMethod(writer, method);
            else
                writer.WriteNullValue();
            writer.WriteNumber("methodToken", body.MethodToken);
            writer.WriteString("reason", body.Reason.ToString());
            writer.WritePropertyName("diagnostic");
            if (body.Diagnostic is { } diagnostic)
                WriteDiagnostic(writer, diagnostic);
            else
                writer.WriteNullValue();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WritePropertyName("diagnostics");
        WriteDiagnostics(writer, coverage.Diagnostics);
        writer.WriteEndObject();
    }

    private static void WriteMethods(
        Utf8JsonWriter writer,
        IEnumerable<MethodIdentity> methods)
    {
        writer.WriteStartArray();
        foreach (MethodIdentity method in methods)
            WriteMethod(writer, method);
        writer.WriteEndArray();
    }

    private static void WriteMethod(
        Utf8JsonWriter writer,
        MethodIdentity method)
    {
        writer.WriteStartObject();
        writer.WriteString("assemblyName", method.AssemblyName);
        writer.WriteString(
            "moduleVersionId",
            method.ModuleVersionId);
        writer.WriteNumber("metadataToken", method.MetadataToken);
        writer.WritePropertyName("declaringType");
        AnalysisIdentityJson.WriteType(writer, method.DeclaringType);
        writer.WriteString("name", method.Name);
        writer.WritePropertyName("parameterTypes");
        writer.WriteStartArray();
        foreach (TypeRef parameterType in method.ParameterTypes)
            AnalysisIdentityJson.WriteType(writer, parameterType);
        writer.WriteEndArray();
        writer.WritePropertyName("returnType");
        AnalysisIdentityJson.WriteType(writer, method.ReturnType);
        writer.WriteBoolean("isStatic", method.IsStatic);
        writer.WriteBoolean("isExtension", method.IsExtension);
        writer.WriteString(
            "callerUnsafeMode",
            method.CallerUnsafeMode.ToString());
        writer.WriteNumber("genericArity", method.GenericArity);
        writer.WritePropertyName("genericParameterNames");
        writer.WriteStartArray();
        foreach (string parameterName in method.GenericParameterNames)
            writer.WriteStringValue(parameterName);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteReasonCounts(
        Utf8JsonWriter writer,
        IEnumerable<LibraryStructuralReasonCount> reasons)
    {
        writer.WriteStartArray();
        foreach (LibraryStructuralReasonCount reason in reasons)
        {
            writer.WriteStartObject();
            writer.WriteString("reason", reason.Reason);
            writer.WriteNumber("count", reason.Count);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteDistribution(
        Utf8JsonWriter writer,
        LibraryStructuralMetricDistribution distribution)
    {
        writer.WriteStartObject();
        writer.WriteString("metric", distribution.Metric.ToString());
        writer.WriteNumber(
            "completeBodyCount",
            distribution.CompleteBodyCount);
        WriteNumber(writer, "minimum", distribution.Minimum);
        WriteNumber(writer, "p50", distribution.P50);
        WriteNumber(writer, "p90", distribution.P90);
        WriteNumber(writer, "p95", distribution.P95);
        WriteNumber(writer, "p99", distribution.P99);
        WriteNumber(writer, "maximum", distribution.Maximum);
        writer.WritePropertyName("maximumBodies");
        writer.WriteStartArray();
        foreach (LibraryStructuralExtremeBody body
            in distribution.MaximumBodies)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("evidenceMethod");
            WriteMethod(writer, body.EvidenceMethod);
            writer.WritePropertyName("logicalOwner");
            WriteMethod(writer, body.LogicalOwner);
            writer.WriteNumber("value", body.Value);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteNumber(
            "additionalMaximumBodyCount",
            distribution.AdditionalMaximumBodyCount);
        writer.WriteEndObject();
    }

    private static void WriteBooleanDisposition(
        Utf8JsonWriter writer,
        LibraryStructuralBooleanDisposition disposition)
    {
        writer.WriteStartObject();
        writer.WriteString("name", disposition.Name);
        writer.WriteNumber(
            "completeBodyCount",
            disposition.CompleteBodyCount);
        writer.WriteNumber("presentCount", disposition.PresentCount);
        writer.WriteNumber("absentCount", disposition.AbsentCount);
        writer.WriteEndObject();
    }

    private static void WriteTypeSummary(
        Utf8JsonWriter writer,
        LibraryStructuralTypeSummary summary)
    {
        writer.WriteStartObject();
        WriteTypeIdentity(writer, summary.Type);
        writer.WriteNumber("bodyCount", summary.BodyCount);
        writer.WriteNumber(
            "instructionCount",
            summary.InstructionCount);
        writer.WriteNumber(
            "complexityTotal",
            summary.ComplexityTotal);
        writer.WriteNumber("loopCount", summary.LoopCount);
        writer.WriteNumber(
            "directCallCount",
            summary.DirectCallCount);
        writer.WriteNumber(
            "allocationCount",
            summary.AllocationCount);
        writer.WriteEndObject();
    }

    private static void WriteRelationship(
        Utf8JsonWriter writer,
        LibraryStructuralTypeRelationship relationship)
    {
        writer.WriteStartObject();
        writer.WriteString(
            "sourceTypeKey",
            LibraryStructuralReport.TypeKey(relationship.Source));
        writer.WriteString(
            "sourceDisplay",
            relationship.Source.ToQualifiedDisplayString());
        writer.WritePropertyName("source");
        AnalysisIdentityJson.WriteType(writer, relationship.Source);
        writer.WriteString(
            "targetTypeKey",
            LibraryStructuralReport.TypeKey(relationship.Target));
        writer.WriteString(
            "targetDisplay",
            relationship.Target.ToQualifiedDisplayString());
        writer.WritePropertyName("target");
        AnalysisIdentityJson.WriteType(writer, relationship.Target);
        writer.WriteNumber(
            "callSiteCount",
            relationship.CallSiteCount);
        writer.WriteNumber(
            "sourceDegree",
            relationship.SourceDegree);
        writer.WriteNumber(
            "targetDegree",
            relationship.TargetDegree);
        writer.WriteEndObject();
    }

    private static void WriteTypeIdentity(
        Utf8JsonWriter writer,
        TypeRef type)
    {
        writer.WriteString(
            "typeKey",
            LibraryStructuralReport.TypeKey(type));
        writer.WriteString(
            "display",
            type.ToQualifiedDisplayString());
        writer.WritePropertyName("type");
        AnalysisIdentityJson.WriteType(writer, type);
    }

    private static void WriteStructuralSalience(
        Utf8JsonWriter writer,
        LibraryStructuralSalienceDocument salience)
    {
        var graphDocuments =
            new Dictionary<GraphDocumentIdentity, string>(
                ReferenceEqualityComparer.Instance);
        writer.WriteStartObject();
        writer.WriteString(
            "methodologyVersion",
            salience.MethodologyVersion);
        writer.WriteString(
            "evidenceMode",
            salience.EvidenceMode.ToString());
        writer.WritePropertyName("namespaceIndex");
        WriteNamespaceIndex(writer, salience.NamespaceIndex);
        writer.WritePropertyName("typeLeverageShards");
        writer.WriteStartArray();
        foreach (LibraryStructuralTypeLeverageShard shard
            in salience.TypeLeverageShards)
        {
            WriteTypeLeverageShard(
                writer,
                shard,
                graphDocuments);
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteNamespaceIndex(
        Utf8JsonWriter writer,
        LibraryStructuralNamespaceLeverageIndex index)
    {
        writer.WriteStartObject();
        writer.WriteString(
            "methodologyVersion",
            index.MethodologyVersion);
        writer.WriteString(
            "evidenceMode",
            index.EvidenceMode.ToString());
        writer.WriteString(
            "disposition",
            index.Disposition.ToString());
        writer.WritePropertyName("rows");
        writer.WriteStartArray();
        foreach (LibraryStructuralNamespaceLeverageRow row
            in index.Rows)
        {
            writer.WriteStartObject();
            writer.WriteString("namespace", row.Namespace);
            writer.WriteNumber("typeCount", row.TypeCount);
            writer.WriteNumber(
                "externalIncomingSourceTypeCount",
                row.ExternalIncomingSourceTypeCount);
            writer.WriteBoolean(
                "topLeverage",
                row.TopLeverage);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WritePropertyName("signatureUse");
        WriteSignatureUseQualification(writer, index.SignatureUse);
        writer.WriteEndObject();
    }

    private static void WriteTypeLeverageShard(
        Utf8JsonWriter writer,
        LibraryStructuralTypeLeverageShard leverage,
        Dictionary<GraphDocumentIdentity, string> graphDocuments)
    {
        writer.WriteStartObject();
        writer.WriteString(
            "methodologyVersion",
            leverage.MethodologyVersion);
        writer.WriteString(
            "evidenceMode",
            leverage.EvidenceMode.ToString());
        writer.WriteString("namespace", leverage.Namespace);
        writer.WritePropertyName("rows");
        writer.WriteStartArray();
        foreach (LibraryStructuralTypeLeverageRow row in leverage.Rows)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("type");
            WriteTypeAddress(writer, row.Type);
            writer.WritePropertyName("name");
            WriteTypeName(writer, row.Name);
            writer.WriteString(
                "classification",
                row.Classification.ToString());
            writer.WriteNumber(
                "classificationMask",
                (int)row.Classification);
            writer.WriteBoolean(
                "designationEligible",
                row.DesignationEligible);
            writer.WriteNumber(
                "signatureIncomingDegree",
                row.SignatureIncomingDegree);
            writer.WriteNumber(
                "signatureOutgoingDegree",
                row.SignatureOutgoingDegree);
            writer.WriteString("role", row.Role.ToString());
            if (row.Pole is { } pole)
                writer.WriteString("pole", pole.ToString());
            else
                writer.WriteNull("pole");
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WritePropertyName("seaLevel");
        WriteTypeLeverageOrder(writer, leverage.SeaLevel);
        writer.WritePropertyName("mountainPeak");
        WriteTypeLeverageOrder(writer, leverage.MountainPeak);
        writer.WriteString(
            "roleDisposition",
            leverage.RoleDisposition.ToString());
        writer.WritePropertyName("signatureUse");
        WriteSignatureUseQualification(
            writer,
            leverage.SignatureUse);
        writer.WritePropertyName("graphWork");
        writer.WriteStartObject();
        writer.WritePropertyName("signatureIncomingDegree");
        WriteGraphWorkReceipt(
            writer,
            leverage.GraphWork.SignatureIncomingDegree,
            graphDocuments);
        writer.WritePropertyName("signatureOutgoingDegree");
        WriteGraphWorkReceipt(
            writer,
            leverage.GraphWork.SignatureOutgoingDegree,
            graphDocuments);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteTypeLeverageOrder(
        Utf8JsonWriter writer,
        LibraryStructuralTypeLeverageOrder order)
    {
        writer.WriteStartObject();
        writer.WriteString(
            "disposition",
            order.Disposition.ToString());
        writer.WritePropertyName("types");
        writer.WriteStartArray();
        foreach (MetadataTypeDefinitionAddress type in order.Types)
            WriteTypeAddress(writer, type);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteSignatureUseQualification(
        Utf8JsonWriter writer,
        LibraryStructuralSignatureUseQualification qualification)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("receipt");
        writer.WriteStartObject();
        writer.WriteString(
            "moduleVersionId",
            qualification.Receipt.ModuleVersionId);
        writer.WritePropertyName("assemblyIdentity");
        WriteAssemblyIdentity(
            writer,
            qualification.Receipt.Assembly);
        if (qualification.Receipt.ExactNamespace is { } exactNamespace)
        {
            writer.WriteString(
                "exactNamespace",
                exactNamespace);
        }
        else
        {
            writer.WriteNull("exactNamespace");
        }
        writer.WritePropertyName("counters");
        WriteMetadataCounters(
            writer,
            qualification.Receipt.Counters);
        writer.WriteEndObject();
        writer.WriteString(
            "disposition",
            qualification.Disposition.ToString());
        writer.WritePropertyName("coverage");
        writer.WriteStartObject();
        writer.WriteNumber(
            "considered",
            qualification.Coverage.Considered);
        writer.WriteNumber(
            "examined",
            qualification.Coverage.Examined);
        writer.WriteNumber(
            "unavailable",
            qualification.Coverage.Unavailable);
        writer.WriteNumber(
            "limited",
            qualification.Coverage.Limited);
        writer.WriteEndObject();
        writer.WriteNumber(
            "occurrenceCount",
            qualification.OccurrenceCount);
        writer.WritePropertyName("diagnostics");
        writer.WriteStartArray();
        foreach (MetadataLibrarySignatureUseDiagnostic diagnostic
            in qualification.Diagnostics)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", diagnostic.Kind.ToString());
            WriteNumber(
                writer,
                "metadataToken",
                diagnostic.MetadataToken);
            writer.WriteString("detail", diagnostic.Detail);
            WriteString(
                writer,
                "budgetDimension",
                diagnostic.BudgetDimension?.ToString());
            WriteNumber(
                writer,
                "budgetLimit",
                diagnostic.BudgetLimit);
            WriteNumber(
                writer,
                "attemptedCharge",
                diagnostic.AttemptedCharge);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
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

    private static void WriteMetadataCounters(
        Utf8JsonWriter writer,
        MetadataOperationCounters counters)
    {
        writer.WriteStartObject();
        writer.WriteNumber(
            "metadataRows",
            counters.MetadataRows);
        writer.WriteNumber(
            "methodImplementationRows",
            counters.MethodImplementationRows);
        writer.WriteNumber(
            "declarationCandidates",
            counters.DeclarationCandidates);
        writer.WriteNumber(
            "relationshipEdges",
            counters.RelationshipEdges);
        writer.WriteNumber(
            "signatureBytes",
            counters.SignatureBytes);
        writer.WriteNumber(
            "genericSubstitutionNodes",
            counters.GenericSubstitutionNodes);
        writer.WriteNumber(
            "structuredNodes",
            counters.StructuredNodes);
        writer.WriteNumber(
            "retainedText",
            counters.RetainedText);
        writer.WriteNumber(
            "interfaceImplementationRows",
            counters.InterfaceImplementationRows);
        writer.WriteNumber(
            "retainedMethodSemanticsAssociations",
            counters.RetainedMethodSemanticsAssociations);
        writer.WriteEndObject();
    }

    private static void WriteBodyUseCoverage(
        Utf8JsonWriter writer,
        AnalysisLibraryBodyUseCoverage coverage)
    {
        writer.WriteStartObject();
        writer.WriteNumber(
            "bodiesConsidered",
            coverage.BodiesConsidered);
        writer.WriteNumber(
            "bodiesExamined",
            coverage.BodiesExamined);
        writer.WriteNumber(
            "bodiesPhysicalOnly",
            coverage.BodiesPhysicalOnly);
        writer.WriteNumber(
            "bodiesUnavailable",
            coverage.BodiesUnavailable);
        writer.WriteNumber(
            "bodiesLimited",
            coverage.BodiesLimited);
        writer.WriteNumber(
            "operandsConsidered",
            coverage.OperandsConsidered);
        writer.WriteNumber(
            "operandsExamined",
            coverage.OperandsExamined);
        writer.WriteNumber(
            "operandsUnavailable",
            coverage.OperandsUnavailable);
        writer.WriteNumber(
            "operandsLimited",
            coverage.OperandsLimited);
        writer.WriteEndObject();
    }

    private static void WriteAnalysisWorkReceipt(
        Utf8JsonWriter writer,
        WorkReceipt receipt)
    {
        writer.WriteStartObject();
        writer.WriteNumber("unitsVisited", receipt.UnitsVisited);
        writer.WritePropertyName("producers");
        writer.WriteStartArray();
        foreach (ProducerParticipation producer in receipt.Producers)
        {
            writer.WriteStartObject();
            writer.WriteString("producer", producer.Producer);
            writer.WriteString(
                "outcome",
                producer.Outcome.ToString());
            writer.WriteNumber(
                "unitsAttempted",
                producer.UnitsAttempted);
            writer.WriteNumber(
                "unitsCompleted",
                producer.UnitsCompleted);
            writer.WriteNumber(
                "unitsFailed",
                producer.UnitsFailed);
            writer.WritePropertyName("layers");
            writer.WriteStartArray();
            foreach (ProducerLayerParticipation layer
                in producer.Layers)
            {
                writer.WriteStartObject();
                writer.WriteString("layer", layer.Layer);
                writer.WriteNumber("acquired", layer.Acquired);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WritePropertyName("critical");
        if (receipt.Critical is { } critical)
        {
            writer.WriteStartObject();
            writer.WriteString("owner", critical.Owner);
            writer.WriteString("budget", critical.Budget);
            writer.WriteNumber("unitToken", critical.UnitToken);
            writer.WriteString("unit", critical.Unit);
            writer.WriteString("message", critical.Message);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNullValue();
        }
        writer.WriteBoolean(
            "identityBudgetArmed",
            receipt.IdentityBudgetArmed);
        writer.WriteNumber(
            "identityWorkCharged",
            receipt.IdentityWorkCharged);
        writer.WriteNumber(
            "signatureShapeNodesWalked",
            receipt.SignatureShapeNodesWalked);
        writer.WriteEndObject();
    }

    private static void WriteGraphWorkReceipt(
        Utf8JsonWriter writer,
        GraphExecutionWorkReceipt receipt,
        Dictionary<GraphDocumentIdentity, string> graphDocuments)
    {
        if (!graphDocuments.TryGetValue(
                receipt.SourceDocument,
                out string? sourceDocument))
        {
            sourceDocument =
                $"graph-document-{graphDocuments.Count + 1}";
            graphDocuments.Add(
                receipt.SourceDocument,
                sourceDocument);
        }

        writer.WriteStartObject();
        writer.WriteString(
            "sourceDocument",
            sourceDocument);
        writer.WriteNumber(
            "canonicalNodesExamined",
            receipt.CanonicalNodesExamined);
        writer.WriteNumber(
            "canonicalEdgesExamined",
            receipt.CanonicalEdgesExamined);
        writer.WriteNumber(
            "selectedEdgesIndexed",
            receipt.SelectedEdgesIndexed);
        writer.WriteNumber(
            "structuralViewsBuilt",
            receipt.StructuralViewsBuilt);
        writer.WriteNumber(
            "adjacencyEntriesExamined",
            receipt.AdjacencyEntriesExamined);
        writer.WriteNumber(
            "nodesAdmitted",
            receipt.NodesAdmitted);
        writer.WriteBoolean(
            "terminalSettled",
            receipt.TerminalSettled);
        writer.WriteEndObject();
    }

    private static void WriteDiagnostics(
        Utf8JsonWriter writer,
        IEnumerable<AnalysisDiagnostic> diagnostics)
    {
        writer.WriteStartArray();
        foreach (AnalysisDiagnostic diagnostic in diagnostics)
            WriteDiagnostic(writer, diagnostic);
        writer.WriteEndArray();
    }

    private static void WriteDiagnostic(
        Utf8JsonWriter writer,
        AnalysisDiagnostic diagnostic)
    {
        writer.WriteStartObject();
        writer.WriteNumber("methodToken", diagnostic.MethodToken);
        writer.WriteString("method", diagnostic.Method);
        writer.WriteString("message", diagnostic.Message);
        WriteNumber(
            writer,
            "sourceMethodToken",
            diagnostic.SourceMethodToken);
        writer.WritePropertyName("declaringType");
        if (diagnostic.DeclaringType is { } declaringType)
            AnalysisIdentityJson.WriteType(writer, declaringType);
        else
            writer.WriteNullValue();
        writer.WritePropertyName("sourceDeclaringType");
        if (diagnostic.SourceDeclaringType is { } sourceDeclaringType)
            AnalysisIdentityJson.WriteType(writer, sourceDeclaringType);
        else
            writer.WriteNullValue();
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
        if (value is { } number)
            writer.WriteNumber(propertyName, number);
        else
            writer.WriteNull(propertyName);
    }

    private static void WriteNumber(
        Utf8JsonWriter writer,
        string propertyName,
        long? value)
    {
        if (value is { } number)
            writer.WriteNumber(propertyName, number);
        else
            writer.WriteNull(propertyName);
    }
}
