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
using ILInspector.Analysis.Planning;
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
    public void AsyncSiblingProducer_FindsSyncCallsWithAsyncSiblings()
    {
        string path = typeof(OptimizationOpportunityFixtures)
            .Assembly.Location;
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path)
            {
                IncludeDepsJsonAssets = false,
                IncludeAspNetCoreSharedFramework = false,
                PreferImplementationAssemblies = true,
            });
        AsyncSiblingRow[] rows =
            AsyncSiblingTestExecution.Execute(path, resolver)
                .Rows.ToArray();

        AsyncSiblingRow local = Assert.Single(rows, row =>
            row.Caller.Name
                == nameof(OptimizationOpportunityAsyncSiblingFixtures
                    .CallsSyncSiblingFromAsync));
        Assert.Equal(
            nameof(OptimizationOpportunityAsyncSiblingFixtures
                .CallsSyncSiblingFromAsync),
            local.Caller.Name);
        Assert.Equal(
            local.Caller,
            local.SynchronousCall.Caller);
        Assert.Equal(
            nameof(OptimizationOpportunityAsyncSiblingFixtures
                .ReadValues),
            local.SynchronousCall.Callee.Name);
        Assert.Equal(
            nameof(OptimizationOpportunityAsyncSiblingFixtures
                .ReadValuesAsync),
            local.Alternative.Name);

        AsyncSiblingRow framework = Assert.Single(rows, row =>
            row.Caller.Name
                == nameof(OptimizationOpportunityFixtures
                    .CallsFileReadLinesFromAsync));
        Assert.Equal(
            nameof(File.ReadLines),
            framework.SynchronousCall.Callee.Name);
        Assert.Equal(
            nameof(File.ReadLinesAsync),
            framework.Alternative.Name);
        Assert.Equal(
            TypeRef.CoreLibrary,
            framework.Alternative.DeclaringType.Assembly);
        Assert.Equal(
            "System.Threading.CancellationToken",
            framework.Alternative
                .ParameterTypes[^1]
                .ToQualifiedDisplayString());

        Assert.DoesNotContain(rows, row =>
            row.Caller.Name
                == nameof(OptimizationOpportunityFixtures
                    .CallsSyncSiblingFromNonAsync));
        Assert.DoesNotContain(rows, row =>
            row.Caller.Name
                == nameof(OptimizationOpportunityFixtures
                    .CallsSyncWithoutCompatibleAsyncSibling));
        Assert.DoesNotContain(rows, row =>
            row.Caller.Name
                == nameof(OptimizationOpportunityFixtures
                    .CallsSyncWithMismatchedAsyncReturn));
        Assert.DoesNotContain(rows, row =>
            row.Caller.Name
                == nameof(OptimizationOpportunityFixtures
                    .ExecuteOwnCoreAsync));
        Assert.DoesNotContain(rows, row =>
            row.Caller.Name
                == nameof(OptimizationOpportunityFixtures
                    .CallsBothLifecycleSiblingsAsync));
        Assert.DoesNotContain(rows, row =>
            row.Caller.Name
                == nameof(OptimizationOpportunityFixtures
                    .CallsBothFileReadLinesSiblingsAsync));
        Assert.DoesNotContain(rows, row =>
            row.Caller.Name
                == nameof(GenericSelfSiblingFixture<int>
                    .ReadAsync)
            && row.Caller.DeclaringType.Name
                .StartsWith(
                    "GenericSelfSiblingFixture",
                    StringComparison.Ordinal));
        Assert.DoesNotContain(rows, row =>
            row.Caller.Name
                == nameof(InterfaceSelfSiblingFixture
                    .ReadAsync)
            && row.Caller.DeclaringType.Name
                == nameof(InterfaceSelfSiblingFixture));
        Assert.DoesNotContain(rows, row =>
            row.Caller.Name
                == nameof(ConstrainedSiblingFixture
                    .CallsConstrainedSiblingFromAsync));
        Assert.DoesNotContain(rows, row =>
            row.Caller.Name
                == nameof(ConstrainedSiblingFixture
                    .CallsAllowsRefStructFromAsync));
        Assert.DoesNotContain(rows, row =>
            row.Caller.Name
                == nameof(DerivedVirtualSelfSiblingFixture
                    .ReadAsync)
            && row.Caller.DeclaringType.Name
                == nameof(
                    DerivedVirtualSelfSiblingFixture));
        Assert.DoesNotContain(rows, row =>
            row.Caller.Name
                == nameof(UnsupportedSignatureSiblingFixture
                    .AnalyzeAsync)
            && row.Caller.DeclaringType.Name
                == nameof(
                    UnsupportedSignatureSiblingFixture));

        Assert.Single(
            rows,
            row => row.Caller.Name
                == nameof(GenericPrivateSiblingFixture<int>
                    .CallsPrivateSiblingFromAsync));

        Assert.DoesNotContain(rows, row =>
            row.Caller.Name
                == nameof(RequiredTokenSiblingFixture
                    .CallsRequiredTokenSiblingAsync));

        Assert.DoesNotContain(
            rows,
            row => row.Caller.Name
                == nameof(UnrelatedVirtualSiblingConsumer
                    .LoadAsync)
                && row.Caller.DeclaringType.Name
                    == nameof(
                        UnrelatedVirtualSiblingConsumer));

        Assert.DoesNotContain(
            rows,
            row => row.Caller.Name
                    == nameof(
                        NewSlotVirtualSiblingLeaf
                            .LoadAsync)
                && row.Caller.DeclaringType.Name
                    == nameof(
                        NewSlotVirtualSiblingLeaf));

        Assert.DoesNotContain(
            rows,
            row => row.Caller.Name
                    == nameof(
                        UnrelatedOverrideSiblingConsumer
                            .LoadAsync)
                && row.Caller.DeclaringType.Name
                    == nameof(
                        UnrelatedOverrideSiblingConsumer));
        Assert.DoesNotContain(
            rows,
            row => row.Caller.Name
                    == nameof(
                        GenericNewSlotSiblingLeaf<int, string>
                            .ReadAsync)
                && row.Caller.DeclaringType.Name
                    .StartsWith(
                        "GenericNewSlotSiblingLeaf",
                        StringComparison.Ordinal));
        Assert.DoesNotContain(
            rows,
            row => row.Caller.Name
                    == nameof(
                        GenericMatchingNewSlotSiblingLeaf<int>
                            .ReadAsync)
                && row.Caller.DeclaringType.Name
                    .StartsWith(
                        "GenericMatchingNewSlotSiblingLeaf",
                        StringComparison.Ordinal));

        Assert.Single(
            rows,
            row => row.Caller.Name
                == nameof(GenericInvocationSiblingFixture
                    .CallsDifferentInstantiationAsync));
        Assert.DoesNotContain(rows, row =>
            row.Caller.Name
                == nameof(GenericInvocationSiblingFixture
                    .CallsSameInstantiationAsync));
    }

    [Fact]
    public async Task AsyncSiblingMethodIndex_ConcurrentReadsBuildTypeOnce()
    {
        string path = typeof(OptimizationOpportunityFixtures)
            .Assembly.Location;
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle fixtureType = reader.TypeDefinitions.Single(
            handle => reader.StringComparer.Equals(
                reader.GetTypeDefinition(handle).Name,
                nameof(OptimizationOpportunityFixtures)));
        int methodCount = reader.GetTypeDefinition(fixtureType)
            .GetMethods()
            .Count;
        int scanned = 0;
        var index = new LibraryBodyAsyncSiblingMethodIndex(
            (definingReader, handle) =>
            {
                if (ReferenceEquals(definingReader, reader)
                    && definingReader.GetMethodDefinition(handle)
                        .GetDeclaringType() == fixtureType)
                {
                    Interlocked.Increment(ref scanned);
                    Thread.Sleep(1);
                }
            });
        using var start = new ManualResetEventSlim();
        Task<IReadOnlyDictionary<
            string,
            ImmutableArray<MethodDefinitionHandle>>>[] reads =
            Enumerable.Range(0, 16)
                .Select(_ => Task.Run(() =>
                {
                    start.Wait(TestContext.Current.CancellationToken);
                    return index.MethodsByName(
                        reader,
                        fixtureType);
                }, TestContext.Current.CancellationToken))
                .ToArray();

        start.Set();
        await Task.WhenAll(reads);

        Assert.Equal(methodCount, scanned);
        Assert.All(
            reads,
            read => Assert.Same(reads[0].Result, read.Result));
    }

    [Fact]
    public void AsyncSiblingProducer_InheritedSiblingUsesNearestNameLevel()
    {
        AsyncSiblingRow[] rows = AsyncSiblingTestExecution.Execute(
                typeof(OptimizationOpportunityFixtures)
                    .Assembly.Location)
            .Rows.ToArray();

        AsyncSiblingRow inherited = Assert.Single(
            rows,
            row => row.Caller.Name
                    == nameof(
                        InheritedAsyncSiblingDerived<int>
                            .AnalyzeAsync)
                && row.Caller.DeclaringType.Name
                    .StartsWith(
                        nameof(InheritedAsyncSiblingDerived<int>),
                        StringComparison.Ordinal));
        Assert.Equal(
            nameof(InheritedAsyncSiblingDerived<int>.Read),
            inherited.SynchronousCall.Callee.Name);
        Assert.Equal(
            nameof(InheritedAsyncSiblingBase<int>.ReadAsync),
            inherited.Alternative.Name);
        Assert.Equal(
            TypeRefKind.GenericInstance,
            inherited.Alternative.DeclaringType.Kind);
        Assert.Equal(
            TypeRefKind.GenericParameter,
            Assert.Single(
                inherited.Alternative
                    .ParameterTypes).Kind);
        Assert.DoesNotContain(
            rows,
            row => row.Caller.Name
                    == nameof(
                    HiddenInheritedAsyncSiblingDerived
                        .AnalyzeAsync)
                && row.Caller.DeclaringType.Name
                    == nameof(
                        HiddenInheritedAsyncSiblingDerived));
        Assert.Contains(
            rows,
            row => row.Caller.Name
                    == nameof(
                        NearestInheritedAsyncSiblingLeaf
                            .AnalyzeAsync)
                && row.Caller.DeclaringType.Name
                    == nameof(
                        NearestInheritedAsyncSiblingLeaf)
                && row.Alternative.DeclaringType.Name
                    == nameof(
                        NearestInheritedAsyncSiblingMiddle));
        Assert.Contains(
            rows,
            row => row.Caller.Name
                    == nameof(
                    ProtectedInheritedAsyncSiblingDerived
                        .AnalyzeAsync)
                && row.Caller.DeclaringType.Name
                    == nameof(
                        ProtectedInheritedAsyncSiblingDerived));
        Assert.DoesNotContain(
            rows,
            row => row.Caller.Name
                    == nameof(
                    ProtectedInheritedAsyncSiblingConsumer
                        .AnalyzeAsync)
                && row.Caller.DeclaringType.Name
                    == nameof(
                        ProtectedInheritedAsyncSiblingConsumer));
        Assert.Contains(
            rows,
            row => row.Caller.Name
                    == nameof(
                    InternalInheritedAsyncSiblingDerived
                        .AnalyzeAsync)
                && row.Caller.DeclaringType.Name
                    == nameof(
                        InternalInheritedAsyncSiblingDerived));
    }

    [Fact]
    public void
        AsyncSiblingProducer_InheritedSynchronousReceiverHidingFailsClosed()
    {
        AsyncSiblingRow[] rows = AsyncSiblingTestExecution.Execute(
                typeof(OptimizationOpportunityFixtures)
                    .Assembly.Location)
            .Rows.ToArray();

        Assert.DoesNotContain(
            rows,
            row => row.Caller.Name
                    == nameof(
                        HiddenInheritedSynchronousSiblingDerived
                            .AnalyzeAsync)
                && row.Caller.DeclaringType.Name
                    == nameof(
                        HiddenInheritedSynchronousSiblingDerived));
        Assert.DoesNotContain(
            rows,
            row => row.Caller.Name
                    == nameof(
                        InheritedSynchronousSiblingConsumer
                            .AnalyzeAsync)
                && row.Caller.DeclaringType.Name
                    == nameof(
                        InheritedSynchronousSiblingConsumer));
        Assert.DoesNotContain(
            rows,
            row => row.Caller.Name
                    == nameof(
                        InheritedSynchronousDerivedReceiverConsumer
                            .AnalyzeAsync)
                && row.Caller.DeclaringType.Name
                    == nameof(
                        InheritedSynchronousDerivedReceiverConsumer));
        Assert.DoesNotContain(
            rows,
            row => row.Caller.Name
                    == nameof(
                        InheritedSynchronousNameBypassConsumer
                            .ReadAsync)
                && row.Caller.DeclaringType.Name
                    == nameof(
                        InheritedSynchronousNameBypassConsumer));
        Assert.DoesNotContain(
            rows,
            row => row.Caller.Name
                    == nameof(
                        InterfaceInheritedSynchronousConsumer
                            .AnalyzeAsync)
                && row.Caller.DeclaringType.Name
                    == nameof(
                        InterfaceInheritedSynchronousConsumer));
        Assert.DoesNotContain(
            rows,
            row => row.Caller.Name
                    == nameof(
                        SameTypeInheritedSynchronousBase
                            .AnalyzeAsync)
                && row.Caller.DeclaringType.Name
                    == nameof(
                        SameTypeInheritedSynchronousBase));
        Assert.DoesNotContain(
            rows,
            row => row.Caller.Name
                    == nameof(
                        StaticInheritedSynchronousConsumer
                            .AnalyzeAsync)
                && row.Caller.DeclaringType.Name
                    == nameof(
                        StaticInheritedSynchronousConsumer));
        Assert.Contains(
            rows,
            row => row.Caller.Name
                    == nameof(
                        SealedSynchronousSiblingConsumer
                            .AnalyzeAsync)
                && row.Caller.DeclaringType.Name
                    == nameof(
                        SealedSynchronousSiblingConsumer));
    }

    [Fact]
    public void AsyncSiblingTypeMatching_RejectsMixedNonCoreOrigins()
    {
        MetadataTypeDefinitionName typeName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Sample",
                    ["Value"]))
            .Name;
        var currentIdentity =
            new AssemblyReferenceIdentity(
                "Collision",
                new Version(1, 0),
                null,
                null);
        TypeRef current = TypeRef.Definition(
            "Collision",
            "Sample",
            "Value",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.CurrentAssembly(
                    currentIdentity),
                typeName));
        TypeRef same = TypeRef.Definition(
            "Collision",
            "Sample",
            "Value",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.AssemblyReference(
                    currentIdentity),
                typeName));
        TypeRef different = TypeRef.Definition(
            "Collision",
            "Sample",
            "Value",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.AssemblyReference(
                    new AssemblyReferenceIdentity(
                        "Collision",
                        new Version(2, 0),
                        null,
                        null)),
                typeName));

        Assert.True(
            LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingTypesMatch(
                    current,
                    same));
        TypeRef differentCase = TypeRef.Definition(
            "cOLLISION",
            "Sample",
            "Value",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.AssemblyReference(
                    currentIdentity with
                    {
                        Name = "cOLLISION",
                    }),
                typeName));
        Assert.True(
            LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingTypesMatch(
                    current,
                    differentCase));
        Assert.False(
            LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingTypesMatch(
                    current,
                    different));
    }

    [Fact]
    public void AsyncSiblingProducer_PrefetchedImageDoesNotReopenRootPath()
    {
        string sourcePath = typeof(OptimizationOpportunityFixtures)
            .Assembly.Location;
        ImmutableArray<byte> image =
            [.. File.ReadAllBytes(sourcePath)];
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"DeletedAsyncSiblingRoot-{Guid.NewGuid():N}.dll");
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(sourcePath)
            {
                IncludeDepsJsonAssets = false,
                IncludeAspNetCoreSharedFramework = false,
                PreferImplementationAssemblies = true,
            });

        AsyncSiblingProducerResult result = AsyncSiblingTestExecution.Execute(
            missingPath,
            [.. image],
            resolver);

        Assert.Contains(
            result.Rows,
            row => row.Caller.Name
                    == nameof(OptimizationOpportunityFixtures
                        .CallsFileReadLinesFromAsync));
    }

    [Fact]
    public void AsyncSiblingTypeMatching_DistinguishesExactAssemblyReferenceIdentity()
    {
        MetadataTypeDefinitionName typeName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Sample",
                    ["Value"]))
            .Name;
        static TypeRef CreateType(
            Version version,
            MetadataTypeDefinitionName typeName)
        {
            var identity = new AssemblyReferenceIdentity(
                "Dependency",
                version,
                null,
                null);
            return TypeRef.Definition(
                "Dependency",
                "Sample",
                "Value",
                new ResolvableTypeReference(
                    new TypeReferenceOrigin
                        .AssemblyReference(identity),
                    typeName));
        }

        TypeRef versionOne =
            CreateType(new Version(1, 0), typeName);
        TypeRef versionTwo =
            CreateType(new Version(2, 0), typeName);

        Assert.Equal(versionOne, versionTwo);
        Assert.False(
            LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingTypesMatch(
                    versionOne,
                    versionTwo));
    }

    [Fact]
    public void AsyncSiblingTypeMatching_HonorsTrustedCoreLibraryFacades()
    {
        MetadataTypeDefinitionName typeName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "System.IO",
                    ["Stream"]))
            .Name;
        static TypeRef CreateType(
            string assemblyName,
            Version version,
            MetadataTypeDefinitionName typeName,
            bool trustedFrameworkAssembly = true)
        {
            var identity = new AssemblyReferenceIdentity(
                assemblyName,
                version,
                null,
                null);
            return TypeRef.Definition(
                assemblyName,
                "System.IO",
                "Stream",
                new ResolvableTypeReference(
                    new TypeReferenceOrigin
                        .AssemblyReference(identity),
                    typeName),
                trustedFrameworkAssembly);
        }

        TypeRef netstandard =
            CreateType(
                "netstandard",
                new Version(2, 0),
                typeName);
        TypeRef systemRuntime =
            CreateType(
                "System.Runtime",
                new Version(11, 0),
                typeName);
        TypeRef untrustedSystemRuntime =
            CreateType(
                "System.Runtime",
                new Version(11, 0),
                typeName,
                trustedFrameworkAssembly: false);
        TypeRef intrinsic = TypeRef.Definition(
            TypeRef.CoreLibrary,
            "System.IO",
            "Stream",
            new ResolvableTypeReference(
                new TypeReferenceOrigin
                    .IntrinsicCoreLibrary(),
                typeName));

        Assert.Equal(netstandard, systemRuntime);
        Assert.True(
            LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingTypesMatch(
                    netstandard,
                    systemRuntime));
        Assert.False(
            LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingTypesMatch(
                    netstandard,
                    untrustedSystemRuntime));
        Assert.True(
            LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingTypesMatch(
                    intrinsic,
                    systemRuntime));
        Assert.False(
            LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingTypesMatch(
                    intrinsic,
                    untrustedSystemRuntime));
    }

    [Theory]
    [InlineData("system.runtime")]
    [InlineData("SYSTEM.THREADING.TASKS")]
    public void AsyncSiblingFrameworkIdentity_IgnoresAssemblyNameCase(
        string assemblyName)
    {
        TypeRef task = TypeRef.Definition(
            assemblyName,
            "System.Threading.Tasks",
            "Task");

        Assert.True(
            FrameworkIdentity.IsKnownFrameworkType(
                task,
                "System.Threading.Tasks",
                "System.Threading.Tasks",
                "Task"));
    }

    [Fact]
    public void AsyncSiblingMethodMatching_PreservesOpenGenericSignature()
    {
        TypeRef declaring = TypeRef.Definition(
            "Sample",
            "Sample",
            "Api");
        TypeRef int32 = TypeRef.CoreLib(
            "System",
            "Int32");
        TypeRef methodParameter =
            TypeRef.MethodGenericParameter(0);
        var generic = new MemberRef(
            declaring,
            "Read",
            [int32],
            int32,
            MemberKind.Method)
        {
            TypeArguments = [int32],
            OpenParameterTypes = [methodParameter],
            OpenReturnType = int32,
            SignatureHeader = 0x10,
            RequiredParameterCount = 1,
            GenericArity = 1,
        };
        var concrete = generic with
        {
            OpenParameterTypes = [int32],
        };

        Assert.True(
            LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingMethodsMatch(
                    generic,
                    generic));
        Assert.False(
            LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingMethodsMatch(
                    generic,
                    concrete));
        var source = new MethodIdentity(
            "Sample",
            Guid.Empty,
            declaring,
            "Read",
            [int32],
            int32,
            0x06000001,
            IsStatic: true,
            GenericArity: 1)
        {
            SignatureHeader = 0x10,
            RequiredParameterCount = 1,
        };
        Assert.False(
            LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingMethodMatchesSource(
                    generic,
                    source));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AsyncSiblingCancellationTokenDefault_MustBeNull(
        bool validDefault,
        bool duplicateParameter)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("TokenDefault.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("TokenDefault"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle systemRuntime =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(
                    "System.Runtime"),
                new Version(11, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle cancellationToken =
            metadata.AddTypeReference(
                systemRuntime,
                metadata.GetOrAddString(
                    "System.Threading"),
                metadata.GetOrAddString(
                    "CancellationToken"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            metadata.GetOrAddString("Sample"),
            metadata.GetOrAddString("Api"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        ParameterHandle parameter =
            metadata.AddParameter(
                ParameterAttributes.Optional
                    | ParameterAttributes.HasDefault,
                metadata.GetOrAddString(
                    "cancellationToken"),
                sequenceNumber: 1);
        if (validDefault)
            metadata.AddConstant(parameter, null);
        else
            metadata.AddConstant(parameter, 0);
        if (duplicateParameter)
        {
            ParameterHandle duplicate =
                metadata.AddParameter(
                    ParameterAttributes.Optional
                        | ParameterAttributes.HasDefault,
                    metadata.GetOrAddString(
                        "duplicateCancellationToken"),
                    sequenceNumber: 1);
            metadata.AddConstant(duplicate, null);
        }
        var bodies = new BlobBuilder();
        var bodyEncoder =
            new MethodBodyStreamEncoder(bodies);
        var il = new BlobBuilder();
        il.WriteByte((byte)ILOpCode.Ret);
        int body = bodyEncoder.AddMethodBody(
            new InstructionEncoder(il),
            maxStack: 0);
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("ReadAsync"),
            metadata.GetOrAddBlob(
                new byte[]
                {
                    0x00, 0x01, 0x01, 0x11,
                    (byte)CodedIndex
                        .TypeDefOrRefOrSpec(
                            cancellationToken),
                }),
            body,
            parameter);
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        using var peReader = new PEReader(
            new MemoryStream(
                image.ToArray(),
                writable: false));
        MetadataReader reader =
            peReader.GetMetadataReader();

        Assert.Equal(
            validDefault && !duplicateParameter,
            LibraryBodyAsyncSiblingSignatureMatcher
                .TrailingParameterCanBeOmitted(
                    reader,
                    reader.GetMethodDefinition(
                        MetadataTokens
                            .MethodDefinitionHandle(1)),
                    parameterCount: 1));
    }

    [Fact]
    public void AsyncSiblingProducer_ClassicAsyncUsesMoveNextEvidenceCoordinate()
    {
        string path = typeof(ClassicAsyncSiblingFixture)
            .Assembly.Location;
        AsyncSiblingRow[] rows =
            AsyncSiblingTestExecution.Execute(path).Rows.ToArray();

        AsyncSiblingRow row = Assert.Single(
            rows,
            candidate => candidate.Caller.Name
                    == nameof(ClassicAsyncSiblingFixture
                        .CallsSyncSiblingFromAsync));
        DirectCall evidence = row.SynchronousCall;
        int evidenceMethodToken =
            evidence.EvidenceMethod.MetadataToken;

        Assert.NotEqual(
            row.Caller.MetadataToken,
            evidenceMethodToken);
        Assert.NotEqual(
            row.Caller,
            evidence.Caller);
        Assert.Equal(
            evidenceMethodToken,
            evidence.Caller.MetadataToken);
        Assert.Equal(
            nameof(ClassicAsyncSiblingFixture.ReadValue),
            evidence.Callee.Name);
        Assert.Equal(
            nameof(ClassicAsyncSiblingFixture.ReadValueAsync),
            row.Alternative.Name);
        Assert.Equal(
            "MoveNext",
            evidence.EvidenceMethod.Name);

        Assert.DoesNotContain(
            rows,
            opportunity => opportunity.Caller.DeclaringType.Name
                    == nameof(
                        ClassicGenericSelfSiblingFixture<int>)
                && opportunity.Caller.Name
                    == nameof(
                        ClassicGenericSelfSiblingFixture<int>
                            .ReadAsync));
        Assert.DoesNotContain(
            rows,
            opportunity => opportunity.Caller.DeclaringType.Name
                    == nameof(
                        ClassicGenericMethodSelfSiblingFixture)
                && opportunity.Caller.Name
                    == nameof(
                        ClassicGenericMethodSelfSiblingFixture
                            .ReadAsync));
        Assert.DoesNotContain(
            rows,
            opportunity => opportunity.Caller.Name
                    == nameof(
                        ClassicGenericInterfaceSelfSiblingFixture
                            .LoadAsync));
        Assert.DoesNotContain(
            rows,
            opportunity => opportunity.Caller.Name.EndsWith(
                    ".FetchAsync",
                    StringComparison.Ordinal));
        Assert.DoesNotContain(
            rows,
            opportunity => opportunity.Caller.Name
                    == nameof(
                        ClassicGenericVirtualSelfSiblingFixture
                            .LookupAsync));
        Assert.DoesNotContain(
            rows,
            opportunity => opportunity.Caller.Name
                    == nameof(
                        ClassicAsyncSiblingFixture
                            .CallsRefWithOutSiblingAsync));
        Assert.Single(
            rows,
            opportunity => opportunity.Caller.Name
                    == nameof(
                        ClassicAsyncSiblingFixture
                            .CallsCompatibleRefSiblingAsync));

        var collision = Assert.Single(
            rows,
            opportunity => opportunity.Caller.Name
                    == nameof(
                        ClassicStateMachineCollision<int>
                            .AnalyzeAsync)
                && opportunity.Caller.DeclaringType
                    .Resolution?.Type.Segments[0]
                    == "ClassicStateMachineCollision`1");
        Assert.Equal(
            "ClassicStateMachineCollision`1",
            collision.Caller.DeclaringType
                .Resolution?.Type.Segments[0]);

        Assert.DoesNotContain(
            rows,
            opportunity => opportunity.Caller.DeclaringType.Name
                    == nameof(
                        ClassicInterfaceCacheFixture)
                && opportunity.Caller.Name
                    == nameof(
                        ClassicInterfaceCacheFixture
                            .AaaOtherAsync));
        Assert.DoesNotContain(
            rows,
            opportunity => opportunity.Caller.DeclaringType.Name
                    == nameof(
                        ClassicInterfaceCacheFixture)
                && opportunity.Caller.Name
                    == nameof(
                        ClassicInterfaceCacheFixture
                            .ReadAsync));
        Assert.Contains(
            rows,
            opportunity => opportunity.Caller.Name
                    == nameof(
                        ClassicSelfCacheFixture
                            .ZzzAnalyzeAsync));
        Assert.DoesNotContain(
            rows,
            opportunity => opportunity.Caller.Name
                    == nameof(
                        ClassicSelfCacheFixture
                            .AaaAsync));
        Assert.DoesNotContain(
            rows,
            opportunity => opportunity.Caller.DeclaringType.Name
                    == nameof(
                        ClassicProtectedSiblingDerivedFixture)
                && opportunity.Caller.Name
                    == nameof(
                        ClassicProtectedSiblingDerivedFixture
                            .AnalyzeAsync));
        Assert.DoesNotContain(
            rows,
            opportunity => opportunity.Caller.DeclaringType.Name
                    == nameof(
                        ClassicPrivateProtectedSiblingDerivedFixture)
                && opportunity.Caller.Name
                    == nameof(
                        ClassicPrivateProtectedSiblingDerivedFixture
                            .AnalyzeAsync));
        Assert.DoesNotContain(
            rows,
            opportunity => opportunity.Caller.DeclaringType.Name
                    == nameof(
                        ClassicCovariantInterfaceSelfSiblingFixture)
                && opportunity.Caller.Name
                    == nameof(
                        ClassicCovariantInterfaceSelfSiblingFixture
                            .ReadAsync));
        Assert.DoesNotContain(
            rows,
            opportunity => opportunity.Caller.DeclaringType.Name
                    == nameof(
                        ClassicProtectedReceiverDerivedFixture)
                && opportunity.Caller.Name
                    == nameof(
                        ClassicProtectedReceiverDerivedFixture
                            .AnalyzeAsync));
        Assert.DoesNotContain(
            rows,
            opportunity => opportunity.Caller.DeclaringType.Name
                    == nameof(
                        ClassicProtectedStaticSiblingDerivedFixture)
                && opportunity.Caller.Name
                    == nameof(
                        ClassicProtectedStaticSiblingDerivedFixture
                            .AnalyzeAsync));
        Assert.DoesNotContain(
            rows,
            opportunity => opportunity.Caller.DeclaringType.Name
                    == "IClassicContravariantDefaultSiblingFixture`1"
                && opportunity.Caller.Name
                    == nameof(
                        IClassicContravariantDefaultSiblingFixture<
                            object>.ConsumeAsync));
        Assert.DoesNotContain(
            rows,
            opportunity => opportunity.Caller.DeclaringType.Name
                    == nameof(
                        ClassicUnrelatedExplicitDefaultSiblingFixture)
                && opportunity.Caller.Name.EndsWith(
                    ".ReadAsync",
                    StringComparison.Ordinal));
        Assert.Contains(
            rows,
            opportunity => opportunity.Caller.DeclaringType.Name
                    .EndsWith(
                        "+"
                        + nameof(
                            ClassicNestedPrivateSiblingFixture.Consumer),
                        StringComparison.Ordinal)
                && opportunity.Caller.Name
                    == nameof(
                        ClassicNestedPrivateSiblingFixture.Consumer
                            .AnalyzeAsync));
        Assert.DoesNotContain(
            rows,
            opportunity => opportunity.Caller.DeclaringType.Name
                    == nameof(
                        IClassicHiddenDerivedSiblingFixture)
                && opportunity.Caller.Name
                    == nameof(
                        IClassicHiddenDerivedSiblingFixture
                            .ReadAsync));
    }

    [Fact]
    public void
        AsyncSiblingProducer_PrivateAccessIsDirectionalAcrossNestedTypes()
    {
        AsyncSiblingRow[] rows = AsyncSiblingTestExecution.Execute(
                typeof(ClassicAsyncSiblingFixture)
                    .Assembly.Location)
            .Rows.ToArray();

        Assert.Contains(
            rows,
            row => row.Caller.DeclaringType.Name
                    .EndsWith(
                        "+"
                        + nameof(
                            ClassicNestedPrivateSiblingFixture.Consumer),
                        StringComparison.Ordinal)
                && row.Caller.Name
                    == nameof(
                        ClassicNestedPrivateSiblingFixture.Consumer
                            .AnalyzeAsync));
        Assert.DoesNotContain(
            rows,
            row => row.Caller.DeclaringType.Name
                    == nameof(
                        ClassicOuterToNestedPrivateSiblingFixture)
                && row.Caller.Name
                    == nameof(
                        ClassicOuterToNestedPrivateSiblingFixture
                            .AnalyzeAsync));
        Assert.DoesNotContain(
            rows,
            row => row.Caller.DeclaringType.Name
                    .Contains(
                        nameof(
                            ClassicSiblingNestedPrivateSiblingFixture),
                        StringComparison.Ordinal)
                && row.Caller.DeclaringType.Name
                    .EndsWith(
                        "+"
                        + nameof(
                            ClassicSiblingNestedPrivateSiblingFixture
                                .Consumer),
                        StringComparison.Ordinal)
                && row.Caller.Name
                    == nameof(
                        ClassicSiblingNestedPrivateSiblingFixture
                            .Consumer.AnalyzeAsync));
    }

    [Fact]
    public void AsyncSiblingPrivateAccess_CyclicDeclaringTypeFailsClosed()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("CyclicNestedType.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("CyclicNestedType"),
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
        TypeDefinitionHandle cyclic =
            metadata.AddTypeDefinition(
                TypeAttributes.NestedPrivate,
                metadata.GetOrAddString("Sample"),
                metadata.GetOrAddString("Cyclic"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddNestedType(cyclic, cyclic);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        using var peReader =
            new PEReader(
                new MemoryStream(image.ToArray()));

        Assert.False(
            LibraryBodyAsyncSiblingAccessibilityAnalyzer
                .TryTopLevelType(
                peReader.GetMetadataReader(),
                cyclic,
                out _));
    }

    [Fact]
    public void
        AsyncSiblingProducer_FriendAccessRequiresProvableReceiver()
    {
        string path =
            FixtureCatalog.AnalysisAsyncSiblingFriend
                .AssemblyPath();
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path)
            {
                IncludeDepsJsonAssets = true,
                IncludeAspNetCoreSharedFramework = false,
                PreferImplementationAssemblies = true,
            });

        AsyncSiblingProducerResult result =
            AsyncSiblingTestExecution.Execute(path, resolver);

        Assert.DoesNotContain(
            result.Rows,
            row => row.Caller.Name
                    == "AnalyzeAsync"
                && row.Caller.DeclaringType.Name
                    == "FriendProtectedReceiver");
        Assert.DoesNotContain(
            result.Rows,
            row => row.Caller.Name
                    == "PublicAnalyzeAsync");
        Assert.DoesNotContain(
            result.Rows,
            row => row.Caller.Name
                    == "InternalAnalyzeAsync");
        Assert.Contains(
            result.Rows,
            row => row.Caller.Name
                    == "AnalyzeAsync"
                && row.Caller.DeclaringType.Name
                    == "FriendSiblingConsumer"
                && row.Alternative.DeclaringType.Name
                    == "FriendSiblingGrantor"
                && row.Alternative.Name == "ReadAsync");
        AnalysisDiagnostic diagnostic =
            Assert.Single(result.Diagnostics);
        Assert.NotEqual(0, diagnostic.MethodToken);
        Assert.Contains(
            "BadImageFormatException",
            diagnostic.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AsyncSiblingSelection_ExactCandidateWinsRegardlessOfOrder()
    {
        TypeRef declaring = TypeRef.Definition(
            "Sample",
            "Sample",
            "Api");
        TypeRef int32 = TypeRef.CoreLib(
            "System",
            "Int32");
        MemberRef exact = new(
            declaring,
            "ReadAsync",
            [int32],
            int32,
            MemberKind.Method);
        MemberRef optionalOne = exact with
        {
            ParameterTypes =
            [
                int32,
                TypeRef.CoreLib(
                    "System.Threading",
                    "CancellationToken"),
            ],
        };
        MemberRef optionalTwo = optionalOne with
        {
            ReturnType = TypeRef.CoreLib(
                "System.Threading.Tasks",
                "ValueTask"),
        };

        AssertSelection(
            [optionalOne, optionalTwo, exact],
            exact);
        AssertSelection(
            [exact, optionalOne, optionalTwo],
            exact);

        static void AssertSelection(
            MemberRef[] candidates,
            MemberRef expected)
        {
            MemberRef? best = null;
            bool ambiguous = false;
            foreach (MemberRef candidate in candidates)
            {
                LibraryBodyAsyncSiblingCandidateResolver
                    .ConsiderAsyncSibling(
                        candidate,
                        ref best,
                        ref ambiguous);
            }
            Assert.Same(expected, best);
            Assert.False(ambiguous);
        }
    }

    [Fact]
    public void ConstructedInterfaceIdentity_RequiresMatchingArguments()
    {
        TypeRef interfaceDefinition = TypeRef.Definition(
            "Sample",
            "Sample",
            "IReader`1");
        TypeRef int32 = TypeRef.CoreLib(
            "System",
            "Int32");
        TypeRef text = TypeRef.CoreLib(
            "System",
            "String");
        TypeRef intReader = TypeRef.GenericInstance(
            interfaceDefinition,
            [int32]);
        TypeRef stringReader = TypeRef.GenericInstance(
            interfaceDefinition,
            [text]);

        Assert.True(
            LibraryBodyAsyncSiblingDispatchAnalyzer
                .ConstructedTypeArgumentsMatch(
                    intReader,
                    intReader));
        Assert.False(
            LibraryBodyAsyncSiblingDispatchAnalyzer
                .ConstructedTypeArgumentsMatch(
                    intReader,
                    stringReader));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AsyncSiblingProducer_DuplicateLocalTypeIdentityFailsClosed(
        bool useMemberReference)
    {
        byte[] image = BuildDuplicateLocalTypeAssembly(
            useMemberReference);
        AsyncSiblingProducerResult result =
            AsyncSiblingTestExecution.Execute(
            "DuplicateLocalTypes.dll",
            image);

        Assert.Empty(result.Rows);
    }

    [Fact]
    public void MethodImplSignature_RequiresByRefDirection()
    {
        TypeRef declaring = TypeRef.Definition(
            "Probe",
            "Sample",
            "Reader");
        TypeRef byRefInt = TypeRef.ByRef(
            TypeRef.CoreLib("System", "Int32"));
        TypeRef task = TypeRef.Definition(
            "System.Runtime",
            "System.Threading.Tasks",
            "Task",
            trustedFrameworkAssembly: true);
        var body = new MemberRef(
            declaring,
            "AnalyzeAsync",
            [byRefInt],
            task,
            MemberKind.Method)
        {
            HasThis = true,
            SignatureHeader = 0x20,
            RequiredParameterCount = 1,
            ParameterDirections =
                [ParameterDirection.Ref],
        };
        var declaration = body with
        {
            Name = "OtherAsync",
            ParameterDirections =
                [ParameterDirection.Out],
        };

        Assert.False(
            LibraryBodyAsyncSiblingDispatchAnalyzer
                .SameMethodImplSignature(
                    body,
                    declaration));
    }

    [Theory]
    [InlineData(
        false,
        ParameterAttributes.None,
        ParameterAttributes.None,
        true)]
    [InlineData(
        true,
        ParameterAttributes.None,
        ParameterAttributes.None,
        true)]
    [InlineData(
        true,
        ParameterAttributes.Out,
        ParameterAttributes.Out,
        true)]
    [InlineData(
        true,
        ParameterAttributes.In,
        ParameterAttributes.In,
        true)]
    [InlineData(
        true,
        ParameterAttributes.None,
        ParameterAttributes.Out,
        false)]
    public void AsyncSiblingProducer_ResolvedMemberRefUsesParamDirection(
        bool byRef,
        ParameterAttributes synchronousDirection,
        ParameterAttributes asynchronousDirection,
        bool expected)
    {
        byte[] dependency =
            BuildDirectionProbeDependency(
                byRef,
                synchronousDirection,
                asynchronousDirection);
        byte[] caller =
            BuildDirectionProbeCaller(byRef);

        AsyncSiblingProducerResult result =
            AsyncSiblingTestExecution.Execute(
                "DirectionProbeCaller.dll",
                caller,
                new DirectionProbeResolver(dependency));

        int count = result.Rows.Count(
            row => row.Caller.Name == "AnalyzeAsync");
        Assert.Equal(expected ? 1 : 0, count);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void AsyncSiblingProducer_AmbiguousResolvedMemberRefDirectionFailsClosed()
    {
        byte[] dependency =
            BuildDirectionProbeDependency(
                byRef: true,
                ParameterAttributes.None,
                ParameterAttributes.None,
                duplicateSynchronous: true);
        byte[] caller =
            BuildDirectionProbeCaller(byRef: true);
        AsyncSiblingProducerResult result =
            AsyncSiblingTestExecution.Execute(
                "DirectionProbeCaller.dll",
                caller,
                new DirectionProbeResolver(dependency));

        Assert.DoesNotContain(
            result.Rows,
            row => row.Caller.Name == "AnalyzeAsync");
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void AsyncSiblingProducer_UnresolvedSynchronousMemberFailsClosed()
    {
        byte[] dependency =
            BuildDirectionProbeDependency(
                byRef: false,
                ParameterAttributes.None,
                ParameterAttributes.None,
                synchronousName: "Gone");
        byte[] caller =
            BuildDirectionProbeCaller(byRef: false);
        AsyncSiblingProducerResult result =
            AsyncSiblingTestExecution.Execute(
                "DirectionProbeCaller.dll",
                caller,
                new DirectionProbeResolver(dependency));

        Assert.DoesNotContain(
            result.Rows,
            row => row.Caller.Name == "AnalyzeAsync");
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void AsyncSiblingProducer_StaticMetadataMustBeConsistent(
        bool asynchronousMethodIsStatic,
        int expected)
    {
        byte[] dependency =
            BuildDirectionProbeDependency(
                byRef: false,
                ParameterAttributes.None,
                ParameterAttributes.None,
                asynchronousMethodIsStatic:
                    asynchronousMethodIsStatic);
        byte[] caller =
            BuildDirectionProbeCaller(byRef: false);
        AsyncSiblingProducerResult result =
            AsyncSiblingTestExecution.Execute(
                "DirectionProbeCaller.dll",
                caller,
                new DirectionProbeResolver(dependency));

        Assert.Equal(
            expected,
            result.Rows.Count(
                row => row.Caller.Name == "AnalyzeAsync"));
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void AsyncSiblingProducer_GenericCountMatchesRows(
        bool addAsynchronousGenericParameter,
        int expected)
    {
        byte[] dependency =
            BuildDirectionProbeDependency(
                byRef: false,
                ParameterAttributes.None,
                ParameterAttributes.None,
                genericSignature: true,
                addAsynchronousGenericParameter:
                    addAsynchronousGenericParameter);
        byte[] caller =
            BuildDirectionProbeCaller(
                byRef: false,
                genericSignature: true);
        AsyncSiblingProducerResult result =
            AsyncSiblingTestExecution.Execute(
                "DirectionProbeCaller.dll",
                caller,
                new DirectionProbeResolver(dependency));

        Assert.Equal(
            expected,
            result.Rows.Count(
                row => row.Caller.Name == "AnalyzeAsync"));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void AsyncSiblingProducer_RuntimeAsyncIgnoresClassicAttribute()
    {
        byte[] dependency =
            BuildDirectionProbeDependency(
                byRef: false,
                ParameterAttributes.None,
                ParameterAttributes.None);
        byte[] caller =
            BuildDirectionProbeCaller(
                byRef: false,
                addStateMachineAttribute: true);
        AsyncSiblingProducerResult result =
            AsyncSiblingTestExecution.Execute(
                "DirectionProbeCaller.dll",
                caller,
                new DirectionProbeResolver(dependency));

        Assert.Single(
            result.Rows,
            row => row.Caller.Name == "AnalyzeAsync");
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void AsyncSiblingProducer_MvidCollisionPreservesRecursiveInterfaceSuppression()
    {
        Guid dependencyMvid = Guid.Parse(
            "11111111-2222-3333-4444-555555555555");
        byte[] dependency =
            BuildMvidCollisionDependency(
                dependencyMvid);
        var resolver =
            new MvidCollisionResolver(dependency);
        byte[] collisionImage = BuildMvidCollisionRoot(
            dependencyMvid);
        byte[] distinctImage = BuildMvidCollisionRoot(
            Guid.Parse(
                "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"));
        AsyncSiblingProducerResult collision =
            AsyncSiblingTestExecution.Execute(
                "CollisionRoot.dll",
                collisionImage,
                resolver);
        AsyncSiblingProducerResult distinct =
            AsyncSiblingTestExecution.Execute(
                "CollisionRoot.dll",
                distinctImage,
                resolver);
        LibraryBodyAnalysisExecution collisionEvidence =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "CollisionRoot.dll",
                [.. collisionImage],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                resolver);

        Assert.Contains(
            collisionEvidence.CallGraph.DirectCalls,
            call => call.Caller.Name
                    == "ReadAsync"
                && call.Callee.Name == "Read"
                && call.Callee.DeclaringType.Name
                    == "IReader");
        Assert.Empty(collision.Diagnostics);
        Assert.DoesNotContain(
            distinct.Rows,
            row => row.Caller.Name == "ReadAsync");
        Assert.DoesNotContain(
            collision.Rows,
            row => row.Caller.Name == "ReadAsync");
    }
}
