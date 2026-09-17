using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Analysis.ClassicAsyncFixtures;
using ILInspector.Analysis.MalformedOwnershipFixtures;
using ILInspector.Analysis.UnoptimizedAsyncFixtures;
using ILInspector.CallGraph;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public partial class LibraryBodyIndexTests
{
    [Fact]
    public void
        ModuleIdentity_IsImageDerivedAcrossFeaturesAndScopes()
    {
        string sourcePath =
            typeof(LibraryBodyIndexTests).Assembly.Location;
        ImmutableArray<byte> image =
            [.. File.ReadAllBytes(sourcePath)];
        using var peReader = new PEReader(image);
        MetadataReader reader = peReader.GetMetadataReader();
        var expected = new LibraryBodyModuleIdentity(
            AssemblyReferenceIdentity.FromAssemblyDefinition(reader),
            reader.GetGuid(reader.GetModuleDefinition().Mvid));

        LibraryBodyIndex ordinary =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "ordinary-label.dll",
                image,
                LibraryBodyAnalysisFeatures.MethodEvidence);
        LibraryBodyIndex capabilityLimited =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "capability-label.dll",
                image,
                LibraryBodyAnalysisFeatures.None);
        LibraryBodyIndex filtered =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "filtered-label.dll",
                image,
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope:
                    new HashSet<int> { 0x0600FFFF });

        Assert.NotEmpty(ordinary.DeclaredMethods);
        Assert.NotEmpty(ordinary.DirectCalls);
        Assert.Equal(
            LibraryBodyAnalysisFeatures.None,
            capabilityLimited.Features);
        Assert.Equal(
            LibraryBodyAnalysisFeatures.MethodEvidence,
            filtered.Features);
        Assert.Empty(filtered.DirectCalls);
        Assert.Equal(expected, ordinary.ModuleIdentity);
        Assert.Equal(expected, capabilityLimited.ModuleIdentity);
        Assert.Equal(expected, filtered.ModuleIdentity);
    }

    [Fact]
    public void
        ModuleIdentity_MethodlessPrefetchedImageRetainsExactIdentity()
    {
        Guid moduleVersionId = Guid.NewGuid();
        ImmutableArray<byte> image =
            [.. EmitAssembly(
                "MethodlessIdentity",
                static _ => { },
                moduleVersionId)];

        LibraryBodyIndex index =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "not-the-assembly-name.dll",
                image,
                LibraryBodyAnalysisFeatures.MethodEvidence);

        Assert.Empty(index.DeclaredMethods);
        Assert.Equal(
            new AssemblyReferenceIdentity(
                "MethodlessIdentity",
                new Version(1, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null),
            index.ModuleIdentity.AssemblyIdentity);
        Assert.Equal(
            moduleVersionId,
            index.ModuleIdentity.ModuleVersionId);
    }

    [Fact]
    public void ModuleIdentity_DistinguishesAssemblyAndModuleGeneration()
    {
        Guid firstModuleVersionId = Guid.NewGuid();
        LibraryBodyModuleIdentity first =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "first.dll",
                [.. EmitAssembly(
                    "FirstIdentity",
                    static _ => { },
                    firstModuleVersionId)],
                LibraryBodyAnalysisFeatures.None)
            .ModuleIdentity;
        LibraryBodyModuleIdentity differentAssembly =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "second.dll",
                [.. EmitAssembly(
                    "SecondIdentity",
                    static _ => { },
                    firstModuleVersionId)],
                LibraryBodyAnalysisFeatures.None)
            .ModuleIdentity;
        LibraryBodyModuleIdentity differentGeneration =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "third.dll",
                [.. EmitAssembly(
                    "FirstIdentity",
                    static _ => { },
                    Guid.NewGuid())],
                LibraryBodyAnalysisFeatures.None)
            .ModuleIdentity;

        Assert.NotEqual(first, differentAssembly);
        Assert.NotEqual(first, differentGeneration);
    }

    [Fact]
    public void ModuleIdentity_StandaloneModuleHasNoAssemblyIdentity()
    {
        Guid moduleVersionId = Guid.NewGuid();
        LibraryBodyIndex index =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "standalone-label.netmodule",
                [.. EmitStandaloneModule(
                    "StandaloneIdentity.netmodule",
                    moduleVersionId)],
                LibraryBodyAnalysisFeatures.None);

        Assert.Null(index.ModuleIdentity.AssemblyIdentity);
        Assert.Equal(
            moduleVersionId,
            index.ModuleIdentity.ModuleVersionId);
    }

    [Fact]
    public void ModuleIdentity_RejectsEmptyModuleVersionIdentifier()
    {
        ImmutableArray<byte> image =
            [.. EmitAssembly(
                "EmptyModuleVersionId",
                static _ => { },
                Guid.Empty)];

        BadImageFormatException error =
            Assert.Throws<BadImageFormatException>(
                () => LibraryBodyIndex.OpenFromPrefetchedImage(
                    "empty-mvid.dll",
                    image,
                    LibraryBodyAnalysisFeatures.None));

        Assert.Contains(
            "non-empty MVID",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        SyntheticModuleIdentity_EmptyEvidenceRequiresExplicitIdentity()
    {
        Assert.Throws<ArgumentException>(
            () => LibraryBodyIndex.FromEvidence([], []));

        var identity = new LibraryBodyModuleIdentity(
            new AssemblyReferenceIdentity(
                "EmptySynthetic",
                Version: null,
                Culture: null,
                PublicKeyToken: null),
            Guid.Empty);
        LibraryBodyIndex index = LibraryBodyIndex.FromEvidence(
            [],
            [],
            moduleIdentity: identity);

        Assert.Same(identity, index.ModuleIdentity);
    }

    [Fact]
    public void
        SyntheticModuleIdentity_ValidatesMethodsAgainstExplicitIdentity()
    {
        Guid moduleVersionId = Guid.NewGuid();
        MethodIdentity method = SyntheticMethod(
            "ActualAssembly",
            moduleVersionId);
        var identity = new LibraryBodyModuleIdentity(
            new AssemblyReferenceIdentity(
                "DifferentAssembly",
                Version: null,
                Culture: null,
                PublicKeyToken: null),
            moduleVersionId);

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => LibraryBodyIndex.FromEvidence(
                [method],
                [],
                moduleIdentity: identity));

        Assert.Equal("methods", error.ParamName);
    }

    [Fact]
    public void
        SyntheticModuleIdentity_NonEmptyEvidenceDerivesFixtureIdentity()
    {
        Guid moduleVersionId = Guid.NewGuid();
        LibraryBodyIndex index = LibraryBodyIndex.FromEvidence(
            [SyntheticMethod("SyntheticAssembly", moduleVersionId)],
            []);

        Assert.Equal(
            "SyntheticAssembly",
            index.ModuleIdentity.AssemblyIdentity?.Name);
        Assert.Equal(
            moduleVersionId,
            index.ModuleIdentity.ModuleVersionId);
    }

    [Fact]
    public void OptimizationOpportunities_AttachOnlyDirectCallerLoopInvocations()
    {
        var index = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerLoop.AssemblyPath());
        var opportunities = index.OptimizationOpportunities
            .Where(opportunity => opportunity.Method.DeclaringType.Name == "CallerLoopFixture")
            .ToArray();

        var direct = Assert.Single(opportunities, opportunity =>
            opportunity.Method.Name == "BoxDirect"
            && opportunity.Shape == "box-value-type");
        var evidence = Assert.IsType<CallerLoopEvidence>(direct.CallerLoop);
        var witness = Assert.Single(evidence.Witness);
        Assert.Equal(1, evidence.Depth);
        Assert.Equal("InvokeDirectInLoop", witness.Caller.Name);
        Assert.Equal("BoxDirect", witness.Callee.Name);
        Assert.True(witness.InLoop);
        Assert.False(direct.InLoop);
        Assert.Equal("once", direct.Multiplicity);
        Assert.Equal(PerformanceTriageProvenance.Exact, direct.Provenance);
        Assert.StartsWith("pt~", direct.CandidateId, StringComparison.Ordinal);

        var callVirtual = Assert.Single(opportunities, opportunity =>
            opportunity.Method.Name == "BoxVirtual"
            && opportunity.Shape == "box-value-type");
        Assert.Equal(CallKind.CallVirtual, Assert.Single(callVirtual.CallerLoop!.Witness).Kind);

        var constructor = Assert.Single(index.OptimizationOpportunities, opportunity =>
            opportunity.Method.DeclaringType.Name == "CallerLoopConstructorTarget"
            && opportunity.Method.Name == ".ctor"
            && opportunity.Shape == "box-value-type");
        Assert.Equal(CallKind.NewObject, Assert.Single(constructor.CallerLoop!.Witness).Kind);

        var conditional = Assert.Single(opportunities, opportunity =>
            opportunity.Method.Name == "BoxConditionally"
            && opportunity.Shape == "box-value-type");
        Assert.NotNull(conditional.CallerLoop);
        Assert.False(conditional.InLoop);
        Assert.NotEqual("loop", conditional.Multiplicity);

        foreach (string method in new[] { "BoxOutsideLoop", "BoxFunctionTarget", "RecursiveBox" })
        {
            var nearMiss = Assert.Single(opportunities, opportunity =>
                opportunity.Method.Name == method
                && opportunity.Shape == "box-value-type");
            Assert.Null(nearMiss.CallerLoop);
        }
    }

    [Fact]
    public void FindCalls_FindsConsoleWriteLine()
    {
        var index = LibraryBodyIndex.Open(typeof(CallSiteFixtures).Assembly.Location);

        var calls = index.FindCalls(MemberPattern.Method("System.Console", "WriteLine"));

        var call = Assert.Single(calls.Where(c => c.Caller.Name == nameof(CallSiteFixtures.CallsConsoleWriteLine)));
        Assert.Equal(CallKind.Call, call.Kind);
        Assert.Equal(TypeRef.CoreLib("System", "String"), Assert.Single(call.Callee.ParameterTypes));
        Assert.Empty(index.Diagnostics);
    }

    [Fact]
    public void DeclaredMethods_PreservesMethodsWithoutBodies()
    {
        var index = LibraryBodyIndex.Open(
            FixtureCatalog.DiffPair.OldAssemblyPath());

        Assert.Contains(index.DeclaredMethods, method =>
            method.DeclaringType.Name == "BodyStateSample"
            && method.Name == "BodyState");
        Assert.DoesNotContain(index.Methods, method =>
            method.DeclaringType.Name == "BodyStateSample"
            && method.Name == "BodyState");
    }

    [Fact]
    public void BuildCallTree_PreservesRecoverableBodyAnalysisFailure()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("MalformedBody.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("MalformedBody"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Sample"),
            metadata.GetOrAddString("Broken"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                1,
                returnType => returnType.Void(),
                parameters =>
                    parameters.AddParameter()
                        .Type()
                        .Pointer()
                        .Int32());
        var helperSignature = new BlobBuilder();
        new BlobEncoder(helperSignature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        var il = new BlobBuilder();
        il.WriteByte((byte)ILOpCode.Ldc_i4_1);
        il.WriteByte((byte)ILOpCode.Newarr);
        il.WriteInt32(0x02000002);
        il.WriteByte((byte)ILOpCode.Pop);
        il.WriteByte((byte)ILOpCode.Call);
        il.WriteInt32(0x06000002);
        il.WriteByte(0xFE);
        il.WriteByte(0x06);
        il.WriteInt32(0x0AFFFFFF);
        il.WriteByte((byte)ILOpCode.Pop);
        il.WriteByte((byte)ILOpCode.Ret);
        int bodyOffset = bodyEncoder.AddMethodBody(
            new InstructionEncoder(il),
            maxStack: 1);
        var helperIl = new BlobBuilder();
        var helperInstructions = new InstructionEncoder(helperIl);
        helperInstructions.OpCode(ILOpCode.Ret);
        int helperBodyOffset = bodyEncoder.AddMethodBody(
            helperInstructions,
            maxStack: 0);
        BlobHandle signatureHandle =
            metadata.GetOrAddBlob(signature);
        BlobHandle helperSignatureHandle =
            metadata.GetOrAddBlob(helperSignature);
        MethodDefinitionHandle methodHandle =
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Run"),
                signatureHandle,
                bodyOffset,
                MetadataTokens.ParameterHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Helper"),
            helperSignatureHandle,
            helperBodyOffset,
            MetadataTokens.ParameterHandle(1));
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        string path = Path.Combine(
            Path.GetTempPath(),
            $"MalformedBody-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(path, image.ToArray());
            LibraryBodyIndex index =
                LibraryBodyIndex.Open(
                    path,
                    LibraryBodyAnalysisFeatures.MethodEvidence);
            AnalysisDiagnostic diagnostic =
                Assert.Single(index.Diagnostics);
            int methodToken = MetadataTokens.GetToken(methodHandle);
            CallTreeNode tree = index.BuildCallTree(methodToken);
            LibraryBodyIndex optimizationIndex =
                LibraryBodyIndex.Open(
                    path,
                    LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities);
            AnalysisDiagnostic optimizationDiagnostic =
                Assert.Single(optimizationIndex.Diagnostics);

            Assert.Equal(methodToken, diagnostic.MethodToken);
            Assert.Equal(
                methodToken,
                optimizationDiagnostic.MethodToken);
            Assert.Contains(
                optimizationIndex.OptimizationOpportunities,
                opportunity => opportunity.Shape
                    == "small-array");
            Assert.Single(optimizationIndex.DirectCalls);
            Assert.Equal(CallTreeStatus.AnalysisIncomplete, tree.Status);
            Assert.Same(diagnostic, tree.Diagnostic);
            Assert.Single(index.DirectCalls);
            Assert.Contains(index.UnsafeEvidence, evidence =>
                evidence.Member.MetadataToken == methodToken
                && evidence.Reason == "Unsafe signature");
            CallTreeNode truncated =
                index.BuildCallTree(
                    methodToken,
                    maxNodes: 1);
            Assert.Equal(
                CallTreeStatus.Truncated,
                truncated.Status);
            Assert.Same(diagnostic, truncated.Diagnostic);

            ResolvedAssemblyReference assembly =
                ResolvedAssemblyReference.CreateFromPath(
                    path,
                    AssemblyResolutionProvenance.Local(
                        "malformed-body call-tree test"));
            var policy = new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(path));
            using var scope = new CatalogCallGraphScope(
                policy,
                [new CatalogCallGraphParticipant(index, assembly)]);
            CallTreeNode catalogTree = scope.BuildCallTree(
                index,
                methodToken);

            Assert.Equal(
                CallTreeStatus.AnalysisIncomplete,
                catalogTree.Status);
            Assert.Same(diagnostic, catalogTree.Diagnostic);

            CallTreeNode truncatedCatalogTree =
                scope.BuildCallTree(
                    index,
                    methodToken,
                    maxNodes: 1);
            Assert.Equal(
                CallTreeStatus.Truncated,
                truncatedCatalogTree.Status);
            Assert.Same(
                diagnostic,
                truncatedCatalogTree.Diagnostic);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LibraryBodyIndex_PrefetchedImageScopeSkipsMalformedUnselectedBody()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("ScopedMalformedBody.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("ScopedMalformedBody"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Sample"),
            metadata.GetOrAddString("Scoped"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                0,
                returnType => returnType.Void(),
                parameters => { });
        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        var selectedIl = new BlobBuilder();
        selectedIl.WriteByte((byte)ILOpCode.Ret);
        int selectedBodyOffset = bodyEncoder.AddMethodBody(
            new InstructionEncoder(selectedIl),
            maxStack: 0);
        var malformedIl = new BlobBuilder();
        malformedIl.WriteByte(0xFE);
        int malformedBodyOffset = bodyEncoder.AddMethodBody(
            new InstructionEncoder(malformedIl),
            maxStack: 0);
        BlobHandle signatureHandle =
            metadata.GetOrAddBlob(signature);
        MethodDefinitionHandle selectedHandle =
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Selected"),
                signatureHandle,
                selectedBodyOffset,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle malformedHandle =
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Malformed"),
                signatureHandle,
                malformedBodyOffset,
                MetadataTokens.ParameterHandle(1));
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        var immutableImage =
            ImmutableArray.Create(image.ToArray());
        int selectedToken =
            MetadataTokens.GetToken(selectedHandle);
        int malformedToken =
            MetadataTokens.GetToken(malformedHandle);

        LibraryBodyIndex full =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "ScopedMalformedBody.dll",
                immutableImage,
                LibraryBodyAnalysisFeatures.MethodEvidence);
        LibraryBodyIndex scoped =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "ScopedMalformedBody.dll",
                immutableImage,
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope:
                    new HashSet<int> { selectedToken });

        Assert.Contains(full.Diagnostics, diagnostic =>
            diagnostic.MethodToken == malformedToken);
        Assert.Empty(scoped.Diagnostics);
        Assert.Contains(scoped.Methods, method =>
            method.MetadataToken == selectedToken);
        Assert.Contains(scoped.Methods, method =>
            method.MetadataToken == malformedToken);
    }

    [Fact]
    public void CrossAssemblyMetadataResolver_UsesRetainedCandidateImage()
    {
        string targetPath = typeof(Console).Assembly.Location;
        var resolver = new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(targetPath)
        {
            IncludeDepsJsonAssets = false,
            IncludeAspNetCoreSharedFramework = false,
            PreferImplementationAssemblies = true,
        });

        using var stream = File.OpenRead(targetPath);
        using var peReader = new PEReader(stream);
        Assert.True(peReader.HasMetadata);
        var reader = peReader.GetMetadataReader();
        var retainedResolver =
            new ThirdOpenFailsResolver(resolver);
        using var builder = new LibraryBodyAnalysisBuilder(
            targetPath,
            reader,
            peReader,
            retainedResolver);

        bool resolvedFrameworkType = false;
        foreach (var handle in reader.TypeReferences)
        {
            var typeReference = reader.GetTypeReference(handle);
            if (typeReference.ResolutionScope.Kind != HandleKind.AssemblyReference)
                continue;
            var assemblyReference = (AssemblyReferenceHandle)typeReference.ResolutionScope;
            if (!FrameworkAssemblyKeys.IsFrameworkReference(reader, assemblyReference))
                continue;

            var resolved = builder.TryResolveExternalTypeDefinition(handle);
            if (resolved is not { } definition)
                continue;

            var definingType = definition.DefiningReader.GetTypeDefinition(definition.Definition);
            Assert.Equal(reader.GetString(typeReference.Name), definition.DefiningReader.GetString(definingType.Name));
            Assert.True(
                !definingType.BaseType.IsNil || definingType.GetFields().Count > 0 || definingType.GetMethods().Count > 0,
                "resolved type metadata should be readable");
            resolvedFrameworkType = true;
            break;
        }

        Assert.True(resolvedFrameworkType, "expected at least one framework TypeRef to resolve to readable metadata");
        Assert.All(
            retainedResolver.OpenCounts,
            count => Assert.InRange(count, 1, 2));
    }

    [Fact]
    public void CrossAssemblyMetadataResolver_FollowsForwardersToDefiningAssembly()
    {
        // A framework TypeRef names the assembly the compiler bound against, which is a
        // facade that defines nothing and forwards to the definer. This resolver hands back
        // exactly the assembly a TypeRef names -- no implementation-preference redirect --
        // so the definition is reachable only by following the facade's ExportedType
        // forwarder. Dropping that hop makes this return null, which was the #3400 bug.
        string frameworkDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string targetPath = typeof(Console).Assembly.Location;

        using var stream = File.OpenRead(targetPath);
        using var peReader = new PEReader(stream);
        Assert.True(peReader.HasMetadata);
        var reader = peReader.GetMetadataReader();
        using var builder = new LibraryBodyAnalysisBuilder(
            targetPath, reader, peReader, new FrameworkDirectoryResolver(frameworkDir));

        bool exercisedForwarder = false;
        foreach (var handle in reader.TypeReferences)
        {
            var typeReference = reader.GetTypeReference(handle);
            if (typeReference.ResolutionScope.Kind != HandleKind.AssemblyReference)
                continue;

            string referencedName = reader.GetString(
                reader.GetAssemblyReference((AssemblyReferenceHandle)typeReference.ResolutionScope).Name);
            string ns = reader.GetString(typeReference.Namespace);
            string name = reader.GetString(typeReference.Name);
            string fullTypeName = ns.Length == 0 ? name : $"{ns}.{name}";
            string namedPath = Path.Combine(frameworkDir, referencedName + ".dll");

            // Only a TypeRef whose named assembly forwards rather than defines exercises the hop.
            if (!File.Exists(namedPath)
                || !AssemblyForwardsType(namedPath, ns, name))
                continue;

            var definition = builder.TryResolveExternalTypeDefinition(handle);
            Assert.True(
                definition is not null,
                $"{fullTypeName} is forwarded by {referencedName} and must resolve through that forwarder");

            var definingReader = definition!.Value.DefiningReader;
            string definingName = definingReader.GetString(definingReader.GetAssemblyDefinition().Name);
            Assert.NotEqual(referencedName, definingName);
            Assert.Equal(name, definingReader.GetString(
                definingReader.GetTypeDefinition(definition.Value.Definition).Name));

            exercisedForwarder = true;
            break;
        }

        Assert.True(exercisedForwarder, "expected a framework TypeRef whose named assembly forwards the type");
    }

    [Fact]
    public void CrossAssemblyMetadataResolver_ForwarderCycleTerminatesWithoutResolving()
    {
        // Answering every identity with the same facade turns the forwarder chain into a
        // cycle. Resolution must terminate and report nothing rather than spin or invent
        // a definition.
        string frameworkDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string facade = Path.Combine(frameworkDir, "netstandard.dll");
        Assert.True(File.Exists(facade));
        Assert.True(AssemblyForwardsType(facade, "System", "Object"));

        string targetPath = typeof(Console).Assembly.Location;
        using var stream = File.OpenRead(targetPath);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        using var builder = new LibraryBodyAnalysisBuilder(
            targetPath, reader, peReader, new ConstantResolver(facade));

        var objectReference = FindExternalTypeReference(reader, "System", "Object");
        Assert.False(objectReference.IsNil, "System.Console should reference System.Object");

        Assert.True(builder.TryResolveExternalTypeDefinition(objectReference) is null);
    }

    [Fact]
    public void CrossAssemblyMetadataResolver_NullResolverFailsHonest()
    {
        string targetPath = typeof(Console).Assembly.Location;
        using var stream = File.OpenRead(targetPath);
        using var peReader = new PEReader(stream);
        Assert.True(peReader.HasMetadata);
        var reader = peReader.GetMetadataReader();
        var externalType = FirstExternalTypeReference(reader);
        using var builder = new LibraryBodyAnalysisBuilder(targetPath, reader, peReader, resolver: null);

        Assert.Null(builder.TryResolveExternalTypeDefinition(externalType));

        var oneArg = LibraryBodyIndex.Open(targetPath);
        var nullResolver = LibraryBodyIndex.Open(targetPath, resolver: null);
        Assert.Equal(oneArg.Methods.Length, nullResolver.Methods.Length);
        Assert.Equal(oneArg.DirectCalls.Length, nullResolver.DirectCalls.Length);
        Assert.Equal(oneArg.UnsafeEvidence.Length, nullResolver.UnsafeEvidence.Length);
        Assert.Equal(oneArg.Diagnostics.Length, nullResolver.Diagnostics.Length);
    }

    [Fact]
    public void Open_MethodEvidenceWithResolverDoesNotChangeIndexShape()
    {
        string targetPath = typeof(CallSiteFixtures).Assembly.Location;
        var resolver = new CountingResolver();
        string paddedPath = Path.Combine(
            Path.GetTempPath(),
            $"MethodEvidencePadded-{Guid.NewGuid():N}.dll");
        int sourceToken = typeof(CallSiteFixtures)
            .GetMethod(nameof(CallSiteFixtures.CallsConsoleWriteLine))!
            .MetadataToken;
        var bodyScope = new HashSet<int> { sourceToken };

        try
        {
            var oneArg = LibraryBodyIndex.Open(
                targetPath,
                LibraryBodyAnalysisFeatures.MethodEvidence);
            var withResolver = LibraryBodyIndex.Open(
                targetPath,
                LibraryBodyAnalysisFeatures.MethodEvidence,
                resolver);

            Assert.Equal(oneArg.Methods.Length, withResolver.Methods.Length);
            Assert.Equal(oneArg.DirectCalls.Length, withResolver.DirectCalls.Length);
            Assert.Equal(oneArg.UnsafeEvidence.Length, withResolver.UnsafeEvidence.Length);
            Assert.Equal(oneArg.Diagnostics.Length, withResolver.Diagnostics.Length);
            Assert.Equal(0, resolver.ResolveCalls);

            _ = LibraryBodyIndex.Open(
                targetPath,
                LibraryBodyAnalysisFeatures.MethodEvidence,
                resolver,
                bodyScope);

            File.Copy(targetPath, paddedPath);
            const int OverlaySize = 32 * 1024 * 1024;
            using (var stream = new FileStream(
                paddedPath,
                FileMode.Open,
                FileAccess.Write,
                FileShare.Read))
            {
                stream.SetLength(
                    stream.Length + OverlaySize);
            }
            long baseline = AllocatedFor(targetPath);
            long padded = AllocatedFor(paddedPath);
            long overlayAllocation = padded - baseline;

            Assert.True(
                overlayAllocation > -(OverlaySize / 2)
                    && overlayAllocation < OverlaySize / 2,
                $"Method-evidence allocation delta {overlayAllocation:N0} fell outside the stable "
                    + $"range for the file overlay (baseline {baseline:N0}; padded {padded:N0}).");
            Assert.Equal(0, resolver.ResolveCalls);
        }
        finally
        {
            File.Delete(paddedPath);
        }

        long AllocatedFor(string path)
        {
            long before =
                GC.GetAllocatedBytesForCurrentThread();
            _ = LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures.MethodEvidence,
                resolver,
                bodyScope);
            return GC.GetAllocatedBytesForCurrentThread()
                - before;
        }
    }

    [Fact]
    public void OptimizationOpportunities_RootImageIsRetainedOnce()
    {
        string sourcePath = typeof(OptimizationOpportunityFixtures)
            .Assembly.Location;
        string paddedPath = Path.Combine(
            Path.GetTempPath(),
            $"OptimizationPadded-{Guid.NewGuid():N}.dll");
        const int OverlaySize = 32 * 1024 * 1024;

        try
        {
            File.Copy(sourcePath, paddedPath);
            using (var stream = new FileStream(
                paddedPath,
                FileMode.Open,
                FileAccess.Write,
                FileShare.Read))
            {
                stream.SetLength(stream.Length + OverlaySize);
            }

            long baseline = AllocatedFor(sourcePath);
            long padded = AllocatedFor(paddedPath);
            long overlayAllocation = padded - baseline;

            Assert.InRange(
                overlayAllocation,
                OverlaySize / 2,
                OverlaySize + OverlaySize / 2);
        }
        finally
        {
            File.Delete(paddedPath);
        }

        static long AllocatedFor(string path)
        {
            var resolver = new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(path)
                {
                    IncludeDepsJsonAssets = false,
                    IncludeAspNetCoreSharedFramework = false,
                    PreferImplementationAssemblies = true,
                    SnapshotAssemblyImages = false,
                });
            int sourceToken = typeof(OptimizationOpportunityFixtures)
                .GetMethod(
                    nameof(OptimizationOpportunityFixtures
                        .CallsFileReadLinesFromAsync))!
                .MetadataToken;
            long before = GC.GetAllocatedBytesForCurrentThread();
            LibraryBodyIndex index = LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures.MethodEvidence
                    | LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities,
                resolver,
                new HashSet<int> { sourceToken });
            long allocated =
                GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Contains(
                index.OptimizationOpportunities,
                opportunity => opportunity.Shape
                    == "sync-call-in-async");
            return allocated;
        }
    }

    /// <summary>
    /// A chain that starts Platform-scoped stays Platform-scoped for every hop. This gates the
    /// structured resolution engine's non-downgrade rule against a real framework forwarder
    /// chain; it does NOT gate tightening, because a framework TypeRef starts at Platform and
    /// the chain never begins at Any.
    /// <see cref="ForwarderIntoFrameworkSignedAssemblyIsResolvedUnderPlatformScope"/> is the
    /// tightening gate.
    /// </summary>
    [Fact]
    public void CrossAssemblyMetadataResolver_ResolvesEveryForwarderHopUnderPlatformScope()
    {
        // Resolving a hop under Any would let a confusable local copy satisfy it -- see
        // ILInspector.Analysis.SpoofRuntimeFixtures, an unsigned assembly literally named
        // System.Runtime.
        string frameworkDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string targetPath = typeof(Console).Assembly.Location;
        var recorder = new RecordingResolver(new FrameworkDirectoryResolver(frameworkDir));

        using var stream = File.OpenRead(targetPath);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        using var builder = new LibraryBodyAnalysisBuilder(targetPath, reader, peReader, recorder);

        var objectReference = FindExternalTypeReference(reader, "System", "Object");
        Assert.False(objectReference.IsNil, "System.Console should reference System.Object");
        Assert.True(builder.TryResolveExternalTypeDefinition(objectReference) is not null);

        // Non-empty rather than "> 1": whether System.Object takes a forwarder hop from
        // System.Console depends on the framework layout, but that this test observed real
        // resolution at all does not.
        Assert.NotEmpty(recorder.Requests);
        Assert.All(recorder.Requests, request =>
            Assert.Equal(AssemblyResolutionScope.Platform, request.Scope));
    }

    /// <summary>
    /// The tightening gate: the unit tests for scope classification would still pass if the
    /// structured resolution engine stopped applying the forwarded target's scope at the hop.
    ///
    /// The chain is synthetic because no natural artifact pairs an Any-scoped TypeRef with a
    /// framework-signed forwarder target -- a real framework TypeRef is already Platform at hop 0.
    /// Contoso.App references unsigned Contoso.Facade (Any), which forwards Sample.Widget to a
    /// framework-signed identity. That hop must be requested under Platform, or a confusable
    /// local copy could answer for a framework-signed name.
    /// </summary>
    [Fact]
    public void ForwarderIntoFrameworkSignedAssemblyIsResolvedUnderPlatformScope()
    {
        string directory = Path.Combine(Path.GetTempPath(), "fwd-scope-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        try
        {
            // 31bf3856ad364e35 signs Microsoft framework assemblies such as
            // WindowsBase. It must tighten exactly like the other platform keys.
            byte[] frameworkToken = Convert.FromHexString("31bf3856ad364e35");
            string appPath = Path.Combine(directory, "Contoso.App.dll");
            string facadePath = Path.Combine(directory, "Contoso.Facade.dll");
            string corelibPath = Path.Combine(directory, "Fake.CoreLib.dll");

            File.WriteAllBytes(appPath, EmitAssembly("Contoso.App", metadata =>
            {
                var facadeReference = metadata.AddAssemblyReference(
                    metadata.GetOrAddString("Contoso.Facade"),
                    new Version(1, 0, 0, 0),
                    culture: default,
                    publicKeyOrToken: default, // unsigned -> ScopeForReference yields Any
                    flags: default,
                    hashValue: default);
                metadata.AddTypeReference(
                    facadeReference,
                    metadata.GetOrAddString("Sample"),
                    metadata.GetOrAddString("Widget"));
            }));

            File.WriteAllBytes(facadePath, EmitAssembly("Contoso.Facade", metadata =>
            {
                var corelibReference = metadata.AddAssemblyReference(
                    metadata.GetOrAddString("Fake.CoreLib"),
                    new Version(1, 0, 0, 0),
                    culture: default,
                    publicKeyOrToken: metadata.GetOrAddBlob(frameworkToken),
                    flags: default,
                    hashValue: default);
                metadata.AddExportedType(
                    // tdForwarder (ECMA-335 II.23.1.15); not named in System.Reflection.TypeAttributes.
                    (TypeAttributes)0x00200000,
                    metadata.GetOrAddString("Sample"),
                    metadata.GetOrAddString("Widget"),
                    corelibReference,
                    typeDefinitionId: 0);
            }));

            File.WriteAllBytes(corelibPath, EmitAssembly("Fake.CoreLib", metadata =>
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("Sample"),
                    metadata.GetOrAddString("Widget"),
                    baseType: default,
                    fieldList: MetadataTokens.FieldDefinitionHandle(1),
                    methodList: MetadataTokens.MethodDefinitionHandle(1))));

            var recorder = new RecordingResolver(new FrameworkDirectoryResolver(directory));

            using var stream = File.OpenRead(appPath);
            using var peReader = new PEReader(stream);
            var reader = peReader.GetMetadataReader();
            using var builder = new LibraryBodyAnalysisBuilder(appPath, reader, peReader, recorder);

            var widgetReference = FindExternalTypeReference(reader, "Sample", "Widget");
            Assert.False(widgetReference.IsNil);
            Assert.NotNull(builder.TryResolveExternalTypeDefinition(widgetReference));

            // The premise: the chain really does start unconstrained. Without this the test
            // would pass for an implementation that scoped everything Platform from hop 0.
            Assert.Equal(
                AssemblyResolutionScope.Any,
                Assert.Single(recorder.Requests, request => request.Name == "Contoso.Facade").Scope);

            // The property.
            Assert.Equal(
                AssemblyResolutionScope.Platform,
                Assert.Single(recorder.Requests, request => request.Name == "Fake.CoreLib").Scope);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DirectCalls_RecordVirtualCallEvidenceWithoutInferringTargets()
    {
        var index = LibraryBodyIndex.Open(typeof(CallSiteFixtures).Assembly.Location);

        var call = Assert.Single(index.DirectCalls.Where(c =>
            c.Caller.Name == nameof(CallSiteFixtures.CallsVirtualToString)
            && c.Callee.DeclaringType.Equals(TypeRef.CoreLib("System", "Object"))
            && c.Callee.Name == "ToString"));

        Assert.Equal(CallKind.CallVirtual, call.Kind);
    }
}
