using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;

using DotnetInspector.Fixtures;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextCallCensusQueryTests
{
    static string IndirectPath =>
        FixtureCatalog.AnalysisCallerGraphIndirectCaller.AssemblyPath();
    static string CallerPath =>
        FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath();
    static string TargetPath =>
        FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
    static string TargetV2Path =>
        FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath();

    [Fact]
    public async Task ExecutePublishesOneCanonicalMultiParticipantCensus()
    {
        await using CensusContext forward =
            CensusContext.Create(
                IndirectPath,
                CallerPath,
                TargetPath);
        await using CensusContext reverse =
            CensusContext.Create(
                TargetPath,
                CallerPath,
                IndirectPath);

        AssemblyContextCallCensusResult first =
            AssemblyContextCallCensusQuery.Execute(
                forward.Group,
                TestContext.Current.CancellationToken);
        AssemblyContextCallCensusResult second =
            AssemblyContextCallCensusQuery.Execute(
                reverse.Group,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            first.Subjects.Select(subject => subject.Identity.Name),
            second.Subjects.Select(subject => subject.Identity.Name));
        Assert.Equal(
            first.Participants.Select(
                participant => participant.Subject.Identity.Name),
            second.Participants.Select(
                participant => participant.Subject.Identity.Name));
        Assert.Equal(
            first.Members.Select(MemberFingerprint),
            second.Members.Select(MemberFingerprint));
        Assert.Equal(
            first.Occurrences.Select(OccurrenceFingerprint),
            second.Occurrences.Select(OccurrenceFingerprint));
        Assert.Equal(3, first.Participants.Length);
        Assert.Equal(
            first.Participants.Length,
            first.Receipt!.ParticipantCount);
        Assert.Equal(first.Members.Length, first.Receipt.MemberCount);
        Assert.Equal(
            first.Occurrences.Length,
            first.Receipt.OccurrenceCount);
        Assert.Equal(
            first.UnresolvedOccurrences.Length,
            first.Receipt.UnresolvedOccurrenceCount);
        Assert.Equal(
            first.VersionSkewedBindings.Length,
            first.Receipt.VersionSkewedBindingCount);
        Assert.Contains(
            first.Occurrences,
            occurrence =>
                occurrence.SourceMethod.Name == "Run"
                && occurrence.Source.Identity.Name.Contains(
                    "IndirectCaller",
                    StringComparison.Ordinal)
                && occurrence.TargetMethod.Name == "Run"
                && occurrence.Target.Identity.Name.Contains(
                    "Caller",
                    StringComparison.Ordinal));
        Assert.Equal(
            2,
            first.Occurrences.Count(occurrence =>
                occurrence.SourceMethod.Name == "RunTwice"
                && occurrence.TargetMethod.Name == "Echo"));
        AssemblyContextCallCensusOccurrence generated = Assert.Single(
            first.Occurrences,
            occurrence =>
                occurrence.SourceMethod.Name == "AsyncRoot"
                && occurrence.TargetMethod.Name == "AsyncUse");
        Assert.NotEqual(
            generated.SourceMethod.MetadataToken,
            generated.Call.EvidenceMethod.MetadataToken);
        Assert.Equal(
            generated.Call.EvidenceMethod.MetadataToken,
            generated.OrderingKey.EvidenceMethod.MetadataToken);
    }

    [Fact]
    public async Task ExecuteRetainsPositiveEvidenceWhenOneParticipantFails()
    {
        ResolvedAssemblyReference caller =
            ResolvedAssemblyReference.CreateFromPath(
                CallerPath,
                AssemblyResolutionProvenance.Local(
                    "call-census test"));
        ResolvedAssemblyReference target =
            ResolvedAssemblyReference.CreateFromPath(
                TargetPath,
                AssemblyResolutionProvenance.Local(
                    "call-census test"));
        ResolvedAssemblyReference malformed =
            ResolvedAssemblyReference.Create(
                target.Identity,
                path: null,
                () => new MemoryStream([0x00, 0x01, 0x02]),
                AssemblyResolutionProvenance.Local(
                    "malformed call-census test"));
        await using CensusContext context =
            CensusContext.Create(caller, malformed);

        AssemblyContextCallCensusResult result =
            AssemblyContextCallCensusQuery.Execute(
                context.Group,
                TestContext.Current.CancellationToken);

        Assert.False(result.IsComplete);
        Assert.Equal(2, result.Subjects.Length);
        Assert.Single(result.Participants);
        Assert.IsType<AssemblyContextCallCensusFailure.Rejected>(
            Assert.Single(result.Failures));
        Assert.NotNull(result.Receipt);
        Assert.Contains(
            result.Occurrences,
            occurrence =>
                occurrence.SourceMethod.Name == "RunOuter"
                && occurrence.TargetMethod.Name == "Run");
        Assert.Contains(
            result.UnresolvedOccurrences,
            occurrence =>
                occurrence.SourceMethod.Name == "Run"
                && occurrence.Call.Callee.Name == "Ping");
        Assert.True(result.Diagnostics.IsIncomplete);
    }

    [Fact]
    public async Task ExecuteRejectsDuplicatePhysicalArtifacts()
    {
        await using CensusContext context =
            CensusContext.Create(TargetPath, TargetPath);

        AssemblyContextCallCensusRequestException exception =
            Assert.Throws<AssemblyContextCallCensusRequestException>(
                () => AssemblyContextCallCensusQuery.Execute(
                    context.Group,
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            "distinct physical assembly artifacts",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecutePreservesVersionSkewEvidenceAcrossInputOrder()
    {
        await using CensusContext forward =
            CensusContext.Create(
                CallerPath,
                TargetPath,
                TargetV2Path);
        await using CensusContext reverse =
            CensusContext.Create(
                TargetV2Path,
                TargetPath,
                CallerPath);

        AssemblyContextCallCensusResult first =
            AssemblyContextCallCensusQuery.Execute(
                forward.Group,
                TestContext.Current.CancellationToken);
        AssemblyContextCallCensusResult second =
            AssemblyContextCallCensusQuery.Execute(
                reverse.Group,
                TestContext.Current.CancellationToken);

        Assert.NotEmpty(first.VersionSkewedBindings);
        Assert.Equal(
            first.VersionSkewedBindings.Select(
                VersionSkewFingerprint),
            second.VersionSkewedBindings.Select(
                VersionSkewFingerprint));
        Assert.Equal(
            first.VersionSkewedBindings.Length,
            first.Diagnostics.VersionSkewedBindingCount);
        Assert.Contains(
            first.Occurrences,
            occurrence =>
                occurrence.SourceMethod.Name == "Run"
                && occurrence.Target.Identity.Version
                    == new Version(1, 0, 0, 0));
    }

    [Fact]
    public async Task ExecutePreservesExactParticipantPolicySelection()
    {
        (string directory, ResolvedAssemblyReference caller,
            ResolvedAssemblyReference selected,
            ResolvedAssemblyReference shadow) =
                BuildSameIdentityCallFixture();
        try
        {
            var policy = new SelectedAssemblyPolicy(
                [caller, selected, shadow],
                selected);
            await using CensusContext context =
                CensusContext.Create(
                    policy,
                    caller,
                    selected,
                    shadow);

            AssemblyContextCallCensusResult result =
                AssemblyContextCallCensusQuery.Execute(
                    context.Group,
                    TestContext.Current.CancellationToken);

            AssemblyContextCallCensusOccurrence occurrence =
                Assert.Single(
                    result.Occurrences,
                    occurrence =>
                        occurrence.SourceMethod.Name == "Run"
                        && occurrence.TargetMethod.Name == "Ping");
            Assert.Same(
                selected.Registration,
                occurrence.Target.Registration);
            Assert.DoesNotContain(
                result.UnresolvedOccurrences,
                occurrence =>
                    occurrence.SourceMethod.Name == "Run"
                    && occurrence.Call.Callee.Name == "Ping");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TypeCallCompatibilityPreservesExactParticipantPolicySelection()
    {
        (string directory, ResolvedAssemblyReference caller,
            ResolvedAssemblyReference selected,
            ResolvedAssemblyReference shadow) =
                BuildSameIdentityCallFixture();
        try
        {
            var policy = new SelectedAssemblyPolicy(
                [caller, selected, shadow],
                selected);
            await using CensusContext context =
                CensusContext.Create(
                    policy,
                    caller,
                    selected,
                    shadow);

            AssemblyContextTypeCallCompatibilityResult result =
                AssemblyContextTypeCallCompatibilityQuery.Execute(
                    context.Group,
                    caller.Registration,
                    Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                        MetadataTypeDefinitionName.Create(
                            "Consumer",
                            ["Entry"])).Name,
                    TestContext.Current.CancellationToken);

            Assert.True(
                result.TopologyComplete,
                string.Join(
                    Environment.NewLine,
                    result.UnresolvedOccurrences.Select(occurrence =>
                        occurrence.Limitation)));
            TypeCallCompatibilityOccurrence occurrence =
                Assert.Single(
                    result.Occurrences,
                    occurrence =>
                        occurrence.SourceMethod.Name == "Run"
                        && occurrence.TargetMethod.Name == "Ping");
            Assert.Same(
                selected.Registration,
                occurrence.Target.Registration);
            Assert.Empty(result.UnresolvedOccurrences);
            TypeCallCompatibilityAcquisitionReceipt receipt =
                Assert.IsType<TypeCallCompatibilityAcquisitionReceipt>(
                    result.Receipt);
            Assert.All(
                receipt.TargetSources,
                targetReceipt =>
                    Assert.Equal(
                        0,
                        targetReceipt.Receipt.BodiesAcquired));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TypeCallCompatibilityPublishesExactExternalColorAndTerminals()
    {
        (string directory, ResolvedAssemblyReference caller,
            ResolvedAssemblyReference target) =
                BuildCompatibilityCallFixture();
        try
        {
            await using CensusContext context =
                CensusContext.Create(caller, target);
            MetadataTypeDefinitionName focusType =
                Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create(
                        "Consumer",
                        ["Entry"])).Name;

            AssemblyContextTypeCallCompatibilityResult result =
                AssemblyContextTypeCallCompatibilityQuery.Execute(
                    context.Group,
                    caller.Registration,
                    focusType,
                    TestContext.Current.CancellationToken);

            Assert.True(
                result.TopologyComplete,
                string.Join(
                    Environment.NewLine,
                    result.UnresolvedOccurrences.Select(occurrence =>
                        occurrence.Limitation)));
            Assert.Empty(result.UnresolvedOccurrences);
            Assert.Equal(4, result.Occurrences.Length);
            Assert.Equal(3, result.Targets.Length);
            TypeCallCompatibilityAcquisitionReceipt receipt =
                Assert.IsType<TypeCallCompatibilityAcquisitionReceipt>(
                    result.Receipt);
            Assert.Equal(
                MethodDefinitionSourceBreadthKind.ExactTypes,
                receipt.FocusSource.Breadth.Kind);
            Assert.Equal(2, receipt.FocusSource.DefinitionsVisited);
            Assert.Equal(2, receipt.FocusSource.BodiesAcquired);
            Assert.Equal(4, receipt.DistinctCallees);
            Assert.Equal(3, receipt.TargetMethodsInspected);
            Assert.Equal(
                3,
                receipt.TargetSources.Sum(targetReceipt =>
                    targetReceipt.Receipt.DefinitionsVisited));
            Assert.All(
                receipt.TargetSources,
                targetReceipt =>
                    Assert.Equal(
                        0,
                        targetReceipt.Receipt.BodiesAcquired));
            TypeCallCompatibilityTarget dangerous = Assert.Single(
                result.Targets,
                candidate => candidate.Method.Name == "Dangerous");
            Assert.Equal(
                TypeCallCompatibilityTargetDisposition.Available,
                dangerous.Disposition);
            Assert.Contains(
                dangerous.Attributes,
                attribute =>
                    attribute.AttributeTypeName
                    == KnownAttributeNames
                        .RequiresUnreferencedCodeAttribute);
            TypeCallCompatibilityTarget dynamic = Assert.Single(
                result.Targets,
                candidate => candidate.Method.Name == "Dynamic");
            Assert.Equal(
                [
                    KnownAttributeNames.RequiresAssemblyFilesAttribute,
                    KnownAttributeNames.RequiresDynamicCodeAttribute,
                ],
                dynamic.Attributes
                    .Select(attribute => attribute.AttributeTypeName)
                    .Order());
            Assert.Contains(
                result.Targets,
                candidate =>
                    candidate.Method.Name == "SameLibrary"
                    && ReferenceEquals(
                        candidate.Subject.Registration,
                        caller.Registration)
                    && candidate.Attributes.Any(attribute =>
                        attribute.AttributeTypeName
                        == KnownAttributeNames
                            .RequiresAssemblyFilesAttribute));

            StructuralSubjectIdentity.WorkspaceSubject workspace =
                StructuralSubjectTestData.Package(
                    new(
                        "compatibility.fixture",
                        "1.0.0",
                        "local",
                        "net11.0",
                        runtimeIdentifier: null))
                    .Workspace;
            SubjectRelationFocusAuthority focus =
                SubjectRelationFocusAuthority.ForAcquiredType(
                    workspace,
                    caller.Registration,
                    caller.Identity,
                    caller.Provenance,
                    focusType);
            SubjectRelationPopulationAuthority population =
                SubjectRelationPopulationAuthority.Capture(
                    workspace,
                    context.Group);
            InspectionGraphCharacteristicDescriptor dynamicColor =
                CompatibilityInspectionGraphCatalog.RequiresDynamicCode;
            InspectionGraphCharacteristicDescriptor filesColor =
                CompatibilityInspectionGraphCatalog.RequiresAssemblyFiles;
            var request = new AnnotatedTypeRelationsCallRequest(
                focus,
                population,
                result,
                [
                    CompatibilityInspectionGraphCatalog
                        .RequiresUnreferencedCode,
                    dynamicColor,
                    filesColor,
                ],
                [dynamicColor, filesColor]);

            AnnotatedTypeRelationsContribution rows =
                AnnotatedTypeRelationsCallAdoption.Rows(request);
            AnnotatedTypeRelationsRelationshipContribution relationship =
                Assert.Single(rows.Relationships);
            SubjectRelationRow row = Assert.Single(
                Assert.IsType<
                    AnnotatedTypeRelationsFamilyTerminal.Rows>(
                        relationship.Terminal).Values);
            Assert.Equal("Dynamic", MemberName(row.Target));
            Assert.Single(row.Occurrences);
            Assert.Equal(2, rows.Colors.Count(color =>
                !color.Annotations.IsEmpty));
            AnnotatedTypeRelationsCatalog catalog =
                AnnotatedTypeRelationsCallAdoption.Register(
                    new AnnotatedTypeRelationsRegistry()).Compile();
            IInspectionQueryCatalog queryCatalog =
                new InspectionQueryRegistry<object>()
                    .Add(
                        AnnotatedTypeRelationsCallAdoption.RowsQuery,
                        static (_, _) => throw new InvalidOperationException())
                    .Add(
                        AnnotatedTypeRelationsCallAdoption.CountQuery,
                        static (_, _) => throw new InvalidOperationException())
                    .Add(
                        AnnotatedTypeRelationsCallAdoption.ExistsQuery,
                        static (_, _) => throw new InvalidOperationException())
                    .Compile();
            AnnotatedTypeRelationsPlan plan = Assert.IsType<
                AnnotatedTypeRelationsPlanningResult.Accepted>(
                    catalog.Resolve(
                        new(
                            focus,
                            [CallGraphInspectionGraphCatalog.Call],
                            SubjectRelationDirectionSelection.Outgoing,
                            [
                                CompatibilityInspectionGraphCatalog
                                    .RequiresUnreferencedCode,
                                dynamicColor,
                                filesColor,
                            ],
                            [dynamicColor, filesColor],
                            AnnotatedTypeRelationsTerminal.Rows),
                        queryCatalog)).Plan;
            InspectionQueryResults results =
                new InspectionQueryRegistry<
                    AnnotatedTypeRelationsContribution>()
                    .Add(
                        AnnotatedTypeRelationsCallAdoption.RowsQuery,
                        static value => value)
                    .Compile()
                    .Plan(
                        AnnotatedTypeRelationsCallAdoption.RowsQuery)
                    .Run(rows);
            AnnotatedTypeRelationsDocument document =
                AnnotatedTypeRelationsGenerator.Generate(plan, results);
            Assert.Single(document.Relations);
            Assert.Single(document.Occurrences);
            Assert.Equal(2, document.Annotations.Length);
            AnnotatedTypeRelationsCallInspectionResult.Available inspection =
                Assert.IsType<
                    AnnotatedTypeRelationsCallInspectionResult.Available>(
                        AnnotatedTypeRelationsCallInspection.Execute(
                            context.Group,
                            caller.Registration,
                            focusType,
                            population,
                            new(
                                focus,
                                AnnotatedTypeRelationsFamilySelection
                                    .AllApplicable,
                                SubjectRelationDirectionSelection.Outgoing,
                                [
                                    CompatibilityInspectionGraphCatalog
                                        .RequiresUnreferencedCode.Id,
                                    dynamicColor.Id,
                                    filesColor.Id,
                                ],
                                [dynamicColor.Id, filesColor.Id],
                                AnnotatedTypeRelationsTerminal.Rows),
                            TestContext.Current.CancellationToken));
            Assert.Equal(
                document.Relations,
                inspection.Inspection.Content.Relations);
            Assert.Equal(
                document.Occurrences,
                inspection.Inspection.Content.Occurrences);
            Assert.Equal(
                document.Annotations,
                inspection.Inspection.Content.Annotations);
            using (var json = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(json))
                {
                    AnnotatedTypeRelationsJson.Write(
                        writer,
                        inspection.Inspection.Content);
                }

                using JsonDocument parsed =
                    JsonDocument.Parse(json.ToArray());
                JsonElement root = parsed.RootElement;
                Assert.Equal(
                    "call",
                    root.GetProperty("selection")
                        .GetProperty("relationships")[0]
                        .GetString());
                Assert.Equal(
                    "FocusedCallSite",
                    root.GetProperty("occurrences")[0]
                        .GetProperty("evidence")
                        .GetProperty("kind")
                        .GetString());
                Assert.Equal(
                    document.Annotations.Length,
                    root.GetProperty("annotations").GetArrayLength());
            }

            AnnotatedTypeRelationsContribution count =
                AnnotatedTypeRelationsCallAdoption.Count(request);
            var countTerminal = Assert.IsType<
                AnnotatedTypeRelationsFamilyTerminal.Count>(
                    Assert.Single(count.Relationships).Terminal);
            Assert.Equal(1, countTerminal.Value);
            Assert.True(countTerminal.IsExact);
            Assert.All(
                count.Colors,
                color => Assert.Empty(color.Annotations));

            AnnotatedTypeRelationsContribution exists =
                AnnotatedTypeRelationsCallAdoption.Exists(request);
            Assert.Equal(
                AnnotatedTypeRelationsExistsValue.True,
                Assert.IsType<
                    AnnotatedTypeRelationsFamilyTerminal.Exists>(
                        Assert.Single(exists.Relationships).Terminal)
                    .Value);

            AssemblyContextTypeCallCompatibilityResult limitedEvidence =
                result with
                {
                    Targets =
                    [
                        .. result.Targets.Select(target =>
                            target with
                            {
                                Disposition =
                                    TypeCallCompatibilityTargetDisposition
                                        .Limited,
                                Limitation = "fixture limitation",
                            }),
                    ],
                };
            var limitedWitnessRequest =
                new AnnotatedTypeRelationsCallRequest(
                    focus,
                    population,
                    limitedEvidence,
                    [dynamicColor, filesColor],
                    [dynamicColor, filesColor]);
            Assert.Equal(
                AnnotatedTypeRelationsExistsValue.True,
                Assert.IsType<
                    AnnotatedTypeRelationsFamilyTerminal.Exists>(
                        Assert.Single(
                            AnnotatedTypeRelationsCallAdoption.Exists(
                                limitedWitnessRequest).Relationships)
                            .Terminal).Value);
            var limitedAbsenceRequest =
                new AnnotatedTypeRelationsCallRequest(
                    focus,
                    population,
                    limitedEvidence,
                    [
                        CompatibilityInspectionGraphCatalog
                            .RequiresUnreferencedCode,
                        dynamicColor,
                    ],
                    [
                        CompatibilityInspectionGraphCatalog
                            .RequiresUnreferencedCode,
                        dynamicColor,
                    ]);
            AnnotatedTypeRelationsContribution limitedAbsence =
                AnnotatedTypeRelationsCallAdoption.Exists(
                    limitedAbsenceRequest);
            Assert.Equal(
                AnnotatedTypeRelationsExistsValue.Undecided,
                Assert.IsType<
                    AnnotatedTypeRelationsFamilyTerminal.Exists>(
                        Assert.Single(limitedAbsence.Relationships).Terminal)
                    .Value);

            var unfiltered = new AnnotatedTypeRelationsCallRequest(
                focus,
                population,
                result);
            AnnotatedTypeRelationsContribution unfilteredRows =
                AnnotatedTypeRelationsCallAdoption.Rows(unfiltered);
            var unfilteredRowsTerminal = Assert.IsType<
                AnnotatedTypeRelationsFamilyTerminal.Rows>(
                    Assert.Single(unfilteredRows.Relationships).Terminal);
            SubjectRelationRow dangerousRow = Assert.Single(
                unfilteredRowsTerminal.Values,
                candidate => MemberName(candidate.Target) == "Dangerous");
            Assert.Equal(2, dangerousRow.Occurrences.Length);
            var unfilteredCount = Assert.IsType<
                AnnotatedTypeRelationsFamilyTerminal.Count>(
                    Assert.Single(
                        AnnotatedTypeRelationsCallAdoption.Count(
                            unfiltered).Relationships).Terminal);
            Assert.True(unfilteredCount.IsExact);
            Assert.Equal(
                unfilteredRowsTerminal.Values.Length,
                unfilteredCount.Value);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TypeCallCompatibilityDoesNotOpenUnrelatedParticipants()
    {
        (string directory, ResolvedAssemblyReference caller,
            ResolvedAssemblyReference target) =
                BuildCompatibilityCallFixture();
        try
        {
            ResolvedAssemblyReference unrelatedSource =
                ResolvedAssemblyReference.CreateFromPath(
                    IndirectPath,
                    AssemblyResolutionProvenance.Local(
                        "unrelated focused-call participant"));
            int opens = 0;
            ResolvedAssemblyReference unrelated =
                ResolvedAssemblyReference.Create(
                    unrelatedSource.Identity,
                    unrelatedSource.Path,
                    () =>
                    {
                        opens++;
                        return File.OpenRead(IndirectPath);
                    },
                    unrelatedSource.Provenance);
            await using CensusContext context =
                CensusContext.Create(caller, target, unrelated);

            AssemblyContextTypeCallCompatibilityResult result =
                AssemblyContextTypeCallCompatibilityQuery.Execute(
                    context.Group,
                    caller.Registration,
                    CompatibilityFocusType(),
                    TestContext.Current.CancellationToken);

            Assert.True(result.TopologyComplete);
            Assert.Equal(0, opens);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TypeCallCompatibilityMissingSignatureDependencyQualifiesTopology()
    {
        (string directory, ResolvedAssemblyReference caller,
            ResolvedAssemblyReference target) =
                BuildMissingSignatureDependencyCallFixture();
        try
        {
            await using CensusContext context =
                CensusContext.Create(caller, target);

            AssemblyContextTypeCallCompatibilityResult result =
                AssemblyContextTypeCallCompatibilityQuery.Execute(
                    context.Group,
                    caller.Registration,
                    CompatibilityFocusType(),
                    TestContext.Current.CancellationToken);

            Assert.Equal(
                TypeCallCompatibilityTopologyDisposition.Partial,
                result.TopologyDisposition);
            Assert.Empty(result.Occurrences);
            Assert.Contains(
                result.UnresolvedOccurrences,
                occurrence =>
                    occurrence.Call.Callee.Name == "Ping"
                    && occurrence.Limitation.Contains(
                        "signature",
                        StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TypeCallCompatibilityUnresolvedFocusCallsKeepExistsUndecided()
    {
        (string directory, ResolvedAssemblyReference caller,
            ResolvedAssemblyReference target) =
                BuildCompatibilityCallFixture();
        try
        {
            await using CensusContext context =
                CensusContext.Create(caller);
            MetadataTypeDefinitionName focusType = CompatibilityFocusType();

            AssemblyContextTypeCallCompatibilityResult result =
                AssemblyContextTypeCallCompatibilityQuery.Execute(
                    context.Group,
                    caller.Registration,
                    focusType,
                    TestContext.Current.CancellationToken);

            Assert.Equal(
                TypeCallCompatibilityTopologyDisposition.Partial,
                result.TopologyDisposition);
            Assert.NotEmpty(result.UnresolvedOccurrences);
            Assert.DoesNotContain(
                result.Occurrences,
                occurrence =>
                    ReferenceEquals(
                        occurrence.Target.Registration,
                        target.Registration));
            (SubjectRelationFocusAuthority focus,
                SubjectRelationPopulationAuthority population) =
                    Authorities(caller, focusType, context.Group);
            AnnotatedTypeRelationsContribution exists =
                AnnotatedTypeRelationsCallAdoption.Exists(
                    new(
                        focus,
                        population,
                        result,
                        [
                            CompatibilityInspectionGraphCatalog
                                .RequiresUnreferencedCode,
                        ],
                        [
                            CompatibilityInspectionGraphCatalog
                                .RequiresUnreferencedCode,
                        ]));

            AnnotatedTypeRelationsRelationshipContribution relationship =
                Assert.Single(exists.Relationships);
            Assert.Equal(
                SubjectRelationProducerDisposition.Partial,
                relationship.Disposition);
            Assert.Equal(
                AnnotatedTypeRelationsExistsValue.Undecided,
                Assert.IsType<
                    AnnotatedTypeRelationsFamilyTerminal.Exists>(
                        relationship.Terminal).Value);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TypeCallCompatibilityUnreadableTargetsKeepColorAbsenceUndecided()
    {
        (string directory, ResolvedAssemblyReference caller,
            ResolvedAssemblyReference target) =
                BuildCompatibilityCallFixture();
        try
        {
            bool rejectTarget = false;
            ResolvedAssemblyReference conditionalTarget =
                ResolvedAssemblyReference.Create(
                    target.Identity,
                    target.Path,
                    () => rejectTarget
                        ? new MemoryStream([0x00, 0x01, 0x02])
                        : File.OpenRead(target.Path!),
                    target.Provenance);
            rejectTarget = true;
            await using CensusContext context =
                CensusContext.Create(caller, conditionalTarget);
            MetadataTypeDefinitionName focusType = CompatibilityFocusType();
            AssemblyContextTypeCallCompatibilityResult result =
                AssemblyContextTypeCallCompatibilityQuery.Execute(
                    context.Group,
                    caller.Registration,
                    focusType,
                    TestContext.Current.CancellationToken);

            Assert.False(result.TopologyComplete);
            Assert.Equal(
                2,
                result.UnavailableTargetMetadata);
            (SubjectRelationFocusAuthority focus,
                SubjectRelationPopulationAuthority population) =
                    Authorities(caller, focusType, context.Group);
            AnnotatedTypeRelationsContribution exists =
                AnnotatedTypeRelationsCallAdoption.Exists(
                    new(
                        focus,
                        population,
                        result,
                        [
                            CompatibilityInspectionGraphCatalog
                                .RequiresUnreferencedCode,
                        ],
                        [
                            CompatibilityInspectionGraphCatalog
                                .RequiresUnreferencedCode,
                        ]));

            AnnotatedTypeRelationsColorContribution color =
                Assert.Single(exists.Colors);
            Assert.Equal(
                SubjectRelationProducerDisposition.Partial,
                color.Disposition);
            Assert.Equal(2, color.Coverage.Unavailable);
            Assert.Equal(
                AnnotatedTypeRelationsExistsValue.Undecided,
                Assert.IsType<
                    AnnotatedTypeRelationsFamilyTerminal.Exists>(
                        Assert.Single(exists.Relationships).Terminal)
                    .Value);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TypeCallCompatibilityMalformedTargetAttributesKeepColorAbsenceUndecided()
    {
        (string directory, ResolvedAssemblyReference caller,
            ResolvedAssemblyReference target) =
                BuildCompatibilityCallFixture();
        try
        {
            CorruptTargetAttributeConstructor(target.Path!);
            await using CensusContext context =
                CensusContext.Create(caller, target);
            MetadataTypeDefinitionName focusType = CompatibilityFocusType();

            AssemblyContextTypeCallCompatibilityResult result =
                AssemblyContextTypeCallCompatibilityQuery.Execute(
                    context.Group,
                    caller.Registration,
                    focusType,
                    TestContext.Current.CancellationToken);

            Assert.True(result.TopologyComplete);
            Assert.Empty(result.UnresolvedOccurrences);
            Assert.Contains(
                result.Targets,
                candidate =>
                    candidate.Method.Name == "Dangerous"
                    && candidate.Disposition
                        == TypeCallCompatibilityTargetDisposition.Limited);
            (SubjectRelationFocusAuthority focus,
                SubjectRelationPopulationAuthority population) =
                    Authorities(caller, focusType, context.Group);
            AnnotatedTypeRelationsContribution exists =
                AnnotatedTypeRelationsCallAdoption.Exists(
                    new(
                        focus,
                        population,
                        result,
                        [
                            CompatibilityInspectionGraphCatalog
                                .RequiresUnreferencedCode,
                        ],
                        [
                            CompatibilityInspectionGraphCatalog
                                .RequiresUnreferencedCode,
                        ]));

            AnnotatedTypeRelationsColorContribution color =
                Assert.Single(exists.Colors);
            Assert.Equal(
                SubjectRelationProducerDisposition.Partial,
                color.Disposition);
            Assert.Equal(
                AnnotatedTypeRelationsExistsValue.Undecided,
                Assert.IsType<
                    AnnotatedTypeRelationsFamilyTerminal.Exists>(
                        Assert.Single(exists.Relationships).Terminal)
                    .Value);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static void CorruptTargetAttributeConstructor(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        using (var image = new PEReader(
            new MemoryStream(bytes, writable: false)))
        {
            MetadataReader reader = image.GetMetadataReader();
            MethodDefinitionHandle dangerous =
                Assert.Single(
                    reader.MethodDefinitions,
                    handle =>
                        reader.GetString(
                            reader.GetMethodDefinition(handle).Name)
                        == "Dangerous");
            CustomAttributeHandle attribute =
                Assert.Single(
                    reader.GetMethodDefinition(dangerous)
                        .GetCustomAttributes());
            int rowSize =
                reader.GetTableRowSize(TableIndex.CustomAttribute);
            Assert.Equal(6, rowSize);
            Assert.True(
                reader.GetHeapSize(HeapIndex.Blob)
                    < ushort.MaxValue);
            int rowOffset =
                image.PEHeaders.MetadataStartOffset
                + reader.GetTableMetadataOffset(
                    TableIndex.CustomAttribute)
                + ((MetadataTokens.GetRowNumber(attribute) - 1)
                    * rowSize);
            const ushort invalidMemberReference =
                (0x1fff << 3) | 0x3;
            BinaryPrimitives.WriteUInt16LittleEndian(
                bytes.AsSpan(
                    rowOffset + sizeof(ushort),
                    sizeof(ushort)),
                invalidMemberReference);
        }
        File.WriteAllBytes(path, bytes);
    }

    static string MemberName(InspectionGraphSubject subject) =>
        Assert.IsType<InspectionGraphMemberIdentity.CallGraph>(
            Assert.IsType<InspectionGraphSubject.MemberSubject>(
                subject).Identity).Member.Name;

    static MetadataTypeDefinitionName CompatibilityFocusType() =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Consumer",
                    ["Entry"])).Name;

    static (SubjectRelationFocusAuthority Focus,
        SubjectRelationPopulationAuthority Population) Authorities(
                ResolvedAssemblyReference caller,
                MetadataTypeDefinitionName focusType,
                object populationIdentity)
    {
        StructuralSubjectIdentity.WorkspaceSubject workspace =
                StructuralSubjectTestData.Package(
                    new(
                        "compatibility.fixture",
                        "1.0.0",
                        "local",
                        "net11.0",
                        runtimeIdentifier: null))
                    .Workspace;
        return (
                SubjectRelationFocusAuthority.ForAcquiredType(
                    workspace,
                    caller.Registration,
                    caller.Identity,
                    caller.Provenance,
                    focusType),
                SubjectRelationPopulationAuthority.Capture(
                    workspace,
                    populationIdentity));
    }

    static string MemberFingerprint(
        AssemblyContextCallCensusMember member) =>
        string.Join(
            "|",
            member.OrderingKey.Assembly.Name,
            member.OrderingKey.ModuleVersionId,
            member.OrderingKey.MetadataToken,
            member.HasBody);

    static string OccurrenceFingerprint(
        AssemblyContextCallCensusOccurrence occurrence) =>
        string.Join(
            "|",
            occurrence.SourceOrderingKey.Assembly.Name,
            occurrence.SourceOrderingKey.MetadataToken,
            occurrence.TargetOrderingKey.Assembly.Name,
            occurrence.TargetOrderingKey.MetadataToken,
            occurrence.OrderingKey.EvidenceMethod.MetadataToken,
            occurrence.OrderingKey.ILOffset,
            occurrence.OrderingKey.OperandToken,
            occurrence.OrderingKey.Kind);

    static string VersionSkewFingerprint(
        ILInspector.Analysis.CatalogCallCensusVersionSkewEvidence
            evidence) =>
        string.Join(
            "|",
            evidence.CallSite.Storage.ModuleVersionId,
            evidence.CallSite.Storage.MethodToken,
            evidence.CallSite.Storage.ILOffset,
            evidence.Requested,
            evidence.Selected,
            string.Join(",", evidence.AdmittedAlternatives));

    static (string Directory, ResolvedAssemblyReference Caller,
        ResolvedAssemblyReference Selected,
        ResolvedAssemblyReference Shadow)
        BuildSameIdentityCallFixture()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "dotnet-inspect-call-census-query-"
                + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var targetName = new AssemblyName("CallCensusQueryTarget")
        {
            Version = new Version(1, 0, 0, 0),
        };
        string selectedPath =
            Path.Combine(directory, "selected.dll");
        MethodBuilder selectedMethod =
            BuildTarget(targetName, selectedPath);
        string shadowPath =
            Path.Combine(directory, "shadow.dll");
        _ = BuildTarget(targetName, shadowPath);

        var callerName = new AssemblyName("CallCensusQueryCaller")
        {
            Version = new Version(1, 0, 0, 0),
        };
        var callerAssembly = new PersistedAssemblyBuilder(
            callerName,
            typeof(object).Assembly);
        ModuleBuilder callerModule =
            callerAssembly.DefineDynamicModule(callerName.Name!);
        TypeBuilder callerType = callerModule.DefineType(
            "Consumer.Entry",
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed);
        MethodBuilder run = callerType.DefineMethod(
            "Run",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes);
        ILGenerator il = run.GetILGenerator();
        il.Emit(OpCodes.Call, selectedMethod);
        il.Emit(OpCodes.Ret);
        _ = callerType.CreateType();
        string callerPath =
            Path.Combine(directory, "caller.dll");
        callerAssembly.Save(callerPath);
        return (
            directory,
            Reference(callerPath),
            Reference(selectedPath),
            Reference(shadowPath));

        static MethodBuilder BuildTarget(
            AssemblyName assemblyName,
            string path)
        {
            var assembly = new PersistedAssemblyBuilder(
                assemblyName,
                typeof(object).Assembly);
            ModuleBuilder module =
                assembly.DefineDynamicModule(assemblyName.Name!);
            TypeBuilder type = module.DefineType(
                "Target.Api",
                TypeAttributes.Public
                    | TypeAttributes.Abstract
                    | TypeAttributes.Sealed);
            MethodBuilder ping = type.DefineMethod(
                "Ping",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(void),
                Type.EmptyTypes);
            ping.GetILGenerator().Emit(OpCodes.Ret);
            _ = type.CreateType();
            assembly.Save(path);
            return ping;
        }

        static ResolvedAssemblyReference Reference(string path) =>
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(
                    "call-census query policy test"));
    }

    static (string Directory, ResolvedAssemblyReference Caller,
        ResolvedAssemblyReference Target)
        BuildCompatibilityCallFixture()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "dotnet-inspect-type-call-compatibility-"
                + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var targetName = new AssemblyName("CompatibilityTarget")
        {
            Version = new Version(1, 0, 0, 0),
        };
        var targetAssembly = new PersistedAssemblyBuilder(
            targetName,
            typeof(object).Assembly);
        ModuleBuilder targetModule =
            targetAssembly.DefineDynamicModule(targetName.Name!);
        TypeBuilder targetType = targetModule.DefineType(
            "Target.Api",
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed);
        MethodBuilder dangerous = targetType.DefineMethod(
            "Dangerous",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes);
        dangerous.SetCustomAttribute(
            new CustomAttributeBuilder(
                typeof(RequiresUnreferencedCodeAttribute)
                    .GetConstructor([typeof(string)])!,
                ["fixture"]));
        dangerous.GetILGenerator().Emit(OpCodes.Ret);
        MethodBuilder dynamic = targetType.DefineMethod(
            "Dynamic",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes);
        dynamic.SetCustomAttribute(
            new CustomAttributeBuilder(
                typeof(RequiresDynamicCodeAttribute)
                    .GetConstructor([typeof(string)])!,
                ["fixture"]));
        dynamic.SetCustomAttribute(
            new CustomAttributeBuilder(
                typeof(RequiresAssemblyFilesAttribute)
                    .GetConstructor([typeof(string)])!,
                ["fixture"]));
        dynamic.GetILGenerator().Emit(OpCodes.Ret);
        _ = targetType.CreateType();
        string targetPath = Path.Combine(directory, "target.dll");
        targetAssembly.Save(targetPath);

        var callerName = new AssemblyName("CompatibilityCaller")
        {
            Version = new Version(1, 0, 0, 0),
        };
        var callerAssembly = new PersistedAssemblyBuilder(
            callerName,
            typeof(object).Assembly);
        ModuleBuilder callerModule =
            callerAssembly.DefineDynamicModule(callerName.Name!);
        TypeBuilder localTargetType = callerModule.DefineType(
            "Consumer.LocalTarget",
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed);
        MethodBuilder sameLibrary = localTargetType.DefineMethod(
            "SameLibrary",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes);
        sameLibrary.SetCustomAttribute(
            new CustomAttributeBuilder(
                typeof(RequiresAssemblyFilesAttribute)
                    .GetConstructor([typeof(string)])!,
                ["fixture"]));
        sameLibrary.GetILGenerator().Emit(OpCodes.Ret);
        _ = localTargetType.CreateType();
        TypeBuilder callerType = callerModule.DefineType(
            "Consumer.Entry",
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed);
        MethodBuilder local = callerType.DefineMethod(
            "Local",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes);
        local.GetILGenerator().Emit(OpCodes.Ret);
        MethodBuilder run = callerType.DefineMethod(
            "Run",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes);
        ILGenerator il = run.GetILGenerator();
        il.Emit(OpCodes.Call, dangerous);
        il.Emit(OpCodes.Call, dangerous);
        il.Emit(OpCodes.Call, dynamic);
        il.Emit(OpCodes.Call, sameLibrary);
        il.Emit(OpCodes.Call, local);
        il.Emit(OpCodes.Ret);
        _ = callerType.CreateType();
        string callerPath = Path.Combine(directory, "caller.dll");
        callerAssembly.Save(callerPath);

        return (
            directory,
            Reference(callerPath),
            Reference(targetPath));

        static ResolvedAssemblyReference Reference(string path) =>
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(
                    "Type call compatibility test"));
    }

    static (string Directory, ResolvedAssemblyReference Caller,
        ResolvedAssemblyReference Target)
        BuildMissingSignatureDependencyCallFixture()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "dotnet-inspect-call-signature-dependency-"
                + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var dependencyName = new AssemblyName("CallSignatureDependency");
        var dependencyAssembly = new PersistedAssemblyBuilder(
            dependencyName,
            typeof(object).Assembly);
        ModuleBuilder dependencyModule =
            dependencyAssembly.DefineDynamicModule(dependencyName.Name!);
        Type dependencyType = dependencyModule.DefineType(
                "Contracts.Payload",
                TypeAttributes.Public)
            .CreateType()!;
        dependencyAssembly.Save(
            Path.Combine(directory, "CallSignatureDependency.dll"));

        var targetName = new AssemblyName("CallSignatureTarget");
        var targetAssembly = new PersistedAssemblyBuilder(
            targetName,
            typeof(object).Assembly);
        ModuleBuilder targetModule =
            targetAssembly.DefineDynamicModule(targetName.Name!);
        TypeBuilder targetType = targetModule.DefineType(
            "Target.Api",
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed);
        MethodBuilder ping = targetType.DefineMethod(
            "Ping",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [dependencyType]);
        ping.GetILGenerator().Emit(OpCodes.Ret);
        _ = targetType.CreateType();
        string targetPath =
            Path.Combine(directory, "CallSignatureTarget.dll");
        targetAssembly.Save(targetPath);

        var callerName = new AssemblyName("CallSignatureCaller");
        var callerAssembly = new PersistedAssemblyBuilder(
            callerName,
            typeof(object).Assembly);
        ModuleBuilder callerModule =
            callerAssembly.DefineDynamicModule(callerName.Name!);
        TypeBuilder callerType = callerModule.DefineType(
            "Consumer.Entry",
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed);
        MethodBuilder run = callerType.DefineMethod(
            "Run",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes);
        ILGenerator il = run.GetILGenerator();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, ping);
        il.Emit(OpCodes.Ret);
        _ = callerType.CreateType();
        string callerPath =
            Path.Combine(directory, "CallSignatureCaller.dll");
        callerAssembly.Save(callerPath);

        return (
            directory,
            Reference(callerPath),
            Reference(targetPath));

        static ResolvedAssemblyReference Reference(string path) =>
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(
                    "call signature dependency test"));
    }

    sealed class SelectedAssemblyPolicy(
        IReadOnlyList<ResolvedAssemblyReference> assemblies,
        ResolvedAssemblyReference selected)
        : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            if (request.Target
                    is not AssemblyBindingTarget.AssemblyReference reference)
            {
                return Unavailable();
            }

            ResolvedAssemblyReference? match =
                assemblies.FirstOrDefault(assembly =>
                    assembly.Identity.IsEquivalentTo(
                        reference.Identity));
            if (match is null)
                return Unavailable();

            return new(
                Version,
                AssemblyBindingSelection.Found(
                    selected.Identity.IsEquivalentTo(
                        reference.Identity)
                            ? selected
                            : match));
        }

        AssemblyBindingSelectionSnapshot Unavailable() =>
            new(
                Version,
                AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind
                            .CandidateUnavailable)));
    }

    sealed class CensusContext : IAsyncDisposable
    {
        CensusContext(
            InspectionWorkspace workspace,
            AssemblyContextGroup group)
        {
            Workspace = workspace;
            Group = group;
        }

        internal InspectionWorkspace Workspace { get; }
        internal AssemblyContextGroup Group { get; }

        internal static CensusContext Create(params string[] paths) =>
            Create(
                paths.Select(path =>
                    ResolvedAssemblyReference.CreateFromPath(
                        path,
                        AssemblyResolutionProvenance.Local(
                            "call-census test")))
                    .ToArray());

        internal static CensusContext Create(
            params ResolvedAssemblyReference[] assemblies)
        {
            var policy =
                new SourceRelativeAssemblyGroupBindingPolicy(
                    assemblies.Select(assembly => (
                        assembly,
                        Policy: (IAssemblyBindingPolicy)
                            new AssemblyDependencyResolver(
                                new(
                                    assembly.Path
                                        ?? CallerPath)
                                {
                                    PreferImplementationAssemblies = true,
                                    AllowPlatformAssemblyVersionRollForward =
                                        true,
                                }))));
            return Create(policy, assemblies);
        }

        internal static CensusContext Create(
            IAssemblyBindingPolicy policy,
            params ResolvedAssemblyReference[] assemblies)
        {
            var workspace = new InspectionWorkspace();
            AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup(
                    assemblies.Select(assembly =>
                        new AssemblyContextParticipant(
                            assembly,
                            policy)));
            return new(workspace, group);
        }

        public ValueTask DisposeAsync() => Workspace.DisposeAsync();
    }
}
