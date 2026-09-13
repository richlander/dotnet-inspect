using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyPairCallUseQueryTests
{
    [Fact]
    public void ExecuteReturnsExactCallsAcrossBothPairDirections()
    {
        using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());

        AssemblyPairCallUseResult result =
            AssemblyPairCallUseQuery.Execute(
                context.Group,
                context.Second,
                context.First);

        Assert.True(result.IsComplete);
        Assert.Empty(result.Failures);
        AssemblyPairCallUseOccurrence direct = Assert.Single(
            result.Occurrences,
            occurrence =>
                occurrence.Source.Identity.Name
                    == "ILInspector.Analysis.CallerGraphCaller"
                && occurrence.SourceMethod.Name == "Run"
                && occurrence.Target.Identity.Name
                    == "ILInspector.Analysis.CallerGraphTarget"
                && occurrence.TargetMethod.Name == "Ping"
                && occurrence.Call.Kind == Analysis.CallKind.Call);
        Assert.True(direct.Call.ExactTarget);
        AssemblyPairCallUseOccurrence openVirtual = Assert.Single(
            result.Occurrences,
            occurrence =>
                occurrence.SourceMethod.Name == "CallBodiless"
                && occurrence.TargetMethod.Name == "Invoke"
                && occurrence.Call.Kind
                    == Analysis.CallKind.CallVirtual);
        Assert.False(openVirtual.Call.ExactTarget);
        AssemblyPairCallUseOccurrence constructor = Assert.Single(
            result.Occurrences,
            occurrence =>
                occurrence.SourceMethod.Name == "UseBox"
                && occurrence.TargetMethod.Name == ".ctor"
                && occurrence.Call.Kind
                    == Analysis.CallKind.NewObject);
        Assert.True(constructor.Call.ExactTarget);
        Assert.DoesNotContain(
            result.Occurrences,
            occurrence =>
                occurrence.Call.Kind is
                    Analysis.CallKind.LoadFunction
                    or Analysis.CallKind.LoadVirtualFunction
                    or Analysis.CallKind.CallIndirect);
        Assert.All(
            result.Occurrences,
            occurrence =>
                Assert.Equal(
                    occurrence.SourceMethod.MetadataToken,
                    occurrence.Call.Caller.MetadataToken));
    }

    [Fact]
    public void ProjectionRetainsEveryOccurrenceInBothSummaryViews()
    {
        using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        AssemblyPairCallUseResult result =
            AssemblyPairCallUseQuery.Execute(
                context.Group,
                context.First,
                context.Second);

        AssemblyPairCallUseProjection projection =
            AssemblyPairCallUseProjection.Create(result);

        Assert.Same(result, projection.Pair);
        Assert.True(projection.IsComplete);
        Assert.Equal(
            Enumerable.Range(0, result.Occurrences.Length),
            projection.ConsumerUseSites
                .SelectMany(site => site.OccurrenceIndexes)
                .Order());
        Assert.Equal(
            Enumerable.Range(0, result.Occurrences.Length),
            projection.ProviderApiTypes
                .SelectMany(type => type.OccurrenceIndexes)
                .Order());
        Assert.All(
            projection.ConsumerUseSites,
            site =>
            {
                AssemblyPairCallUseOccurrence[] occurrences =
                [.. site.OccurrenceIndexes.Select(
                    index => result.Occurrences[index])];
                Assert.All(
                    occurrences,
                    occurrence =>
                    {
                        Assert.Same(
                            site.Source.Registration,
                            occurrence.Source.Registration);
                        Assert.Equal(
                            site.SourceModuleVersionId,
                            occurrence.SourceModuleVersionId);
                        Assert.Equal(
                            site.SourceMethod.MetadataToken,
                            occurrence.SourceMethod.MetadataToken);
                        Assert.Same(
                            site.Target.Registration,
                            occurrence.Target.Registration);
                    });
                Assert.Equal(
                    occurrences
                        .Select(occurrence =>
                            occurrence.TargetMethod.DeclaringType)
                        .Distinct(),
                    site.TargetTypes);
                Assert.Equal(
                    occurrences
                        .Select(occurrence => occurrence.TargetMethod)
                        .Distinct(),
                    site.TargetMethods);
            });
        Assert.All(
            projection.ProviderApiTypes,
            type =>
            {
                AssemblyPairCallUseOccurrence[] occurrences =
                [.. type.OccurrenceIndexes.Select(
                    index => result.Occurrences[index])];
                Assert.All(
                    occurrences,
                    occurrence =>
                    {
                        Assert.Same(
                            type.Source.Registration,
                            occurrence.Source.Registration);
                        Assert.Same(
                            type.Target.Registration,
                            occurrence.Target.Registration);
                        Assert.Equal(
                            type.TargetModuleVersionId,
                            occurrence.TargetModuleVersionId);
                        Assert.Equal(
                            type.TargetType,
                            occurrence.TargetMethod.DeclaringType);
                    });
                Assert.Equal(
                    occurrences
                        .Select(occurrence => occurrence.SourceMethod)
                        .Distinct(),
                    type.SourceMethods);
                Assert.Equal(
                    occurrences
                        .Select(occurrence => occurrence.TargetMethod)
                        .Distinct(),
                    type.TargetMethods);
            });

        AssemblyPairCallUseConsumerUseSite repeated =
            Assert.Single(
                projection.ConsumerUseSites,
                site => site.SourceMethod.Name == "RunTwice");
        Assert.Equal(2, repeated.CallSiteCount);
        Assert.Single(repeated.TargetTypes);
        Assert.Single(repeated.TargetMethods);
    }

    [Fact]
    public void ProjectionOrderIsIndependentOfRequestArgumentOrder()
    {
        using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());

        AssemblyPairCallUseProjection forward =
            AssemblyPairCallUseProjection.Create(
                AssemblyPairCallUseQuery.Execute(
                    context.Group,
                    context.First,
                    context.Second));
        AssemblyPairCallUseProjection reverse =
            AssemblyPairCallUseProjection.Create(
                AssemblyPairCallUseQuery.Execute(
                    context.Group,
                    context.Second,
                    context.First));

        Assert.Equal(
            forward.ConsumerUseSites.Select(ConsumerFingerprint),
            reverse.ConsumerUseSites.Select(ConsumerFingerprint));
        Assert.Equal(
            forward.ProviderApiTypes.Select(ProviderFingerprint),
            reverse.ProviderApiTypes.Select(ProviderFingerprint));
    }

    [Fact]
    public void ExecuteRejectsARegistrationOutsideTheGroup()
    {
        using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        ResolvedAssemblyReference outside =
            ResolvedAssemblyReference.CreateFromPath(
                FixtureCatalog.AnalysisCallerGraphTargetV2
                    .AssemblyPath(),
                AssemblyResolutionProvenance.Local(
                    "pairwise query outside participant"));

        Assert.Throws<AssemblyPairCallUseRequestException>(
            () => AssemblyPairCallUseQuery.Execute(
                context.Group,
                context.First,
                outside));
    }

    [Fact]
    public void ExecuteDoesNotTurnVersionSkewIntoAnAbsenceClaim()
    {
        using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath());

        AssemblyPairCallUseResult result =
            AssemblyPairCallUseQuery.Execute(
                context.Group,
                context.First,
                context.Second);

        Assert.False(result.IsComplete);
        Assert.True(
            result.Diagnostics.UnresolvedCandidateCallCount > 0);
        Assert.Empty(result.Occurrences);
    }

    [Fact]
    public void SameNameParticipantsDoNotTurnLocalCallsIntoPairGaps()
    {
        using PairContext context = PairContext.Create(
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath(),
            FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath());

        AssemblyPairCallUseResult result =
            AssemblyPairCallUseQuery.Execute(
                context.Group,
                context.First,
                context.Second);

        Assert.True(result.IsComplete);
        Assert.Equal(
            0,
            result.Diagnostics.UnresolvedCandidateCallCount);
        Assert.Empty(result.Occurrences);
    }

    [Fact]
    public void PairRelevantAssemblyReferenceIgnoresOnlyVersion()
    {
        var target = new AssemblyReferenceIdentity(
            "Dependency",
            new Version(2, 0, 0, 0),
            "neutral",
            "b03f5f7f11d50a3a");
        AssemblyReferenceIdentity versionSkewed = target with
        {
            Version = new Version(1, 0, 0, 0),
            Culture = null,
        };

        Assert.True(
            AssemblyPairCallUseQuery
                .IsPairRelevantAssemblyReference(
                    versionSkewed,
                    target));
        Assert.False(
            AssemblyPairCallUseQuery
                .IsPairRelevantAssemblyReference(
                    versionSkewed with
                    {
                        PublicKeyToken = null,
                    },
                    target));
        Assert.False(
            AssemblyPairCallUseQuery
                .IsPairRelevantAssemblyReference(
                    versionSkewed with
                    {
                        Culture = "fr-FR",
                    },
                    target));
    }

    [Fact]
    public void ExecuteRejectsDistinctRegistrationsForTheSamePhysicalImage()
    {
        string path =
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        using PairContext context = PairContext.Create(path, path);

        AssemblyPairCallUseRequestException exception =
            Assert.Throws<AssemblyPairCallUseRequestException>(
                () => AssemblyPairCallUseQuery.Execute(
                context.Group,
                context.First,
                context.Second));

        Assert.Contains(
            "distinct physical assembly artifacts",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExecuteCarriesRejectedParticipantBesideAvailableEvidence()
    {
        string callerPath =
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath();
        string targetPath =
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        ResolvedAssemblyReference caller =
            ResolvedAssemblyReference.CreateFromPath(
                callerPath,
                AssemblyResolutionProvenance.Local(
                    "pairwise call-use test"));
        ResolvedAssemblyReference targetIdentity =
            ResolvedAssemblyReference.CreateFromPath(
                targetPath,
                AssemblyResolutionProvenance.Local(
                    "pairwise call-use test"));
        ResolvedAssemblyReference malformed =
            ResolvedAssemblyReference.Create(
                targetIdentity.Identity,
                path: null,
                () => new MemoryStream([0x00, 0x01, 0x02]),
                AssemblyResolutionProvenance.Local(
                    "malformed pairwise call-use test"));
        using PairContext context =
            PairContext.Create(caller, malformed, callerPath);

        AssemblyPairCallUseResult result =
            AssemblyPairCallUseQuery.Execute(
                context.Group,
                context.First,
                context.Second);

        Assert.False(result.IsComplete);
        Assert.Equal(2, result.Subjects.Length);
        Assert.Single(result.Participants);
        Assert.IsType<AssemblyPairCallUseFailure.Rejected>(
            Assert.Single(result.Failures));
        Assert.Empty(result.Occurrences);
    }

    static string ConsumerFingerprint(
        AssemblyPairCallUseConsumerUseSite site) =>
        string.Join(
        "|",
        site.Source.Identity.Name,
        site.SourceModuleVersionId,
        site.SourceMethod.MetadataToken,
        site.Target.Identity.Name,
        site.TargetModuleVersionId,
        string.Join(
            ",",
            site.TargetTypes.Select(
                type => type.ToQualifiedDisplayString())),
        string.Join(
            ",",
            site.TargetMethods.Select(
                method => method.MetadataToken)),
        string.Join(",", site.OccurrenceIndexes));

    static string ProviderFingerprint(
        AssemblyPairCallUseProviderApiType type) =>
        string.Join(
        "|",
        type.Source.Identity.Name,
        type.SourceModuleVersionId,
        type.Target.Identity.Name,
        type.TargetModuleVersionId,
        type.TargetType.ToQualifiedDisplayString(),
        string.Join(
            ",",
            type.SourceMethods.Select(
                method => method.MetadataToken)),
        string.Join(
            ",",
            type.TargetMethods.Select(
                method => method.MetadataToken)),
        string.Join(",", type.OccurrenceIndexes));

    [Fact]
    public void ExecuteSeparatesFunctionPointerDependenciesInPlanCache()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"pairwise-function-pointer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var dependencyV1 = new AssemblyReferenceIdentity(
                "Collision.Dependency",
                new Version(1, 0, 0, 0),
                Culture: null,
                PublicKeyToken: "0011223344556677");
            var dependencyV2 = dependencyV1 with
            {
                Version = new Version(2, 0, 0, 0),
                PublicKeyToken = "8899aabbccddeeff",
            };
            string providerPath = Path.Combine(
                directory,
                "FunctionPointer.Provider.dll");
            string callerPath = Path.Combine(
                directory,
                "FunctionPointer.Caller.dll");
            File.WriteAllBytes(
                providerPath,
                BuildFunctionPointerProvider(
                    dependencyV1,
                    dependencyV2));
            File.WriteAllBytes(
                callerPath,
                BuildFunctionPointerCaller(
                    dependencyV1,
                    dependencyV2));
            using PairContext context = PairContext.Create(
                callerPath,
                providerPath);

            AssemblyPairCallUseResult result =
                AssemblyPairCallUseQuery.Execute(
                    context.Group,
                    context.First,
                    context.Second);

            Assert.True(
                result.IsComplete,
                $"failures={result.Failures.Length}; "
                + $"unresolved={result.Diagnostics.UnresolvedCandidateCallCount}; "
                + $"occurrences={result.Occurrences.Length}; "
                + $"diagnostics={string.Join(
                    " | ",
                    result.Participants.SelectMany(
                        participant => participant.Diagnostics))}");
            Assert.Equal(
                0,
                result.Diagnostics.UnresolvedCandidateCallCount);
            Assert.Collection(
                result.Occurrences,
                occurrence =>
                {
                    Assert.Equal(
                        "CallFirst",
                        occurrence.SourceMethod.Name);
                    Assert.Equal(
                        "Use",
                        occurrence.TargetMethod.Name);
                },
                occurrence =>
                {
                    Assert.Equal(
                        "CallSecond",
                        occurrence.SourceMethod.Name);
                    Assert.Equal(
                        "Use",
                        occurrence.TargetMethod.Name);
                });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static byte[] BuildFunctionPointerProvider(
        AssemblyReferenceIdentity dependencyV1,
        AssemblyReferenceIdentity dependencyV2)
    {
        MetadataBuilder metadata =
            AssemblyMetadata("FunctionPointer.Provider");
        TypeReferenceHandle typeV1 = AddDependencyType(
            metadata,
            dependencyV1);
        TypeReferenceHandle typeV2 = AddDependencyType(
            metadata,
            dependencyV2);
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            metadata.GetOrAddString("Provider"),
            metadata.GetOrAddString("Api"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        var methodBodies = new BlobBuilder();
        var bodyEncoder =
            new MethodBodyStreamEncoder(methodBodies);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Use"),
            AddFunctionPointerUseSignature(metadata, typeV1),
            AddRetBody(bodyEncoder),
            parameterList: MetadataTokens.ParameterHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Use"),
            AddFunctionPointerUseSignature(metadata, typeV2),
            AddRetBody(bodyEncoder),
            parameterList: MetadataTokens.ParameterHandle(1));
        return Serialize(metadata, methodBodies);
    }

    static byte[] BuildFunctionPointerCaller(
        AssemblyReferenceIdentity dependencyV1,
        AssemblyReferenceIdentity dependencyV2)
    {
        MetadataBuilder metadata =
            AssemblyMetadata("FunctionPointer.Caller");
        var provider = new AssemblyReferenceIdentity(
            "FunctionPointer.Provider",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        AssemblyReferenceHandle providerReference =
            AddAssemblyReference(metadata, provider);
        TypeReferenceHandle providerType =
            metadata.AddTypeReference(
                providerReference,
                metadata.GetOrAddString("Provider"),
                metadata.GetOrAddString("Api"));
        TypeReferenceHandle typeV1 = AddDependencyType(
            metadata,
            dependencyV1);
        TypeReferenceHandle typeV2 = AddDependencyType(
            metadata,
            dependencyV2);
        MemberReferenceHandle useV1 =
            metadata.AddMemberReference(
                providerType,
                metadata.GetOrAddString("Use"),
                AddFunctionPointerUseSignature(
                    metadata,
                    typeV1));
        MemberReferenceHandle useV2 =
            metadata.AddMemberReference(
                providerType,
                metadata.GetOrAddString("Use"),
                AddFunctionPointerUseSignature(
                    metadata,
                    typeV2));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            metadata.GetOrAddString("Consumer"),
            metadata.GetOrAddString("Calls"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        var methodBodies = new BlobBuilder();
        var bodyEncoder =
            new MethodBodyStreamEncoder(methodBodies);
        AddCallMethod(
            metadata,
            bodyEncoder,
            "CallFirst",
            useV1);
        AddCallMethod(
            metadata,
            bodyEncoder,
            "CallSecond",
            useV2);
        return Serialize(metadata, methodBodies);
    }

    static void AddCallMethod(
        MetadataBuilder metadata,
        MethodBodyStreamEncoder bodyEncoder,
        string name,
        MemberReferenceHandle target)
    {
        var code = new BlobBuilder();
        var instructions = new InstructionEncoder(
            code,
            new ControlFlowBuilder());
        instructions.OpCode(ILOpCode.Ldnull);
        instructions.Call(target);
        instructions.OpCode(ILOpCode.Ret);
        int bodyOffset = bodyEncoder.AddMethodBody(
            instructions,
            maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(name),
            AddVoidSignature(metadata),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));
    }

    static int AddRetBody(
        MethodBodyStreamEncoder bodyEncoder)
    {
        var code = new BlobBuilder();
        var instructions = new InstructionEncoder(code);
        instructions.OpCode(ILOpCode.Ret);
        return bodyEncoder.AddMethodBody(
            instructions,
            maxStack: 0);
    }

    static BlobHandle AddFunctionPointerUseSignature(
        MetadataBuilder metadata,
        EntityHandle dependencyType)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                1,
                returnType => returnType.Void(),
                parameters =>
                {
                    MethodSignatureEncoder pointer =
                        parameters
                            .AddParameter()
                            .Type()
                            .FunctionPointer(
                                SignatureCallingConvention.Default,
                                FunctionPointerAttributes.None);
                    pointer.Parameters(
                        1,
                        returnType => returnType.Void(),
                        pointerParameters =>
                            pointerParameters
                                .AddParameter()
                                .Type()
                                .Type(
                                    dependencyType,
                                    isValueType: false));
                });
        return metadata.GetOrAddBlob(signature);
    }

    static BlobHandle AddVoidSignature(
        MetadataBuilder metadata)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                0,
                returnType => returnType.Void(),
                parameters => { });
        return metadata.GetOrAddBlob(signature);
    }

    static TypeReferenceHandle AddDependencyType(
        MetadataBuilder metadata,
        AssemblyReferenceIdentity dependency)
    {
        AssemblyReferenceHandle reference =
            AddAssemblyReference(metadata, dependency);
        return metadata.AddTypeReference(
            reference,
            metadata.GetOrAddString("Dependency"),
            metadata.GetOrAddString("Value"));
    }

    static AssemblyReferenceHandle AddAssemblyReference(
        MetadataBuilder metadata,
        AssemblyReferenceIdentity identity) =>
        metadata.AddAssemblyReference(
            metadata.GetOrAddString(identity.Name),
            identity.Version ?? new Version(0, 0, 0, 0),
            identity.Culture is null
                ? default
                : metadata.GetOrAddString(identity.Culture),
            identity.PublicKeyToken is null
                ? default
                : metadata.GetOrAddBlob(
                    Convert.FromHexString(
                        identity.PublicKeyToken)),
            flags: default,
            hashValue: default);

    static MetadataBuilder AssemblyMetadata(string name)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString($"{name}.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(name),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        return metadata;
    }

    static byte[] Serialize(
        MetadataBuilder metadata,
        BlobBuilder methodBodies)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    sealed class PairContext : IDisposable
    {
        PairContext(
            InspectionWorkspace workspace,
            AssemblyContextGroup group,
            ResolvedAssemblyReference first,
            ResolvedAssemblyReference second)
        {
            Workspace = workspace;
            Group = group;
            First = first;
            Second = second;
        }

        internal InspectionWorkspace Workspace { get; }
        internal AssemblyContextGroup Group { get; }
        internal ResolvedAssemblyReference First { get; }
        internal ResolvedAssemblyReference Second { get; }

        internal static PairContext Create(
            string firstPath,
            string secondPath)
        {
            ResolvedAssemblyReference first =
                ResolvedAssemblyReference.CreateFromPath(
                    firstPath,
                    AssemblyResolutionProvenance.Local(
                        "pairwise call-use test"));
            ResolvedAssemblyReference second =
                ResolvedAssemblyReference.CreateFromPath(
                    secondPath,
                    AssemblyResolutionProvenance.Local(
                        "pairwise call-use test"));
            return Create(first, second, firstPath);
        }

        internal static PairContext Create(
            ResolvedAssemblyReference first,
            ResolvedAssemblyReference second,
            string resolutionPath)
        {
            var policy =
                new SourceRelativeAssemblyGroupBindingPolicy(
                new[]
                {
                    (
                        first,
                        Policy: (IAssemblyBindingPolicy)
                            new AssemblyDependencyResolver(
                                new(resolutionPath)
                                {
                                    PreferImplementationAssemblies = true,
                                    AllowPlatformAssemblyVersionRollForward =
                                        true,
                                })),
                    (
                        second,
                        Policy: (IAssemblyBindingPolicy)
                            new AssemblyDependencyResolver(
                                new(resolutionPath)
                                {
                                    PreferImplementationAssemblies = true,
                                    AllowPlatformAssemblyVersionRollForward =
                                        true,
                                })),
                });
            var workspace = new InspectionWorkspace();
            AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup(
                    new[]
                    {
                        new AssemblyContextParticipant(first, policy),
                        new AssemblyContextParticipant(second, policy),
                    });
            return new(workspace, group, first, second);
        }

        public void Dispose() => Workspace.Dispose();
    }
}
