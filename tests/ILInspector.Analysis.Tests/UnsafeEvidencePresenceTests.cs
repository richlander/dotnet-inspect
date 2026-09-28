using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public class UnsafeEvidencePresenceTests
{
    [Fact]
    public void
        UnsafeEvidencePresence_RejectsSameImageCorrespondenceAboveBudget()
    {
        ImmutableArray<byte> image =
            BuildLargeSameImageCorrespondenceAssembly();

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => UnsafeEvidencePresence.HasEvidence(
                    "LargeSameImageCorrespondence.dll",
                    image));

        Assert.Contains(
            "same-image correspondence",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_RejectsAggregateTypeSpecAndMethodSpecWork()
    {
        ImmutableArray<byte> image =
            BuildLargeOperandResolutionAssembly();

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => UnsafeEvidencePresence.HasEvidence(
                    "LargeOperandResolution.dll",
                    image));

        Assert.Contains(
            "same-image correspondence",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_ChargesRepeatedTargetGenericParameterRows()
    {
        ImmutableArray<byte> image =
            BuildLargeGenericDeclarationAssembly();

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => UnsafeEvidencePresence.HasEvidence(
                    "LargeGenericDeclaration.dll",
                    image));

        Assert.Contains(
            "metadata-row budget",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_ReusesValidatedLookalikeCallerGenericRows()
    {
        ImmutableArray<byte> image =
            BuildLargeGenericCallerIdentityAssembly(
                methodCount: 4);

        Assert.False(
            UnsafeEvidencePresence.HasEvidence(
                "LargeGenericCallerIdentity.dll",
                image));
    }

    [Fact]
    public void
        UnsafeEvidencePresence_RejectsLookalikeCallerGenericRowsAboveBudget()
    {
        ImmutableArray<byte> image =
            BuildLargeGenericCallerIdentityAssembly(
                methodCount: 5);

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => UnsafeEvidencePresence.HasEvidence(
                    "LargeGenericCallerIdentity.dll",
                    image));

        Assert.Contains(
            "metadata-row budget",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_AccountsLookalikeCallerAttributeRowsWithinBudget()
    {
        ImmutableArray<byte> image =
            BuildLargeAttributeCallerIdentityAssembly(
                attributeCount: 262_000);

        Assert.False(
            UnsafeEvidencePresence.HasEvidence(
                "LargeAttributeCallerIdentity.dll",
                image));
    }

    [Fact]
    public void
        UnsafeEvidencePresence_RejectsLookalikeCallerAttributeRowsAboveBudget()
    {
        ImmutableArray<byte> image =
            BuildLargeAttributeCallerIdentityAssembly(
                attributeCount: 262_145);

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => UnsafeEvidencePresence.HasEvidence(
                    "LargeAttributeCallerIdentity.dll",
                    image));

        Assert.Contains(
            "metadata-row budget",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_AccountsLookalikeCallerTypeSpecAttributeNamesWithinBudget()
    {
        ImmutableArray<byte> image =
            BuildLargeAttributeCallerIdentityAssembly(
                attributeCount: 100,
                useTypeSpecification: true,
                attributeTypeNameLength: 1024);

        Assert.False(
            UnsafeEvidencePresence.HasEvidence(
                "LargeTypeSpecAttributeCallerIdentity.dll",
                image));
    }

    [Fact]
    public void
        UnsafeEvidencePresence_RejectsLookalikeCallerTypeSpecAttributeNamesAboveBudget()
    {
        ImmutableArray<byte> image =
            BuildLargeAttributeCallerIdentityAssembly(
                attributeCount: 5_000,
                useTypeSpecification: true,
                attributeTypeNameLength: 1024);

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => UnsafeEvidencePresence.HasEvidence(
                    "LargeTypeSpecAttributeCallerIdentity.dll",
                    image));

        Assert.Contains(
            "same-image correspondence",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_MalformedTypeSpecParentFailsVisibly()
    {
        ImmutableArray<byte> image =
            BuildMalformedTypeSpecCallAssembly();

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => UnsafeEvidencePresence.HasEvidence(
                    "MalformedTypeSpecCall.dll",
                    image));

        Assert.Contains(
            "unsupported or malformed type",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void
        UnsafeEvidencePresence_MalformedConstructedDeclaringTypeArityFailsVisibly(
            int argumentCount)
    {
        ImmutableArray<byte> image =
            BuildMalformedLocalConstructedCallAssembly(
                argumentCount);

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => UnsafeEvidencePresence.HasEvidence(
                    "MalformedConstructedCall.dll",
                    image));

        Assert.Contains(
            "Unsafe evidence presence is incomplete",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_MalformedOpenMemberSignatureFailsVisibly()
    {
        ImmutableArray<byte> image =
            BuildMalformedOpenSignatureAssembly(
                malformedMemberReference: true);

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => UnsafeEvidencePresence.HasEvidence(
                    "MalformedOpenMemberSignature.dll",
                    image));

        Assert.Contains(
            "Unsafe evidence presence is incomplete",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_MalformedTargetSignatureFailsVisibly()
    {
        ImmutableArray<byte> image =
            BuildMalformedOpenSignatureAssembly(
                malformedMemberReference: false);

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => UnsafeEvidencePresence.HasEvidence(
                    "MalformedTargetSignature.dll",
                    image));

        Assert.Contains(
            "Unsafe evidence presence is incomplete",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void
        UnsafeEvidencePresence_InvalidTargetGenericDeclarationFailsVisibly(
            int genericParameterIndex)
    {
        ImmutableArray<byte> image =
            BuildTargetGenericDeclarationAssembly(
                genericParameterIndex);

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => UnsafeEvidencePresence.HasEvidence(
                    "MismatchedTargetGenericDeclaration.dll",
                    image));

        Assert.Contains(
            "Unsafe evidence presence is incomplete",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void
        UnsafeEvidencePresence_InvalidDirectTargetGenericDeclarationFailsVisibly(
            int genericParameterIndex)
    {
        ImmutableArray<byte> image =
            BuildTargetGenericDeclarationAssembly(
                genericParameterIndex,
                directDefinition: true);

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => UnsafeEvidencePresence.HasEvidence(
                    "MismatchedDirectTargetGenericDeclaration.dll",
                    image));

        Assert.Contains(
            "Unsafe evidence presence is incomplete",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1, CallTreeStatus.External)]
    [InlineData(0, CallTreeStatus.Leaf)]
    [InlineData(1, CallTreeStatus.External)]
    public void
        SameImageCalls_MalformedTargetGenericDeclarationDoesNotBind(
            int genericParameterIndex,
            CallTreeStatus expectedStatus)
    {
        ImmutableArray<byte> image =
            BuildTargetGenericDeclarationAssembly(
                genericParameterIndex);
        string path = Path.Combine(
            Path.GetTempPath(),
            $"target-generic-declaration-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, image.AsSpan());
        try
        {
            LibraryBodyIndex index =
                LibraryBodyIndex.Open(
                    path,
                    LibraryBodyAnalysisFeatures.MethodEvidence);
            MethodIdentity caller = Assert.Single(
                index.Methods,
                method => method.Name == "Call");
            CallTreeNode child = Assert.Single(
                index.BuildCallTree(
                        caller.MetadataToken,
                        maxDepth: 2,
                        maxNodes: 10)
                    .Children);

            Assert.Empty(index.Diagnostics);
            Assert.Equal(expectedStatus, child.Status);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(-1, CallTreeStatus.External)]
    [InlineData(0, CallTreeStatus.Leaf)]
    [InlineData(1, CallTreeStatus.External)]
    public void
        SameImageCalls_MalformedDirectTargetGenericDeclarationDoesNotBind(
            int genericParameterIndex,
            CallTreeStatus expectedStatus)
    {
        ImmutableArray<byte> image =
            BuildTargetGenericDeclarationAssembly(
                genericParameterIndex,
                directDefinition: true);
        AssertSingleCallTreeStatus(
            image,
            "direct-target-generic-declaration",
            expectedStatus);
    }

    [Theory]
    [InlineData(-1, CallTreeStatus.External)]
    [InlineData(0, CallTreeStatus.Leaf)]
    [InlineData(1, CallTreeStatus.External)]
    public void
        SameImageCalls_MalformedPhysicalCallerGenericDeclarationDoesNotBind(
            int genericParameterIndex,
            CallTreeStatus expectedStatus)
    {
        ImmutableArray<byte> image =
            BuildPhysicalCallerGenericDeclarationAssembly(
                genericParameterIndex);
        AssertSingleCallTreeStatus(
            image,
            "physical-caller-generic-declaration",
            expectedStatus);
    }

    [Fact]
    public void
        SameImageCalls_GuardRejectedPhysicalCallerRetainsInvalidDeclaration()
    {
        ImmutableArray<byte> image =
            BuildGuardRejectedGenericCallerAssembly();
        string path = Path.Combine(
            Path.GetTempPath(),
            $"guard-rejected-generic-caller-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, image.AsSpan());
        try
        {
            LibraryBodyIndex index =
                LibraryBodyIndex.Open(
                    path,
                    LibraryBodyAnalysisFeatures.MethodEvidence);
            MethodIdentity caller = Assert.Single(
                index.DeclaredMethods,
                method => method.Name == "Caller");

            Assert.True(
                caller.HasInvalidGenericParameterDeclaration);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void
        UnsafeEvidencePresence_InvalidPhysicalCallerGenericDeclarationFailsVisibly(
            int genericParameterIndex)
    {
        ImmutableArray<byte> image =
            BuildPhysicalCallerGenericDeclarationAssembly(
                genericParameterIndex);

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => UnsafeEvidencePresence.HasEvidence(
                    "MismatchedPhysicalCallerGenericDeclaration.dll",
                    image));

        Assert.Contains(
            "Unsafe evidence presence is incomplete",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void
        UnsafeEvidencePresence_ValidDirectGenericDeclarationsRemainSafe(
            bool physicalCaller)
    {
        ImmutableArray<byte> image =
            physicalCaller
                ? BuildPhysicalCallerGenericDeclarationAssembly(
                    genericParameterIndex: 0)
                : BuildTargetGenericDeclarationAssembly(
                    genericParameterIndex: 0,
                    directDefinition: true);

        Assert.False(
            UnsafeEvidencePresence.HasEvidence(
                "ValidDirectGenericDeclaration.dll",
                image));
    }

    [Fact]
    public void
        UnsafeEvidencePresence_MalformedLocalTypeReferenceFailsVisibly()
    {
        ImmutableArray<byte> image =
            BuildMalformedLocalTypeReferenceAssembly();

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => UnsafeEvidencePresence.HasEvidence(
                    "MalformedLocalTypeReference.dll",
                    image));

        Assert.Contains(
            "Unsafe evidence presence is incomplete",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_AmbiguousLocalDeclaringTypeFailsVisibly()
    {
        ImmutableArray<byte> image =
            BuildAmbiguousLocalDeclaringTypeCallAssembly();

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => UnsafeEvidencePresence.HasEvidence(
                    "AmbiguousDeclaringTypeCall.dll",
                    image));

        Assert.Contains(
            "ambiguous declaring-type correspondence",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_AmbiguousLocalMethodFailsVisibly()
    {
        ImmutableArray<byte> image =
            BuildAmbiguousLocalMethodCallAssembly();

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => UnsafeEvidencePresence.HasEvidence(
                    "AmbiguousMethodCall.dll",
                    image));

        Assert.Contains(
            "ambiguous MethodDef correspondence",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_GuardRejectedPointerLocalFailsVisibly()
    {
        ImmutableArray<byte> image =
            BuildGuardRejectedUnsafeAssembly(
                GuardRejectedSignatureKind.Local);

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => UnsafeEvidencePresence.HasEvidence(
                    "GuardRejectedLocal.dll",
                    image));

        Assert.Contains(
            "Unsafe evidence presence is incomplete",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "unsafe local signature exceeds the safe decoding limits",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_EarlierIncompleteResultOverridesLaterEvidence()
    {
        ImmutableArray<byte> image =
            BuildGuardRejectedUnsafeAssembly(
                GuardRejectedSignatureKind.Local,
                appendUnsafeBody: true);

        Assert.Throws<InvalidDataException>(
            () => UnsafeEvidencePresence.HasEvidence(
                "IncompleteThenEvidence.dll",
                image));
    }

    [Fact]
    public void
        UnsafeEvidencePresence_CustomModifiedPointerLocalCountsAsEvidence()
    {
        ImmutableArray<byte> image =
            BuildCustomModifiedPointerLocalAssembly();

        Assert.True(
            UnsafeEvidencePresence.HasEvidence(
                "CustomModifiedPointerLocal.dll",
                image));
    }

    [Fact]
    public void
        TypeRef_ContainsPointer_TraversesCustomModifierPayload()
    {
        TypeRef modified = TypeRef.UnsupportedModified(
            TypeRef.CoreLib(
                "System.Runtime.CompilerServices",
                "IsVolatile"),
            TypeRef.Pointer(
                TypeRef.CoreLib("System", "Int32")),
            isRequired: false);

        Assert.True(modified.ContainsPointer());
    }

    [Fact]
    public void
        UnsafeEvidencePresence_GuardRejectedPointerMemberRefFailsVisibly()
    {
        ImmutableArray<byte> image =
            BuildGuardRejectedUnsafeAssembly(
                GuardRejectedSignatureKind.MemberReference);

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => UnsafeEvidencePresence.HasEvidence(
                    "GuardRejectedMemberRef.dll",
                    image));

        Assert.Contains(
            "Unsafe evidence presence is incomplete",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "unsafe call signature exceeds the safe decoding limits",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_GuardRejectedPointerMethodSpecFailsVisibly()
    {
        ImmutableArray<byte> image =
            BuildGuardRejectedUnsafeAssembly(
                GuardRejectedSignatureKind
                    .MethodSpecification);

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => UnsafeEvidencePresence.HasEvidence(
                    "GuardRejectedMethodSpec.dll",
                    image));

        Assert.Contains(
            "Unsafe evidence presence is incomplete",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "unsafe call signature exceeds the safe decoding limits",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_GuardRejectedPointerMethodDefCallFailsVisibly()
    {
        ImmutableArray<byte> image =
            BuildGuardRejectedMethodDefinitionAssembly(
                called: true,
                unsafeLookalikeType: false);

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => UnsafeEvidencePresence.HasEvidence(
                    "GuardRejectedMethodDefCall.dll",
                    image));

        Assert.Contains(
            "unsafe call signature exceeds the safe decoding limits",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_GuardRejectedPointerMethodDefDeclarationFailsVisibly()
    {
        ImmutableArray<byte> image =
            BuildGuardRejectedMethodDefinitionAssembly(
                called: false,
                unsafeLookalikeType: false);

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => UnsafeEvidencePresence.HasEvidence(
                    "GuardRejectedMethodDefDeclaration.dll",
                    image));

        Assert.Contains(
            "unsafe method signature exceeds the safe decoding limits",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_GuardRejectedUnsafeLookalikeMethodDefFailsVisibly()
    {
        ImmutableArray<byte> image =
            BuildGuardRejectedMethodDefinitionAssembly(
                called: true,
                unsafeLookalikeType: true);

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => UnsafeEvidencePresence.HasEvidence(
                    "GuardRejectedUnsafeLookalikeMethodDef.dll",
                    image));

        Assert.Contains(
            "unsafe call signature exceeds the safe decoding limits",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnsafeSignatureMarkerCache_RepeatedHandleScansOnce()
    {
        using MetadataReaderProvider provider =
            BuildMetadataWithBlobs(
                [[0x00, 0x01, 0x01, 0x08]],
                out ImmutableArray<BlobHandle> handles);
        int scans = 0;
        var cache = new UnsafeSignatureMarkerCache(
            provider.GetMetadataReader(),
            _ => scans++);

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(
                UnsafeSignatureMarkers.None,
                cache.GetMarkers(handles[0]));
        }

        Assert.Equal(1, scans);
    }

    [Fact]
    public void
        UnsafeSignatureMarkerCache_RejectsCumulativeWorkAboveAssemblyBudget()
    {
        int blobLength =
            MetadataSafetyPolicy.MaxStructuralSignatureWorkChars
            / 2
            + 1;
        var first = new byte[blobLength];
        var second = new byte[blobLength];
        Array.Fill(first, (byte)0x08);
        Array.Fill(second, (byte)0x08);
        second[^1] = 0x09;
        using MetadataReaderProvider provider =
            BuildMetadataWithBlobs(
                [first, second],
                out ImmutableArray<BlobHandle> handles);
        var cache = new UnsafeSignatureMarkerCache(
            provider.GetMetadataReader());

        Assert.Equal(
            UnsafeSignatureMarkers.None,
            cache.GetMarkers(handles[0]));
        BadImageFormatException exception =
            Assert.Throws<BadImageFormatException>(
                () => cache.GetMarkers(handles[1]));

        Assert.Contains(
            "exceeds the assembly budget",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_ConstructedGenericCallIsNotUnsafeByParentShape()
    {
        ImmutableArray<byte> image =
            BuildConstructedGenericCallAssembly();

        Assert.False(
            UnsafeEvidencePresence.HasEvidence(
                "ConstructedGenericCall.dll",
                image));
    }

    [Fact]
    public void
        UnsafeEvidencePresence_UserDefinedUnsafeLookalikeDoesNotCountAsEvidence()
    {
        ImmutableArray<byte> image =
            BuildUnsafeLookalikeCallAssembly();

        Assert.False(
            UnsafeEvidencePresence.HasEvidence(
                "UnsafeLookalike.dll",
                image));
        AssertNoUnsafeEvidenceInFullCensus(image);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_ExternalUnsafeLookalikeDoesNotCountAsEvidence()
    {
        ImmutableArray<byte> image =
            BuildExternalUnsafeLookalikeCallAssembly();

        Assert.False(
            UnsafeEvidencePresence.HasEvidence(
                "ExternalUnsafeLookalike.dll",
                image));
        AssertNoUnsafeEvidenceInFullCensus(image);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_GuardRejectedUnsafeLookalikeMemberRefFailsVisibly()
    {
        ImmutableArray<byte> image =
            BuildGuardRejectedUnsafeAssembly(
                GuardRejectedSignatureKind.MemberReference,
                unsafeLookalikeParent: true);

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => UnsafeEvidencePresence.HasEvidence(
                    "GuardRejectedUnsafeLookalike.dll",
                    image));

        Assert.Contains(
            "unsafe call signature exceeds the safe decoding limits",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnsafeEvidencePresence_RejectsAssemblyIlAboveBudget()
    {
        ImmutableArray<byte> image =
            BuildLargeBodyAssembly(
                unsafeFirst: false);

        InvalidDataException exception =
            Assert.Throws<InvalidDataException>(
                () => UnsafeEvidencePresence.HasEvidence(
                    "LargeSafeBody.dll",
                    image));

        Assert.Contains(
            "IL scanning exceeds the assembly budget",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        UnsafeEvidencePresence_StopsBeforeCopyingOrMaterializingLargeSuffix()
    {
        ImmutableArray<byte> image =
            BuildLargeBodyAssembly(
                unsafeFirst: true);
        long before =
            GC.GetAllocatedBytesForCurrentThread();

        Assert.True(
            UnsafeEvidencePresence.HasEvidence(
                "LargeUnsafeBody.dll",
                image));

        long allocated =
            GC.GetAllocatedBytesForCurrentThread()
            - before;
        Assert.True(
            allocated
                < UnsafePresenceWorkBudget.MaxIlBytes / 4,
            $"Presence probing allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void
        UnsafeEvidencePresence_EarlierEvidenceIsNotScheduleDependent()
    {
        ImmutableArray<byte> image =
            BuildSchedulingSensitiveAssembly();

        for (int attempt = 0; attempt < 20; attempt++)
        {
            Assert.True(
                UnsafeEvidencePresence.HasEvidence(
                    "SchedulingSensitive.dll",
                    image));
        }
    }

    static ImmutableArray<byte> BuildGuardRejectedUnsafeAssembly(
        GuardRejectedSignatureKind rejectedKind,
        bool unsafeLookalikeParent = false,
        bool appendUnsafeBody = false)
    {
        var metadata = CreateMetadata("GuardRejected");
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Sample"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        var code = new BlobBuilder();
        StandaloneSignatureHandle localSignature = default;

        if (rejectedKind
            == GuardRejectedSignatureKind.Local)
        {
            localSignature = metadata.AddStandaloneSignature(
                metadata.GetOrAddBlob(
                    GuardRejectedSignature(
                        SignatureBlobGuard.Kind
                            .LocalVariables)));
        }
        else
        {
            AssemblyReferenceHandle reference =
                metadata.AddAssemblyReference(
                    metadata.GetOrAddString("External"),
                    new Version(1, 0, 0, 0),
                    default,
                    default,
                    default,
                    default);
            TypeReferenceHandle external =
                metadata.AddTypeReference(
                    reference,
                    metadata.GetOrAddString(
                        unsafeLookalikeParent
                            ? "System.Runtime.CompilerServices"
                            : "N"),
                    metadata.GetOrAddString(
                        unsafeLookalikeParent
                            ? "Unsafe"
                            : "External"));
            BlobHandle memberSignature =
                rejectedKind
                    == GuardRejectedSignatureKind
                        .MethodSpecification
                    ? metadata.GetOrAddBlob(
                        new byte[]
                        {
                            0x10,
                            0x01,
                            0x00,
                            0x01,
                        })
                    : metadata.GetOrAddBlob(
                        GuardRejectedSignature(
                            SignatureBlobGuard.Kind.Method));
            EntityHandle member =
                metadata.AddMemberReference(
                    external,
                    metadata.GetOrAddString("Invoke"),
                    memberSignature);
            if (rejectedKind
                == GuardRejectedSignatureKind
                    .MethodSpecification)
            {
                member = metadata.AddMethodSpecification(
                    member,
                    metadata.GetOrAddBlob(
                        GuardRejectedSignature(
                            SignatureBlobGuard.Kind
                                .MethodSpecification)));
            }
            code.WriteByte((byte)ILOpCode.Call);
            code.WriteInt32(
                MetadataTokens.GetToken(member));
        }
        code.WriteByte((byte)ILOpCode.Ret);
        int bodyOffset = bodyEncoder.AddMethodBody(
            new InstructionEncoder(code),
            maxStack: 1,
            localVariablesSignature: localSignature,
            attributes: localSignature.IsNil
                ? MethodBodyAttributes.None
                : MethodBodyAttributes.InitLocals);

        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            AddVoidMethodSignature(metadata),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));
        if (appendUnsafeBody)
        {
            var unsafeCode = new BlobBuilder();
            unsafeCode.WriteByte((byte)ILOpCode.Calli);
            unsafeCode.WriteInt32(0);
            unsafeCode.WriteByte((byte)ILOpCode.Ret);
            int unsafeBodyOffset =
                bodyEncoder.AddMethodBody(
                    new InstructionEncoder(unsafeCode),
                    maxStack: 1);
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("UnsafeLater"),
                AddVoidMethodSignature(metadata),
                unsafeBodyOffset,
                MetadataTokens.ParameterHandle(1));
        }
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return ImmutableArray.Create(image.ToArray());
    }

    static ImmutableArray<byte>
        BuildCustomModifiedPointerLocalAssembly()
    {
        var metadata = CreateMetadata(
            "CustomModifiedPointerLocal");
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Sample"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        AssemblyReferenceHandle coreLibrary =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        metadata.AddTypeReference(
            coreLibrary,
            metadata.GetOrAddString(
                "System.Runtime.CompilerServices"),
            metadata.GetOrAddString("IsVolatile"));
        StandaloneSignatureHandle localSignature =
            metadata.AddStandaloneSignature(
                metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0x07,
                        0x01,
                        0x20,
                        0x05,
                        0x0F,
                        0x08,
                    }));
        var bodies = new BlobBuilder();
        var code = new BlobBuilder();
        code.WriteByte((byte)ILOpCode.Ret);
        int bodyOffset =
            new MethodBodyStreamEncoder(bodies)
                .AddMethodBody(
                    new InstructionEncoder(code),
                    maxStack: 1,
                    localVariablesSignature:
                        localSignature,
                    attributes:
                        MethodBodyAttributes.InitLocals);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            AddVoidMethodSignature(metadata),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));

        return Serialize(metadata, bodies);
    }

    static byte[] GuardRejectedSignature(
        SignatureBlobGuard.Kind kind)
    {
        var signature = new BlobBuilder();
        if (kind == SignatureBlobGuard.Kind.LocalVariables)
        {
            signature.WriteByte(0x07);
            signature.WriteByte(0x01);
        }
        else if (kind
            == SignatureBlobGuard.Kind.MethodSpecification)
        {
            signature.WriteByte(0x0A);
            signature.WriteByte(0x01);
        }
        else
        {
            signature.WriteByte(0x00);
            signature.WriteByte(0x01);
            signature.WriteByte(0x01);
        }
        for (int i = 0;
            i <= SignatureBlobGuard.DefaultMaxDepth;
            i++)
        {
            signature.WriteByte(0x0F);
        }
        signature.WriteByte(0x08);
        return signature.ToArray();
    }

    static ImmutableArray<byte>
        BuildGuardRejectedMethodDefinitionAssembly(
            bool called,
            bool unsafeLookalikeType)
    {
        var metadata = CreateMetadata(
            "GuardRejectedMethodDefinition");
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString(
                unsafeLookalikeType
                    ? "System.Runtime.CompilerServices"
                    : "N"),
            metadata.GetOrAddString(
                unsafeLookalikeType
                    ? "Unsafe"
                    : "Sample"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var bodies = new BlobBuilder();
        if (called)
        {
            var code = new BlobBuilder();
            code.WriteByte((byte)ILOpCode.Call);
            code.WriteInt32(
                MetadataTokens.GetToken(
                    MetadataTokens.MethodDefinitionHandle(2)));
            code.WriteByte((byte)ILOpCode.Ret);
            int bodyOffset =
                new MethodBodyStreamEncoder(bodies)
                    .AddMethodBody(
                        new InstructionEncoder(code),
                        maxStack: 1);
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Caller"),
                AddVoidMethodSignature(metadata),
                bodyOffset,
                MetadataTokens.ParameterHandle(1));
        }
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Target"),
            metadata.GetOrAddBlob(
                GuardRejectedSignature(
                    SignatureBlobGuard.Kind.Method)),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));

        return Serialize(metadata, bodies);
    }

    static BlobHandle AddVoidMethodSignature(
        MetadataBuilder metadata)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                parameterCount: 0,
                returnType => returnType.Void(),
                parameters => { });
        return metadata.GetOrAddBlob(signature);
    }

    static ImmutableArray<byte>
        BuildLargeSameImageCorrespondenceAssembly()
    {
        MetadataBuilder metadata = CreateMetadata(
            "LargeSameImageCorrespondence",
            out ModuleDefinitionHandle module);
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Caller"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Target"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));
        MemberReferenceHandle target =
            metadata.AddMemberReference(
                metadata.AddTypeReference(
                    module,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("Target")),
                metadata.GetOrAddString(
                    new string(
                        'M',
                        MetadataSafetyPolicy
                            .MaxTypeNameCharacters)),
                AddVoidMethodSignature(metadata));
        var bodies = new BlobBuilder();
        var code = new BlobBuilder();
        code.WriteByte((byte)ILOpCode.Call);
        code.WriteInt32(
            MetadataTokens.GetToken(target));
        code.WriteByte((byte)ILOpCode.Ret);
        int bodyOffset =
            new MethodBodyStreamEncoder(bodies)
                .AddMethodBody(
                    new InstructionEncoder(code),
                    maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Run"),
            AddVoidMethodSignature(metadata),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));

        StringHandle candidateName =
            metadata.GetOrAddString(
                new string(
                    'M',
                    MetadataSafetyPolicy
                        .MaxTypeNameCharacters));
        BlobHandle candidateSignature =
            AddIntMethodSignature(metadata);
        for (int index = 0;
            index
                <= UnsafePresenceWorkBudget
                        .MaxCorrespondenceBytes
                    / MetadataSafetyPolicy
                        .MaxTypeNameCharacters;
            index++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                candidateName,
                candidateSignature,
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));
        }

        return Serialize(metadata, bodies);
    }

    static ImmutableArray<byte>
        BuildLargeOperandResolutionAssembly()
    {
        const int typeNameLength =
            MetadataSafetyPolicy
                .MaxTypeNameCharacters
            - 16;
        MetadataBuilder metadata = CreateMetadata(
            "LargeOperandResolution");
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Caller"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        AssemblyReferenceHandle externalAssembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("External"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle externalType =
            metadata.AddTypeReference(
                externalAssembly,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString(
                    new string(
                        'T',
                        typeNameLength)));
        var typeSpecification = new BlobBuilder();
        new BlobEncoder(typeSpecification)
            .TypeSpecificationSignature()
            .GenericInstantiation(
                externalType,
                genericArgumentCount: 1,
                isValueType: false)
            .AddArgument()
            .Int32();
        TypeSpecificationHandle constructedType =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(
                    typeSpecification));
        var genericMethodSignature =
            new BlobBuilder();
        new BlobEncoder(genericMethodSignature)
            .MethodSignature(
                SignatureCallingConvention.Default,
                genericParameterCount: 1,
                isInstanceMethod: false)
            .Parameters(
                parameterCount: 0,
                returnType => returnType.Void(),
                _ => { });
        MemberReferenceHandle method =
            metadata.AddMemberReference(
                constructedType,
                metadata.GetOrAddString("Invoke"),
                metadata.GetOrAddBlob(
                    genericMethodSignature));
        MethodSpecificationHandle instantiatedMethod =
            metadata.AddMethodSpecification(
                method,
                metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0x0A,
                        0x01,
                        0x08,
                    }));

        var bodies = new BlobBuilder();
        var code = new BlobBuilder();
        int callCount =
            UnsafePresenceWorkBudget
                .MaxCorrespondenceBytes
            / typeNameLength
            + 1;
        for (int index = 0;
            index < callCount;
            index++)
        {
            code.WriteByte((byte)ILOpCode.Call);
            code.WriteInt32(
                MetadataTokens.GetToken(
                    instantiatedMethod));
        }
        code.WriteByte((byte)ILOpCode.Ret);
        int bodyOffset =
            new MethodBodyStreamEncoder(bodies)
                .AddMethodBody(
                    new InstructionEncoder(code),
                    maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Run"),
            AddVoidMethodSignature(metadata),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));

        return Serialize(metadata, bodies);
    }

    static ImmutableArray<byte>
        BuildMalformedTypeSpecCallAssembly()
    {
        MetadataBuilder metadata =
            CreateMetadata(
                "MalformedTypeSpecCall");
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Caller"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var typeSignature = new BlobBuilder();
        for (int index = 0;
            index <= SignatureBlobGuard.DefaultMaxDepth;
            index++)
        {
            typeSignature.WriteByte(0x0F);
        }
        typeSignature.WriteByte(0x08);
        TypeSpecificationHandle malformedType =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(
                    typeSignature));
        MemberReferenceHandle method =
            metadata.AddMemberReference(
                malformedType,
                metadata.GetOrAddString("Invoke"),
                AddVoidMethodSignature(metadata));
        var bodies = new BlobBuilder();
        var code = new BlobBuilder();
        code.WriteByte((byte)ILOpCode.Call);
        code.WriteInt32(
            MetadataTokens.GetToken(method));
        code.WriteByte((byte)ILOpCode.Ret);
        int bodyOffset =
            new MethodBodyStreamEncoder(bodies)
                .AddMethodBody(
                    new InstructionEncoder(code),
                    maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Run"),
            AddVoidMethodSignature(metadata),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));

        return Serialize(metadata, bodies);
    }

    static ImmutableArray<byte> BuildUnsafeLookalikeCallAssembly()
    {
        var metadata = CreateMetadata("UnsafeLookalike");
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            metadata.GetOrAddString(
                "System.Runtime.CompilerServices"),
            metadata.GetOrAddString("Unsafe"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Entry"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        var identityCode = new BlobBuilder();
        identityCode.WriteByte(
            (byte)ILOpCode.Ldc_i4_1);
        identityCode.WriteByte((byte)ILOpCode.Ret);
        int identityBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(identityCode),
            maxStack: 1);
        MethodDefinitionHandle identity =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Identity"),
                AddIntMethodSignature(metadata),
                identityBody,
                MetadataTokens.ParameterHandle(1));

        var callerCode = new BlobBuilder();
        callerCode.WriteByte((byte)ILOpCode.Call);
        callerCode.WriteInt32(
            MetadataTokens.GetToken(identity));
        callerCode.WriteByte((byte)ILOpCode.Ret);
        int callerBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(callerCode),
            maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Run"),
            AddIntMethodSignature(metadata),
            callerBody,
            MetadataTokens.ParameterHandle(1));

        return Serialize(metadata, bodies);
    }

    static ImmutableArray<byte>
        BuildExternalUnsafeLookalikeCallAssembly()
    {
        var metadata = CreateMetadata(
            "ExternalUnsafeLookalike");
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Entry"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        AssemblyReferenceHandle reference =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("UnsafeLookalike"),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
        TypeReferenceHandle unsafeType =
            metadata.AddTypeReference(
                reference,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString("Unsafe"));
        MemberReferenceHandle method =
            metadata.AddMemberReference(
                unsafeType,
                metadata.GetOrAddString("M"),
                AddVoidMethodSignature(metadata));
        var code = new BlobBuilder();
        code.WriteByte((byte)ILOpCode.Call);
        code.WriteInt32(MetadataTokens.GetToken(method));
        code.WriteByte((byte)ILOpCode.Ret);
        var bodies = new BlobBuilder();
        int bodyOffset = new MethodBodyStreamEncoder(bodies)
            .AddMethodBody(
                new InstructionEncoder(code),
                maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Run"),
            AddVoidMethodSignature(metadata),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));

        return Serialize(metadata, bodies);
    }

    static ImmutableArray<byte> BuildLargeBodyAssembly(
        bool unsafeFirst)
    {
        var metadata = CreateMetadata("LargeBody");
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("LargeBody"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        byte[] il = new byte[
            UnsafePresenceWorkBudget.MaxIlBytes + 2];
        if (unsafeFirst)
            il[0] = (byte)ILOpCode.Calli;
        il[^1] = (byte)ILOpCode.Ret;
        var code = new BlobBuilder(il.Length);
        code.WriteBytes(il);
        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        int bodyOffset = bodyEncoder.AddMethodBody(
            new InstructionEncoder(code),
            maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            AddVoidMethodSignature(metadata),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));

        return Serialize(metadata, bodies);
    }

    static ImmutableArray<byte>
        BuildSchedulingSensitiveAssembly()
    {
        var metadata = CreateMetadata(
            "SchedulingSensitive");
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Probe"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        StandaloneSignatureHandle calliSignature =
            metadata.AddStandaloneSignature(
                AddVoidMethodSignature(metadata));
        var bodies = new BlobBuilder();
        var bodyEncoder =
            new MethodBodyStreamEncoder(bodies);
        var unsafeCode = new BlobBuilder();
        unsafeCode.WriteByte((byte)ILOpCode.Calli);
        unsafeCode.WriteInt32(
            MetadataTokens.GetToken(calliSignature));
        unsafeCode.WriteByte((byte)ILOpCode.Ret);
        int unsafeBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(unsafeCode),
            maxStack: 1);

        const int switchTargets = 262_144;
        var safeCode = new BlobBuilder(
            1 + sizeof(int)
                + switchTargets * sizeof(int)
                + 1);
        safeCode.WriteByte((byte)ILOpCode.Switch);
        safeCode.WriteInt32(switchTargets);
        safeCode.WriteBytes(
            new byte[
                switchTargets * sizeof(int)]);
        safeCode.WriteByte((byte)ILOpCode.Ret);
        int safeBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(safeCode),
            maxStack: 1);

        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("UnsafeFirst"),
            AddVoidMethodSignature(metadata),
            unsafeBody,
            MetadataTokens.ParameterHandle(1));
        for (int index = 0; index < 200; index++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(
                    $"Safe{index}"),
                AddVoidMethodSignature(metadata),
                safeBody,
                MetadataTokens.ParameterHandle(1));
        }

        return Serialize(metadata, bodies);
    }

    static BlobHandle AddIntMethodSignature(
        MetadataBuilder metadata)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                parameterCount: 0,
                returnType => returnType.Type().Int32(),
                parameters => { });
        return metadata.GetOrAddBlob(signature);
    }

    static ImmutableArray<byte> BuildConstructedGenericCallAssembly()
    {
        var metadata = CreateMetadata("ConstructedGenericCall");
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Sample"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        AssemblyReferenceHandle reference =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("External"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle genericType =
            metadata.AddTypeReference(
                reference,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Container`1"));
        int genericTypeCode =
            CodedIndex.TypeDefOrRefOrSpec(
                genericType);
        var constructedType = new BlobBuilder();
        constructedType.WriteByte(0x15);
        constructedType.WriteByte(0x12);
        constructedType.WriteCompressedInteger(
            genericTypeCode);
        constructedType.WriteByte(0x01);
        constructedType.WriteByte(0x08);
        TypeSpecificationHandle parent =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(
                    constructedType));
        MemberReferenceHandle member =
            metadata.AddMemberReference(
                parent,
                metadata.GetOrAddString("Invoke"),
                AddVoidMethodSignature(metadata));

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        var code = new BlobBuilder();
        code.WriteByte((byte)ILOpCode.Call);
        code.WriteInt32(
            MetadataTokens.GetToken(member));
        code.WriteByte((byte)ILOpCode.Ret);
        int bodyOffset = bodyEncoder.AddMethodBody(
            new InstructionEncoder(code),
            maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            AddVoidMethodSignature(metadata),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return ImmutableArray.Create(image.ToArray());
    }

    static ImmutableArray<byte>
        BuildMalformedLocalConstructedCallAssembly(
            int argumentCount)
    {
        MetadataBuilder metadata =
            CreateMetadata(
                "MalformedConstructedCall");
        TypeDefinitionHandle targetType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Target`1"),
                baseType: default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Caller"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));
        metadata.AddGenericParameter(
            targetType,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            index: 0);

        var parentSignature = new BlobBuilder();
        parentSignature.WriteByte(0x15);
        parentSignature.WriteByte(0x12);
        parentSignature.WriteCompressedInteger(
            CodedIndex.TypeDefOrRefOrSpec(
                targetType));
        parentSignature.WriteCompressedInteger(
            argumentCount);
        for (int index = 0;
            index < argumentCount;
            index++)
        {
            parentSignature.WriteByte(0x08);
        }
        TypeSpecificationHandle parent =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(
                    parentSignature));
        MemberReferenceHandle member =
            metadata.AddMemberReference(
                parent,
                metadata.GetOrAddString("Invoke"),
                AddVoidMethodSignature(metadata));

        var bodies = new BlobBuilder();
        var bodyEncoder =
            new MethodBodyStreamEncoder(bodies);
        var targetCode = new BlobBuilder();
        targetCode.WriteByte((byte)ILOpCode.Ret);
        int targetBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(targetCode),
            maxStack: 0);
        var callerCode = new BlobBuilder();
        callerCode.WriteByte((byte)ILOpCode.Call);
        callerCode.WriteInt32(
            MetadataTokens.GetToken(member));
        callerCode.WriteByte((byte)ILOpCode.Ret);
        int callerBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(callerCode),
            maxStack: 0);
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Invoke"),
            AddVoidMethodSignature(metadata),
            targetBody,
            MetadataTokens.ParameterHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Call"),
            AddVoidMethodSignature(metadata),
            callerBody,
            MetadataTokens.ParameterHandle(1));

        return Serialize(metadata, bodies);
    }

    static ImmutableArray<byte>
        BuildMalformedOpenSignatureAssembly(
            bool malformedMemberReference)
    {
        MetadataBuilder metadata =
            CreateMetadata(
                "MalformedOpenSignature");
        TypeDefinitionHandle targetType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Target`1"),
                baseType: default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Caller"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));
        metadata.AddGenericParameter(
            targetType,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            index: 0);

        BlobHandle memberSignature =
            malformedMemberReference
                ? AddOpenTypeParameterMethodSignature(
                    metadata,
                    parameterIndex: 1)
                : AddOpenMethodParameterSignature(
                    metadata,
                    parameterIndex: 0);
        MemberReferenceHandle member =
            metadata.AddMemberReference(
                targetType,
                metadata.GetOrAddString("Invoke"),
                memberSignature);

        var bodies = new BlobBuilder();
        var bodyEncoder =
            new MethodBodyStreamEncoder(bodies);
        var targetCode = new BlobBuilder();
        targetCode.WriteByte((byte)ILOpCode.Ret);
        int targetBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(targetCode),
            maxStack: 0);
        var callerCode = new BlobBuilder();
        callerCode.WriteByte((byte)ILOpCode.Call);
        callerCode.WriteInt32(
            MetadataTokens.GetToken(member));
        callerCode.WriteByte((byte)ILOpCode.Ret);
        int callerBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(callerCode),
            maxStack: 0);
        MethodDefinitionHandle targetMethod =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Invoke"),
                malformedMemberReference
                    ? AddOpenTypeParameterMethodSignature(
                        metadata,
                        parameterIndex: 0)
                    : AddOpenMethodParameterSignature(
                        metadata,
                        parameterIndex: 1),
                targetBody,
                MetadataTokens.ParameterHandle(1));
        if (!malformedMemberReference)
        {
            metadata.AddGenericParameter(
                targetMethod,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("TMethod"),
                index: 0);
        }
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Call"),
            AddVoidMethodSignature(metadata),
            callerBody,
            MetadataTokens.ParameterHandle(1));

        return Serialize(metadata, bodies);
    }

    static BlobHandle
        AddOpenTypeParameterMethodSignature(
            MetadataBuilder metadata,
            int parameterIndex)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteByte(0x01);
        signature.WriteByte(0x01);
        signature.WriteByte(0x13);
        signature.WriteCompressedInteger(
            parameterIndex);
        return metadata.GetOrAddBlob(signature);
    }

    static BlobHandle
        AddOpenMethodParameterSignature(
            MetadataBuilder metadata,
            int parameterIndex)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x10);
        signature.WriteByte(0x01);
        signature.WriteByte(0x01);
        signature.WriteByte(0x01);
        signature.WriteByte(0x1E);
        signature.WriteCompressedInteger(
            parameterIndex);
        return metadata.GetOrAddBlob(signature);
    }

    static ImmutableArray<byte>
        BuildTargetGenericDeclarationAssembly(
            int genericParameterIndex,
            bool directDefinition = false)
    {
        MetadataBuilder metadata =
            CreateMetadata(
                genericParameterIndex == 0
                    ? "MatchingTargetGenericDeclaration"
                    : "MismatchedTargetGenericDeclaration");
        TypeDefinitionHandle targetType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Target"),
                baseType: default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Caller"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));
        BlobHandle genericSignature =
            AddGenericVoidMethodSignature(metadata);

        var bodies = new BlobBuilder();
        var bodyEncoder =
            new MethodBodyStreamEncoder(bodies);
        var targetCode = new BlobBuilder();
        targetCode.WriteByte((byte)ILOpCode.Ret);
        int targetBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(targetCode),
            maxStack: 0);
        MethodDefinitionHandle targetMethod =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Invoke"),
                genericSignature,
                targetBody,
                MetadataTokens.ParameterHandle(1));
        EntityHandle specificationMethod =
            directDefinition
                ? targetMethod
                : metadata.AddMemberReference(
                    targetType,
                    metadata.GetOrAddString("Invoke"),
                    genericSignature);
        MethodSpecificationHandle specification =
            metadata.AddMethodSpecification(
                specificationMethod,
                AddSingleIntMethodSpecSignature(
                    metadata));
        var callerCode = new BlobBuilder();
        callerCode.WriteByte((byte)ILOpCode.Call);
        callerCode.WriteInt32(
            MetadataTokens.GetToken(
                specification));
        callerCode.WriteByte((byte)ILOpCode.Ret);
        int callerBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(callerCode),
            maxStack: 0);
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Call"),
            AddVoidMethodSignature(metadata),
            callerBody,
            MetadataTokens.ParameterHandle(1));
        if (genericParameterIndex >= 0)
        {
            metadata.AddGenericParameter(
                targetMethod,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: genericParameterIndex);
        }

        return Serialize(metadata, bodies);
    }

    static ImmutableArray<byte>
        BuildPhysicalCallerGenericDeclarationAssembly(
            int genericParameterIndex)
    {
        MetadataBuilder metadata =
            CreateMetadata(
                genericParameterIndex == 0
                    ? "MatchingPhysicalCallerGenericDeclaration"
                    : "MismatchedPhysicalCallerGenericDeclaration");
        TypeDefinitionHandle targetType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Target`1"),
                baseType: default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Caller"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));
        var constructedTarget = new BlobBuilder();
        constructedTarget.WriteByte(0x15);
        constructedTarget.WriteByte(0x12);
        constructedTarget.WriteCompressedInteger(
            MetadataTokens.GetRowNumber(targetType) << 2);
        constructedTarget.WriteByte(0x01);
        constructedTarget.WriteByte(0x1E);
        constructedTarget.WriteByte(0x00);
        TypeSpecificationHandle targetSpecification =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(
                    constructedTarget));
        MemberReferenceHandle member =
            metadata.AddMemberReference(
                targetSpecification,
                metadata.GetOrAddString("Invoke"),
                AddVoidMethodSignature(metadata));

        var bodies = new BlobBuilder();
        var bodyEncoder =
            new MethodBodyStreamEncoder(bodies);
        var targetCode = new BlobBuilder();
        targetCode.WriteByte((byte)ILOpCode.Ret);
        int targetBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(targetCode),
            maxStack: 0);
        var callerCode = new BlobBuilder();
        callerCode.WriteByte((byte)ILOpCode.Call);
        callerCode.WriteInt32(
            MetadataTokens.GetToken(member));
        callerCode.WriteByte((byte)ILOpCode.Ret);
        int callerBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(callerCode),
            maxStack: 0);
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Invoke"),
            AddVoidMethodSignature(metadata),
            targetBody,
            MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle callerMethod =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Call"),
                AddGenericVoidMethodSignature(metadata),
                callerBody,
                MetadataTokens.ParameterHandle(1));
        metadata.AddGenericParameter(
            targetType,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            index: 0);
        if (genericParameterIndex >= 0)
        {
            metadata.AddGenericParameter(
                callerMethod,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("TMethod"),
                index: genericParameterIndex);
        }

        return Serialize(metadata, bodies);
    }

    static ImmutableArray<byte>
        BuildGuardRejectedGenericCallerAssembly()
    {
        MetadataBuilder metadata =
            CreateMetadata("GuardRejectedGenericCaller");
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Sample"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var bodies = new BlobBuilder();
        var callerCode = new BlobBuilder();
        callerCode.WriteByte((byte)ILOpCode.Call);
        callerCode.WriteInt32(
            MetadataTokens.GetToken(
                MetadataTokens.MethodDefinitionHandle(2)));
        callerCode.WriteByte((byte)ILOpCode.Ret);
        int callerBody =
            new MethodBodyStreamEncoder(bodies)
                .AddMethodBody(
                    new InstructionEncoder(callerCode),
                    maxStack: 0);
        MethodDefinitionHandle caller =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Caller"),
                metadata.GetOrAddBlob(
                    GuardRejectedGenericMethodSignature()),
                callerBody,
                MetadataTokens.ParameterHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Target"),
            AddVoidMethodSignature(metadata),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        metadata.AddGenericParameter(
            caller,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            index: 1);

        return Serialize(metadata, bodies);
    }

    static byte[] GuardRejectedGenericMethodSignature()
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x10);
        signature.WriteByte(0x01);
        signature.WriteByte(0x00);
        for (int index = 0;
            index <= SignatureBlobGuard.DefaultMaxDepth;
            index++)
        {
            signature.WriteByte(0x0F);
        }
        signature.WriteByte(0x08);
        return signature.ToArray();
    }

    static ImmutableArray<byte>
        BuildLargeGenericDeclarationAssembly()
    {
        const int GenericParameterCount = 32 * 1024;
        const int CallCount = 9;
        MetadataBuilder metadata =
            CreateMetadata("LargeGenericDeclaration");
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Caller"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle targetType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Target"),
                baseType: default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));
        MemberReferenceHandle member =
            metadata.AddMemberReference(
                targetType,
                metadata.GetOrAddString("Invoke"),
                AddVoidMethodSignature(metadata));

        var bodies = new BlobBuilder();
        var callerCode = new BlobBuilder();
        for (int index = 0; index < CallCount; index++)
        {
            callerCode.WriteByte((byte)ILOpCode.Call);
            callerCode.WriteInt32(
                MetadataTokens.GetToken(member));
        }
        callerCode.WriteByte((byte)ILOpCode.Ret);
        int callerBody =
            new MethodBodyStreamEncoder(bodies)
                .AddMethodBody(
                    new InstructionEncoder(callerCode),
                    maxStack: 0);
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Call"),
            AddVoidMethodSignature(metadata),
            callerBody,
            MetadataTokens.ParameterHandle(1));
        var genericSignature = new BlobBuilder();
        new BlobEncoder(genericSignature)
            .MethodSignature(
                SignatureCallingConvention.Default,
                genericParameterCount:
                    GenericParameterCount,
                isInstanceMethod: false)
            .Parameters(
                parameterCount: 0,
                returnType => returnType.Void(),
                _ => { });
        MethodDefinitionHandle targetMethod =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Invoke"),
                metadata.GetOrAddBlob(genericSignature),
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));
        for (int index = 0;
            index < GenericParameterCount;
            index++)
        {
            metadata.AddGenericParameter(
                targetMethod,
                GenericParameterAttributes.None,
                metadata.GetOrAddString($"T{index}"),
                index);
        }

        return Serialize(metadata, bodies);
    }

    static ImmutableArray<byte>
        BuildLargeGenericCallerIdentityAssembly(
            int methodCount)
    {
        const int GenericParameterCount = 32 * 1024;
        MetadataBuilder metadata =
            CreateMetadata("LargeGenericCallerIdentity");
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString(
                "System.Runtime.CompilerServices"),
            metadata.GetOrAddString("Unsafe"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Target"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(
                methodCount + 1));

        var genericSignature = new BlobBuilder();
        new BlobEncoder(genericSignature)
            .MethodSignature(
                SignatureCallingConvention.Default,
                genericParameterCount:
                    GenericParameterCount,
                isInstanceMethod: false)
            .Parameters(
                parameterCount: 0,
                returnType => returnType.Void(),
                _ => { });
        BlobHandle callerSignature =
            metadata.GetOrAddBlob(genericSignature);
        var bodies = new BlobBuilder();
        var callerCode = new BlobBuilder();
        callerCode.WriteByte((byte)ILOpCode.Call);
        callerCode.WriteInt32(
            MetadataTokens.GetToken(
                MetadataTokens.MethodDefinitionHandle(
                    methodCount + 1)));
        callerCode.WriteByte((byte)ILOpCode.Ret);
        int callerBody =
            new MethodBodyStreamEncoder(bodies)
                .AddMethodBody(
                    new InstructionEncoder(callerCode),
                    maxStack: 0);
        for (int methodIndex = 0;
            methodIndex < methodCount;
            methodIndex++)
        {
            MethodDefinitionHandle caller =
                metadata.AddMethodDefinition(
                    MethodAttributes.Public
                        | MethodAttributes.Static,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString(
                        $"Call{methodIndex}"),
                    callerSignature,
                    callerBody,
                    MetadataTokens.ParameterHandle(1));
            for (int parameterIndex = 0;
                parameterIndex < GenericParameterCount;
                parameterIndex++)
            {
                metadata.AddGenericParameter(
                    caller,
                    GenericParameterAttributes.None,
                    metadata.GetOrAddString("T"),
                    parameterIndex);
            }
        }
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Target"),
            AddVoidMethodSignature(metadata),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));

        return Serialize(metadata, bodies);
    }

    static ImmutableArray<byte>
        BuildLargeAttributeCallerIdentityAssembly(
            int attributeCount,
            bool useTypeSpecification = false,
            int attributeTypeNameLength = 1)
    {
        MetadataBuilder metadata =
            CreateMetadata("LargeAttributeCallerIdentity");
        TypeDefinitionHandle unsafeType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Abstract
                    | TypeAttributes.Sealed,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString("Unsafe"),
                baseType: default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Target"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));

        var bodies = new BlobBuilder();
        var callerCode = new BlobBuilder();
        callerCode.WriteByte((byte)ILOpCode.Call);
        callerCode.WriteInt32(
            MetadataTokens.GetToken(
                MetadataTokens.MethodDefinitionHandle(2)));
        callerCode.WriteByte((byte)ILOpCode.Ret);
        int callerBody =
            new MethodBodyStreamEncoder(bodies)
                .AddMethodBody(
                    new InstructionEncoder(callerCode),
                    maxStack: 0);
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Call"),
            AddVoidMethodSignature(metadata),
            callerBody,
            MetadataTokens.ParameterHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Target"),
            AddVoidMethodSignature(metadata),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));

        TypeReferenceHandle markerAttribute =
            metadata.AddTypeReference(
                resolutionScope: default,
                @namespace: default,
                metadata.GetOrAddString(
                    new string(
                        'A',
                        attributeTypeNameLength)
                    + (useTypeSpecification
                        ? "`1"
                        : "")));
        EntityHandle markerConstructorOwner =
            markerAttribute;
        if (useTypeSpecification)
        {
            var markerTypeSpecification =
                new BlobBuilder();
            new BlobEncoder(
                    markerTypeSpecification)
                .TypeSpecificationSignature()
                .GenericInstantiation(
                    markerAttribute,
                    genericArgumentCount: 1,
                    isValueType: false)
                .AddArgument()
                .Int32();
            markerConstructorOwner =
                metadata.AddTypeSpecification(
                    metadata.GetOrAddBlob(
                        markerTypeSpecification));
        }
        MemberReferenceHandle markerConstructor =
            metadata.AddMemberReference(
                markerConstructorOwner,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(
                    new byte[] { 0x20, 0x00, 0x01 }));
        BlobHandle markerValue =
            metadata.GetOrAddBlob(
                new byte[] { 0x01, 0x00, 0x00, 0x00 });
        for (int index = 0;
            index < attributeCount;
            index++)
        {
            metadata.AddCustomAttribute(
                unsafeType,
                markerConstructor,
                markerValue);
        }

        return Serialize(metadata, bodies);
    }

    static void AssertSingleCallTreeStatus(
        ImmutableArray<byte> image,
        string fileStem,
        CallTreeStatus expectedStatus)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"{fileStem}-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, image.AsSpan());
        try
        {
            LibraryBodyIndex index =
                LibraryBodyIndex.Open(
                    path,
                    LibraryBodyAnalysisFeatures.MethodEvidence);
            MethodIdentity caller = Assert.Single(
                index.Methods,
                method => method.Name == "Call");
            CallTreeNode child = Assert.Single(
                index.BuildCallTree(
                        caller.MetadataToken,
                        maxDepth: 2,
                        maxNodes: 10)
                    .Children);
            MethodIdentity target = Assert.Single(
                index.DeclaredMethods,
                method => method.Name == "Invoke");
            CallTreeNode callerTree =
                index.BuildCallerTree(
                    target.MetadataToken,
                    maxDepth: 2,
                    maxNodes: 10);

            Assert.Empty(index.Diagnostics);
            Assert.Equal(expectedStatus, child.Status);
            Assert.Equal(
                expectedStatus == CallTreeStatus.Leaf
                    ? 1
                    : 0,
                callerTree.Children.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    static ImmutableArray<byte>
        BuildMalformedLocalTypeReferenceAssembly()
    {
        MetadataBuilder metadata =
            CreateMetadata(
                "MalformedLocalTypeReference",
                out ModuleDefinitionHandle module);
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Caller"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeReferenceHandle malformedType =
            metadata.AddTypeReference(
                module,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString(""));
        MemberReferenceHandle member =
            metadata.AddMemberReference(
                malformedType,
                metadata.GetOrAddString("Invoke"),
                AddVoidMethodSignature(metadata));

        var bodies = new BlobBuilder();
        var bodyEncoder =
            new MethodBodyStreamEncoder(bodies);
        var callerCode = new BlobBuilder();
        callerCode.WriteByte((byte)ILOpCode.Call);
        callerCode.WriteInt32(
            MetadataTokens.GetToken(member));
        callerCode.WriteByte((byte)ILOpCode.Ret);
        int callerBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(callerCode),
            maxStack: 0);
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Call"),
            AddVoidMethodSignature(metadata),
            callerBody,
            MetadataTokens.ParameterHandle(1));

        return Serialize(metadata, bodies);
    }

    static BlobHandle AddGenericVoidMethodSignature(
        MetadataBuilder metadata)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x10);
        signature.WriteByte(0x01);
        signature.WriteByte(0x00);
        signature.WriteByte(0x01);
        return metadata.GetOrAddBlob(signature);
    }

    static BlobHandle AddSingleIntMethodSpecSignature(
        MetadataBuilder metadata)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x0A);
        signature.WriteByte(0x01);
        signature.WriteByte(0x08);
        return metadata.GetOrAddBlob(signature);
    }

    static ImmutableArray<byte>
        BuildAmbiguousLocalDeclaringTypeCallAssembly()
    {
        MetadataBuilder metadata =
            CreateMetadata(
                "AmbiguousDeclaringTypeCall",
                out ModuleDefinitionHandle module);
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Target"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Target"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Caller"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(3));
        MemberReferenceHandle member =
            metadata.AddMemberReference(
                metadata.AddTypeReference(
                    module,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("Target")),
                metadata.GetOrAddString("Invoke"),
                AddVoidMethodSignature(metadata));

        var bodies = new BlobBuilder();
        var bodyEncoder =
            new MethodBodyStreamEncoder(bodies);
        var targetCode = new BlobBuilder();
        targetCode.WriteByte((byte)ILOpCode.Ret);
        int targetBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(targetCode),
            maxStack: 0);
        var callerCode = new BlobBuilder();
        callerCode.WriteByte((byte)ILOpCode.Call);
        callerCode.WriteInt32(
            MetadataTokens.GetToken(member));
        callerCode.WriteByte((byte)ILOpCode.Ret);
        int callerBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(callerCode),
            maxStack: 0);
        for (int index = 0; index < 2; index++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Invoke"),
                AddVoidMethodSignature(metadata),
                targetBody,
                MetadataTokens.ParameterHandle(1));
        }
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Call"),
            AddVoidMethodSignature(metadata),
            callerBody,
            MetadataTokens.ParameterHandle(1));

        return Serialize(metadata, bodies);
    }

    static ImmutableArray<byte>
        BuildAmbiguousLocalMethodCallAssembly()
    {
        MetadataBuilder metadata =
            CreateMetadata(
                "AmbiguousMethodCall",
                out ModuleDefinitionHandle module);
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Target"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Caller"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(3));
        MemberReferenceHandle member =
            metadata.AddMemberReference(
                metadata.AddTypeReference(
                    module,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("Target")),
                metadata.GetOrAddString("Invoke"),
                AddVoidMethodSignature(metadata));

        var bodies = new BlobBuilder();
        var bodyEncoder =
            new MethodBodyStreamEncoder(bodies);
        var targetCode = new BlobBuilder();
        targetCode.WriteByte((byte)ILOpCode.Ret);
        int targetBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(targetCode),
            maxStack: 0);
        var callerCode = new BlobBuilder();
        callerCode.WriteByte((byte)ILOpCode.Call);
        callerCode.WriteInt32(
            MetadataTokens.GetToken(member));
        callerCode.WriteByte((byte)ILOpCode.Ret);
        int callerBody = bodyEncoder.AddMethodBody(
            new InstructionEncoder(callerCode),
            maxStack: 0);
        for (int index = 0; index < 2; index++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Invoke"),
                AddVoidMethodSignature(metadata),
                targetBody,
                MetadataTokens.ParameterHandle(1));
        }
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Call"),
            AddVoidMethodSignature(metadata),
            callerBody,
            MetadataTokens.ParameterHandle(1));

        return Serialize(metadata, bodies);
    }

    static MetadataBuilder CreateMetadata(
        string name)
        => CreateMetadata(
            name,
            out _);

    static MetadataBuilder CreateMetadata(
        string name,
        out ModuleDefinitionHandle module)
    {
        var metadata = new MetadataBuilder();
        module = metadata.AddModule(
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
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        return metadata;
    }

    static MetadataReaderProvider BuildMetadataWithBlobs(
        IReadOnlyList<byte[]> blobs,
        out ImmutableArray<BlobHandle> handles)
    {
        MetadataBuilder metadata =
            CreateMetadata("SignatureMarkers");
        handles = blobs
            .Select(metadata.GetOrAddBlob)
            .ToImmutableArray();
        var root = new MetadataRootBuilder(
            metadata,
            suppressValidation: true);
        var image = new BlobBuilder();
        root.Serialize(
            image,
            methodBodyStreamRva: 0,
            mappedFieldDataStreamRva: 0);
        return MetadataReaderProvider.FromMetadataImage(
            ImmutableArray.Create(image.ToArray()));
    }

    static ImmutableArray<byte> Serialize(
        MetadataBuilder metadata,
        BlobBuilder bodies)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return ImmutableArray.Create(image.ToArray());
    }

    static void AssertNoUnsafeEvidenceInFullCensus(
        ImmutableArray<byte> image)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"unsafe-lookalike-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, image.AsSpan());
        try
        {
            LibraryBodyIndex index =
                LibraryBodyIndex.Open(
                    path,
                    LibraryBodyAnalysisFeatures.MethodEvidence);
            Assert.Empty(index.UnsafeEvidence);
            Assert.Empty(index.Diagnostics);
        }
        finally
        {
            File.Delete(path);
        }
    }

    enum GuardRejectedSignatureKind
    {
        Local,
        MemberReference,
        MethodSpecification,
    }
}
