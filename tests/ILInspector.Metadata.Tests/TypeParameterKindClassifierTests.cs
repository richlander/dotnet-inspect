using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace ILInspector.Metadata.Tests;

public class TypeParameterKindClassifierTests
{
    [Fact]
    public void Classify_ReusesSameModuleDefinitionKindAcrossConstraints()
    {
        const int ChainLength = 128;
        const int ConstraintCount = 4_096;
        GenericParameterHandle parameter = default;
        using MetadataImage image = BuildMetadata(metadata =>
        {
            TypeDefinitionHandle baseType = default;
            for (int i = 0; i < ChainLength; i++)
            {
                baseType = metadata.AddTypeDefinition(
                    TypeAttributes.Public | TypeAttributes.Class,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString($"Base{i}"),
                    baseType,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1));
            }

            TypeDefinitionHandle consumer =
                AddTypeDefinition(metadata, "Consumer`1");
            parameter = metadata.AddGenericParameter(
                consumer,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
            for (int i = 0; i < ConstraintCount; i++)
            {
                metadata.AddGenericParameterConstraint(
                    parameter,
                    baseType);
            }
        });

        long before = GC.GetAllocatedBytesForCurrentThread();
        TypeParameterTypeKind kind =
            TypeParameterKindClassifier.Classify(
                image.Reader,
                parameter,
                hasValueTypeConstraint: false,
                hasReferenceTypeConstraint: false,
                new TypeParameterKindClassifier.ChainState());
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(TypeParameterTypeKind.ReferenceType, kind);
        Assert.InRange(allocated, 0, 64 * 1024);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Classify_IllegalConstraintWrapperStaysUndetermined(
        bool pinned)
    {
        GenericParameterHandle parameter = default;
        using MetadataImage image = BuildMetadata(metadata =>
        {
            TypeDefinitionHandle modifier = AddTypeDefinition(
                metadata,
                "Modifier");
            TypeDefinitionHandle baseType = AddTypeDefinition(
                metadata,
                "Base");
            TypeDefinitionHandle consumer = AddTypeDefinition(
                metadata,
                "Consumer`1");
            parameter = metadata.AddGenericParameter(
                consumer,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);

            var signature = new BlobBuilder();
            if (pinned)
            {
                signature.WriteByte(0x45); // PINNED
            }
            else
            {
                signature.WriteByte(0x1f); // CMOD_REQD
                signature.WriteCompressedInteger(
                    MetadataTokens.GetRowNumber(modifier) << 2);
            }

            signature.WriteByte(0x12); // CLASS
            signature.WriteCompressedInteger(
                MetadataTokens.GetRowNumber(baseType) << 2);
            TypeSpecificationHandle wrapped =
                metadata.AddTypeSpecification(
                    metadata.GetOrAddBlob(signature));
            metadata.AddGenericParameterConstraint(
                parameter,
                wrapped);
        });

        TypeParameterTypeKind kind =
            TypeParameterKindClassifier.Classify(
                image.Reader,
                parameter,
                hasValueTypeConstraint: false,
                hasReferenceTypeConstraint: false,
                new TypeParameterKindClassifier.ChainState());

        Assert.Equal(TypeParameterTypeKind.Undetermined, kind);
    }

    [Fact]
    public void ResolutionPlan_CachesSharedLargePublicKeyAcrossDuplicateRows()
    {
        const int ReferenceCount = 32;
        var references = new List<TypeReferenceHandle>();
        using MetadataImage image = BuildMetadata(metadata =>
        {
            var key = new BlobBuilder();
            key.WriteBytes(new byte[1024 * 1024]);
            BlobHandle keyHandle = metadata.GetOrAddBlob(key);
            StringHandle assemblyName =
                metadata.GetOrAddString("LargeKeyDependency");
            StringHandle typeNamespace = metadata.GetOrAddString("N");
            StringHandle typeName = metadata.GetOrAddString("Base");

            for (int i = 0; i < ReferenceCount; i++)
            {
                AssemblyReferenceHandle assembly =
                    metadata.AddAssemblyReference(
                        assemblyName,
                        new Version(1, 0, 0, 0),
                        culture: default,
                        publicKeyOrToken: keyHandle,
                        flags: AssemblyFlags.PublicKey,
                        hashValue: default);
                references.Add(metadata.AddTypeReference(
                    assembly,
                    typeNamespace,
                    typeName));
            }
        });
        ResolvedAssemblyReference source =
            ResolvedAssemblyReference.CreateFromPath(
                typeof(TypeParameterKindClassifierTests)
                    .Assembly.Location,
                AssemblyResolutionProvenance.Local(
                    nameof(
                        ResolutionPlan_CachesSharedLargePublicKeyAcrossDuplicateRows)));
        var plan =
            new TypeParameterKindClassifier.ResolutionPlan(
                image.Reader,
                source);

        long before = GC.GetAllocatedBytesForCurrentThread();
        foreach (TypeReferenceHandle reference in references)
        {
            Assert.Equal(
                TypeParameterKindClassifier.ConstraintClass.Unreadable,
                plan.Classify(image.Reader, reference));
        }
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Single(plan.Requests);
        Assert.InRange(allocated, 0, 8 * 1024 * 1024);
    }

    [Fact]
    public void ResolutionPlan_BoundsCollectedTypeRequests()
    {
        var references = new List<TypeReferenceHandle>();
        using MetadataImage image = BuildMetadata(metadata =>
        {
            AssemblyReferenceHandle assembly =
                AddAssemblyReference(metadata, "Dependency");
            references.Add(
                metadata.AddTypeReference(
                    assembly,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("First")));
            references.Add(
                metadata.AddTypeReference(
                    assembly,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("Second")));
            references.Add(
                metadata.AddTypeReference(
                    assembly,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("Third")));
        });
        ResolvedAssemblyReference source =
            ResolvedAssemblyReference.CreateFromPath(
                typeof(TypeParameterKindClassifierTests)
                    .Assembly.Location,
                AssemblyResolutionProvenance.Local(
                    nameof(
                        ResolutionPlan_BoundsCollectedTypeRequests)));
        var plan =
            new TypeParameterKindClassifier.ResolutionPlan(
                image.Reader,
                source,
                maxTypeResolutionRequests: 1);

        Assert.All(
            references,
            reference => Assert.Equal(
                TypeParameterKindClassifier.ConstraintClass.Unreadable,
                plan.Classify(image.Reader, reference)));

        Assert.Equal(
            "N.First",
            Assert.Single(plan.Requests).Type.ToMetadataFullName());
        Assert.Equal(2, plan.ProjectedReferenceCount);
        Assert.NotNull(plan.RequestBudgetFailure);
    }

    [Fact]
    public void ResolutionPlan_RollbackReleasesProvisionalProjections()
    {
        var references = new List<TypeReferenceHandle>();
        using MetadataImage image = BuildMetadata(metadata =>
        {
            AssemblyReferenceHandle assembly =
                AddAssemblyReference(metadata, "Dependency");
            for (int i = 0; i < 10; i++)
            {
                references.Add(
                    metadata.AddTypeReference(
                        assembly,
                        metadata.GetOrAddString("N"),
                        metadata.GetOrAddString($"Type{i}")));
            }
        });
        ResolvedAssemblyReference source =
            ResolvedAssemblyReference.CreateFromPath(
                typeof(TypeParameterKindClassifierTests)
                    .Assembly.Location,
                AssemblyResolutionProvenance.Local(
                    nameof(
                        ResolutionPlan_RollbackReleasesProvisionalProjections)));
        var plan =
            new TypeParameterKindClassifier.ResolutionPlan(
                image.Reader,
                source,
                maxTypeResolutionRequests: 1);

        Assert.Equal(
            TypeParameterKindClassifier.ConstraintClass.Unreadable,
            plan.Classify(image.Reader, references[0]));
        var checkpoint = plan.Checkpoint();

        foreach (TypeReferenceHandle reference in references.Skip(1))
        {
            Assert.Equal(
                TypeParameterKindClassifier.ConstraintClass.Unreadable,
                plan.Classify(image.Reader, reference));
            plan.Rollback(checkpoint);
        }

        Assert.Equal(1, plan.ProjectedReferenceCount);
        Assert.Equal(
            "N.Type0",
            Assert.Single(plan.Requests).Type.ToMetadataFullName());
        Assert.Null(plan.RequestBudgetFailure);
    }

    [Fact]
    public void ResolutionPlan_TracksOnlyConstructedConstraintRoot()
    {
        const int ArgumentCount = 32;
        GenericParameterHandle parameter = default;
        using MetadataImage image = BuildMetadata(metadata =>
        {
            AssemblyReferenceHandle dependency =
                AddAssemblyReference(
                    metadata,
                    "Dependency");
            TypeReferenceHandle root =
                metadata.AddTypeReference(
                    dependency,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString(
                        $"G`{ArgumentCount}"));
            TypeReferenceHandle[] arguments =
            [
                .. Enumerable.Range(0, ArgumentCount)
                    .Select(index =>
                        metadata.AddTypeReference(
                            dependency,
                            metadata.GetOrAddString("N"),
                            metadata.GetOrAddString(
                                $"A{index}"))),
            ];
            TypeDefinitionHandle consumer =
                AddTypeDefinition(
                    metadata,
                    "Consumer`1");
            parameter = metadata.AddGenericParameter(
                consumer,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);

            var signature = new BlobBuilder();
            signature.WriteByte(0x15); // GENERICINST
            signature.WriteByte(0x12); // CLASS
            signature.WriteCompressedInteger(
                (MetadataTokens.GetRowNumber(root) << 2) | 1);
            signature.WriteCompressedInteger(ArgumentCount);
            foreach (TypeReferenceHandle argument in arguments)
            {
                signature.WriteByte(0x12); // CLASS
                signature.WriteCompressedInteger(
                    (MetadataTokens.GetRowNumber(argument) << 2) | 1);
            }

            metadata.AddGenericParameterConstraint(
                parameter,
                metadata.AddTypeSpecification(
                    metadata.GetOrAddBlob(signature)));
        });
        ResolvedAssemblyReference source =
            ResolvedAssemblyReference.CreateFromPath(
                typeof(TypeParameterKindClassifierTests)
                    .Assembly.Location,
                AssemblyResolutionProvenance.Local(
                    nameof(
                        ResolutionPlan_TracksOnlyConstructedConstraintRoot)));
        var plan =
            new TypeParameterKindClassifier.ResolutionPlan(
                image.Reader,
                source);

        TypeParameterTypeKind kind =
            TypeParameterKindClassifier.Classify(
                image.Reader,
                parameter,
                hasValueTypeConstraint: false,
                hasReferenceTypeConstraint: false,
                new TypeParameterKindClassifier.ChainState(
                    plan));

        Assert.Equal(TypeParameterTypeKind.Undetermined, kind);
        TypeResolutionRequest request =
            Assert.Single(plan.Requests);
        Assert.Equal("N.G`32", request.Type.ToMetadataFullName());
    }

    [Fact]
    public void ResolutionPlan_MalformedConstructedConstraintCreatesNoRequest()
    {
        GenericParameterHandle parameter = default;
        using MetadataImage image = BuildMetadata(metadata =>
        {
            TypeReferenceHandle root =
                metadata.AddTypeReference(
                    AddAssemblyReference(
                        metadata,
                        "Dependency"),
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("G`1"));
            TypeDefinitionHandle consumer =
                AddTypeDefinition(
                    metadata,
                    "Consumer`1");
            parameter = metadata.AddGenericParameter(
                consumer,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);

            var signature = new BlobBuilder();
            signature.WriteByte(0x15); // GENERICINST
            signature.WriteByte(0x12); // CLASS
            signature.WriteCompressedInteger(
                (MetadataTokens.GetRowNumber(root) << 2) | 1);
            signature.WriteCompressedInteger(1);
            signature.WriteByte(0xff);
            metadata.AddGenericParameterConstraint(
                parameter,
                metadata.AddTypeSpecification(
                    metadata.GetOrAddBlob(signature)));
        });
        ResolvedAssemblyReference source =
            ResolvedAssemblyReference.CreateFromPath(
                typeof(TypeParameterKindClassifierTests)
                    .Assembly.Location,
                AssemblyResolutionProvenance.Local(
                    nameof(
                        ResolutionPlan_MalformedConstructedConstraintCreatesNoRequest)));
        var plan =
            new TypeParameterKindClassifier.ResolutionPlan(
                image.Reader,
                source);

        TypeParameterTypeKind kind =
            TypeParameterKindClassifier.Classify(
                image.Reader,
                parameter,
                hasValueTypeConstraint: false,
                hasReferenceTypeConstraint: false,
                new TypeParameterKindClassifier.ChainState(
                    plan));

        Assert.Equal(TypeParameterTypeKind.Undetermined, kind);
        Assert.Empty(plan.Requests);
    }

    [Fact]
    public void ResolutionPlan_OverBudgetTypeSpecClosureCreatesNoRequest()
    {
        const int ClosureLength =
            TypeSpecGuard.MaxCumulativeBytes;
        GenericParameterHandle parameter = default;
        using MetadataImage image = BuildMetadata(metadata =>
        {
            AssemblyReferenceHandle dependency =
                AddAssemblyReference(
                    metadata,
                    "Dependency");
            TypeReferenceHandle root =
                metadata.AddTypeReference(
                    dependency,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("G`1"));
            TypeReferenceHandle argument =
                metadata.AddTypeReference(
                    dependency,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("Argument"));
            for (int row = 1; row <= ClosureLength; row++)
            {
                var nested = new BlobBuilder();
                nested.WriteByte(0x20); // CMOD_OPT
                nested.WriteCompressedInteger(
                    row == ClosureLength
                        ? (MetadataTokens.GetRowNumber(argument) << 2) | 1
                        : ((row + 1) << 2) | 2);
                nested.WriteByte(0x12); // CLASS
                nested.WriteCompressedInteger(
                    (MetadataTokens.GetRowNumber(argument) << 2) | 1);
                metadata.AddTypeSpecification(
                    metadata.GetOrAddBlob(nested));
            }

            TypeDefinitionHandle consumer =
                AddTypeDefinition(
                    metadata,
                    "Consumer`1");
            parameter = metadata.AddGenericParameter(
                consumer,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
            var signature = new BlobBuilder();
            signature.WriteByte(0x15); // GENERICINST
            signature.WriteByte(0x12); // CLASS
            signature.WriteCompressedInteger(
                (MetadataTokens.GetRowNumber(root) << 2) | 1);
            signature.WriteCompressedInteger(1);
            signature.WriteByte(0x20); // CMOD_OPT
            signature.WriteCompressedInteger((1 << 2) | 2);
            signature.WriteByte(0x12); // CLASS
            signature.WriteCompressedInteger(
                (MetadataTokens.GetRowNumber(argument) << 2) | 1);
            metadata.AddGenericParameterConstraint(
                parameter,
                metadata.AddTypeSpecification(
                    metadata.GetOrAddBlob(signature)));
        });
        ResolvedAssemblyReference source =
            ResolvedAssemblyReference.CreateFromPath(
                typeof(TypeParameterKindClassifierTests)
                    .Assembly.Location,
                AssemblyResolutionProvenance.Local(
                    nameof(
                        ResolutionPlan_OverBudgetTypeSpecClosureCreatesNoRequest)));
        var plan =
            new TypeParameterKindClassifier.ResolutionPlan(
                image.Reader,
                source);

        TypeParameterTypeKind kind =
            TypeParameterKindClassifier.Classify(
                image.Reader,
                parameter,
                hasValueTypeConstraint: false,
                hasReferenceTypeConstraint: false,
                new TypeParameterKindClassifier.ChainState(
                    plan));

        Assert.Equal(TypeParameterTypeKind.Undetermined, kind);
        Assert.Empty(plan.Requests);
    }

    [Fact]
    public void Classify_MalformedSiblingNumbersStayUndetermined()
    {
        GenericParameterHandle malformed = default;
        using MetadataImage image = BuildMetadata(metadata =>
        {
            TypeDefinitionHandle consumer =
                AddTypeDefinition(
                    metadata,
                    "Consumer`2");
            metadata.AddGenericParameter(
                consumer,
                GenericParameterAttributes.ReferenceTypeConstraint,
                metadata.GetOrAddString("First"),
                index: 1);
            malformed = metadata.AddGenericParameter(
                consumer,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("Second"),
                index: 0);
            var selfReference = new BlobBuilder();
            selfReference.WriteByte(0x13); // VAR
            selfReference.WriteCompressedInteger(0);
            metadata.AddGenericParameterConstraint(
                malformed,
                metadata.AddTypeSpecification(
                    metadata.GetOrAddBlob(selfReference)));
        });

        TypeParameterTypeKind kind =
            TypeParameterKindClassifier.Classify(
                image.Reader,
                malformed,
                hasValueTypeConstraint: false,
                hasReferenceTypeConstraint: false,
                new TypeParameterKindClassifier.ChainState());

        Assert.Equal(TypeParameterTypeKind.Undetermined, kind);
    }

    [Fact]
    public void Classify_BaselessObjectWithReferencesIsNotCoreLibrary()
    {
        GenericParameterHandle parameter = default;
        using MetadataImage image = BuildMetadata(metadata =>
        {
            AddAssemblyReference(metadata, "ActualCore");
            TypeDefinitionHandle objectType =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("System"),
                    metadata.GetOrAddString("Object"),
                    baseType: default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1));
            TypeDefinitionHandle enumType =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("System"),
                    metadata.GetOrAddString("Enum"),
                    objectType,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1));
            TypeDefinitionHandle consumer =
                AddTypeDefinition(
                    metadata,
                    "Consumer`1");
            parameter = metadata.AddGenericParameter(
                consumer,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
            metadata.AddGenericParameterConstraint(
                parameter,
                enumType);
        });

        TypeParameterTypeKind kind =
            TypeParameterKindClassifier.Classify(
                image.Reader,
                parameter,
                hasValueTypeConstraint: false,
                hasReferenceTypeConstraint: false,
                new TypeParameterKindClassifier.ChainState());

        Assert.Equal(TypeParameterTypeKind.ReferenceType, kind);
    }

    [Fact]
    public void Extract_RejectedTypeDoesNotConsumeResolutionCandidate()
    {
        string rejectedPath = WriteAssembly(
            $"RejectedDependency{Guid.NewGuid():N}",
            "RejectedBase");
        string validPath = WriteAssembly(
            $"ValidDependency{Guid.NewGuid():N}",
            "ValidBase");
        string consumerPath = Path.Combine(
            Path.GetTempPath(),
            $"constraint-rollback-{Guid.NewGuid():N}.dll");

        try
        {
            ResolvedAssemblyReference rejected =
                ResolvedAssemblyReference.CreateFromPath(
                    rejectedPath,
                    AssemblyResolutionProvenance.Local(
                        nameof(
                            Extract_RejectedTypeDoesNotConsumeResolutionCandidate)));
            ResolvedAssemblyReference valid =
                ResolvedAssemblyReference.CreateFromPath(
                    validPath,
                    AssemblyResolutionProvenance.Local(
                        nameof(
                            Extract_RejectedTypeDoesNotConsumeResolutionCandidate)));
            File.WriteAllBytes(
                consumerPath,
                BuildConsumer(
                    rejected.Identity.Name,
                    valid.Identity.Name));
            ResolvedAssemblyReference source =
                ResolvedAssemblyReference.CreateFromPath(
                    consumerPath,
                    AssemblyResolutionProvenance.Local(
                        nameof(
                            Extract_RejectedTypeDoesNotConsumeResolutionCandidate)));

            using var pe =
                new PEReader(File.OpenRead(consumerPath));
            using var catalog = new TypeResolutionCatalog(
                new TypeResolutionContextOptions
                {
                    MaxCandidates = 2,
                });
            ApiSurface surface = ApiSurfaceExtractor.Extract(
                pe,
                source,
                catalog,
                new MappingPolicy(rejected, valid));

            Assert.DoesNotContain(
                surface.Types,
                type => type.Name == "Rejected`1");
            ApiType accepted = Assert.Single(
                surface.Types,
                type => type.Name == "Accepted`1");
            Assert.Equal(
                TypeParameterTypeKind.ReferenceType,
                Assert.Single(accepted.TypeParameters).TypeKind);
        }
        finally
        {
            File.Delete(consumerPath);
            File.Delete(rejectedPath);
            File.Delete(validPath);
        }
    }

    [Fact]
    public void Extract_ClassifiesClassWithExternalConstructedBase()
    {
        var (consumerPath, dependencyPath, basePath) =
            EmitExternalConstructedBaseSample();
        try
        {
            ResolvedAssemblyReference source =
                ResolvedAssemblyReference.CreateFromPath(
                    consumerPath,
                    AssemblyResolutionProvenance.Local(
                        nameof(
                            Extract_ClassifiesClassWithExternalConstructedBase)));
            ResolvedAssemblyReference dependency =
                ResolvedAssemblyReference.CreateFromPath(
                    dependencyPath,
                    AssemblyResolutionProvenance.Local(
                        nameof(
                            Extract_ClassifiesClassWithExternalConstructedBase)));
            ResolvedAssemblyReference baseAssembly =
                ResolvedAssemblyReference.CreateFromPath(
                    basePath,
                    AssemblyResolutionProvenance.Local(
                        nameof(
                            Extract_ClassifiesClassWithExternalConstructedBase)));
            using var pe =
                new PEReader(File.OpenRead(consumerPath));
            using var catalog = new TypeResolutionCatalog();

            ApiSurface surface = ApiSurfaceExtractor.Extract(
                pe,
                source,
                catalog,
                new MappingPolicy(dependency, baseAssembly));

            ApiType type = Assert.Single(surface.Types);
            Assert.Equal(
                TypeParameterTypeKind.ReferenceType,
                Assert.Single(type.TypeParameters).TypeKind);
        }
        finally
        {
            File.Delete(consumerPath);
            File.Delete(dependencyPath);
            File.Delete(basePath);
        }
    }

    [Fact]
    public void Extract_ClassifiesSameImageClassWithExternalConstructedBase()
    {
        var (consumerPath, basePath) =
            EmitSameImageExternalConstructedBaseSample();
        try
        {
            ResolvedAssemblyReference source =
                ResolvedAssemblyReference.CreateFromPath(
                    consumerPath,
                    AssemblyResolutionProvenance.Local(
                        nameof(
                            Extract_ClassifiesSameImageClassWithExternalConstructedBase)));
            ResolvedAssemblyReference baseAssembly =
                ResolvedAssemblyReference.CreateFromPath(
                    basePath,
                    AssemblyResolutionProvenance.Local(
                        nameof(
                            Extract_ClassifiesSameImageClassWithExternalConstructedBase)));
            using var pe =
                new PEReader(File.OpenRead(consumerPath));
            using var catalog = new TypeResolutionCatalog();

            ApiSurface surface = ApiSurfaceExtractor.Extract(
                pe,
                source,
                catalog,
                new MappingPolicy(baseAssembly));

            ApiType consumer = Assert.Single(
                surface.Types,
                type => type.Name == "Consumer`1");
            Assert.Equal(
                TypeParameterTypeKind.ReferenceType,
                Assert.Single(consumer.TypeParameters).TypeKind);
            Assert.Empty(surface.InspectionFailures);
        }
        finally
        {
            File.Delete(consumerPath);
            File.Delete(basePath);
        }
    }

    [Fact]
    public void Extract_RollbackAtFullRequestBudgetDoesNotReportExhaustion()
    {
        string dependencyPath = WriteAssembly(
            $"SharedDependency{Guid.NewGuid():N}",
            "Base");
        string consumerPath = Path.Combine(
            Path.GetTempPath(),
            $"constraint-full-budget-rollback-{Guid.NewGuid():N}.dll");

        try
        {
            ResolvedAssemblyReference dependency =
                ResolvedAssemblyReference.CreateFromPath(
                    dependencyPath,
                    AssemblyResolutionProvenance.Local(
                        nameof(
                            Extract_RollbackAtFullRequestBudgetDoesNotReportExhaustion)));
            File.WriteAllBytes(
                consumerPath,
                BuildFullBudgetRollbackConsumer(
                    dependency.Identity.Name));
            ResolvedAssemblyReference source =
                ResolvedAssemblyReference.CreateFromPath(
                    consumerPath,
                    AssemblyResolutionProvenance.Local(
                        nameof(
                            Extract_RollbackAtFullRequestBudgetDoesNotReportExhaustion)));
            using var pe =
                new PEReader(File.OpenRead(consumerPath));
            using var catalog = new TypeResolutionCatalog(
                new TypeResolutionContextOptions
                {
                    MaxTypeResolutionRequests = 1,
                });

            ApiSurface surface = ApiSurfaceExtractor.Extract(
                pe,
                source,
                catalog,
                new MappingPolicy(dependency));

            Assert.Equal(
                2,
                surface.Types.Count(type =>
                    type.Name.StartsWith(
                        "Accepted",
                        StringComparison.Ordinal)));
            Assert.All(
                surface.Types.Where(type =>
                    type.Name.StartsWith(
                        "Accepted",
                        StringComparison.Ordinal)),
                type => Assert.Equal(
                    TypeParameterTypeKind.ReferenceType,
                    Assert.Single(type.TypeParameters).TypeKind));
            Assert.DoesNotContain(
                surface.InspectionFailures,
                failure => failure.Operation
                    == "resolve generic parameter constraints");
        }
        finally
        {
            File.Delete(consumerPath);
            File.Delete(dependencyPath);
        }
    }

    [Fact]
    public void Extract_RejectsForgedClassMarkerForExternalValueTypeBase()
    {
        var (consumerPath, dependencyPath, basePath) =
            EmitForgedExternalValueTypeBaseSample();
        try
        {
            ResolvedAssemblyReference source =
                ResolvedAssemblyReference.CreateFromPath(
                    consumerPath,
                    AssemblyResolutionProvenance.Local(
                        nameof(
                            Extract_RejectsForgedClassMarkerForExternalValueTypeBase)));
            ResolvedAssemblyReference dependency =
                ResolvedAssemblyReference.CreateFromPath(
                    dependencyPath,
                    AssemblyResolutionProvenance.Local(
                        nameof(
                            Extract_RejectsForgedClassMarkerForExternalValueTypeBase)));
            ResolvedAssemblyReference baseAssembly =
                ResolvedAssemblyReference.CreateFromPath(
                    basePath,
                    AssemblyResolutionProvenance.Local(
                        nameof(
                            Extract_RejectsForgedClassMarkerForExternalValueTypeBase)));
            using var pe =
                new PEReader(File.OpenRead(consumerPath));
            using var catalog = new TypeResolutionCatalog();

            ApiSurface surface = ApiSurfaceExtractor.Extract(
                pe,
                source,
                catalog,
                new MappingPolicy(dependency, baseAssembly));

            Assert.Equal(
                TypeParameterTypeKind.Undetermined,
                Assert.Single(Assert.Single(surface.Types).TypeParameters)
                    .TypeKind);
        }
        finally
        {
            File.Delete(consumerPath);
            File.Delete(dependencyPath);
            File.Delete(basePath);
        }
    }

    [Fact]
    public void Extract_CyclicExternalConstructedBasesStayUndetermined()
    {
        var (consumerPath, firstPath, secondPath) =
            EmitCyclicExternalConstructedBasesSample();
        try
        {
            ResolvedAssemblyReference source =
                ResolvedAssemblyReference.CreateFromPath(
                    consumerPath,
                    AssemblyResolutionProvenance.Local(
                        nameof(
                            Extract_CyclicExternalConstructedBasesStayUndetermined)));
            ResolvedAssemblyReference first =
                ResolvedAssemblyReference.CreateFromPath(
                    firstPath,
                    AssemblyResolutionProvenance.Local(
                        nameof(
                            Extract_CyclicExternalConstructedBasesStayUndetermined)));
            ResolvedAssemblyReference second =
                ResolvedAssemblyReference.CreateFromPath(
                    secondPath,
                    AssemblyResolutionProvenance.Local(
                        nameof(
                            Extract_CyclicExternalConstructedBasesStayUndetermined)));
            using var pe =
                new PEReader(File.OpenRead(consumerPath));
            using var catalog = new TypeResolutionCatalog();

            ApiSurface surface = ApiSurfaceExtractor.Extract(
                pe,
                source,
                catalog,
                new MappingPolicy(first, second));

            Assert.Equal(
                TypeParameterTypeKind.Undetermined,
                Assert.Single(Assert.Single(surface.Types).TypeParameters)
                    .TypeKind);
            Assert.Contains(
                surface.InspectionFailures,
                failure =>
                    failure.Operation
                        == ApiSurface.ConstraintResolutionOperation
                    && failure.Detail.Contains(
                        "cycle",
                        StringComparison.OrdinalIgnoreCase));
            ApiDiff diff =
                ApiDiffAnalyzer.Compare(surface, surface);
            Assert.False(diff.IsEmpty);
            Assert.NotEmpty(diff.InspectionFailures);
        }
        finally
        {
            File.Delete(consumerPath);
            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }

    [Fact]
    public void CatalogExtraction_CachesDuplicateLargeKeyRows()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"duplicate-key-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(
            path,
            BuildDuplicateKeyConsumer(
                referenceCount: 32,
                publicKeyBytes: 1024 * 1024));
        try
        {
            ResolvedAssemblyReference source =
                ResolvedAssemblyReference.CreateFromPath(
                    path,
                    AssemblyResolutionProvenance.Local(
                        nameof(
                            CatalogExtraction_CachesDuplicateLargeKeyRows)));
            using var catalog = new TypeResolutionCatalog();

            long before =
                GC.GetAllocatedBytesForCurrentThread();
            ResolutionAwareApiSurfaceOutcome outcome =
                catalog.ExtractApiSurface(
                    source,
                    new MissingPolicy());
            long allocated =
                GC.GetAllocatedBytesForCurrentThread() - before;

            ApiSurface surface =
                Assert.IsType<
                    ResolutionAwareApiSurfaceOutcome.Read>(
                        outcome)
                    .Surface;
            Assert.Equal(
                32,
                Assert.Single(
                    surface.Types,
                    type => type.Name == "Consumer`32")
                    .TypeParameters.Count);
            Assert.InRange(
                allocated,
                0,
                8 * 1024 * 1024);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CatalogExtraction_DegradesRootAdjacencyAndKeepsHealthyTypes(
        bool malformedAssemblyReference)
    {
        byte[] image =
            BuildMalformedAdjacencyAssembly(
                malformedAssemblyReference);
        ResolvedAssemblyReference source =
            ResolvedAssemblyReference.Create(
                ReadIdentity(image),
                path: null,
                openRead: () =>
                    new MemoryStream(image, writable: false),
                AssemblyResolutionProvenance.Local(
                    nameof(
                        CatalogExtraction_DegradesRootAdjacencyAndKeepsHealthyTypes)));
        using var catalog = new TypeResolutionCatalog();

        ApiSurface surface =
            Assert.IsType<
                ResolutionAwareApiSurfaceOutcome.Read>(
                    catalog.ExtractApiSurface(
                        source,
                        new MissingPolicy()))
                .Surface;

        Assert.Contains(
            surface.Types,
            type => type.Name == "HealthyOne");
        Assert.Contains(
            surface.Types,
            type => type.Name == "HealthyTwo");
        Assert.Contains(
            surface.InspectionFailures,
            failure =>
                failure.Operation
                    != ApiSurface.ConstraintResolutionOperation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResolutionCandidate_RejectsMalformedAdjacency(
        bool malformedAssemblyReference)
    {
        byte[] image =
            BuildMalformedAdjacencyAssembly(
                malformedAssemblyReference);
        ResolvedAssemblyReference source =
            ResolvedAssemblyReference.Create(
                ReadIdentity(image),
                path: null,
                openRead: () =>
                    new MemoryStream(image, writable: false),
                AssemblyResolutionProvenance.Local(
                    nameof(
                        ResolutionCandidate_RejectsMalformedAdjacency)));
        MetadataTypeDefinitionName name =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "N",
                    ["HealthyOne"]))
                .Name;
        TypeResolutionRequest request =
            TypeResolutionRequest.FromAssembly(
                source,
                AssemblyResolutionScope.Any,
                name);
        using var catalog = new TypeResolutionCatalog();
        Assert.IsType<ResolutionAwareApiSurfaceOutcome.Read>(
            catalog.ExtractApiSurface(
                source,
                new MissingPolicy()));
        using TypeResolutionContext context =
            catalog.CreateContext(
                new MissingPolicy(),
                [source],
                [request]);

        Assert.IsType<TypeResolutionFailure.CandidateOpenFailed>(
            Assert.IsType<TypeResolutionOutcome.Rejected>(
                context.Resolve(request))
                .Failure);
    }

    [Fact]
    public void CatalogExtraction_WhenRootIsSelectedAsDependency_UsesStrictAdjacency()
    {
        byte[] image = BuildMalformedSelfConstraintAssembly();
        ResolvedAssemblyReference source =
            ResolvedAssemblyReference.Create(
                ReadIdentity(image),
                path: null,
                openRead: () =>
                    new MemoryStream(image, writable: false),
                AssemblyResolutionProvenance.Local(
                    nameof(
                        CatalogExtraction_WhenRootIsSelectedAsDependency_UsesStrictAdjacency)));
        using var catalog = new TypeResolutionCatalog();
        var policy = new MappingPolicy(source);

        for (int pass = 0; pass < 2; pass++)
        {
            ApiSurface surface =
                Assert.IsType<ResolutionAwareApiSurfaceOutcome.Read>(
                    catalog.ExtractApiSurface(
                        source,
                        policy))
                .Surface;

            ApiType consumer = Assert.Single(
                surface.Types,
                type => type.Name == "Consumer`1");
            Assert.Equal(
                TypeParameterTypeKind.Undetermined,
                Assert.Single(consumer.TypeParameters).TypeKind);
            Assert.Contains(
                surface.InspectionFailures,
                failure =>
                    failure.Operation
                        == ApiSurface.ConstraintResolutionOperation);
        }
    }

    [Fact]
    public void CatalogExtraction_RejectsImageChangedAfterInventory()
    {
        Guid mvid = Guid.NewGuid();
        byte[] inventoried =
            BuildSimpleAssembly("Changing", "First", mvid);
        byte[] changed =
            BuildSimpleAssembly("Changing", "Second", mvid);
        int opens = 0;
        var source = ResolvedAssemblyReference.Create(
            ReadIdentity(inventoried),
            path: null,
            openRead: () => new MemoryStream(
                Interlocked.Increment(ref opens) == 1
                    ? inventoried
                    : changed,
                writable: false),
            AssemblyResolutionProvenance.Local(
                nameof(
                    CatalogExtraction_RejectsImageChangedAfterInventory)));
        using var catalog = new TypeResolutionCatalog();

        var rejected =
            Assert.IsType<
                ResolutionAwareApiSurfaceOutcome.Rejected>(
                    catalog.ExtractApiSurface(
                        source,
                        new MissingPolicy()));

        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            rejected.Failure.Kind);
        Assert.Equal(2, opens);
    }

    static (string ConsumerPath, string DependencyPath, string BasePath)
        EmitExternalConstructedBaseSample()
    {
        var baseAssembly =
            new System.Reflection.Emit.PersistedAssemblyBuilder(
                new System.Reflection.AssemblyName(
                    $"ExternalGenericBase{Guid.NewGuid():N}"),
                typeof(object).Assembly);
        var baseModule =
            baseAssembly.DefineDynamicModule(
                "ExternalGenericBase");
        var genericBaseBuilder = baseModule.DefineType(
            "N.GenericBase`1",
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Class);
        genericBaseBuilder.DefineGenericParameters("T");
        Type genericBase =
            genericBaseBuilder.CreateType()!;
        string basePath = Path.Combine(
            Path.GetTempPath(),
            $"external-generic-base-{Guid.NewGuid():N}.dll");
        baseAssembly.Save(basePath);

        var dependencyAssembly =
            new System.Reflection.Emit.PersistedAssemblyBuilder(
                new System.Reflection.AssemblyName(
                    $"ExternalDerived{Guid.NewGuid():N}"),
                typeof(object).Assembly);
        var dependencyModule =
            dependencyAssembly.DefineDynamicModule(
                "ExternalDerived");
        Type derived = dependencyModule
            .DefineType(
                "N.Derived",
                TypeAttributes.Public
                    | TypeAttributes.Class,
                genericBase.MakeGenericType(typeof(int)))
            .CreateType()!;
        string dependencyPath = Path.Combine(
            Path.GetTempPath(),
            $"external-derived-{Guid.NewGuid():N}.dll");
        dependencyAssembly.Save(dependencyPath);

        var consumerAssembly =
            new System.Reflection.Emit.PersistedAssemblyBuilder(
                new System.Reflection.AssemblyName(
                    $"ExternalBaseConsumer{Guid.NewGuid():N}"),
                typeof(object).Assembly);
        var consumerModule =
            consumerAssembly.DefineDynamicModule(
                "ExternalBaseConsumer");
        var consumer = consumerModule.DefineType(
            "ExternalBaseConsumer`1",
            TypeAttributes.Public | TypeAttributes.Class);
        consumer.DefineGenericParameters("T")[0]
            .SetBaseTypeConstraint(derived);
        consumer.CreateType();
        string consumerPath = Path.Combine(
            Path.GetTempPath(),
            $"external-base-consumer-{Guid.NewGuid():N}.dll");
        consumerAssembly.Save(consumerPath);
        return (consumerPath, dependencyPath, basePath);
    }

    static (string ConsumerPath, string BasePath)
        EmitSameImageExternalConstructedBaseSample()
    {
        var baseAssembly =
            new System.Reflection.Emit.PersistedAssemblyBuilder(
                new System.Reflection.AssemblyName(
                    $"SameImageGenericBase{Guid.NewGuid():N}"),
                typeof(object).Assembly);
        var baseModule =
            baseAssembly.DefineDynamicModule(
                "SameImageGenericBase");
        var genericBaseBuilder = baseModule.DefineType(
            "N.GenericBase`1",
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Class);
        genericBaseBuilder.DefineGenericParameters("T");
        Type genericBase =
            genericBaseBuilder.CreateType()!;
        string basePath = Path.Combine(
            Path.GetTempPath(),
            $"same-image-generic-base-{Guid.NewGuid():N}.dll");
        baseAssembly.Save(basePath);

        var consumerAssembly =
            new System.Reflection.Emit.PersistedAssemblyBuilder(
                new System.Reflection.AssemblyName(
                    $"SameImageConsumer{Guid.NewGuid():N}"),
                typeof(object).Assembly);
        var consumerModule =
            consumerAssembly.DefineDynamicModule(
                "SameImageConsumer");
        Type derived = consumerModule
            .DefineType(
                "N.Derived",
                TypeAttributes.Public
                    | TypeAttributes.Class,
                genericBase.MakeGenericType(typeof(int)))
            .CreateType()!;
        var consumer = consumerModule.DefineType(
            "Consumer`1",
            TypeAttributes.Public | TypeAttributes.Class);
        consumer.DefineGenericParameters("T")[0]
            .SetBaseTypeConstraint(derived);
        consumer.CreateType();
        string consumerPath = Path.Combine(
            Path.GetTempPath(),
            $"same-image-consumer-{Guid.NewGuid():N}.dll");
        consumerAssembly.Save(consumerPath);
        return (consumerPath, basePath);
    }

    static byte[] BuildFullBudgetRollbackConsumer(
        string dependencyAssemblyName)
    {
        var metadata = NewMetadata(
            $"FullBudgetRollback{Guid.NewGuid():N}");
        AssemblyReferenceHandle dependencyAssembly =
            AddAssemblyReference(
                metadata,
                dependencyAssemblyName);
        TypeReferenceHandle sharedBase =
            metadata.AddTypeReference(
                dependencyAssembly,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Base"));

        AddTypeDefinition(metadata, "<Module>", TypeAttributes.NotPublic);
        AddConstrainedType("AcceptedBefore`1");
        TypeDefinitionHandle rejected =
            AddConstrainedType("Rejected`1");
        AddConstrainedType("AcceptedAfter`1");

        var malformedInterface = new BlobBuilder();
        malformedInterface.WriteByte(0xff);
        metadata.AddInterfaceImplementation(
            rejected,
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(malformedInterface)));
        return SerializePe(metadata);

        TypeDefinitionHandle AddConstrainedType(string name)
        {
            TypeDefinitionHandle type =
                AddTypeDefinition(metadata, name);
            GenericParameterHandle parameter =
                metadata.AddGenericParameter(
                    type,
                    GenericParameterAttributes.None,
                    metadata.GetOrAddString("T"),
                    index: 0);
            metadata.AddGenericParameterConstraint(
                parameter,
                sharedBase);
            return type;
        }
    }

    static (string ConsumerPath, string DependencyPath, string BasePath)
        EmitForgedExternalValueTypeBaseSample()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string baseName = $"ForgedValueBase{suffix}";
        string dependencyName = $"ForgedDerived{suffix}";
        string consumerName = $"ForgedConsumer{suffix}";

        MetadataBuilder baseMetadata = NewMetadata(baseName);
        System.Reflection.AssemblyName coreIdentity =
            typeof(object).Assembly.GetName();
        byte[] coreToken =
            coreIdentity.GetPublicKeyToken() ?? [];
        AssemblyReferenceHandle coreLibrary =
            baseMetadata.AddAssemblyReference(
                baseMetadata.GetOrAddString(coreIdentity.Name!),
                coreIdentity.Version ?? new Version(0, 0, 0, 0),
                string.IsNullOrEmpty(coreIdentity.CultureName)
                    ? default
                    : baseMetadata.GetOrAddString(
                        coreIdentity.CultureName),
                baseMetadata.GetOrAddBlob(coreToken),
                flags: default,
                hashValue: default);
        TypeReferenceHandle valueType =
            baseMetadata.AddTypeReference(
                coreLibrary,
                baseMetadata.GetOrAddString("System"),
                baseMetadata.GetOrAddString("ValueType"));
        AddTypeDefinition(
            baseMetadata,
            "<Module>",
            TypeAttributes.NotPublic);
        TypeDefinitionHandle genericValue =
            baseMetadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Sealed
                    | TypeAttributes.SequentialLayout,
                baseMetadata.GetOrAddString("N"),
                baseMetadata.GetOrAddString("GenericValue`1"),
                valueType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        baseMetadata.AddGenericParameter(
            genericValue,
            GenericParameterAttributes.None,
            baseMetadata.GetOrAddString("T"),
            index: 0);

        MetadataBuilder dependencyMetadata =
            NewMetadata(dependencyName);
        AssemblyReferenceHandle baseReference =
            AddAssemblyReference(dependencyMetadata, baseName);
        TypeReferenceHandle externalValue =
            dependencyMetadata.AddTypeReference(
                baseReference,
                dependencyMetadata.GetOrAddString("N"),
                dependencyMetadata.GetOrAddString("GenericValue`1"));
        var constructedBase = new BlobBuilder();
        constructedBase.WriteByte(0x15); // GENERICINST
        constructedBase.WriteByte(0x12); // forged CLASS marker
        constructedBase.WriteCompressedInteger(
            (MetadataTokens.GetRowNumber(externalValue) << 2) | 1);
        constructedBase.WriteCompressedInteger(1);
        constructedBase.WriteByte(0x08); // I4
        TypeSpecificationHandle baseSpecification =
            dependencyMetadata.AddTypeSpecification(
                dependencyMetadata.GetOrAddBlob(constructedBase));
        AddTypeDefinition(
            dependencyMetadata,
            "<Module>",
            TypeAttributes.NotPublic);
        dependencyMetadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            dependencyMetadata.GetOrAddString("N"),
            dependencyMetadata.GetOrAddString("Derived"),
            baseSpecification,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        MetadataBuilder consumerMetadata = NewMetadata(consumerName);
        AssemblyReferenceHandle dependencyReference =
            AddAssemblyReference(
                consumerMetadata,
                dependencyName);
        TypeReferenceHandle derived =
            consumerMetadata.AddTypeReference(
                dependencyReference,
                consumerMetadata.GetOrAddString("N"),
                consumerMetadata.GetOrAddString("Derived"));
        AddTypeDefinition(
            consumerMetadata,
            "<Module>",
            TypeAttributes.NotPublic);
        TypeDefinitionHandle consumer =
            AddTypeDefinition(
                consumerMetadata,
                "Consumer`1");
        GenericParameterHandle parameter =
            consumerMetadata.AddGenericParameter(
                consumer,
                GenericParameterAttributes.None,
                consumerMetadata.GetOrAddString("T"),
                index: 0);
        consumerMetadata.AddGenericParameterConstraint(
            parameter,
            derived);

        string basePath = Path.Combine(
            Path.GetTempPath(),
            $"{baseName}.dll");
        string dependencyPath = Path.Combine(
            Path.GetTempPath(),
            $"{dependencyName}.dll");
        string consumerPath = Path.Combine(
            Path.GetTempPath(),
            $"{consumerName}.dll");
        File.WriteAllBytes(basePath, SerializePe(baseMetadata));
        File.WriteAllBytes(
            dependencyPath,
            SerializePe(dependencyMetadata));
        File.WriteAllBytes(
            consumerPath,
            SerializePe(consumerMetadata));
        return (consumerPath, dependencyPath, basePath);
    }

    static (string ConsumerPath, string FirstPath, string SecondPath)
        EmitCyclicExternalConstructedBasesSample()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string firstName = $"CyclicFirst{suffix}";
        string secondName = $"CyclicSecond{suffix}";
        string consumerName = $"CyclicConsumer{suffix}";

        byte[] firstImage = BuildConstructedBaseAssembly(
            firstName,
            "First`1",
            secondName,
            "Second`1");
        byte[] secondImage = BuildConstructedBaseAssembly(
            secondName,
            "Second`1",
            firstName,
            "First`1");

        MetadataBuilder consumerMetadata = NewMetadata(consumerName);
        AssemblyReferenceHandle firstReference =
            AddAssemblyReference(consumerMetadata, firstName);
        TypeReferenceHandle firstType =
            consumerMetadata.AddTypeReference(
                firstReference,
                consumerMetadata.GetOrAddString("N"),
                consumerMetadata.GetOrAddString("First`1"));
        TypeSpecificationHandle constraint =
            AddConstructedClass(
                consumerMetadata,
                firstType);
        AddTypeDefinition(
            consumerMetadata,
            "<Module>",
            TypeAttributes.NotPublic);
        TypeDefinitionHandle consumer =
            AddTypeDefinition(
                consumerMetadata,
                "Consumer`1");
        GenericParameterHandle parameter =
            consumerMetadata.AddGenericParameter(
                consumer,
                GenericParameterAttributes.None,
                consumerMetadata.GetOrAddString("T"),
                index: 0);
        consumerMetadata.AddGenericParameterConstraint(
            parameter,
            constraint);

        string firstPath = Path.Combine(
            Path.GetTempPath(),
            $"{firstName}.dll");
        string secondPath = Path.Combine(
            Path.GetTempPath(),
            $"{secondName}.dll");
        string consumerPath = Path.Combine(
            Path.GetTempPath(),
            $"{consumerName}.dll");
        File.WriteAllBytes(firstPath, firstImage);
        File.WriteAllBytes(secondPath, secondImage);
        File.WriteAllBytes(
            consumerPath,
            SerializePe(consumerMetadata));
        return (consumerPath, firstPath, secondPath);
    }

    static byte[] BuildConstructedBaseAssembly(
        string assemblyName,
        string typeName,
        string baseAssemblyName,
        string baseTypeName)
    {
        MetadataBuilder metadata = NewMetadata(assemblyName);
        AssemblyReferenceHandle baseReference =
            AddAssemblyReference(metadata, baseAssemblyName);
        TypeReferenceHandle baseType =
            metadata.AddTypeReference(
                baseReference,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString(baseTypeName));
        TypeSpecificationHandle constructedBase =
            AddConstructedClass(metadata, baseType);
        AddTypeDefinition(
            metadata,
            "<Module>",
            TypeAttributes.NotPublic);
        TypeDefinitionHandle type =
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString(typeName),
                constructedBase,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddGenericParameter(
            type,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            index: 0);
        return SerializePe(metadata);
    }

    static TypeSpecificationHandle AddConstructedClass(
        MetadataBuilder metadata,
        TypeReferenceHandle type)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x15); // GENERICINST
        signature.WriteByte(0x12); // CLASS
        signature.WriteCompressedInteger(
            (MetadataTokens.GetRowNumber(type) << 2) | 1);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x08); // I4
        return metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(signature));
    }

    static byte[] BuildDuplicateKeyConsumer(
        int referenceCount,
        int publicKeyBytes)
    {
        MetadataBuilder metadata =
            NewMetadata($"DuplicateKey{Guid.NewGuid():N}");
        AddTypeDefinition(
            metadata,
            "<Module>",
            TypeAttributes.NotPublic);
        TypeDefinitionHandle consumer =
            AddTypeDefinition(
                metadata,
                $"Consumer`{referenceCount}");
        var key = new BlobBuilder();
        key.WriteBytes(new byte[publicKeyBytes]);
        BlobHandle keyHandle = metadata.GetOrAddBlob(key);
        StringHandle assemblyName =
            metadata.GetOrAddString("LargeKeyDependency");
        StringHandle typeNamespace =
            metadata.GetOrAddString("N");
        StringHandle typeName =
            metadata.GetOrAddString("Base");

        for (int i = 0; i < referenceCount; i++)
        {
            AssemblyReferenceHandle assembly =
                metadata.AddAssemblyReference(
                    assemblyName,
                    new Version(1, 0, 0, 0),
                    culture: default,
                    publicKeyOrToken: keyHandle,
                    flags: AssemblyFlags.PublicKey,
                    hashValue: default);
            TypeReferenceHandle type =
                metadata.AddTypeReference(
                    assembly,
                    typeNamespace,
                    typeName);
            GenericParameterHandle parameter =
                metadata.AddGenericParameter(
                    consumer,
                    GenericParameterAttributes.None,
                    metadata.GetOrAddString($"T{i}"),
                    index: i);
            metadata.AddGenericParameterConstraint(
                parameter,
                type);
        }

        return SerializePe(metadata);
    }

    static byte[] BuildSimpleAssembly(
        string assemblyName,
        string typeName,
        Guid mvid)
    {
        MetadataBuilder metadata =
            NewMetadata(assemblyName, mvid);
        AddTypeDefinition(
            metadata,
            "<Module>",
            TypeAttributes.NotPublic);
        AddTypeDefinition(metadata, typeName);
        return SerializePe(metadata);
    }

    static byte[] BuildMalformedAdjacencyAssembly(
        bool malformedAssemblyReference)
    {
        MetadataBuilder metadata =
            NewMetadata($"MalformedAdjacency{Guid.NewGuid():N}");
        BlobHandle token = default;
        if (malformedAssemblyReference)
        {
            var tokenBytes = new BlobBuilder();
            tokenBytes.WriteUInt32(0x01020304);
            token = metadata.GetOrAddBlob(tokenBytes);
        }

        AssemblyReferenceHandle target =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("Target"),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: token,
                flags: default,
                hashValue: default);
        if (!malformedAssemblyReference)
        {
            metadata.AddExportedType(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("NotAForwarder"),
                target,
                typeDefinitionId: 0);
        }

        AddTypeDefinition(
            metadata,
            "<Module>",
            TypeAttributes.NotPublic);
        AddTypeDefinition(metadata, "HealthyOne");
        AddTypeDefinition(metadata, "HealthyTwo");
        return SerializePe(metadata);
    }

    static byte[] BuildMalformedSelfConstraintAssembly()
    {
        string assemblyName =
            $"MalformedSelfConstraint{Guid.NewGuid():N}";
        MetadataBuilder metadata = NewMetadata(assemblyName);
        AssemblyReferenceHandle self =
            AddAssemblyReference(metadata, assemblyName);
        metadata.AddExportedType(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("NotAForwarder"),
            self,
            typeDefinitionId: 0);

        AddTypeDefinition(
            metadata,
            "<Module>",
            TypeAttributes.NotPublic);
        AddTypeDefinition(metadata, "ReferenceBase");
        TypeDefinitionHandle consumer =
            AddTypeDefinition(metadata, "Consumer`1");
        GenericParameterHandle parameter =
            metadata.AddGenericParameter(
                consumer,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
        TypeReferenceHandle constraint =
            metadata.AddTypeReference(
                self,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("ReferenceBase"));
        metadata.AddGenericParameterConstraint(
            parameter,
            constraint);
        return SerializePe(metadata);
    }

    static AssemblyReferenceIdentity ReadIdentity(
        byte[] image)
    {
        using var pe = new PEReader(
            ImmutableCollectionsMarshal
                .AsImmutableArray(image));
        return AssemblyReferenceIdentity
            .FromAssemblyDefinition(
                pe.GetMetadataReader());
    }

    static byte[] BuildConsumer(
        string rejectedAssemblyName,
        string validAssemblyName)
    {
        var metadata = NewMetadata(
            $"ConstraintConsumer{Guid.NewGuid():N}");
        AssemblyReferenceHandle rejectedAssembly =
            AddAssemblyReference(
                metadata,
                rejectedAssemblyName);
        AssemblyReferenceHandle validAssembly =
            AddAssemblyReference(
                metadata,
                validAssemblyName);
        TypeReferenceHandle rejectedBase =
            metadata.AddTypeReference(
                rejectedAssembly,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("RejectedBase"));
        TypeReferenceHandle validBase =
            metadata.AddTypeReference(
                validAssembly,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("ValidBase"));

        AddTypeDefinition(metadata, "<Module>", TypeAttributes.NotPublic);
        TypeDefinitionHandle rejectedType =
            AddTypeDefinition(metadata, "Rejected`1");
        TypeDefinitionHandle acceptedType =
            AddTypeDefinition(metadata, "Accepted`1");
        GenericParameterHandle rejectedParameter =
            metadata.AddGenericParameter(
                rejectedType,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
        metadata.AddGenericParameterConstraint(
            rejectedParameter,
            rejectedBase);
        GenericParameterHandle acceptedParameter =
            metadata.AddGenericParameter(
                acceptedType,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
        metadata.AddGenericParameterConstraint(
            acceptedParameter,
            validBase);

        var malformedInterface = new BlobBuilder();
        malformedInterface.WriteByte(0xff);
        TypeSpecificationHandle malformed =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(malformedInterface));
        metadata.AddInterfaceImplementation(
            rejectedType,
            malformed);
        return SerializePe(metadata);
    }

    static string WriteAssembly(
        string assemblyName,
        string typeName)
    {
        var metadata = NewMetadata(assemblyName);
        AddTypeDefinition(metadata, "<Module>", TypeAttributes.NotPublic);
        AddTypeDefinition(metadata, typeName);
        string path = Path.Combine(
            Path.GetTempPath(),
            $"{assemblyName}.dll");
        File.WriteAllBytes(path, SerializePe(metadata));
        return path;
    }

    static MetadataImage BuildMetadata(
        Action<MetadataBuilder> addRows)
    {
        MetadataBuilder metadata = NewMetadata("Synthetic");
        AddTypeDefinition(
            metadata,
            "<Module>",
            TypeAttributes.NotPublic);
        addRows(metadata);

        var root =
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true);
        var image = new BlobBuilder();
        root.Serialize(
            image,
            methodBodyStreamRva: 0,
            mappedFieldDataStreamRva: 0);
        return new MetadataImage(image.ToImmutableArray());
    }

    static MetadataBuilder NewMetadata(
        string assemblyName,
        Guid? mvid = null)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName:
                metadata.GetOrAddString($"{assemblyName}.dll"),
            mvid: metadata.GetOrAddGuid(
                mvid ?? Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        return metadata;
    }

    static TypeDefinitionHandle AddTypeDefinition(
        MetadataBuilder metadata,
        string name,
        TypeAttributes attributes = TypeAttributes.Public) =>
        metadata.AddTypeDefinition(
            attributes,
            name == "<Module>"
                ? default
                : metadata.GetOrAddString("N"),
            metadata.GetOrAddString(name),
            baseType: default,
            fieldList:
                MetadataTokens.FieldDefinitionHandle(1),
            methodList:
                MetadataTokens.MethodDefinitionHandle(1));

    static AssemblyReferenceHandle AddAssemblyReference(
        MetadataBuilder metadata,
        string name) =>
        metadata.AddAssemblyReference(
            metadata.GetOrAddString(name),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);

    static byte[] SerializePe(MetadataBuilder metadata)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    sealed class MetadataImage(
        ImmutableArray<byte> image) : IDisposable
    {
        readonly MetadataReaderProvider _provider =
            MetadataReaderProvider.FromMetadataImage(image);

        internal MetadataReader Reader =>
            _provider.GetMetadataReader();

        public void Dispose() => _provider.Dispose();
    }

    sealed class MappingPolicy(
        params ResolvedAssemblyReference[] assemblies)
        : IAssemblyBindingPolicy
    {
        readonly Dictionary<string, ResolvedAssemblyReference>
            _assemblies = assemblies.ToDictionary(
                assembly => assembly.Identity.Name,
                StringComparer.Ordinal);

        public AssemblyBindingPolicyVersion Version { get; } =
            new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore() =>
                request.Target
                is AssemblyBindingTarget.AssemblyReference reference
                && _assemblies.TryGetValue(
                reference.Identity.Name,
                out ResolvedAssemblyReference? assembly)
                ? AssemblyBindingSelection.Found(assembly)
                : AssemblyBindingSelection.NotFound();
        }
    }

    sealed class MissingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } =
            new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore() =>
                AssemblyBindingSelection.NotFound();
        }
    }
}
