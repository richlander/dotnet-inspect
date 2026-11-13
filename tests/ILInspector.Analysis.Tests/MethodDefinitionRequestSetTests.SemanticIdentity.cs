using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using ILInspector.Analysis.Planning;

namespace ILInspector.Analysis.Tests;

public sealed partial class MethodDefinitionRequestSetTests
{
    public static TheoryData<string> SemanticIdentityParitySubjects() =>
    [
        FixtureCatalog.Get(FixtureIds.AnalysisCalleeResolution).AssemblyPath(),
        typeof(MethodDefinitionRequestSetTests).Assembly.Location,
        typeof(ImmutableArray).Assembly.Location,
        typeof(Enumerable).Assembly.Location,
    ];

    [Theory]
    [MemberData(nameof(SemanticIdentityParitySubjects))]
    public void MethodQuerySource_SemanticIdentityMatchesLibraryBodyAnalysis(
        string path)
    {
        LibraryCallGraphAnalysisResult graph =
            LibraryBodyAnalysisService.ExecutePath(
                    path,
                    LibraryBodyAnalysisRequest.Create(
                        LibraryBodyAnalysisFeatures.MethodEvidence))
                .CallGraph;

        AssertSemanticIdentityParity(graph, ProbeSemanticIdentities(path));
    }

    [Fact]
    public void MethodQuerySource_SemanticIdentityParityCoversDeclarationShapes()
    {
        // The parity subjects exercise the shapes semantic identity must
        // carry: extension, generic, nested-type, and bodiless methods.
        string path = typeof(Enumerable).Assembly.Location;
        ImmutableDictionary<int, SemanticIdentityObservation> identities =
            ProbeSemanticIdentities(path);
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinition Definition(int token) =>
            reader.GetMethodDefinition(
                MetadataTokens.MethodDefinitionHandle(token & 0x00FFFFFF));

        Assert.Contains(
            identities.Values,
            observation => observation.MethodIdentity!.IsExtension);
        Assert.Contains(
            identities.Values,
            observation => observation.MethodIdentity!.GenericArity > 0
                && !observation.MethodIdentity.GenericParameterNames.IsEmpty);
        Assert.Contains(
            identities.Keys,
            token => reader.GetTypeDefinition(
                    Definition(token).GetDeclaringType())
                .IsNested);
        Assert.Contains(
            identities.Keys,
            token => Definition(token).RelativeVirtualAddress == 0);
        Assert.Contains(
            identities.Values,
            observation => observation.MethodIdentity!.IsVirtualDispatchOpen);
    }

    [Fact]
    public void MethodQuerySource_SemanticIdentityAbsentWhenUndeclared()
    {
        MethodDefinitionSourceAssociation undeclared =
            Association(
                CountingProducer.Instance,
                ProducerTerminal.Count);

        MethodDefinitionSourceRequestSetExecution execution =
            Execute(AcceptedPlan([undeclared]));

        Assert.Same(
            MethodSemanticIdentityCoverage.Empty,
            Assert.Single(execution.GroupReceipts)
                .PhysicalCoverage.SemanticIdentity);
        Assert.Same(
            MethodSemanticIdentityCoverage.Empty,
            execution.ResultOf(undeclared)
                .SourceReceipt.Coverage.SemanticIdentity);
    }

    [Fact]
    public void MethodQuerySource_SemanticIdentityRequiresDeclaration()
    {
        MethodDefinitionSourceAssociation undeclared =
            Association(
                UndeclaredSemanticIdentityProbe.Instance,
                ProducerTerminal.Count);

        ProducerContractException exception =
            Assert.Throws<ProducerContractException>(
                () => Execute(AcceptedPlan([undeclared])));

        Assert.Contains(
            nameof(MethodDefinitionLayers.SemanticIdentity),
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MethodQuerySource_SemanticIdentityReceiptsExactMethodWork()
    {
        string path =
            FixtureCatalog.Get(FixtureIds.AnalysisCalleeResolution).AssemblyPath();
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinitionHandle method =
            FindMethod(reader, "Consumer", "UseClosedGeneric");
        MethodDefinition definition = reader.GetMethodDefinition(method);

        MethodDefinitionSourceAssociation exact =
            Association(
                SemanticIdentityProbe.IdentityOnly,
                ProducerTerminal.Rows,
                MethodDefinitionSourceBreadth.ExactMethods(method));
        MethodDefinitionSourceRequestSetExecution execution =
            ExecuteAt(AcceptedPlan([exact]), path);
        MethodDefinitionSourceCoverage coverage =
            execution.ResultOf(exact).SourceReceipt.Coverage;
        MethodSemanticIdentityCoverage identity = coverage.SemanticIdentity;

        // The oracle decodes the same MethodDef with a fresh counter.
        var counter = new MethodSemanticIdentityReader.WorkCounter();
        TypeDefinitionHandle typeHandle = definition.GetDeclaringType();
        MethodSemanticIdentityReader.Read(
            reader,
            reader.GetString(reader.GetAssemblyDefinition().Name),
            reader.GetGuid(reader.GetModuleDefinition().Mvid),
            typeHandle,
            method,
            definition,
            MethodSemanticIdentityReader.CreateScope(
                reader,
                reader.GetTypeDefinition(typeHandle),
                definition,
                counter),
            counter);

        Assert.Equal(
            new MethodSemanticIdentityCoverage(
                Constructed: true,
                counter.Work,
                identity.Methods,
                Decoded: 1,
                SignatureBoundExceeded: 0,
                Malformed: 0),
            identity);
        Assert.Equal(1, identity.Methods.Count);
        Assert.True(identity.Methods.Contains(method));

        // The signature, name, and type decode are each charged; a
        // non-generic method in a non-generic, non-static type reads no
        // GenericParam or CustomAttribute row.
        Assert.Equal(
            reader.GetBlobReader(definition.Signature).Length,
            identity.Work.SignatureBytes);
        Assert.Equal(
            reader.GetBlobReader(definition.Name).Length,
            identity.Work.NameBytes);
        Assert.True(identity.Work.TypeDecodeWork > 0);
        Assert.Equal(0, identity.Work.GenericParameterRows);
        Assert.Equal(0, identity.Work.CustomAttributeRows);

        // Identity acquires no body, needs no module lookup, and does not arm
        // the identity-text budget.
        Assert.Equal(0, coverage.BodiesAttempted.Count);
        Assert.Equal(0, coverage.ModuleLookupMethods.Count);
        Assert.False(
            execution.ResultOf(exact).WorkReceipt.IdentityBudgetArmed);
    }

    [Fact]
    public void MethodQuerySource_SemanticIdentityOfBodilessMethodAcquiresNoBody()
    {
        string path = typeof(ImmutableArray).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinitionHandle bodiless = reader.MethodDefinitions.First(
            handle =>
                reader.GetMethodDefinition(handle).RelativeVirtualAddress == 0);

        MethodDefinitionSourceAssociation exact =
            Association(
                SemanticIdentityProbe.IdentityOnly,
                ProducerTerminal.Rows,
                MethodDefinitionSourceBreadth.ExactMethods(bodiless));
        MethodDefinitionSourceRequestSetExecution execution =
            ExecuteAt(AcceptedPlan([exact]), path);
        MethodDefinitionSourceCoverage coverage =
            execution.ResultOf(exact).SourceReceipt.Coverage;
        SemanticIdentityObservation observation = Assert.Single(
                ResultOf<ImmutableDictionary<int, SemanticIdentityObservation>>(
                        execution,
                        exact)
                    .Value!)
            .Value;

        MethodSemanticIdentity identity =
            Assert.IsType<MethodSemanticIdentityResult.Decoded>(
                    observation.Result)
                .Identity;
        Assert.Equal(MetadataTokens.GetToken(bodiless), identity.MetadataToken);
        Assert.Equal(
            reader.GetString(reader.GetMethodDefinition(bodiless).Name),
            identity.Name);
        Assert.Equal(1, coverage.SemanticIdentity.Decoded);
        Assert.Equal(0, coverage.BodiesAttempted.Count);
        Assert.Equal(0, coverage.BodiesAcquired.Count);
    }

    [Fact]
    public void MethodQuerySource_SemanticIdentityDecodedOnceAcrossLanes()
    {
        string path =
            FixtureCatalog.Get(FixtureIds.AnalysisCalleeResolution).AssemblyPath();
        MethodDefinitionSourceAssociation first =
            Association(
                SemanticIdentityProbe.Instance,
                ProducerTerminal.Rows);
        MethodDefinitionSourceAssociation second =
            Association(
                SemanticIdentityProbe.Second,
                ProducerTerminal.Rows);
        MethodDefinitionSourceAssociation undeclared =
            Association(
                CountingProducer.Instance,
                ProducerTerminal.Count);

        MethodDefinitionSourceRequestSetExecution execution =
            ExecuteAt(AcceptedPlan([first, second, undeclared]), path);
        MethodSemanticIdentityCoverage physical =
            Assert.Single(execution.GroupReceipts)
                .PhysicalCoverage.SemanticIdentity;
        MethodSemanticIdentityCoverage firstCoverage =
            execution.ResultOf(first).SourceReceipt.Coverage.SemanticIdentity;
        MethodSemanticIdentityCoverage secondCoverage =
            execution.ResultOf(second).SourceReceipt.Coverage.SemanticIdentity;
        ImmutableDictionary<int, SemanticIdentityObservation> firstIdentities =
            ResultOf<ImmutableDictionary<int, SemanticIdentityObservation>>(
                    execution,
                    first)
                .Value!;

        Assert.Equal(
            firstIdentities.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.MethodIdentity),
            ResultOf<ImmutableDictionary<int, SemanticIdentityObservation>>(
                    execution,
                    second)
                .Value!
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.MethodIdentity));

        // The physical source decodes each MethodDef once and reads the
        // module's assembly name and MVID once.
        Assert.True(physical.Constructed);
        Assert.True(firstCoverage.Constructed);
        Assert.False(secondCoverage.Constructed);
        Assert.Equal(firstIdentities.Count, physical.Methods.Count);
        Assert.Equal(firstIdentities.Count, physical.Decoded);

        // Each lane receipts the identities it read, with the work they
        // required, and the physical work is charged only once.
        Assert.Equal(firstCoverage.Work, secondCoverage.Work);
        Assert.Equal(firstCoverage.Work, physical.Work);
        Assert.Equal(firstIdentities.Count, firstCoverage.Methods.Count);
        Assert.Equal(firstIdentities.Count, secondCoverage.Methods.Count);
        Assert.Same(
            MethodSemanticIdentityCoverage.Empty,
            execution.ResultOf(undeclared)
                .SourceReceipt.Coverage.SemanticIdentity);
    }

    internal static void AssertSemanticIdentityParity(
        LibraryCallGraphAnalysisResult graph,
        ImmutableDictionary<int, SemanticIdentityObservation> actual)
    {
        // The source issues the analysis's identity for every MethodDef the
        // analysis published, and a typed malformed outcome for every other.
        Assert.NotEmpty(graph.DeclaredMethods);
        Assert.Equal(
            graph.DeclaredMethods.Length,
            actual.Count(pair =>
                pair.Value.Result is not MethodSemanticIdentityResult.Malformed));
        foreach (MethodIdentity expected in graph.DeclaredMethods)
        {
            Assert.True(
                actual.TryGetValue(
                    expected.MetadataToken,
                    out SemanticIdentityObservation? observation),
                $"No source identity for 0x{expected.MetadataToken:X8}.");
            Assert.Equal(expected, observation.MethodIdentity);
        }
    }

    internal static ImmutableDictionary<int, SemanticIdentityObservation>
        ProbeSemanticIdentities(string path)
    {
        MethodDefinitionSourceAssociation association =
            Association(SemanticIdentityProbe.Instance, ProducerTerminal.Rows);
        return ResultOf<ImmutableDictionary<int, SemanticIdentityObservation>>(
                ExecuteAt(AcceptedPlan([association]), path),
                association)
            .Value!;
    }

    internal static ImmutableDictionary<int, SemanticIdentityObservation>
        ProbeSemanticIdentities(ImmutableArray<byte> image)
    {
        MethodDefinitionSourceAssociation association =
            Association(SemanticIdentityProbe.Instance, ProducerTerminal.Rows);
        return ResultOf<ImmutableDictionary<int, SemanticIdentityObservation>>(
                Execute(AcceptedPlan([association]), image),
                association)
            .Value!;
    }

    internal static MethodSemanticIdentityCoverage ProbeSemanticIdentityCoverage(
        ImmutableArray<byte> image)
    {
        MethodDefinitionSourceAssociation association =
            Association(SemanticIdentityProbe.Instance, ProducerTerminal.Rows);
        return Execute(AcceptedPlan([association]), image)
            .ResultOf(association)
            .SourceReceipt.Coverage.SemanticIdentity;
    }
}
