using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspector.Queries;
using DotnetInspector.RoundTripCompilation;
using ILInspector.Findings;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Research;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using DecompilerMetadataSource = ILInspector.Decompiler.Pipeline.MetadataSource;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "RoundTrip")]
public sealed class RoundTripComparisonTests
{
    static string AssemblyPath => typeof(RoundTripComparisonTests).Assembly.Location;

    [Fact]
    public void Compare_ReportsExactCSharpAndIlForSameArtifact()
    {
        var request = CreateRequest();

        var result = RoundTripComparison.Compare(request, File.ReadAllBytes(AssemblyPath));

        Assert.Equal(RoundTripComparisonStatus.Completed, result.Status);
        var member = Assert.Single(result.Members);
        Assert.Equal(MethodCorrespondenceStatus.Exact, member.Correspondence.Status);
        Assert.Equal(RoundTripEvidenceStatus.Exact, member.CSharpStatus);
        Assert.Equal(IlBodyDiffOutcome.Exact, member.IlStatus);
        AssertRetainedEvidence(member);
        string json = JsonSerializer.Serialize(result);
        Assert.Contains("\"cSharpStatus\":0", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"\"token\":{request.Targets[0].Method.Token}", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"evidence\":", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compare_ReportsChangedEvidenceForRecompiledBody()
    {
        var request = CreateRequest();
        var donor = Compile("""
            namespace ILInspector.Decompiler.Tests;
            public sealed class RoundTripComparisonFixture
            {
                public int Transform(int value) => value + 2;
            }
            """);

        var result = RoundTripComparison.Compare(request, donor);

        Assert.Equal(RoundTripComparisonStatus.Completed, result.Status);
        var member = Assert.Single(result.Members);
        Assert.Equal(MethodCorrespondenceStatus.Exact, member.Correspondence.Status);
        Assert.Equal(RoundTripEvidenceStatus.Changed, member.CSharpStatus);
        Assert.NotEqual(IlBodyDiffOutcome.Exact, member.IlStatus);
        AssertRetainedEvidence(member);
    }

    [Fact]
    public void Compare_PreservesAbsentCorrespondenceAsUnavailable()
    {
        var request = CreateRequest();
        var donor = Compile("public sealed class Other { public int Transform(int value) => value; }");

        var result = RoundTripComparison.Compare(request, donor);

        Assert.Equal(RoundTripComparisonStatus.Completed, result.Status);
        var member = Assert.Single(result.Members);
        Assert.Equal(MethodCorrespondenceStatus.Absent, member.Correspondence.Status);
        Assert.Equal(RoundTripEvidenceStatus.Unavailable, member.CSharpStatus);
        Assert.Equal(IlBodyDiffOutcome.Unavailable, member.IlStatus);
        Assert.Null(member.Evidence);
    }

    [Fact]
    public void Compare_FailsWhenInputBytesNoLongerMatchRequest()
    {
        var valid = CreateRequest();
        var request = RoundTripRequest.Create(
            new RoundTripArtifactIdentity(AssemblyPath, new string('0', 64), "test"),
            valid.Module,
            valid.Targets,
            valid.Scope,
            valid.BodyPolicy,
            valid.Replacements);

        var result = RoundTripComparison.Compare(request, File.ReadAllBytes(AssemblyPath));

        Assert.Equal(RoundTripComparisonStatus.Failed, result.Status);
        Assert.Contains("content hash", result.Failure);
        Assert.Empty(result.Members);
    }

    [Fact]
    public void Compare_PreservesBodylessEndpointAsUnavailable()
    {
        var donor = Compile("""
            namespace ILInspector.Decompiler.Tests;
            public abstract class RoundTripComparisonFixture
            {
                public abstract int Transform(int value);
            }
            """);

        var result = RoundTripComparison.Compare(CreateRequest(), donor);

        Assert.Equal(RoundTripComparisonStatus.Completed, result.Status);
        var member = Assert.Single(result.Members);
        Assert.Equal(MethodCorrespondenceStatus.Exact, member.Correspondence.Status);
        Assert.Equal(RoundTripEvidenceStatus.Unavailable, member.CSharpStatus);
        Assert.Equal(IlBodyDiffOutcome.Unavailable, member.IlStatus);
        Assert.Null(member.CSharpDiff);
        Assert.NotEmpty(member.CSharpFailure!);
        Assert.Contains("new absent:", member.IlFailure);
        ResearchProducerCompletion completion = Completion(member.Evidence);
        var native = Assert.IsType<ResearchProducerWorkOutcome.ProducedCSharp>(
            completion.Results.Single(result => result.Item.Producer == ResearchProducerKind.CSharp).Outcome).Result;
        Assert.Null(native.BodyDiff);
        var absent = Assert.IsType<FindingInspection<CSharpCanonicalLine>.Absent>(
            native.Findings.NewInspection.Value);
        Assert.Equal(FindingInspectionAbsenceKind.NoApplicableInput, absent.Kind);
    }

    [Fact]
    public void Compare_PreservesMalformedBodyAsUnavailable()
    {
        byte[] donor = Compile(DonorSource("value + 1"));
        using (var pe = new PEReader(new MemoryStream(donor, writable: false)))
        {
            var reader = pe.GetMetadataReader();
            int rva = reader.GetMethodDefinition(FindMethod(
                reader, nameof(RoundTripComparisonFixture), nameof(RoundTripComparisonFixture.Transform)))
                .RelativeVirtualAddress;
            var section = pe.PEHeaders.SectionHeaders.Single(section =>
                rva >= section.VirtualAddress && rva < section.VirtualAddress + section.SizeOfRawData);
            donor[section.PointerToRawData + rva - section.VirtualAddress] = 0;
        }

        var result = RoundTripComparison.Compare(CreateRequest(), donor);

        Assert.Equal(RoundTripComparisonStatus.Completed, result.Status);
        var member = Assert.Single(result.Members);
        Assert.Equal(MethodCorrespondenceStatus.Exact, member.Correspondence.Status);
        Assert.Equal(RoundTripEvidenceStatus.Unavailable, member.CSharpStatus);
        Assert.Equal(IlBodyDiffOutcome.Unavailable, member.IlStatus);
        Assert.NotNull(member.Evidence);
        Assert.NotEmpty(member.CSharpFailure!);
        Assert.NotEmpty(member.IlFailure!);
    }

    [Fact]
    public void Compare_PreservesFailedCorrespondenceAsUnavailable()
    {
        var valid = CreateRequest();
        Guid wrongModule = Guid.NewGuid();
        var request = RoundTripRequest.Create(
            valid.Artifact,
            valid.Module with { ModuleVersionId = wrongModule },
            [valid.Targets[0] with { Method = valid.Targets[0].Method with { ModuleVersionId = wrongModule } }],
            valid.Scope, valid.BodyPolicy, valid.Replacements);

        var result = RoundTripComparison.Compare(request, File.ReadAllBytes(AssemblyPath));

        Assert.Equal(RoundTripComparisonStatus.Completed, result.Status);
        var member = Assert.Single(result.Members);
        Assert.Equal(MethodCorrespondenceStatus.Failed, member.Correspondence.Status);
        Assert.Equal(RoundTripEvidenceStatus.Unavailable, member.CSharpStatus);
        Assert.Equal(IlBodyDiffOutcome.Unavailable, member.IlStatus);
        Assert.Null(member.Evidence);
        Assert.Equal(member.Correspondence.Failure, member.CSharpFailure);
    }

    [Fact]
    public void QueryComparison_RetainsRejectedDesignation()
    {
        using var original = DecompilerMetadataSource.OpenWithoutSymbols(AssemblyPath);
        using var donor = DecompilerMetadataSource.OpenWithoutSymbols(AssemblyPath);
        using var workspace = new InspectionWorkspace();
        var query = new RoundTripComparisonQuery(workspace, original, donor);
        var target = CreateRequest().Targets[0].Method;

        var result = query.Compare(target, target with { ModuleVersionId = Guid.NewGuid() });

        Assert.Equal(RoundTripEvidenceStatus.Unavailable, result.CSharpStatus);
        Assert.Equal(IlBodyDiffOutcome.Unavailable, result.IlStatus);
        var failure = Assert.IsType<LocalComparisonQueryResult.NonSuccess>(result.Evidence);
        Assert.Equal(QueryComparisonSide.After, failure.Side);
        Assert.IsType<LocalComparisonQueryFailure.DesignationUnavailable>(failure.Failure);
        Assert.NotEmpty(result.CSharpFailure!);
        Assert.Null(result.CSharpDiff);
        Assert.Null(result.IlDiff);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CSharpRoundTripChangedRejectsFailureRows(bool identityFailure, bool includeChanges)
    {
        var request = CreateRequest();
        var member = Assert.Single(RoundTripComparison.Compare(
            request, Compile(DonorSource("value + 2"))).Members);
        var native = Assert.IsType<ResearchProducerWorkOutcome.ProducedCSharp>(
            Completion(member.Evidence).Results
                .Single(result => result.Item.Producer == ResearchProducerKind.CSharp).Outcome).Result;
        var diff = new CSharpBodyDiffResult(
            includeChanges ? native.BodyDiff!.Rows : [],
            identityFailure ? [] :
            [new("", "", request.Targets[0].Anchor, "Transform",
                CSharpDiffFailureKind.BodyDiffSkipped, "producer diff failed")],
            identityFailure ?
            [new("new", "", 0, default, "identity", "identity resolution failed")] : []);
        var outcome = new CSharpMemberEndpointComparison(
            native.Old, native.New, native.Findings, diff);

        var result = RoundTripComparisonQuery.ClassifyCSharp(outcome);

        Assert.Equal(RoundTripEvidenceStatus.Unavailable, result.Status);
        Assert.Contains(identityFailure ? "identity resolution failed" : "producer diff failed", result.Failure);
    }

    [Fact]
    public void ScopeCompare_ComparesClusterAndAllDonorsDirectly()
    {
        var clusterRequest = CreateRequest();
        var allRequest = WithScope(clusterRequest, RoundTripScope.All);
        var cluster = CompileResult(DonorSource("value + 1"));
        var all = CompileResult(DonorSource("value + 1", includeUnrelated: true));

        var result = RoundTripScopeComparison.Compare(
            clusterRequest,
            cluster.Provenance,
            cluster.PeImage!,
            allRequest,
            all.Provenance,
            all.PeImage!);

        Assert.Equal(RoundTripScopeComparisonStatus.Completed, result.Status);
        var member = Assert.Single(result.Members);
        Assert.Equal(RoundTripEvidenceStatus.Exact, member.CSharpStatus);
        Assert.Equal(IlBodyDiffOutcome.Exact, member.IlStatus);
        Assert.NotNull(result.Cluster);
        Assert.NotNull(result.All);
        var direct = Completion(member.Evidence);
        var pair = Assert.IsType<ResearchProducerWorkBasis.DesignatedPair>(direct.WorkItems[0].Basis).Pair;
        Assert.Equal(Assert.Single(result.Cluster.Members).Correspondence.Target,
            Assert.IsType<ResearchTargetOutcome.Resolved>(pair.Before.Outcome).Address);
        Assert.Equal(Assert.Single(result.All.Members).Correspondence.Target,
            Assert.IsType<ResearchTargetOutcome.Resolved>(pair.After.Outcome).Address);
    }

    [Fact]
    public void ScopeCompare_ReportsCleanDirectDonorDifference()
    {
        var clusterRequest = CreateRequest();
        var allRequest = WithScope(clusterRequest, RoundTripScope.All);
        var cluster = CompileResult(DonorSource("value + 1"));
        var all = CompileResult(DonorSource("value + 2", includeUnrelated: true));

        var result = RoundTripScopeComparison.Compare(
            clusterRequest,
            cluster.Provenance,
            cluster.PeImage!,
            allRequest,
            all.Provenance,
            all.PeImage!);

        Assert.Equal(RoundTripScopeComparisonStatus.Completed, result.Status);
        var member = Assert.Single(result.Members);
        Assert.Equal(RoundTripEvidenceStatus.Changed, member.CSharpStatus);
        Assert.NotEqual(IlBodyDiffOutcome.Exact, member.IlStatus);
    }

    [Fact]
    public void ScopeCompare_RejectsCompilerContextMismatch()
    {
        var clusterRequest = CreateRequest();
        var allRequest = WithScope(clusterRequest, RoundTripScope.All);
        var cluster = CompileResult(DonorSource("value + 1"), OptimizationLevel.Release);
        var all = CompileResult(DonorSource("value + 1", includeUnrelated: true), OptimizationLevel.Debug);

        var result = RoundTripScopeComparison.Compare(
            clusterRequest,
            cluster.Provenance,
            cluster.PeImage!,
            allRequest,
            all.Provenance,
            all.PeImage!);

        Assert.Equal(RoundTripScopeComparisonStatus.Unavailable, result.Status);
        Assert.Contains("compiler or reference context differs", result.Failure);
        Assert.Null(result.Cluster);
        Assert.Empty(result.Members);
    }

    [Fact]
    public void ScopeCompare_RejectsReferenceContentMismatch()
    {
        var clusterRequest = CreateRequest();
        var allRequest = WithScope(clusterRequest, RoundTripScope.All);
        var cluster = CompileResult(DonorSource("value + 1"));
        var all = CompileResult(DonorSource("value + 1", includeUnrelated: true));
        var changedReference = all.Provenance.References[0] with { Sha256 = new string('0', 64) };
        var changedContext = all.Provenance with
        {
            References = all.Provenance.References.SetItem(0, changedReference),
        };

        var result = RoundTripScopeComparison.Compare(
            clusterRequest,
            cluster.Provenance,
            cluster.PeImage!,
            allRequest,
            changedContext,
            all.PeImage!);

        Assert.Equal(RoundTripScopeComparisonStatus.Unavailable, result.Status);
        Assert.Contains("compiler or reference context differs", result.Failure);
        Assert.Empty(result.Members);
    }

    static RoundTripRequest CreateRequest()
    {
        using var image = new MetadataImage(AssemblyPath);
        var methodHandle = FindMethod(
            image.Reader,
            nameof(RoundTripComparisonFixture),
            nameof(RoundTripComparisonFixture.Transform));
        var method = image.Reader.GetMethodDefinition(methodHandle);
        var typeHandle = method.GetDeclaringType();
        var anchor = ApiMemberIdentity.CreateMethodAnchor(
            image.Reader,
            typeHandle,
            method);
        var module = image.Reader.GetModuleDefinition();
        var mvid = image.Reader.GetGuid(module.Mvid);
        return RoundTripRequest.Create(
            RoundTripArtifactIdentity.FromFile(AssemblyPath, "test"),
            new RoundTripModuleIdentity(image.Reader.GetString(module.Name), mvid),
            [new RoundTripTarget(MetadataMethodAddress.Create(image.Reader, methodHandle), anchor)],
            RoundTripScope.Cluster,
            RoundTripBodyPolicy.Selected);
    }

    static ResearchProducerCompletion Completion(LocalComparisonQueryResult? evidence)
        => Assert.IsType<ResearchProducerSessionOutcome.Completed>(
            Assert.IsType<LocalComparisonQueryResult.Published>(evidence).Outcome).Completion;

    static void AssertRetainedEvidence(RoundTripMemberComparison member)
    {
        var published = Assert.IsType<LocalComparisonQueryResult.Published>(member.Evidence);
        Assert.NotSame(published.Identity!.Before, published.Identity.After);
        var completion = Completion(published);
        Assert.Equal(2, completion.Results.Length);
        Assert.All(completion.WorkItems, item =>
        {
            var pair = Assert.IsType<ResearchProducerWorkBasis.DesignatedPair>(item.Basis).Pair;
            Assert.Equal(member.Target.Method,
                Assert.IsType<ResearchTargetOutcome.Resolved>(pair.Before.Outcome).Address);
            Assert.Equal(member.Correspondence.Target,
                Assert.IsType<ResearchTargetOutcome.Resolved>(pair.After.Outcome).Address);
        });
        var csharp = Assert.IsType<ResearchProducerWorkOutcome.ProducedCSharp>(
            completion.Results.Single(result => result.Item.Producer == ResearchProducerKind.CSharp).Outcome).Result;
        Assert.Equal(csharp.BodyDiff!.Rows, member.CSharpDiff!.Rows);
        var il = Assert.IsType<ResearchProducerWorkOutcome.ProducedIlBody>(
            completion.Results.Single(result => result.Item.Producer == ResearchProducerKind.IlBody).Outcome).Result;
        Assert.Equal(il.MemberDiff!.Diff.Rows, member.IlDiff!.Rows);
    }

    static byte[] Compile(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "round-trip-comparison-donor",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        using var output = new MemoryStream();
        var emit = compilation.Emit(output);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return output.ToArray();
    }

    static RoundTripCompilationResult<string> CompileResult(
        string source,
        OptimizationLevel optimization = OptimizationLevel.Release)
        => RoundTripCompilationEngine.Compile(
            compose: () => source,
            source: artifact => artifact,
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpParseOptions(LanguageVersion.Preview),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: optimization),
            grow: (_, _, _) => RoundTripGrowthResult.Stop("unexpected"));

    static RoundTripRequest WithScope(RoundTripRequest request, RoundTripScope scope)
        => RoundTripRequest.Create(
            request.Artifact,
            request.Module,
            request.Targets,
            scope,
            request.BodyPolicy,
            request.Replacements);

    static string DonorSource(string expression, bool includeUnrelated = false)
        => $$"""
            namespace ILInspector.Decompiler.Tests;
            public sealed class RoundTripComparisonFixture
            {
                public int Transform(int value) => {{expression}};
            }
            {{(includeUnrelated ? "public sealed class Unrelated { }" : "")}}
            """;

    static MethodDefinitionHandle FindMethod(MetadataReader reader, string typeName, string methodName)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(type.Name) != typeName)
                continue;
            foreach (var methodHandle in type.GetMethods())
            {
                if (reader.GetString(reader.GetMethodDefinition(methodHandle).Name) == methodName)
                    return methodHandle;
            }
        }
        throw new InvalidOperationException($"Method '{typeName}::{methodName}' was not found.");
    }

    sealed class MetadataImage : IDisposable
    {
        readonly Stream _stream;
        readonly PEReader _pe;

        public MetadataImage(string path)
        {
            _stream = File.OpenRead(path);
            _pe = new PEReader(_stream);
            Reader = _pe.GetMetadataReader();
        }

        public MetadataReader Reader { get; }

        public void Dispose()
        {
            _pe.Dispose();
            _stream.Dispose();
        }
    }
}

public sealed class RoundTripComparisonFixture
{
    public int Transform(int value) => value + 1;
}
