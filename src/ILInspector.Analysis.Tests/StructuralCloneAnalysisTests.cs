using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

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
    public void Compare_CompilerProducedOneOperationChanges_AreNear()
    {
        using PEReader image = OpenFixture();
        MetadataReader reader = image.GetMetadataReader();

        StructuralCloneComparison constant =
            StructuralCloneAnalysis.Compare(
                image,
                Method(reader, nameof(StructuralCloneFixture.NearConstantA)),
                Method(reader, nameof(StructuralCloneFixture.NearConstantB)));
        StructuralCloneComparison call =
            StructuralCloneAnalysis.Compare(
                image,
                Method(reader, nameof(StructuralCloneFixture.NearCallTargetA)),
                Method(reader, nameof(StructuralCloneFixture.NearCallTargetB)));
        StructuralCloneComparison constantReverse =
            StructuralCloneAnalysis.Compare(
                image,
                Method(reader, nameof(StructuralCloneFixture.NearConstantB)),
                Method(reader, nameof(StructuralCloneFixture.NearConstantA)));
        StructuralCloneComparison callReverse =
            StructuralCloneAnalysis.Compare(
                image,
                Method(reader, nameof(StructuralCloneFixture.NearCallTargetB)),
                Method(reader, nameof(StructuralCloneFixture.NearCallTargetA)));

        AssertNearOperationChange(constant);
        AssertNearOperationChange(call);
        AssertNearOperationChange(constantReverse);
        AssertNearOperationChange(callReverse);
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
    public void Discover_CompilerProducedPopulation_FindsClosedExactFamilies()
    {
        using PEReader image = OpenFixture();
        MetadataReader reader = image.GetMetadataReader();
        ImmutableArray<MethodDefinitionHandle> population =
        [
            Method(reader, nameof(StructuralCloneFixture.ExactPositiveA)),
            Method(reader, nameof(StructuralCloneFixture.ExactPositiveB)),
            Method(reader, nameof(StructuralCloneFixture.EdgeRoleNegativeA)),
            Method(reader, nameof(StructuralCloneFixture.EdgeRoleNegativeB)),
            Method(reader, nameof(StructuralCloneFixture.SignatureHazardByte)),
            Method(reader, nameof(StructuralCloneFixture.SignatureHazardUInt)),
            Method(reader, nameof(StructuralCloneFixture.SignatureHazardString)),
            Method(reader, nameof(StructuralCloneFixture.SignatureHazardObject)),
            Method(reader, nameof(StructuralCloneFixture.MetadataOperandsA)),
            Method(reader, nameof(StructuralCloneFixture.MetadataOperandsB)),
            Method(reader, nameof(StructuralCloneFixture.ExceptionHandlingA)),
            Method(reader, nameof(StructuralCloneFixture.ExceptionHandlingB)),
        ];

        StructuralCloneDiscoveryResult result =
            StructuralCloneAnalysis.Discover(image, population);

        Assert.Equal(
            StructuralCloneDiscoveryDisposition.Completed,
            result.Disposition);
        Assert.Empty(result.Blockers);
        Assert.Empty(result.SuppressedBuckets);
        Assert.Empty(result.UnresolvedComparisons);
        Assert.Equal(4, result.Clusters.Length);
        Assert.Equal(12, result.Receipt.ProcessedMethods);
        Assert.Equal(10, result.Receipt.EligibleMethods);
        Assert.Equal(2, result.Receipt.UnsupportedMethods);
        Assert.Equal(4, result.Receipt.ExactComparisons);
        Assert.Equal(1, result.Receipt.DifferentComparisons);
        StructuralCloneMethodOutcome[] unsupported =
        [
            .. result.Methods.Where(static method =>
                method.Disposition
                    == StructuralCloneDisposition.Unsupported),
        ];
        Assert.Equal(2, unsupported.Length);
        Assert.All(
            unsupported,
            static method => Assert.All(
                method.Blockers,
                static blocker => Assert.Equal(
                    StructuralCloneDiscoveryBlockerKind.MethodUnsupported,
                    blocker.Kind)));

        AssertCluster(
            result,
            Method(reader, nameof(StructuralCloneFixture.ExactPositiveA)),
            Method(reader, nameof(StructuralCloneFixture.ExactPositiveB)));
        AssertCluster(
            result,
            Method(reader, nameof(StructuralCloneFixture.SignatureHazardByte)),
            Method(reader, nameof(StructuralCloneFixture.SignatureHazardUInt)));
        AssertCluster(
            result,
            Method(reader, nameof(StructuralCloneFixture.SignatureHazardString)),
            Method(reader, nameof(StructuralCloneFixture.SignatureHazardObject)));
        AssertCluster(
            result,
            Method(reader, nameof(StructuralCloneFixture.MetadataOperandsA)),
            Method(reader, nameof(StructuralCloneFixture.MetadataOperandsB)));

        HashSet<MethodDefinitionHandle> clustered =
        [
            .. result.Clusters.SelectMany(static cluster =>
                cluster.Members.Select(static member => member.Handle)),
        ];
        Assert.DoesNotContain(
            Method(reader, nameof(StructuralCloneFixture.EdgeRoleNegativeA)),
            clustered);
        Assert.DoesNotContain(
            Method(reader, nameof(StructuralCloneFixture.EdgeRoleNegativeB)),
            clustered);
    }

    [Fact]
    public void Discover_ThreeMemberFamily_UsesRepresentativeEvidence()
    {
        using PEReader image = OpenImage(BuildTripletAssembly([0x2A]));
        ImmutableArray<MethodDefinitionHandle> population =
        [
            MetadataTokens.MethodDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(3),
        ];

        StructuralCloneDiscoveryResult result =
            StructuralCloneAnalysis.Discover(image, population);

        Assert.Equal(
            StructuralCloneDiscoveryDisposition.Completed,
            result.Disposition);
        StructuralCloneCluster cluster = Assert.Single(result.Clusters);
        Assert.Equal(3, cluster.Members.Length);
        Assert.Equal(2, cluster.Evidence.Length);
        Assert.All(
            cluster.Evidence,
            static evidence => Assert.Equal(
                StructuralCloneRelation.Exact,
                evidence.Relation));
        Assert.Equal(2, result.Receipt.CandidateComparisons);
    }

    [Fact]
    public void Discover_InputOrder_DoesNotChangeClusterIdentity()
    {
        using PEReader image = OpenImage(BuildTripletAssembly([0x2A]));
        MethodDefinitionHandle first =
            MetadataTokens.MethodDefinitionHandle(1);
        MethodDefinitionHandle second =
            MetadataTokens.MethodDefinitionHandle(2);
        MethodDefinitionHandle third =
            MetadataTokens.MethodDefinitionHandle(3);

        StructuralCloneDiscoveryResult forward =
            StructuralCloneAnalysis.Discover(
                image,
                [first, second, third]);
        StructuralCloneDiscoveryResult reverse =
            StructuralCloneAnalysis.Discover(
                image,
                [third, first, second]);

        Assert.Equal(
            Assert.Single(forward.Clusters).Identity,
            Assert.Single(reverse.Clusters).Identity);
    }

    [Fact]
    public void Discover_DuplicateHandles_AreCallerError()
    {
        using PEReader image = OpenImage(BuildTwinAssembly([0x2A]));
        MethodDefinitionHandle method =
            MetadataTokens.MethodDefinitionHandle(1);

        Assert.Throws<ArgumentException>(
            () => StructuralCloneAnalysis.Discover(
                image,
                [method, method]));
    }

    [Fact]
    public void Discover_ExactComparisonBudget_CanComplete()
    {
        using PEReader image = OpenImage(BuildTripletAssembly([0x2A]));

        StructuralCloneDiscoveryResult result =
            StructuralCloneAnalysis.Discover(
                image,
                [
                    MetadataTokens.MethodDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(2),
                    MetadataTokens.MethodDefinitionHandle(3),
                ],
                new StructuralCloneDiscoveryLimits(
                    MaximumCandidateComparisons: 2));

        Assert.Equal(
            StructuralCloneDiscoveryDisposition.Completed,
            result.Disposition);
        Assert.Single(result.Clusters);
        Assert.Equal(2, result.Receipt.CandidateComparisons);
    }

    [Fact]
    public void Discover_ExhaustedBudget_SuppressesLaterBucketsBeforeReplay()
    {
        using PEReader image = OpenFixture();
        MetadataReader reader = image.GetMetadataReader();

        StructuralCloneDiscoveryResult result =
            StructuralCloneAnalysis.Discover(
                image,
                [
                    Method(
                        reader,
                        nameof(StructuralCloneFixture.ExactPositiveA)),
                    Method(
                        reader,
                        nameof(StructuralCloneFixture.ExactPositiveB)),
                    Method(
                        reader,
                        nameof(StructuralCloneFixture.MetadataOperandsA)),
                    Method(
                        reader,
                        nameof(StructuralCloneFixture.MetadataOperandsB)),
                ],
                new StructuralCloneDiscoveryLimits(
                    MaximumCandidateComparisons: 1));

        Assert.Equal(
            StructuralCloneDiscoveryDisposition.LimitReached,
            result.Disposition);
        Assert.Single(result.Clusters);
        Assert.Single(result.SuppressedBuckets);
        Assert.Equal(1, result.Receipt.CandidateComparisons);
        Assert.Equal(6, result.Receipt.BodyProductions);
    }

    [Fact]
    public void Discover_MidBucketBudget_EmitsNoPartialCluster()
    {
        using PEReader image = OpenImage(BuildTripletAssembly([0x2A]));

        StructuralCloneDiscoveryResult result =
            StructuralCloneAnalysis.Discover(
                image,
                [
                    MetadataTokens.MethodDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(2),
                    MetadataTokens.MethodDefinitionHandle(3),
                ],
                new StructuralCloneDiscoveryLimits(
                    MaximumCandidateComparisons: 1));

        Assert.Equal(
            StructuralCloneDiscoveryDisposition.LimitReached,
            result.Disposition);
        Assert.Empty(result.Clusters);
        StructuralCloneSuppressedBucket suppressed =
            Assert.Single(result.SuppressedBuckets);
        Assert.Equal(3, suppressed.Methods.Length);
        Assert.Equal(
            StructuralCloneDiscoveryBlockerKind.CandidateComparisonLimit,
            suppressed.Reason.Kind);
        Assert.Equal(1, result.Receipt.CandidateComparisons);
        Assert.Equal(6, result.Receipt.BodyProductions);
    }

    [Fact]
    public void Discover_MethodLimitAdmission_IsAtomic()
    {
        using PEReader image = OpenImage(BuildTwinAssembly([0x2A]));

        StructuralCloneDiscoveryResult result =
            StructuralCloneAnalysis.Discover(
                image,
                [
                    MetadataTokens.MethodDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(2),
                ],
                new StructuralCloneDiscoveryLimits(MaximumMethods: 1));

        Assert.Equal(
            StructuralCloneDiscoveryDisposition.LimitReached,
            result.Disposition);
        Assert.Empty(result.Methods);
        Assert.Equal(0, result.Receipt.ProcessedMethods);
        Assert.Equal(0, result.Receipt.BodyProductions);
        Assert.Equal(2, result.Receipt.SuppressedMethods);
    }

    [Fact]
    public void Discover_MalformedModuleIdentity_ReturnsTypedFailure()
    {
        using PEReader image = OpenImage(
            BuildMalformedModuleIdentityTwinAssembly());

        StructuralCloneDiscoveryResult result =
            StructuralCloneAnalysis.Discover(
                image,
                [
                    MetadataTokens.MethodDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(2),
                ]);

        Assert.Equal(
            StructuralCloneDiscoveryDisposition.Failed,
            result.Disposition);
        Assert.Empty(result.Methods);
        StructuralCloneDiscoveryBlocker blocker =
            Assert.Single(result.Blockers);
        Assert.Equal(
            StructuralCloneDiscoveryBlockerKind.MetadataReadFailure,
            blocker.Kind);
    }

    [Fact]
    public void Discover_MalformedMetadataRootPreservesReason()
    {
        byte[] bytes = BuildTwinAssembly([0x2A]);
        using (PEReader validImage = OpenImage(bytes))
            bytes[validImage.PEHeaders.MetadataStartOffset] = 0;
        using PEReader image = OpenImage(bytes);

        StructuralCloneDiscoveryResult result =
            StructuralCloneAnalysis.Discover(
                image,
                [
                    MetadataTokens.MethodDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(2),
                ]);

        StructuralCloneDiscoveryBlocker blocker =
            Assert.Single(result.Blockers);
        Assert.Equal(
            MetadataRootMalformedReason.InvalidSignature,
            blocker.MetadataRootReason);
    }

    [Fact]
    public void Discover_InvalidLimits_AreCallerError()
    {
        using PEReader image = OpenImage(BuildTwinAssembly([0x2A]));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => StructuralCloneAnalysis.Discover(
                image,
                [MetadataTokens.MethodDefinitionHandle(1)],
                new StructuralCloneDiscoveryLimits(
                    MaximumCandidateComparisons: 0)));
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
        using PEReader reservedHeaderImage = OpenImage(
            BuildCalliTwinAssembly(
                calli,
                signature: [0x80, 0x00, 0x01]));
        using PEReader zeroArityGenericImage = OpenImage(
            BuildCalliTwinAssembly(
                calli,
                signature: [0x10, 0x00, 0x00, 0x01]));

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
        AssertFailedMetadataOperand(reservedHeaderImage);
        AssertFailedMetadataOperand(zeroArityGenericImage);
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
            { new byte[] { 0x80, 0x00, 0x01 } },
            { new byte[] { 0x40, 0x00, 0x01 } },
            { new byte[] { 0x10, 0x01, 0x00, 0x01 } },
            { new byte[] { 0x00, 0x00, 0x1B, 0x80, 0x00, 0x01 } },
            { new byte[] { 0x10, 0x00, 0x00, 0x01 } },
            { new byte[] { 0x00, 0x00, 0x1B, 0x10, 0x00, 0x00, 0x01 } },
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
        Assert.True(
            instructionLimited.Receipt.LeftEdges > 0);
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
            { new byte[] { 0x07, 0x01, 0x14, 0x08, 0x00, 0x00, 0x00 } },
            { new byte[] { 0x07, 0x01, 0x14, 0x08, 0x01, 0x02, 0x01, 0x02, 0x00 } },
            { new byte[] { 0x07, 0x01, 0x14, 0x08, 0x01, 0x00, 0x02, 0x00, 0x00 } },
            { new byte[] { 0x07, 0x01, 0x13, 0x00 } },
            { new byte[] { 0x07, 0x01, 0x1E, 0x00 } },
        };

    [Fact]
    public void Compare_ReservedLocalSignatureHeaderFailsBeforeLocalCount()
    {
        using PEReader image = OpenImage(
            BuildLocalSignaturePairAssembly(
                [0x87, 0x01, 0x08],
                [0x87, 0x01, 0x08]));

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(
                image,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        Assert.Equal(
            StructuralCloneDisposition.Failed,
            comparison.Disposition);
        Assert.Equal(0, comparison.Receipt.LeftLocals);
        Assert.Equal(0, comparison.Receipt.RightLocals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compare_ValidGenericLocalScopesRemainSupported(
        bool methodParameter)
    {
        using PEReader image = OpenImage(
            BuildGenericLocalTwinAssembly(methodParameter));

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
    public void Compare_MalformedUserStringTrailerFails()
    {
        using PEReader image = OpenImage(
            BuildUserStringTwinAssembly(
                "A",
                replacementTerminal: 0x02));

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

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("\u0001")]
    [InlineData("\u007F")]
    [InlineData("\u0100")]
    public void Compare_ValidUserStringTrailerRemainsSupported(
        string text)
    {
        using PEReader image = OpenImage(
            BuildUserStringTwinAssembly(
                text,
                replacementTerminal: null));

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(
                image,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        Assert.Equal(
            StructuralCloneRelation.Exact,
            comparison.Relation);
    }

    [Theory]
    [InlineData("A", 1)]
    [InlineData("\u0100", 0)]
    public void Compare_UserStringHintVariantsRemainSupported(
        string text,
        byte replacementTerminal)
    {
        using PEReader image = OpenImage(
            BuildUserStringTwinAssembly(
                text,
                replacementTerminal));

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
    public void Compare_CompilerProducedNonAsciiUserStringRemainsSupported()
    {
        using PEReader image = OpenFixture();
        MetadataReader reader = image.GetMetadataReader();

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(
                image,
                Method(
                    reader,
                    nameof(
                        StructuralCloneUserStringFixture.NonAsciiA)),
                Method(
                    reader,
                    nameof(
                        StructuralCloneUserStringFixture.NonAsciiB)));

        Assert.Equal(
            StructuralCloneRelation.Exact,
            comparison.Relation);
    }

    [Fact]
    public void Compare_MalformedModuleIdentityFailsWithoutThrowing()
    {
        using PEReader image = OpenImage(
            BuildMalformedModuleIdentityTwinAssembly());

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
            static blocker =>
                blocker.Kind
                    == StructuralCloneBlockerKind.MetadataReadFailure);
    }

    [Fact]
    public void Compare_MalformedMetadataRootFailsWithoutThrowing()
    {
        byte[] bytes = BuildTwinAssembly([0x2A]);
        using (PEReader validImage = OpenImage(bytes))
            bytes[validImage.PEHeaders.MetadataStartOffset] = 0;
        using PEReader image = OpenImage(bytes);

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
            static blocker =>
                blocker.Kind
                    == StructuralCloneBlockerKind.MetadataReadFailure);
        Assert.Contains(
            nameof(MalformedMetadataRootException),
            Assert.Single(comparison.Blockers).Detail,
            StringComparison.Ordinal);
        Assert.Equal(
            MetadataRootMalformedReason.InvalidSignature,
            Assert.Single(comparison.Blockers).MetadataRootReason);
    }

    [Fact]
    public void Compare_MalformedMetadataDirectoryFailsWithoutThrowing()
    {
        byte[] bytes = BuildTwinAssembly([0x2A]);
        using (PEReader validImage = OpenImage(bytes))
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(
                    validImage.PEHeaders.CorHeaderStartOffset
                        + 3 * sizeof(uint)),
                uint.MaxValue);
        }
        using PEReader image = OpenImage(bytes);

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
            static blocker =>
                blocker.Kind
                    == StructuralCloneBlockerKind.MetadataReadFailure);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => StructuralCloneAnalysis.Compare(
                image,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2),
                new StructuralCloneComparisonLimits(
                    MaximumInstructions: 0)));
    }

    [Fact]
    public void Compare_DisposedImageStillThrows()
    {
        PEReader image = OpenImage(BuildTwinAssembly([0x2A]));
        image.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => StructuralCloneAnalysis.Compare(
                image,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2)));
    }

    [Fact]
    public void Compare_EdgeLimitPrecedesMetadataOperandValidation()
    {
        using PEReader image = OpenImage(
            BuildTwinAssembly(
                BuildInvalidOperandDuplicateTargetSwitch(256)));

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(
                image,
                MetadataTokens.MethodDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2),
                new StructuralCloneComparisonLimits(
                    MaximumEdges: 1));

        AssertLimit(
            comparison,
            StructuralCloneBlockerKind.EdgeLimit);
        Assert.DoesNotContain(
            comparison.Blockers,
            static blocker =>
                blocker.Kind
                    == StructuralCloneBlockerKind.InvalidMetadataOperand);
        Assert.Equal(257, comparison.Receipt.LeftEdges);
        Assert.Equal(257, comparison.Receipt.RightEdges);
    }

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
    public void Compare_PinnedTypeSpecLocalFails()
    {
        using PEReader image = OpenImage(
            BuildPinnedTypeSpecLocalTwinAssembly());

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
    public void Compare_OneChangedEdgeIsNearButSwitchOrderIsDifferent()
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

        AssertNearEdge(
            StructuralCloneAnalysis.Compare(edgeLeft, edgeRight),
            StructuralCloneEditKind.Changed);
        Assert.Equal(
            StructuralCloneRelation.Different,
            StructuralCloneAnalysis.Compare(
                switchLeft,
                switchRight).Relation);
    }

    [Fact]
    public void Compare_CompilerProducedTwoOperationContrast_IsDifferent()
    {
        using PEReader image = OpenFixture();
        MetadataReader reader = image.GetMetadataReader();

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(
                image,
                Method(
                    reader,
                    nameof(StructuralCloneFixture.NearHardNegativeA)),
                Method(
                    reader,
                    nameof(StructuralCloneFixture.NearHardNegativeB)));

        Assert.Equal(
            StructuralCloneDisposition.Completed,
            comparison.Disposition);
        Assert.Equal(
            StructuralCloneRelation.Different,
            comparison.Relation);
        Assert.Null(comparison.Alignment);
        Assert.True(comparison.AlignmentReceipt?.Exhausted);
        Assert.True(
            comparison.AlignmentReceipt!.VerificationSteps
                >= comparison.AlignmentReceipt.Candidates);
    }

    [Fact]
    public void Compare_OperationReordering_IsNotOneChange()
    {
        StructuralCloneBodyFacts left = Facts(
            token: 1,
            il: [0x02, 0x17, 0x58, 0x2A]);
        StructuralCloneBodyFacts right = Facts(
            token: 2,
            il: [0x17, 0x02, 0x58, 0x2A]);

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(left, right);

        Assert.Equal(
            StructuralCloneRelation.Different,
            comparison.Relation);
        Assert.Null(comparison.Alignment);
        Assert.True(comparison.AlignmentReceipt?.Exhausted);
    }

    [Fact]
    public void Compare_ChangedOperationsRequireOneJointBlockWitness()
    {
        StructuralCloneBodyFacts left = RigidColoredGraph(
            token: 1,
            changedBlock: 1,
            ILOpCode.Nop);
        StructuralCloneBodyFacts right = RigidColoredGraph(
            token: 2,
            changedBlock: 2,
            ILOpCode.Break);

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(left, right);

        Assert.Equal(
            StructuralCloneRelation.Different,
            comparison.Relation);
        Assert.Null(comparison.Alignment);
        Assert.True(comparison.AlignmentReceipt?.Exhausted);
    }

    [Fact]
    public void Compare_ChangedLocalUseHasJointRestoringWitness()
    {
        StructuralCloneMethodSignature signature =
            new(0, 0, 0, 0, ReturnsVoid: true);
        StructuralCloneBodyFacts left = Facts(
            token: 1,
            il: [0x06, 0x26, 0x06, 0x26, 0x07, 0x26, 0x2A],
            locals: [s_int, s_int],
            signature: signature);
        StructuralCloneBodyFacts right = Facts(
            token: 2,
            il: [0x07, 0x26, 0x06, 0x26, 0x07, 0x26, 0x2A],
            locals: [s_int, s_int],
            signature: signature);

        AssertNearOperationChange(
            StructuralCloneAnalysis.Compare(left, right));
    }

    [Fact]
    public void Compare_LargeSingleBlockDifferencePrunesNonRestoringPositions()
    {
        List<byte> leftIl = [];
        for (int index = 0; index < 50; index++)
        {
            leftIl.Add(0x17);
            leftIl.Add(0x26);
        }
        leftIl.Add(0x02);
        leftIl.Add(0x2A);
        byte[] rightIl = [.. leftIl];
        rightIl[0] = 0x18;
        rightIl[2] = 0x19;

        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(
                Facts(token: 1, il: [.. leftIl]),
                Facts(token: 2, il: rightIl));

        Assert.Equal(
            StructuralCloneRelation.Different,
            comparison.Relation);
        StructuralCloneAlignmentReceipt receipt =
            Assert.IsType<StructuralCloneAlignmentReceipt>(
                comparison.AlignmentReceipt);
        Assert.Equal(0, receipt.Candidates);
        Assert.Equal(0, receipt.VerificationSteps);
        Assert.True(receipt.IndexSteps > 0);
        Assert.True(receipt.Exhausted);
    }

    [Fact]
    public void Compare_LargeMultiBlockNearUsesMaskedCandidateIndex()
    {
        StructuralCloneBodyFacts left =
            LargeNearGraph(token: 1, changedValue: 1_000);
        StructuralCloneBodyFacts right =
            LargeNearGraph(token: 2, changedValue: 1_005);

        StructuralCloneComparison forward =
            StructuralCloneAnalysis.Compare(left, right);
        StructuralCloneComparison reverse =
            StructuralCloneAnalysis.Compare(right, left);

        AssertNearOperationChange(forward);
        AssertNearOperationChange(reverse);
        Assert.InRange(forward.AlignmentReceipt!.Candidates, 1, 4);
        Assert.InRange(reverse.AlignmentReceipt!.Candidates, 1, 4);
        Assert.True(forward.AlignmentReceipt.VerificationSteps > 10_000);
        Assert.True(reverse.AlignmentReceipt.VerificationSteps > 10_000);

        StructuralCloneComparison limited =
            StructuralCloneAnalysis.Compare(
                left,
                right,
                new StructuralCloneComparisonLimits(
                    MaximumNearAlignmentVerificationSteps: 1_000));
        AssertLimit(
            limited,
            StructuralCloneBlockerKind
                .NearAlignmentVerificationStepLimit);
    }

    // Regression coverage for a round-2/round-3 review finding: an earlier
    // most-constrained-variable optimization in SearchBlocks truncated a
    // left block's candidate list at its first match, silently discarding
    // any remaining true candidates. Whenever the retained candidate later
    // failed to extend to a full witness, the search had no fallback and
    // reported Different for methods that were actually exact clones (a
    // false negative invisible in the LimitReached/Different distinction).
    // No hand-built fixture previously exercised genuine multi-candidate
    // block ambiguity requiring backtracking, so this silently regressed
    // with all 1030 other tests green. This test constructs `right` as a
    // randomly permuted structural copy of a randomly generated `left`
    // graph -- by construction, an isomorphism always exists -- so any
    // outcome other than Exact proves the search dropped a valid witness.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(23)]
    [InlineData(24)]
    [InlineData(25)]
    [InlineData(26)]
    [InlineData(27)]
    [InlineData(28)]
    [InlineData(29)]
    [InlineData(30)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(34)]
    [InlineData(35)]
    [InlineData(36)]
    [InlineData(37)]
    [InlineData(38)]
    [InlineData(39)]
    [InlineData(40)]
    // These four seeds were found (out of a 1-20,000 sweep) to be the
    // only ones, at blockCount: 6, that actually reach SearchBlocks with
    // a left block holding 2+ live candidates where the first-found
    // candidate is not part of the eventual witness -- i.e., they are
    // the only seeds in that sweep that fail when the removed unsound
    // inner break is reintroduced. Seeds 1-40 above pass regardless of
    // that break and so do not, by themselves, guard against it; these
    // pinned seeds are required for the test to be a meaningful
    // regression guard rather than a vacuous one.
    [InlineData(1280)]
    [InlineData(12271)]
    [InlineData(17310)]
    [InlineData(18979)]
    public void Compare_RandomPermutedIsomorphicGraph_AlwaysFindsWitness(
        int seed)
    {
        StructuralCloneBodyFacts left = RandomAmbiguousGraph(
            token: 1,
            seed,
            blockCount: 6);
        int[] permutation = RandomPermutation(seed, count: 6);
        StructuralCloneBodyFacts right = PermuteGraph(
            left,
            token: 2,
            permutation);

        StructuralCloneComparison forward =
            StructuralCloneAnalysis.Compare(left, right);
        StructuralCloneComparison reverse =
            StructuralCloneAnalysis.Compare(right, left);

        Assert.Equal(
            StructuralCloneDisposition.Completed,
            forward.Disposition);
        Assert.Equal(StructuralCloneRelation.Exact, forward.Relation);
        Assert.Equal(
            StructuralCloneDisposition.Completed,
            reverse.Disposition);
        Assert.Equal(StructuralCloneRelation.Exact, reverse.Relation);
    }

    [Fact]
    public void Compare_LargeLocalNearUsesLocalUseIndex()
    {
        StructuralCloneBodyFacts left =
            LargeLocalNearGraph(token: 1, changedLocal: 30);
        StructuralCloneBodyFacts right =
            LargeLocalNearGraph(token: 2, changedLocal: 31);

        StructuralCloneComparison forward =
            StructuralCloneAnalysis.Compare(left, right);
        StructuralCloneComparison reverse =
            StructuralCloneAnalysis.Compare(right, left);

        AssertNearOperationChange(forward);
        AssertNearOperationChange(reverse);
        Assert.InRange(forward.AlignmentReceipt!.Candidates, 1, 4);
        Assert.InRange(reverse.AlignmentReceipt!.Candidates, 1, 4);
    }

    [Fact]
    public void Compare_OneEdgeChangeInsertionAndRemoval_AreNear()
    {
        StructuralCloneBodyFacts original = Facts(
            token: 1,
            il: [0x02, 0x2D, 0x02, 0x17, 0x2A, 0x18, 0x2A]);
        StructuralCloneBodyFacts retargeted =
            WithRetargetedEdge(original, token: 2, 0, 0, 1);
        StructuralCloneBodyFacts withoutEdge =
            WithoutEdge(original, token: 3, 0, 0);

        StructuralCloneComparison changed =
            StructuralCloneAnalysis.Compare(original, retargeted);
        StructuralCloneComparison changedReverse =
            StructuralCloneAnalysis.Compare(retargeted, original);
        StructuralCloneComparison removed =
            StructuralCloneAnalysis.Compare(original, withoutEdge);
        StructuralCloneComparison inserted =
            StructuralCloneAnalysis.Compare(withoutEdge, original);

        AssertNearEdge(changed, StructuralCloneEditKind.Changed);
        AssertNearEdge(changedReverse, StructuralCloneEditKind.Changed);
        AssertNearEdge(removed, StructuralCloneEditKind.Removed);
        AssertNearEdge(inserted, StructuralCloneEditKind.Inserted);
    }

    [Fact]
    public void Compare_LoopBackEdgeRetargeting_IsNear()
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
            StructuralCloneRelation.Near,
            StructuralCloneAnalysis.Compare(
                loop,
                retargeted).Relation);
    }

    [Fact]
    public void Compare_DuplicateSwitchTargetChange_IsNear()
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
            StructuralCloneRelation.Near,
            StructuralCloneAnalysis.Compare(
                duplicate,
                distinct).Relation);
    }

    [Fact]
    public void Compare_SymmetricGraph_ReportsStableExactAndNearAmbiguity()
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
        StructuralCloneComparison near =
            StructuralCloneAnalysis.Compare(left, nearMiss);
        StructuralCloneComparison repeatedNear =
            StructuralCloneAnalysis.Compare(left, nearMiss);
        Assert.Equal(StructuralCloneRelation.Near, near.Relation);
        Assert.Equal(
            StructuralCloneCorrespondenceKind.Ambiguous,
            near.Alignment?.Kind);
        Assert.True(near.Alignment?.Receipt.Exhausted);
        Assert.All(
            near.Alignment!.Alternatives,
            static alternative =>
            {
                Assert.Single(alternative.Blocks);
                Assert.Single(alternative.Operations);
                Assert.Empty(alternative.Edges);
            });
        Assert.Equal(
            near.Alignment.Alternatives.Select(AlignmentAlternativeKey),
            repeatedNear.Alignment!.Alternatives.Select(
                AlignmentAlternativeKey));
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
    public void Compare_NopIsNearButThisParameterRemainsExactDiscriminator()
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
            StructuralCloneRelation.Near,
            StructuralCloneAnalysis.Compare(
                plain,
                withNop).Relation);
        Assert.Equal(
            StructuralCloneRelation.Different,
            StructuralCloneAnalysis.Compare(
                plain,
                instance).Relation);
    }

    [Fact]
    public void Compare_OperationInsertionAndRemovalReverseEvidence()
    {
        StructuralCloneBodyFacts plain = Facts(
            token: 1,
            il: [0x02, 0x2A]);
        StructuralCloneBodyFacts withNop = Facts(
            token: 2,
            il: [0x00, 0x02, 0x2A]);

        StructuralCloneComparison removed =
            StructuralCloneAnalysis.Compare(withNop, plain);
        StructuralCloneComparison inserted =
            StructuralCloneAnalysis.Compare(plain, withNop);

        Assert.Equal(StructuralCloneRelation.Near, removed.Relation);
        Assert.Equal(StructuralCloneRelation.Near, inserted.Relation);
        Assert.All(
            removed.Alignment!.Alternatives,
            static alternative => Assert.Equal(
                StructuralCloneEditKind.Removed,
                Assert.Single(alternative.Operations).Kind));
        Assert.All(
            inserted.Alignment!.Alternatives,
            static alternative => Assert.Equal(
                StructuralCloneEditKind.Inserted,
                Assert.Single(alternative.Operations).Kind));
    }

    [Fact]
    public void Compare_UnreachableBlockInsertionAndRemovalCarriesContents()
    {
        StructuralCloneMethodSignature signature =
            new(0, 0, 0, 0, ReturnsVoid: true);
        StructuralCloneBodyFacts plain = Facts(
            token: 1,
            il: [0x2A],
            signature: signature);
        StructuralCloneBodyFacts withBlock = Facts(
            token: 2,
            il: [0x2A, 0x17, 0x26, 0x2A],
            signature: signature);

        StructuralCloneComparison removed =
            StructuralCloneAnalysis.Compare(withBlock, plain);
        StructuralCloneComparison inserted =
            StructuralCloneAnalysis.Compare(plain, withBlock);

        Assert.Equal(StructuralCloneRelation.Near, removed.Relation);
        Assert.Equal(StructuralCloneRelation.Near, inserted.Relation);
        StructuralCloneAlignmentAlternative removedAlternative =
            Assert.Single(removed.Alignment!.Alternatives);
        Assert.Equal(
            StructuralCloneEditKind.Removed,
            Assert.Single(removedAlternative.Blocks).Kind);
        Assert.Equal(3, removedAlternative.Operations.Length);
        StructuralCloneAlignmentAlternative insertedAlternative =
            Assert.Single(inserted.Alignment!.Alternatives);
        Assert.Equal(
            StructuralCloneEditKind.Inserted,
            Assert.Single(insertedAlternative.Blocks).Kind);
        Assert.Equal(3, insertedAlternative.Operations.Length);
    }

    [Fact]
    public void Compare_NearEnumerationLimitsDoNotBecomeDifferent()
    {
        StructuralCloneBodyFacts left = Facts(
            token: 1,
            il: [0x2A, 0x17, 0x2A, 0x17, 0x2A],
            signature: new(0, 0, 0, 0, ReturnsVoid: true));
        StructuralCloneBodyFacts right = Facts(
            token: 2,
            il: [0x2A, 0x17, 0x2A, 0x18, 0x2A],
            signature: new(0, 0, 0, 0, ReturnsVoid: true));

        StructuralCloneComparison candidateLimited =
            StructuralCloneAnalysis.Compare(
                left,
                right,
                new StructuralCloneComparisonLimits(
                    MaximumNearAlignmentCandidates: 1));
        StructuralCloneComparison indexLimited =
            StructuralCloneAnalysis.Compare(
                left,
                right,
                new StructuralCloneComparisonLimits(
                    MaximumNearAlignmentIndexSteps: 1));
        StructuralCloneComparison alternativeLimited =
            StructuralCloneAnalysis.Compare(
                Facts(
                    token: 3,
                    il: [0x2A, 0x17, 0x2A, 0x17, 0x2A],
                    signature: new(0, 0, 0, 0, ReturnsVoid: true)),
                Facts(
                    token: 4,
                    il: [0x2A, 0x17, 0x2A, 0x18, 0x2A],
                    signature: new(0, 0, 0, 0, ReturnsVoid: true)),
                new StructuralCloneComparisonLimits(
                    MaximumNearAlignmentAlternatives: 1));
        StructuralCloneComparison verificationLimited =
            StructuralCloneAnalysis.Compare(
                Facts(
                    token: 5,
                    il: [0x2A, 0x17, 0x2A, 0x17, 0x2A],
                    signature: new(0, 0, 0, 0, ReturnsVoid: true)),
                Facts(
                    token: 6,
                    il: [0x2A, 0x17, 0x2A, 0x18, 0x2A],
                    signature: new(0, 0, 0, 0, ReturnsVoid: true)),
                new StructuralCloneComparisonLimits(
                    MaximumNearAlignmentVerificationSteps: 1));
        StructuralCloneComparison blockElementLimited =
            StructuralCloneAnalysis.Compare(
                Facts(
                    token: 7,
                    il: [0x2A, 0x17, 0x26, 0x2A],
                    signature: new(0, 0, 0, 0, ReturnsVoid: true)),
                Facts(
                    token: 8,
                    il: [0x2A],
                    signature: new(0, 0, 0, 0, ReturnsVoid: true)),
                new StructuralCloneComparisonLimits(
                    MaximumNearBlockElements: 1));

        AssertLimit(
            indexLimited,
            StructuralCloneBlockerKind.NearAlignmentIndexStepLimit);
        AssertLimit(
            candidateLimited,
            StructuralCloneBlockerKind.NearAlignmentCandidateLimit);
        AssertLimit(
            alternativeLimited,
            StructuralCloneBlockerKind.NearAlignmentAlternativeLimit);
        AssertLimit(
            verificationLimited,
            StructuralCloneBlockerKind
                .NearAlignmentVerificationStepLimit);
        AssertLimit(
            blockElementLimited,
            StructuralCloneBlockerKind.NearBlockElementLimit);
        Assert.Equal(1, indexLimited.AlignmentReceipt?.IndexSteps);
        Assert.False(indexLimited.AlignmentReceipt?.Exhausted);
        Assert.False(candidateLimited.AlignmentReceipt?.Exhausted);
        Assert.False(alternativeLimited.AlignmentReceipt?.Exhausted);
        Assert.False(verificationLimited.AlignmentReceipt?.Exhausted);
        Assert.False(blockElementLimited.AlignmentReceipt?.Exhausted);
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

    static void AssertNearOperationChange(
        StructuralCloneComparison comparison)
    {
        Assert.Equal(
            StructuralCloneDisposition.Completed,
            comparison.Disposition);
        Assert.Equal(StructuralCloneRelation.Near, comparison.Relation);
        Assert.NotNull(comparison.Alignment);
        Assert.True(comparison.Alignment.Receipt.Exhausted);
        Assert.Equal(
            comparison.Alignment.Receipt,
            comparison.AlignmentReceipt);
        Assert.All(
            comparison.Alignment.Alternatives,
            static alternative =>
            {
                Assert.Single(alternative.Blocks);
                Assert.Equal(
                    StructuralCloneEditKind.Changed,
                    Assert.Single(alternative.Operations).Kind);
                Assert.Empty(alternative.Edges);
            });
    }

    static void AssertNearEdge(
        StructuralCloneComparison comparison,
        StructuralCloneEditKind kind)
    {
        Assert.Equal(StructuralCloneRelation.Near, comparison.Relation);
        Assert.All(
            comparison.Alignment!.Alternatives,
            alternative =>
            {
                Assert.Single(alternative.Blocks);
                Assert.Empty(alternative.Operations);
                Assert.Equal(
                    kind,
                    Assert.Single(alternative.Edges).Kind);
            });
    }

    static string AlignmentAlternativeKey(
        StructuralCloneAlignmentAlternative alternative)
        => string.Join(
            "|",
            alternative.Blocks.Select(edit =>
                $"B:{edit.Kind}:{string.Join(',', edit.LeftBlocks)}:"
                    + string.Join(',', edit.RightBlocks))
            .Concat(alternative.Operations.Select(edit =>
                $"O:{edit.Kind}:{edit.Left}:{edit.Right}"))
            .Concat(alternative.Edges.Select(edit =>
                $"E:{edit.Kind}:{edit.Left}:{edit.Right}")));

    static StructuralCloneBodyFacts RandomAmbiguousGraph(
        int token,
        int seed,
        int blockCount)
    {
        // A general random directed graph: block 0 is entry, block
        // blockCount - 1 is the sole Ret exit, and every other block has
        // two random outgoing edges (Branch ordinal 0, FallThrough
        // ordinal 0) chosen from all blocks. All non-exit blocks carry
        // identical (empty) operations, so color refinement alone often
        // cannot disambiguate them -- the search must rely on genuine
        // backtracking to find a consistent global assignment, unlike
        // the earlier symmetric "twin" design where any locally valid
        // choice was automatically part of a valid global witness.
        if (blockCount < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(blockCount),
                blockCount,
                "blockCount must be at least 2 (entry plus exit).");
        }
        var random = new Random(seed);
        int exit = blockCount - 1;
        var blocks = ImmutableArray.CreateBuilder<StructuralCloneBlock>(
            blockCount);
        for (int index = 0; index < blockCount; index++)
        {
            if (index == exit)
            {
                blocks.Add(new StructuralCloneBlock(
                    index,
                    index,
                    true,
                    [
                        new(
                            ILOpCode.Ret,
                            StructuralCloneOperandKind.None,
                            0),
                    ],
                    [],
                    []));
                continue;
            }
            int branchTarget = random.Next(blockCount);
            int fallThroughTarget = random.Next(blockCount);
            blocks.Add(new StructuralCloneBlock(
                index,
                index,
                false,
                [],
                [
                    new(
                        new(StructuralCloneEdgeKind.Branch, 0),
                        branchTarget),
                    new(
                        new(StructuralCloneEdgeKind.FallThrough, 0),
                        fallThroughTarget),
                ],
                []));
        }
        return new StructuralCloneBodyFacts(
            Address(token),
            BodyBytes: blocks.Count,
            InstructionCount: blocks.Sum(
                static block => block.Operations.Length),
            InitLocals: false,
            Locals: [],
            Signature: new(0, 0, 0, 0, ReturnsVoid: true),
            RebuildTestGraph(blocks.ToImmutable()));
    }

    static int[] RandomPermutation(int seed, int count)
    {
        // Index 0 must stay fixed: FindWitness requires the entry block
        // (always index 0 by construction) to map entry-to-entry, so a
        // permutation must only shuffle the non-entry indices.
        var random = new Random(seed + 1_000);
        int[] permutation = [.. Enumerable.Range(0, count)];
        for (int index = count - 1; index > 1; index--)
        {
            int swap = random.Next(1, index + 1);
            (permutation[index], permutation[swap]) =
                (permutation[swap], permutation[index]);
        }
        return permutation;
    }

    static StructuralCloneBodyFacts PermuteGraph(
        StructuralCloneBodyFacts body,
        int token,
        int[] permutation)
    {
        var byOldIndex = body.Graph.Blocks.ToDictionary(
            static block => block.Index);
        ImmutableArray<StructuralCloneBlock> blocks =
        [
            .. Enumerable.Range(0, permutation.Length).Select(newIndex =>
            {
                StructuralCloneBlock original =
                    byOldIndex[Array.IndexOf(permutation, newIndex)];
                return new StructuralCloneBlock(
                    newIndex,
                    newIndex,
                    original.ExitsMethod,
                    original.Operations,
                    [
                        .. original.Outgoing.Select(edge => edge with
                        {
                            Target = permutation[edge.Target],
                        }),
                    ],
                    []);
            }),
        ];
        return new StructuralCloneBodyFacts(
            Address(token),
            body.BodyBytes,
            body.InstructionCount,
            body.InitLocals,
            body.Locals,
            body.Signature,
            RebuildTestGraph(blocks));
    }

    static StructuralCloneBodyFacts RigidColoredGraph(
        int token,
        int changedBlock,
        ILOpCode operation)
    {
        ImmutableArray<StructuralCloneBlock> blocks =
        [
            new(0, 0, true, [], [], []),
            .. Enumerable.Range(1, 5).Select(block =>
            {
                int cycle = 1 + (block % 5);
                int permutation = block switch
                {
                    1 => 2,
                    2 => 1,
                    _ => block,
                };
                return new StructuralCloneBlock(
                    block,
                    block,
                    false,
                    block == changedBlock
                        ? [new(operation, StructuralCloneOperandKind.None, 0)]
                        : [],
                    [
                        new(
                            new(StructuralCloneEdgeKind.Branch, 0),
                            cycle),
                        new(
                            new(StructuralCloneEdgeKind.Branch, 1),
                            permutation),
                    ],
                    []);
            }),
        ];
        return new StructuralCloneBodyFacts(
            Address(token),
            BodyBytes: 1,
            InstructionCount: 1,
            InitLocals: false,
            Locals: [],
            Signature: new(0, 0, 0, 0, ReturnsVoid: true),
            RebuildTestGraph(blocks));
    }

    static StructuralCloneBodyFacts LargeNearGraph(
        int token,
        int changedValue)
    {
        const int Conditions = 55;
        const int FirstReturn = Conditions;
        const int DefaultReturn = Conditions * 2;
        var blocks =
            ImmutableArray.CreateBuilder<StructuralCloneBlock>(
                DefaultReturn + 1);
        for (int condition = 0; condition < Conditions; condition++)
        {
            blocks.Add(new StructuralCloneBlock(
                condition,
                condition,
                false,
                [
                    new(
                        ILOpCode.Ldarg,
                        StructuralCloneOperandKind.Argument,
                        0),
                    new(
                        ILOpCode.Ldc_i4,
                        StructuralCloneOperandKind.Immediate,
                        condition),
                ],
                [
                    new(
                        new(StructuralCloneEdgeKind.Branch, 0),
                        FirstReturn + condition),
                    new(
                        new(StructuralCloneEdgeKind.FallThrough, 0),
                        condition + 1 < Conditions
                            ? condition + 1
                            : DefaultReturn),
                ],
                []));
        }
        for (int value = 0; value < Conditions; value++)
        {
            blocks.Add(new StructuralCloneBlock(
                FirstReturn + value,
                FirstReturn + value,
                true,
                [
                    new(
                        ILOpCode.Ldc_i4,
                        StructuralCloneOperandKind.Immediate,
                        value == 0 ? changedValue : 1_000 + value),
                    new(
                        ILOpCode.Ret,
                        StructuralCloneOperandKind.None,
                        0),
                ],
                [],
                []));
        }
        blocks.Add(new StructuralCloneBlock(
            DefaultReturn,
            DefaultReturn,
            true,
            [
                new(
                    ILOpCode.Ldc_i4,
                    StructuralCloneOperandKind.Immediate,
                    -1),
                new(
                    ILOpCode.Ret,
                    StructuralCloneOperandKind.None,
                    0),
            ],
            [],
            []));
        return new StructuralCloneBodyFacts(
            Address(token),
            BodyBytes: blocks.Count,
            InstructionCount: blocks.Sum(
                static block => block.Operations.Length),
            InitLocals: false,
            Locals: [],
            Signature: s_staticIntToInt,
            RebuildTestGraph(blocks.ToImmutable()));
    }

    static StructuralCloneBodyFacts LargeLocalNearGraph(
        int token,
        int changedLocal)
    {
        const int LocalCount = 60;
        const int ErrorBlock = LocalCount + 1;
        const int SuccessBlock = LocalCount + 2;
        ImmutableArray<StructuralCloneOperation> initialization =
        [
            .. Enumerable.Range(0, LocalCount).SelectMany(local =>
                new[]
                {
                    new StructuralCloneOperation(
                        ILOpCode.Ldc_i4,
                        StructuralCloneOperandKind.Immediate,
                        local),
                    new StructuralCloneOperation(
                        ILOpCode.Stloc,
                        StructuralCloneOperandKind.Local,
                        local),
                }),
        ];
        var blocks =
            ImmutableArray.CreateBuilder<StructuralCloneBlock>(
                SuccessBlock + 1);
        blocks.Add(new StructuralCloneBlock(
            0,
            0,
            false,
            initialization,
            [
                new(
                    new(StructuralCloneEdgeKind.FallThrough, 0),
                    1),
            ],
            []));
        for (int guard = 0; guard < LocalCount; guard++)
        {
            blocks.Add(new StructuralCloneBlock(
                guard + 1,
                guard + 1,
                false,
                [
                    new(
                        ILOpCode.Ldloc,
                        StructuralCloneOperandKind.Local,
                        guard == 30 ? changedLocal : guard),
                ],
                [
                    new(
                        new(StructuralCloneEdgeKind.Branch, 0),
                        ErrorBlock),
                    new(
                        new(StructuralCloneEdgeKind.FallThrough, 0),
                        guard + 1 < LocalCount
                            ? guard + 2
                            : SuccessBlock),
                ],
                []));
        }
        blocks.Add(new StructuralCloneBlock(
            ErrorBlock,
            ErrorBlock,
            true,
            [
                new(
                    ILOpCode.Ldc_i4,
                    StructuralCloneOperandKind.Immediate,
                    -1),
                new(
                    ILOpCode.Ret,
                    StructuralCloneOperandKind.None,
                    0),
            ],
            [],
            []));
        blocks.Add(new StructuralCloneBlock(
            SuccessBlock,
            SuccessBlock,
            true,
            [
                new(
                    ILOpCode.Ldc_i4,
                    StructuralCloneOperandKind.Immediate,
                    0),
                new(
                    ILOpCode.Ret,
                    StructuralCloneOperandKind.None,
                    0),
            ],
            [],
            []));
        return new StructuralCloneBodyFacts(
            Address(token),
            BodyBytes: blocks.Count,
            InstructionCount: blocks.Sum(
                static block => block.Operations.Length),
            InitLocals: true,
            Locals:
            [
                .. Enumerable.Repeat(
                    StructuralCloneTypeIdentity.Create(s_int),
                    LocalCount),
            ],
            Signature: s_staticIntToInt,
            RebuildTestGraph(blocks.ToImmutable()));
    }

    static StructuralCloneBodyFacts WithRetargetedEdge(
        StructuralCloneBodyFacts body,
        int token,
        int source,
        int ordinal,
        int target)
    {
        ImmutableArray<StructuralCloneBlock> blocks =
        [
            .. body.Graph.Blocks.Select(block =>
                block.Index == source
                    ? block with
                    {
                        Outgoing = block.Outgoing.SetItem(
                            ordinal,
                            block.Outgoing[ordinal] with
                            {
                                Target = target,
                            }),
                    }
                    : block),
        ];
        return body with
        {
            Method = Address(token),
            Graph = RebuildTestGraph(blocks),
        };
    }

    static StructuralCloneBodyFacts WithoutEdge(
        StructuralCloneBodyFacts body,
        int token,
        int source,
        int ordinal)
    {
        ImmutableArray<StructuralCloneBlock> blocks =
        [
            .. body.Graph.Blocks.Select(block =>
                block.Index == source
                    ? block with
                    {
                        Outgoing = block.Outgoing.RemoveAt(ordinal),
                    }
                    : block),
        ];
        return body with
        {
            Method = Address(token),
            Graph = RebuildTestGraph(blocks),
        };
    }

    static StructuralCloneGraph RebuildTestGraph(
        ImmutableArray<StructuralCloneBlock> blocks)
    {
        var incoming =
            new ImmutableArray<StructuralCloneEdge>.Builder[blocks.Length];
        for (int index = 0; index < incoming.Length; index++)
        {
            incoming[index] =
                ImmutableArray.CreateBuilder<StructuralCloneEdge>();
        }
        foreach (StructuralCloneBlock source in blocks)
        {
            foreach (StructuralCloneEdge edge in source.Outgoing)
            {
                incoming[edge.Target].Add(
                    new StructuralCloneEdge(
                        edge.Role,
                        source.Index));
            }
        }
        return new StructuralCloneGraph(
            [
                .. blocks.Select(block => block with
                {
                    Incoming = incoming[block.Index].ToImmutable(),
                }),
            ]);
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

    static byte[] BuildInvalidOperandDuplicateTargetSwitch(
        int targetCount)
    {
        byte[] il = new byte[checked(
            12 + targetCount * sizeof(int))];
        il[0] = 0x16;
        il[1] = 0x45;
        BinaryPrimitives.WriteInt32LittleEndian(
            il.AsSpan(2),
            targetCount);
        int callOffset = 6 + targetCount * sizeof(int);
        il[callOffset] = 0x28;
        BinaryPrimitives.WriteInt32LittleEndian(
            il.AsSpan(callOffset + 1),
            0x0600FFFF);
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

    static void AssertCluster(
        StructuralCloneDiscoveryResult result,
        params MethodDefinitionHandle[] expected)
    {
        int[] expectedTokens =
        [
            .. expected
                .Select(static method =>
                    MetadataTokens.GetToken(method))
                .Order(),
        ];
        Assert.Single(
            result.Clusters,
            cluster => cluster.Identity.MethodTokens.SequenceEqual(
                expectedTokens));
    }

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

    static byte[] BuildGenericLocalTwinAssembly(
        bool methodParameter)
    {
        MetadataBuilder metadata = AssemblyMetadata();
        TypeDefinitionHandle fixture = AddFixtureType(metadata);
        StandaloneSignatureHandle locals =
            AddLocalSignature(
                metadata,
                methodParameter
                    ? [0x07, 0x01, 0x1E, 0x00]
                    : [0x07, 0x01, 0x13, 0x00]);
        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        BlobHandle methodSignature = metadata.GetOrAddBlob(
            methodParameter
                ? new byte[] { 0x10, 0x01, 0x00, 0x01 }
                : new byte[] { 0x00, 0x00, 0x01 });
        MethodDefinitionHandle left = AddMethod(
            metadata,
            "Left",
            AddBody(bodyEncoder, [0x2A], locals),
            methodSignature);
        MethodDefinitionHandle right = AddMethod(
            metadata,
            "Right",
            AddBody(bodyEncoder, [0x2A], locals),
            methodSignature);
        if (methodParameter)
        {
            metadata.AddGenericParameter(
                left,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                0);
            metadata.AddGenericParameter(
                right,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                0);
        }
        else
        {
            metadata.AddGenericParameter(
                fixture,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                0);
        }
        return Serialize(metadata, bodies);
    }

    static byte[] BuildUserStringTwinAssembly(
        string text,
        byte? replacementTerminal)
    {
        MetadataBuilder metadata = AssemblyMetadata();
        UserStringHandle userString =
            metadata.GetOrAddUserString(text);
        int token =
            0x70000000 | MetadataTokens.GetHeapOffset(userString);
        byte[] il = [0x72, 0, 0, 0, 0, 0x26, 0x2A];
        BinaryPrimitives.WriteInt32LittleEndian(
            il.AsSpan(1),
            token);
        AddFixtureType(metadata);

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        AddMethod(
            metadata,
            "Left",
            AddBody(bodyEncoder, il, default));
        AddMethod(
            metadata,
            "Right",
            AddBody(bodyEncoder, il, default));
        byte[] image = Serialize(metadata, bodies);
        if (replacementTerminal is { } terminal)
        {
            int stream = FindUserStringStream(image);
            int entry =
                stream + MetadataTokens.GetHeapOffset(userString);
            int entryLength = image[entry];
            image[entry + entryLength] = terminal;
        }
        return image;
    }

    static byte[] BuildMalformedModuleIdentityTwinAssembly()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString("MalformedMvid.dll"),
            MetadataTokens.GuidHandle(999),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("MalformedMvid"),
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
        AddFixtureType(metadata);

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        AddMethod(
            metadata,
            "Left",
            AddBody(bodyEncoder, [0x2A], default));
        AddMethod(
            metadata,
            "Right",
            AddBody(bodyEncoder, [0x2A], default));
        return Serialize(
            metadata,
            bodies,
            suppressValidation: true);
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

    static byte[] BuildPinnedTypeSpecLocalTwinAssembly()
    {
        MetadataBuilder metadata = AssemblyMetadata();
        metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(
                new byte[] { 0x45, 0x08 }));
        StandaloneSignatureHandle locals =
            AddLocalSignature(
                metadata,
                [0x07, 0x01, 0x12, 0x06]);
        AddFixtureType(metadata);

        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        byte[] il = [0x14, 0x0A, 0x06, 0x26, 0x2A];
        AddMethod(
            metadata,
            "Left",
            AddBody(bodyEncoder, il, locals));
        AddMethod(
            metadata,
            "Right",
            AddBody(bodyEncoder, il, locals));
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

    static byte[] BuildTripletAssembly(byte[] il)
    {
        MetadataBuilder metadata = AssemblyMetadata();
        AddFixtureType(metadata);
        var bodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(bodies);
        AddMethod(
            metadata,
            "First",
            AddBody(bodyEncoder, il, localSignature: default));
        AddMethod(
            metadata,
            "Second",
            AddBody(bodyEncoder, il, localSignature: default));
        AddMethod(
            metadata,
            "Third",
            AddBody(bodyEncoder, il, localSignature: default));
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

    static TypeDefinitionHandle AddFixtureType(
        MetadataBuilder metadata)
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

    static MethodDefinitionHandle AddMethod(
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
        BlobBuilder methodBodies,
        bool suppressValidation = false)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: suppressValidation),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static int FindUserStringStream(byte[] image)
    {
        using PEReader pe = OpenImage(image);
        int root = pe.PEHeaders.MetadataStartOffset;
        int position = root + 12;
        int versionLength =
            BinaryPrimitives.ReadInt32LittleEndian(
                image.AsSpan(position));
        position += 4 + versionLength;
        position = (position + 3) & ~3;
        position += 2;
        int streamCount =
            BinaryPrimitives.ReadUInt16LittleEndian(
                image.AsSpan(position));
        position += 2;

        for (int index = 0; index < streamCount; index++)
        {
            int offset =
                BinaryPrimitives.ReadInt32LittleEndian(
                    image.AsSpan(position));
            position += 8;
            int nameStart = position;
            while (image[position] != 0)
                position++;
            string name = Encoding.ASCII.GetString(
                image,
                nameStart,
                position - nameStart);
            position++;
            position = (position + 3) & ~3;
            if (name == "#US")
                return root + offset;
        }

        throw new InvalidDataException(
            "The fixture has no #US stream.");
    }
}
