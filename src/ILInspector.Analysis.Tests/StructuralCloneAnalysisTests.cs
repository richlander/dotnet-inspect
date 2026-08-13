using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis.StructuralCloneFixtures;
using ILInspector.Instructions;
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
}
