using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.ILDiff;
using ILInspector.Instructions;
using Inspector.Findings;

namespace ILInspector.ILDiff.Tests;

/// <summary>
/// Exception clauses are part of the canonical body (#9942): each clause
/// compares by kind, by catch Type under the operand rule, and by its extents
/// as aligned instructions. A clause-only difference is its own outcome, and
/// the Finding census takes the same atoms, so the two never diverge.
/// </summary>
public class IlBodyDiffExceptionRegionTests
{
    public enum Shape
    {
        CatchInvalidOperation,
        CatchException,
        CatchInvalidOperationWiderTry,
        CatchInvalidOperationLongBranchBefore,
        Finally,
        CatchAllObject,
        NestedCatchInFinally,
        FilterOnInvalidOperation,
        Fault,
    }

    [Fact]
    public void CatchTypeChanged_IsAnExceptionRegionDiff()
    {
        IlBodyDiffResult diff = Compare(Shape.CatchInvalidOperation, Shape.CatchException);

        Assert.Equal(IlBodyDiffOutcome.ExceptionRegionDiff, diff.Outcome);
        Assert.All(diff.Rows, row => Assert.True(row.Operation.IsExceptionClause));
        Assert.Contains(diff.Rows, row => row.Kind == IlDiffKind.Remove && row.Operation.Display.Contains("InvalidOperationException"));
        Assert.Contains(diff.Rows, row => row.Kind == IlDiffKind.Add && row.Operation.Display.Contains("System.Exception"));
        Assert.Contains(diff.Rows, row => row.Message.StartsWith("Removed IL exception clause 'catch", StringComparison.Ordinal));
    }

    [Fact]
    public void TryBoundaryMoved_IsAnExceptionRegionDiff()
    {
        IlBodyDiffResult diff = Compare(Shape.CatchInvalidOperation, Shape.CatchInvalidOperationWiderTry);

        Assert.Equal(IlBodyDiffOutcome.ExceptionRegionDiff, diff.Outcome);
        Assert.Equal(2, diff.Rows.Length);
        Assert.All(diff.Rows, row => Assert.Equal("catch", row.Operation.OpcodeFamily));
    }

    [Fact]
    public void ShiftedClauseOffsets_AreExact()
    {
        // A long branch before the try shifts every clause offset; the extents
        // still start the same aligned instructions.
        IlBodyDiffResult diff = Compare(Shape.CatchInvalidOperation, Shape.CatchInvalidOperationLongBranchBefore);

        Assert.Equal(IlBodyDiffOutcome.Exact, diff.Outcome);
    }

    [Fact]
    public void FinallyAgainstCatchAll_IsAnOpcodeDiff()
    {
        // The handlers' opcodes differ (endfinally against pop; leave), so the
        // operation stream decides before the clause table does.
        IlBodyDiffResult diff = Compare(Shape.Finally, Shape.CatchAllObject);

        Assert.Equal(IlBodyDiffOutcome.OpcodeDiff, diff.Outcome);
        Assert.Contains(diff.Rows, row => row.Operation.IsExceptionClause);
        Assert.Contains(diff.Rows, row => !row.Operation.IsExceptionClause);
    }

    [Theory]
    [InlineData(Shape.NestedCatchInFinally)]
    [InlineData(Shape.FilterOnInvalidOperation)]
    [InlineData(Shape.Fault)]
    public void IdenticalClauseTables_AreExact(Shape shape)
    {
        Assert.Equal(IlBodyDiffOutcome.Exact, Compare(shape, shape).Outcome);
    }

    [Fact]
    public void FilterAgainstCatch_IsAnExceptionRegionDiff()
    {
        // The filter's own instructions differ, so the stream is not exact;
        // the clause rows still name the kinds.
        IlBodyDiffResult diff = Compare(Shape.CatchInvalidOperation, Shape.FilterOnInvalidOperation);

        Assert.NotEqual(IlBodyDiffOutcome.Exact, diff.Outcome);
        Assert.Contains(diff.Rows, row => row.Operation.OpcodeFamily == "filter");
    }

    [Fact]
    public void ClauseAtoms_FollowTheInstructionsInDeclarationOrder()
    {
        (byte[] image, int body) = Image(Shape.NestedCatchInFinally);
        using var pe = new PEReader(new MemoryStream(image));
        MetadataReader reader = pe.GetMetadataReader();
        MethodInstructions instructions = MethodInstructions.Decode(pe.GetMethodBody(body));

        Assert.True(IlBodyDiff.TryCanonicalize(instructions, reader, out var operations, out _));
        int firstClause = Array.FindIndex([.. operations], operation => operation.IsExceptionClause);
        Assert.Equal(instructions.Instructions.Length, firstClause);
        Assert.Equal(["catch", "finally"], operations.Skip(firstClause).Select(operation => operation.OpcodeFamily));
        Assert.All(operations.Skip(firstClause), operation => Assert.Equal(4, operation.Targets.Length));
    }

    /// <summary>
    /// The census bound counts clause atoms: a body one instruction under the
    /// limit with two clauses is a typed inspection failure, not an exception
    /// from the matcher.
    /// </summary>
    [Fact]
    public void NearLimitBodyWithClauses_IsATypedInspectionFailure()
    {
        int instructions = IlFindings.MaxCanonicalOperations - 1;
        (byte[] image, int body) = Image(Shape.NestedCatchInFinally, paddingNops: instructions - 8);
        using var pe = new PEReader(new MemoryStream(image));
        MethodInstructions decoded = MethodInstructions.Decode(pe.GetMethodBody(body));
        Assert.True(decoded.IsComplete);
        Assert.Equal(2, decoded.Blocks.Regions.Length);
        Assert.True(decoded.Instructions.Length + 2 > IlFindings.MaxCanonicalOperations);

        var inspection = IlFindings.Inspect(decoded, pe.GetMetadataReader(), new FindingSubject("C::M", "M"));
        var failed = Assert.IsAssignableFrom<FindingInspection<CanonicalIlOperation>.Failed>(inspection);
        Assert.Contains("exception clauses", failed.Error.Reason);
    }

    [Fact]
    public void Census_AgreesWithTheComparison()
    {
        AssertCensusAgrees(Shape.CatchInvalidOperation, Shape.CatchException, exact: false);
        AssertCensusAgrees(Shape.CatchInvalidOperation, Shape.CatchInvalidOperationWiderTry, exact: false);
        AssertCensusAgrees(Shape.CatchInvalidOperation, Shape.CatchInvalidOperationLongBranchBefore, exact: true);
        AssertCensusAgrees(Shape.NestedCatchInFinally, Shape.NestedCatchInFinally, exact: true);
    }

    [Fact]
    public void CatchType_ComparesUnderTheNormalization()
    {
        // The catch Type is a type operand: without a reader the body cannot
        // be canonicalized, and under the whole-assembly scope its assembly is
        // elided.
        (byte[] image, int body) = Image(Shape.CatchInvalidOperation);
        using var pe = new PEReader(new MemoryStream(image));
        MethodInstructions instructions = MethodInstructions.Decode(pe.GetMethodBody(body));
        Assert.False(IlBodyDiff.TryCanonicalize(instructions, reader: null, out _, out string? failure));
        Assert.Contains("requires a MetadataReader-backed comparison", failure);

        Assert.True(IlBodyDiff.TryCanonicalize(instructions, pe.GetMetadataReader(), IlBodyDiffNormalization.NormalizeAssemblyScope, out var operations, out _));
        CanonicalIlOperation clause = Assert.Single(operations, operation => operation.IsExceptionClause);
        Assert.Contains("<assembly>", clause.Operand!.Value);
    }

    static void AssertCensusAgrees(Shape before, Shape after, bool exact)
    {
        (byte[] oldImage, int oldBody) = Image(before);
        (byte[] newImage, int newBody) = Image(after);
        using var oldPe = new PEReader(new MemoryStream(oldImage));
        using var newPe = new PEReader(new MemoryStream(newImage));
        // M is the second method row of every image; the body RVA is for the diff.
        var comparison = Assert.IsAssignableFrom<FindingComparison<CanonicalIlOperation>.Complete>(IlFindings.Compare(
            oldPe, oldPe.GetMetadataReader(), MetadataTokens.MethodDefinitionHandle(2),
            newPe, newPe.GetMetadataReader(), MetadataTokens.MethodDefinitionHandle(2),
            new FindingSubject("C::M", "M")).Value);
        bool censusExact = comparison.Pairs.All(pair => pair.Kind == PairKind.Present
            && pair is PairFinding<CanonicalIlOperation>.Present { Difference: FindingDifferenceKind.None });
        Assert.Equal(exact, censusExact);
        Assert.Equal(exact, Compare(before, after).IsExact);
    }

    static IlBodyDiffResult Compare(Shape before, Shape after, IlBodyDiffNormalization normalization = IlBodyDiffNormalization.None)
    {
        (byte[] oldImage, int oldBody) = Image(before);
        (byte[] newImage, int newBody) = Image(after);
        using var oldPe = new PEReader(new MemoryStream(oldImage));
        using var newPe = new PEReader(new MemoryStream(newImage));
        return IlBodyDiff.Compare(
            oldPe.GetMetadataReader(), oldPe.GetMethodBody(oldBody),
            newPe.GetMetadataReader(), newPe.GetMethodBody(newBody),
            normalization);
    }

    /// <summary>
    /// One image with a public static method <c>C::M()</c> of the shape, and
    /// a callee <c>C::Work()</c> it calls. Returns the image and M's body RVA.
    /// </summary>
    static (byte[] Image, int BodyRva) Image(Shape shape, int paddingNops = 0)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Eh.dll"), metadata.GetOrAddGuid(new Guid("5a5a5a5a-0000-4000-8000-000000000001")), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("Eh"), new Version(1, 0, 0, 0), default, default, 0, AssemblyHashAlgorithm.Sha1);
        AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"), new Version(10, 0, 0, 0), default, default, default, default);
        TypeReferenceHandle systemObject = metadata.AddTypeReference(runtime, metadata.GetOrAddString("System"), metadata.GetOrAddString("Object"));
        TypeReferenceHandle systemException = metadata.AddTypeReference(runtime, metadata.GetOrAddString("System"), metadata.GetOrAddString("Exception"));
        TypeReferenceHandle invalidOperation = metadata.AddTypeReference(runtime, metadata.GetOrAddString("System"), metadata.GetOrAddString("InvalidOperationException"));
        metadata.AddTypeDefinition(default, default, metadata.GetOrAddString("<Module>"), default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));

        var voidSignature = new BlobBuilder();
        new BlobEncoder(voidSignature).MethodSignature().Parameters(0, returnType => returnType.Void(), _ => { });
        BlobHandle voidHandle = metadata.GetOrAddBlob(voidSignature);
        var bodies = new BlobBuilder();
        var stream = new MethodBodyStreamEncoder(bodies);

        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed,
            default, metadata.GetOrAddString("C"), systemObject,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        MethodDefinitionHandle work = MetadataTokens.MethodDefinitionHandle(1);
        var workCode = new InstructionEncoder(new BlobBuilder());
        workCode.OpCode(ILOpCode.Ret);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, MethodImplAttributes.IL,
            metadata.GetOrAddString("Work"), voidHandle, stream.AddMethodBody(workCode), default);

        var code = new InstructionEncoder(new BlobBuilder(), new ControlFlowBuilder());
        for (int i = 0; i < paddingNops; i++)
            code.OpCode(ILOpCode.Nop);
        Emit(code, shape, work, systemObject, systemException, invalidOperation);
        int bodyOffset = stream.AddMethodBody(code, maxStack: 2);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, MethodImplAttributes.IL,
            metadata.GetOrAddString("M"), voidHandle, bodyOffset, default);

        var image = new BlobBuilder();
        new ManagedPEBuilder(
                new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll | Characteristics.ExecutableImage),
                new MetadataRootBuilder(metadata),
                bodies)
            .Serialize(image);
        byte[] bytes = image.ToArray();
        using var pe = new PEReader(new MemoryStream(bytes));
        int rva = pe.GetMetadataReader().GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(2)).RelativeVirtualAddress;
        return (bytes, rva);
    }

    static void Emit(
        InstructionEncoder il,
        Shape shape,
        MethodDefinitionHandle work,
        TypeReferenceHandle systemObject,
        TypeReferenceHandle systemException,
        TypeReferenceHandle invalidOperation)
    {
        LabelHandle tryStart = il.DefineLabel();
        LabelHandle tryEnd = il.DefineLabel();
        LabelHandle handlerStart = il.DefineLabel();
        LabelHandle handlerEnd = il.DefineLabel();
        LabelHandle end = il.DefineLabel();
        switch (shape)
        {
            case Shape.CatchInvalidOperation:
            case Shape.CatchException:
            case Shape.CatchAllObject:
            case Shape.CatchInvalidOperationLongBranchBefore:
            {
                if (shape == Shape.CatchInvalidOperationLongBranchBefore)
                {
                    LabelHandle next = il.DefineLabel();
                    il.Branch(ILOpCode.Br, next);
                    il.MarkLabel(next);
                }
                else
                {
                    LabelHandle next = il.DefineLabel();
                    il.Branch(ILOpCode.Br_s, next);
                    il.MarkLabel(next);
                }
                il.MarkLabel(tryStart);
                il.Call(work);
                il.Branch(ILOpCode.Leave_s, end);
                il.MarkLabel(tryEnd);
                il.MarkLabel(handlerStart);
                il.OpCode(ILOpCode.Pop);
                il.Branch(ILOpCode.Leave_s, end);
                il.MarkLabel(handlerEnd);
                il.MarkLabel(end);
                il.OpCode(ILOpCode.Ret);
                EntityHandle catchType = shape switch
                {
                    Shape.CatchException => systemException,
                    Shape.CatchAllObject => systemObject,
                    _ => invalidOperation,
                };
                il.ControlFlowBuilder!.AddCatchRegion(tryStart, tryEnd, handlerStart, handlerEnd, catchType);
                break;
            }
            case Shape.CatchInvalidOperationWiderTry:
            {
                // The same instructions as CatchInvalidOperation; the try
                // starts one instruction earlier, at the br.s.
                il.MarkLabel(tryStart);
                LabelHandle next = il.DefineLabel();
                il.Branch(ILOpCode.Br_s, next);
                il.MarkLabel(next);
                il.Call(work);
                il.Branch(ILOpCode.Leave_s, end);
                il.MarkLabel(tryEnd);
                il.MarkLabel(handlerStart);
                il.OpCode(ILOpCode.Pop);
                il.Branch(ILOpCode.Leave_s, end);
                il.MarkLabel(handlerEnd);
                il.MarkLabel(end);
                il.OpCode(ILOpCode.Ret);
                il.ControlFlowBuilder!.AddCatchRegion(tryStart, tryEnd, handlerStart, handlerEnd, invalidOperation);
                break;
            }
            case Shape.Finally:
            case Shape.Fault:
            {
                LabelHandle next = il.DefineLabel();
                il.Branch(ILOpCode.Br_s, next);
                il.MarkLabel(next);
                il.MarkLabel(tryStart);
                il.Call(work);
                il.Branch(ILOpCode.Leave_s, end);
                il.MarkLabel(tryEnd);
                il.MarkLabel(handlerStart);
                il.OpCode(ILOpCode.Nop);
                il.OpCode(ILOpCode.Endfinally);
                il.MarkLabel(handlerEnd);
                il.MarkLabel(end);
                il.OpCode(ILOpCode.Ret);
                if (shape == Shape.Finally)
                    il.ControlFlowBuilder!.AddFinallyRegion(tryStart, tryEnd, handlerStart, handlerEnd);
                else
                    il.ControlFlowBuilder!.AddFaultRegion(tryStart, tryEnd, handlerStart, handlerEnd);
                break;
            }
            case Shape.NestedCatchInFinally:
            {
                LabelHandle innerTryStart = il.DefineLabel();
                LabelHandle innerTryEnd = il.DefineLabel();
                LabelHandle innerHandlerStart = il.DefineLabel();
                LabelHandle innerHandlerEnd = il.DefineLabel();
                il.MarkLabel(tryStart);
                il.MarkLabel(innerTryStart);
                il.Call(work);
                il.Branch(ILOpCode.Leave_s, end);
                il.MarkLabel(innerTryEnd);
                il.MarkLabel(innerHandlerStart);
                il.OpCode(ILOpCode.Pop);
                il.Branch(ILOpCode.Leave_s, end);
                il.MarkLabel(innerHandlerEnd);
                il.MarkLabel(tryEnd);
                il.MarkLabel(handlerStart);
                il.OpCode(ILOpCode.Endfinally);
                il.MarkLabel(handlerEnd);
                il.MarkLabel(end);
                il.OpCode(ILOpCode.Ret);
                il.ControlFlowBuilder!.AddCatchRegion(innerTryStart, innerTryEnd, innerHandlerStart, innerHandlerEnd, invalidOperation);
                il.ControlFlowBuilder!.AddFinallyRegion(tryStart, tryEnd, handlerStart, handlerEnd);
                break;
            }
            case Shape.FilterOnInvalidOperation:
            {
                LabelHandle filterStart = il.DefineLabel();
                LabelHandle next = il.DefineLabel();
                il.Branch(ILOpCode.Br_s, next);
                il.MarkLabel(next);
                il.MarkLabel(tryStart);
                il.Call(work);
                il.Branch(ILOpCode.Leave_s, end);
                il.MarkLabel(tryEnd);
                il.MarkLabel(filterStart);
                il.OpCode(ILOpCode.Isinst);
                il.Token(invalidOperation);
                il.OpCode(ILOpCode.Ldnull);
                il.OpCode(ILOpCode.Cgt_un);
                il.OpCode(ILOpCode.Endfilter);
                il.MarkLabel(handlerStart);
                il.OpCode(ILOpCode.Pop);
                il.Branch(ILOpCode.Leave_s, end);
                il.MarkLabel(handlerEnd);
                il.MarkLabel(end);
                il.OpCode(ILOpCode.Ret);
                il.ControlFlowBuilder!.AddFilterRegion(tryStart, tryEnd, handlerStart, handlerEnd, filterStart);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }
    }
}
