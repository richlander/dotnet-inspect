using CSharpText;
using DotnetInspector.Fixtures;
using ILInspector.Metadata;
using Inspector.Artifacts;

namespace ILInspector.Research.Tests;

public sealed class LibraryNameFamilySummaryTests
{
    [Fact]
    public void LibraryNameFamilies_PartitionsCompleteTypeInventory()
    {
        using FixtureExecution fixture = OpenFixture();
        LibraryNameFamilyDocument document = Execute(fixture);
        LibraryNameFamilyPopulation all = Population(
            document,
            LibraryNameFamilyPopulationKind.AllTypes);

        Assert.Equal(document.Types.Length, document.Receipt.TypeCount);
        Assert.Equal(document.Types.Length, all.TypeCount);
        Assert.Equal(
            document.Types.Length,
            all.OneWord.EligibleTypeCount
                + all.OneWord.ResidualTypeCount);
        Assert.Equal(
            document.Types.Length,
            all.TwoWord.EligibleTypeCount
                + all.TwoWord.ResidualTypeCount);
        Assert.Equal(
            all.OneWord.EligibleTypeCount,
            all.Families
                .Where(static family =>
                    family.Identity.Kind
                        == LibraryNameFamilyKind.OneWordSuffix)
                .Sum(static family => family.TypeCount));
        Assert.Equal(
            all.OneWord.ResidualTypeCount,
            all.OneWord.Residuals.Sum(static residual =>
                residual.Count));
        Assert.Equal(
            all.TwoWord.EligibleTypeCount,
            all.Families
                .Where(static family =>
                    family.Identity.Kind
                        == LibraryNameFamilyKind.TwoWordSuffix)
                .Sum(static family => family.TypeCount));
        Assert.Equal(
            all.TwoWord.ResidualTypeCount,
            all.TwoWord.Residuals.Sum(static residual =>
                residual.Count));
        Assert.Equal(
            Enumerable.Range(
                document.Types[0].Type.Definition.Value,
                document.Types.Length),
            document.Types.Select(static row =>
                row.Type.Definition.Value));

        LibraryNameFamilyTypeRow generic = Type(
            document,
            "GenericValidator`1");
        Assert.Equal("GenericValidator", generic.NameStem);
        Assert.Equal("Validator", generic.OneWordSuffix!.Words[0]);

        LibraryNameFamilyTypeRow numbered = Type(
            document,
            "DelegateInvoker1");
        Assert.Equal(
            [
                IdentifierWordSpanClassification.Word,
                IdentifierWordSpanClassification.Word,
                IdentifierWordSpanClassification.Ordinal,
            ],
            numbered.Spans.Select(static span =>
                span.Classification));
        Assert.Equal("Invoker", numbered.OneWordSuffix!.Words[0]);
        Assert.Equal(
            ["Delegate", "Invoker"],
            numbered.TwoWordSuffix!.Words);

        LibraryNameFamilyTypeRow unresolved = Type(document, "ZZQ1");
        Assert.Contains(
            unresolved.Spans,
            static span =>
                span.Classification
                    == IdentifierWordSpanClassification.Unresolved
                && span.Evidence.Kind
                    == IdentifierWordRuleKind.UnknownUppercaseRun);
        Assert.Null(unresolved.OneWordSuffix);
        Assert.Equal(
            LibraryNameFamilyOneWordResidualReason.UnresolvedTerminus,
            unresolved.OneWordResidual);

        LibraryNameFamilyRow oneBar = Family(
            all,
            LibraryNameFamilyKind.OneWordSuffix,
            ["Bar"]);
        Assert.Equal(2, oneBar.TypeCount);
        LibraryNameFamilyRow joinedBar = Family(
            all,
            LibraryNameFamilyKind.TwoWordSuffix,
            ["Foo", "Bar"],
            string.Empty);
        LibraryNameFamilyRow separatedBar = Family(
            all,
            LibraryNameFamilyKind.TwoWordSuffix,
            ["Foo", "Bar"],
            "_");
        Assert.NotEqual(
            joinedBar.Identity.Separator,
            separatedBar.Identity.Separator);

        LibraryNameFamilyRow validators = Family(
            all,
            LibraryNameFamilyKind.OneWordSuffix,
            ["Validator"]);
        Assert.True(validators.TypeCount >= 7);
        Assert.True(validators.DistinctNamespaceCount >= 2);
        Assert.Equal(
            validators.TypeCount,
            validators.Types.Distinct().Count());
        Assert.All(
            document.Types.Where(static type =>
                type.OneWordSuffix?.Words[0] == "Validator"),
            type => Assert.Equal(
                validators.Identity,
                type.OneWordSuffix));
    }

    [Fact]
    public void LibraryNameFamilies_PreservesExactMetadataIdentity()
    {
        using FixtureExecution fixture = OpenFixture();
        LibraryNameFamilyDocument document = Execute(fixture);

        LibraryNameFamilyTypeRow topLevel = Assert.Single(
            document.Types,
            static type =>
                type.MetadataSimpleName == "Validator"
                && type.Name.Segments.Length == 1);
        LibraryNameFamilyTypeRow nested = Assert.Single(
            document.Types,
            static type =>
                type.MetadataSimpleName == "Validator"
                && type.Name.Segments.Length == 2);
        Assert.NotEqual(topLevel.Type, nested.Type);
        Assert.Equal(topLevel.OneWordSuffix, nested.OneWordSuffix);

        LibraryNameFamilyTypeRow generic = Type(
            document,
            "GenericValidator`1");
        Assert.Equal("GenericValidator", generic.NameStem);
        Assert.Equal(
            "GenericValidator`1",
            generic.MetadataSimpleName);
    }

    [Fact]
    public void LibraryNameFamilies_PreserveWordRuleEvidence()
    {
        using FixtureExecution fixture = OpenFixture();
        LibraryNameFamilyDocument document = Execute(fixture);

        LibraryNameFamilyTypeRow numbered = Type(
            document,
            "DelegateInvoker1");
        IdentifierWordSpan ordinal = Assert.Single(
            numbered.Spans,
            static span =>
                span.Classification
                    == IdentifierWordSpanClassification.Ordinal);
        Assert.Equal(
            IdentifierWordRuleKind.NumberedFamilyOrdinal,
            ordinal.Evidence.Kind);
        Assert.Equal(
            ["Delegate", "Invoker"],
            numbered.TwoWordSuffix!.Words);

        LibraryNameFamilyTypeRow unresolved = Type(document, "ZZQ1");
        Assert.Contains(
            unresolved.Spans,
            static span =>
                span.Classification
                    == IdentifierWordSpanClassification.Unresolved
                && span.Evidence.Kind
                    == IdentifierWordRuleKind.UnknownUppercaseRun);
        Assert.Null(unresolved.OneWordSuffix);
        Assert.Null(unresolved.TwoWordSuffix);
    }

    [Fact]
    public void LibraryNameFamilies_SeparateExactSpellings()
    {
        using FixtureExecution fixture = OpenFixture();
        LibraryNameFamilyDocument document = Execute(fixture);
        LibraryNameFamilyPopulation all = Population(
            document,
            LibraryNameFamilyPopulationKind.AllTypes);

        Assert.NotEqual(
            Type(document, "CustomerValidator").OneWordSuffix,
            Type(document, "CustomerValidators").OneWordSuffix);
        Assert.NotEqual(
            Family(
                all,
                LibraryNameFamilyKind.TwoWordSuffix,
                ["Foo", "Bar"],
                string.Empty).Identity,
            Family(
                all,
                LibraryNameFamilyKind.TwoWordSuffix,
                ["Foo", "Bar"],
                "_").Identity);
    }

    [Fact]
    public void LibraryNameFamilies_SeparateSourcePopulations()
    {
        using FixtureExecution fixture = OpenFixture();
        LibraryNameFamilyDocument document = Execute(fixture);
        PdbSourceProvenanceResult provenance =
            Assert.IsType<PdbSourceProvenanceOutcome.Available>(
                fixture.Provenance).Result;

        Assert.Equal(
            LibraryNameFamilyProvenanceState.Available,
            document.Provenance.State);
        Assert.Same(
            provenance.Binding,
            document.Provenance.Binding);
        Assert.Equal(5, document.Populations.Length);
        Assert.Equal(
            provenance.Receipt.OrdinaryEvidenceOnlyCount,
            Population(
                document,
                LibraryNameFamilyPopulationKind
                    .OrdinaryEvidenceOnly).TypeCount);
        Assert.Equal(
            provenance.Receipt.GeneratedEvidenceOnlyCount,
            Population(
                document,
                LibraryNameFamilyPopulationKind
                    .GeneratedEvidenceOnly).TypeCount);
        Assert.Equal(
            provenance.Receipt.MixedEvidenceCount,
            Population(
                document,
                LibraryNameFamilyPopulationKind.MixedEvidence).TypeCount);
        Assert.Equal(
            provenance.Receipt.UnknownCount,
            Population(
                document,
                LibraryNameFamilyPopulationKind.Unknown).TypeCount);
        Assert.Equal(
            document.Types.Length,
            document.Populations
                .Where(static population =>
                    population.Kind
                        != LibraryNameFamilyPopulationKind.AllTypes)
                .Sum(static population => population.TypeCount));
        Assert.All(
            document.Types,
            static row => Assert.NotNull(row.SourceEvidence));

        Assert.Equal(
            PdbTypeSourceDisposition.MixedEvidence,
            Type(document, "MixedValidator").SourceEvidence!.Disposition);
        Assert.Equal(
            PdbTypeSourceDisposition.Unknown,
            Type(document, "UnknownValidator").SourceEvidence!.Disposition);
        Assert.Contains(
            document.Types,
            static row =>
                row.SourceEvidence!.Disposition
                    == PdbTypeSourceDisposition.GeneratedEvidenceOnly);
        Assert.Contains(
            document.Types,
            static row =>
                row.SourceEvidence!.Disposition
                    == PdbTypeSourceDisposition.OrdinaryEvidenceOnly);
    }

    [Fact]
    public void LibraryNameFamilies_RejectMismatchedProvenanceWithoutLosingAllTypes()
    {
        using FixtureExecution fixture = OpenFixture();
        using FixtureExecution foreign = OpenFixture();

        LibraryNameFamilyDocument document = Execute(
            fixture,
            foreign.Provenance);

        Assert.Equal(
            LibraryNameFamilyProvenanceState.Rejected,
            document.Provenance.State);
        Assert.Equal(
            LibraryNameFamilyProvenanceRejection.ArtifactMismatch,
            document.Provenance.Rejection);
        Assert.Single(document.Populations);
        Assert.Equal(
            LibraryNameFamilyPopulationKind.AllTypes,
            document.Populations[0].Kind);
        Assert.All(
            document.Types,
            static row => Assert.Null(row.SourceEvidence));
    }

    [Fact]
    public void LibraryNameFamilies_RejectMismatchedArtifactSession()
    {
        using FixtureExecution fixture = OpenFixture();
        using FixtureExecution foreign = OpenFixture();

        var rejected =
            Assert.IsType<LibraryNameFamilySummaryOutcome.Rejected>(
                LibraryNameFamilySummary.Execute(
                    fixture.Assembly,
                    foreign.Session));

        Assert.Equal(
            LibraryNameFamilyRejectionReason.ArtifactIdentityMismatch,
            rejected.Reason);
    }

    [Fact]
    public void LibraryNameFamilies_RequireArtifactBackedAssembly()
    {
        byte[] image = File.ReadAllBytes(
            FixtureCatalog.ResearchNameFamilies.AssemblyPath());
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromStreamIfManaged(
                () => new MemoryStream(image, writable: false),
                AssemblyResolutionProvenance.Local("unbound-fixture"))
            ?? throw new InvalidOperationException(
                "The fixture must contain managed metadata.");
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(assembly);

        var unavailable =
            Assert.IsType<LibraryNameFamilySummaryOutcome.Unavailable>(
                LibraryNameFamilySummary.Execute(assembly, session));

        Assert.Equal(
            LibraryNameFamilyUnavailableReason.MissingArtifactBinding,
            unavailable.Reason);
    }

    [Fact]
    public void LibraryNameFamilies_PreserveUnavailableProvenanceQualification()
    {
        using FixtureExecution fixture = OpenFixture();
        var unavailable = new PdbSourceProvenanceOutcome.Unavailable(
            PdbSourceProvenanceUnavailableReason.PdbNotLoaded,
            "No matching PDB was loaded.");

        LibraryNameFamilyDocument document = Execute(
            fixture,
            unavailable);

        Assert.Equal(
            LibraryNameFamilyProvenanceState.Unavailable,
            document.Provenance.State);
        Assert.Equal(
            PdbSourceProvenanceUnavailableReason.PdbNotLoaded,
            document.Provenance.UnavailableReason);
        Assert.Single(document.Populations);
    }

    [Fact]
    public void LibraryNameFamilies_RejectIncompleteOrMismatchedInventory()
    {
        using FixtureExecution fixture = OpenFixture();

        var unavailable =
            Assert.IsType<LibraryNameFamilySummaryOutcome.Unavailable>(
                LibraryNameFamilySummary.Execute(
                    fixture.Assembly,
                    fixture.Session,
                    limits: new(maximumRetainedTypes: 1)));

        Assert.Equal(
            LibraryNameFamilyUnavailableReason.IncompleteTypeInventory,
            unavailable.Reason);
    }

    [Fact]
    public void LibraryNameFamilies_IsDeterministic()
    {
        using FixtureExecution fixture = OpenFixture();

        LibraryNameFamilyDocument first = Execute(fixture);
        LibraryNameFamilyDocument second = Execute(fixture);

        Assert.Equal(
            first.Types.Select(TypeProjection),
            second.Types.Select(TypeProjection));
        Assert.Equal(
            first.Populations.SelectMany(PopulationProjection),
            second.Populations.SelectMany(PopulationProjection));
    }

    private static object TypeProjection(LibraryNameFamilyTypeRow row) =>
        new
        {
            row.Type,
            row.MetadataSimpleName,
            row.NameStem,
            Spans = string.Join(
                ";",
                row.Spans.Select(static span =>
                    $"{span.Start}:{span.Length}:{span.Text}:"
                    + $"{span.Classification}:{span.Evidence}")),
            One = FamilyProjection(row.OneWordSuffix),
            Two = FamilyProjection(row.TwoWordSuffix),
            row.OneWordResidual,
            row.TwoWordResidual,
            Source = row.SourceEvidence?.Disposition,
        };

    private static IEnumerable<object> PopulationProjection(
        LibraryNameFamilyPopulation population) =>
        population.Families.Select(family => new
        {
            Population = population.Kind,
            Identity = FamilyProjection(family.Identity),
            family.TypeCount,
            family.PublicTypeCount,
            family.DistinctNamespaceCount,
            Types = string.Join(
                ",",
                family.Types.Select(static type =>
                    type.Definition.Value)),
        });

    private static string? FamilyProjection(
        LibraryNameFamilyIdentity? identity) =>
        identity is null
            ? null
            : $"{identity.Kind}:{string.Join("|", identity.Words)}:"
                + identity.Separator;

    private static LibraryNameFamilyDocument Execute(
        FixtureExecution fixture,
        PdbSourceProvenanceOutcome? provenance = null)
    {
        LibraryNameFamilySummaryOutcome outcome =
            LibraryNameFamilySummary.Execute(
                fixture.Assembly,
                fixture.Session,
                provenance: provenance ?? fixture.Provenance);
        return Assert.IsType<LibraryNameFamilySummaryOutcome.Available>(
            outcome).Document;
    }

    private static LibraryNameFamilyPopulation Population(
        LibraryNameFamilyDocument document,
        LibraryNameFamilyPopulationKind kind) =>
        Assert.Single(
            document.Populations,
            population => population.Kind == kind);

    private static LibraryNameFamilyTypeRow Type(
        LibraryNameFamilyDocument document,
        string metadataSimpleName) =>
        Assert.Single(
            document.Types,
            type => type.MetadataSimpleName == metadataSimpleName);

    private static LibraryNameFamilyRow Family(
        LibraryNameFamilyPopulation population,
        LibraryNameFamilyKind kind,
        string[] words,
        string? separator = null) =>
        Assert.Single(
            population.Families,
            family =>
                family.Identity.Kind == kind
                && family.Identity.Words.SequenceEqual(words)
                && family.Identity.Separator == separator);

    private static FixtureExecution OpenFixture()
    {
        byte[] image = File.ReadAllBytes(
            FixtureCatalog.ResearchNameFamilies.AssemblyPath());
        var authority = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission =
            authority.CreateAdmissionAuthorization();
        ArtifactContribution contribution;
        using (ArtifactContributionScope scope =
               authority.BeginContribution(admission))
        {
            contribution = scope.Register(
                TestArtifactProvenance.Instance,
                _ => new MemoryStream(image, writable: false));
        }
        authority.CreateRetainedContent(
            contribution.Registration,
            _ => new MemoryStream(image, writable: false));
        authority.CompleteAdmission(admission);
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromArtifactIfManaged(
                contribution.Registration,
                () => new MemoryStream(image, writable: false),
                AssemblyResolutionProvenance.Local("name-family-fixture"))
            ?? throw new InvalidOperationException(
                "The fixture must contain managed metadata.");
        PdbContext context = PdbContext.OpenEmbeddedPdbOnly(assembly);
        PdbSourceProvenanceOutcome provenance =
            context.InspectSourceProvenance();
        AssemblyInspectionSession session =
            AssemblyInspectionSession.Borrow(context);
        return new(authority, assembly, context, session, provenance);
    }

    private sealed record FixtureExecution(
        ArtifactGenerationAuthority Authority,
        ResolvedAssemblyReference Assembly,
        PdbContext Context,
        AssemblyInspectionSession Session,
        PdbSourceProvenanceOutcome Provenance) : IDisposable
    {
        public void Dispose()
        {
            Session.Dispose();
            Context.Dispose();
        }
    }

    private sealed class TestArtifactProvenance : IArtifactProvenance
    {
        public static TestArtifactProvenance Instance { get; } = new();
    }
}
