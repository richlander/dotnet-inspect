using DotnetInspector.Commands;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Sections;
using ILInspector.Findings;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System.Text;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Tests;

// Runs isolated from all other collections so the shared MethodBodyInspectionSession
// open counter is reliable (no concurrent session opens from parallel tests).
[CollectionDefinition("IndexBuildGuard", DisableParallelization = true)]
public class IndexBuildGuardCollection;

/// <summary>
/// Guards the "build the analysis index once per command" invariant delivered by the #2139 perf
/// work (PRs #2187 member, #2199 type, #2210 library): every index-backed section of one command
/// shares a single <see cref="Analysis.LibraryBodyIndex"/> build. A new section that opens its own
/// <see cref="MethodBodyInspectionSession"/> instead of the shared one would silently reintroduce a
/// per-section rebuild — these tests fail immediately if that happens.
/// </summary>
[Collection("IndexBuildGuard")]
public class IndexBuildInvariantTests
{
    static string FixtureAssembly => typeof(IndexBuildGuardFixture).Assembly.Location;

    static (string AssemblyPath, string Directory)
        CreateEmbeddedPdbAssembly()
    {
        const string source =
            """
            namespace EmbeddedPdbFixture;

            public static class Sample
            {
                public static int Value() => 42;
            }
            """;
        string directory = Path.Combine(
            AppContext.BaseDirectory,
            $"embedded-pdb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string assemblyPath =
                Path.Combine(directory, "EmbeddedPdbFixture.dll");
            var references =
                ((string)AppContext.GetData(
                    "TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path =>
                    MetadataReference.CreateFromFile(path));
            var compilation = CSharpCompilation.Create(
                "EmbeddedPdbFixture",
                [
                    CSharpSyntaxTree.ParseText(
                        SourceText.From(source, Encoding.UTF8),
                        new CSharpParseOptions(
                            LanguageVersion.Preview),
                        path: "/_/EmbeddedPdbFixture.cs")
                ],
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    deterministic: true));
            using var assembly = File.Create(assemblyPath);
            using var sourceLink = new MemoryStream(
                Encoding.UTF8.GetBytes(
                    """{"documents":{"/_/*":"https://example.test/*"}}"""));
            EmitResult result = compilation.Emit(
                assembly,
                sourceLinkStream: sourceLink,
                options: new EmitOptions(
                    debugInformationFormat:
                        DebugInformationFormat.Embedded,
                    pdbFilePath: "EmbeddedPdbFixture.pdb"));
            Assert.True(
                result.Success,
                string.Join(
                    Environment.NewLine,
                    result.Diagnostics));
            return (assemblyPath, directory);
        }
        catch
        {
            Directory.Delete(
                directory,
                recursive: true);
            throw;
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task MemberCommand_MultipleIndexSections_BuildsIndexOnce()
    {
        MethodBodyInspectionSession.OpenCountForTests = 0;

        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(IndexBuildGuardFixture).FullName,
            AssemblyPath = FixtureAssembly,
            MemberFilter = [nameof(IndexBuildGuardFixture.Work)],
            IncludeSections =
            [
                SectionNames.Calls,
                SectionNames.CallGraph,
                SectionNames.UnsafeOperations,
                SectionNames.AllocationFacts,
                SectionNames.SafetyFacts,
                SectionNames.CostFacts,
            ],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Detailed,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, MethodBodyInspectionSession.OpenCountForTests);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TypeCommand_MultipleAnalysisSections_BuildsIndexOnce()
    {
        MethodBodyInspectionSession.OpenCountForTests = 0;

        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeof(IndexBuildGuardFixture).FullName,
            AssemblyPath = FixtureAssembly,
            IncludeSections =
            [
                SectionNames.UnsafeMembers,
                SectionNames.CalledTypes,
                SectionNames.TopLeverage,
                SectionNames.AllocationFacts,
                SectionNames.SafetyFacts,
                SectionNames.CostFacts,
                SectionNames.PerformanceTriage,
            ],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, MethodBodyInspectionSession.OpenCountForTests);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task LibraryCommand_MultipleAnalysisSections_BuildsIndexOnce()
    {
        MethodBodyInspectionSession.OpenCountForTests = 0;

        var result = await ConsoleCapture.RunAsync(() => LibraryCommand.ExecuteAsync(new LibraryOptions
        {
            AssemblyName = FixtureAssembly,
            IncludeSections =
            [
                SectionNames.UnsafeMembers,
                SectionNames.TopLeverage,
                SectionNames.PerformanceTriage,
                SectionNames.ArrayPoolEscapes,
            ],
            Markdown = true,
            Rows = RowWindow.Head(25),
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, MethodBodyInspectionSession.OpenCountForTests);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void DiffCommand_AnalysisInputs_OpenOneSessionPerEndpointAssembly()
    {
        MethodBodyInspectionSession.OpenCountForTests = 0;

        var input = DiffCommand.CreateBodySignalComparisonInput(
            [FixtureAssembly],
            [FixtureAssembly],
            new DiffOptions());

        Assert.Single(input.OldIndexes);
        Assert.Single(input.NewIndexes);
        Assert.Equal(2, MethodBodyInspectionSession.OpenCountForTests);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void DiffCommand_ImplementationInputs_OpenOneSessionPerEndpointAssembly()
    {
        MethodBodyInspectionSession.OpenCountForTests = 0;

        var input = DiffCommand.CreateImplementationComparisonInput(
            [FixtureAssembly],
            [FixtureAssembly],
            new DiffOptions());

        Assert.Single(input.OldAssemblies);
        Assert.Single(input.NewAssemblies);
        Assert.Equal(2, MethodBodyInspectionSession.OpenCountForTests);
    }

    [Fact]
    public void TimelineCommand_AnalysisInspection_OpensTargetedSession()
    {
        MethodBodyInspectionSession.OpenCountForTests = 0;

        FindingInspection<Analysis.UnsafetyOccurrence> inspection =
            TimelineCommand.InspectUnsafetyAssemblies(
                [FixtureAssembly],
                typeof(IndexBuildGuardFixture).FullName!,
                nameof(IndexBuildGuardFixture.Work));

        Assert.IsType<
            FindingInspection<Analysis.UnsafetyOccurrence>.Complete>(
                inspection.Value);
        Assert.Equal(1, MethodBodyInspectionSession.OpenCountForTests);
    }

    [Fact]
    public void TimelineCommand_AnalysisSession_HonorsCapabilitiesAndBodyScope()
    {
        Analysis.MethodIdentity target = Analysis.LibraryBodyIndex
            .Open(
                FixtureAssembly,
                includeAllocations: false,
                includeOpportunities: false)
            .DeclaredMethods
            .Single(method =>
                method.DeclaringType.Name
                    == nameof(IndexBuildGuardFixture)
                && method.Name
                    == nameof(IndexBuildGuardFixture.Work));
        MethodBodyInspectionSession.OpenCountForTests = 0;

        MethodBodyInspectionSession session =
            TimelineCommand.OpenAnalysisSession(
                FixtureAssembly,
                Analysis.AnalysisFindings.AllocationDescriptor,
                target.MetadataToken);

        Assert.Equal(
            Analysis.LibraryBodyAnalysisFeatures.MethodEvidence
                | Analysis.LibraryBodyAnalysisFeatures.Allocations,
            session.BodyIndex.Features);
        Assert.NotEmpty(
            session.BodyIndex.GetAllocationOccurrences()[target.MetadataToken]);
        Assert.Equal(
            [target.MetadataToken],
            session.BodyIndex.GetDirectCallsByCaller().Keys);

        MethodBodyInspectionSession unsafetySession =
            TimelineCommand.OpenAnalysisSession(
                FixtureAssembly,
                Analysis.AnalysisFindings.UnsafetyDescriptor,
                target.MetadataToken);

        Assert.Equal(
            Analysis.LibraryBodyAnalysisFeatures.MethodEvidence,
            unsafetySession.BodyIndex.Features);
        Assert.Empty(unsafetySession.BodyIndex.GetAllocationOccurrences());
        Assert.Equal(2, MethodBodyInspectionSession.OpenCountForTests);
    }

    [Fact]
    public void PdbContext_RequiresPrefetchForSharedParallelBodyAnalysis()
    {
        using var lazy =
            ILInspector.SourceLink.SourceLinkService.Open(FixtureAssembly);
        Assert.Throws<InvalidOperationException>(
            () => lazy.Context.GetPrefetchedImage());

        using var prefetched =
            ILInspector.SourceLink.SourceLinkService.OpenPrefetched(FixtureAssembly);
        var image = prefetched.Context.GetPrefetchedImage();
        Assert.False(image.IsDefaultOrEmpty);
        using var reader =
            new System.Reflection.PortableExecutable.PEReader(image);
        Assert.True(reader.HasMetadata);
    }

    [Fact]
    public void PdbContext_EmbeddedOnlyPrefetch_RetainsImageWithoutLoadingAdjacentPdb()
    {
        var limits = new ILInspector.SourceLink.SourceLinkReadLimits(
            LibraryMetadataService.DiscoveryMaxEmbeddedPdbBytes,
            maxMapBytes: 4 * 1024 * 1024,
            maxMappings: 16 * 1024);
        Assert.True(
            File.Exists(
                Path.ChangeExtension(
                    FixtureAssembly,
                    ".pdb")),
            "The fixture must have an adjacent PDB for this gate to prove it stays unloaded.");

        using var prefetched =
            ILInspector.SourceLink.SourceLinkService
                .OpenEmbeddedPdbOnlyPrefetched(
                    FixtureAssembly,
                    limits);
        var image =
            prefetched.Context.GetPrefetchedImage();

        Assert.False(image.IsDefaultOrEmpty);
        Assert.False(prefetched.HasPdb);
        using var reader =
            new System.Reflection.PortableExecutable.PEReader(
                image);
        Assert.True(reader.HasMetadata);

        var descriptor =
            ILInspector.Metadata.ResolvedAssemblyReference
                .CreateFromPath(
                    FixtureAssembly,
                    ILInspector.Metadata
                        .AssemblyResolutionProvenance
                        .Local("embedded-only prefetch gate"));
        using var descriptorPrefetched =
            ILInspector.SourceLink.SourceLinkService
                .OpenEmbeddedPdbOnlyPrefetched(
                    descriptor,
                    limits);
        Assert.False(
            descriptorPrefetched.Context
                .GetPrefetchedImage()
                .IsDefaultOrEmpty);
        Assert.False(descriptorPrefetched.HasPdb);

        var (embeddedAssembly, embeddedDirectory) =
            CreateEmbeddedPdbAssembly();
        try
        {
            using (var embedded =
                ILInspector.SourceLink.SourceLinkService.Open(
                    embeddedAssembly))
            {
                Assert.True(
                    embedded.Context.HasEmbeddedPdb);
                Assert.True(embedded.HasPdb);
                Assert.True(embedded.HasSourceLink);
            }

            using var embeddedPrefetched =
                ILInspector.SourceLink.SourceLinkService
                    .OpenEmbeddedPdbOnlyPrefetched(
                        embeddedAssembly,
                        limits);
            Assert.True(
                embeddedPrefetched.Context.HasEmbeddedPdb);
            Assert.True(embeddedPrefetched.HasPdb);
            Assert.True(embeddedPrefetched.HasSourceLink);
        }
        finally
        {
            Directory.Delete(
                embeddedDirectory,
                recursive: true);
        }
    }

    [Fact]
    public void
        UnsafeEvidencePresenceQuery_ConsumesBorrowedNonPrefetchedContext()
    {
        using var context =
            ILInspector.Metadata.PdbContext
                .OpenEmbeddedPdbOnly(
                    FixtureAssembly,
                    maxEmbeddedPdbBytes: 8 * 1024 * 1024);
        Assert.Throws<InvalidOperationException>(
            () => context.GetPrefetchedImage());

        DotnetInspector.Queries.UnsafeEvidencePresenceResult
            result =
                DotnetInspector.Queries
                    .UnsafeEvidencePresenceQuery.Execute(
                        FixtureAssembly,
                        context);

        Assert.IsType<
            DotnetInspector.Queries
                .UnsafeEvidencePresenceResult.Available>(
                    result);
        Assert.Throws<InvalidOperationException>(
            () => context.GetPrefetchedImage());
    }
}

public class IndexBuildGuardFixture
{
    // A body with a call and an allocation so the requested analysis sections have data to render.
    public void Work()
    {
        var list = new List<int>();
        Helper(list);
        System.Console.WriteLine(list.Count);
    }

    public void Helper(List<int> items) => items.Add(1);
}
