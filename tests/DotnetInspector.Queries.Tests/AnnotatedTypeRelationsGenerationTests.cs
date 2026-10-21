using DotnetInspector.Sections;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries.Tests;

public sealed class AnnotatedTypeRelationsGenerationTests
{
    private static readonly InspectionGraphEvidenceDescriptor Evidence =
        new("test.annotated-relations-generation", InspectionGraphOwner.Queries);

    [Fact]
    public void RowsCanonicalizeSharedContributionsAndAnnotations()
    {
        Context context = CreateContext();
        InspectionGraphRelationshipDescriptor relationship =
            Relationship("test.calls");
        InspectionGraphCharacteristicDescriptor color =
            Color("test.unsafe-implementation");
        var query = Query("test.rows");
        AnnotatedTypeRelationsPlan plan = Plan(
            context.Focus,
            relationship,
            query,
            AnnotatedTypeRelationsTerminal.Rows,
            color,
            requiredColor: color);
        SubjectRelationRow row = Row(context, relationship, "Target");
        var relationIdentity = new AnnotatedTypeRelationsRelationIdentity(
            relationship,
            row.Source,
            row.Target);
        AnnotatedTypeRelationsAnnotation annotation =
            AnnotatedTypeRelationsAnnotation.Create(
                color,
                new InspectionGraphValue.Boolean(true),
                new AnnotatedTypeRelationsAnnotationTarget.Relation(
                    relationIdentity),
                InspectionGraphCharacteristicDerivationKind.Direct,
                "unsafe body");
        AnnotatedTypeRelationsContribution contribution = Contribution(
            context,
            AnnotatedTypeRelationsTerminal.Rows,
            [
                RelationshipContribution(
                    context,
                    relationship,
                    new AnnotatedTypeRelationsFamilyTerminal.Rows([row]),
                    [color]),
            ],
            [ColorContribution(context, color, [annotation])]);

        AnnotatedTypeRelationsDocument document =
            AnnotatedTypeRelationsGenerator.Generate(
                plan,
                Results(query, contribution));

        var terminal =
            Assert.IsType<AnnotatedTypeRelationsTerminalOutcome.Rows>(
                document.Terminal);
        Assert.Equal(1, terminal.Count);
        Assert.True(terminal.IsComplete);
        Assert.Equal([0, 1], document.Subjects.Select(subject => subject.Id));
        AnnotatedTypeRelationsRelation generated =
            Assert.Single(document.Relations);
        Assert.Equal(0, generated.Id);
        Assert.Equal(0, generated.SourceSubjectId);
        Assert.Equal(1, generated.TargetSubjectId);
        Assert.Single(document.Occurrences);
        Assert.Equal(0, document.Occurrences[0].Id);
        Assert.Equal(0, document.Occurrences[0].RelationId);
        AnnotatedTypeRelationsDocumentAnnotation generatedAnnotation =
            Assert.Single(document.Annotations);
        Assert.Equal(
            new(
                AnnotatedTypeRelationsDocumentTargetKind.Relation,
                generated.Id),
            generatedAnnotation.Target);
        Assert.Same(query, generatedAnnotation.Producer);
        Assert.Single(document.RelationshipOutcomes);
        Assert.Single(document.ColorOutcomes);
        Assert.Single(document.FocusJoins);
    }

    [Fact]
    public void RowsRejectRepeatedCanonicalRelations()
    {
        Context context = CreateContext();
        InspectionGraphRelationshipDescriptor relationship =
            Relationship("test.calls");
        var query = Query("test.rows");
        AnnotatedTypeRelationsPlan plan = Plan(
            context.Focus,
            relationship,
            query,
            AnnotatedTypeRelationsTerminal.Rows);
        SubjectRelationRow row = Row(context, relationship, "Target");
        AnnotatedTypeRelationsContribution contribution = Contribution(
            context,
            AnnotatedTypeRelationsTerminal.Rows,
            [
                RelationshipContribution(
                    context,
                    relationship,
                    new AnnotatedTypeRelationsFamilyTerminal.Rows([row, row])),
            ]);

        AnnotatedTypeRelationsContractException exception =
            Assert.Throws<AnnotatedTypeRelationsContractException>(
                () => AnnotatedTypeRelationsGenerator.Generate(
                    plan,
                    Results(query, contribution)));

        Assert.Contains("was repeated", exception.Message);
    }

    [Fact]
    public void RowsRequireTheirFamilyPopulationAuthority()
    {
        Context context = CreateContext();
        InspectionGraphRelationshipDescriptor relationship =
            Relationship("test.calls");
        var query = Query("test.rows");
        AnnotatedTypeRelationsPlan plan = Plan(
            context.Focus,
            relationship,
            query,
            AnnotatedTypeRelationsTerminal.Rows);
        SubjectRelationRow row = Row(context, relationship, "Target");
        SubjectRelationPopulationAuthority otherPopulation =
            SubjectRelationPopulationAuthority.Capture(
                context.Focus.Workspace,
                new object());
        var family = new AnnotatedTypeRelationsRelationshipContribution(
            SubjectRelationForm.Invocation,
            relationship,
            otherPopulation,
            SubjectRelationProducerDisposition.Complete,
            Coverage(SubjectRelationProducerDisposition.Complete),
            [],
            new AnnotatedTypeRelationsFamilyTerminal.Rows([row]));
        AnnotatedTypeRelationsContribution contribution =
            new(
                context.Focus,
                AnnotatedTypeRelationsTerminal.Rows,
                [family]);

        AnnotatedTypeRelationsContractException exception =
            Assert.Throws<AnnotatedTypeRelationsContractException>(
                () => AnnotatedTypeRelationsGenerator.Generate(
                    plan,
                    Results(query, contribution)));

        Assert.Contains("foreign population", exception.Message);
    }

    [Fact]
    public void RowsPreserveSuccessfulFamiliesBesideFailures()
    {
        Context context = CreateContext();
        InspectionGraphRelationshipDescriptor available =
            Relationship("test.calls");
        InspectionGraphRelationshipDescriptor failed =
            Relationship("test.constructs");
        var query = Query("test.rows");
        AnnotatedTypeRelationsPlan plan = Plan(
            context.Focus,
            [available, failed],
            query,
            AnnotatedTypeRelationsTerminal.Rows);
        SubjectRelationRow row = Row(context, available, "Target");
        AnnotatedTypeRelationsContribution contribution = Contribution(
            context,
            AnnotatedTypeRelationsTerminal.Rows,
            [
                RelationshipContribution(
                    context,
                    available,
                    new AnnotatedTypeRelationsFamilyTerminal.Rows([row])),
                RelationshipContribution(
                    context,
                    failed,
                    new AnnotatedTypeRelationsFamilyTerminal.Rows([]),
                    disposition:
                        SubjectRelationProducerDisposition.Failed),
            ]);

        AnnotatedTypeRelationsDocument document =
            AnnotatedTypeRelationsGenerator.Generate(
                plan,
                Results(query, contribution));

        var terminal =
            Assert.IsType<AnnotatedTypeRelationsTerminalOutcome.Rows>(
                document.Terminal);
        Assert.Equal(1, terminal.Count);
        Assert.False(terminal.IsComplete);
        Assert.Single(document.Relations);
        Assert.Equal(2, document.RelationshipOutcomes.Length);
    }

    [Fact]
    public void DecorationColorDoesNotWeakenRowsCompletion()
    {
        Context context = CreateContext();
        InspectionGraphRelationshipDescriptor relationship =
            Relationship("test.calls");
        InspectionGraphCharacteristicDescriptor decoration =
            Color("test.unsafe-implementation");
        var query = Query("test.rows");
        AnnotatedTypeRelationsPlan plan = Plan(
            context.Focus,
            relationship,
            query,
            AnnotatedTypeRelationsTerminal.Rows,
            decoration);
        SubjectRelationRow row = Row(context, relationship, "Target");
        AnnotatedTypeRelationsContribution contribution = Contribution(
            context,
            AnnotatedTypeRelationsTerminal.Rows,
            [
                RelationshipContribution(
                    context,
                    relationship,
                    new AnnotatedTypeRelationsFamilyTerminal.Rows([row])),
            ],
            [
                ColorContribution(
                    context,
                    decoration,
                    disposition:
                        SubjectRelationProducerDisposition.Partial),
            ]);

        AnnotatedTypeRelationsDocument document =
            AnnotatedTypeRelationsGenerator.Generate(
                plan,
                Results(query, contribution));

        Assert.True(
            Assert.IsType<AnnotatedTypeRelationsTerminalOutcome.Rows>(
                document.Terminal).IsComplete);
        Assert.Equal(
            SubjectRelationProducerDisposition.Partial,
            Assert.Single(document.ColorOutcomes)
                .Contribution.Disposition);
    }

    [Fact]
    public void GenerationRejectsPredicateColorMismatch()
    {
        Context context = CreateContext();
        InspectionGraphRelationshipDescriptor relationship =
            Relationship("test.calls");
        InspectionGraphCharacteristicDescriptor required =
            Color("test.unsafe-propagation");
        var query = Query("test.count");
        AnnotatedTypeRelationsPlan plan = Plan(
            context.Focus,
            relationship,
            query,
            AnnotatedTypeRelationsTerminal.Count,
            required,
            required);
        AnnotatedTypeRelationsContribution contribution = Contribution(
            context,
            AnnotatedTypeRelationsTerminal.Count,
            [
                RelationshipContribution(
                    context,
                    relationship,
                    new AnnotatedTypeRelationsFamilyTerminal.Count(
                        1,
                        isExact: true)),
            ],
            [ColorContribution(context, required)]);

        AnnotatedTypeRelationsContractException exception =
            Assert.Throws<AnnotatedTypeRelationsContractException>(
                () => AnnotatedTypeRelationsGenerator.Generate(
                    plan,
                    Results(query, contribution)));

        Assert.Contains("predicate-color set", exception.Message);
    }

    [Fact]
    public void CountUsesNativeFamilyCountsWithoutFabricatingRows()
    {
        Context context = CreateContext();
        InspectionGraphRelationshipDescriptor first =
            Relationship("test.calls");
        InspectionGraphRelationshipDescriptor second =
            Relationship("test.constructs");
        InspectionGraphCharacteristicDescriptor required =
            Color("test.unsafe-propagation");
        var query = Query("test.count");
        AnnotatedTypeRelationsPlan plan = Plan(
            context.Focus,
            [first, second],
            query,
            AnnotatedTypeRelationsTerminal.Count,
            required,
            required);
        AnnotatedTypeRelationsContribution contribution = Contribution(
            context,
            AnnotatedTypeRelationsTerminal.Count,
            [
                RelationshipContribution(
                    context,
                    first,
                    new AnnotatedTypeRelationsFamilyTerminal.Count(
                        2,
                        isExact: true),
                    [required]),
                RelationshipContribution(
                    context,
                    second,
                    new AnnotatedTypeRelationsFamilyTerminal.Count(
                        1,
                        isExact: false),
                    [required],
                    SubjectRelationProducerDisposition.Partial),
            ],
            [
                ColorContribution(
                    context,
                    required,
                    disposition:
                        SubjectRelationProducerDisposition.Partial),
            ]);

        AnnotatedTypeRelationsDocument document =
            AnnotatedTypeRelationsGenerator.Generate(
                plan,
                Results(query, contribution));

        var terminal =
            Assert.IsType<AnnotatedTypeRelationsTerminalOutcome.CountResult>(
                document.Terminal);
        Assert.Equal(3, terminal.Value);
        Assert.False(terminal.IsExact);
        AssertScalarDocument(document);
    }

    [Fact]
    public void ExistsTrueSettlesDespiteIncompleteNeighbors()
    {
        Context context = CreateContext();
        InspectionGraphRelationshipDescriptor witness =
            Relationship("test.calls");
        InspectionGraphRelationshipDescriptor failed =
            Relationship("test.constructs");
        InspectionGraphCharacteristicDescriptor required =
            Color("test.async-to-blocking");
        var query = Query("test.exists");
        AnnotatedTypeRelationsPlan plan = Plan(
            context.Focus,
            [witness, failed],
            query,
            AnnotatedTypeRelationsTerminal.Exists,
            required,
            required);
        AnnotatedTypeRelationsContribution contribution = Contribution(
            context,
            AnnotatedTypeRelationsTerminal.Exists,
            [
                RelationshipContribution(
                    context,
                    witness,
                    new AnnotatedTypeRelationsFamilyTerminal.Exists(
                        AnnotatedTypeRelationsExistsValue.True),
                    [required],
                    SubjectRelationProducerDisposition.Partial),
                RelationshipContribution(
                    context,
                    failed,
                    new AnnotatedTypeRelationsFamilyTerminal.Exists(
                        AnnotatedTypeRelationsExistsValue.Undecided),
                    [required],
                    SubjectRelationProducerDisposition.Failed),
            ],
            [
                ColorContribution(
                    context,
                    required,
                    disposition:
                        SubjectRelationProducerDisposition.Partial),
            ]);

        AnnotatedTypeRelationsDocument document =
            AnnotatedTypeRelationsGenerator.Generate(
                plan,
                Results(query, contribution));

        Assert.Equal(
            AnnotatedTypeRelationsExistsValue.True,
            Assert.IsType<AnnotatedTypeRelationsTerminalOutcome.Exists>(
                document.Terminal).Value);
        AssertScalarDocument(document);
    }

    [Theory]
    [InlineData(
        SubjectRelationProducerDisposition.Complete,
        AnnotatedTypeRelationsExistsValue.False)]
    [InlineData(
        SubjectRelationProducerDisposition.Partial,
        AnnotatedTypeRelationsExistsValue.Undecided)]
    public void ExistsFalseRequiresCompletePredicateColors(
        SubjectRelationProducerDisposition colorDisposition,
        AnnotatedTypeRelationsExistsValue expected)
    {
        Context context = CreateContext();
        InspectionGraphRelationshipDescriptor relationship =
            Relationship("test.calls");
        InspectionGraphCharacteristicDescriptor required =
            Color("test.unsafe-propagation");
        var query = Query("test.exists");
        AnnotatedTypeRelationsPlan plan = Plan(
            context.Focus,
            relationship,
            query,
            AnnotatedTypeRelationsTerminal.Exists,
            required,
            required);
        AnnotatedTypeRelationsContribution contribution = Contribution(
            context,
            AnnotatedTypeRelationsTerminal.Exists,
            [
                RelationshipContribution(
                    context,
                    relationship,
                    new AnnotatedTypeRelationsFamilyTerminal.Exists(
                        AnnotatedTypeRelationsExistsValue.False),
                    [required]),
            ],
            [
                ColorContribution(
                    context,
                    required,
                    disposition: colorDisposition),
            ]);

        AnnotatedTypeRelationsDocument document =
            AnnotatedTypeRelationsGenerator.Generate(
                plan,
                Results(query, contribution));

        Assert.Equal(
            expected,
            Assert.IsType<AnnotatedTypeRelationsTerminalOutcome.Exists>(
                document.Terminal).Value);
    }

    [Fact]
    public void GenerationRejectsForeignFocus()
    {
        Context context = CreateContext();
        Context foreign = CreateContext("Other");
        InspectionGraphRelationshipDescriptor relationship =
            Relationship("test.calls");
        var query = Query("test.count");
        AnnotatedTypeRelationsPlan plan = Plan(
            context.Focus,
            relationship,
            query,
            AnnotatedTypeRelationsTerminal.Count);
        AnnotatedTypeRelationsContribution contribution = Contribution(
            foreign,
            AnnotatedTypeRelationsTerminal.Count,
            [
                RelationshipContribution(
                    foreign,
                    relationship,
                    new AnnotatedTypeRelationsFamilyTerminal.Count(
                        0,
                        isExact: true)),
            ]);

        AnnotatedTypeRelationsContractException exception =
            Assert.Throws<AnnotatedTypeRelationsContractException>(
                () => AnnotatedTypeRelationsGenerator.Generate(
                    plan,
                    Results(query, contribution)));

        Assert.Contains("foreign focus", exception.Message);
    }

    private static void AssertScalarDocument(
        AnnotatedTypeRelationsDocument document)
    {
        Assert.Empty(document.Subjects);
        Assert.Empty(document.Relations);
        Assert.Empty(document.Occurrences);
        Assert.Empty(document.Annotations);
        Assert.Empty(document.FocusJoins);
    }

    private static AnnotatedTypeRelationsRelationshipContribution
        RelationshipContribution(
            Context context,
            InspectionGraphRelationshipDescriptor relationship,
            AnnotatedTypeRelationsFamilyTerminal terminal,
            IEnumerable<InspectionGraphCharacteristicDescriptor>?
                predicateColors = null,
            SubjectRelationProducerDisposition disposition =
                SubjectRelationProducerDisposition.Complete) =>
        new(
            SubjectRelationForm.Invocation,
            relationship,
            context.Population,
            disposition,
            Coverage(disposition),
            predicateColors,
            terminal);

    private static AnnotatedTypeRelationsColorContribution ColorContribution(
        Context context,
        InspectionGraphCharacteristicDescriptor color,
        IEnumerable<AnnotatedTypeRelationsAnnotation>? annotations = null,
        SubjectRelationProducerDisposition disposition =
            SubjectRelationProducerDisposition.Complete) =>
        new(
            color,
            context.Population,
            disposition,
            Coverage(disposition),
            annotations);

    private static SubjectRelationCoverage Coverage(
        SubjectRelationProducerDisposition disposition) =>
        disposition == SubjectRelationProducerDisposition.Complete
            ? new(1, 1, 0, 0, 0)
            : new(
                1,
                0,
                0,
                disposition == SubjectRelationProducerDisposition.Failed
                    ? 1
                    : 0,
                disposition == SubjectRelationProducerDisposition.Failed
                    ? 0
                    : 1);

    private static AnnotatedTypeRelationsContribution Contribution(
        Context context,
        AnnotatedTypeRelationsTerminal terminal,
        IEnumerable<AnnotatedTypeRelationsRelationshipContribution>
            relationships,
        IEnumerable<AnnotatedTypeRelationsColorContribution>? colors = null) =>
        new(context.Focus, terminal, relationships, colors);

    private static SubjectRelationRow Row(
        Context context,
        InspectionGraphRelationshipDescriptor relationship,
        string targetName)
    {
        InspectionGraphSubject source =
            InspectionGraphSubject.ForStructuralType(
                TypeRef.Definition("Sample", "Sample", "Widget"));
        InspectionGraphSubject target =
            InspectionGraphSubject.ForStructuralType(
                TypeRef.Definition("Sample", "Sample", targetName));
        var occurrence = new InspectionGraphOccurrence(
            0,
            relationship,
            source,
            target,
            new OccurrenceEvidence(new object()),
            []);
        return new(
            SubjectRelationForm.Invocation,
            SubjectRelationEvidenceKind.StaticIlObservation,
            relationship,
            source,
            target,
            SubjectRelationFocusCorrespondence.Create(
                context.Focus,
                context.Population,
                source,
                InspectionGraphEndpointRole.Source,
                new object()),
            [occurrence]);
    }

    private static AnnotatedTypeRelationsPlan Plan(
        SubjectRelationFocusAuthority focus,
        InspectionGraphRelationshipDescriptor relationship,
        InspectionQuery<AnnotatedTypeRelationsContribution> query,
        AnnotatedTypeRelationsTerminal terminal,
        InspectionGraphCharacteristicDescriptor? color = null,
        InspectionGraphCharacteristicDescriptor? requiredColor = null) =>
        Plan(
            focus,
            [relationship],
            query,
            terminal,
            color,
            requiredColor);

    private static AnnotatedTypeRelationsPlan Plan(
        SubjectRelationFocusAuthority focus,
        IEnumerable<InspectionGraphRelationshipDescriptor> relationships,
        InspectionQuery<AnnotatedTypeRelationsContribution> query,
        AnnotatedTypeRelationsTerminal terminal,
        InspectionGraphCharacteristicDescriptor? color = null,
        InspectionGraphCharacteristicDescriptor? requiredColor = null)
    {
        InspectionGraphRelationshipDescriptor[] relationshipSnapshot =
            [.. relationships];
        var registry = new AnnotatedTypeRelationsRegistry();
        foreach (
            InspectionGraphRelationshipDescriptor relationship in
            relationshipSnapshot)
        {
            registry.AddRelationship(
                new(
                    SubjectRelationForm.Invocation,
                    relationship,
                    Queries(query, terminal)));
        }
        if (color is not null)
            registry.AddColor(new(color, Queries(query, terminal)));

        AnnotatedTypeRelationsCatalog catalog = registry.Compile();
        IInspectionQueryCatalog queryCatalog =
            QueryCatalog(query);
        var request = new AnnotatedTypeRelationsPlanningRequest(
            focus,
            relationshipSnapshot,
            SubjectRelationDirectionSelection.Outgoing,
            color is null ? [] : [color],
            requiredColor is null ? [] : [requiredColor],
            terminal);
        return Assert.IsType<AnnotatedTypeRelationsPlanningResult.Accepted>(
            catalog.Resolve(request, queryCatalog)).Plan;
    }

    private static AnnotatedTypeRelationsAdapterQueries Queries(
        InspectionQuery<AnnotatedTypeRelationsContribution> query,
        AnnotatedTypeRelationsTerminal terminal) =>
        terminal switch
        {
            AnnotatedTypeRelationsTerminal.Rows =>
                new(rows: query),
            AnnotatedTypeRelationsTerminal.Count =>
                new(count: query),
            AnnotatedTypeRelationsTerminal.Exists =>
                new(exists: query),
            _ => throw new ArgumentOutOfRangeException(nameof(terminal)),
        };

    private static InspectionQueryResults Results(
        InspectionQuery<AnnotatedTypeRelationsContribution> query,
        AnnotatedTypeRelationsContribution contribution)
    {
        InspectionQueryCatalog<AnnotatedTypeRelationsContribution> catalog =
            new InspectionQueryRegistry<AnnotatedTypeRelationsContribution>()
                .Add(query, static context => context)
                .Compile();
        return catalog.Plan(query).Run(contribution);
    }

    private static IInspectionQueryCatalog QueryCatalog(
        InspectionQuery<AnnotatedTypeRelationsContribution> query) =>
        new InspectionQueryRegistry<object>()
            .Add(
                query,
                static _ => throw new InvalidOperationException(
                    "Planning does not execute adapter queries."))
            .Compile();

    private static InspectionQuery<AnnotatedTypeRelationsContribution> Query(
        string name) =>
        new(name, InspectionCost.NetworkFree);

    private static InspectionGraphRelationshipDescriptor Relationship(
        string id) =>
        new(
            id,
            InspectionGraphOwner.Queries,
            InspectionGraphRelationshipSemantics.Observed,
            [InspectionGraphSubjectKind.Type],
            [InspectionGraphSubjectKind.Type],
            [InspectionGraphSubjectKind.Type],
            [InspectionGraphSubjectKind.Type],
            [
                new(
                    InspectionGraphSubjectKind.Type,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    InspectionGraphEndpointRole.Source),
            ],
            InspectionGraphEndpointProjection.Exact,
            OccurrenceIdentityProjection.Instance,
            [Evidence]);

    private static InspectionGraphCharacteristicDescriptor Color(string id) =>
        new(
            id,
            InspectionGraphOwner.Queries,
            InspectionGraphValueCatalog.Boolean,
            [InspectionGraphTargetKind.Edge],
            [],
            [InspectionGraphCharacteristicDerivationKind.Direct],
            InspectionGraphAggregationPolicy.None);

    private static Context CreateContext(string typeName = "Widget")
    {
        RealizedMemberCoordinate.Package coordinate =
            new(
                "sample.package",
                "1.0.0",
                "nuget-org",
                "net11.0",
                runtimeIdentifier: null);
        StructuralSubjectTestData.PackageContext package =
            StructuralSubjectTestData.Package(coordinate);
        StructuralSubjectIdentity.LibrarySubject library =
            StructuralSubjectIdentity.ForLibrary(
                package.Subject,
                Library(coordinate));
        SubjectRelationFocusAuthority focus =
            SubjectRelationFocusAuthority.Capture(
                StructuralSubjectIdentity.ForType(
                    library,
                    TypeName("Sample", typeName)));
        SubjectRelationPopulationAuthority population =
            SubjectRelationPopulationAuthority.Capture(
                package.Workspace,
                new object());
        return new(focus, population);
    }

    private static WorkspaceContextMember Library(
        RealizedMemberCoordinate.Package coordinate)
    {
        ResolvedAssemblyReference assembly = ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity(
                "Sample",
                new Version(1, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null),
            path: null,
            () => new MemoryStream([0], writable: false),
            AssemblyResolutionProvenance.Package(
                coordinate.PackageId,
                coordinate.Version,
                coordinate.Framework,
                coordinate.RuntimeIdentifier));
        return new WorkspaceContextMember(
            WorkspaceMemberCoordinate.Package(
                coordinate.PackageId,
                coordinate.Version,
                coordinate.Framework,
                coordinate.RuntimeIdentifier),
            coordinate,
            new AssemblyContextParticipant(
                assembly,
                NoResolverAssemblyBindingPolicy.Instance));
    }

    private static MetadataTypeDefinitionName TypeName(
        string @namespace,
        string name) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(@namespace, [name])).Name;

    private sealed class OccurrenceIdentityProjection :
        InspectionGraphOccurrenceIdentityProjection
    {
        public static OccurrenceIdentityProjection Instance { get; } = new();

        public override object Project(
            InspectionGraphOccurrence occurrence) =>
            ((OccurrenceEvidence)occurrence.Evidence).Identity;
    }

    private sealed record OccurrenceEvidence(object Identity)
        : IInspectionGraphOccurrenceEvidence
    {
        public InspectionGraphEvidenceDescriptor Descriptor => Evidence;
    }

    private sealed record Context(
        SubjectRelationFocusAuthority Focus,
        SubjectRelationPopulationAuthority Population);
}
