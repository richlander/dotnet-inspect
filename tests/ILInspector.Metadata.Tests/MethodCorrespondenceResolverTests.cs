using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.MetadataPrimitives;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

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
    public void PdbContext_NonMethodTokenFailsCorrespondence()
    {
        using var source = PdbContext.OpenMetadataOnly(AssemblyPath);
        using var target = PdbContext.OpenMetadataOnly(AssemblyPath);

        MethodCorrespondenceResult result =
            target.ResolveMethodCorrespondence(
                source,
                MetadataTokens.GetToken(
                    MetadataTokens.TypeDefinitionHandle(1)));

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Null(result.Target);
        Assert.Contains("not a MethodDef", result.Failure);
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
            MethodCorrespondenceResolver.ResolveApiMember(
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
        MethodCorrespondenceResult apiResult =
            MethodCorrespondenceResolver.ResolveApiMember(
                sourceReader,
                MetadataMethodAddress.Create(sourceReader, sourceMethod),
                targetPe.GetMetadataReader());
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("method table", result.Failure);
        Assert.Equal(MethodCorrespondenceStatus.Failed, apiResult.Status);
        Assert.Contains("method table", apiResult.Failure);
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
        MethodCorrespondenceResult apiResult =
            MethodCorrespondenceResolver.ResolveApiMember(
                sourceReader,
                MetadataMethodAddress.Create(sourceReader, sourceMethod),
                targetPe.GetMetadataReader());

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Empty(result.Candidates);
        Assert.Contains("matching target methods", result.Failure);
        Assert.Equal(MethodCorrespondenceStatus.Failed, apiResult.Status);
        Assert.Empty(apiResult.Candidates);
        Assert.Contains("matching target methods", apiResult.Failure);
    }

    [Fact]
    public void ResolveApiMember_DuplicateCandidatesAreAmbiguous()
    {
        byte[] sourceImage = BuildDuplicateMethodsImage(1);
        byte[] targetImage = BuildDuplicateMethodsImage(2);
        using var sourcePe = new PEReader(new MemoryStream(sourceImage));
        using var targetPe = new PEReader(new MemoryStream(targetImage));
        MetadataReader sourceReader = sourcePe.GetMetadataReader();
        MethodDefinitionHandle sourceMethod =
            sourceReader.MethodDefinitions.Single();

        MethodCorrespondenceResult result =
            MethodCorrespondenceResolver.ResolveApiMember(
                sourceReader,
                MetadataMethodAddress.Create(
                    sourceReader,
                    sourceMethod),
                targetPe.GetMetadataReader());

        Assert.Equal(MethodCorrespondenceStatus.Ambiguous, result.Status);
        Assert.Null(result.Target);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Contains("2 target methods", result.Failure);
        Assert.Contains("API member identity", result.Failure);
    }

    [Fact]
    public void ResolveApiMember_ConstructorDecoratedAnchorNameUsesRawMetadataName()
    {
        byte[] image = BuildNamedMethodSignatureImage(
            ".ctor",
            [0x20, 0x00, 0x01],
            MethodAttributes.Public
                | MethodAttributes.SpecialName
                | MethodAttributes.RTSpecialName);
        using var sourcePe = new PEReader(new MemoryStream(image));
        using var targetPe = new PEReader(new MemoryStream(image));
        MetadataReader sourceReader = sourcePe.GetMetadataReader();
        MethodDefinitionHandle sourceMethod =
            sourceReader.MethodDefinitions.Single();

        MethodCorrespondenceResult result =
            MethodCorrespondenceResolver.ResolveApiMember(
                sourceReader,
                MetadataMethodAddress.Create(
                    sourceReader,
                    sourceMethod),
                targetPe.GetMetadataReader());

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Equal("#ctor", result.Anchor?.MemberName);
    }

    [Fact]
    public void ResolveApiMember_GenericDecoratedAnchorNameUsesRawMetadataName()
    {
        byte[] image = BuildGenericMethodImage();
        using var sourcePe = new PEReader(new MemoryStream(image));
        using var targetPe = new PEReader(new MemoryStream(image));
        MetadataReader sourceReader = sourcePe.GetMetadataReader();
        MethodDefinitionHandle sourceMethod =
            sourceReader.MethodDefinitions.Single();

        MethodCorrespondenceResult result =
            MethodCorrespondenceResolver.ResolveApiMember(
                sourceReader,
                MetadataMethodAddress.Create(
                    sourceReader,
                    sourceMethod),
                targetPe.GetMetadataReader());

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Equal("M<T>", result.Anchor?.MemberName);
    }

    [Fact]
    public void ResolveApiMember_ParameterDirectionMismatchIsAbsent()
    {
        byte[] sourceImage =
            BuildDirectionalMethodImage(ParameterAttributes.None);
        byte[] targetImage =
            BuildDirectionalMethodImage(ParameterAttributes.Out);
        using var sourcePe =
            new PEReader(new MemoryStream(sourceImage));
        using var targetPe =
            new PEReader(new MemoryStream(targetImage));
        MetadataReader sourceReader = sourcePe.GetMetadataReader();
        MethodDefinitionHandle sourceMethod =
            sourceReader.MethodDefinitions.Single();

        MethodCorrespondenceResult result =
            MethodCorrespondenceResolver.ResolveApiMember(
                sourceReader,
                MetadataMethodAddress.Create(
                    sourceReader,
                    sourceMethod),
                targetPe.GetMetadataReader());

        Assert.Equal(MethodCorrespondenceStatus.Absent, result.Status);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_ReturnTypeMismatchIsAbsent()
    {
        byte[] sourceImage = BuildNamedMethodSignatureImage(
            "M",
            [0x00, 0x00, 0x08],
            MethodAttributes.Public | MethodAttributes.Static);
        byte[] targetImage = BuildNamedMethodSignatureImage(
            "M",
            [0x00, 0x00, 0x0e],
            MethodAttributes.Public | MethodAttributes.Static);
        using var sourcePe =
            new PEReader(new MemoryStream(sourceImage));
        using var targetPe =
            new PEReader(new MemoryStream(targetImage));
        MetadataReader sourceReader = sourcePe.GetMetadataReader();
        MethodDefinitionHandle sourceMethod =
            sourceReader.MethodDefinitions.Single();

        MethodCorrespondenceResult result =
            MethodCorrespondenceResolver.ResolveApiMember(
                sourceReader,
                MetadataMethodAddress.Create(
                    sourceReader,
                    sourceMethod),
                targetPe.GetMetadataReader());

        Assert.Equal(MethodCorrespondenceStatus.Absent, result.Status);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_InstanceMismatchIsAbsent()
    {
        byte[] sourceImage = BuildNamedMethodSignatureImage(
            "M",
            [0x00, 0x00, 0x01],
            MethodAttributes.Public | MethodAttributes.Static);
        byte[] targetImage = BuildNamedMethodSignatureImage(
            "M",
            [0x20, 0x00, 0x01],
            MethodAttributes.Public);
        using var sourcePe =
            new PEReader(new MemoryStream(sourceImage));
        using var targetPe =
            new PEReader(new MemoryStream(targetImage));
        MetadataReader sourceReader = sourcePe.GetMetadataReader();
        MethodDefinitionHandle sourceMethod =
            sourceReader.MethodDefinitions.Single();

        MethodCorrespondenceResult result =
            MethodCorrespondenceResolver.ResolveApiMember(
                sourceReader,
                MetadataMethodAddress.Create(
                    sourceReader,
                    sourceMethod),
                targetPe.GetMetadataReader());

        Assert.Equal(MethodCorrespondenceStatus.Absent, result.Status);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_RequiredReturnModifierMismatchIsAbsent()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildModifiedReturnMethodImage(
                required: true,
                modifierName: "IsExternalInit"),
            BuildModifiedReturnMethodImage(
                required: null,
                modifierName: null));

        Assert.Equal(MethodCorrespondenceStatus.Absent, result.Status);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_OptionalReturnModifierMismatchRemainsExact()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildModifiedReturnMethodImage(
                required: false,
                modifierName: "OptionalMarker"),
            BuildModifiedReturnMethodImage(
                required: null,
                modifierName: null));

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_FunctionPointerCallingConventionMismatchIsAbsent()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildFunctionPointerMethodImage(
                nestedHeader: 0x01),
            BuildFunctionPointerMethodImage(
                nestedHeader: 0x02));

        Assert.Equal(MethodCorrespondenceStatus.Absent, result.Status);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_ExtensibleFunctionPointerConventionMismatchIsAbsent()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildFunctionPointerMethodImage(
                nestedHeader: 0x09,
                conventionModifier: "CallConvCdecl"),
            BuildFunctionPointerMethodImage(
                nestedHeader: 0x09,
                conventionModifier: "CallConvStdcall"));

        Assert.Equal(MethodCorrespondenceStatus.Absent, result.Status);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_CompilerProducedRequiredModifierAndFunctionPointerMismatchesAreAbsent()
    {
        byte[] sourceImage = CompileFixture(
            "CorrespondenceSource",
            """
            public class C
            {
                public int P { get; init; }
                public static unsafe void M(delegate* unmanaged[Cdecl]<void> value) { }
            }
            """);
        byte[] targetImage = CompileFixture(
            "CorrespondenceTarget",
            """
            public class C
            {
                public int P { get; set; }
                public static unsafe void M(delegate* unmanaged[Stdcall]<void> value) { }
            }
            """);
        using var sourcePe =
            new PEReader(new MemoryStream(sourceImage));
        using var targetPe =
            new PEReader(new MemoryStream(targetImage));
        MetadataReader sourceReader =
            sourcePe.GetMetadataReader();
        MetadataReader targetReader =
            targetPe.GetMetadataReader();

        MethodCorrespondenceResult setter =
            MethodCorrespondenceResolver.ResolveApiMember(
                sourceReader,
                MetadataMethodAddress.Create(
                    sourceReader,
                    FindMethod(
                        sourceReader,
                        "C",
                        "set_P")),
                targetReader);
        MethodCorrespondenceResult functionPointer =
            MethodCorrespondenceResolver.ResolveApiMember(
                sourceReader,
                MetadataMethodAddress.Create(
                    sourceReader,
                    FindMethod(
                        sourceReader,
                        "C",
                        "M")),
                targetReader);

        Assert.Equal(MethodCorrespondenceStatus.Absent, setter.Status);
        Assert.Equal(
            MethodCorrespondenceStatus.Absent,
            functionPointer.Status);
    }

    [Fact]
    public void ResolveApiMember_EncodedGenericArityMismatchFails()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildGenericMethodImage(),
            BuildGenericMethodImage(
                encodedArity: 2,
                rowCount: 1));

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("encoded generic arity", result.Failure);
    }

    [Fact]
    public void ResolveApiMember_EncodedGenericArityMismatchOnNonmatchingOverloadDoesNotPoisonExactCandidate()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildMethodDefinitionArityBoundaryImage(
                genericParameterRowCount: 0,
                encodedGenericArity: 0,
                methodParameterIndex: null),
            BuildNonmatchingMalformedGenericOverloadImage());

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_OutOfRangeParameterRowFails()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildParameterRowMethodImage(
                parameterCount: 0,
                parameterSequence: null),
            BuildParameterRowMethodImage(
                parameterCount: 0,
                parameterSequence: 1));

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("encoded parameter count", result.Failure);
    }

    [Fact]
    public void ResolveApiMember_OmittedParameterRowsRemainExact()
    {
        byte[] image = BuildParameterRowMethodImage(
            parameterCount: 1,
            parameterSequence: null);

        MethodCorrespondenceResult result =
            ResolveApiMember(image, image);

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_ReturnParameterDirectionDoesNotAffectIdentity()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildParameterRowMethodImage(
                parameterCount: 0,
                parameterSequence: 0,
                parameterAttributes: ParameterAttributes.Out),
            BuildParameterRowMethodImage(
                parameterCount: 0,
                parameterSequence: null));

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_DifferentDefiningAssemblyIsAbsent()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildScopedTypeMethodImage("Dependency.A"),
            BuildScopedTypeMethodImage("Dependency.B"));

        Assert.Equal(MethodCorrespondenceStatus.Absent, result.Status);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_AssemblyScopeCaseFoldingUsesOrdinalIdentity()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildScopedTypeMethodImage("\u017f"),
            BuildScopedTypeMethodImage("S"));

        Assert.Equal(MethodCorrespondenceStatus.Absent, result.Status);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_AssemblyScopeOrdinaryCaseDifferenceRemainsExact()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildScopedTypeMethodImage(
                "dependency",
                culture: "en-us"),
            BuildScopedTypeMethodImage(
                "DEPENDENCY",
                culture: "EN-US"));

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_CoreLibraryFacadeScopesCorrespond()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildScopedTypeMethodImage(
                "system.runtime",
                publicKeyToken:
                    [0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a]),
            BuildScopedTypeMethodImage(
                "SYSTEM.PRIVATE.CORELIB",
                publicKeyToken:
                    [0x7c, 0xec, 0x85, 0xd7, 0xbe, 0xa7, 0x79, 0x8e]));

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_UntrustedCoreLibraryFacadeDoesNotCorrespond()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildScopedTypeMethodImage("System.Runtime"),
            BuildScopedTypeMethodImage(
                "System.Private.CoreLib",
                publicKeyToken:
                    [0x7c, 0xec, 0x85, 0xd7, 0xbe, 0xa7, 0x79, 0x8e]));

        Assert.Equal(MethodCorrespondenceStatus.Absent, result.Status);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_ReferencePackCoreLibraryFacadeMatchesRuntime()
    {
        const string AssemblyFileName = "System.Collections.dll";
        string? referencePath =
            FindPlatformReferenceAssembly(AssemblyFileName);
        string runtimePath = Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!,
            AssemblyFileName);
        Assert.SkipWhen(
            referencePath is null || !File.Exists(runtimePath),
            "The matching reference-pack and runtime assemblies are unavailable.");

        using var sourcePe =
            new PEReader(File.OpenRead(referencePath));
        using var targetPe =
            new PEReader(File.OpenRead(runtimePath));
        MetadataReader source = sourcePe.GetMetadataReader();
        MetadataReader target = targetPe.GetMetadataReader();
        AssemblyReferenceIdentity sourceScope =
            FindTypeReferenceAssembly(
                source,
                "System.Collections",
                "IComparer");
        AssemblyReferenceIdentity targetScope =
            FindTypeReferenceAssembly(
                target,
                "System.Collections",
                "IComparer");
        Assert.NotEqual(sourceScope.Name, targetScope.Name);
        Assert.True(
            PlatformKeys.IsCoreLibraryFacadeReference(sourceScope));
        Assert.True(
            PlatformKeys.IsCoreLibraryFacadeReference(targetScope));
        MethodDefinitionHandle sourceMethod = FindMethod(
            source,
            "StructuralComparisons",
            "get_StructuralComparer");

        MethodCorrespondenceResult result =
            MethodCorrespondenceResolver.ResolveApiMember(
                source,
                MetadataMethodAddress.Create(source, sourceMethod),
                target);

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_CurrentTypeDefMatchesTargetCoreLibraryForwarder()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: true,
                includeForwarder: false),
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: false,
                includeForwarder: true));

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_CurrentTypeDefWithoutTargetForwarderIsAbsent()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: true,
                includeForwarder: false),
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: false,
                includeForwarder: false));

        Assert.Equal(MethodCorrespondenceStatus.Absent, result.Status);
        Assert.Empty(result.Candidates);
    }

    [Theory]
    [InlineData(ForwarderTargetKind.NonCoreLibrary)]
    [InlineData(ForwarderTargetKind.UnsignedCoreLibrary)]
    [InlineData(ForwarderTargetKind.ForgedCoreLibrary)]
    [InlineData(ForwarderTargetKind.File)]
    [InlineData(ForwarderTargetKind.MissingForwarderFlag)]
    [InlineData(ForwarderTargetKind.NestedVisibility)]
    public void ResolveApiMember_UntrustedTargetForwarderDoesNotAuthorizeCurrentTypeDef(
        ForwarderTargetKind targetKind)
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: true,
                includeForwarder: false),
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: false,
                includeForwarder: true,
                forwarderTargetKind: targetKind));

        Assert.Equal(MethodCorrespondenceStatus.Absent, result.Status);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_RealNestedForwarderRowDoesNotConflictWithRootEvidence()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: true,
                includeForwarder: false,
                typeNamespace: ""),
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: false,
                includeForwarder: true,
                includeNestedForwarderRow: true,
                typeNamespace: ""));

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_CompetingForwarderForMatchedRootFails()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: true,
                includeForwarder: false),
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: false,
                includeForwarder: true,
                includeConflictingForwarder: true));

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("conflicting exported-root evidence", result.Failure);
    }

    [Fact]
    public void ResolveApiMember_MalformedForwarderForMatchedRootFails()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: true,
                includeForwarder: false),
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: false,
                includeForwarder: true,
                includeMalformedForwarder: true,
                malformedForwarderMatchesRoot: true));

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("malformed exported-root evidence", result.Failure);
    }

    [Fact]
    public void ResolveApiMember_OnlyMalformedForwarderForMatchedRootFails()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: true,
                includeForwarder: false),
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: false,
                includeForwarder: false,
                includeMalformedForwarder: true,
                malformedForwarderMatchesRoot: true));

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("malformed exported-root evidence", result.Failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveApiMember_OutOfRangeDirectForwarderImplementationFails(
        bool useFile)
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: true,
                includeForwarder: false),
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: false,
                includeForwarder: false,
                includeMalformedForwarder: true,
                malformedForwarderMatchesRoot: true,
                malformedForwarderHasForwarderFlag: useFile,
                malformedForwarderUsesFile: useFile));

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("malformed exported-root evidence", result.Failure);
    }

    [Fact]
    public void ResolveApiMember_TargetForwardersAreChargedOncePerReader()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: true,
                includeForwarder: false),
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: false,
                includeForwarder: true,
                methodCount: 100,
                noiseForwarderCount: 2_000));

        Assert.Equal(MethodCorrespondenceStatus.Ambiguous, result.Status);
        Assert.Equal(100, result.Candidates.Count);
    }

    [Fact]
    public void ResolveApiMember_UnrelatedMalformedForwarderDoesNotAuthorizeOrFail()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: true,
                includeForwarder: false),
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: false,
                includeForwarder: true,
                includeMalformedForwarder: true));

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_UnrelatedInvalidImplementationTagDoesNotFail()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: true,
                includeForwarder: false),
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: false,
                includeForwarder: true,
                includeInvalidImplementationTagForwarder: true));

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_InvalidImplementationTagForMatchedRootFails()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: true,
                includeForwarder: false),
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: false,
                includeForwarder: false,
                includeInvalidImplementationTagForwarder: true,
                invalidImplementationTagMatchesRoot: true));

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("malformed exported-root evidence", result.Failure);
    }

    [Fact]
    public void ResolveApiMember_ReusedMalformedForwarderAssemblyReferenceIsProjectedOnce()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: true,
                includeForwarder: false),
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: false,
                includeForwarder: true,
                noiseForwarderCount: 9,
                noisePublicKeyBytes: 512 * 1024,
                noisePublicKeyIsToken: true));

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_MalformedAssemblyReferenceStorageDoesNotRepeatCharges()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: true,
                includeForwarder: false),
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: false,
                includeForwarder: true,
                noiseForwarderCount: 9,
                noisePublicKeyBytes: 8,
                noisePublicKeyIsToken: true,
                noiseAssemblyNameLength: 512 * 1024,
                invalidNoiseCultureHandle: true));

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_DistinctMalformedAssemblyReferenceStorageIsCharged()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: true,
                includeForwarder: false),
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: false,
                includeForwarder: true,
                noiseForwarderCount: 8,
                noisePublicKeyBytes: 8,
                noisePublicKeyIsToken: true,
                noiseAssemblyNameLength: 512 * 1024,
                invalidNoiseCultureHandle: true,
                distinctNoiseAssemblyReferences: true));

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("work budget", result.Failure);
    }

    [Fact]
    public void ResolveApiMember_ReusedForwarderAssemblyReferenceIsProjectedOnce()
    {
        byte[] source =
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: true,
                includeForwarder: false);
        byte[] target =
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: false,
                includeForwarder: true,
                noiseForwarderCount: 256,
                noisePublicKeyBytes: 64 * 1024);

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        MethodCorrespondenceResult result =
            ResolveApiMember(source, target);
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Single(result.Candidates);
        Assert.InRange(allocated, 0, 4 * 1024 * 1024);
    }

    [Fact]
    public void ResolveApiMember_ReusedGenericAssemblyReferenceDoesNotRepeatPublicKeyInStructuralBudget()
    {
        byte[] source =
            BuildRepeatedGenericAssemblyScopeImage(
                parameterCount: 2,
                publicKeyBytes: 512 * 1024,
                typeNameLength: 4);
        byte[] target =
            BuildRepeatedGenericAssemblyScopeImage(
                parameterCount: 2,
                publicKeyBytes: 512 * 1024,
                typeNameLength: 4);

        MethodCorrespondenceResult result =
            ResolveApiMember(source, target);

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_EmptyFullAssemblyKeyCountsNormalizedTokenInStructuralBudget()
    {
        byte[] image =
            BuildRepeatedGenericAssemblyScopeImage(
                parameterCount: 9_000,
                publicKeyBytes: 16,
                typeNameLength: 4,
                reuseTypeReference: true,
                emptyNonNilPublicKey: true);

        MethodCorrespondenceResult result =
            ResolveApiMember(image, image);

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("encoded-character budget", result.Failure);
    }

    [Fact]
    public void ResolveApiMember_ReusedGenericAssemblyReferenceIsProjectedOnceBeforeBudgetFailure()
    {
        byte[] source =
            BuildRepeatedGenericAssemblyScopeImage(
                parameterCount: 100,
                publicKeyBytes: 512 * 1024,
                typeNameLength: 16 * 1024);
        byte[] target =
            BuildRepeatedGenericAssemblyScopeImage(
                parameterCount: 1,
                publicKeyBytes: 8,
                typeNameLength: 4);

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        MethodCorrespondenceResult result =
            ResolveApiMember(source, target);
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("budget", result.Failure);
        Assert.InRange(allocated, 0, 8 * 1024 * 1024);
    }

    [Fact]
    public void ResolveApiMember_DistinctTypeReferencesSharingLargeAssemblyNameFailBeforeScanAmplification()
    {
        byte[] source =
            BuildRepeatedGenericAssemblyScopeImage(
                parameterCount: 65_000,
                publicKeyBytes: 8,
                typeNameLength: 4,
                assemblyNameLength: 1_000_000,
                genericInstantiation: false);
        byte[] target =
            BuildRepeatedGenericAssemblyScopeImage(
                parameterCount: 1,
                publicKeyBytes: 8,
                typeNameLength: 4,
                genericInstantiation: false);

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        MethodCorrespondenceResult result =
            ResolveApiMember(source, target);
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("budget", result.Failure);
        Assert.InRange(allocated, 0, 4 * 1024 * 1024);
    }

    [Fact]
    public void ResolveApiMember_ReusedTypeDefinitionGenericParametersAreProjectedOnce()
    {
        byte[] source =
            BuildRepeatedTypeDefinitionArityScanImage(
                methodCount: 1,
                genericParameterCount: 0);
        byte[] target =
            BuildRepeatedTypeDefinitionArityScanImage(
                methodCount: 5_000,
                genericParameterCount: 65_000);

        var timer = System.Diagnostics.Stopwatch.StartNew();
        MethodCorrespondenceResult result =
            ResolveApiMember(source, target);
        timer.Stop();

        Assert.True(
            result.Status == MethodCorrespondenceStatus.Absent,
            result.Failure);
        Assert.True(
            timer.Elapsed < TimeSpan.FromMilliseconds(500),
            $"Correspondence took {timer.Elapsed}.");
    }

    [Fact]
    public void MethodCorrespondenceContext_TypeDefinitionGenericParametersAreChargedOnce()
    {
        using var pe =
            new PEReader(
                new MemoryStream(
                    BuildRepeatedTypeDefinitionArityScanImage(
                        methodCount: 1,
                        genericParameterCount: 65_000)));
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle type =
            reader.TypeDefinitions.Last();
        var context =
            new ApiMemberIdentity.MethodCorrespondenceContext();
        int charged = 0;

        Assert.True(
            context.TryGetTypeDefinitionGenericParameterCount(
                reader,
                type,
                value => charged += value,
                out int firstCount));
        Assert.True(
            context.TryGetTypeDefinitionGenericParameterCount(
                reader,
                type,
                value => charged += value,
                out int secondCount));

        Assert.Equal(65_000, firstCount);
        Assert.Equal(firstCount, secondCount);
        Assert.Equal(65_000, charged);
    }

    [Fact]
    public void MethodCorrespondenceContext_MalformedTypeDefinitionGenericParametersAreChargedOnce()
    {
        using var pe =
            new PEReader(
                new MemoryStream(
                    BuildRepeatedTypeDefinitionArityScanImage(
                        methodCount: 1,
                        genericParameterCount: 65_000,
                        malformedGenericParameterIndex: true)));
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle type =
            reader.TypeDefinitions.Last();
        var context =
            new ApiMemberIdentity.MethodCorrespondenceContext();
        int charged = 0;

        Assert.False(
            context.TryGetTypeDefinitionGenericParameterCount(
                reader,
                type,
                value => charged += value,
                out int firstCount));
        Assert.False(
            context.TryGetTypeDefinitionGenericParameterCount(
                reader,
                type,
                value => charged += value,
                out int secondCount));

        Assert.Equal(-1, firstCount);
        Assert.Equal(firstCount, secondCount);
        Assert.Equal(65_000, charged);
    }

    [Fact]
    public void ResolveApiMember_HiddenMaximumTypeArityFails()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildTypeDefinitionArityBoundaryImage(
                genericParameterCount: 0,
                declareArity: false),
            BuildTypeDefinitionArityBoundaryImage(
                genericParameterCount: 65_536,
                declareArity: false));

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("metadata-name arity", result.Failure);
    }

    [Fact]
    public void ResolveApiMember_MaximumTypeArityMatchesItself()
    {
        byte[] source =
            BuildTypeDefinitionArityBoundaryImage(
                genericParameterCount: 65_536,
                declareArity: true);
        byte[] target =
            BuildTypeDefinitionArityBoundaryImage(
                genericParameterCount: 65_536,
                declareArity: true);

        MethodCorrespondenceResult result =
            ResolveApiMember(source, target);

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Single(result.Candidates);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65_535)]
    public void ResolveApiMember_MaximumTypeArityWithSignatureReferenceMatchesItself(
        int typeParameterIndex)
    {
        byte[] source =
            BuildTypeDefinitionArityBoundaryImage(
                genericParameterCount: 65_536,
                declareArity: true,
                typeParameterIndex);
        byte[] target =
            BuildTypeDefinitionArityBoundaryImage(
                genericParameterCount: 65_536,
                declareArity: true,
                typeParameterIndex);

        MethodCorrespondenceResult result =
            ResolveApiMember(source, target);

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_MaximumNestedTypeArityWithSignatureReferenceMatchesItself()
    {
        byte[] source =
            BuildNestedTypeDefinitionArityBoundaryImage();
        byte[] target =
            BuildNestedTypeDefinitionArityBoundaryImage();

        MethodCorrespondenceResult result =
            ResolveApiMember(source, target);

        Assert.True(
            result.Status == MethodCorrespondenceStatus.Exact,
            result.Failure);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_NestedTypeRawContextUsesCumulativeRows()
    {
        byte[] source =
            BuildNestedTypeDefinitionCorrespondenceImage();
        byte[] target =
            BuildNestedTypeDefinitionCorrespondenceImage();

        MethodCorrespondenceResult result =
            ResolveApiMember(source, target);

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Equal(
            "Outer<T>+Inner<U>",
            result.Anchor!.TypeFullName);
    }

    [Fact]
    public void ResolveApiMember_HiddenMaximumMethodArityFailsInEitherDirection()
    {
        byte[] nonGeneric =
            BuildMethodDefinitionArityBoundaryImage(
                genericParameterRowCount: 0,
                encodedGenericArity: 0,
                methodParameterIndex: null);
        byte[] hidden =
            BuildMethodDefinitionArityBoundaryImage(
                genericParameterRowCount: 65_536,
                encodedGenericArity: 0,
                methodParameterIndex: null);

        Assert.Equal(
            MethodCorrespondenceStatus.Failed,
            ResolveApiMember(nonGeneric, hidden).Status);
        Assert.Equal(
            MethodCorrespondenceStatus.Failed,
            ResolveApiMember(hidden, nonGeneric).Status);
    }

    [Fact]
    public void ResolveApiMember_HiddenNearMaximumMethodArityFails()
    {
        MethodCorrespondenceResult result =
            ResolveApiMember(
                BuildMethodDefinitionArityBoundaryImage(
                    genericParameterRowCount: 0,
                    encodedGenericArity: 0,
                    methodParameterIndex: null),
                BuildMethodDefinitionArityBoundaryImage(
                    genericParameterRowCount: 65_535,
                    encodedGenericArity: 0,
                    methodParameterIndex: null));

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
    }

    [Fact]
    public void ResolveApiMember_MaximumMethodArityWithSignatureReferenceMatchesItself()
    {
        byte[] source =
            BuildMethodDefinitionArityBoundaryImage(
                genericParameterRowCount: 65_536,
                encodedGenericArity: 65_536,
                methodParameterIndex: 65_535);
        byte[] target =
            BuildMethodDefinitionArityBoundaryImage(
                genericParameterRowCount: 65_536,
                encodedGenericArity: 65_536,
                methodParameterIndex: 65_535);

        MethodCorrespondenceResult result =
            ResolveApiMember(source, target);

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void MethodCorrespondenceContext_MaximumTypeArityChargeFailureIsNotCached()
    {
        using var pe =
            new PEReader(
                new MemoryStream(
                    BuildTypeDefinitionArityBoundaryImage(
                        genericParameterCount: 65_536,
                        declareArity: true)));
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle type =
            reader.TypeDefinitions.Skip(1).First();
        var context =
            new ApiMemberIdentity.MethodCorrespondenceContext();
        int chargeCalls = 0;

        Assert.Throws<InvalidOperationException>(
            () => context.TryGetTypeDefinitionGenericParameterCount(
                reader,
                type,
                value =>
                {
                    chargeCalls++;
                    throw new InvalidOperationException();
                },
                out _));
        Assert.True(
            context.TryGetTypeDefinitionGenericParameterCount(
                reader,
                type,
                value =>
                {
                    chargeCalls++;
                    Assert.Equal(65_536, value);
                },
                out int firstCount));
        Assert.True(
            context.TryGetTypeDefinitionGenericParameterCount(
                reader,
                type,
                value => chargeCalls++,
                out int secondCount));

        Assert.Equal(65_536, firstCount);
        Assert.Equal(firstCount, secondCount);
        Assert.Equal(2, chargeCalls);
    }

    [Fact]
    public void MethodCorrespondenceContext_UnsortedGenericParameterOwnersFailOnce()
    {
        using var pe =
            new PEReader(
                new MemoryStream(
                    BuildNoncontiguousTypeDefinitionGenericParametersImage()));
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle type =
            reader.TypeDefinitions.Skip(1).First();
        var context =
            new ApiMemberIdentity.MethodCorrespondenceContext();
        int charged = 0;

        Assert.False(
            context.TryGetTypeDefinitionGenericParameterCount(
                reader,
                type,
                value => charged += value,
                out int firstCount));
        Assert.False(
            context.TryGetTypeDefinitionGenericParameterCount(
                reader,
                type,
                value => charged += value,
                out int secondCount));

        Assert.Equal(-1, firstCount);
        Assert.Equal(firstCount, secondCount);
        Assert.Equal(3, charged);
    }

    [Fact]
    public void MethodCorrespondenceContext_InterleavedOwnersUseCodedIndexOrder()
    {
        using var pe =
            new PEReader(
                new MemoryStream(
                    BuildInterleavedGenericParameterOwnersImage()));
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle type =
            reader.TypeDefinitions.Last();
        var context =
            new ApiMemberIdentity.MethodCorrespondenceContext();
        int charged = 0;

        Assert.True(
            context.TryGetTypeDefinitionGenericParameterCount(
                reader,
                type,
                value => charged += value,
                out int count));

        Assert.Equal(1, count);
        Assert.Equal(3, charged);
    }

    [Fact]
    public void MethodCorrespondenceContext_LaterOwnerChargeFailureIsNotCached()
    {
        using var pe =
            new PEReader(
                new MemoryStream(
                    BuildInterleavedGenericParameterOwnersImage()));
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle first =
            reader.TypeDefinitions.Skip(1).First();
        TypeDefinitionHandle last =
            reader.TypeDefinitions.Last();
        var context =
            new ApiMemberIdentity.MethodCorrespondenceContext();

        Assert.True(
            context.TryGetTypeDefinitionGenericParameterCount(
                reader,
                first,
                static _ => { },
                out int firstCount));
        Assert.Equal(1, firstCount);
        Assert.Throws<BadImageFormatException>(
            () => context
                .TryGetTypeDefinitionGenericParameterCount(
                    reader,
                    last,
                    static _ =>
                        throw new BadImageFormatException(
                            "budget exhausted"),
                    out _));

        Assert.True(
            context.TryGetTypeDefinitionGenericParameterCount(
                reader,
                last,
                static _ => { },
                out int lastCount));
        Assert.Equal(1, lastCount);
    }

    [Fact]
    public void ResolveApiMember_DistinctGenericAssemblyReferencesFailWithinOperationBudget()
    {
        byte[] source =
            BuildRepeatedGenericAssemblyScopeImage(
                parameterCount: 5,
                publicKeyBytes: 900 * 1024,
                typeNameLength: 4,
                distinctAssemblyReferences: true);
        byte[] target =
            BuildRepeatedGenericAssemblyScopeImage(
                parameterCount: 1,
                publicKeyBytes: 8,
                typeNameLength: 4);

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        MethodCorrespondenceResult result =
            ResolveApiMember(source, target);
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("budget", result.Failure);
        Assert.InRange(allocated, 0, 4 * 1024 * 1024);
    }

    [Fact]
    public void ResolveApiMember_ForwarderBudgetExhaustionFailsWithoutSelectingPartialEvidence()
    {
        string matchedNamespace = new('n', 1_000);
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: true,
                includeForwarder: false,
                typeNamespace: matchedNamespace),
            BuildForwarderBudgetAmbiguityImage(
                noiseForwarderCount: 55_805,
                matchedNamespace: matchedNamespace));

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("work budget", result.Failure);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_NestedForwarderFanoutDoesNotRescanParentChains()
    {
        byte[] source =
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: true,
                includeForwarder: false);
        byte[] target =
            BuildNestedForwarderFanoutImage(leafCount: 55_000);

        var timer = System.Diagnostics.Stopwatch.StartNew();
        MethodCorrespondenceResult result =
            ResolveApiMember(source, target);
        timer.Stop();

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Single(result.Candidates);
        Assert.InRange(
            timer.Elapsed,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ResolveApiMember_NestedCurrentTypeDefUsesForwardedRoot()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: true,
                includeForwarder: false,
                nested: true),
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: false,
                includeForwarder: true,
                nested: true));

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_NestedCurrentTypeDefDoesNotUseLeafForwarder()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: true,
                includeForwarder: false,
                nested: true),
            BuildCurrentToCoreLibraryForwarderImage(
                useTypeDefinition: false,
                includeForwarder: true,
                nested: true,
                forwardedRootName: "Inner"));

        Assert.Equal(MethodCorrespondenceStatus.Absent, result.Status);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_ReferencePackTypeDefMatchesRuntimeCoreLibraryForwarder()
    {
        const string AssemblyFileName =
            "System.Runtime.InteropServices.dll";
        string? referencePath =
            FindPlatformReferenceAssembly(AssemblyFileName);
        string runtimePath = Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!,
            AssemblyFileName);
        Assert.SkipWhen(
            referencePath is null || !File.Exists(runtimePath),
            "The matching reference-pack and runtime assemblies are unavailable.");

        using var sourcePe =
            new PEReader(File.OpenRead(referencePath));
        using var targetPe =
            new PEReader(File.OpenRead(runtimePath));
        MetadataReader source = sourcePe.GetMetadataReader();
        MetadataReader target = targetPe.GetMetadataReader();
        MethodDefinitionHandle sourceMethod = FindMethod(
            source,
            "SecureStringMarshal",
            "SecureStringToCoTaskMemAnsi");
        MethodDefinitionHandle targetMethod = FindMethod(
            target,
            "SecureStringMarshal",
            "SecureStringToCoTaskMemAnsi");
        Assert.Equal(
            HandleKind.TypeDefinition,
            ReadSingleParameterTypeHandle(
                source,
                sourceMethod).Kind);
        Assert.Equal(
            HandleKind.TypeReference,
            ReadSingleParameterTypeHandle(
                target,
                targetMethod).Kind);
        EntityHandle targetParameter =
            ReadSingleParameterTypeHandle(
                target,
                targetMethod);
        TypeReference targetReference =
            target.GetTypeReference(
                (TypeReferenceHandle)targetParameter);
        Assert.Equal(
            HandleKind.AssemblyReference,
            targetReference.ResolutionScope.Kind);
        Assert.True(
            PlatformKeys.IsCoreLibraryFacadeReference(
                AssemblyReferenceIdentity.From(
                    target,
                    (AssemblyReferenceHandle)
                        targetReference.ResolutionScope)));
        AssertCoreLibraryForwarder(
            target,
            "System.Security",
            "SecureString");

        MethodCorrespondenceResult result =
            MethodCorrespondenceResolver.ResolveApiMember(
                source,
                MetadataMethodAddress.Create(source, sourceMethod),
                target);

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Single(result.Candidates);
        Assert.Equal(targetMethod, result.Target?.Handle);
    }

    [Fact]
    public void ResolveApiMember_DifferentAssemblyCultureIsAbsent()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildScopedTypeMethodImage(
                "Dependency",
                culture: "en-US"),
            BuildScopedTypeMethodImage(
                "Dependency",
                culture: "fr-FR"));

        Assert.Equal(MethodCorrespondenceStatus.Absent, result.Status);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_DifferentAssemblyTokenIsAbsent()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildScopedTypeMethodImage(
                "Dependency",
                publicKeyToken: [1, 2, 3, 4, 5, 6, 7, 8]),
            BuildScopedTypeMethodImage(
                "Dependency",
                publicKeyToken: [8, 7, 6, 5, 4, 3, 2, 1]));

        Assert.Equal(MethodCorrespondenceStatus.Absent, result.Status);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_AssemblyReferenceVersionRemainsNormalized()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildScopedTypeMethodImage(
                "Dependency",
                version: new Version(1, 0, 0, 0)),
            BuildScopedTypeMethodImage(
                "Dependency",
                version: new Version(2, 0, 0, 0)));

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_ClassAndValueTypeSignaturesAreAbsentInEitherDirection()
    {
        byte[] classImage =
            BuildScopedTypeMethodImage(
                "Dependency",
                rawTypeKind: 0x12);
        byte[] valueTypeImage =
            BuildScopedTypeMethodImage(
                "Dependency",
                rawTypeKind: 0x11);

        MethodCorrespondenceResult classToValueType =
            ResolveApiMember(classImage, valueTypeImage);
        MethodCorrespondenceResult valueTypeToClass =
            ResolveApiMember(valueTypeImage, classImage);

        Assert.Equal(
            MethodCorrespondenceStatus.Absent,
            classToValueType.Status);
        Assert.Empty(classToValueType.Candidates);
        Assert.Equal(
            MethodCorrespondenceStatus.Absent,
            valueTypeToClass.Status);
        Assert.Empty(valueTypeToClass.Candidates);
    }

    [Fact]
    public void ResolveApiMember_CurrentTypeDefAndTypeRefRemainEquivalent()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildCurrentScopedTypeMethodImage(
                useTypeReference: false),
            BuildCurrentScopedTypeMethodImage(
                useTypeReference: true));

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_InvalidCurrentModuleScopeFails()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildCurrentScopedTypeMethodImage(
                useTypeReference: true,
                moduleRow: 2),
            BuildCurrentScopedTypeMethodImage(
                useTypeReference: true));

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("current-module scope", result.Failure);
    }

    [Fact]
    public void ResolveApiMember_NamedTypeSegmentsDoNotCollide()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildScopedTypeMethodImage(
                "Dependency",
                @namespace: "N",
                typeName: "T.U"),
            BuildScopedTypeMethodImage(
                "Dependency",
                @namespace: "N.T",
                typeName: "U"));

        Assert.Equal(MethodCorrespondenceStatus.Absent, result.Status);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_RenamedMethodGenericParameterRemainsExact()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildGenericMethodImage(
                parameterName: "T",
                useGenericParameter: true),
            BuildGenericMethodImage(
                parameterName: "U",
                useGenericParameter: true));

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_RenamedTypeGenericParameterRemainsExact()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildTypeGenericMethodImage("T"),
            BuildTypeGenericMethodImage("U"));

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_OutOfRangeGenericParameterIndexFails()
    {
        byte[] image = BuildGenericMethodImage(
            encodedArity: 1,
            rowCount: 1,
            useGenericParameter: true,
            genericParameterIndex: 1);

        MethodCorrespondenceResult result =
            ResolveApiMember(image, image);

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("out-of-range method generic parameter", result.Failure);
    }

    [Fact]
    public void ResolveApiMember_RejectedTypeSpecificationFails()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildTypeSpecificationMethodImage(
                overBudgetTypeSpecification: true),
            BuildTypeSpecificationMethodImage(
                overBudgetTypeSpecification: false));

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("type specification", result.Failure);
    }

    [Fact]
    public void ResolveApiMember_DeclaringTypeArityMismatchFails()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildTypeGenericMethodImage(
                "T",
                genericParameterRows: 1),
            BuildTypeGenericMethodImage(
                "T",
                genericParameterRows: 2));

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("metadata-name arity", result.Failure);
    }

    [Fact]
    public void ResolveApiMember_InvalidArrayShapeFailsInEitherImage()
    {
        byte[] valid = BuildMethodSignatureImage(
            [0x00, 0x01, 0x01, 0x14, 0x08, 0x01, 0x00, 0x00]);
        byte[] invalid = BuildMethodSignatureImage(
            [0x00, 0x01, 0x01, 0x14, 0x08, 0x01, 0x02, 0x03, 0x04, 0x00]);

        MethodCorrespondenceResult invalidTarget =
            ResolveApiMember(valid, invalid);
        MethodCorrespondenceResult invalidSource =
            ResolveApiMember(invalid, valid);

        Assert.Equal(MethodCorrespondenceStatus.Failed, invalidTarget.Status);
        Assert.Contains("array shape", invalidTarget.Failure);
        Assert.Equal(MethodCorrespondenceStatus.Failed, invalidSource.Status);
        Assert.Contains("array shape", invalidSource.Failure);
    }

    [Fact]
    public void ResolveApiMember_ZeroRankArrayFails()
    {
        byte[] image = BuildMethodSignatureImage(
            [0x00, 0x01, 0x01, 0x14, 0x08, 0x00, 0x00, 0x00]);

        MethodCorrespondenceResult result =
            ResolveApiMember(image, image);

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("array shape", result.Failure);
    }

    [Fact]
    public void ResolveApiMember_ArraySizeDifferenceIsAbsent()
    {
        MethodCorrespondenceResult result = ResolveApiMember(
            BuildMethodSignatureImage(
                [0x00, 0x01, 0x01, 0x14, 0x08, 0x01, 0x01, 0x03, 0x00]),
            BuildMethodSignatureImage(
                [0x00, 0x01, 0x01, 0x14, 0x08, 0x01, 0x01, 0x04, 0x00]));

        Assert.Equal(MethodCorrespondenceStatus.Absent, result.Status);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void ResolveApiMember_SignatureTypeDefArityMismatchFailsInEitherImage()
    {
        byte[] valid =
            BuildSignatureGenericTypeImage(genericParameterRows: 2);
        byte[] invalid =
            BuildSignatureGenericTypeImage(genericParameterRows: 1);

        MethodCorrespondenceResult invalidTarget =
            ResolveApiMember(valid, invalid);
        MethodCorrespondenceResult invalidSource =
            ResolveApiMember(invalid, valid);

        Assert.Equal(MethodCorrespondenceStatus.Failed, invalidTarget.Status);
        Assert.Contains("metadata-name arity", invalidTarget.Failure);
        Assert.Equal(MethodCorrespondenceStatus.Failed, invalidSource.Status);
        Assert.Contains("metadata-name arity", invalidSource.Failure);
    }

    [Fact]
    public void ResolveApiMember_GenericInstantiationArgumentCountMismatchFails()
    {
        byte[] image = BuildSignatureGenericTypeImage(
            genericParameterRows: 2,
            encodedArgumentCount: 1);

        MethodCorrespondenceResult result =
            ResolveApiMember(image, image);

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("argument count", result.Failure);
    }

    [Fact]
    public void ResolveApiMember_ZeroArgumentGenericInstantiationFails()
    {
        byte[] image = BuildMethodSignatureImage(
            [0x00, 0x01, 0x01, 0x15, 0x12, 0x08, 0x00]);

        MethodCorrespondenceResult result =
            ResolveApiMember(image, image);

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("at least one element", result.Failure);
    }

    [Fact]
    public void ResolveApiMember_TypeExtensionAttributesAreChargedOnce()
    {
        byte[] sourceImage =
            BuildExtensionAttributeImage(
                methodCount: 1,
                typeAttributeCount: 0,
                methodAttributeCount: 0,
                attributeNameLength: 8,
                includeTypeExtensionMarker: false,
                includeMethodExtensionMarkers: false);
        byte[] targetImage =
            BuildExtensionAttributeImage(
                methodCount: 500,
                typeAttributeCount: 500,
                methodAttributeCount: 0,
                attributeNameLength: 500,
                includeTypeExtensionMarker: false,
                includeMethodExtensionMarkers: false);

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        MethodCorrespondenceResult result =
            ResolveApiMember(sourceImage, targetImage);
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(MethodCorrespondenceStatus.Ambiguous, result.Status);
        Assert.Equal(500, result.Candidates.Count);
        Assert.True(
            allocated < 24 * 1024 * 1024,
            $"Type-attribute classification allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void ResolveApiMember_ReusedOversizedTargetMethodNameIsComparedOnce()
    {
        byte[] source =
            BuildMethodComparisonNameImage(
                methodCount: 1,
                methodName: "é");
        byte[] target =
            BuildMethodComparisonNameImage(
                methodCount: 256,
                methodName:
                    "é" + new string('a', 128 * 1024 - 1));

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        MethodCorrespondenceResult result =
            ResolveApiMember(source, target);
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(MethodCorrespondenceStatus.Absent, result.Status);
        Assert.InRange(allocated, 0, 4 * 1024 * 1024);
    }

    [Fact]
    public void ResolveApiMember_ReusedOversizedTargetTypeNameIsComparedOnce()
    {
        byte[] source =
            BuildTypeComparisonNameImage(
                typeCount: 1,
                leafName: "é");
        byte[] target =
            BuildTypeComparisonNameImage(
                typeCount: 256,
                leafName:
                    "é" + new string('a', 128 * 1024 - 1));

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        MethodCorrespondenceResult result =
            ResolveApiMember(source, target);
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(MethodCorrespondenceStatus.Absent, result.Status);
        Assert.InRange(allocated, 0, 4 * 1024 * 1024);
    }

    [Fact]
    public void ResolveApiMember_DeepDeclaringTypeCycleChecksRespectOperationBudget()
    {
        byte[] source =
            BuildTypeComparisonNameImage(
                typeCount: 1,
                leafName: "C",
                nestingDepth: 1);
        byte[] target =
            BuildTypeComparisonNameImage(
                typeCount: 256,
                leafName: "C",
                nestingDepth: MetadataSafetyPolicy.MaxRelationshipNodes);

        MethodCorrespondenceResult result =
            ResolveApiMember(source, target);

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains(
            "correspondence anchor work budget",
            result.Failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveApiMember_RepeatedDeepSignatureRelationshipsRespectOperationBudget(
        bool useTypeReference)
    {
        byte[] source =
            BuildRepeatedSignatureRelationshipImage(
                methodCount: 1,
                nestingDepth: MetadataSafetyPolicy.MaxRelationshipNodes,
                useTypeReference);
        byte[] target =
            BuildRepeatedSignatureRelationshipImage(
                methodCount:
                    MetadataSafetyPolicy.MaxCorrespondenceCandidates + 1,
                nestingDepth: MetadataSafetyPolicy.MaxRelationshipNodes,
                useTypeReference);

        MethodCorrespondenceResult result =
            ResolveApiMember(source, target);

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains(
            "correspondence anchor work budget",
            result.Failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveApiMember_MaximumDepthSignatureRelationshipsRemainExact(
        bool useTypeReference)
    {
        byte[] source =
            BuildRepeatedSignatureRelationshipImage(
                methodCount: 1,
                nestingDepth: MetadataSafetyPolicy.MaxRelationshipNodes,
                useTypeReference);
        byte[] target =
            BuildRepeatedSignatureRelationshipImage(
                methodCount: 1,
                nestingDepth: MetadataSafetyPolicy.MaxRelationshipNodes,
                useTypeReference);

        MethodCorrespondenceResult result =
            ResolveApiMember(source, target);

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
    }

    [Fact]
    public void ResolveApiMember_MethodExtensionAttributesRespectOperationBudget()
    {
        byte[] sourceImage =
            BuildExtensionAttributeImage(
                methodCount: 1,
                typeAttributeCount: 0,
                methodAttributeCount: 0,
                attributeNameLength: 8,
                includeTypeExtensionMarker: true,
                includeMethodExtensionMarkers: false);
        byte[] targetImage =
            BuildExtensionAttributeImage(
                methodCount: 1,
                typeAttributeCount: 0,
                methodAttributeCount: 70_000,
                attributeNameLength: 32,
                includeTypeExtensionMarker: true,
                includeMethodExtensionMarkers: false);
        using (var targetPe =
            new PEReader(new MemoryStream(targetImage)))
        {
            MetadataReader reader =
                targetPe.GetMetadataReader();
            TypeDefinition type =
                reader.GetTypeDefinition(
                    MetadataTokens.TypeDefinitionHandle(2));
            Assert.True(
                AttributeReader.HasExtensionAttribute(
                    reader,
                    type.GetCustomAttributes()));
            Assert.Equal(
                70_000,
                reader.MethodDefinitions.Sum(
                    handle => reader.GetMethodDefinition(handle)
                        .GetCustomAttributes().Count));
        }

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        MethodCorrespondenceResult result =
            ResolveApiMember(sourceImage, targetImage);
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("work budget", result.Failure);
        Assert.True(
            allocated < 24 * 1024 * 1024,
            $"Method-attribute rejection allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void ResolveApiMember_NestedFunctionPointerUsesCorrespondenceLength()
    {
        byte[] image =
            BuildNestedFunctionPointerBudgetImage(
                modifierCount: 2_500,
                modifierNameLength: 500);

        MethodCorrespondenceResult result =
            ResolveApiMember(image, image);

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("encoded-character budget", result.Failure);
    }

    [Fact]
    public void DurableAnchorAndStrictResolveIgnoreCorrespondenceOnlySize()
    {
        byte[] image = BuildRepeatedAssemblyScopeImage(
            parameterCount: 258,
            assemblyNameLength: 4_000);
        using var sourcePe = new PEReader(new MemoryStream(image));
        using var targetPe = new PEReader(new MemoryStream(image));
        MetadataReader source = sourcePe.GetMetadataReader();
        MetadataReader target = targetPe.GetMetadataReader();
        MethodDefinitionHandle handle =
            source.MethodDefinitions.Single();
        MethodDefinition method =
            source.GetMethodDefinition(handle);

        MethodAnchorInfo anchor =
            ApiMemberIdentity.CreateMethodAnchorInfo(
                source,
                method.GetDeclaringType(),
                method);
        MethodCorrespondenceResult strict =
            MethodCorrespondenceResolver.Resolve(
                source,
                MetadataMethodAddress.Create(source, handle),
                target);
        MethodCorrespondenceResult api =
            MethodCorrespondenceResolver.ResolveApiMember(
                source,
                MetadataMethodAddress.Create(source, handle),
                target);

        Assert.True(anchor.Anchor.CanonicalSignature.Length < 2_048);
        Assert.Equal(MethodCorrespondenceStatus.Exact, strict.Status);
        Assert.Equal(MethodCorrespondenceStatus.Failed, api.Status);
        Assert.Contains("encoded-character budget", api.Failure);
    }

    [Fact]
    public void DurableAnchorAndStrictResolveIgnoreCorrespondenceOnlyMalformedShapes()
    {
        byte[][] images =
        [
            BuildMethodSignatureImage(
                [0x00, 0x01, 0x01, 0x14, 0x08, 0x00, 0x00, 0x00]),
            BuildSignatureGenericTypeImage(genericParameterRows: 1),
        ];

        foreach (byte[] image in images)
        {
            using var sourcePe =
                new PEReader(new MemoryStream(image));
            using var targetPe =
                new PEReader(new MemoryStream(image));
            MetadataReader source = sourcePe.GetMetadataReader();
            MetadataReader target = targetPe.GetMetadataReader();
            MethodDefinitionHandle handle =
                source.MethodDefinitions.Single();
            MethodDefinition method =
                source.GetMethodDefinition(handle);

            _ = ApiMemberIdentity.CreateMethodAnchorInfo(
                source,
                method.GetDeclaringType(),
                method);
            MethodCorrespondenceResult strict =
                MethodCorrespondenceResolver.Resolve(
                    source,
                    MetadataMethodAddress.Create(source, handle),
                    target);
            MethodCorrespondenceResult api =
                MethodCorrespondenceResolver.ResolveApiMember(
                    source,
                    MetadataMethodAddress.Create(source, handle),
                    target);

            Assert.Equal(MethodCorrespondenceStatus.Exact, strict.Status);
            Assert.Equal(MethodCorrespondenceStatus.Failed, api.Status);
        }
    }

    [Fact]
    public void ResolveApiMember_RepeatedNearLimitCandidatesFailWithinOperationBudget()
    {
        byte[] sourceImage =
            BuildWideGenericModoptImage(
                parameterCount: 0,
                genericArity: 1);
        byte[] targetImage =
            BuildWideGenericModoptImage(
                parameterCount: 30,
                genericArity: 2_030,
                methodCount: 8);
        using var sourcePe =
            new PEReader(new MemoryStream(sourceImage));
        using var targetPe =
            new PEReader(new MemoryStream(targetImage));
        MetadataReader sourceReader = sourcePe.GetMetadataReader();
        MethodDefinitionHandle sourceMethod =
            sourceReader.MethodDefinitions.Single();

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        MethodCorrespondenceResult result =
            MethodCorrespondenceResolver.ResolveApiMember(
                sourceReader,
                MetadataMethodAddress.Create(
                    sourceReader,
                    sourceMethod),
                targetPe.GetMetadataReader());
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("correspondence anchor work budget", result.Failure);
        Assert.True(
            allocated < 24 * 1024 * 1024,
            $"Correspondence rejection allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void ResolveApiMember_RepeatedIdentityMaterializationFailsWithinOperationBudget()
    {
        string typeName = new('T', 4_000);
        byte[] sourceImage = BuildRepeatedApiMemberImage(
            typeName,
            "M",
            [0x00, 0x00, 0x1c],
            methodCount: 1);
        byte[] targetImage = BuildRepeatedApiMemberImage(
            typeName,
            "M",
            [0x00, 0x00, 0x01],
            methodCount: 1_000);

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        MethodCorrespondenceResult result =
            ResolveApiMember(sourceImage, targetImage);
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("correspondence anchor work budget", result.Failure);
        Assert.True(
            allocated < 24 * 1024 * 1024,
            $"Repeated identity rejection allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void ResolveApiMember_DistinctNonmatchingNamesFailWithinOperationBudget()
    {
        int nameLength =
            MetadataSafetyPolicy.MaxStructuralSignatureChars / 2;
        string prefix = new('M', nameLength - 1);
        byte[] sourceImage = BuildRepeatedApiMemberImage(
            "C",
            prefix + "A",
            [0x00, 0x00, 0x01],
            methodCount: 1);
        byte[] targetImage = BuildRepeatedApiMemberImage(
            "C",
            prefix + "B",
            [0x00, 0x00, 0x01],
            methodCount: 16,
            distinguishMethodNames: true);

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        MethodCorrespondenceResult result =
            ResolveApiMember(sourceImage, targetImage);
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Contains("correspondence anchor work budget", result.Failure);
        Assert.True(
            allocated < 24 * 1024 * 1024,
            $"Name-prefilter rejection allocated {allocated:N0} bytes.");
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
        => BuildNamedMethodSignatureImage(
            "M",
            signature,
            MethodAttributes.Public | MethodAttributes.Static);

    static byte[] BuildNamedMethodSignatureImage(
        string name,
        byte[] signature,
        MethodAttributes attributes)
    {
        var metadata = CreateSingleTypeMetadata("MethodSignature");
        metadata.AddMethodDefinition(
            attributes,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(name),
            metadata.GetOrAddBlob(signature),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildGenericMethodImage(
        int encodedArity = 1,
        int rowCount = 1,
        string parameterName = "T",
        bool useGenericParameter = false,
        int genericParameterIndex = 0)
    {
        var metadata = CreateSingleTypeMetadata("MethodSignature");
        var signature = new BlobBuilder();
        signature.WriteByte(0x10);
        signature.WriteCompressedInteger(encodedArity);
        signature.WriteCompressedInteger(
            useGenericParameter ? 1 : 0);
        signature.WriteByte(0x01);
        if (useGenericParameter)
        {
            signature.WriteByte(0x1e);
            signature.WriteCompressedInteger(
                genericParameterIndex);
        }
        MethodDefinitionHandle method =
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                metadata.GetOrAddBlob(signature),
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));
        for (int i = 0; i < rowCount; i++)
        {
            metadata.AddGenericParameter(
                method,
                GenericParameterAttributes.None,
                metadata.GetOrAddString(
                    i == 0
                        ? parameterName
                        : $"{parameterName}{i}"),
                index: i);
        }
        return Serialize(metadata);
    }

    static byte[] BuildTypeGenericMethodImage(
        string parameterName,
        int genericParameterRows = 1)
    {
        var metadata =
            CreateSingleTypeMetadata(
                "TypeGeneric",
                "C`1",
                out TypeDefinitionHandle type);
        for (int i = 0; i < genericParameterRows; i++)
        {
            metadata.AddGenericParameter(
                type,
                GenericParameterAttributes.None,
                metadata.GetOrAddString(
                    i == 0
                        ? parameterName
                        : $"{parameterName}{i}"),
                index: i);
        }
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(
                new byte[]
                {
                    0x00, 0x01, 0x01, 0x13, 0x00,
                }),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildScopedTypeMethodImage(
        string assemblyName,
        Version? version = null,
        string @namespace = "N",
        string typeName = "T",
        string? culture = null,
        byte[]? publicKeyToken = null,
        byte rawTypeKind = 0x12)
    {
        var metadata =
            CreateSingleTypeMetadata("ScopedType");
        AssemblyReferenceHandle scope =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(assemblyName),
                version ?? new Version(1, 0, 0, 0),
                culture is null
                    ? default
                    : metadata.GetOrAddString(culture),
                publicKeyToken is null
                    ? default
                    : metadata.GetOrAddBlob(publicKeyToken),
                default,
                default);
        metadata.AddTypeReference(
            scope,
            metadata.GetOrAddString(@namespace),
            metadata.GetOrAddString(typeName));
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(
                new byte[]
                {
                    0x00, 0x01, 0x01, rawTypeKind, 0x05,
                }),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildCurrentScopedTypeMethodImage(
        bool useTypeReference,
        int moduleRow = 1)
    {
        var metadata =
            CreateSingleTypeMetadata("CurrentScopedType");
        int codedIndex;
        if (useTypeReference)
        {
            TypeReferenceHandle type =
                metadata.AddTypeReference(
                    MetadataTokens.EntityHandle(moduleRow),
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("T"));
            codedIndex =
                (MetadataTokens.GetRowNumber(type) << 2) | 1;
        }
        else
        {
            TypeDefinitionHandle type =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("T"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(2));
            codedIndex =
                MetadataTokens.GetRowNumber(type) << 2;
        }

        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x01);
        signature.WriteByte(0x12);
        signature.WriteCompressedInteger(codedIndex);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata);
    }

    public enum ForwarderTargetKind
    {
        PlatformCoreLibrary,
        NonCoreLibrary,
        UnsignedCoreLibrary,
        ForgedCoreLibrary,
        File,
        MissingForwarderFlag,
        NestedVisibility,
    }

    static byte[] BuildCurrentToCoreLibraryForwarderImage(
        bool useTypeDefinition,
        bool includeForwarder,
        int methodCount = 1,
        int noiseForwarderCount = 0,
        bool nested = false,
        string forwardedRootName = "T",
        bool includeMalformedForwarder = false,
        bool malformedForwarderMatchesRoot = false,
        bool includeConflictingForwarder = false,
        bool includeNestedForwarderRow = false,
        ForwarderTargetKind forwarderTargetKind =
            ForwarderTargetKind.PlatformCoreLibrary,
        int noisePublicKeyBytes = 0,
        bool noisePublicKeyIsToken = false,
        int noiseAssemblyNameLength = 0,
        bool invalidNoiseCultureHandle = false,
        bool distinctNoiseAssemblyReferences = false,
        bool includeInvalidImplementationTagForwarder = false,
        bool invalidImplementationTagMatchesRoot = false,
        bool malformedForwarderHasForwarderFlag = true,
        bool malformedForwarderUsesFile = false,
        string typeNamespace = "N")
    {
        var metadata =
            CreateSingleTypeMetadata("CurrentToCoreLibrary");
        AssemblyReferenceHandle coreLibrary =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Private.CoreLib"),
                new Version(1, 0, 0, 0),
                default,
                metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0x7c, 0xec, 0x85, 0xd7,
                        0xbe, 0xa7, 0x79, 0x8e,
                    }),
                default,
                default);
        EntityHandle forwarderTarget = coreLibrary;
        TypeAttributes forwarderAttributes =
            TypeAttributes.Public | (TypeAttributes)0x00200000;
        switch (forwarderTargetKind)
        {
            case ForwarderTargetKind.PlatformCoreLibrary:
                break;
            case ForwarderTargetKind.NonCoreLibrary:
                forwarderTarget =
                    metadata.AddAssemblyReference(
                        metadata.GetOrAddString("Some.Other"),
                        new Version(1, 0, 0, 0),
                        default,
                        metadata.GetOrAddBlob(
                            new byte[]
                            {
                                0xb0, 0x3f, 0x5f, 0x7f,
                                0x11, 0xd5, 0x0a, 0x3a,
                            }),
                        default,
                        default);
                break;
            case ForwarderTargetKind.UnsignedCoreLibrary:
                forwarderTarget =
                    metadata.AddAssemblyReference(
                        metadata.GetOrAddString(
                            "System.Private.CoreLib"),
                        new Version(1, 0, 0, 0),
                        default,
                        default,
                        default,
                        default);
                break;
            case ForwarderTargetKind.ForgedCoreLibrary:
                forwarderTarget =
                    metadata.AddAssemblyReference(
                        metadata.GetOrAddString(
                            "System.Private.CoreLib"),
                        new Version(1, 0, 0, 0),
                        default,
                        metadata.GetOrAddBlob(
                            new byte[]
                            {
                                1, 2, 3, 4,
                                5, 6, 7, 8,
                            }),
                        default,
                        default);
                break;
            case ForwarderTargetKind.File:
                forwarderTarget =
                    metadata.AddAssemblyFile(
                        metadata.GetOrAddString("module.netmodule"),
                        default,
                        containsMetadata: true);
                break;
            case ForwarderTargetKind.MissingForwarderFlag:
                forwarderAttributes = TypeAttributes.Public;
                break;
            case ForwarderTargetKind.NestedVisibility:
                forwarderAttributes =
                    TypeAttributes.NestedPublic
                    | (TypeAttributes)0x00200000;
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(forwarderTargetKind));
        }
        EntityHandle signatureType;
        if (useTypeDefinition)
        {
            TypeDefinitionHandle root =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    metadata.GetOrAddString(typeNamespace),
                    metadata.GetOrAddString("T"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(2));
            signatureType = root;
            if (nested)
            {
                TypeDefinitionHandle leaf =
                    metadata.AddTypeDefinition(
                        TypeAttributes.NestedPublic,
                        default,
                        metadata.GetOrAddString("Inner"),
                        default,
                        MetadataTokens.FieldDefinitionHandle(1),
                        MetadataTokens.MethodDefinitionHandle(2));
                metadata.AddNestedType(leaf, root);
                signatureType = leaf;
            }
        }
        else
        {
            TypeReferenceHandle root =
                metadata.AddTypeReference(
                    coreLibrary,
                    metadata.GetOrAddString(typeNamespace),
                    metadata.GetOrAddString("T"));
            signatureType = root;
            if (nested)
            {
                signatureType =
                    metadata.AddTypeReference(
                        root,
                        default,
                        metadata.GetOrAddString("Inner"));
            }
        }
        if (includeForwarder)
        {
            metadata.AddExportedType(
                forwarderAttributes,
                metadata.GetOrAddString(typeNamespace),
                metadata.GetOrAddString(forwardedRootName),
                forwarderTarget,
                typeDefinitionId: 0);
        }
        if (includeNestedForwarderRow)
        {
            ExportedTypeHandle owner =
                metadata.AddExportedType(
                    TypeAttributes.NotPublic
                        | (TypeAttributes)0x00200000,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("Container"),
                    coreLibrary,
                    typeDefinitionId: 0);
            metadata.AddExportedType(
                TypeAttributes.NotPublic,
                default,
                metadata.GetOrAddString(forwardedRootName),
                owner,
                typeDefinitionId: 0);
        }
        if (includeConflictingForwarder)
        {
            AssemblyReferenceHandle conflictingTarget =
                metadata.AddAssemblyReference(
                    metadata.GetOrAddString("Some.Other"),
                    new Version(1, 0, 0, 0),
                    default,
                    metadata.GetOrAddBlob(
                        new byte[]
                        {
                            0xb0, 0x3f, 0x5f, 0x7f,
                            0x11, 0xd5, 0x0a, 0x3a,
                        }),
                    default,
                    default);
            metadata.AddExportedType(
                TypeAttributes.Public | (TypeAttributes)0x00200000,
                metadata.GetOrAddString(typeNamespace),
                metadata.GetOrAddString(forwardedRootName),
                conflictingTarget,
                typeDefinitionId: 0);
        }
        EntityHandle noiseTarget = coreLibrary;
        AssemblyReferenceHandle noiseAssemblyReference = default;
        var noiseAssemblyReferences =
            new List<AssemblyReferenceHandle>();
        StringHandle noiseAssemblyName = default;
        StringHandle noiseAssemblyCulture = default;
        BlobHandle noiseAssemblyKey = default;
        AssemblyFlags noiseAssemblyFlags = default;
        if (noisePublicKeyBytes > 0)
        {
            var key = new byte[noisePublicKeyBytes];
            key[0] = 1;
            noiseAssemblyName =
                metadata.GetOrAddString(
                    noiseAssemblyNameLength > 0
                        ? new string(
                            'h',
                            noiseAssemblyNameLength)
                        : "Hostile");
            noiseAssemblyCulture =
                invalidNoiseCultureHandle
                    ? metadata.GetOrAddString("en-US")
                    : default;
            noiseAssemblyKey =
                metadata.GetOrAddBlob(key);
            noiseAssemblyFlags =
                noisePublicKeyIsToken
                    ? default
                    : AssemblyFlags.PublicKey;
            if (!distinctNoiseAssemblyReferences)
            {
                noiseAssemblyReference =
                    AddNoiseAssemblyReference();
                noiseAssemblyReferences.Add(
                    noiseAssemblyReference);
                noiseTarget = noiseAssemblyReference;
            }
        }
        for (int i = 0; i < noiseForwarderCount; i++)
        {
            if (distinctNoiseAssemblyReferences)
            {
                noiseAssemblyReference =
                    AddNoiseAssemblyReference();
                noiseAssemblyReferences.Add(
                    noiseAssemblyReference);
                noiseTarget = noiseAssemblyReference;
            }
            metadata.AddExportedType(
                TypeAttributes.Public
                    | (TypeAttributes)0x00200000,
                metadata.GetOrAddString("Noise"),
                metadata.GetOrAddString($"T{i}"),
                noiseTarget,
                typeDefinitionId: 0);
        }
        if (includeMalformedForwarder)
        {
            EntityHandle malformedTarget =
                malformedForwarderUsesFile
                    ? MetadataTokens.AssemblyFileHandle(
                        metadata.GetRowCount(TableIndex.File) + 1)
                    : MetadataTokens.AssemblyReferenceHandle(
                        metadata.GetRowCount(TableIndex.AssemblyRef) + 1);
            metadata.AddExportedType(
                TypeAttributes.Public
                    | (malformedForwarderHasForwarderFlag
                        ? (TypeAttributes)0x00200000
                        : 0),
                metadata.GetOrAddString(
                    malformedForwarderMatchesRoot
                        ? typeNamespace
                        : "Noise"),
                metadata.GetOrAddString(
                    malformedForwarderMatchesRoot
                        ? forwardedRootName
                        : "Malformed"),
                malformedTarget,
                typeDefinitionId: 0);
        }
        ExportedTypeHandle invalidImplementationTagForwarder = default;
        if (includeInvalidImplementationTagForwarder)
        {
            invalidImplementationTagForwarder =
                metadata.AddExportedType(
                    TypeAttributes.Public
                        | (TypeAttributes)0x00200000,
                    metadata.GetOrAddString(
                        invalidImplementationTagMatchesRoot
                            ? typeNamespace
                            : "Noise"),
                    metadata.GetOrAddString(
                        invalidImplementationTagMatchesRoot
                            ? forwardedRootName
                            : "InvalidImplementation"),
                    coreLibrary,
                    typeDefinitionId: 0);
        }

        int codedIndex =
            signatureType.Kind switch
            {
                HandleKind.TypeDefinition =>
                    MetadataTokens.GetRowNumber(
                        (TypeDefinitionHandle)signatureType)
                        << 2,
                HandleKind.TypeReference =>
                    (MetadataTokens.GetRowNumber(
                        (TypeReferenceHandle)signatureType)
                        << 2) | 1,
                _ => throw new InvalidOperationException(),
            };
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x01);
        signature.WriteByte(0x12);
        signature.WriteCompressedInteger(codedIndex);
        BlobHandle signatureBlob =
            metadata.GetOrAddBlob(signature);
        StringHandle methodName =
            metadata.GetOrAddString("M");
        for (int i = 0; i < methodCount; i++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                methodName,
                signatureBlob,
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));
        }
        byte[] image = Serialize(metadata);
        if (invalidNoiseCultureHandle)
        {
            using var pe = new PEReader(new MemoryStream(image));
            MetadataReader reader = pe.GetMetadataReader();
            int stringIndexSize =
                reader.GetHeapSize(HeapIndex.String) >= 0x10000
                    ? 4
                    : 2;
            int blobIndexSize =
                reader.GetHeapSize(HeapIndex.Blob) >= 0x10000
                    ? 4
                    : 2;
            int rowSize =
                reader.GetTableRowSize(TableIndex.AssemblyRef);
            foreach (AssemblyReferenceHandle assembly
                in noiseAssemblyReferences)
            {
                int cultureOffset =
                    pe.PEHeaders.MetadataStartOffset
                    + reader.GetTableMetadataOffset(
                        TableIndex.AssemblyRef)
                    + (MetadataTokens.GetRowNumber(assembly) - 1)
                        * rowSize
                    + 12
                    + blobIndexSize
                    + stringIndexSize;
                image.AsSpan(
                        cultureOffset,
                        stringIndexSize)
                    .Fill(0xff);
                if (stringIndexSize == 4)
                    image[cultureOffset + 3] = 0x7f;
            }
        }
        if (!invalidImplementationTagForwarder.IsNil)
        {
            using var pe = new PEReader(new MemoryStream(image));
            MetadataReader reader = pe.GetMetadataReader();
            int stringIndexSize =
                reader.GetHeapSize(HeapIndex.String) >= 0x10000
                    ? 4
                    : 2;
            int rowSize =
                reader.GetTableRowSize(TableIndex.ExportedType);
            int implementationOffset =
                pe.PEHeaders.MetadataStartOffset
                + reader.GetTableMetadataOffset(TableIndex.ExportedType)
                + (MetadataTokens.GetRowNumber(
                        invalidImplementationTagForwarder) - 1)
                    * rowSize
                + 8
                + (2 * stringIndexSize);
            if ((image[implementationOffset] & 0x03) != 1)
            {
                throw new InvalidOperationException(
                    "The fixture expected an AssemblyRef implementation.");
            }
            image[implementationOffset] |= 0x03;
        }
        return image;

        AssemblyReferenceHandle AddNoiseAssemblyReference() =>
            metadata.AddAssemblyReference(
                noiseAssemblyName,
                new Version(1, 0, 0, 0),
                noiseAssemblyCulture,
                noiseAssemblyKey,
                noiseAssemblyFlags,
                default);
    }

    static byte[] BuildForwarderBudgetAmbiguityImage(
        int noiseForwarderCount,
        string matchedNamespace)
    {
        var metadata =
            CreateSingleTypeMetadata("ForwarderBudgetAmbiguity");
        AssemblyReferenceHandle coreLibrary =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Private.CoreLib"),
                new Version(1, 0, 0, 0),
                default,
                metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0x7c, 0xec, 0x85, 0xd7,
                        0xbe, 0xa7, 0x79, 0x8e,
                    }),
                default,
                default);
        TypeDefinitionHandle definition =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString(matchedNamespace),
                metadata.GetOrAddString("T"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(3));
        TypeReferenceHandle reference =
            metadata.AddTypeReference(
                coreLibrary,
                metadata.GetOrAddString(matchedNamespace),
                metadata.GetOrAddString("T"));
        TypeAttributes forwarder =
            TypeAttributes.Public | (TypeAttributes)0x00200000;
        for (int i = 0; i < noiseForwarderCount; i++)
        {
            metadata.AddExportedType(
                forwarder,
                metadata.GetOrAddString("Noise"),
                metadata.GetOrAddString($"T{i}"),
                coreLibrary,
                typeDefinitionId: 0);
        }
        metadata.AddExportedType(
            forwarder,
            metadata.GetOrAddString(matchedNamespace),
            metadata.GetOrAddString("T"),
            coreLibrary,
            typeDefinitionId: 0);

        AddMethod(definition);
        AddMethod(reference);
        return Serialize(metadata);

        void AddMethod(EntityHandle parameterType)
        {
            int codedIndex =
                parameterType.Kind switch
                {
                    HandleKind.TypeDefinition =>
                        MetadataTokens.GetRowNumber(
                            (TypeDefinitionHandle)parameterType)
                            << 2,
                    HandleKind.TypeReference =>
                        (MetadataTokens.GetRowNumber(
                            (TypeReferenceHandle)parameterType)
                            << 2) | 1,
                    _ => throw new InvalidOperationException(),
                };
            var signature = new BlobBuilder();
            signature.WriteByte(0x00);
            signature.WriteCompressedInteger(1);
            signature.WriteByte(0x01);
            signature.WriteByte(0x12);
            signature.WriteCompressedInteger(codedIndex);
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                metadata.GetOrAddBlob(signature),
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));
        }
    }

    static byte[] BuildNestedForwarderFanoutImage(
        int leafCount)
    {
        var metadata =
            CreateSingleTypeMetadata("NestedForwarderFanout");
        AssemblyReferenceHandle coreLibrary =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Private.CoreLib"),
                new Version(1, 0, 0, 0),
                default,
                metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0x7c, 0xec, 0x85, 0xd7,
                        0xbe, 0xa7, 0x79, 0x8e,
                    }),
                default,
                default);
        TypeReferenceHandle signatureType =
            metadata.AddTypeReference(
                coreLibrary,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("T"));
        ExportedTypeHandle parent =
            metadata.AddExportedType(
                TypeAttributes.Public | (TypeAttributes)0x00200000,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("T"),
                coreLibrary,
                typeDefinitionId: 0);
        for (int i = 0;
            i < MetadataSafetyPolicy.MaxRelationshipNodes - 2;
            i++)
        {
            parent =
                metadata.AddExportedType(
                    TypeAttributes.NotPublic,
                    default,
                    metadata.GetOrAddString($"P{i}"),
                    parent,
                    typeDefinitionId: 0);
        }
        for (int i = 0; i < leafCount; i++)
        {
            metadata.AddExportedType(
                TypeAttributes.NotPublic,
                default,
                metadata.GetOrAddString($"L{i}"),
                parent,
                typeDefinitionId: 0);
        }

        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x01);
        signature.WriteByte(0x12);
        signature.WriteCompressedInteger(
            (MetadataTokens.GetRowNumber(signatureType) << 2) | 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildRepeatedGenericAssemblyScopeImage(
        int parameterCount,
        int publicKeyBytes,
        int typeNameLength,
        bool distinctAssemblyReferences = false,
        bool reuseTypeReference = false,
        bool emptyNonNilPublicKey = false,
        int assemblyNameLength = 0,
        bool genericInstantiation = true)
    {
        if (reuseTypeReference && distinctAssemblyReferences)
        {
            throw new ArgumentException(
                "A shared TypeRef cannot use distinct assembly references.");
        }
        if (emptyNonNilPublicKey && publicKeyBytes != 16)
        {
            throw new ArgumentException(
                "The empty full-key fixture requires its 16-byte marker.");
        }

        var metadata =
            CreateSingleTypeMetadata("RepeatedGenericAssemblyScope");
        AssemblyReferenceHandle sharedAssembly = default;
        if (!distinctAssemblyReferences)
            sharedAssembly = AddAssemblyReference(0);
        StringHandle typeNamespace =
            metadata.GetOrAddString("N");
        StringHandle typeName =
            metadata.GetOrAddString(
                genericInstantiation
                    ? new string('a', typeNameLength - 3) + "G`1"
                    : new string('a', typeNameLength));
        var types =
            new TypeReferenceHandle[parameterCount];
        TypeReferenceHandle sharedType = default;
        if (reuseTypeReference)
        {
            sharedType =
                metadata.AddTypeReference(
                    sharedAssembly,
                    typeNamespace,
                    typeName);
        }
        for (int i = 0; i < types.Length; i++)
        {
            AssemblyReferenceHandle assembly =
                distinctAssemblyReferences
                    ? AddAssemblyReference(i)
                    : sharedAssembly;
            types[i] =
                reuseTypeReference
                    ? sharedType
                    : metadata.AddTypeReference(
                        assembly,
                        typeNamespace,
                        typeName);
        }

        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(parameterCount);
        signature.WriteByte(0x01);
        foreach (TypeReferenceHandle type in types)
        {
            if (genericInstantiation)
            {
                signature.WriteByte(0x15);
                signature.WriteByte(0x12);
                signature.WriteCompressedInteger(
                    (MetadataTokens.GetRowNumber(type) << 2) | 1);
                signature.WriteCompressedInteger(1);
                signature.WriteByte(0x08);
            }
            else
            {
                signature.WriteByte(0x12);
                signature.WriteCompressedInteger(
                    (MetadataTokens.GetRowNumber(type) << 2) | 1);
            }
        }
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        byte[] image = Serialize(metadata);
        if (emptyNonNilPublicKey)
        {
            ReadOnlySpan<byte> encodedKey =
                [16, .. EmptyFullKeyMarker()];
            int offset = image.AsSpan().IndexOf(encodedKey);
            if (offset < 0
                || image.AsSpan(offset + 1).IndexOf(encodedKey) >= 0)
            {
                throw new InvalidOperationException(
                    "The full-key marker was not unique in the fixture.");
            }
            image[offset] = 0;
        }
        return image;

        AssemblyReferenceHandle AddAssemblyReference(int index)
        {
            byte[] publicKey =
                emptyNonNilPublicKey
                    ? EmptyFullKeyMarker()
                    : new byte[publicKeyBytes];
            if (!emptyNonNilPublicKey)
            {
                publicKey[0] = 1;
                publicKey[^1] = (byte)index;
            }
            return metadata.AddAssemblyReference(
                metadata.GetOrAddString(
                    assemblyNameLength > 0
                        ? new string('d', assemblyNameLength)
                        : $"Dependency{index}"),
                new Version(1, 0, 0, 0),
                default,
                metadata.GetOrAddBlob(publicKey),
                AssemblyFlags.PublicKey,
                default);
        }

        static byte[] EmptyFullKeyMarker() =>
        [
            0xde, 0xad, 0xbe, 0xef,
            0x17, 0x42, 0x99, 0xc3,
            0x5a, 0x61, 0x78, 0x0d,
            0x2f, 0xb4, 0x83, 0xe9,
        ];
    }

    static byte[] BuildRepeatedTypeDefinitionArityScanImage(
        int methodCount,
        int genericParameterCount,
        bool malformedGenericParameterIndex = false)
    {
        var metadata =
            CreateSingleTypeMetadata(
                "RepeatedTypeDefinitionArityScan");
        TypeDefinitionHandle parameterType = default;
        if (genericParameterCount > 0)
        {
            parameterType =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString(
                        $"G`{genericParameterCount}"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(
                        methodCount + 1));
            for (int i = 0; i < genericParameterCount; i++)
            {
                metadata.AddGenericParameter(
                    parameterType,
                    GenericParameterAttributes.None,
                    metadata.GetOrAddString($"T{i}"),
                    i);
            }
        }

        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(
            parameterType.IsNil ? 0 : 1);
        signature.WriteByte(0x01);
        if (!parameterType.IsNil)
        {
            signature.WriteByte(0x12);
            signature.WriteCompressedInteger(
                MetadataTokens.GetRowNumber(parameterType) << 2);
        }
        BlobHandle signatureBlob =
            metadata.GetOrAddBlob(signature);
        StringHandle methodName =
            metadata.GetOrAddString("M");
        for (int i = 0; i < methodCount; i++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                methodName,
                signatureBlob,
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));
        }
        byte[] image = Serialize(metadata);
        if (malformedGenericParameterIndex)
        {
            using var pe =
                new PEReader(new MemoryStream(image));
            MetadataReader reader = pe.GetMetadataReader();
            int offset =
                pe.PEHeaders.MetadataStartOffset
                + reader.GetTableMetadataOffset(
                    TableIndex.GenericParam);
            image[offset] = 1;
            image[offset + 1] = 0;
        }
        return image;
    }

    static byte[] BuildTypeDefinitionArityBoundaryImage(
        int genericParameterCount,
        bool declareArity,
        int? typeParameterIndex = null)
    {
        string typeName =
            declareArity
                ? $"C`{genericParameterCount}"
                : "C";
        MetadataBuilder metadata =
            CreateSingleTypeMetadata(
                "TypeDefinitionArityBoundary",
                typeName,
                out TypeDefinitionHandle type);
        for (int i = 0; i < genericParameterCount; i++)
        {
            metadata.AddGenericParameter(
                type,
                GenericParameterAttributes.None,
                default,
                i);
        }

        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(0);
        if (typeParameterIndex is int index)
        {
            signature.WriteByte(0x13);
            signature.WriteCompressedInteger(index);
        }
        else
        {
            signature.WriteByte(0x01);
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

    static byte[]
        BuildNestedTypeDefinitionArityBoundaryImage()
    {
        MetadataBuilder metadata =
            CreateSingleTypeMetadata(
                "NestedTypeDefinitionArityBoundary",
                "Outer`1",
                out TypeDefinitionHandle outer);
        TypeDefinitionHandle inner =
            metadata.AddTypeDefinition(
                TypeAttributes.NestedPublic,
                default,
                metadata.GetOrAddString("Inner`65535"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddNestedType(inner, outer);
        metadata.AddGenericParameter(
            outer,
            GenericParameterAttributes.None,
            default,
            0);
        for (int i = 0; i < 65_536; i++)
        {
            metadata.AddGenericParameter(
                inner,
                GenericParameterAttributes.None,
                default,
                i);
        }

        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(0);
        signature.WriteByte(0x13);
        signature.WriteCompressedInteger(65_535);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata);
    }

    static byte[]
        BuildNestedTypeDefinitionCorrespondenceImage()
    {
        MetadataBuilder metadata =
            CreateSingleTypeMetadata(
                "NestedTypeDefinitionCorrespondence",
                "Outer`1",
                out TypeDefinitionHandle outer);
        TypeDefinitionHandle inner =
            metadata.AddTypeDefinition(
                TypeAttributes.NestedPublic,
                default,
                metadata.GetOrAddString("Inner`1"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddNestedType(inner, outer);
        StringHandle outerName =
            metadata.GetOrAddString("T");
        metadata.AddGenericParameter(
            outer,
            GenericParameterAttributes.None,
            outerName,
            0);
        metadata.AddGenericParameter(
            inner,
            GenericParameterAttributes.None,
            outerName,
            0);
        metadata.AddGenericParameter(
            inner,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("U"),
            1);

        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(0);
        signature.WriteByte(0x13);
        signature.WriteCompressedInteger(1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildMethodDefinitionArityBoundaryImage(
        int genericParameterRowCount,
        int encodedGenericArity,
        int? methodParameterIndex)
    {
        MetadataBuilder metadata =
            CreateSingleTypeMetadata(
                "MethodDefinitionArityBoundary");
        var signature = new BlobBuilder();
        signature.WriteByte(
            encodedGenericArity == 0
                ? (byte)0x00
                : (byte)0x10);
        if (encodedGenericArity != 0)
        {
            signature.WriteCompressedInteger(
                encodedGenericArity);
        }
        signature.WriteCompressedInteger(0);
        if (methodParameterIndex is int index)
        {
            signature.WriteByte(0x1e);
            signature.WriteCompressedInteger(index);
        }
        else
        {
            signature.WriteByte(0x01);
        }
        MethodDefinitionHandle method =
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                metadata.GetOrAddBlob(signature),
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));
        for (int i = 0;
            i < genericParameterRowCount;
            i++)
        {
            metadata.AddGenericParameter(
                method,
                GenericParameterAttributes.None,
                default,
                i);
        }
        return Serialize(metadata);
    }

    static byte[] BuildNonmatchingMalformedGenericOverloadImage()
    {
        MetadataBuilder metadata =
            CreateSingleTypeMetadata(
                "NonmatchingMalformedGenericOverload");
        MethodDefinitionHandle malformed =
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                metadata.GetOrAddBlob(
                    new byte[] { 0x00, 0x00, 0x08 }),
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(
                new byte[] { 0x00, 0x00, 0x01 }),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        metadata.AddGenericParameter(
            malformed,
            GenericParameterAttributes.None,
            default,
            0);
        return Serialize(metadata);
    }

    static byte[]
        BuildNoncontiguousTypeDefinitionGenericParametersImage()
    {
        MetadataBuilder metadata =
            CreateSingleTypeMetadata(
                "NoncontiguousTypeDefinitionGenericParameters",
                "C",
                out TypeDefinitionHandle first);
        TypeDefinitionHandle second =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("D"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddGenericParameter(
            first,
            GenericParameterAttributes.None,
            default,
            0);
        metadata.AddGenericParameter(
            first,
            GenericParameterAttributes.None,
            default,
            1);
        metadata.AddGenericParameter(
            second,
            GenericParameterAttributes.None,
            default,
            0);

        byte[] image = Serialize(metadata);
        using var pe =
            new PEReader(new MemoryStream(image));
        MetadataReader reader = pe.GetMetadataReader();
        int rowSize =
            reader.GetTableRowSize(TableIndex.GenericParam);
        int tableOffset =
            pe.PEHeaders.MetadataStartOffset
            + reader.GetTableMetadataOffset(
                TableIndex.GenericParam);
        if (rowSize != 8)
        {
            throw new InvalidOperationException(
                "The fixture expected two-byte GenericParam indices.");
        }

        int secondOwnerOffset = tableOffset + rowSize + 4;
        image[tableOffset + rowSize] = 0;
        image[tableOffset + rowSize + 1] = 0;
        image[secondOwnerOffset] = 6;
        image[secondOwnerOffset + 1] = 0;
        int thirdOwnerOffset =
            tableOffset + (2 * rowSize) + 4;
        image[tableOffset + (2 * rowSize)] = 1;
        image[tableOffset + (2 * rowSize) + 1] = 0;
        image[thirdOwnerOffset] = 4;
        image[thirdOwnerOffset + 1] = 0;
        return image;
    }

    static byte[]
        BuildInterleavedGenericParameterOwnersImage()
    {
        MetadataBuilder metadata =
            CreateSingleTypeMetadata(
                "InterleavedGenericParameterOwners",
                "C",
                out TypeDefinitionHandle firstType);
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("D"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle lastType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("E"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        MethodDefinitionHandle method = default;
        for (int i = 0; i < 3; i++)
        {
            method =
                metadata.AddMethodDefinition(
                    MethodAttributes.Public
                        | MethodAttributes.Static,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString($"M{i}"),
                    metadata.GetOrAddBlob(
                        new byte[] { 0x00, 0x00, 0x01 }),
                    bodyOffset: 0,
                    MetadataTokens.ParameterHandle(1));
        }
        metadata.AddGenericParameter(
            firstType,
            GenericParameterAttributes.None,
            default,
            0);
        metadata.AddGenericParameter(
            method,
            GenericParameterAttributes.None,
            default,
            0);
        metadata.AddGenericParameter(
            lastType,
            GenericParameterAttributes.None,
            default,
            0);
        return Serialize(metadata);
    }

    static byte[] BuildMethodComparisonNameImage(
        int methodCount,
        string methodName)
    {
        var metadata =
            CreateSingleTypeMetadata("MethodComparisonNames");
        StringHandle name =
            metadata.GetOrAddString(methodName);
        for (int i = 0; i < methodCount; i++)
        {
            var signature = new BlobBuilder();
            signature.WriteByte(0x00);
            signature.WriteCompressedInteger(i);
            signature.WriteByte(0x01);
            for (int parameter = 0; parameter < i; parameter++)
                signature.WriteByte(0x08);
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                name,
                metadata.GetOrAddBlob(signature),
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));
        }
        return Serialize(metadata);
    }

    static byte[] BuildTypeComparisonNameImage(
        int typeCount,
        string leafName,
        int nestingDepth = 2)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("TypeComparisonNames.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("TypeComparisonNames"),
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
        StringHandle nestedName =
            metadata.GetOrAddString(leafName);
        StringHandle intermediateName =
            metadata.GetOrAddString("N");
        for (int i = 0; i < typeCount; i++)
        {
            MethodDefinitionHandle firstMethod =
                MetadataTokens.MethodDefinitionHandle(i + 1);
            TypeDefinitionHandle parent = default;
            for (int depth = 0; depth < nestingDepth; depth++)
            {
                TypeDefinitionHandle type =
                    metadata.AddTypeDefinition(
                        depth == 0
                            ? TypeAttributes.Public
                            : TypeAttributes.NestedPublic,
                        default,
                        depth == nestingDepth - 1
                            ? nestedName
                            : depth == 0
                                ? metadata.GetOrAddString($"P{i}")
                                : intermediateName,
                        default,
                        MetadataTokens.FieldDefinitionHandle(1),
                        firstMethod);
                if (!parent.IsNil)
                    metadata.AddNestedType(type, parent);
                parent = type;
            }
        }
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(0);
        signature.WriteByte(0x01);
        BlobHandle signatureBlob =
            metadata.GetOrAddBlob(signature);
        StringHandle methodName =
            metadata.GetOrAddString("M");
        for (int i = 0; i < typeCount; i++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                methodName,
                signatureBlob,
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));
        }
        return Serialize(metadata);
    }

    static byte[] BuildRepeatedSignatureRelationshipImage(
        int methodCount,
        int nestingDepth,
        bool useTypeReference)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("SignatureRelationships.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("SignatureRelationships"),
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
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Probe"),
            metadata.GetOrAddString("S"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        int signatureTypeRow;
        int signatureTypeTag;
        if (useTypeReference)
        {
            EntityHandle scope =
                MetadataTokens.EntityHandle(1);
            TypeReferenceHandle signatureType = default;
            for (int depth = 0; depth < nestingDepth; depth++)
            {
                signatureType =
                    metadata.AddTypeReference(
                        scope,
                        depth == 0
                            ? metadata.GetOrAddString("Probe")
                            : default,
                        metadata.GetOrAddString(
                            depth == nestingDepth - 1
                                ? "T"
                                : "N"));
                scope = signatureType;
            }
            signatureTypeRow =
                MetadataTokens.GetRowNumber(signatureType);
            signatureTypeTag = 1;
        }
        else
        {
            TypeDefinitionHandle parent = default;
            TypeDefinitionHandle signatureType = default;
            for (int depth = 0; depth < nestingDepth; depth++)
            {
                signatureType =
                    metadata.AddTypeDefinition(
                        depth == 0
                            ? TypeAttributes.Public
                            : TypeAttributes.NestedPublic,
                        depth == 0
                            ? metadata.GetOrAddString("Probe")
                            : default,
                        metadata.GetOrAddString(
                            depth == nestingDepth - 1
                                ? "T"
                                : "N"),
                        default,
                        MetadataTokens.FieldDefinitionHandle(1),
                        MetadataTokens.MethodDefinitionHandle(
                            methodCount + 1));
                if (!parent.IsNil)
                    metadata.AddNestedType(signatureType, parent);
                parent = signatureType;
            }
            signatureTypeRow =
                MetadataTokens.GetRowNumber(signatureType);
            signatureTypeTag = 0;
        }

        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(0);
        signature.WriteByte(0x12);
        signature.WriteCompressedInteger(
            (signatureTypeRow << 2) | signatureTypeTag);
        BlobHandle signatureBlob =
            metadata.GetOrAddBlob(signature);
        StringHandle methodName =
            metadata.GetOrAddString("M");
        for (int i = 0; i < methodCount; i++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                methodName,
                signatureBlob,
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));
        }
        return Serialize(metadata);
    }

    static byte[] BuildSignatureGenericTypeImage(
        int genericParameterRows,
        int encodedArgumentCount = 2)
    {
        var metadata =
            CreateSingleTypeMetadata(
                "SignatureGenericType",
                "C",
                out _);
        TypeDefinitionHandle genericType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("G`2"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x01);
        signature.WriteByte(0x15);
        signature.WriteByte(0x12);
        signature.WriteCompressedInteger(
            MetadataTokens.GetRowNumber(genericType) << 2);
        signature.WriteCompressedInteger(encodedArgumentCount);
        for (int i = 0; i < encodedArgumentCount; i++)
            signature.WriteByte(0x08);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        for (int i = 0; i < genericParameterRows; i++)
        {
            metadata.AddGenericParameter(
                genericType,
                GenericParameterAttributes.None,
                metadata.GetOrAddString($"T{i}"),
                index: i);
        }
        return Serialize(metadata);
    }

    static byte[] BuildModifiedReturnMethodImage(
        bool? required,
        string? modifierName)
    {
        var metadata =
            CreateSingleTypeMetadata("ModifiedReturn");
        if (required is not null)
        {
            AssemblyReferenceHandle runtime =
                metadata.AddAssemblyReference(
                    metadata.GetOrAddString("System.Runtime"),
                    new Version(1, 0, 0, 0),
                    default,
                    default,
                    default,
                    default);
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString(
                    modifierName
                    ?? throw new ArgumentNullException(
                        nameof(modifierName))));
        }

        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(0);
        if (required is bool isRequired)
        {
            signature.WriteByte(
                isRequired ? (byte)0x1f : (byte)0x20);
            signature.WriteCompressedInteger(
                (1 << 2) | 1);
        }
        signature.WriteByte(0x01);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildFunctionPointerMethodImage(
        byte nestedHeader,
        string? conventionModifier = null)
    {
        var metadata =
            CreateSingleTypeMetadata("FunctionPointer");
        if (conventionModifier is not null)
        {
            AssemblyReferenceHandle runtime =
                metadata.AddAssemblyReference(
                    metadata.GetOrAddString("System.Runtime"),
                    new Version(1, 0, 0, 0),
                    default,
                    default,
                    default,
                    default);
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString(
                    conventionModifier));
        }

        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x01);
        signature.WriteByte(0x1b);
        signature.WriteByte(nestedHeader);
        signature.WriteCompressedInteger(0);
        if (conventionModifier is not null)
        {
            signature.WriteByte(0x20);
            signature.WriteCompressedInteger(
                (1 << 2) | 1);
        }
        signature.WriteByte(0x01);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildNestedFunctionPointerBudgetImage(
        int modifierCount,
        int modifierNameLength)
    {
        var metadata =
            CreateSingleTypeMetadata(
                "NestedFunctionPointerBudget");
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
                new string('T', modifierNameLength)));

        var inner = new BlobBuilder();
        inner.WriteByte(0x00);
        inner.WriteCompressedInteger(modifierCount);
        inner.WriteByte(0x01);
        for (int i = 0; i < modifierCount; i++)
        {
            inner.WriteByte(0x20);
            inner.WriteCompressedInteger((1 << 2) | 1);
            inner.WriteByte(0x08);
        }

        var outerFunctionPointer = new BlobBuilder();
        outerFunctionPointer.WriteByte(0x00);
        outerFunctionPointer.WriteCompressedInteger(0);
        outerFunctionPointer.WriteByte(0x1b);
        outerFunctionPointer.LinkSuffix(inner);

        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x01);
        signature.WriteByte(0x1b);
        signature.LinkSuffix(outerFunctionPointer);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildExtensionAttributeImage(
        int methodCount,
        int typeAttributeCount,
        int methodAttributeCount,
        int attributeNameLength,
        bool includeTypeExtensionMarker,
        bool includeMethodExtensionMarkers)
    {
        var metadata =
            CreateSingleTypeMetadata(
                "ExtensionAttributes",
                "C",
                out TypeDefinitionHandle type,
                TypeAttributes.Public
                    | TypeAttributes.Abstract
                    | TypeAttributes.Sealed);
        AssemblyReferenceHandle assembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("Dependency"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        BlobHandle constructorSignature =
            metadata.GetOrAddBlob(
                new byte[] { 0x20, 0x00, 0x01 });
        BlobHandle value =
            metadata.GetOrAddBlob(
                new byte[] { 0x01, 0x00, 0x00, 0x00 });
        TypeReferenceHandle noiseAttribute =
            metadata.AddTypeReference(
                assembly,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString(
                    new string('A', attributeNameLength)));
        MemberReferenceHandle noiseConstructor =
            metadata.AddMemberReference(
                noiseAttribute,
                metadata.GetOrAddString(".ctor"),
                constructorSignature);
        TypeReferenceHandle extensionAttribute =
            metadata.AddTypeReference(
                assembly,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString(
                    "ExtensionAttribute"));
        MemberReferenceHandle extensionConstructor =
            metadata.AddMemberReference(
                extensionAttribute,
                metadata.GetOrAddString(".ctor"),
                constructorSignature);

        BlobHandle signature =
            metadata.GetOrAddBlob(
                new byte[] { 0x00, 0x00, 0x01 });
        var methods =
            new List<MethodDefinitionHandle>(
                methodCount);
        for (int i = 0; i < methodCount; i++)
        {
            methods.Add(
                metadata.AddMethodDefinition(
                    MethodAttributes.Public
                        | MethodAttributes.Static,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString("M"),
                    signature,
                    bodyOffset: 0,
                    MetadataTokens.ParameterHandle(1)));
        }

        for (int i = 0; i < typeAttributeCount; i++)
        {
            metadata.AddCustomAttribute(
                type,
                noiseConstructor,
                value);
        }
        if (includeTypeExtensionMarker)
        {
            metadata.AddCustomAttribute(
                type,
                extensionConstructor,
                value);
        }

        foreach (MethodDefinitionHandle method in methods)
        {
            for (int i = 0;
                i < methodAttributeCount;
                i++)
            {
                metadata.AddCustomAttribute(
                    method,
                    noiseConstructor,
                    value);
            }
            if (includeMethodExtensionMarkers)
            {
                metadata.AddCustomAttribute(
                    method,
                    extensionConstructor,
                    value);
            }
        }
        return Serialize(metadata);
    }

    static byte[] BuildParameterRowMethodImage(
        int parameterCount,
        int? parameterSequence,
        ParameterAttributes parameterAttributes =
            ParameterAttributes.None)
    {
        var metadata =
            CreateSingleTypeMetadata("ParameterRows");
        ParameterHandle firstParameter =
            parameterSequence is int sequence
                ? metadata.AddParameter(
                    parameterAttributes,
                    default,
                    sequence)
                : MetadataTokens.ParameterHandle(1);
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
            firstParameter);
        return Serialize(metadata);
    }

    static byte[] BuildRepeatedApiMemberImage(
        string typeName,
        string methodName,
        byte[] signature,
        int methodCount,
        bool distinguishMethodNames = false)
    {
        var metadata =
            CreateSingleTypeMetadata(
                "RepeatedApiMember",
                typeName);
        BlobHandle signatureBlob =
            metadata.GetOrAddBlob(signature);
        for (int i = 0; i < methodCount; i++)
        {
            StringHandle name =
                metadata.GetOrAddString(
                    distinguishMethodNames
                        ? $"{methodName}{i}"
                        : methodName);
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                name,
                signatureBlob,
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));
        }
        return Serialize(metadata);
    }

    static byte[] BuildDirectionalMethodImage(
        ParameterAttributes direction)
    {
        var metadata = CreateSingleTypeMetadata("DirectionalMethod");
        ParameterHandle parameter = metadata.AddParameter(
            direction,
            metadata.GetOrAddString("value"),
            sequenceNumber: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(
                new byte[] { 0x00, 0x01, 0x01, 0x10, 0x08 }),
            bodyOffset: 0,
            parameter);
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

    static byte[] BuildRepeatedAssemblyScopeImage(
        int parameterCount,
        int assemblyNameLength)
    {
        var metadata = CreateSingleTypeMetadata(
            "RepeatedAssemblyScope");
        AssemblyReferenceHandle assembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(
                    new string('A', assemblyNameLength)),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("T"));
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

    static byte[] BuildTypeSpecificationMethodImage(
        bool overBudgetTypeSpecification)
    {
        var metadata = CreateSingleTypeMetadata(
            "TypeSpecification");
        var typeSpecification = new BlobBuilder();
        if (overBudgetTypeSpecification)
        {
            for (int i = 0; i <= TypeSpecGuard.MaxCumulativeBytes; i++)
                typeSpecification.WriteByte(0x1d);
            typeSpecification.WriteByte(0x08);
        }
        else
        {
            typeSpecification.WriteByte(0x1c);
        }
        TypeSpecificationHandle typeSpec =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(typeSpecification));
        int typeSpecCodedIndex =
            (MetadataTokens.GetRowNumber(typeSpec) << 2) | 2;

        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x01);
        signature.WriteByte(0x1b);
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(0);
        signature.WriteByte(0x20);
        signature.WriteCompressedInteger(typeSpecCodedIndex);
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
        int genericArity,
        int methodCount = 1)
    {
        var metadata = CreateSingleTypeMetadata(
            "WideGenericModopt",
            "C`1",
            out TypeDefinitionHandle type);
        metadata.AddGenericParameter(
            type,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            index: 0);
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
            metadata.GetOrAddString($"G`{genericArity}"));

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
        BlobHandle signatureBlob = metadata.GetOrAddBlob(signature);
        StringHandle methodName = metadata.GetOrAddString("M");
        for (int i = 0; i < methodCount; i++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                methodName,
                signatureBlob,
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));
        }
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
        out TypeDefinitionHandle type,
        TypeAttributes typeAttributes =
            TypeAttributes.Public)
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
            typeAttributes,
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

    static EntityHandle ReadSingleParameterTypeHandle(
        MetadataReader reader,
        MethodDefinitionHandle methodHandle)
    {
        BlobReader signature = reader.GetBlobReader(
            reader.GetMethodDefinition(methodHandle).Signature);
        _ = signature.ReadByte();
        Assert.Equal(1, signature.ReadCompressedInteger());
        _ = signature.ReadByte();
        Assert.Equal(0x12, signature.ReadByte());
        int codedIndex = signature.ReadCompressedInteger();
        int row = codedIndex >> 2;
        return (codedIndex & 3) switch
        {
            0 => MetadataTokens.TypeDefinitionHandle(row),
            1 => MetadataTokens.TypeReferenceHandle(row),
            2 => MetadataTokens.TypeSpecificationHandle(row),
            _ => throw new BadImageFormatException(
                "The signature contains an invalid TypeDefOrRef coded index."),
        };
    }

    static string? FindPlatformReferenceAssembly(
        string fileName)
    {
        string runtimeDirectory =
            Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string? dotnetRoot = Path.GetDirectoryName(
            Path.GetDirectoryName(
                Path.GetDirectoryName(runtimeDirectory)));
        if (dotnetRoot is null)
            return null;
        string packs = Path.Combine(
            dotnetRoot,
            "packs",
            "Microsoft.NETCore.App.Ref");
        string matchingPack = Path.Combine(
            packs,
            Path.GetFileName(runtimeDirectory));
        return Directory.Exists(matchingPack)
            ? Directory
                .EnumerateFiles(
                    matchingPack,
                    fileName,
                    SearchOption.AllDirectories)
                .SingleOrDefault()
            : null;
    }

    static AssemblyReferenceIdentity FindTypeReferenceAssembly(
        MetadataReader reader,
        string @namespace,
        string name)
    {
        TypeReferenceHandle handle =
            reader.TypeReferences.Single(candidate =>
            {
                TypeReference reference =
                    reader.GetTypeReference(candidate);
                return reader.GetString(reference.Namespace)
                        == @namespace
                    && reader.GetString(reference.Name) == name;
            });
        EntityHandle scope =
            reader.GetTypeReference(handle).ResolutionScope;
        Assert.Equal(HandleKind.AssemblyReference, scope.Kind);
        return AssemblyReferenceIdentity.From(
            reader,
            (AssemblyReferenceHandle)scope);
    }

    static void AssertCoreLibraryForwarder(
        MetadataReader reader,
        string @namespace,
        string name)
    {
        Span<ExportedTypeHandle> rootToLeaf =
            stackalloc ExportedTypeHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        foreach (ExportedTypeHandle handle in reader.ExportedTypes)
        {
            Assert.True(
                MetadataRelationshipTraversal
                    .TryWalkExportedTypeImplementationChain(
                        reader,
                        handle,
                        rootToLeaf,
                        out int consumed,
                        out EntityHandle terminal,
                        out _));
            if (consumed == 0
                || terminal.Kind != HandleKind.AssemblyReference)
            {
                continue;
            }
            ExportedType root =
                reader.GetExportedType(rootToLeaf[0]);
            if (!root.IsForwarder
                || reader.GetString(root.Namespace) != @namespace
                || reader.GetString(root.Name) != name)
            {
                continue;
            }
            Assert.True(
                PlatformKeys.IsCoreLibraryFacadeReference(
                    AssemblyReferenceIdentity.From(
                        reader,
                        (AssemblyReferenceHandle)terminal)));
            return;
        }
        Assert.Fail(
            $"Expected {reader.GetString(reader.GetAssemblyDefinition().Name)} "
                + $"to forward '{@namespace}.{name}' to a core-library facade.");
    }

    static MethodCorrespondenceResult ResolveApiMember(
        byte[] sourceImage,
        byte[] targetImage)
    {
        using var sourcePe =
            new PEReader(new MemoryStream(sourceImage));
        using var targetPe =
            new PEReader(new MemoryStream(targetImage));
        MetadataReader sourceReader =
            sourcePe.GetMetadataReader();
        MethodDefinitionHandle sourceMethod =
            sourceReader.MethodDefinitions.Single();
        return MethodCorrespondenceResolver.ResolveApiMember(
            sourceReader,
            MetadataMethodAddress.Create(
                sourceReader,
                sourceMethod),
            targetPe.GetMetadataReader());
    }

    static byte[] CompileFixture(
        string assemblyName,
        string source)
    {
        string trustedPlatformAssemblies =
            (string?)AppContext.GetData(
                "TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException(
                "The trusted platform assembly list is unavailable.");
        MetadataReference[] references =
            trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Select(
                    static path =>
                        MetadataReference.CreateFromFile(path))
                .ToArray();
        CSharpCompilation compilation =
            CSharpCompilation.Create(
                assemblyName,
                [
                    CSharpSyntaxTree.ParseText(
                        source,
                        new CSharpParseOptions(
                            LanguageVersion.Preview)),
                ],
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    allowUnsafe: true));
        using var peStream = new MemoryStream();
        EmitResult emit = compilation.Emit(peStream);
        Assert.True(
            emit.Success,
            string.Join(
                Environment.NewLine,
                emit.Diagnostics));
        return peStream.ToArray();
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
