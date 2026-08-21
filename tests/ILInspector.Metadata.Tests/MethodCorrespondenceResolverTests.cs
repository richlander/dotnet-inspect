using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata.Tests;

public sealed class MethodCorrespondenceResolverTests
{
    static string AssemblyPath => typeof(MethodCorrespondenceResolverTests).Assembly.Location;

    [Fact]
    public void Resolve_ReturnsExactAddressAcrossReadersOfSameArtifact()
    {
        using var oldImage = Open(AssemblyPath);
        using var newImage = Open(AssemblyPath);
        var source = FindMethod(oldImage.Reader, nameof(CorrespondenceFixture), nameof(CorrespondenceFixture.Transform));

        var result = MethodCorrespondenceResolver.Resolve(
            oldImage.Reader,
            MetadataMethodAddress.Create(oldImage.Reader, source),
            newImage.Reader);

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.True(result.IsExact);
        Assert.NotNull(result.Anchor);
        var target = Assert.IsType<MetadataMethodAddress>(result.Target);
        Assert.True(target.BelongsTo(newImage.Reader));
        Assert.Single(result.Candidates);
        Assert.Null(result.Failure);
    }

    [Fact]
    public void Resolve_ReturnsAbsentForNearMissInAnotherModule()
    {
        using var sourceImage = Open(AssemblyPath);
        using var targetImage = Open(typeof(object).Assembly.Location);
        var source = FindMethod(sourceImage.Reader, nameof(CorrespondenceFixture), nameof(CorrespondenceFixture.Transform));

        var result = MethodCorrespondenceResolver.Resolve(
            sourceImage.Reader,
            MetadataMethodAddress.Create(sourceImage.Reader, source),
            targetImage.Reader);

        Assert.Equal(MethodCorrespondenceStatus.Absent, result.Status);
        Assert.Null(result.Target);
        Assert.Empty(result.Candidates);
        Assert.NotNull(result.Anchor);
    }

    [Fact]
    public void Resolve_ReturnsFailedForSourceAddressFromWrongModule()
    {
        using var sourceImage = Open(AssemblyPath);
        using var otherImage = Open(typeof(object).Assembly.Location);
        var otherMethod = otherImage.Reader.MethodDefinitions.First();

        var result = MethodCorrespondenceResolver.Resolve(
            sourceImage.Reader,
            MetadataMethodAddress.Create(otherImage.Reader, otherMethod),
            sourceImage.Reader);

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Null(result.Target);
        Assert.Contains("different metadata module", result.Failure);
    }

    [Fact]
    public void Resolve_TrailingMethodSignatureBytesFailClosed()
    {
        byte[] sourceImage =
            BuildMethodSignatureImage([0x00, 0x00, 0x01, 0x08]);
        byte[] targetImage =
            BuildMethodSignatureImage([0x00, 0x00, 0x01]);
        using var sourcePe = new PEReader(new MemoryStream(sourceImage));
        using var targetPe = new PEReader(new MemoryStream(targetImage));
        MetadataReader sourceReader = sourcePe.GetMetadataReader();
        MethodDefinitionHandle sourceMethod =
            sourceReader.MethodDefinitions.Single();

        MethodCorrespondenceResult result =
            MethodCorrespondenceResolver.Resolve(
                sourceReader,
                MetadataMethodAddress.Create(
                    sourceReader,
                    sourceMethod),
                targetPe.GetMetadataReader());

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("BadImageFormatException", result.Failure);
    }

    [Fact]
    public void Resolve_TerminalSentinelFailsClosedInsteadOfColliding()
    {
        byte[] sourceImage =
            BuildMethodSignatureImage([0x05, 0x01, 0x01, 0x08, 0x41]);
        byte[] targetImage =
            BuildMethodSignatureImage([0x05, 0x01, 0x01, 0x08]);
        using var sourcePe = new PEReader(new MemoryStream(sourceImage));
        using var targetPe = new PEReader(new MemoryStream(targetImage));
        MetadataReader sourceReader = sourcePe.GetMetadataReader();
        MethodDefinitionHandle sourceMethod =
            sourceReader.MethodDefinitions.Single();

        MethodCorrespondenceResult result =
            MethodCorrespondenceResolver.Resolve(
                sourceReader,
                MetadataMethodAddress.Create(sourceReader, sourceMethod),
                targetPe.GetMetadataReader());

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("BadImageFormatException", result.Failure);
    }

    [Fact]
    public void Resolve_MethodGenericNamesRespectAnchorBudget()
    {
        byte[] image = BuildManyMethodGenericParametersImage(
            genericParameterCount: 2_000,
            genericParameterNameLength: 2_000);
        using var sourcePe = new PEReader(new MemoryStream(image));
        using var targetPe = new PEReader(new MemoryStream(image));
        MetadataReader sourceReader = sourcePe.GetMetadataReader();
        MethodDefinitionHandle sourceMethod =
            sourceReader.MethodDefinitions.Single();

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        MethodCorrespondenceResult result =
            MethodCorrespondenceResolver.Resolve(
                sourceReader,
                MetadataMethodAddress.Create(sourceReader, sourceMethod),
                targetPe.GetMetadataReader());
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("generic-parameter names", result.Failure);
        Assert.True(
            allocated < 16 * 1024 * 1024,
            $"Generic-name rejection allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void Resolve_DuplicateRowsStayWithinAllocationBudget()
    {
        byte[] sourceImage = BuildDuplicateMethodsImage(1);
        byte[] targetImage = BuildDuplicateMethodsImage(
            MetadataSafetyPolicy.MaxCorrespondenceMethodRows + 1);
        using var sourcePe = new PEReader(new MemoryStream(sourceImage));
        using var targetPe = new PEReader(new MemoryStream(targetImage));
        MetadataReader sourceReader = sourcePe.GetMetadataReader();
        MethodDefinitionHandle sourceMethod =
            sourceReader.MethodDefinitions.Single();

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        MethodCorrespondenceResult result =
            MethodCorrespondenceResolver.Resolve(
                sourceReader,
                MetadataMethodAddress.Create(sourceReader, sourceMethod),
                targetPe.GetMetadataReader());
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("method table", result.Failure);
        Assert.True(
            allocated < 16 * 1024 * 1024,
            $"Duplicate-row rejection allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void Resolve_DuplicateCandidatesFailClosedAtCap()
    {
        byte[] sourceImage = BuildDuplicateMethodsImage(1);
        byte[] targetImage = BuildDuplicateMethodsImage(
            MetadataSafetyPolicy.MaxCorrespondenceCandidates + 1);
        using var sourcePe = new PEReader(new MemoryStream(sourceImage));
        using var targetPe = new PEReader(new MemoryStream(targetImage));
        MetadataReader sourceReader = sourcePe.GetMetadataReader();
        MethodDefinitionHandle sourceMethod =
            sourceReader.MethodDefinitions.Single();

        MethodCorrespondenceResult result =
            MethodCorrespondenceResolver.Resolve(
                sourceReader,
                MetadataMethodAddress.Create(sourceReader, sourceMethod),
                targetPe.GetMetadataReader());

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Empty(result.Candidates);
        Assert.Contains("matching target methods", result.Failure);
    }

    [Fact]
    public void Resolve_OversizedShallowSignatureRejectsBeforeLargeAllocation()
    {
        byte[] image = BuildWidePrimitiveMethodImage(250_000);
        using var sourcePe = new PEReader(new MemoryStream(image));
        using var targetPe = new PEReader(new MemoryStream(image));
        MetadataReader sourceReader = sourcePe.GetMetadataReader();
        MethodDefinitionHandle sourceMethod =
            sourceReader.MethodDefinitions.Single();

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        MethodCorrespondenceResult result =
            MethodCorrespondenceResolver.Resolve(
                sourceReader,
                MetadataMethodAddress.Create(sourceReader, sourceMethod),
                targetPe.GetMetadataReader());
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("BadImageFormatException", result.Failure);
        Assert.True(
            allocated < 16 * 1024 * 1024,
            $"Oversized shallow signature allocated {allocated:N0} bytes before rejection.");
    }

    [Fact]
    public void Resolve_OversizedAssemblyKeyRejectsBeforeCopyAndHexExpansion()
    {
        byte[] image = BuildAssemblyKeyMethodImage(
            MetadataSafetyPolicy.MaxStructuralSignatureChars / 2 + 1);
        using var sourcePe = new PEReader(new MemoryStream(image));
        using var targetPe = new PEReader(new MemoryStream(image));
        MetadataReader sourceReader = sourcePe.GetMetadataReader();
        MethodDefinitionHandle sourceMethod =
            sourceReader.MethodDefinitions.Single();

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        MethodCorrespondenceResult result =
            MethodCorrespondenceResolver.Resolve(
                sourceReader,
                MetadataMethodAddress.Create(sourceReader, sourceMethod),
                targetPe.GetMetadataReader());
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("assembly-reference key", result.Failure);
        Assert.True(
            allocated < 2 * 1024 * 1024,
            $"Oversized assembly-key rejection allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void Resolve_OversizedMethodNameRejectsBeforeLargeAllocation()
    {
        byte[] image = BuildOversizedMethodNameImage(
            8 * 1024 * 1024);
        using var sourcePe = new PEReader(new MemoryStream(image));
        using var targetPe = new PEReader(new MemoryStream(image));
        MetadataReader sourceReader = sourcePe.GetMetadataReader();
        MethodDefinitionHandle sourceMethod =
            sourceReader.MethodDefinitions.Single();

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        MethodCorrespondenceResult result =
            MethodCorrespondenceResolver.Resolve(
                sourceReader,
                MetadataMethodAddress.Create(sourceReader, sourceMethod),
                targetPe.GetMetadataReader());
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("metadata string", result.Failure);
        Assert.True(
            allocated < 2 * 1024 * 1024,
            $"Oversized method-name rejection allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void Resolve_OversizedAnchorTypeNameRejectsBeforeLargeAllocation()
    {
        byte[] image = BuildOversizedTypeReferenceNameImage(
            8 * 1024 * 1024);
        using var sourcePe = new PEReader(new MemoryStream(image));
        using var targetPe = new PEReader(new MemoryStream(image));
        MetadataReader sourceReader = sourcePe.GetMetadataReader();
        MethodDefinitionHandle sourceMethod =
            sourceReader.MethodDefinitions.Single();

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        MethodCorrespondenceResult result =
            MethodCorrespondenceResolver.Resolve(
                sourceReader,
                MetadataMethodAddress.Create(sourceReader, sourceMethod),
                targetPe.GetMetadataReader());
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("metadata string", result.Failure);
        Assert.True(
            allocated < 2 * 1024 * 1024,
            $"Oversized anchor type-name rejection allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void CreateMethodAnchor_RepeatedTypeNamesFailBeforeLargeAllocation()
    {
        // Many parameters each naming the same long TypeRef: per-name caps alone
        // still allow O(params × name) amplification because EnsureAnchorSignatureBudget
        // runs only after the full type tree is built. The cumulative work budget must
        // reject during construction. Gated by MaxAnchorSignatureWorkChars.
        const int parameterCount = 1_000;
        const int typeNameLength = 20_000;
        byte[] image = BuildRepeatedTypeReferenceNameImage(
            parameterCount,
            typeNameLength);
        using var pe = new PEReader(new MemoryStream(image));
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinitionHandle methodHandle =
            reader.MethodDefinitions.Single();
        MethodDefinition method =
            reader.GetMethodDefinition(methodHandle);

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        BadImageFormatException ex =
            Assert.Throws<BadImageFormatException>(
                () => ApiMemberIdentity.CreateMethodAnchorInfo(
                    reader,
                    method.GetDeclaringType(),
                    method));
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Contains("cumulative work budget", ex.Message);
        Assert.True(
            allocated < 16 * 1024 * 1024,
            $"Repeated type-name anchor rejection allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void CreateMethodAnchor_NestedArrayModoptsFailBeforeLargeAllocation()
    {
        // Many parameters each carry modopt(TypeSpec) where the TypeSpec is a
        // deep SZARRAY nest. GetModifiedType discards the modifier from the
        // rendered anchor, so EnsureAnchorSignatureBudget never sees the tree;
        // composite-node charging must reject during construction.
        const int parameterCount = 1_000;
        const int arrayDepth = 500;
        byte[] image = BuildNestedArrayModoptImage(
            parameterCount,
            arrayDepth);
        using var pe = new PEReader(new MemoryStream(image));
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinitionHandle methodHandle =
            reader.MethodDefinitions.Single();
        MethodDefinition method =
            reader.GetMethodDefinition(methodHandle);

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        BadImageFormatException ex =
            Assert.Throws<BadImageFormatException>(
                () => ApiMemberIdentity.CreateMethodAnchorInfo(
                    reader,
                    method.GetDeclaringType(),
                    method));
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Contains("cumulative work budget", ex.Message);
        Assert.True(
            allocated < 16 * 1024 * 1024,
            $"Nested modopt array rejection allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void CreateMethodAnchor_WideGenericModoptsFailBeforeLargeAllocation()
    {
        // Wide GENERICINST<!0 × arity> as a discarded modopt TypeSpec: leaf
        // names are only two characters, so name-length charging alone lets
        // O(params × arity) Encoded nodes allocate hundreds of MiB before the
        // work budget trips. The short-leaf floor must reject earlier.
        const int parameterCount = 2_000;
        const int genericArity = 2_030;
        byte[] image = BuildWideGenericModoptImage(
            parameterCount,
            genericArity);
        using var pe = new PEReader(new MemoryStream(image));
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinitionHandle methodHandle =
            reader.MethodDefinitions.Single();
        MethodDefinition method =
            reader.GetMethodDefinition(methodHandle);

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        BadImageFormatException ex =
            Assert.Throws<BadImageFormatException>(
                () => ApiMemberIdentity.CreateMethodAnchorInfo(
                    reader,
                    method.GetDeclaringType(),
                    method));
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Contains("cumulative work budget", ex.Message);
        Assert.True(
            allocated < 16 * 1024 * 1024,
            $"Wide generic modopt rejection allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void CreateMethodAnchor_WideTypeRefGenericModoptsFailBeforeLargeAllocation()
    {
        // Same width shape as WideGenericModopts, but every generic argument is
        // a short TypeRef. After the first decode the TypeRef cache hits and
        // must still pay the leaf floor; charging cached.Length alone let this
        // path allocate ~100 MiB before reject (and succeed under the budget at
        // slightly smaller widths).
        const int parameterCount = 2_000;
        const int genericArity = 2_030;
        byte[] image = BuildWideTypeRefGenericModoptImage(
            parameterCount,
            genericArity);
        using var pe = new PEReader(new MemoryStream(image));
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinitionHandle methodHandle =
            reader.MethodDefinitions.Single();
        MethodDefinition method =
            reader.GetMethodDefinition(methodHandle);

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        BadImageFormatException ex =
            Assert.Throws<BadImageFormatException>(
                () => ApiMemberIdentity.CreateMethodAnchorInfo(
                    reader,
                    method.GetDeclaringType(),
                    method));
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Contains("cumulative work budget", ex.Message);
        Assert.True(
            allocated < 16 * 1024 * 1024,
            $"Wide TypeRef generic modopt rejection allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void CreateMethodAnchor_UniqueLongTypeRefModoptsFailBeforeLargeAllocation()
    {
        // Unique long TypeRef modifiers: cache misses used to Format+GetString
        // before the work budget could reject, so a handful of ~900 KiB names
        // allocated >16 MiB on both reject and under-budget success. Lazy
        // TypeRef leaves charge UTF-8 length and materialize only on render.
        const int uniqueCount = 5;
        const int typeNameLength = 900_000;
        byte[] image = BuildUniqueLongTypeRefModoptImage(
            uniqueCount,
            typeNameLength);
        using var pe = new PEReader(new MemoryStream(image));
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinitionHandle methodHandle =
            reader.MethodDefinitions.Single();
        MethodDefinition method =
            reader.GetMethodDefinition(methodHandle);

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        BadImageFormatException ex =
            Assert.Throws<BadImageFormatException>(
                () => ApiMemberIdentity.CreateMethodAnchorInfo(
                    reader,
                    method.GetDeclaringType(),
                    method));
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Contains("cumulative work budget", ex.Message);
        Assert.True(
            allocated < 16 * 1024 * 1024,
            $"Unique long TypeRef modopt rejection allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void Resolve_TrailingConstraintTypeSpecBytesFailClosed()
    {
        byte[] sourceImage =
            BuildConstraintTypeSpecImage([0x08, 0x0e]);
        byte[] targetImage =
            BuildConstraintTypeSpecImage([0x08]);
        using var sourcePe = new PEReader(new MemoryStream(sourceImage));
        using var targetPe = new PEReader(new MemoryStream(targetImage));
        MetadataReader sourceReader = sourcePe.GetMetadataReader();
        MethodDefinitionHandle sourceMethod =
            sourceReader.MethodDefinitions.Single();

        MethodCorrespondenceResult result =
            MethodCorrespondenceResolver.Resolve(
                sourceReader,
                MetadataMethodAddress.Create(
                    sourceReader,
                    sourceMethod),
                targetPe.GetMetadataReader());

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("BadImageFormatException", result.Failure);
    }

    [Fact]
    public void Resolve_ReturnsFailedWithinBudgetForDeepOversizedStructuralSignature()
    {
        byte[] image = BuildConstrainedMethodImage(
            constraintCopies: 500,
            typeSpecificationDepth: 400);
        using var sourcePe = new PEReader(new MemoryStream(image));
        using var targetPe = new PEReader(new MemoryStream(image));
        MetadataReader sourceReader = sourcePe.GetMetadataReader();
        MetadataReader targetReader = targetPe.GetMetadataReader();
        MethodDefinitionHandle sourceMethod =
            sourceReader.MethodDefinitions.Single();

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        MethodCorrespondenceResult result =
            MethodCorrespondenceResolver.Resolve(
                sourceReader,
                MetadataMethodAddress.Create(sourceReader, sourceMethod),
                targetReader);
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Null(result.Target);
        Assert.Empty(result.Candidates);
        Assert.Contains("BadImageFormatException", result.Failure);
        Assert.True(
            allocated < 64 * 1024 * 1024,
            $"Deep TypeSpec rejection allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void Resolve_DeepAcceptedSignatureDoesNotExpandAnchorQuadratically()
    {
        byte[] warmImage = BuildDeepMethodSignatureImage(
            typeDepth: 1,
            typeNameLength: 32);
        using (var warmSourcePe = new PEReader(new MemoryStream(warmImage)))
        using (var warmTargetPe = new PEReader(new MemoryStream(warmImage)))
        {
            MetadataReader warmSourceReader =
                warmSourcePe.GetMetadataReader();
            MethodDefinitionHandle warmMethod =
                warmSourceReader.MethodDefinitions.Single();
            _ = MethodCorrespondenceResolver.Resolve(
                warmSourceReader,
                MetadataMethodAddress.Create(warmSourceReader, warmMethod),
                warmTargetPe.GetMetadataReader());
        }

        byte[] image = BuildDeepMethodSignatureImage(
            typeDepth: 511,
            typeNameLength: 262_070);
        using var sourcePe = new PEReader(new MemoryStream(image));
        using var targetPe = new PEReader(new MemoryStream(image));
        MetadataReader sourceReader = sourcePe.GetMetadataReader();
        MethodDefinitionHandle sourceMethod =
            sourceReader.MethodDefinitions.Single();

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        MethodCorrespondenceResult result =
            MethodCorrespondenceResolver.Resolve(
                sourceReader,
                MetadataMethodAddress.Create(sourceReader, sourceMethod),
                targetPe.GetMetadataReader());
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.NotNull(result.Anchor);
        Assert.True(
            allocated < 64 * 1024 * 1024,
            $"Deep anchor construction allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void Resolve_DeepDeclaringTypeRejectsAggregateNameWithoutQuadraticExpansion()
    {
        byte[] warmImage = BuildNestedDeclaringTypeImage(
            nestingDepth: 2,
            segmentNameLength: 16);
        using (var warmSourcePe = new PEReader(new MemoryStream(warmImage)))
        using (var warmTargetPe = new PEReader(new MemoryStream(warmImage)))
        {
            MetadataReader warmSourceReader =
                warmSourcePe.GetMetadataReader();
            MethodDefinitionHandle warmMethod =
                warmSourceReader.MethodDefinitions.Single();
            _ = MethodCorrespondenceResolver.Resolve(
                warmSourceReader,
                MetadataMethodAddress.Create(warmSourceReader, warmMethod),
                warmTargetPe.GetMetadataReader());
        }

        byte[] image = BuildNestedDeclaringTypeImage(
            nestingDepth: 256,
            segmentNameLength: 1_020);
        using var sourcePe = new PEReader(new MemoryStream(image));
        using var targetPe = new PEReader(new MemoryStream(image));
        MetadataReader sourceReader = sourcePe.GetMetadataReader();
        MethodDefinitionHandle sourceMethod =
            sourceReader.MethodDefinitions.Single();

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        MethodCorrespondenceResult result =
            MethodCorrespondenceResolver.Resolve(
                sourceReader,
                MetadataMethodAddress.Create(sourceReader, sourceMethod),
                targetPe.GetMetadataReader());
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("metadata type name exceeds", result.Failure);
        Assert.True(
            allocated < 32 * 1024 * 1024,
            $"Deep declaring-type anchor allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void Resolve_WideGenericParameterAnchorFailsWithinBudget()
    {
        byte[] image = BuildWideGenericParameterImage(
            parameterCount: 100_000,
            genericParameterNameLength: 1_023);
        using var sourcePe = new PEReader(new MemoryStream(image));
        using var targetPe = new PEReader(new MemoryStream(image));
        MetadataReader sourceReader = sourcePe.GetMetadataReader();
        MethodDefinitionHandle sourceMethod =
            sourceReader.MethodDefinitions.Single();
        MethodDefinition method =
            sourceReader.GetMethodDefinition(sourceMethod);

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<BadImageFormatException>(
            () => ApiMemberIdentity.CreateMethodAnchor(
                sourceReader,
                method.GetDeclaringType(),
                method));
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.True(
            allocated < 64 * 1024 * 1024,
            $"Wide anchor rejection allocated {allocated:N0} bytes.");

        MethodCorrespondenceResult result =
            MethodCorrespondenceResolver.Resolve(
                sourceReader,
                MetadataMethodAddress.Create(
                    sourceReader,
                    sourceMethod),
                targetPe.GetMetadataReader());
        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("BadImageFormatException", result.Failure);
    }

    [Fact]
    public void Resolve_WideArrayRanksFailWithinBudget()
    {
        byte[] image = BuildWideArrayRankImage(
            parameterCount: 200,
            rank: 1_000_000);
        using var sourcePe = new PEReader(new MemoryStream(image));
        using var targetPe = new PEReader(new MemoryStream(image));
        MetadataReader sourceReader = sourcePe.GetMetadataReader();
        MethodDefinitionHandle sourceMethod =
            sourceReader.MethodDefinitions.Single();

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        MethodCorrespondenceResult result =
            MethodCorrespondenceResolver.Resolve(
                sourceReader,
                MetadataMethodAddress.Create(
                    sourceReader,
                    sourceMethod),
                targetPe.GetMetadataReader());
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("BadImageFormatException", result.Failure);
        Assert.True(
            allocated < 64 * 1024 * 1024,
            $"Wide array-rank rejection allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void BuildMethodKey_ChargesMethodNameWork()
    {
        byte[] image = BuildManyNamedMethodsImage(
            methodCount: 210,
            methodNameLength: 20_000);
        using var pe = new PEReader(new MemoryStream(image));
        MetadataReader reader = pe.GetMetadataReader();
        var builder = new StructuralSignatureBuilder(reader);

        int built = 0;
        foreach (MethodDefinitionHandle handle
            in reader.MethodDefinitions)
        {
            try
            {
                _ = builder.BuildMethodKey(
                    reader.GetMethodDefinition(handle));
                built++;
            }
            catch (BadImageFormatException)
            {
                break;
            }
        }

        Assert.InRange(built, 1, 209);
    }

    [Fact]
    public void BuildMethodKey_CumulativeWorkBudgetFailsBeforeRepeatingDecode()
    {
        byte[] image = BuildConstrainedMethodImage(
            constraintCopies: 380,
            methodCount: 10,
            constraintTypeNameLength: 2048);
        using var pe = new PEReader(new MemoryStream(image));
        MetadataReader reader = pe.GetMetadataReader();
        var methods = reader.MethodDefinitions.ToArray();
        var builder = new StructuralSignatureBuilder(reader);

        int firstFailure = -1;
        for (int i = 0; i < methods.Length; i++)
        {
            try
            {
                _ = builder.BuildMethodKey(
                    reader.GetMethodDefinition(methods[i]));
            }
            catch (BadImageFormatException)
            {
                firstFailure = i;
                break;
            }
        }

        Assert.InRange(firstFailure, 1, methods.Length - 2);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<BadImageFormatException>(
            () => builder.BuildMethodKey(
                reader.GetMethodDefinition(methods[firstFailure + 1])));
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.True(
            allocated < 1024 * 1024,
            $"A repeated exhausted-budget call allocated {allocated:N0} bytes.");
    }

    static byte[] BuildConstrainedMethodImage(
        int constraintCopies,
        int methodCount = 1,
        int typeSpecificationDepth = 0,
        int constraintTypeNameLength = 0)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Probe.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Probe"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        var runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        var disposable = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString(
                constraintTypeNameLength == 0
                    ? "IDisposable"
                    : new string('X', constraintTypeNameLength)));
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        for (int i = 0; i < methodCount; i++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString($"C{i}"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1 + i));
        }

        BlobHandle signature = metadata.GetOrAddBlob(
            new byte[] { 0x10, 0x01, 0x00, 0x01 });
        var methods = new List<MethodDefinitionHandle>(methodCount);
        for (int i = 0; i < methodCount; i++)
        {
            methods.Add(metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                signature,
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1)));
        }

        BlobHandle typeSpecification = default;
        if (typeSpecificationDepth > 0)
        {
            var type = new BlobBuilder();
            for (int i = 0; i < typeSpecificationDepth; i++)
                type.WriteByte(0x1D);
            type.WriteByte(0x08);
            typeSpecification = metadata.GetOrAddBlob(type);
        }

        foreach (MethodDefinitionHandle method in methods)
        {
            var parameter = metadata.AddGenericParameter(
                method,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
            for (int i = 0; i < constraintCopies; i++)
            {
                EntityHandle constraint = typeSpecificationDepth == 0
                    ? disposable
                    : metadata.AddTypeSpecification(typeSpecification);
                metadata.AddGenericParameterConstraint(parameter, constraint);
            }
        }

        var pe = new ManagedPEBuilder(
            new PEHeaderBuilder(
                imageCharacteristics:
                    Characteristics.Dll | Characteristics.ExecutableImage),
            new MetadataRootBuilder(metadata),
            ilStream: new BlobBuilder());
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildMethodSignatureImage(byte[] signature)
    {
        var metadata = CreateSingleTypeMetadata("MethodSignature");
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildManyMethodGenericParametersImage(
        int genericParameterCount,
        int genericParameterNameLength)
    {
        var metadata = CreateSingleTypeMetadata("ManyMethodGenerics");
        var signature = new BlobBuilder();
        signature.WriteByte(0x10);
        signature.WriteCompressedInteger(genericParameterCount);
        signature.WriteCompressedInteger(0);
        signature.WriteByte(0x01);
        MethodDefinitionHandle method =
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                metadata.GetOrAddBlob(signature),
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));
        StringHandle name = metadata.GetOrAddString(
            new string('T', genericParameterNameLength));
        for (int i = 0; i < genericParameterCount; i++)
        {
            metadata.AddGenericParameter(
                method,
                GenericParameterAttributes.None,
                name,
                i);
        }
        return Serialize(metadata);
    }

    static byte[] BuildDuplicateMethodsImage(int methodCount)
    {
        var metadata = CreateSingleTypeMetadata("DuplicateMethods");
        BlobHandle signature = metadata.GetOrAddBlob(
            new byte[] { 0x00, 0x00, 0x01 });
        StringHandle name = metadata.GetOrAddString("M");
        for (int i = 0; i < methodCount; i++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                name,
                signature,
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));
        }
        return Serialize(metadata);
    }

    static byte[] BuildWidePrimitiveMethodImage(int parameterCount)
    {
        var metadata = CreateSingleTypeMetadata("WidePrimitiveMethod");
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(parameterCount);
        signature.WriteByte(0x01);
        for (int i = 0; i < parameterCount; i++)
            signature.WriteByte(0x08);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildAssemblyKeyMethodImage(int keyLength)
    {
        var metadata = CreateSingleTypeMetadata("AssemblyKeyMethod");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Dependency"),
            new Version(1, 0, 0, 0),
            default,
            metadata.GetOrAddBlob(new byte[keyLength]),
            default,
            default);
        metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("Dependency"),
            metadata.GetOrAddString("Token"));
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x01);
        signature.WriteByte(0x12);
        signature.WriteCompressedInteger((1 << 2) | 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildOversizedMethodNameImage(int nameLength)
    {
        var metadata = CreateSingleTypeMetadata(
            "OversizedMethodName");
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(new string('M', nameLength)),
            metadata.GetOrAddBlob(
                new byte[] { 0x00, 0x00, 0x01 }),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildOversizedTypeReferenceNameImage(int nameLength)
    {
        var metadata = CreateSingleTypeMetadata(
            "OversizedTypeReferenceName");
        AssemblyReferenceHandle assembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("Dependency"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("Dependency"),
            metadata.GetOrAddString(
                new string('T', nameLength)));
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x01);
        signature.WriteByte(0x12);
        signature.WriteCompressedInteger((1 << 2) | 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildRepeatedTypeReferenceNameImage(
        int parameterCount,
        int typeNameLength)
    {
        var metadata = CreateSingleTypeMetadata(
            "RepeatedTypeReferenceName");
        AssemblyReferenceHandle assembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("Dependency"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString(
                new string('T', typeNameLength)));
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(parameterCount);
        signature.WriteByte(0x01);
        for (int i = 0; i < parameterCount; i++)
        {
            signature.WriteByte(0x12);
            signature.WriteCompressedInteger((1 << 2) | 1);
        }
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildNestedArrayModoptImage(
        int parameterCount,
        int arrayDepth)
    {
        var metadata = CreateSingleTypeMetadata("NestedArrayModopt");
        var typeSpecSignature = new BlobBuilder();
        for (int i = 0; i < arrayDepth; i++)
            typeSpecSignature.WriteByte(0x1d); // ELEMENT_TYPE_SZARRAY
        typeSpecSignature.WriteByte(0x08); // ELEMENT_TYPE_I4
        TypeSpecificationHandle typeSpec =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(typeSpecSignature));
        // TypeDefOrRef coded index: TypeSpec tag = 2.
        int typeSpecCodedIndex =
            (MetadataTokens.GetRowNumber(typeSpec) << 2) | 2;

        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(parameterCount);
        signature.WriteByte(0x01); // void
        for (int i = 0; i < parameterCount; i++)
        {
            signature.WriteByte(0x20); // ELEMENT_TYPE_CMOD_OPT
            signature.WriteCompressedInteger(typeSpecCodedIndex);
            signature.WriteByte(0x08); // I4
        }
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildWideGenericModoptImage(
        int parameterCount,
        int genericArity)
    {
        var metadata = CreateSingleTypeMetadata("WideGenericModopt");
        AssemblyReferenceHandle assembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("Dependency"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("G"));

        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x15); // ELEMENT_TYPE_GENERICINST
        typeSpecSignature.WriteByte(0x12); // ELEMENT_TYPE_CLASS
        typeSpecSignature.WriteCompressedInteger((1 << 2) | 1); // TypeRef 1
        typeSpecSignature.WriteCompressedInteger(genericArity);
        for (int i = 0; i < genericArity; i++)
        {
            typeSpecSignature.WriteByte(0x13); // ELEMENT_TYPE_VAR
            typeSpecSignature.WriteCompressedInteger(0);
        }
        TypeSpecificationHandle typeSpec =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(typeSpecSignature));
        int typeSpecCodedIndex =
            (MetadataTokens.GetRowNumber(typeSpec) << 2) | 2;

        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(parameterCount);
        signature.WriteByte(0x01);
        for (int i = 0; i < parameterCount; i++)
        {
            signature.WriteByte(0x20); // CMOD_OPT
            signature.WriteCompressedInteger(typeSpecCodedIndex);
            signature.WriteByte(0x08);
        }
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildUniqueLongTypeRefModoptImage(
        int uniqueCount,
        int typeNameLength)
    {
        var metadata = CreateSingleTypeMetadata("UniqueLongTypeRefModopt");
        AssemblyReferenceHandle assembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("Dependency"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        for (int i = 0; i < uniqueCount; i++)
        {
            metadata.AddTypeReference(
                assembly,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString(new string('T', typeNameLength) + i));
        }

        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(uniqueCount);
        signature.WriteByte(0x01);
        for (int i = 0; i < uniqueCount; i++)
        {
            signature.WriteByte(0x20); // CMOD_OPT
            signature.WriteCompressedInteger(((i + 1) << 2) | 1); // TypeRef i+1
            signature.WriteByte(0x08);
        }
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildWideTypeRefGenericModoptImage(
        int parameterCount,
        int genericArity)
    {
        var metadata = CreateSingleTypeMetadata("WideTypeRefGenericModopt");
        AssemblyReferenceHandle assembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("Dependency"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        // Row 1: short leaf reused across the generic arity (cache-hit path).
        metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("T"));
        // Row 2: open generic type.
        metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("G"));

        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x15); // ELEMENT_TYPE_GENERICINST
        typeSpecSignature.WriteByte(0x12); // ELEMENT_TYPE_CLASS
        typeSpecSignature.WriteCompressedInteger((2 << 2) | 1); // TypeRef 2
        typeSpecSignature.WriteCompressedInteger(genericArity);
        for (int i = 0; i < genericArity; i++)
        {
            typeSpecSignature.WriteByte(0x12); // ELEMENT_TYPE_CLASS
            typeSpecSignature.WriteCompressedInteger((1 << 2) | 1); // TypeRef 1
        }
        TypeSpecificationHandle typeSpec =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(typeSpecSignature));
        int typeSpecCodedIndex =
            (MetadataTokens.GetRowNumber(typeSpec) << 2) | 2;

        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(parameterCount);
        signature.WriteByte(0x01);
        for (int i = 0; i < parameterCount; i++)
        {
            signature.WriteByte(0x20); // CMOD_OPT
            signature.WriteCompressedInteger(typeSpecCodedIndex);
            signature.WriteByte(0x08);
        }
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildConstraintTypeSpecImage(
        byte[] typeSpecSignature)
    {
        var metadata = CreateSingleTypeMetadata("ConstraintTypeSpec");
        TypeSpecificationHandle typeSpec =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(typeSpecSignature));
        MethodDefinitionHandle method =
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                metadata.GetOrAddBlob(
                    new byte[] { 0x10, 0x01, 0x00, 0x01 }),
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));
        GenericParameterHandle parameter =
            metadata.AddGenericParameter(
                method,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
        metadata.AddGenericParameterConstraint(parameter, typeSpec);
        return Serialize(metadata);
    }

    static byte[] BuildWideGenericParameterImage(
        int parameterCount,
        int genericParameterNameLength)
    {
        var metadata = CreateSingleTypeMetadata(
            "WideAnchor",
            "C`1",
            out TypeDefinitionHandle type);
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(parameterCount);
        signature.WriteByte(0x01);
        for (int i = 0; i < parameterCount; i++)
        {
            signature.WriteByte(0x13);
            signature.WriteCompressedInteger(0);
        }
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        metadata.AddGenericParameter(
            type,
            GenericParameterAttributes.None,
            metadata.GetOrAddString(
                new string('X', genericParameterNameLength)),
            index: 0);
        return Serialize(metadata);
    }

    static byte[] BuildWideArrayRankImage(
        int parameterCount,
        int rank)
    {
        var metadata = CreateSingleTypeMetadata("WideArrayRank");
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(parameterCount);
        signature.WriteByte(0x01);
        for (int i = 0; i < parameterCount; i++)
        {
            signature.WriteByte(0x14);
            signature.WriteByte(0x08);
            signature.WriteCompressedInteger(rank);
            signature.WriteCompressedInteger(0);
            signature.WriteCompressedInteger(0);
        }
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildManyNamedMethodsImage(
        int methodCount,
        int methodNameLength)
    {
        var metadata = CreateSingleTypeMetadata("ManyNamedMethods");
        BlobHandle signature = metadata.GetOrAddBlob(
            new byte[] { 0x00, 0x00, 0x01 });
        for (int i = 0; i < methodCount; i++)
        {
            string suffix = i.ToString("D3");
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(
                    new string(
                        'M',
                        methodNameLength - suffix.Length)
                    + suffix),
                signature,
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));
        }
        return Serialize(metadata);
    }

    static MetadataBuilder CreateSingleTypeMetadata(
        string name,
        string typeName = "C")
        => CreateSingleTypeMetadata(name, typeName, out _);

    static MetadataBuilder CreateSingleTypeMetadata(
        string name,
        string typeName,
        out TypeDefinitionHandle type)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString($"{name}.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(name),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        type = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString(typeName),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        return metadata;
    }

    static byte[] Serialize(MetadataBuilder metadata)
    {
        var pe = new ManagedPEBuilder(
            new PEHeaderBuilder(
                imageCharacteristics:
                    Characteristics.Dll
                    | Characteristics.ExecutableImage),
            new MetadataRootBuilder(metadata),
            ilStream: new BlobBuilder());
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildDeepMethodSignatureImage(
        int typeDepth,
        int typeNameLength)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("DeepMethod.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("DeepMethod"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        var runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("Probe"),
            metadata.GetOrAddString(new string('X', typeNameLength)));
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x01);
        for (int i = 0; i < typeDepth; i++)
            signature.WriteByte(0x1D);
        signature.WriteByte(0x12);
        signature.WriteCompressedInteger((1 << 2) | 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            new PEHeaderBuilder(
                imageCharacteristics:
                    Characteristics.Dll | Characteristics.ExecutableImage),
            new MetadataRootBuilder(metadata),
            ilStream: new BlobBuilder());
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildNestedDeclaringTypeImage(
        int nestingDepth,
        int segmentNameLength)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("NestedMethod.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("NestedMethod"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var types =
            new List<TypeDefinitionHandle>(nestingDepth);
        for (int i = 0; i < nestingDepth; i++)
        {
            string prefix = i.ToString("D3");
            string name =
                prefix
                + new string(
                    'X',
                    segmentNameLength - prefix.Length);
            types.Add(metadata.AddTypeDefinition(
                i == 0
                    ? TypeAttributes.Public
                    : TypeAttributes.NestedPublic,
                i == 0
                    ? metadata.GetOrAddString("Probe")
                    : default,
                metadata.GetOrAddString(name),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1)));
        }

        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(
                new byte[] { 0x00, 0x00, 0x01 }),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        for (int i = 1; i < types.Count; i++)
            metadata.AddNestedType(types[i], types[i - 1]);

        var pe = new ManagedPEBuilder(
            new PEHeaderBuilder(
                imageCharacteristics:
                    Characteristics.Dll | Characteristics.ExecutableImage),
            new MetadataRootBuilder(metadata),
            ilStream: new BlobBuilder());
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

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

    static MetadataImage Open(string path) => new(path);

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

public sealed class CorrespondenceFixture
{
    public int Transform(string value) => value.Length;
}
