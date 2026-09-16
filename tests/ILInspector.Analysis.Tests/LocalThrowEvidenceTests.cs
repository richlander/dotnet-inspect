using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis.LocalThrowFixtures;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public sealed class LocalThrowEvidenceTests
{
    static readonly TypeRef s_void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef s_exception = TypeRef.Definition("Fixture", "Fixtures", "E");
    static string FixturePath => FixtureCatalog.AnalysisLocalThrows.AssemblyPath();

    [Theory]
    [InlineData(nameof(LocalThrowSamples.Direct), nameof(LocalException))]
    [InlineData(nameof(LocalThrowSamples.Derived), nameof(ChildException))]
    [InlineData(nameof(LocalThrowSamples.Caught), nameof(LocalException))]
    [InlineData(nameof(LocalThrowSamples.Specialized), nameof(SpecializedException))]
    public void QualifiesLocalConstructionAndRetainsPhysicalCoordinates(
        string methodName, string typeName)
    {
        int token = Token(methodName);
        LibraryBodyIndex index = Open([token]);
        var body = Assert.IsType<MethodLocalThrowEvidence.Inspected>(
            Assert.Single(index.LocalThrows, result => result.MethodToken == token));
        LocalThrowSite site = Assert.Single(body.Sites);
        var known = Assert.IsType<LocalThrowTypeEvidence.Known>(site.Type);

        Assert.True(body.IsComplete);
        Assert.Equal(LocalThrowInstructionKind.Throw, site.Kind);
        Assert.Equal(typeName, known.ExceptionType.Name);
        Assert.IsType<TypeReferenceOrigin.CurrentAssembly>(
            known.ExceptionType.Resolution?.Origin);
        Assert.Equal(index.ModuleIdentity.ModuleVersionId, body.Method.ModuleVersionId);
        Assert.Equal(index.ModuleIdentity.ModuleVersionId, known.Definition.ModuleVersionId);
        Assert.Equal(
            typeName switch
            {
                nameof(LocalException) => typeof(LocalException).MetadataToken,
                nameof(ChildException) => typeof(ChildException).MetadataToken,
                _ => typeof(SpecializedException).MetadataToken,
            },
            known.Definition.Definition.Value);
        Assert.True(known.ConstructionOffset < site.ILOffset);
        Assert.Contains(index.DirectCalls, call =>
            call.EvidenceMethod.MetadataToken == token
            && call.ILOffset == known.ConstructionOffset
            && call.OperandToken == known.ConstructorToken
            && call.Kind == CallKind.NewObject);
    }

    [Theory]
    [InlineData(nameof(LocalThrowSamples.ConstructOnly))]
    [InlineData(nameof(LocalThrowSamples.HelperOnly))]
    [InlineData(nameof(LocalThrowSamples.CatchOnly))]
    public void ConstructionCallsAndCatchDeclarationsAreNotThrows(string methodName)
    {
        MethodLocalThrowEvidence body = Body(Open([Token(methodName)]), methodName);
        Assert.IsType<MethodLocalThrowEvidence.Inspected>(body);
        Assert.True(body.IsComplete);
        Assert.Empty(body.Sites);
    }

    [Theory]
    [InlineData(nameof(LocalThrowSamples.Unknown), LocalThrowUnresolvedReason.UnsupportedValue)]
    [InlineData(nameof(LocalThrowSamples.Null), LocalThrowUnresolvedReason.UnsupportedValue)]
    [InlineData(nameof(LocalThrowSamples.Rethrow), LocalThrowUnresolvedReason.Rethrow)]
    [InlineData(nameof(LocalThrowSamples.External), LocalThrowUnresolvedReason.UnresolvedType)]
    [InlineData(nameof(LocalThrowSamples.Generic), LocalThrowUnresolvedReason.UnresolvedType)]
    public void UnresolvedSitesAreNotCompleteAbsence(
        string methodName, LocalThrowUnresolvedReason reason)
    {
        MethodLocalThrowEvidence body = Body(Open([Token(methodName)]), methodName);
        Assert.IsType<MethodLocalThrowEvidence.Inspected>(body);
        Assert.False(body.IsComplete);
        LocalThrowSite site = Assert.Single(body.Sites);
        Assert.Equal(reason, Assert.IsType<LocalThrowTypeEvidence.Unresolved>(site.Type).Reason);
        Assert.Equal(
            reason == LocalThrowUnresolvedReason.Rethrow
                ? LocalThrowInstructionKind.Rethrow : LocalThrowInstructionKind.Throw,
            site.Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExternalQualificationUsesAuthorizedResolution(bool prefetched)
    {
        int token = Token(nameof(LocalThrowSamples.External));
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(FixturePath)
            {
                IncludeDepsJsonAssets = false,
                IncludeAspNetCoreSharedFramework = false,
                PreferImplementationAssemblies = true,
            });
        LibraryBodyIndex index = prefetched
            ? LibraryBodyIndex.OpenFromPrefetchedImage(
                FixturePath, [.. File.ReadAllBytes(FixturePath)],
                LibraryBodyAnalysisFeatures.LocalThrows, resolver, new HashSet<int> { token })
            : LibraryBodyIndex.Open(
                FixturePath, LibraryBodyAnalysisFeatures.LocalThrows,
                resolver, new HashSet<int> { token });
        MethodLocalThrowEvidence body = Body(index, nameof(LocalThrowSamples.External));
        var known = Assert.IsType<LocalThrowTypeEvidence.Known>(
            Assert.Single(body.Sites).Type);
        Assert.True(body.IsComplete);
        Assert.Equal(nameof(ArgumentNullException), known.ExceptionType.Name);
        Assert.IsType<TypeReferenceOrigin.AssemblyReference>(
            known.ExceptionType.Resolution?.Origin);
        Assert.Equal(typeof(ArgumentNullException).Module.ModuleVersionId,
            known.Definition.ModuleVersionId);
        Assert.Equal(typeof(ArgumentNullException).MetadataToken,
            known.Definition.Definition.Value);
        Assert.DoesNotContain(index.Diagnostics, diagnostic => diagnostic.MethodToken == token);
    }

    [Fact]
    public void DifferentSitesRetainDifferentExactTypes()
    {
        string name = nameof(LocalThrowSamples.MultipleSites);
        MethodLocalThrowEvidence body = Body(Open([Token(name)]), name);
        Assert.True(body.IsComplete);
        Assert.Equal(
            [nameof(LocalException), nameof(ChildException)],
            body.Sites.Select(site =>
                Assert.IsType<LocalThrowTypeEvidence.Known>(site.Type).ExceptionType.Name));
    }

    [Fact]
    public void CombinedSelectionPreservesExistingArgumentProvenance()
    {
        int token = Token(nameof(LocalThrowSamples.External));
        var scope = new HashSet<int> { token };
        LibraryBodyIndex baseline = LibraryBodyIndex.Open(
            FixturePath, LibraryBodyAnalysisFeatures.JsonWireContractFlow,
            bodyScope: scope);
        LibraryBodyIndex combined = LibraryBodyIndex.Open(
            FixturePath,
            LibraryBodyAnalysisFeatures.JsonWireContractFlow | LibraryBodyAnalysisFeatures.LocalThrows,
            bodyScope: scope);

        DirectCall original = Assert.Single(baseline.DirectCalls);
        DirectCall enriched = Assert.Single(combined.DirectCalls);
        Assert.NotEmpty(original.ResolvedArgumentValues);
        Assert.Equal(original.ResolvedArgumentValues, enriched.ResolvedArgumentValues);
        Assert.Equal(original.ArgumentSources, enriched.ArgumentSources);
        Assert.Equal(original.IsReachable, enriched.IsReachable);
        Assert.Single(Body(combined, nameof(LocalThrowSamples.External)).Sites);
    }

    [Fact]
    public void OptInAndScopeDoNotManufactureEmptyEvidence()
    {
        int token = Token(nameof(LocalThrowSamples.Direct));
        LibraryBodyIndex unrequested = LibraryBodyIndex.Open(
            FixturePath, LibraryBodyAnalysisFeatures.MethodEvidence,
            bodyScope: new HashSet<int> { token });
        Assert.Throws<InvalidOperationException>(() => unrequested.LocalThrows);
        Assert.Equal(LibraryBodyAnalysisFeatures.None,
            LibraryBodyAnalysisFeatures.Default & LibraryBodyAnalysisFeatures.LocalThrows);

        LibraryBodyIndex requested = Open([token]);
        Assert.Equal(
            LibraryBodyAnalysisFeatures.MethodEvidence | LibraryBodyAnalysisFeatures.LocalThrows,
            requested.Features);
        Assert.Empty(requested.ReturnFlows);
        Assert.All(requested.DirectCalls, call => Assert.Empty(call.ResolvedArgumentValues));
        Assert.Equal(LocalThrowUnavailableReason.ScopeExcluded,
            Assert.IsType<MethodLocalThrowEvidence.Unavailable>(
                Body(requested, nameof(LocalThrowSamples.Derived))).Reason);

        MethodInfo? abstractMethod = typeof(AbstractThrowSample).GetMethod("NoBody");
        Assert.NotNull(abstractMethod);
        var absent = Assert.IsType<MethodLocalThrowEvidence.Unavailable>(
            Assert.Single(requested.LocalThrows,
                result => result.MethodToken == abstractMethod.MetadataToken));
        Assert.Equal(LocalThrowUnavailableReason.NoManagedBody, absent.Reason);
        Assert.False(absent.IsComplete);
    }

    [Fact]
    public void DeferredThrowRemainsOnItsPhysicalBody()
    {
        string name = nameof(LocalThrowSamples.Deferred);
        LibraryBodyIndex index = Open([Token(name)]);
        MethodLocalThrowEvidence kickoff = Body(index, name);
        Assert.True(kickoff.IsComplete);
        Assert.Empty(kickoff.Sites);
        var physical = Assert.Single(
            index.LocalThrows.OfType<MethodLocalThrowEvidence.Inspected>(),
            result => result.Sites.Any(site => site.Type is LocalThrowTypeEvidence.Known));
        Assert.NotEqual(kickoff.MethodToken, physical.MethodToken);
        Assert.Equal("MoveNext", physical.Method.Name);
        Assert.Equal(physical.Method.MetadataToken, physical.MethodToken);
    }

    [Fact]
    public void RuntimeThrowAndThrowIfNullPreserveRealAssetDistinction()
    {
        MethodInfo? throwing = typeof(ArgumentNullException).GetMethod(
            "Throw", BindingFlags.Static | BindingFlags.NonPublic, [typeof(string)]);
        MethodInfo? helper = typeof(ArgumentNullException).GetMethod(
            "ThrowIfNull", [typeof(object), typeof(string)]);
        Assert.NotNull(throwing);
        Assert.NotNull(helper);
        var scope = new HashSet<int> { throwing.MetadataToken, helper.MetadataToken };
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            typeof(ArgumentNullException).Assembly.Location,
            LibraryBodyAnalysisFeatures.LocalThrows,
            bodyScope: scope);

        MethodLocalThrowEvidence positive = Assert.Single(
            index.LocalThrows, result => result.MethodToken == throwing.MetadataToken);
        MethodLocalThrowEvidence negative = Assert.Single(
            index.LocalThrows, result => result.MethodToken == helper.MetadataToken);
        Assert.True(positive.IsComplete);
        Assert.Equal(nameof(ArgumentNullException),
            Assert.IsType<LocalThrowTypeEvidence.Known>(
                Assert.Single(positive.Sites).Type).ExceptionType.Name);
        Assert.True(negative.IsComplete);
        Assert.Empty(negative.Sites);
        Assert.DoesNotContain(index.Diagnostics, diagnostic => scope.Contains(diagnostic.MethodToken));
    }

    public static TheoryData<byte[], LocalThrowUnresolvedReason?> ValueCases => new()
    {
        { [0x73, 1, 0, 0, 0x0A, 0x7A], null },
        { [0x73, 1, 0, 0, 0x0A, 0x25, 0x26, 0x7A], null },
        { [0x73, 1, 0, 0, 0x0A, 0x0A, 0x06, 0x7A], null },
        { [0x73, 1, 0, 0, 0x0A, 0x74, 1, 0, 0, 1, 0x7A], null },
        { [0x73, 1, 0, 0, 0x0A, 0x0A, 0x02, 0x0A, 0x06, 0x7A],
            LocalThrowUnresolvedReason.UnsupportedValue },
        { [0x73, 1, 0, 0, 0x0A, 0x0A, 0x12, 0, 0x26, 0x06, 0x7A],
            LocalThrowUnresolvedReason.UnresolvedValue },
        { [0x02, 0x2D, 7, 0x73, 1, 0, 0, 0x0A, 0x2B, 5,
            0x73, 2, 0, 0, 0x0A, 0x7A],
            LocalThrowUnresolvedReason.MultipleSources },
        { [0x02, 0x2D, 7, 0x73, 1, 0, 0, 0x0A, 0x2B, 5,
            0x73, 1, 0, 0, 0x0A, 0x7A],
            LocalThrowUnresolvedReason.MultipleSources },
    };

    [Theory]
    [MemberData(nameof(ValueCases))]
    public void ValueProvenanceIsNotConstructionProximity(
        byte[] il, LocalThrowUnresolvedReason? unresolved)
    {
        MethodInstructions instructions = MethodInstructions.Decode(il, il.Length, []);
        Assert.True(instructions.IsComplete);
        var context = new MethodBodyAnalysisContext(
            new MethodIdentity("Fixture", Guid.Empty, s_exception, "M",
                [s_exception], s_void, 0x06000001, IsStatic: true),
            instructions, [], [s_exception]);
        var sites = ImmutableArray.CreateBuilder<LocalThrowSite>();
        MethodCallAnalysis.Collect(
            context, new ValueResolver(), static _ => AllocationMultiplicity.Once,
            ImmutableArray.CreateBuilder<DirectCall>(),
            ImmutableArray.CreateBuilder<UnsafeEvidence>(),
            includeIndirectOpcodes: false,
            includeCallValueFlow: false,
            localThrows: sites,
            qualifyExceptionType: static _ => new(true));

        LocalThrowSite site = Assert.Single(sites);
        if (unresolved is { } reason)
            Assert.Equal(reason, Assert.IsType<LocalThrowTypeEvidence.Unresolved>(site.Type).Reason);
        else
            Assert.Equal(0, Assert.IsType<LocalThrowTypeEvidence.Known>(site.Type).ConstructionOffset);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonExceptionAndLookalikeExceptionDoNotQualify(bool lookalike)
    {
        LibraryBodyIndex index = Probe(
            [0x73, 2, 0, 0, 6, 0x7A], lookalike: lookalike);
        MethodLocalThrowEvidence body = Assert.Single(
            index.LocalThrows, result => result.MethodToken == 0x06000001);
        Assert.False(body.IsComplete);
        Assert.Equal(LocalThrowUnresolvedReason.NonExceptionType,
            Assert.IsType<LocalThrowTypeEvidence.Unresolved>(
                Assert.Single(body.Sites).Type).Reason);
    }

    [Fact]
    public void ReferenceStubAndMalformedBodyAreUnavailable()
    {
        var reference = Assert.IsType<MethodLocalThrowEvidence.Unavailable>(
            Assert.Single(Probe([0x2A], reference: true).LocalThrows,
                result => result.MethodToken == 0x06000001));
        Assert.Equal(LocalThrowUnavailableReason.ReferenceAssembly, reference.Reason);
        Assert.False(reference.IsComplete);

        LibraryBodyIndex malformed = Probe([0xFE]);
        var failed = Assert.IsType<MethodLocalThrowEvidence.Unavailable>(
            Assert.Single(malformed.LocalThrows, result => result.MethodToken == 0x06000001));
        Assert.Equal(LocalThrowUnavailableReason.AnalysisFailed, failed.Reason);
        Assert.False(failed.IsComplete);
        Assert.False(string.IsNullOrEmpty(failed.Detail));
        Assert.Contains(malformed.Diagnostics, diagnostic => diagnostic.MethodToken == failed.MethodToken);
    }

    [Fact]
    public void CyclicAncestryRemainsUnresolved()
    {
        MethodLocalThrowEvidence body = Assert.Single(
            Probe([0x73, 2, 0, 0, 6, 0x7A], cyclic: true).LocalThrows,
            result => result.MethodToken == 0x06000001);
        Assert.False(body.IsComplete);
        Assert.Equal(LocalThrowUnresolvedReason.UnresolvedType,
            Assert.IsType<LocalThrowTypeEvidence.Unresolved>(
                Assert.Single(body.Sites).Type).Reason);
    }

    static int Token(string name)
        => typeof(LocalThrowSamples).GetMethod(name)!.MetadataToken;

    static LibraryBodyIndex Open(int[] tokens)
        => LibraryBodyIndex.Open(
            FixturePath, LibraryBodyAnalysisFeatures.LocalThrows,
            bodyScope: tokens.ToHashSet());

    static MethodLocalThrowEvidence Body(LibraryBodyIndex index, string name)
        => Assert.Single(index.LocalThrows, result => result.MethodToken == Token(name));

    sealed class ValueResolver : IMethodCallResolver
    {
        public MemberRef ResolveMember(int token) => new(
            token == 0x0A000001 ? s_exception : TypeRef.Definition("Fixture", "Fixtures", "OtherE"),
            ".ctor", [], s_void, MemberKind.Constructor) { HasThis = true };
        public MemberRef ResolveIndirectCall(int signatureToken)
            => throw new InvalidOperationException("No indirect call in these specimens.");
        public int DefinitionToken(int operandToken) => operandToken;
        public string? ResolveUserString(int token) => null;
        public TypeRef ResolveType(int token) => s_exception;
    }

    static LibraryBodyIndex Probe(
        byte[] il, bool reference = false, bool lookalike = false, bool cyclic = false)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("LocalThrowProbe.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        AssemblyDefinitionHandle assembly = metadata.AddAssembly(
            metadata.GetOrAddString("LocalThrowProbe"), new Version(1, 0, 0, 0),
            default, default, 0, AssemblyHashAlgorithm.None);
        AssemblyName core = typeof(object).Assembly.GetName();
        AssemblyReferenceHandle coreRef = metadata.AddAssemblyReference(
            metadata.GetOrAddString(core.Name!), core.Version!, default,
            metadata.GetOrAddBlob(core.GetPublicKeyToken()!), 0, default);
        TypeReferenceHandle objectType = metadata.AddTypeReference(
            coreRef, metadata.GetOrAddString("System"), metadata.GetOrAddString("Object"));
        BlobHandle instanceSignature = metadata.GetOrAddBlob(new byte[] { 0x20, 0, 1 });
        MemberReferenceHandle objectConstructor = metadata.AddMemberReference(
            objectType, metadata.GetOrAddString(".ctor"), instanceSignature);
        if (reference)
        {
            TypeReferenceHandle attribute = metadata.AddTypeReference(
                coreRef, metadata.GetOrAddString("System.Runtime.CompilerServices"),
                metadata.GetOrAddString("ReferenceAssemblyAttribute"));
            MemberReferenceHandle constructor = metadata.AddMemberReference(
                attribute, metadata.GetOrAddString(".ctor"), instanceSignature);
            metadata.AddCustomAttribute(
                assembly, constructor, metadata.GetOrAddBlob(new byte[] { 1, 0, 0, 0 }));
        }

        metadata.AddTypeDefinition(TypeAttributes.NotPublic,
            default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(TypeAttributes.Public, metadata.GetOrAddString(
            lookalike ? "System" : "Fixtures"),
            metadata.GetOrAddString(lookalike ? "Exception" : "Probe"),
            cyclic ? MetadataTokens.TypeDefinitionHandle(2) : objectType,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var stream = new BlobBuilder();
        var bodies = new MethodBodyStreamEncoder(stream);
        var code = new BlobBuilder();
        code.WriteBytes(il);
        int bodyOffset = bodies.AddMethodBody(new InstructionEncoder(code));
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL, metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(new byte[] { 0, 0, 1 }),
            bodyOffset, MetadataTokens.ParameterHandle(1));
        var constructorCode = new BlobBuilder();
        var encoder = new InstructionEncoder(constructorCode);
        encoder.LoadArgument(0);
        encoder.Call(objectConstructor);
        encoder.OpCode(ILOpCode.Ret);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            MethodImplAttributes.IL, metadata.GetOrAddString(".ctor"),
            instanceSignature, bodies.AddMethodBody(encoder), MetadataTokens.ParameterHandle(1));
        var pe = new ManagedPEBuilder(
            new PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            new MetadataRootBuilder(metadata), stream, flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return LibraryBodyIndex.OpenFromPrefetchedImage(
            "LocalThrowProbe.dll", image.ToImmutableArray(),
            LibraryBodyAnalysisFeatures.LocalThrows,
            bodyScope: new HashSet<int> { 0x06000001 });
    }
}
