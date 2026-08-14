using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis.StructuralCloneFixtures;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Analysis.Tests;

public class StructuralCloneAnalysisTests
{
    static readonly TypeRef s_int =
        TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_string =
        TypeRef.CoreLib("System", "String");
    static readonly StructuralCloneMethodSignature s_staticIntToInt =
        new(
            Header: 0,
            GenericArity: 0,
            RequiredParameterCount: 1,
            ParameterCount: 1,
            ReturnsVoid: false);

    [Fact]
    public void Compare_CompilerProducedExactPair_UsesProductOwnedBodyComparison()
    {
        using PEReader image = OpenFixture();
        MetadataReader reader = image.GetMetadataReader();

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(
                image,
                Method(reader, nameof(StructuralCloneFixture.ExactPositiveA)),
                Method(reader, nameof(StructuralCloneFixture.ExactPositiveB)));

        Assert.Equal(
            StructuralCloneDisposition.Completed,
            comparison.Disposition);
        Assert.Equal(
            StructuralCloneRelation.Exact,
            comparison.Relation);
        Assert.NotNull(comparison.Correspondence);
        Assert.True(comparison.Receipt.WitnessFound);
        Assert.True(comparison.Receipt.LeftEdges > 0);
        Assert.Equal(
            comparison.Receipt.LeftEdges,
            comparison.Receipt.RightEdges);
        Assert.InRange(
            comparison.Receipt.RefinementRounds,
            1,
            comparison.Receipt.LeftBlocks
                + comparison.Receipt.RightBlocks
                + comparison.Receipt.LeftLocals
                + comparison.Receipt.RightLocals);
    }

    [Fact]
    public void Compare_DifferentSurfaceParameterTypes_RemainExactBodyHazard()
    {
        using PEReader image = OpenFixture();
        MetadataReader reader = image.GetMetadataReader();

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(
                image,
                Method(reader, nameof(StructuralCloneFixture.SignatureHazardByte)),
                Method(reader, nameof(StructuralCloneFixture.SignatureHazardUInt)));

        Assert.Equal(
            StructuralCloneDisposition.Completed,
            comparison.Disposition);
        Assert.Equal(
            StructuralCloneRelation.Exact,
            comparison.Relation);

        StructuralCloneComparison returnHazard =
            StructuralCloneAnalysis.Compare(
                image,
                Method(reader, nameof(StructuralCloneFixture.SignatureHazardString)),
                Method(reader, nameof(StructuralCloneFixture.SignatureHazardObject)));
        Assert.Equal(
            StructuralCloneRelation.Exact,
            returnHazard.Relation);
    }

    [Fact]
    public void Compare_ExceptionHandling_IsUnsupportedNotDifferent()
    {
        using PEReader image = OpenFixture();
        MetadataReader reader = image.GetMetadataReader();

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(
                image,
                Method(reader, nameof(StructuralCloneFixture.ExceptionHandlingA)),
                Method(reader, nameof(StructuralCloneFixture.ExceptionHandlingB)));

        Assert.Equal(
            StructuralCloneDisposition.Unsupported,
            comparison.Disposition);
        Assert.Null(comparison.Relation);
        Assert.All(
            comparison.Blockers,
            static blocker => Assert.Equal(
                StructuralCloneBlockerKind.ExceptionHandling,
                blocker.Kind));
    }

    [Fact]
    public void Compare_SameNamedLocalsFromDifferentAssemblyIdentities_AreDifferent()
    {
        using PEReader image = OpenImage(
            BuildScopedLocalTwinAssembly());

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(
                image,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        Assert.Equal(
            StructuralCloneDisposition.Completed,
            comparison.Disposition);
        Assert.Equal(
            StructuralCloneRelation.Different,
            comparison.Relation);
    }

    [Fact]
    public void Compare_MultiDimensionalArrayLocalShape_IsPreserved()
    {
        using PEReader image = OpenImage(
            BuildLocalSignaturePairAssembly(
                [0x07, 0x01, 0x14, 0x08, 0x02, 0x00, 0x00],
                [0x07, 0x01, 0x14, 0x08, 0x02, 0x02, 0x03, 0x04, 0x02, 0x00, 0x02]));

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(
                image,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        Assert.Equal(
            StructuralCloneDisposition.Completed,
            comparison.Disposition);
        Assert.Equal(
            StructuralCloneRelation.Different,
            comparison.Relation);
    }

    [Fact]
    public void Compare_NestedAndLiteralPlusNamedLocals_AreDifferent()
    {
        using PEReader image = OpenImage(
            BuildNestedPlusLocalTwinAssembly());

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(
                image,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        Assert.Equal(
            StructuralCloneDisposition.Completed,
            comparison.Disposition);
        Assert.Equal(
            StructuralCloneRelation.Different,
            comparison.Relation);
    }

    [Theory]
    [MemberData(nameof(MalformedTwinBodies))]
    public void Compare_MalformedPeBackedTwins_CannotBecomeExact(
        byte[] il,
        StructuralCloneBlockerKind expectedBlocker)
    {
        using PEReader image = OpenImage(
            BuildTwinAssembly(il));

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(
                image,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        Assert.Equal(
            StructuralCloneDisposition.Failed,
            comparison.Disposition);
        Assert.Null(comparison.Relation);
        Assert.Contains(
            comparison.Blockers,
            blocker => blocker.Kind == expectedBlocker);
    }

    public static TheoryData<byte[], StructuralCloneBlockerKind>
        MalformedTwinBodies =>
        new()
        {
            {
                [0xFE, 0x09, 0xFF, 0xFF, 0x2A],
                StructuralCloneBlockerKind.InvalidArgumentSlot
            },
            {
                [0x28, 0xFF, 0xFF, 0x00, 0x06, 0x2A],
                StructuralCloneBlockerKind.InvalidMetadataOperand
            },
            {
                [0x28, 0x02, 0x00, 0x00, 0x02, 0x2A],
                StructuralCloneBlockerKind.InvalidMetadataOperand
            },
            {
                [0x72, 0xFF, 0xFF, 0x00, 0x70, 0x26, 0x2A],
                StructuralCloneBlockerKind.InvalidMetadataOperand
            },
            {
                [0x29, 0xFF, 0xFF, 0x00, 0x11, 0x2A],
                StructuralCloneBlockerKind.InvalidMetadataOperand
            },
            {
                [0x00],
                StructuralCloneBlockerKind.TerminalFallThrough
            },
        };

    [Fact]
    public void Compare_CalliRequiresAMethodSignaturePayload()
    {
        byte[] calli =
        [
            0x16, 0xD3,
            0x29, 0x01, 0x00, 0x00, 0x11,
            0x2A,
        ];
        using PEReader invalidImage = OpenImage(
            BuildCalliTwinAssembly(
                calli,
                signature: [0x07, 0x00]));
        using PEReader truncatedImage = OpenImage(
            BuildCalliTwinAssembly(
                calli,
                signature: [0x00]));
        using PEReader validImage = OpenImage(
            BuildCalliTwinAssembly(
                calli,
                signature: [0x00, 0x00, 0x01]));
        using PEReader propertyImage = OpenImage(
            BuildCalliTwinAssembly(
                calli,
                signature: [0x08, 0x00, 0x01]));
        using PEReader trailingImage = OpenImage(
            BuildCalliTwinAssembly(
                calli,
                signature: [0x00, 0x00, 0x01, 0xFF]));
        using PEReader functionPointerImage = OpenImage(
            BuildCalliTwinAssembly(
                calli,
                signature: [0x00, 0x00, 0x1B, 0x00, 0x00, 0x01]));
        using PEReader unmanagedImage = OpenImage(
            BuildCalliTwinAssembly(
                calli,
                signature: [0x01, 0x00, 0x01]));
        using PEReader nestedPropertyImage = OpenImage(
            BuildCalliTwinAssembly(
                calli,
                signature: [0x00, 0x00, 0x1B, 0x08, 0x00, 0x01]));
        using PEReader voidParameterImage = OpenImage(
            BuildCalliTwinAssembly(
                calli,
                signature: [0x00, 0x01, 0x01, 0x01]));

        StructuralCloneComparison invalid =
            StructuralCloneAnalysis.Compare(
                invalidImage,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));
        StructuralCloneComparison valid =
            StructuralCloneAnalysis.Compare(
                validImage,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        Assert.Equal(
            StructuralCloneDisposition.Failed,
            invalid.Disposition);
        Assert.Contains(
            invalid.Blockers,
            static blocker =>
                blocker.Kind
                    == StructuralCloneBlockerKind.InvalidMetadataOperand);
        StructuralCloneComparison truncated =
            StructuralCloneAnalysis.Compare(
            truncatedImage,
            MetadataTokens.MethodDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));
        Assert.Equal(
            StructuralCloneDisposition.Failed,
            truncated.Disposition);
        Assert.Contains(
            truncated.Blockers,
            static blocker =>
            blocker.Kind
                == StructuralCloneBlockerKind.InvalidMetadataOperand);
        Assert.Equal(
            StructuralCloneRelation.Exact,
            valid.Relation);
        AssertFailedMetadataOperand(propertyImage);
        AssertFailedMetadataOperand(trailingImage);
        AssertFailedMetadataOperand(nestedPropertyImage);
        AssertFailedMetadataOperand(voidParameterImage);
        Assert.Equal(
            StructuralCloneRelation.Exact,
            StructuralCloneAnalysis.Compare(
                functionPointerImage,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2)).Relation);
        Assert.Equal(
            StructuralCloneRelation.Exact,
            StructuralCloneAnalysis.Compare(
                unmanagedImage,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2)).Relation);
    }

    [Theory]
    [MemberData(nameof(InvalidMethodSignatures))]
    public void Compare_MethodDefinitionRequiresCompleteMethodSignature(
        byte[] signature)
    {
        using PEReader image = OpenImage(
            BuildMethodSignatureTwinAssembly(
                [0x2A],
                signature));

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(
                image,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        Assert.Equal(
            StructuralCloneDisposition.Failed,
            comparison.Disposition);
        Assert.Contains(
            comparison.Blockers,
            static blocker =>
                blocker.Kind
                    == StructuralCloneBlockerKind.MetadataReadFailure);
    }

    [Theory]
    [MemberData(nameof(ValidMethodSignatures))]
    public void Compare_ValidMethodSignatureShapesRemainSupported(
        byte[] signature)
    {
        using PEReader image = OpenImage(
            BuildMethodSignatureTwinAssembly(
                [0x14, 0xD3, 0x2A],
                signature,
                addModifierTypeReference: true));

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(
                image,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        Assert.Equal(
            StructuralCloneRelation.Exact,
            comparison.Relation);
    }

    [Fact]
    public void Compare_ValidOverDepthMethodSignatureIsUnsupported()
    {
        byte[] signature =
            new byte[4 + SignatureBlobGuard.DefaultMaxDepth];
        signature[0] = 0x00;
        signature[1] = 0x00;
        signature.AsSpan(2, signature.Length - 3).Fill(0x0F);
        signature[^1] = 0x01;
        using PEReader image = OpenImage(
            BuildMethodSignatureTwinAssembly(
                [0x2A],
                signature));

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(
                image,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        Assert.Equal(
            StructuralCloneDisposition.Unsupported,
            comparison.Disposition);
        Assert.Contains(
            comparison.Blockers,
            static blocker =>
                blocker.Kind
                    == StructuralCloneBlockerKind.UnsupportedMethodSignature);
    }

    [Fact]
    public void Compare_CustomModifiedVoidPreservesVoidReturnShape()
    {
        using PEReader image = OpenImage(
            BuildMethodSignaturePairAssembly(
                [0x2A],
                [0x00, 0x00, 0x01],
                [0x00, 0x00, 0x1F, 0x05, 0x01]));

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(
                image,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        Assert.Equal(
            StructuralCloneRelation.Exact,
            comparison.Relation);
    }

    public static TheoryData<byte[]> InvalidMethodSignatures =>
        new()
        {
            { new byte[] { 0x08, 0x00, 0x01 } },
            { new byte[] { 0x01, 0x00, 0x01 } },
            { new byte[] { 0x02, 0x00, 0x01 } },
            { new byte[] { 0x03, 0x00, 0x01 } },
            { new byte[] { 0x04, 0x00, 0x01 } },
            { new byte[] { 0x09, 0x00, 0x01 } },
            { new byte[] { 0x00, 0x01, 0x01, 0x01 } },
            { new byte[] { 0x00, 0x00, 0x10, 0x01 } },
            { new byte[] { 0x00, 0x00, 0x1D, 0x01 } },
            { new byte[] { 0x05, 0x01, 0x01, 0x41, 0x08 } },
            {
                new byte[]
                {
                    0x00, 0x00, 0x1B, 0x00, 0x01, 0x01, 0x01,
                }
            },
            {
                new byte[]
                {
                    0x00, 0x00, 0x1B, 0x08, 0x00, 0x01,
                }
            },
            { new byte[] { 0x00, 0x00, 0x01, 0xFF } },
            { new byte[] { 0x00, 0x00, 0xFF } },
        };

    public static TheoryData<byte[]> ValidMethodSignatures =>
        new()
        {
            { new byte[] { 0x05, 0x00, 0x01 } },
            { new byte[] { 0x05, 0x01, 0x01, 0x08 } },
            { new byte[] { 0x00, 0x00, 0x0F, 0x01 } },
            { new byte[] { 0x00, 0x00, 0x1B, 0x00, 0x00, 0x01 } },
            { new byte[] { 0x00, 0x00, 0x1F, 0x05, 0x01 } },
        };

    [Fact]
    public void Compare_SpoofedSystemVoidRemainsAValueReturn()
    {
        using PEReader image = OpenImage(
            BuildSpoofVoidTwinAssembly());

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(
                image,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        Assert.Equal(
            StructuralCloneRelation.Exact,
            comparison.Relation);
    }

    [Fact]
    public void Compare_NonIlMethodImplementationIsUnsupported()
    {
        using PEReader image = OpenImage(
            BuildTwinAssembly(
                [0x2A],
                MethodImplAttributes.Native));

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(
                image,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        Assert.Equal(
            StructuralCloneDisposition.Unsupported,
            comparison.Disposition);
        Assert.Contains(
            comparison.Blockers,
            static blocker =>
                blocker.Kind
                    == StructuralCloneBlockerKind.UnsupportedMethodImplementation);
    }

    [Theory]
    [MemberData(nameof(BodyProhibitingMethodFlags))]
    public void Compare_BodyProhibitingMethodFlagsAreUnsupported(
        MethodAttributes attributes,
        MethodImplAttributes implementation)
    {
        using PEReader image = OpenImage(
            BuildTwinAssembly(
                [0x2A],
                implementation,
                attributes));

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(
                image,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        Assert.Equal(
            StructuralCloneDisposition.Unsupported,
            comparison.Disposition);
        Assert.Contains(
            comparison.Blockers,
            static blocker =>
                blocker.Kind
                    == StructuralCloneBlockerKind.UnsupportedMethodImplementation);
    }

    public static TheoryData<MethodAttributes, MethodImplAttributes>
        BodyProhibitingMethodFlags =>
        new()
        {
            {
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL | MethodImplAttributes.ForwardRef
            },
            {
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL | MethodImplAttributes.InternalCall
            },
            {
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.Abstract,
                MethodImplAttributes.IL
            },
            {
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.PinvokeImpl,
                MethodImplAttributes.IL
            },
        };

    [Fact]
    public void Compare_ZeroLocalHeaderFormatDoesNotChangeInitLocals()
    {
        using PEReader image = OpenImage(
            BuildHeaderTwinAssembly());

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(
                image,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        Assert.Equal(
            StructuralCloneRelation.Exact,
            comparison.Relation);
    }

    [Fact]
    public void Compare_PeLimitsReportMeasurementsAndBoundBodyDecode()
    {
        using PEReader instructionImage = OpenFixture();
        MetadataReader reader = instructionImage.GetMetadataReader();
        StructuralCloneComparison instructionLimited =
            StructuralCloneAnalysis.Compare(
                instructionImage,
                Method(reader, nameof(StructuralCloneFixture.ExactPositiveA)),
                Method(reader, nameof(StructuralCloneFixture.ExactPositiveB)),
                new StructuralCloneComparisonLimits(
                    MaximumInstructions: 1));

        using PEReader bodyImage = OpenImage(
            BuildTwinAssembly(
                [.. Enumerable.Repeat((byte)0x00, 64), 0x2A]));
        StructuralCloneComparison bodyLimited =
            StructuralCloneAnalysis.Compare(
                bodyImage,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2),
                new StructuralCloneComparisonLimits(
                    MaximumBodyBytes: 8));

        using PEReader localImage = OpenImage(
            BuildLocalCountTwinAssembly());
        StructuralCloneComparison localLimited =
            StructuralCloneAnalysis.Compare(
                localImage,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2),
                new StructuralCloneComparisonLimits(
                    MaximumLocals: 1));

        AssertLimit(
            instructionLimited,
            StructuralCloneBlockerKind.InstructionLimit);
        Assert.True(
            instructionLimited.Receipt.LeftInstructions > 1);
        Assert.True(
            instructionLimited.Receipt.RightInstructions > 1);
        Assert.True(
            instructionLimited.Receipt.LeftBlocks > 0);
        AssertLimit(
            bodyLimited,
            StructuralCloneBlockerKind.BodySizeLimit);
        Assert.Equal(65, bodyLimited.Receipt.LeftBodyBytes);
        Assert.Equal(0, bodyLimited.Receipt.LeftInstructions);
        AssertLimit(
            localLimited,
            StructuralCloneBlockerKind.LocalLimit);
        Assert.Equal(2, localLimited.Receipt.LeftLocals);
        Assert.Equal(0, localLimited.Receipt.LeftInstructions);
    }

    [Fact]
    public void Compare_InstructionLimitPrecedesMetadataOperandValidation()
    {
        byte[] calli =
        [
            0x16, 0xD3,
            0x29, 0x01, 0x00, 0x00, 0x11,
            0x2A,
        ];
        using PEReader image = OpenImage(
            BuildCalliTwinAssembly(
                calli,
                signature: [0x08, 0x00, 0x01]));

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(
                image,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2),
                new StructuralCloneComparisonLimits(
                    MaximumInstructions: 1));

        AssertLimit(
            comparison,
            StructuralCloneBlockerKind.InstructionLimit);
    }

    [Theory]
    [MemberData(nameof(InvalidLocalSignatures))]
    public void Compare_MalformedLocalSignatureFailsAndRetainsMeasuredReceiptCounts(
        byte[] signature)
    {
        using PEReader image = OpenImage(
            BuildLocalSignaturePairAssembly(
                signature,
                signature));

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(
                image,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        Assert.Equal(
            StructuralCloneDisposition.Failed,
            comparison.Disposition);
        Assert.Contains(
            comparison.Blockers,
            static blocker =>
                blocker.Kind
                    == StructuralCloneBlockerKind.MetadataReadFailure);
        Assert.Equal(5, comparison.Receipt.LeftBodyBytes);
        Assert.Equal(5, comparison.Receipt.RightBodyBytes);
        Assert.Equal(1, comparison.Receipt.LeftLocals);
        Assert.Equal(1, comparison.Receipt.RightLocals);
        Assert.Equal(0, comparison.Receipt.LeftInstructions);
        Assert.Equal(0, comparison.Receipt.RightInstructions);
    }

    [Fact]
    public void Compare_ValidOverDepthLocalSignatureIsUnsupported()
    {
        byte[] signature =
            new byte[3 + SignatureBlobGuard.DefaultMaxDepth];
        signature[0] = 0x07;
        signature[1] = 0x01;
        signature.AsSpan(2, signature.Length - 3).Fill(0x0F);
        signature[^1] = 0x08;
        using PEReader image = OpenImage(
            BuildLocalSignaturePairAssembly(
                signature,
                signature));

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(
                image,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        Assert.Equal(
            StructuralCloneDisposition.Unsupported,
            comparison.Disposition);
        Assert.Contains(
            comparison.Blockers,
            static blocker =>
                blocker.Kind
                    == StructuralCloneBlockerKind.UnsupportedLocalSignature);
    }

    [Fact]
    public void Compare_LocalClassAndValueTypeKindsRemainDistinct()
    {
        using PEReader image = OpenImage(
            BuildLocalTypeKindPairAssembly());

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(
                image,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        Assert.Equal(
            StructuralCloneRelation.Different,
            comparison.Relation);
    }

    public static TheoryData<byte[]> InvalidLocalSignatures =>
        new()
        {
            { new byte[] { 0x07, 0x01 } },
            { new byte[] { 0x07, 0x01, 0x08, 0xFF } },
            { new byte[] { 0x07, 0x01, 0x01 } },
            { new byte[] { 0x07, 0x01, 0x10, 0x01 } },
            { new byte[] { 0x07, 0x01, 0x1D, 0x01 } },
            { new byte[] { 0x07, 0x01, 0x45, 0x01 } },
        };

    [Fact]
    public void Compare_CustomModifiedVoidLocalFails()
    {
        byte[] signature = [0x07, 0x01, 0x1F, 0x05, 0x01];
        using PEReader image = OpenImage(
            BuildReferencedLocalSignaturePairAssembly(
                signature,
                signature));

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(
                image,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        Assert.Equal(
            StructuralCloneDisposition.Failed,
            comparison.Disposition);
        Assert.Contains(
            comparison.Blockers,
            static blocker =>
                blocker.Kind
                    == StructuralCloneBlockerKind.MetadataReadFailure);
    }

    [Fact]
    public void Compare_VoidPointerAndPinnedLocalsRemainSupported()
    {
        byte[] pointerSignature = [0x07, 0x01, 0x0F, 0x01];
        using PEReader pointerImage = OpenImage(
            BuildLocalSignaturePairAssembly(
                pointerSignature,
                pointerSignature));
        byte[] pinnedSignature = [0x07, 0x01, 0x45, 0x08];
        using PEReader pinnedImage = OpenImage(
            BuildLocalSignaturePairAssembly(
                pinnedSignature,
                pinnedSignature));

        Assert.Equal(
            StructuralCloneRelation.Exact,
            StructuralCloneAnalysis.Compare(
                pointerImage,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2)).Relation);
        Assert.Equal(
            StructuralCloneRelation.Exact,
            StructuralCloneAnalysis.Compare(
                pinnedImage,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2)).Relation);
    }

    [Fact]
    public void Compare_NormalizesLocalSlotsWithExplicitTypedBijection()
    {
        StructuralCloneBodyFacts left = Facts(
            token: 1,
            il: [0x02, 0x0A, 0x06, 0x2A],
            locals: [s_int, s_string]);
        StructuralCloneBodyFacts right = Facts(
            token: 2,
            il: [0x02, 0x13, 0x01, 0x11, 0x01, 0x2A],
            locals: [s_string, s_int]);

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(left, right);

        Assert.Equal(
            StructuralCloneRelation.Exact,
            comparison.Relation);
        StructuralCloneCorrespondence correspondence =
            Assert.IsType<StructuralCloneCorrespondence>(
                comparison.Correspondence);
        Assert.Equal(
            StructuralCloneCorrespondenceKind.Unique,
            correspondence.Kind);
        Assert.Equal(
            [1],
            Assert.Single(
                correspondence.Locals,
                static local => local.LeftLocal == 0).RightLocals);
    }

    [Fact]
    public void Compare_LocalTypeOrInitLocalsChange_IsDifferent()
    {
        StructuralCloneBodyFacts left = Facts(
            token: 1,
            il: [0x02, 0x0A, 0x06, 0x2A],
            locals: [s_int],
            initLocals: true);
        StructuralCloneBodyFacts differentType = Facts(
            token: 2,
            il: [0x02, 0x0A, 0x06, 0x2A],
            locals: [s_string],
            initLocals: true);
        StructuralCloneBodyFacts differentInit = Facts(
            token: 3,
            il: [0x02, 0x0A, 0x06, 0x2A],
            locals: [s_int],
            initLocals: false);

        Assert.Equal(
            StructuralCloneRelation.Different,
            StructuralCloneAnalysis.Compare(
                left,
                differentType).Relation);
        Assert.Equal(
            StructuralCloneRelation.Different,
            StructuralCloneAnalysis.Compare(
                left,
                differentInit).Relation);
    }

    [Fact]
    public void Compare_BlockReorderingRetainsExactUniqueCorrespondence()
    {
        StructuralCloneBodyFacts left = Facts(
            token: 1,
            il: [0x02, 0x2D, 0x02, 0x2B, 0x02, 0x17, 0x2A, 0x18, 0x2A]);
        StructuralCloneBodyFacts right = Facts(
            token: 2,
            il: [0x02, 0x2D, 0x04, 0x2B, 0x00, 0x18, 0x2A, 0x17, 0x2A]);

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(left, right);

        Assert.Equal(
            StructuralCloneRelation.Exact,
            comparison.Relation);
        StructuralCloneCorrespondence correspondence =
            Assert.IsType<StructuralCloneCorrespondence>(
                comparison.Correspondence);
        Assert.Equal(
            StructuralCloneCorrespondenceKind.Unique,
            correspondence.Kind);
        Assert.Contains(
            correspondence.Blocks,
            static block =>
                block.RightBlocks.Length == 1
                && block.LeftBlock != block.RightBlocks[0]);
    }

    [Fact]
    public void Compare_ChangedEdgeRoleOrSwitchOrder_IsDifferent()
    {
        StructuralCloneBodyFacts edgeLeft = Facts(
            token: 1,
            il: [0x02, 0x2D, 0x02, 0x2B, 0x02, 0x17, 0x2A, 0x18, 0x2A]);
        StructuralCloneBodyFacts edgeRight = Facts(
            token: 2,
            il: [0x02, 0x2D, 0x04, 0x2B, 0x02, 0x17, 0x2A, 0x18, 0x2A]);
        StructuralCloneBodyFacts switchLeft = Facts(
            token: 3,
            il:
            [
                0x02,
                0x45, 0x02, 0x00, 0x00, 0x00,
                0x02, 0x00, 0x00, 0x00,
                0x04, 0x00, 0x00, 0x00,
                0x16, 0x2A,
                0x17, 0x2A,
                0x18, 0x2A,
            ]);
        StructuralCloneBodyFacts switchRight = Facts(
            token: 4,
            il:
            [
                0x02,
                0x45, 0x02, 0x00, 0x00, 0x00,
                0x04, 0x00, 0x00, 0x00,
                0x02, 0x00, 0x00, 0x00,
                0x16, 0x2A,
                0x17, 0x2A,
                0x18, 0x2A,
            ]);

        Assert.Equal(
            StructuralCloneRelation.Different,
            StructuralCloneAnalysis.Compare(
                edgeLeft,
                edgeRight).Relation);
        Assert.Equal(
            StructuralCloneRelation.Different,
            StructuralCloneAnalysis.Compare(
                switchLeft,
                switchRight).Relation);
    }

    [Fact]
    public void Compare_LoopBackEdgeRetargeting_IsDifferent()
    {
        StructuralCloneBodyFacts loop = Facts(
            token: 1,
            il:
            [
                0x16, 0x0A,
                0x2B, 0x04,
                0x06, 0x17, 0x58, 0x0A,
                0x06, 0x1F, 0x0A,
                0x32, 0xF7,
                0x06, 0x2A,
            ],
            locals: [s_int]);
        StructuralCloneBodyFacts retargeted = Facts(
            token: 2,
            il:
            [
                0x16, 0x0A,
                0x2B, 0x04,
                0x06, 0x17, 0x58, 0x0A,
                0x06, 0x1F, 0x0A,
                0x32, 0xFB,
                0x06, 0x2A,
            ],
            locals: [s_int]);

        Assert.Equal(
            StructuralCloneRelation.Exact,
            StructuralCloneAnalysis.Compare(loop, loop).Relation);
        Assert.Equal(
            StructuralCloneRelation.Different,
            StructuralCloneAnalysis.Compare(
                loop,
                retargeted).Relation);
    }

    [Fact]
    public void Compare_DuplicateSwitchTargetsRemainOrderedEdges()
    {
        StructuralCloneBodyFacts duplicate = Facts(
            token: 1,
            il:
            [
                0x02,
                0x45, 0x02, 0x00, 0x00, 0x00,
                0x02, 0x00, 0x00, 0x00,
                0x02, 0x00, 0x00, 0x00,
                0x16, 0x2A,
                0x17, 0x2A,
                0x18, 0x2A,
            ]);
        StructuralCloneBodyFacts distinct = Facts(
            token: 2,
            il:
            [
                0x02,
                0x45, 0x02, 0x00, 0x00, 0x00,
                0x02, 0x00, 0x00, 0x00,
                0x04, 0x00, 0x00, 0x00,
                0x16, 0x2A,
                0x17, 0x2A,
                0x18, 0x2A,
            ]);

        Assert.Equal(
            StructuralCloneRelation.Exact,
            StructuralCloneAnalysis.Compare(
                duplicate,
                duplicate).Relation);
        Assert.Equal(
            StructuralCloneRelation.Different,
            StructuralCloneAnalysis.Compare(
                duplicate,
                distinct).Relation);
    }

    [Fact]
    public void Compare_SymmetricGraph_ReportsStableAmbiguityAndRejectsNearMiss()
    {
        StructuralCloneBodyFacts left = Facts(
            token: 1,
            il: [0x2A, 0x17, 0x2A, 0x17, 0x2A],
            signature: new(0, 0, 0, 0, ReturnsVoid: true));
        StructuralCloneBodyFacts right = Facts(
            token: 2,
            il: [0x2A, 0x17, 0x2A, 0x17, 0x2A],
            signature: new(0, 0, 0, 0, ReturnsVoid: true));
        StructuralCloneBodyFacts nearMiss = Facts(
            token: 3,
            il: [0x2A, 0x17, 0x2A, 0x18, 0x2A],
            signature: new(0, 0, 0, 0, ReturnsVoid: true));

        StructuralCloneComparison forward =
            StructuralCloneAnalysis.Compare(left, right);
        StructuralCloneComparison reverse =
            StructuralCloneAnalysis.Compare(right, left);

        Assert.Equal(
            StructuralCloneRelation.Exact,
            forward.Relation);
        Assert.Equal(
            StructuralCloneCorrespondenceKind.Ambiguous,
            forward.Correspondence?.Kind);
        Assert.Equal(forward.Disposition, reverse.Disposition);
        Assert.Equal(forward.Relation, reverse.Relation);
        Assert.Contains(
            forward.Correspondence!.Blocks,
            static block => block.RightBlocks.Length == 2);
        Assert.Equal(
            StructuralCloneRelation.Different,
            StructuralCloneAnalysis.Compare(
                left,
                nearMiss).Relation);
    }

    [Fact]
    public void Compare_LimitsRemainOrthogonalToRelation()
    {
        StructuralCloneBodyFacts multiBlock = Facts(
            token: 1,
            il: [0x02, 0x2D, 0x02, 0x2B, 0x02, 0x17, 0x2A, 0x18, 0x2A]);
        StructuralCloneBodyFacts localBody = Facts(
            token: 2,
            il: [0x02, 0x0A, 0x06, 0x2A],
            locals: [s_int, s_string]);
        StructuralCloneBodyFacts symmetric = Facts(
            token: 3,
            il: [0x2A, 0x17, 0x2A, 0x17, 0x2A],
            signature: new(0, 0, 0, 0, ReturnsVoid: true));
        StructuralCloneBodyFacts highFanout = Facts(
            token: 4,
            il: BuildDuplicateTargetSwitch(256));

        StructuralCloneComparison blockLimited =
            StructuralCloneAnalysis.Compare(
                multiBlock,
                multiBlock with
                {
                    Method = Address(4),
                },
                new StructuralCloneComparisonLimits(
                    MaximumBlocks: 1));
        StructuralCloneComparison instructionLimited =
            StructuralCloneAnalysis.Compare(
                multiBlock,
                multiBlock with
                {
                    Method = Address(5),
                },
                new StructuralCloneComparisonLimits(
                    MaximumInstructions: 1));
        StructuralCloneComparison localLimited =
            StructuralCloneAnalysis.Compare(
                localBody,
                localBody with
                {
                    Method = Address(6),
                },
                new StructuralCloneComparisonLimits(
                    MaximumLocals: 1));
        StructuralCloneComparison stepLimited =
            StructuralCloneAnalysis.Compare(
                symmetric,
                symmetric with
                {
                    Method = Address(7),
                },
                new StructuralCloneComparisonLimits(
                    MaximumVerificationSteps: 1));
        StructuralCloneComparison edgeLimited =
            StructuralCloneAnalysis.Compare(
                highFanout,
                highFanout with
                {
                    Method = Address(8),
                },
                new StructuralCloneComparisonLimits(
                    MaximumEdges: 100));

        AssertLimit(
            blockLimited,
            StructuralCloneBlockerKind.BlockLimit);
        AssertLimit(
            instructionLimited,
            StructuralCloneBlockerKind.InstructionLimit);
        AssertLimit(
            localLimited,
            StructuralCloneBlockerKind.LocalLimit);
        AssertLimit(
            stepLimited,
            StructuralCloneBlockerKind.VerificationStepLimit);
        AssertLimit(
            edgeLimited,
            StructuralCloneBlockerKind.EdgeLimit);
        Assert.Equal(257, edgeLimited.Receipt.LeftEdges);
        Assert.Equal(257, edgeLimited.Receipt.RightEdges);
    }

    [Fact]
    public void Produce_MalformedBodyOrInvalidLocalSlotFailsVisibly()
    {
        BodyProduction malformed = StructuralCloneAnalysis.Produce(
            Address(1),
            MethodInstructions.Decode([0xFE], 1, []),
            [],
            initLocals: false,
            new(0, 0, 0, 0, ReturnsVoid: true));
        BodyProduction invalidLocal = StructuralCloneAnalysis.Produce(
            Address(2),
            MethodInstructions.Decode([0x11, 0x01, 0x2A], 3, []),
            [s_int],
            initLocals: true,
            new(0, 0, 0, 0, ReturnsVoid: false));

        Assert.Equal(
            StructuralCloneDisposition.Failed,
            malformed.Disposition);
        Assert.Contains(
            malformed.Blockers,
            static blocker =>
                blocker.Kind
                    == StructuralCloneBlockerKind.IncompleteBody);
        Assert.Equal(
            StructuralCloneDisposition.Failed,
            invalidLocal.Disposition);
        Assert.Contains(
            invalidLocal.Blockers,
            static blocker =>
                blocker.Kind
                    == StructuralCloneBlockerKind.InvalidLocalSlot);
    }

    [Fact]
    public void Produce_ExplicitThisDoesNotAddAnImplicitArgumentSlot()
    {
        BodyProduction production = StructuralCloneAnalysis.Produce(
            Address(1),
            MethodInstructions.Decode([0x03, 0x2A], 2, []),
            [],
            initLocals: false,
            new(
                Header: 0x60,
                GenericArity: 0,
                RequiredParameterCount: 1,
                ParameterCount: 1,
                ReturnsVoid: false));

        Assert.Equal(
            StructuralCloneDisposition.Failed,
            production.Disposition);
        Assert.Contains(
            production.Blockers,
            static blocker =>
                blocker.Kind
                    == StructuralCloneBlockerKind.InvalidArgumentSlot);
    }

    [Fact]
    public void Produce_UnsupportedLocalShapeDoesNotBecomeExact()
    {
        BodyProduction production = StructuralCloneAnalysis.Produce(
            Address(1),
            MethodInstructions.Decode([0x2A], 1, []),
            [TypeRef.Unsupported("function pointer")],
            initLocals: true,
            new(0, 0, 0, 0, ReturnsVoid: true));

        Assert.Equal(
            StructuralCloneDisposition.Unsupported,
            production.Disposition);
        Assert.Contains(
            production.Blockers,
            static blocker =>
                blocker.Kind
                    == StructuralCloneBlockerKind.UnsupportedLocalSignature);
    }

    [Fact]
    public void Produce_ExternalControlFlowIsUnsupportedNotMalformed()
    {
        BodyProduction production = StructuralCloneAnalysis.Produce(
            Address(1),
            MethodInstructions.Decode([0x2B, 0x7F, 0x2A], 3, []),
            [],
            initLocals: false,
            new(0, 0, 0, 0, ReturnsVoid: true));

        Assert.Equal(
            StructuralCloneDisposition.Unsupported,
            production.Disposition);
        Assert.Contains(
            production.Blockers,
            static blocker =>
                blocker.Kind
                    == StructuralCloneBlockerKind.ExternalControlFlow);
    }

    [Fact]
    public void Compare_NopAndThisParameterRemainExactDiscriminators()
    {
        StructuralCloneBodyFacts plain = Facts(
            token: 1,
            il: [0x02, 0x2A]);
        StructuralCloneBodyFacts withNop = Facts(
            token: 2,
            il: [0x00, 0x02, 0x2A]);
        StructuralCloneBodyFacts instance = Facts(
            token: 3,
            il: [0x02, 0x2A],
            signature: new(
                Header: 0x20,
                GenericArity: 0,
                RequiredParameterCount: 0,
                ParameterCount: 0,
                ReturnsVoid: false));

        Assert.Equal(
            StructuralCloneRelation.Different,
            StructuralCloneAnalysis.Compare(
                plain,
                withNop).Relation);
        Assert.Equal(
            StructuralCloneRelation.Different,
            StructuralCloneAnalysis.Compare(
                plain,
                instance).Relation);
    }

    static StructuralCloneBodyFacts Facts(
        int token,
        byte[] il,
        ImmutableArray<TypeRef> locals = default,
        bool initLocals = true,
        StructuralCloneMethodSignature? signature = null)
    {
        if (locals.IsDefault)
            locals = [];
        MethodInstructions instructions =
            MethodInstructions.Decode(il, il.Length, []);
        BodyProduction production = StructuralCloneAnalysis.Produce(
            Address(token),
            instructions,
            locals,
            initLocals,
            signature ?? s_staticIntToInt);
        Assert.Equal(
            StructuralCloneDisposition.Completed,
            production.Disposition);
        return Assert.IsType<StructuralCloneBodyFacts>(
            production.Facts);
    }

    static byte[] BuildDuplicateTargetSwitch(int targetCount)
    {
        byte[] il = new byte[checked(8 + targetCount * sizeof(int))];
        il[0] = 0x16;
        il[1] = 0x45;
        BinaryPrimitives.WriteInt32LittleEndian(
            il.AsSpan(2),
            targetCount);
        il[^2] = 0x16;
        il[^1] = 0x2A;
        return il;
    }

    static MetadataMethodAddress Address(int row)
        => new(
            new Guid("11111111-2222-3333-4444-555555555555"),
            MetadataTokens.MethodDefinitionHandle(row));

    static void AssertLimit(
        StructuralCloneComparison comparison,
        StructuralCloneBlockerKind kind)
    {
        Assert.Equal(
            StructuralCloneDisposition.LimitReached,
            comparison.Disposition);
        Assert.Null(comparison.Relation);
        Assert.Contains(
            comparison.Blockers,
            blocker => blocker.Kind == kind);
    }

    static void AssertFailedMetadataOperand(PEReader image)
    {
        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(
            image,
            MetadataTokens.MethodDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));
        Assert.Equal(
            StructuralCloneDisposition.Failed,
            comparison.Disposition);
        Assert.Contains(
            comparison.Blockers,
            static blocker =>
            blocker.Kind
                == StructuralCloneBlockerKind.InvalidMetadataOperand);
    }

    static PEReader OpenFixture()
        => new(File.OpenRead(
            typeof(StructuralCloneFixture).Assembly.Location));

    static MethodDefinitionHandle Method(
        MetadataReader reader,
        string name)
    {
        MethodDefinitionHandle[] matches =
        [
            .. reader.MethodDefinitions.Where(handle =>
                reader.StringComparer.Equals(
                    reader.GetMethodDefinition(handle).Name,
                    name)),
        ];
        return Assert.Single(matches);
    }

    static PEReader OpenImage(byte[] image)
        => new(new MemoryStream(image, writable: false));

    static byte[] BuildScopedLocalTwinAssembly()
    {
        MetadataBuilder metadata = AssemblyMetadata();
        AssemblyReferenceHandle firstAssembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("SameName"),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
        AssemblyReferenceHandle secondAssembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("SameName"),
                new Version(2, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
        TypeReferenceHandle firstType = metadata.AddTypeReference(
            firstAssembly,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("T"));
        TypeReferenceHandle secondType = metadata.AddTypeReference(
            secondAssembly,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("T"));
        StandaloneSignatureHandle firstLocals =
            AddLocalSignature(metadata, firstType);
        StandaloneSignatureHandle secondLocals =
            AddLocalSignature(metadata, secondType);
        AddFixtureType(metadata);

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        byte[] il = [0x14, 0x0A, 0x06, 0x26, 0x2A];
        int firstBody = AddBody(bodyEncoder, il, firstLocals);
        int secondBody = AddBody(bodyEncoder, il, secondLocals);
        AddMethod(metadata, "Left", firstBody);
        AddMethod(metadata, "Right", secondBody);
        return Serialize(metadata, bodies);
    }

    static byte[] BuildLocalSignaturePairAssembly(
        byte[] firstSignature,
        byte[] secondSignature)
    {
        MetadataBuilder metadata = AssemblyMetadata();
        StandaloneSignatureHandle firstLocals =
            AddLocalSignature(metadata, firstSignature);
        StandaloneSignatureHandle secondLocals =
            AddLocalSignature(metadata, secondSignature);
        AddFixtureType(metadata);

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        byte[] il = [0x14, 0x0A, 0x06, 0x26, 0x2A];
        int firstBody = AddBody(bodyEncoder, il, firstLocals);
        int secondBody = AddBody(bodyEncoder, il, secondLocals);
        AddMethod(metadata, "Left", firstBody);
        AddMethod(metadata, "Right", secondBody);
        return Serialize(metadata, bodies);
    }

    static byte[] BuildLocalTypeKindPairAssembly()
    {
        MetadataBuilder metadata = AssemblyMetadata();
        AssemblyReferenceHandle assembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("External"),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
        metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("T"));
        StandaloneSignatureHandle classLocals =
            AddLocalSignature(metadata, [0x07, 0x01, 0x12, 0x05]);
        StandaloneSignatureHandle valueTypeLocals =
            AddLocalSignature(metadata, [0x07, 0x01, 0x11, 0x05]);
        AddFixtureType(metadata);

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        byte[] il = [0x14, 0x0A, 0x06, 0x26, 0x2A];
        AddMethod(
            metadata,
            "Left",
            AddBody(bodyEncoder, il, classLocals));
        AddMethod(
            metadata,
            "Right",
            AddBody(bodyEncoder, il, valueTypeLocals));
        return Serialize(metadata, bodies);
    }

    static byte[] BuildReferencedLocalSignaturePairAssembly(
        byte[] firstSignature,
        byte[] secondSignature)
    {
        MetadataBuilder metadata = AssemblyMetadata();
        AssemblyReferenceHandle assembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("External"),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
        metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("T"));
        StandaloneSignatureHandle firstLocals =
            AddLocalSignature(metadata, firstSignature);
        StandaloneSignatureHandle secondLocals =
            AddLocalSignature(metadata, secondSignature);
        AddFixtureType(metadata);

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        byte[] il = [0x14, 0x0A, 0x06, 0x26, 0x2A];
        AddMethod(
            metadata,
            "Left",
            AddBody(bodyEncoder, il, firstLocals));
        AddMethod(
            metadata,
            "Right",
            AddBody(bodyEncoder, il, secondLocals));
        return Serialize(metadata, bodies);
    }

    static byte[] BuildNestedPlusLocalTwinAssembly()
    {
        MetadataBuilder metadata = AssemblyMetadata();
        AssemblyReferenceHandle assembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("Scoped"),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
        TypeReferenceHandle outer = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Outer"));
        TypeReferenceHandle nested = metadata.AddTypeReference(
            outer,
            @namespace: default,
            metadata.GetOrAddString("Inner"));
        TypeReferenceHandle literalPlus = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Outer+Inner"));
        StandaloneSignatureHandle nestedLocals =
            AddLocalSignature(metadata, nested);
        StandaloneSignatureHandle literalPlusLocals =
            AddLocalSignature(metadata, literalPlus);
        AddFixtureType(metadata);

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        byte[] il = [0x14, 0x0A, 0x06, 0x26, 0x2A];
        AddMethod(
            metadata,
            "Left",
            AddBody(bodyEncoder, il, nestedLocals));
        AddMethod(
            metadata,
            "Right",
            AddBody(bodyEncoder, il, literalPlusLocals));
        return Serialize(metadata, bodies);
    }

    static byte[] BuildTwinAssembly(
        byte[] il,
        MethodImplAttributes implementation =
            MethodImplAttributes.IL,
        MethodAttributes attributes =
            MethodAttributes.Public | MethodAttributes.Static)
    {
        MetadataBuilder metadata = AssemblyMetadata();
        AddFixtureType(metadata);
        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        int firstBody = AddBody(
            bodyEncoder,
            il,
            localSignature: default);
        int secondBody = AddBody(
            bodyEncoder,
            il,
            localSignature: default);
        AddMethod(
            metadata,
            "Left",
            firstBody,
            implementation: implementation,
            attributes: attributes);
        AddMethod(
            metadata,
            "Right",
            secondBody,
            implementation: implementation,
            attributes: attributes);
        return Serialize(metadata, bodies);
    }

    static byte[] BuildCalliTwinAssembly(
        byte[] il,
        byte[] signature)
    {
        MetadataBuilder metadata = AssemblyMetadata();
        metadata.AddStandaloneSignature(
            metadata.GetOrAddBlob(signature));
        AddFixtureType(metadata);
        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        int firstBody = AddBody(
            bodyEncoder,
            il,
            localSignature: default);
        int secondBody = AddBody(
            bodyEncoder,
            il,
            localSignature: default);
        AddMethod(metadata, "Left", firstBody);
        AddMethod(metadata, "Right", secondBody);
        return Serialize(metadata, bodies);
    }

    static byte[] BuildMethodSignatureTwinAssembly(
        byte[] il,
        byte[] signature,
        bool addModifierTypeReference = false)
    {
        MetadataBuilder metadata = AssemblyMetadata();
        if (addModifierTypeReference)
        {
            AssemblyReferenceHandle assembly =
                metadata.AddAssemblyReference(
                    metadata.GetOrAddString("System.Runtime"),
                    new Version(1, 0, 0, 0),
                    culture: default,
                    publicKeyOrToken: default,
                    flags: default,
                    hashValue: default);
            metadata.AddTypeReference(
                assembly,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString("IsExternalInit"));
        }
        AddFixtureType(metadata);
        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        BlobHandle methodSignature =
            metadata.GetOrAddBlob(signature);
        AddMethod(
            metadata,
            "Left",
            AddBody(bodyEncoder, il, localSignature: default),
            methodSignature);
        AddMethod(
            metadata,
            "Right",
            AddBody(bodyEncoder, il, localSignature: default),
            methodSignature);
        return Serialize(metadata, bodies);
    }

    static byte[] BuildMethodSignaturePairAssembly(
        byte[] il,
        byte[] leftSignature,
        byte[] rightSignature)
    {
        MetadataBuilder metadata = AssemblyMetadata();
        AssemblyReferenceHandle assembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
        metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString(
                "System.Runtime.CompilerServices"),
            metadata.GetOrAddString("IsExternalInit"));
        AddFixtureType(metadata);
        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        AddMethod(
            metadata,
            "Left",
            AddBody(bodyEncoder, il, localSignature: default),
            metadata.GetOrAddBlob(leftSignature));
        AddMethod(
            metadata,
            "Right",
            AddBody(bodyEncoder, il, localSignature: default),
            metadata.GetOrAddBlob(rightSignature));
        return Serialize(metadata, bodies);
    }

    static byte[] BuildSpoofVoidTwinAssembly()
    {
        MetadataBuilder metadata = AssemblyMetadata();
        AssemblyReferenceHandle spoof =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
        TypeReferenceHandle spoofVoid =
            metadata.AddTypeReference(
                spoof,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Void"));
        AddFixtureType(metadata);

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        byte[] il = [0x14, 0x2A];
        int firstBody = AddBody(
            bodyEncoder,
            il,
            localSignature: default);
        int secondBody = AddBody(
            bodyEncoder,
            il,
            localSignature: default);
        AddMethod(
            metadata,
            "Left",
            firstBody,
            signature: AddClassReturnSignature(
                metadata,
                spoofVoid));
        AddMethod(
            metadata,
            "Right",
            secondBody,
            signature: AddObjectReturnSignature(metadata));
        return Serialize(metadata, bodies);
    }

    static byte[] BuildHeaderTwinAssembly()
    {
        MetadataBuilder metadata = AssemblyMetadata();
        AddFixtureType(metadata);
        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        int firstBody = AddBody(
            bodyEncoder,
            [0x2A],
            localSignature: default,
            maxStack: 8,
            attributes: MethodBodyAttributes.InitLocals);
        int secondBody = AddBody(
            bodyEncoder,
            [0x2A],
            localSignature: default,
            maxStack: 9,
            attributes: MethodBodyAttributes.InitLocals);
        AddMethod(metadata, "Left", firstBody);
        AddMethod(metadata, "Right", secondBody);
        return Serialize(metadata, bodies);
    }

    static byte[] BuildLocalCountTwinAssembly()
    {
        MetadataBuilder metadata = AssemblyMetadata();
        var signature = new BlobBuilder();
        signature.WriteBytes(
            new byte[] { 0x07, 0x02, 0x1C, 0x1C });
        StandaloneSignatureHandle locals =
            metadata.AddStandaloneSignature(
                metadata.GetOrAddBlob(signature));
        AddFixtureType(metadata);

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        int firstBody = AddBody(bodyEncoder, [0x2A], locals);
        int secondBody = AddBody(bodyEncoder, [0x2A], locals);
        AddMethod(metadata, "Left", firstBody);
        AddMethod(metadata, "Right", secondBody);
        return Serialize(metadata, bodies);
    }

    static MetadataBuilder AssemblyMetadata()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString("CloneMalformed.dll"),
            metadata.GetOrAddGuid(
                new Guid("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE")),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("CloneMalformed"),
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

    static void AddFixtureType(MetadataBuilder metadata)
        => metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Fixture"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

    static StandaloneSignatureHandle AddLocalSignature(
        MetadataBuilder metadata,
        TypeReferenceHandle type)
    {
        int codedType = checked(
            MetadataTokens.GetRowNumber(type) * 4 + 1);
        var signature = new BlobBuilder();
        signature.WriteByte(0x07);
        signature.WriteByte(0x01);
        signature.WriteByte(0x12);
        signature.WriteCompressedInteger(codedType);
        return metadata.AddStandaloneSignature(
            metadata.GetOrAddBlob(signature));
    }

    static StandaloneSignatureHandle AddLocalSignature(
        MetadataBuilder metadata,
        byte[] signature)
        => metadata.AddStandaloneSignature(
            metadata.GetOrAddBlob(signature));

    static int AddBody(
        MethodBodyStreamEncoder bodies,
        byte[] il,
        StandaloneSignatureHandle localSignature,
        int maxStack = 1,
        MethodBodyAttributes? attributes = null)
    {
        var code = new BlobBuilder(il.Length);
        code.WriteBytes(il);
        return bodies.AddMethodBody(
            new InstructionEncoder(code),
            maxStack,
            localVariablesSignature: localSignature,
            attributes: attributes
                ?? (localSignature.IsNil
                    ? MethodBodyAttributes.None
                    : MethodBodyAttributes.InitLocals));
    }

    static void AddMethod(
        MetadataBuilder metadata,
        string name,
        int bodyOffset,
        BlobHandle signature = default,
        MethodImplAttributes implementation =
            MethodImplAttributes.IL,
        MethodAttributes attributes =
            MethodAttributes.Public | MethodAttributes.Static)
        => metadata.AddMethodDefinition(
            attributes,
            implementation,
            metadata.GetOrAddString(name),
            signature.IsNil
                ? AddVoidSignature(metadata)
                : signature,
            bodyOffset,
            MetadataTokens.ParameterHandle(1));

    static BlobHandle AddClassReturnSignature(
        MetadataBuilder metadata,
        TypeReferenceHandle type)
    {
        var signature = new BlobBuilder();
        signature.WriteBytes(
            new byte[] { 0x00, 0x00, 0x12 });
        signature.WriteCompressedInteger(
            MetadataTokens.GetRowNumber(type) * 4 + 1);
        return metadata.GetOrAddBlob(signature);
    }

    static BlobHandle AddObjectReturnSignature(
        MetadataBuilder metadata)
        => metadata.GetOrAddBlob(
            new byte[] { 0x00, 0x00, 0x1C });

    static BlobHandle AddVoidSignature(MetadataBuilder metadata)
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

    static byte[] Serialize(
        MetadataBuilder metadata,
        BlobBuilder methodBodies)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }
}
