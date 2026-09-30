using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Reflection;

using DotnetInspector.Fixtures;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataRelationInspectionTests
{
    [Fact]
    public void HierarchyAnalysisPassRequiresAnAdmittedSession()
    {
        System.Reflection.ConstructorInfo constructor =
            Assert.Single(
                typeof(MetadataHierarchyRelationAnalysisPass)
                    .GetConstructors());

        Assert.Equal(
            [
                typeof(AssemblyInspectionSession),
                typeof(MetadataHierarchyRelationAnalysisRequest),
            ],
            constructor.GetParameters()
                .Select(static parameter => parameter.ParameterType));
    }

    [Fact]
    public void HierarchyAnalysisRejectsNativeImageBeforeProducerExecution()
    {
        byte[] image =
            MetadataFormatAdmissionTests.BuildImage("v4.0.30319");
        MetadataFormatAdmissionTests.RemoveMetadataDirectory(image);
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(image, writable: false));

        var rejected =
            Assert.IsType<
                MetadataHierarchyRelationAnalysisOutcome.Rejected>(
                    session.AnalyzeHierarchyRelations(
                        new(
                            new(
                                TypeName("System", "Object"),
                                MetadataHierarchyRelationKind.BaseType),
                            MetadataOperationPolicy.Unbounded),
                        TestContext.Current.CancellationToken));

        Assert.IsType<MetadataImageFormatResult.NoMetadata>(
            rejected.Format);
        Assert.False(session.HasMetadata);
    }

    [Fact]
    public void AssemblyReferencePopulationPushesCountAndBoundedRows()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(
                    BuildDuplicateAssemblyReferenceImage(),
                    writable: false));

        var first =
            Assert.IsType<
                MetadataAssemblyReferenceRelationPopulationOutcome.Available>(
                    session.AssemblyReferenceRelations(
                        new(
                            MetadataOperationPolicy.Unbounded,
                            new(),
                            new(
                                startOrdinal: 0,
                                maximumRows: 1)),
                        TestContext.Current.CancellationToken));

        var counted =
            Assert.IsType<
                MetadataAssemblyReferenceRelationPopulationCountOutcome
                    .Counted>(first.Result.Count);
        Assert.Equal(2, counted.Value);
        var firstRows =
            Assert.IsType<
                MetadataAssemblyReferenceRelationPopulationRowsOutcome
                    .Read>(first.Result.Rows);
        MetadataAssemblyReferenceRelationPopulationRow firstRow =
            Assert.Single(firstRows.Items);
        Assert.Equal("Sample.First", firstRow.Target.Name);
        Assert.Equal(2, firstRow.MetadataTokens.Length);
        Assert.Equal(1, firstRows.NextOrdinal);
        Assert.Equal(3, first.Result.Coverage.Considered);
        Assert.Equal(3, first.Result.Coverage.Examined);

        var second =
            Assert.IsType<
                MetadataAssemblyReferenceRelationPopulationOutcome.Available>(
                    session.AssemblyReferenceRelations(
                        new(
                            MetadataOperationPolicy.Unbounded,
                            rows: new(
                                startOrdinal: firstRows.NextOrdinal!.Value,
                                maximumRows: 1),
                            expectedModuleVersionId:
                                first.Result.Receipt.ModuleVersionId),
                        TestContext.Current.CancellationToken));
        var secondRows =
            Assert.IsType<
                MetadataAssemblyReferenceRelationPopulationRowsOutcome
                    .Read>(second.Result.Rows);
        Assert.Equal(
            "Sample.Second",
            Assert.Single(secondRows.Items).Target.Name);
        Assert.Null(secondRows.NextOrdinal);

        var countOnly =
            Assert.IsType<
                MetadataAssemblyReferenceRelationPopulationOutcome.Available>(
                    session.AssemblyReferenceRelations(
                        new(
                            MetadataOperationPolicy.Unbounded,
                            count: new()),
                        TestContext.Current.CancellationToken));
        Assert.Equal(
            2,
            Assert.IsType<
                MetadataAssemblyReferenceRelationPopulationCountOutcome
                    .Counted>(countOnly.Result.Count).Value);
        Assert.Null(countOnly.Result.Rows);

        var stale =
            Assert.IsType<
                MetadataAssemblyReferenceRelationPopulationOutcome.Available>(
                    session.AssemblyReferenceRelations(
                        new(
                            MetadataOperationPolicy.Unbounded,
                            count: new(),
                            rows: new(
                                startOrdinal: 0,
                                maximumRows: 1),
                            expectedModuleVersionId: Guid.NewGuid()),
                        TestContext.Current.CancellationToken));
        _ = Assert.IsType<
            MetadataAssemblyReferenceRelationPopulationCountOutcome
                .Failed>(stale.Result.Count);
        Assert.Equal(
            MetadataAssemblyReferenceRelationPopulationRowsRejection
                .StaleSource,
            Assert.IsType<
                MetadataAssemblyReferenceRelationPopulationRowsOutcome
                    .Rejected>(stale.Result.Rows).Reason);
        Assert.Equal(
            MetadataRelationDiagnosticKind.StaleSource,
            Assert.Single(stale.Result.Diagnostics).Kind);
    }

    [Fact]
    public void ExtensionPopulationPushesCountAndBoundedRowsForHttpClient()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts",
            "System.Net.Http.Json.dll");
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);
        AssemblyReferenceIdentity receiverAssembly =
            Assert.Single(
                session.AssemblyReferenceIdentities(),
                static reference =>
                    reference.Name == "System.Net.Http");
        var receiver = new MetadataExtensionReceiverSelection(
            receiverAssembly,
            TypeName("System.Net.Http", "HttpClient"));

        var first =
            Assert.IsType<
                MetadataExtensionRelationPopulationOutcome.Available>(
                    session.ExtensionRelations(
                        new(
                            receiver,
                            MetadataOperationPolicy.Unbounded,
                            count: new(),
                            rows: new(
                                startOrdinal: 0,
                                maximumRows: 2)),
                        TestContext.Current.CancellationToken));
        int expected =
            Assert.IsType<
                MetadataExtensionRelationPopulationCountOutcome.Counted>(
                    first.Result.Count).Value;
        Assert.True(expected > 2);
        Assert.Equal(
            MetadataRelationFamilyDisposition.Complete,
            first.Result.Disposition);
        Assert.True(first.Result.Coverage.Excluded > 0);

        var drained =
            new List<MetadataExtensionRelationPopulationRow>();
        MetadataExtensionRelationPopulationResult current = first.Result;
        while (true)
        {
            var rows =
                Assert.IsType<
                    MetadataExtensionRelationPopulationRowsOutcome.Read>(
                        current.Rows);
            Assert.InRange(rows.Items.Length, 0, 2);
            drained.AddRange(rows.Items);
            if (rows.NextOrdinal is null)
                break;

            current =
                Assert.IsType<
                    MetadataExtensionRelationPopulationOutcome.Available>(
                        session.ExtensionRelations(
                            new(
                                receiver,
                                MetadataOperationPolicy.Unbounded,
                                rows: new(
                                    rows.NextOrdinal.Value,
                                    maximumRows: 2),
                                expectedModuleVersionId:
                                    current.Receipt.ModuleVersionId),
                            TestContext.Current.CancellationToken))
                    .Result;
        }

        Assert.Equal(expected, drained.Count);
        Assert.All(
            drained.SelectMany(static row => row.Occurrences),
            occurrence =>
            {
                Assert.Equal(
                    HandleKind.MethodDefinition,
                    MetadataTokens.EntityHandle(
                        occurrence.DeclarationMetadataToken).Kind);
                AssertNamedType(
                    occurrence.Receiver,
                    "System.Net.Http",
                    "HttpClient");
            });
        Assert.Contains(
            drained.SelectMany(static row => row.Occurrences),
            static occurrence =>
                occurrence.Member.MemberName == "GetFromJsonAsync");

        var countOnly =
            Assert.IsType<
                MetadataExtensionRelationPopulationOutcome.Available>(
                    session.ExtensionRelations(
                        new(
                            receiver,
                            MetadataOperationPolicy.Unbounded,
                            count: new()),
                        TestContext.Current.CancellationToken));
        Assert.Equal(
            expected,
            Assert.IsType<
                MetadataExtensionRelationPopulationCountOutcome.Counted>(
                    countOnly.Result.Count).Value);
        Assert.Null(countOnly.Result.Rows);

        var stale =
            Assert.IsType<
                MetadataExtensionRelationPopulationOutcome.Available>(
                    session.ExtensionRelations(
                        new(
                            receiver,
                            MetadataOperationPolicy.Unbounded,
                            count: new(),
                            rows: new(0, 1),
                            expectedModuleVersionId: Guid.NewGuid()),
                        TestContext.Current.CancellationToken));
        _ = Assert.IsType<
            MetadataExtensionRelationPopulationCountOutcome.Failed>(
                stale.Result.Count);
        Assert.Equal(
            MetadataExtensionRelationPopulationRowsRejection.StaleSource,
            Assert.IsType<
                MetadataExtensionRelationPopulationRowsOutcome.Rejected>(
                    stale.Result.Rows).Reason);

        var limited =
            Assert.IsType<
                MetadataExtensionRelationPopulationOutcome.Available>(
                    session.ExtensionRelations(
                        new(
                            receiver,
                            new MetadataOperationPolicy(
                                maxMetadataRows: long.MaxValue,
                                maxRelationshipEdges: 0),
                            count: new(),
                            rows: new(0, 1)),
                        TestContext.Current.CancellationToken));
        Assert.Equal(
            MetadataRelationFamilyDisposition.Partial,
            limited.Result.Disposition);
        _ = Assert.IsType<
            MetadataExtensionRelationPopulationCountOutcome.Incomplete>(
                limited.Result.Count);
        _ = Assert.IsType<
            MetadataExtensionRelationPopulationRowsOutcome.Incomplete>(
                limited.Result.Rows);
        Assert.Equal(
            MetadataRelationDiagnosticKind.Limit,
            Assert.Single(limited.Result.Diagnostics).Kind);
    }

    [Fact]
    public void ExtensionPopulationPreservesConstructedReceiverContext()
    {
        string path =
            typeof(Inspector.Findings.FindingExtensions).Assembly.Location;
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);
        AssemblyReferenceIdentity receiverAssembly =
            Assert.Single(
                session.AssemblyReferenceIdentities(),
                static reference =>
                    reference.Name == "System.Runtime");
        var receiver = new MetadataExtensionReceiverSelection(
            receiverAssembly,
            TypeName("System.Collections.Generic", "IEnumerable`1"));

        var available =
            Assert.IsType<
                MetadataExtensionRelationPopulationOutcome.Available>(
                    session.ExtensionRelations(
                        new(
                            receiver,
                            MetadataOperationPolicy.Unbounded,
                            count: new(),
                            rows: new(0, 10)),
                        TestContext.Current.CancellationToken));
        MetadataExtensionRelationPopulationRow row =
            Assert.Single(
                Assert.IsType<
                    MetadataExtensionRelationPopulationRowsOutcome.Read>(
                        available.Result.Rows).Items);
        MetadataExtensionRelationEvidence evidence =
            Assert.Single(row.Occurrences);

        Assert.Equal("Keys<T>", evidence.Member.MemberName);
        var generic =
            Assert.IsType<MetadataTypeIdentity.GenericInstance>(
                evidence.Receiver);
        Assert.Equal(
            "IEnumerable`1",
            generic.Definition.Segments[0].ToString());
        Assert.Contains(
            generic.Arguments,
            static argument =>
                ContainsGenericParameter(argument));
        Assert.Equal(
            1,
            Assert.IsType<
                MetadataExtensionRelationPopulationCountOutcome.Counted>(
                    available.Result.Count).Value);
    }

    [Fact]
    public void ExtensionAndReferenceProducersRetainExactEvidence()
    {
        string path = typeof(MetadataFindings).Assembly.Location;
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);

        var available =
            Assert.IsType<MetadataRelationInspectionOutcome.Available>(
                session.Relations(
                    new(
                        [
                            MetadataRelationFamily.Extensions,
                            MetadataRelationFamily.AssemblyReferences,
                        ],
                        MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataRelationFamilyDisposition.Complete,
            available.Result.Extensions.Disposition);
        MetadataExtensionRelationEvidence method =
            Assert.Single(
            available.Result.Extensions.Evidence,
            static extension =>
                extension.Member.CanonicalSignature.StartsWith(
                    "M:",
                    StringComparison.Ordinal)
                && extension.Member.CanonicalSignature
                    == "M:ILInspector.Metadata.MetadataReaderExtensions.GetFullTypeName(System.Reflection.Metadata.MetadataReader,System.Reflection.Metadata.TypeDefinition)"
                && IsNamedType(
                    extension.Receiver,
                    "System.Reflection.Metadata",
                    "MetadataReader"));
        Assert.Equal(
            HandleKind.MethodDefinition,
            MetadataTokens.EntityHandle(
                method.DeclarationMetadataToken).Kind);

        MetadataExtensionRelationEvidence property =
            Assert.Single(
            available.Result.Extensions.Evidence,
            static extension =>
                extension.Member.CanonicalSignature.StartsWith(
                    "P:",
                    StringComparison.Ordinal)
                && extension.Member.CanonicalSignature.Contains(
                    "IsPublic",
                    StringComparison.Ordinal)
                && extension.Receiver
                    is MetadataTypeIdentity.Named);
        Assert.Equal(
            HandleKind.PropertyDefinition,
            MetadataTokens.EntityHandle(
                property.DeclarationMetadataToken).Kind);
        MetadataExtensionReceiverSelection propertyReceiver =
            ReceiverSelection(
                available.Result.Receipt.Assembly!,
                property.Receiver);
        var propertyPopulation =
            Assert.IsType<
                MetadataExtensionRelationPopulationOutcome.Available>(
                    session.ExtensionRelations(
                        new(
                            propertyReceiver,
                            MetadataOperationPolicy.Unbounded,
                            rows: new(0, 100)),
                        TestContext.Current.CancellationToken));
        Assert.Contains(
            Assert.IsType<
                MetadataExtensionRelationPopulationRowsOutcome.Read>(
                    propertyPopulation.Result.Rows)
                .Items.SelectMany(static row => row.Occurrences),
            occurrence =>
                occurrence.DeclarationMetadataToken
                    == property.DeclarationMetadataToken);

        Assert.Equal(
            MetadataRelationFamilyDisposition.Complete,
            available.Result.AssemblyReferences.Disposition);
        Assert.Contains(
            available.Result.AssemblyReferences.Evidence,
            static reference =>
                reference.Target.Name
                    == "System.Reflection.Metadata");
        Assert.All(
            available.Result.AssemblyReferences.Evidence,
            reference =>
                Assert.Equal(
                    available.Result.Receipt.Assembly,
                    reference.Source));
    }

    [Fact]
    public void HierarchyAndSignaturesPreserveConstructedShapesAndSites()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts",
            "System.Private.CoreLib.dll");
        MetadataTypeDefinitionAddress memoryStream =
            Address(path, "System.IO", "MemoryStream");
        MetadataTypeDefinitionAddress convert =
            Address(path, "System", "Convert");
        MetadataTypeDefinitionAddress memoryExtensions =
            Address(path, "System", "MemoryExtensions");
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);

        var available =
            Assert.IsType<MetadataRelationInspectionOutcome.Available>(
                session.Relations(
                    new(
                        [
                            MetadataRelationFamily.Hierarchy,
                            MetadataRelationFamily.Extensions,
                            MetadataRelationFamily.Signatures,
                        ],
                        MetadataOperationPolicy.Unbounded,
                        typeScope:
                        [
                            memoryStream,
                            convert,
                            memoryExtensions,
                        ]),
                    TestContext.Current.CancellationToken));

        MetadataHierarchyRelationEvidence baseType =
            Assert.Single(
                available.Result.Hierarchy.Evidence,
                relation =>
                    relation.Source == memoryStream
                    && relation.Kind
                        == MetadataHierarchyRelationKind.BaseType);
        AssertNamedType(
            baseType.Target,
            "System.IO",
            "Stream");
        Assert.Equal(
            HandleKind.TypeDefinition,
            MetadataTokens.EntityHandle(
                baseType.MetadataToken).Kind);

        MetadataSignatureRelationEvidence toHexString =
            Assert.Single(
                available.Result.Signatures.Evidence,
                relation =>
                    relation.Kind
                        == MetadataSignatureRelationKind.Accepts
                    && relation.ParameterIndex == 0
                    && relation.Member.MemberName == "ToHexString"
                    && IsConstructedType(
                        relation.Shape,
                        "System",
                        "ReadOnlySpan`1",
                        "byte"));
        Assert.Equal(convert, toHexString.DeclaringType);

        MetadataSignatureRelationEvidence asSpan =
            Assert.Single(
                available.Result.Signatures.Evidence,
                relation =>
                    relation.Kind
                        == MetadataSignatureRelationKind.Returns
                    && relation.Member.CanonicalSignature.Contains(
                        "AsSpan(System.String)",
                        StringComparison.Ordinal)
                    && IsConstructedType(
                        relation.Shape,
                        "System",
                        "ReadOnlySpan`1",
                        "char"));
        Assert.Equal(memoryExtensions, asSpan.DeclaringType);
        MetadataExtensionRelationEvidence asSpanExtension =
            Assert.Single(
                available.Result.Extensions.Evidence,
                relation =>
                    relation.Member.CanonicalSignature
                        == asSpan.Member.CanonicalSignature);
        Assert.Equal(asSpan.Member, asSpanExtension.Member);
        MetadataTypeDefinitionName stringType =
            TypeName("System", "String");
        var stringPopulation =
            Assert.IsType<
                MetadataExtensionRelationPopulationOutcome.Available>(
                    session.ExtensionRelations(
                        new(
                            new(
                                available.Result.Receipt.Assembly!,
                                stringType),
                            MetadataOperationPolicy.Unbounded,
                            count: new(),
                            rows: new(0, 1000)),
                        TestContext.Current.CancellationToken));
        int stringCount =
            Assert.IsType<
                MetadataExtensionRelationPopulationCountOutcome.Counted>(
                    stringPopulation.Result.Count).Value;
        var stringRows =
            Assert.IsType<
                MetadataExtensionRelationPopulationRowsOutcome.Read>(
                    stringPopulation.Result.Rows);
        Assert.Equal(stringCount, stringRows.Items.Length);
        Assert.Null(stringRows.NextOrdinal);
        Assert.Contains(
            stringRows.Items.SelectMany(static row => row.Occurrences),
            occurrence =>
                occurrence.Member.CanonicalSignature
                    == asSpan.Member.CanonicalSignature);
    }

    [Fact]
    public void HierarchyTargetSelectionProducesOnlyMatchingRelationForm()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts",
            "System.Private.CoreLib.dll");
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);

        var available =
            Assert.IsType<MetadataRelationInspectionOutcome.Available>(
                session.Relations(
                    new(
                        [MetadataRelationFamily.Hierarchy],
                        MetadataOperationPolicy.Unbounded,
                        hierarchyTarget: new(
                            TypeName("System.IO", "Stream"),
                            MetadataHierarchyRelationKind.BaseType)),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataRelationFamilyDisposition.Complete,
            available.Result.Hierarchy.Disposition);
        Assert.Equal(7, available.Result.Hierarchy.Evidence.Length);
        Assert.All(
            available.Result.Hierarchy.Evidence,
            relation =>
            {
                Assert.Equal(
                    MetadataHierarchyRelationKind.BaseType,
                    relation.Kind);
                AssertNamedType(
                    relation.Target,
                    "System.IO",
                    "Stream");
            });
    }

    [Fact]
    public void HierarchyTargetSelectionPreservesTypeScope()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts",
            "System.Private.CoreLib.dll");
        MetadataTypeDefinitionAddress memoryStream =
            Address(path, "System.IO", "MemoryStream");
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);

        var available =
            Assert.IsType<MetadataRelationInspectionOutcome.Available>(
                session.Relations(
                    new(
                        [MetadataRelationFamily.Hierarchy],
                        MetadataOperationPolicy.Unbounded,
                        typeScope: [memoryStream],
                        hierarchyTarget: new(
                            TypeName("System.IO", "Stream"),
                            MetadataHierarchyRelationKind.BaseType)),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataRelationFamilyDisposition.Complete,
            available.Result.Hierarchy.Disposition);
        MetadataHierarchyRelationEvidence relation =
            Assert.Single(available.Result.Hierarchy.Evidence);
        Assert.Equal(memoryStream, relation.Source);
        Assert.Equal(1, available.Result.Hierarchy.Coverage?.Considered);
        Assert.Equal(1, available.Result.Hierarchy.Coverage?.Examined);
    }

    [Fact]
    public void HierarchyTargetSelectionContainsProjectionBudgetFailure()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts",
            "System.Private.CoreLib.dll");
        MetadataTypeDefinitionAddress memoryStream =
            Address(path, "System.IO", "MemoryStream");
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);

        var available =
            Assert.IsType<MetadataRelationInspectionOutcome.Available>(
                session.Relations(
                    new(
                        [MetadataRelationFamily.Hierarchy],
                        new MetadataOperationPolicy(
                            maxMetadataRows: long.MaxValue,
                            maxStructuredNodes: 0),
                        typeScope: [memoryStream],
                        hierarchyTarget: new(
                            TypeName("System.IO", "Stream"),
                            MetadataHierarchyRelationKind.BaseType)),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataRelationFamilyDisposition.Partial,
            available.Result.Hierarchy.Disposition);
        Assert.Empty(available.Result.Hierarchy.Evidence);
        MetadataRelationDiagnostic diagnostic =
            Assert.Single(available.Result.Hierarchy.Diagnostics);
        Assert.Equal(
            MetadataRelationDiagnosticKind.Limit,
            diagnostic.Kind);
        Assert.Equal(
            MetadataOperationDimension.StructuredNodes,
            diagnostic.BudgetDimension);
        Assert.Equal(1, available.Result.Hierarchy.Coverage?.Considered);
        Assert.Equal(0, available.Result.Hierarchy.Coverage?.Examined);
        Assert.Equal(1, available.Result.Hierarchy.Coverage?.Limited);
    }

    [Theory]
    [InlineData(MetadataOperationDimension.StructuredNodes)]
    [InlineData(MetadataOperationDimension.RetainedText)]
    public void HierarchyAnalysisContainsProjectionBudgetFailure(
        MetadataOperationDimension dimension)
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts",
            "System.Private.CoreLib.dll");
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);
        var target = new MetadataHierarchyTargetSelection(
            TypeName("System.IO", "Stream"),
            MetadataHierarchyRelationKind.BaseType);
        var policy = new MetadataOperationPolicy(
            maxMetadataRows: long.MaxValue,
            maxStructuredNodes:
                dimension == MetadataOperationDimension.StructuredNodes
                    ? 0
                    : long.MaxValue,
            maxRetainedText:
                dimension == MetadataOperationDimension.RetainedText
                    ? 0
                    : long.MaxValue);

        var rows =
            Assert.IsType<
                MetadataHierarchyRelationAnalysisOutcome.Available>(
                session.AnalyzeHierarchyRelations(
                    new(target, policy),
                    TestContext.Current.CancellationToken));
        var count =
            Assert.IsType<
                MetadataHierarchyRelationAnalysisOutcome.Available>(
                session.AnalyzeHierarchyRelations(
                    new(
                        target,
                        policy,
                        materializeRows: false),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataRelationFamilyDisposition.Partial,
            rows.Result.Relations.Disposition);
        Assert.Empty(rows.Result.Relations.Evidence);
        MetadataRelationDiagnostic diagnostic =
            Assert.Single(rows.Result.Relations.Diagnostics);
        Assert.Equal(
            MetadataRelationDiagnosticKind.Limit,
            diagnostic.Kind);
        Assert.Equal(dimension, diagnostic.BudgetDimension);
        Assert.True(rows.Result.Relations.Coverage?.Limited > 0);

        Assert.Equal(
            MetadataRelationFamilyDisposition.Complete,
            count.Result.Relations.Disposition);
        Assert.True(count.Result.CandidateCount > 0);
        Assert.Empty(count.Result.Relations.Evidence);
        Assert.Equal(
            0,
            count.Result.Receipt.Counters.StructuredNodes);
        Assert.Equal(0, count.Result.Receipt.Counters.RetainedText);
    }

    [Fact]
    public void
        HierarchyTargetSelectionReusesChargedSourceNameAcrossOccurrences()
    {
        string path =
            FixtureCatalog.MetadataInterfaceImplFixtures.AssemblyPath();
        MetadataTypeDefinitionAddress source =
            Address(
                path,
                "ILInspector.Metadata.InterfaceImplFixtures",
                "MultipleConstructedImplementation");
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);
        var target = new MetadataHierarchyTargetSelection(
            TypeName(
                "ILInspector.Metadata.InterfaceImplContracts",
                "IConstructed`1"),
            MetadataHierarchyRelationKind.Interface);

        var unbounded =
            Assert.IsType<MetadataRelationInspectionOutcome.Available>(
                session.Relations(
                    new(
                        [MetadataRelationFamily.Hierarchy],
                        MetadataOperationPolicy.Unbounded,
                        typeScope: [source],
                        hierarchyTarget: target),
                    TestContext.Current.CancellationToken));
        long retainedText =
            unbounded.Result.Receipt.Counters.RetainedText;
        Assert.True(retainedText > 0);
        Assert.Equal(
            MetadataRelationFamilyDisposition.Complete,
            unbounded.Result.Hierarchy.Disposition);
        Assert.Equal(2, unbounded.Result.Hierarchy.Evidence.Length);
        Assert.Same(
            unbounded.Result.Hierarchy.Evidence[0].SourceType,
            unbounded.Result.Hierarchy.Evidence[1].SourceType);

        var bounded =
            Assert.IsType<MetadataRelationInspectionOutcome.Available>(
                session.Relations(
                    new(
                        [MetadataRelationFamily.Hierarchy],
                        new MetadataOperationPolicy(
                            maxMetadataRows: long.MaxValue,
                            maxRetainedText: retainedText),
                        typeScope: [source],
                        hierarchyTarget: target),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataRelationFamilyDisposition.Complete,
            bounded.Result.Hierarchy.Disposition);
        Assert.Equal(2, bounded.Result.Hierarchy.Evidence.Length);
        Assert.Same(
            bounded.Result.Hierarchy.Evidence[0].SourceType,
            bounded.Result.Hierarchy.Evidence[1].SourceType);
        Assert.Equal(
            retainedText,
            bounded.Result.Receipt.Counters.RetainedText);
    }

    [Fact]
    public void HierarchyTargetSelectionRetainsMatchingGenericTypeSpecifications()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts",
            "System.Private.CoreLib.dll");
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);

        var available =
            Assert.IsType<MetadataRelationInspectionOutcome.Available>(
                session.Relations(
                    new(
                        [MetadataRelationFamily.Hierarchy],
                        MetadataOperationPolicy.Unbounded,
                        hierarchyTarget: new(
                            TypeName(
                                "System.Collections.Generic",
                                "IEnumerable`1"),
                            MetadataHierarchyRelationKind.Interface)),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataRelationFamilyDisposition.Complete,
            available.Result.Hierarchy.Disposition);
        Assert.NotEmpty(available.Result.Hierarchy.Evidence);
        Assert.All(
            available.Result.Hierarchy.Evidence,
            relation =>
            {
                Assert.Equal(
                    MetadataHierarchyRelationKind.Interface,
                    relation.Kind);
                var target =
                    Assert.IsType<MetadataTypeIdentity.GenericInstance>(
                        relation.Target);
                Assert.Equal(
                    "System.Collections.Generic",
                    target.Definition.Namespace.ToString());
                Assert.Equal(
                    ["IEnumerable`1"],
                    target.Definition.Segments.Select(
                        static segment => segment.ToString()));
            });
    }

    [Fact]
    public void HierarchyAnalysisMatchesRoslynGenericRelationEvidence()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts",
            "System.Private.CoreLib.dll");
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);
        var target = new MetadataHierarchyTargetSelection(
            TypeName(
                "System.Collections.Generic",
                "IEnumerable`1"),
            MetadataHierarchyRelationKind.Interface);

        var analysis =
            Assert.IsType<
                MetadataHierarchyRelationAnalysisOutcome.Available>(
                session.AnalyzeHierarchyRelations(
                    new(
                        target,
                        MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));
        var countOnly =
            Assert.IsType<
                MetadataHierarchyRelationAnalysisOutcome.Available>(
                session.AnalyzeHierarchyRelations(
                    new(
                        target,
                        MetadataOperationPolicy.Unbounded,
                        materializeRows: false),
                    TestContext.Current.CancellationToken));
        var relations =
            Assert.IsType<MetadataRelationInspectionOutcome.Available>(
                session.Relations(
                    new(
                        [MetadataRelationFamily.Hierarchy],
                        MetadataOperationPolicy.Unbounded,
                        hierarchyTarget: target),
                    TestContext.Current.CancellationToken));
        using var pass =
            new MetadataHierarchyRelationAnalysisPass(
                session,
                new(
                    target,
                    MetadataOperationPolicy.Unbounded));
        var passRows =
            ImmutableArray.CreateBuilder<
                MetadataHierarchyRelationAnalysisRow>();
        int passCandidateCount = 0;
        foreach (TypeDefinitionHandle handle in pass.TypeDefinitions)
        {
            MetadataHierarchyRelationAnalysisUnit unit =
                pass.Analyze(handle);
            Assert.Null(unit.Diagnostic);
            passCandidateCount += unit.CandidateCount;
            if (unit.BaseRelation is { } baseRelation)
                passRows.Add(baseRelation);
            if (unit.InterfaceRelation is { } interfaceRelation)
                passRows.Add(interfaceRelation);
        }

        Assert.Equal(
            MetadataRelationFamilyDisposition.Complete,
            analysis.Result.Relations.Disposition);
        int expectedCandidates =
            relations.Result.Hierarchy.Evidence
                .Select(static row => row.Source)
                .Distinct()
                .Count();
        Assert.Equal(
            expectedCandidates,
            analysis.Result.CandidateCount);
        Assert.Equal(
            expectedCandidates,
            analysis.Result.Relations.Evidence.Length);
        Assert.Equal(
            expectedCandidates,
            countOnly.Result.CandidateCount);
        Assert.Equal(expectedCandidates, passCandidateCount);
        Assert.Empty(countOnly.Result.Relations.Evidence);
        Assert.Equal(
            analysis.Result.Receipt.Counters,
            pass.Counters);
        Assert.Equal(
            analysis.Result.Relations.Evidence
                .SelectMany(static row =>
                    row.MetadataTokens.Select(token => (
                        row.Source,
                        row.SourceType,
                        row.Kind,
                        Token: token))),
            passRows
                .SelectMany(static row =>
                    row.MetadataTokens.Select(token => (
                        row.Source,
                        row.SourceType,
                        row.Kind,
                        Token: token))));
        Assert.Equal(
            relations.Result.Hierarchy.Evidence
                .Select(static row => (
                    row.Source,
                    row.SourceType,
                    row.Kind,
                    row.MetadataToken)),
            analysis.Result.Relations.Evidence
                .SelectMany(static row =>
                    row.MetadataTokens.Select(token => (
                        row.Source,
                        row.SourceType,
                        row.Kind,
                        MetadataToken: token))));
    }

    [Fact]
    public void HierarchyAnalysisForwardPlanStopsOnlyAfterItsBound()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts",
            "System.Private.CoreLib.dll");
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);
        var target = new MetadataHierarchyTargetSelection(
            TypeName(
                "System.Collections.Generic",
                "IEnumerable`1"),
            MetadataHierarchyRelationKind.Interface);
        var complete =
            Assert.IsType<
                MetadataHierarchyRelationAnalysisOutcome.Available>(
                session.AnalyzeHierarchyRelations(
                    new(
                        target,
                        MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));
        var forwardPlan =
            new MetadataHierarchyRelationForwardPlan(
                maximumCandidates: 6);
        var exactTarget = new MetadataHierarchyTargetSelection(
            target.Type,
            target.Kind,
            complete.Result.Receipt.Assembly);
        var forward =
            Assert.IsType<
                MetadataHierarchyRelationAnalysisOutcome.Available>(
                session.AnalyzeHierarchyRelations(
                    new(
                        exactTarget,
                        MetadataOperationPolicy.Unbounded,
                        forwardPlan: forwardPlan),
                    TestContext.Current.CancellationToken));
        var unrelatedAssembly =
            Assert.IsType<
                MetadataHierarchyRelationAnalysisOutcome.Available>(
                    session.AnalyzeHierarchyRelations(
                        new(
                            new(
                                target.Type,
                                target.Kind,
                                new(
                                    "Unrelated",
                                    new Version(1, 0, 0, 0),
                                    Culture: null,
                                    PublicKeyToken: null)),
                            MetadataOperationPolicy.Unbounded,
                            forwardPlan:
                                new(
                                    maximumCandidates: 1)),
                        TestContext.Current.CancellationToken));
        var noFinding =
            Assert.IsType<
                MetadataHierarchyRelationAnalysisOutcome.Available>(
                session.AnalyzeHierarchyRelations(
                    new(
                        new(
                            TypeName("Missing", "INotPresent"),
                            MetadataHierarchyRelationKind.Interface),
                        MetadataOperationPolicy.Unbounded,
                        forwardPlan:
                            new(
                                maximumCandidates: 1)),
                    TestContext.Current.CancellationToken));

        Assert.True(complete.Result.CandidateCount > 6);
        Assert.False(complete.Result.WasStopped);
        Assert.Null(complete.Result.ForwardPlan);
        Assert.True(forward.Result.WasStopped);
        Assert.Same(forwardPlan, forward.Result.ForwardPlan);
        Assert.Equal(6, forward.Result.CandidateCount);
        Assert.Equal(
            MetadataRelationFamilyDisposition.Partial,
            forward.Result.Relations.Disposition);
        Assert.Equal(
            complete.Result.Relations.Evidence
                .Take(6)
                .Select(static row => row.Source),
            forward.Result.Relations.Evidence
                .Select(static row => row.Source));
        Assert.True(
            forward.Result.Receipt.Counters.DeclarationCandidates
            < complete.Result.Receipt.Counters.DeclarationCandidates);
        Assert.False(unrelatedAssembly.Result.WasStopped);
        Assert.Equal(
            complete.Result.CandidateCount,
            unrelatedAssembly.Result.CandidateCount);

        Assert.False(noFinding.Result.WasStopped);
        Assert.Equal(0, noFinding.Result.CandidateCount);
        Assert.Equal(
            MetadataRelationFamilyDisposition.Complete,
            noFinding.Result.Relations.Disposition);
    }

    [Fact]
    public void HierarchyTargetSelectionReportsMalformedGenericTypeSpecifications()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(
                    HierarchyRelationSafetyFixtures
                        .BuildMalformedGenericTypeSpecification(),
                    writable: false));

        var available =
            Assert.IsType<MetadataRelationInspectionOutcome.Available>(
                session.Relations(
                    new(
                        [MetadataRelationFamily.Hierarchy],
                        MetadataOperationPolicy.Unbounded,
                        hierarchyTarget: new(
                            TypeName("Sample", "ITarget`1"),
                            MetadataHierarchyRelationKind.Interface)),
                    TestContext.Current.CancellationToken));

        Assert.Empty(available.Result.Hierarchy.Evidence);
        MetadataRelationDiagnostic diagnostic =
            Assert.Single(available.Result.Hierarchy.Diagnostics);
        Assert.Equal(
            MetadataRelationDiagnosticKind.UnsupportedShape,
            diagnostic.Kind);
        Assert.NotEmpty(diagnostic.Detail);
    }

    [Fact]
    public void HierarchyAnalysisContainsMalformedGenericTypeSpecifications()
    {
        byte[] content =
            HierarchyRelationSafetyFixtures
                .BuildMalformedGenericTypeSpecification();
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(
                    content,
                    writable: false));

        var available =
            Assert.IsType<
                MetadataHierarchyRelationAnalysisOutcome.Available>(
                session.AnalyzeHierarchyRelations(
                    new(
                        new(
                            TypeName("Sample", "ITarget`1"),
                            MetadataHierarchyRelationKind.Interface),
                        MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataRelationFamilyDisposition.Partial,
            available.Result.Relations.Disposition);
        Assert.Empty(available.Result.Relations.Evidence);
        MetadataRelationDiagnostic diagnostic =
            Assert.Single(available.Result.Relations.Diagnostics);
        Assert.Equal(
            MetadataRelationDiagnosticKind.UnsupportedShape,
            diagnostic.Kind);
        Assert.NotEmpty(diagnostic.Detail);

        using var pass =
            new MetadataHierarchyRelationAnalysisPass(
                session,
                new(
                    new(
                        TypeName("Sample", "ITarget`1"),
                        MetadataHierarchyRelationKind.Interface),
                    MetadataOperationPolicy.Unbounded));
        MetadataHierarchyRelationAnalysisUnit unit =
            Assert.Single(
                pass.TypeDefinitions
                    .Select(pass.Analyze),
                static candidate =>
                    candidate.Diagnostic is not null);
        Assert.Equal(
            diagnostic.Kind,
            unit.Diagnostic!.Kind);
        Assert.Equal(
            available.Result.Receipt.Counters,
            pass.Counters);
    }

    [Fact]
    public void
        HierarchyTargetSelectionContainsMalformedGenericContextAndPreservesOtherFamilies()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(
                    HierarchyRelationSafetyFixtures
                        .BuildMalformedGenericContextHierarchy(),
                    writable: false));

        var available =
            Assert.IsType<MetadataRelationInspectionOutcome.Available>(
                session.Relations(
                    new(
                        [
                            MetadataRelationFamily.Hierarchy,
                            MetadataRelationFamily.AssemblyReferences,
                        ],
                        MetadataOperationPolicy.Unbounded,
                        hierarchyTarget: new(
                            TypeName("Sample", "Target"),
                            MetadataHierarchyRelationKind.BaseType)),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataRelationFamilyDisposition.Partial,
            available.Result.Hierarchy.Disposition);
        Assert.Empty(available.Result.Hierarchy.Evidence);
        MetadataRelationDiagnostic diagnostic =
            Assert.Single(available.Result.Hierarchy.Diagnostics);
        Assert.Equal(
            MetadataRelationDiagnosticKind.MalformedMetadata,
            diagnostic.Kind);
        Assert.Equal(1, available.Result.Hierarchy.Coverage?.Unavailable);

        Assert.Equal(
            MetadataRelationFamilyDisposition.Complete,
            available.Result.AssemblyReferences.Disposition);
        MetadataAssemblyReferenceRelationEvidence reference =
            Assert.Single(
                available.Result.AssemblyReferences.Evidence);
        Assert.Equal("Dependency", reference.Target.Name);
    }

    [Fact]
    public void HierarchyAnalysisRejectsCyclicVisibilityBeforeCandidateScan()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(
                    HierarchyRelationSafetyFixtures
                        .BuildCyclicNestedTypeVisibility(128),
                    writable: false));

        var available =
            Assert.IsType<
                MetadataHierarchyRelationAnalysisOutcome.Available>(
                session.AnalyzeHierarchyRelations(
                    new(
                        new(
                            TypeName("Sample", "ITarget"),
                            MetadataHierarchyRelationKind.BaseType),
                        MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataRelationFamilyDisposition.Partial,
            available.Result.Relations.Disposition);
        Assert.Empty(available.Result.Relations.Evidence);
        MetadataRelationDiagnostic diagnostic =
            Assert.Single(available.Result.Relations.Diagnostics);
        Assert.Equal(
            MetadataRelationDiagnosticKind.MalformedMetadata,
            diagnostic.Kind);
        Assert.Contains(
            "cycle",
            diagnostic.Detail,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            3,
            available.Result.Receipt.Counters.DeclarationCandidates);
        Assert.Equal(
            1,
            available.Result.Receipt.Counters.RelationshipEdges);
    }

    [Fact]
    public void SimpleTypeNameSearchMaterializesOnlyMatchingDefinitions()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts",
            "System.Private.CoreLib.dll");
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);

        var found =
            Assert.IsType<MetadataTypeDefinitionNameSearchResult.Found>(
                session.FindTypeDefinitionsBySimpleName("stream"));

        Assert.Contains(
            found.Names,
            name => name.ToMetadataFullName() == "System.IO.Stream");
        Assert.All(
            found.Names,
            name => Assert.Equal(
                "Stream",
                name.Segments[^1],
                ignoreCase: true));
    }

    [Fact]
    public void ProducerLimitReturnsPartialInsteadOfExactEmpty()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts",
            "System.Private.CoreLib.dll");
        MetadataTypeDefinitionAddress memoryStream =
            Address(path, "System.IO", "MemoryStream");
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);

        var available =
            Assert.IsType<MetadataRelationInspectionOutcome.Available>(
                session.Relations(
                    new(
                        [MetadataRelationFamily.Hierarchy],
                        new MetadataOperationPolicy(
                            maxMetadataRows: long.MaxValue,
                            maxRelationshipEdges: 0),
                        typeScope: [memoryStream]),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataRelationFamilyDisposition.Partial,
            available.Result.Hierarchy.Disposition);
        Assert.Empty(available.Result.Hierarchy.Evidence);
        MetadataRelationDiagnostic diagnostic =
            Assert.Single(available.Result.Hierarchy.Diagnostics);
        Assert.Equal(
            MetadataRelationDiagnosticKind.Limit,
            diagnostic.Kind);
        Assert.Equal(
            MetadataOperationDimension.RelationshipEdges,
            diagnostic.BudgetDimension);
        Assert.Equal(1, available.Result.Hierarchy.Coverage?.Considered);
        Assert.Equal(1, available.Result.Hierarchy.Coverage?.Limited);
    }

    [Fact]
    public void TypeScopeMustBelongToExactInspectedImage()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts",
            "System.Private.CoreLib.dll");
        MetadataTypeDefinitionAddress memoryStream =
            Address(path, "System.IO", "MemoryStream");
        var foreign = new MetadataTypeDefinitionAddress(
            Guid.NewGuid(),
            memoryStream.Definition);
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);
        var request = new MetadataRelationInspectionRequest(
            [MetadataRelationFamily.Hierarchy],
            MetadataOperationPolicy.Unbounded,
            typeScope: [foreign]);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => session.Relations(
                request,
                TestContext.Current.CancellationToken));

        Assert.Equal("request", exception.ParamName);
    }

    [Fact]
    public void MalformedModuleMvidSettlesRequestedFamiliesAsFailed()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(
                    BuildInvalidModuleMvidImage(),
                    writable: false));

        var available =
            Assert.IsType<MetadataRelationInspectionOutcome.Available>(
                session.Relations(
                    new(
                        [
                            MetadataRelationFamily.Hierarchy,
                            MetadataRelationFamily.Signatures,
                        ],
                        MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));

        Assert.Null(available.Result.Receipt.ModuleVersionId);
        Assert.NotNull(available.Result.Receipt.Assembly);
        Assert.Equal(
            MetadataRelationFamilyDisposition.Failed,
            available.Result.Hierarchy.Disposition);
        Assert.Equal(
            MetadataRelationFamilyDisposition.Failed,
            available.Result.Signatures.Disposition);
        Assert.Empty(available.Result.Hierarchy.Evidence);
        Assert.Empty(available.Result.Signatures.Evidence);
        Assert.All(
            available.Result.Hierarchy.Diagnostics
                .Concat(available.Result.Signatures.Diagnostics),
            diagnostic => Assert.Equal(
                MetadataRelationDiagnosticKind.MalformedMetadata,
                diagnostic.Kind));
    }

    private static MetadataTypeDefinitionAddress Address(
        string path,
        string @namespace,
        string name)
    {
        using var stream = File.OpenRead(path);
        using var image =
            new PEReader(
                stream,
                PEStreamOptions.PrefetchMetadata);
        MetadataReader reader = image.GetMetadataReader();
        foreach (TypeDefinitionHandle handle
            in reader.TypeDefinitions)
        {
            TypeDefinition definition =
                reader.GetTypeDefinition(handle);
            if (reader.StringComparer.Equals(
                    definition.Namespace,
                    @namespace)
                && reader.StringComparer.Equals(
                    definition.Name,
                    name))
            {
                return MetadataTypeDefinitionAddress.FromHandle(
                    reader,
                    handle);
            }
        }

        throw new InvalidOperationException(
            $"Type '{@namespace}.{name}' was not found.");
    }

    private static MetadataTypeDefinitionName TypeName(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                @namespace,
                [.. segments])).Name;

    private static MetadataExtensionReceiverSelection ReceiverSelection(
        AssemblyReferenceIdentity source,
        MetadataTypeIdentity receiver)
    {
        MetadataNamedTypeIdentity named = receiver switch
        {
            MetadataTypeIdentity.Named value => value.Definition,
            MetadataTypeIdentity.GenericInstance value =>
                value.Definition,
            _ => throw new Xunit.Sdk.XunitException(
                "Expected a named extension receiver."),
        };
        AssemblyReferenceIdentity assembly =
            named.Scope.Kind switch
            {
                MetadataTypeScopeKind.CurrentModule
                    or MetadataTypeScopeKind.ModuleReference =>
                    source,
                MetadataTypeScopeKind.AssemblyReference
                    when named.Scope.Assembly is { } reference =>
                    new(
                        reference.Name.ToString(),
                        reference.Version,
                        reference.Culture?.ToString(),
                        reference.PublicKeyToken?.ToString()),
                _ => throw new Xunit.Sdk.XunitException(
                    "Expected a resolvable extension receiver scope."),
            };
        return new(
            assembly,
            TypeName(
                named.Namespace.ToString(),
                [.. named.Segments.Select(static segment =>
                    segment.ToString())]));
    }

    private static void AssertNamedType(
        MetadataTypeIdentity identity,
        string @namespace,
        string name)
    {
        var named =
            Assert.IsType<MetadataTypeIdentity.Named>(identity);
        Assert.Equal(
            @namespace,
            named.Definition.Namespace.ToString());
        Assert.Equal(
            name,
            Assert.Single(named.Definition.Segments).ToString());
    }

    private static bool IsConstructedType(
        MetadataTypeIdentity identity,
        string @namespace,
        string name,
        string argumentName) =>
        identity
            is MetadataTypeIdentity.GenericInstance
        {
            Definition: var definition,
            Arguments: [MetadataTypeIdentity.Primitive primitive],
        }
        && definition.Namespace.ToString() == @namespace
        && definition.Segments.Length == 1
        && definition.Segments[0].ToString() == name
        && primitive.Name.ToString() == argumentName;

    private static bool IsNamedType(
        MetadataTypeIdentity identity,
        string @namespace,
        string name) =>
        identity is MetadataTypeIdentity.Named named
        && named.Definition.Namespace.ToString() == @namespace
        && named.Definition.Segments.Length == 1
        && named.Definition.Segments[0].ToString() == name;

    private static bool ContainsGenericParameter(
        MetadataTypeIdentity identity) =>
        identity switch
        {
            MetadataTypeIdentity.GenericParameter => true,
            MetadataTypeIdentity.GenericInstance generic =>
                generic.Arguments.Any(ContainsGenericParameter),
            MetadataTypeIdentity.SzArray array =>
                ContainsGenericParameter(array.Element),
            MetadataTypeIdentity.Array array =>
                ContainsGenericParameter(array.Element),
            MetadataTypeIdentity.Pointer pointer =>
                ContainsGenericParameter(pointer.Element),
            MetadataTypeIdentity.ByReference byReference =>
                ContainsGenericParameter(byReference.Element),
            MetadataTypeIdentity.Modified modified =>
                ContainsGenericParameter(modified.Modifier)
                || ContainsGenericParameter(modified.Type),
            MetadataTypeIdentity.Pinned pinned =>
                ContainsGenericParameter(pinned.Type),
            _ => false,
        };

    private static byte[] BuildInvalidModuleMvidImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("InvalidMvid.dll"),
            MetadataTokens.GuidHandle(100),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("InvalidMvid"),
            new Version(1, 0),
            default,
            default,
            default,
            AssemblyHashAlgorithm.None);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var image = new BlobBuilder();
        new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly)
            .Serialize(image);
        return image.ToArray();
    }

    private static byte[] BuildDuplicateAssemblyReferenceImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("ReferencePopulation.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("ReferencePopulation"),
            new Version(1, 0),
            default,
            default,
            default,
            AssemblyHashAlgorithm.None);
        metadata.AddAssemblyReference(
            metadata.GetOrAddString("Sample.First"),
            new Version(1, 0),
            default,
            default,
            default,
            default);
        metadata.AddAssemblyReference(
            metadata.GetOrAddString("sample.first"),
            new Version(1, 0),
            default,
            default,
            default,
            default);
        metadata.AddAssemblyReference(
            metadata.GetOrAddString("Sample.Second"),
            new Version(1, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var image = new BlobBuilder();
        new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly)
            .Serialize(image);
        return image.ToArray();
    }
}
