using DotnetInspector.Fixtures;
using ILInspector.Instructions;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Analysis.Tests;

public class IlBodyDiffTests
{
    [Fact]
    public void Compare_ExactBodies_HasNoDifferences()
    {
        var left = MethodInstructions.Decode([0x00, 0x2a], 2, []);
        var right = MethodInstructions.Decode([0x00, 0x2a], 2, []);

        var diff = IlBodyDiff.Compare(left, right);

        Assert.True(diff.IsExact);
        Assert.Null(diff.Failure);
        Assert.Empty(diff.Rows);
    }

    [Fact]
    public void Compare_InsertedInstruction_AlignsUnchangedSuffix()
    {
        var left = MethodInstructions.Decode([0x00, 0x2a], 2, []);
        var right = MethodInstructions.Decode([0x00, 0x00, 0x2a], 3, []);

        var diff = IlBodyDiff.Compare(left, right);

        Assert.False(diff.IsExact);
        var added = Assert.Single(diff.Rows, row => row.Kind == IlDiffKind.Add);
        Assert.Equal(0x0001, added.Operation.Offset);
        Assert.Equal("nop", added.Operation.OpcodeFamily);
        Assert.DoesNotContain(diff.Rows, row => row.Kind == IlDiffKind.Remove);
    }

    [Fact]
    public void Compare_BranchTargetOffsetShift_DoesNotMarkBranchChanged()
    {
        // br.s targets the final ret in both bodies. The inserted nop shifts the
        // absolute target from IL_0003 to IL_0004, but the branch operation itself
        // is unchanged for this first substrate.
        var left = MethodInstructions.Decode([0x2b, 0x01, 0x00, 0x2a], 4, []);
        var right = MethodInstructions.Decode([0x2b, 0x02, 0x00, 0x00, 0x2a], 5, []);

        var diff = IlBodyDiff.Compare(left, right);

        Assert.False(diff.IsExact);
        Assert.Single(diff.Rows);
        Assert.Equal(IlDiffKind.Add, diff.Rows[0].Kind);
        Assert.Equal("nop", diff.Rows[0].Operation.OpcodeFamily);
    }

    [Fact]
    public void Compare_BranchTargetRetarget_ReportsChangedBranch()
    {
        // Same opcode sequence, but the first branch retargets from IL_0005 to
        // IL_0003. LCS can align the branch instructions, then target validation
        // must still report the control-flow change.
        var left = MethodInstructions.Decode([0x2b, 0x03, 0x00, 0x2a, 0x00, 0x2a], 6, []);
        var right = MethodInstructions.Decode([0x2b, 0x01, 0x00, 0x2a, 0x00, 0x2a], 6, []);

        var diff = IlBodyDiff.Compare(left, right);

        Assert.Collection(
            diff.Rows,
            removed =>
            {
                Assert.Equal(IlDiffKind.Remove, removed.Kind);
                Assert.Equal(0, removed.Operation.Offset);
                Assert.Equal("br", removed.Operation.OpcodeFamily);
                Assert.Equal("IL_0005", removed.Operation.Operand?.Value);
            },
            added =>
            {
                Assert.Equal(IlDiffKind.Add, added.Kind);
                Assert.Equal(0, added.Operation.Offset);
                Assert.Equal("br", added.Operation.OpcodeFamily);
                Assert.Equal("IL_0003", added.Operation.Operand?.Value);
            });
    }

    [Fact]
    public void Compare_BranchTargetInstructionChanged_DoesNotCascadeToBranch()
    {
        var left = MethodInstructions.Decode([0x2b, 0x00, 0x16, 0x2a], 4, []);
        var right = MethodInstructions.Decode([0x2b, 0x00, 0x17, 0x2a], 4, []);

        var diff = IlBodyDiff.Compare(left, right);

        Assert.Collection(
            diff.Rows,
            removed =>
            {
                Assert.Equal(IlDiffKind.Remove, removed.Kind);
                Assert.Equal("ldc.i4", removed.Operation.OpcodeFamily);
                Assert.Equal("0", removed.Operation.Operand?.Value);
            },
            added =>
            {
                Assert.Equal(IlDiffKind.Add, added.Kind);
                Assert.Equal("ldc.i4", added.Operation.OpcodeFamily);
                Assert.Equal("1", added.Operation.Operand?.Value);
            });
    }

    [Fact]
    public void Compare_CompiledFixtureConstantValueChange_ReportsImmediateChange()
    {
        var diff = DiffFixtureDiff("ConstantValue");

        Assert.Collection(
            diff.Rows,
            removed =>
            {
                Assert.Equal(IlDiffKind.Remove, removed.Kind);
                Assert.Equal("ldc.i4", removed.Operation.OpcodeFamily);
                Assert.Equal("1", removed.Operation.Operand?.Value);
                Assert.Equal("Removed IL operation 'ldc.i4 1'", removed.Message);
            },
            added =>
            {
                Assert.Equal(IlDiffKind.Add, added.Kind);
                Assert.Equal("ldc.i4", added.Operation.OpcodeFamily);
                Assert.Equal("2", added.Operation.Operand?.Value);
                Assert.Equal("Added IL operation 'ldc.i4 2'", added.Message);
            });
    }

    [Fact]
    public void Compare_CompiledFixtureStringTokenChange_ReportsStringOperandChange()
    {
        var diff = DiffFixtureDiff("StringToken");

        Assert.Collection(
            diff.Rows,
            removed =>
            {
                Assert.Equal(IlDiffKind.Remove, removed.Kind);
                Assert.Equal("ldstr", removed.Operation.OpcodeFamily);
                Assert.Equal("string \"alpha\"", removed.Operation.Operand?.Value);
            },
            added =>
            {
                Assert.Equal(IlDiffKind.Add, added.Kind);
                Assert.Equal("ldstr", added.Operation.OpcodeFamily);
                Assert.Equal("string \"beta\"", added.Operation.Operand?.Value);
            });
    }

    [Fact]
    public void Compare_CompiledFixtureCallTokenChange_ReportsMemberOperandChange()
    {
        var diff = DiffFixtureDiff("CallToken");

        Assert.Collection(
            diff.Rows,
            removed =>
            {
                Assert.Equal(IlDiffKind.Remove, removed.Kind);
                Assert.Equal("call", removed.Operation.OpcodeFamily);
                Assert.Contains("::Abs(", removed.Operation.Operand?.Value, StringComparison.Ordinal);
            },
            added =>
            {
                Assert.Equal(IlDiffKind.Add, added.Kind);
                Assert.Equal("call", added.Operation.OpcodeFamily);
                Assert.Contains("::Sign(", added.Operation.Operand?.Value, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void Compare_WithScopeNormalization_CompiledFixtureCallTargetChange_IsNotExact()
    {
        var diff = DiffFixtureDiff(
            "CallToken",
            IlBodyDiffOptions.NormalizeCurrentAssemblyScope
            | IlBodyDiffOptions.NormalizePlatformAssemblyScopes);

        Assert.False(diff.IsExact);
        Assert.Contains(diff.Rows, row => row.Operation.Operand?.Value.Contains("::Abs(", StringComparison.Ordinal) == true);
        Assert.Contains(diff.Rows, row => row.Operation.Operand?.Value.Contains("::Sign(", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Compare_CompiledFixtureFieldTokenChange_ReportsFieldOperandChanges()
    {
        var diff = DiffFixtureDiff("FieldToken");

        AssertTokenOperandPair(diff, "stfld", "::InstanceA", "::InstanceB");
        AssertTokenOperandPair(diff, "ldfld", "::InstanceA", "::InstanceB");
        AssertTokenOperandPair(diff, "stsfld", "::StaticA", "::StaticB");
        AssertTokenOperandPair(diff, "ldsfld", "::StaticA", "::StaticB");
    }

    [Fact]
    public void Compare_CompiledFixtureTypeTokenChange_ReportsTypeOperandChanges()
    {
        var diff = DiffFixtureDiff("TypeTokenShapes");

        AssertTokenOperandPair(diff, "ldtoken", "TypeTokenA", "TypeTokenB");
        AssertTokenOperandPair(diff, "isinst", "TypeTokenA", "TypeTokenB");
        AssertTokenOperandPair(diff, "castclass", "TypeTokenA", "TypeTokenB");
        AssertTokenOperandPair(diff, "newarr", "TypeTokenA", "TypeTokenB");
    }

    [Fact]
    public void Compare_CompiledFixtureBoxTokenChange_ReportsBoxAndUnboxOperandChanges()
    {
        var diff = DiffFixtureDiff("BoxToken");

        AssertTokenOperandPair(diff, "box", "System.Int16", "System.Int64");
        AssertTokenOperandPair(diff, "unbox.any", "System.Int16", "System.Int64");
    }

    [Fact]
    public void Compare_CompiledFixtureTokenOpcodes_ReportsTokenVariants()
    {
        var typeDiff = DiffFixtureDiff("LdTokenType");
        AssertTokenOperandPair(typeDiff, "ldtoken", "TypeTokenA", "TypeTokenB");

        var fieldDiff = DiffFixtureDiff("LdTokenField");
        var removedFieldToken = SingleTokenOperand(fieldDiff, IlDiffKind.Remove, "ldtoken", "field ");
        var addedFieldToken = SingleTokenOperand(fieldDiff, IlDiffKind.Add, "ldtoken", "field ");
        Assert.Contains("<PrivateImplementationDetails>", removedFieldToken.Value, StringComparison.Ordinal);
        Assert.Contains("<PrivateImplementationDetails>", addedFieldToken.Value, StringComparison.Ordinal);
        Assert.NotEqual(removedFieldToken.Value, addedFieldToken.Value);

        var methodDiff = DiffFixtureDiff("MethodToken");
        AssertTokenOperandPair(methodDiff, "ldftn", "::TokenTargetA(", "::TokenTargetB(");
    }

    [Fact]
    public void Compare_MethodReferenceVarargSentinelChange_ReportsOperandChange()
    {
        var oldImage = BuildSyntheticEntryImage(
            SyntheticTokenInstruction.Call,
            (metadata, parentType) => metadata.AddMemberReference(
                parentType,
                metadata.GetOrAddString("M"),
                AddVoidMethodSignature(
                    metadata,
                    SignatureCallingConvention.Default,
                    parameterCount: 2,
                    encodeParameters: parameters =>
                    {
                        parameters.AddParameter().Type().Int32();
                        parameters.AddParameter().Type().Int32();
                    })));
        var newImage = BuildSyntheticEntryImage(
            SyntheticTokenInstruction.Call,
            (metadata, parentType) => metadata.AddMemberReference(
                parentType,
                metadata.GetOrAddString("M"),
                AddVoidMethodSignature(
                    metadata,
                    SignatureCallingConvention.VarArgs,
                    parameterCount: 2,
                    encodeParameters: parameters =>
                    {
                        parameters.AddParameter().Type().Int32();
                        var varargs = parameters.StartVarArgs();
                        varargs.AddParameter().Type().Int32();
                    })));

        var diff = SyntheticEntryDiff(oldImage, newImage);

        var removed = SingleTokenOperand(diff, IlDiffKind.Remove, "call", "::M(");
        var added = SingleTokenOperand(diff, IlDiffKind.Add, "call", "::M(");
        Assert.DoesNotContain("vararg", removed.Value, StringComparison.Ordinal);
        Assert.Contains("(int32, int32)", removed.Value, StringComparison.Ordinal);
        Assert.Contains("vararg void", added.Value, StringComparison.Ordinal);
        Assert.Contains("(int32, ..., int32)", added.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_MethodReferenceVarargSentinelSplitChange_ReportsOperandChange()
    {
        var oldImage = BuildSyntheticEntryImage(
            SyntheticTokenInstruction.Call,
            (metadata, parentType) => metadata.AddMemberReference(
                parentType,
                metadata.GetOrAddString("M"),
                AddVoidMethodSignature(
                    metadata,
                    SignatureCallingConvention.VarArgs,
                    parameterCount: 2,
                    encodeParameters: parameters =>
                    {
                        parameters.AddParameter().Type().Int32();
                        var varargs = parameters.StartVarArgs();
                        varargs.AddParameter().Type().Int32();
                    })));
        var newImage = BuildSyntheticEntryImage(
            SyntheticTokenInstruction.Call,
            (metadata, parentType) => metadata.AddMemberReference(
                parentType,
                metadata.GetOrAddString("M"),
                AddVoidMethodSignature(
                    metadata,
                    SignatureCallingConvention.VarArgs,
                    parameterCount: 2,
                    encodeParameters: parameters =>
                    {
                        parameters.AddParameter().Type().Int32();
                        parameters.AddParameter().Type().Int32();
                        _ = parameters.StartVarArgs();
                    })));

        var diff = SyntheticEntryDiff(oldImage, newImage);

        _ = SingleTokenOperand(diff, IlDiffKind.Remove, "call", "(int32, ..., int32)");
        _ = SingleTokenOperand(diff, IlDiffKind.Add, "call", "(int32, int32, ...)");
    }

    [Fact]
    public void Compare_FunctionPointerCallingConventionChange_ReportsOperandChange()
    {
        var oldImage = BuildSyntheticEntryImage(
            SyntheticTokenInstruction.Call,
            (metadata, parentType) => metadata.AddMemberReference(
                parentType,
                metadata.GetOrAddString("M"),
                AddFunctionPointerParameterSignature(metadata, SignatureCallingConvention.CDecl)));
        var newImage = BuildSyntheticEntryImage(
            SyntheticTokenInstruction.Call,
            (metadata, parentType) => metadata.AddMemberReference(
                parentType,
                metadata.GetOrAddString("M"),
                AddFunctionPointerParameterSignature(metadata, SignatureCallingConvention.StdCall)));

        var diff = SyntheticEntryDiff(oldImage, newImage);

        _ = SingleTokenOperand(diff, IlDiffKind.Remove, "call", "method unmanaged[Cdecl] void *()");
        _ = SingleTokenOperand(diff, IlDiffKind.Add, "call", "method unmanaged[Stdcall] void *()");
    }

    [Fact]
    public void Compare_FunctionPointerUnmanagedModifierConventionChange_ReportsOperandChange()
    {
        var oldImage = BuildSyntheticEntryImage(
            SyntheticTokenInstruction.Call,
            (metadata, parentType) => metadata.AddMemberReference(
                parentType,
                metadata.GetOrAddString("M"),
                AddUnmanagedFunctionPointerParameterSignature(metadata, "CallConvCdecl")));
        var newImage = BuildSyntheticEntryImage(
            SyntheticTokenInstruction.Call,
            (metadata, parentType) => metadata.AddMemberReference(
                parentType,
                metadata.GetOrAddString("M"),
                AddUnmanagedFunctionPointerParameterSignature(metadata, "CallConvStdcall")));

        var diff = SyntheticEntryDiff(oldImage, newImage);

        var removed = SingleTokenOperand(diff, IlDiffKind.Remove, "call", "CallConvCdecl");
        var added = SingleTokenOperand(diff, IlDiffKind.Add, "call", "CallConvStdcall");
        Assert.Contains("method unmanaged", removed.Value, StringComparison.Ordinal);
        Assert.Contains("method unmanaged", added.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_MethodTokenGenericArityChange_ReportsOperandChange()
    {
        var oldImage = BuildGenericMethodTokenImage(genericArity: 1);
        var newImage = BuildGenericMethodTokenImage(genericArity: 2);

        var diff = SyntheticEntryDiff(oldImage, newImage);

        _ = SingleTokenOperand(diff, IlDiffKind.Remove, "ldtoken", "::M`1(");
        _ = SingleTokenOperand(diff, IlDiffKind.Add, "ldtoken", "::M`2(");
    }

    [Fact]
    public void Compare_AssemblyReferenceVersionChange_ReportsOperandChange()
    {
        var oldImage = BuildAssemblyReferenceCallImage(new Version(1, 0, 0, 0));
        var newImage = BuildAssemblyReferenceCallImage(new Version(2, 0, 0, 0));

        var diff = SyntheticEntryDiff(oldImage, newImage);

        _ = SingleTokenOperand(diff, IlDiffKind.Remove, "call", "[Dep, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null]N.T::M(");
        _ = SingleTokenOperand(diff, IlDiffKind.Add, "call", "[Dep, Version=2.0.0.0, Culture=neutral, PublicKeyToken=null]N.T::M(");
    }

    // Malformed metadata can encode a TypeReference resolution scope or a nested-type chain that
    // points back at itself. The IL-diff operand/signature name climbs would then recurse until an
    // *uncatchable* StackOverflowException — which MetadataOperandResolver's try/catch cannot
    // intercept — so these assert the climbs terminate instead of overflowing the test runner.
    [Fact]
    public void Compare_SelfReferentialTypeReferenceOperand_DoesNotStackOverflow()
    {
        var image = BuildSyntheticEntryImage(
            SyntheticTokenInstruction.LdToken,
            (metadata, _) => metadata.AddTypeReference(
                // ResolutionScope points at this very TypeReference row (token 1) -> a cycle.
                MetadataTokens.TypeReferenceHandle(1),
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Loop")));

        var diff = SyntheticEntryDiff(image, image);

        Assert.True(diff.IsExact);
    }

    [Fact]
    public void Compare_SelfNestedTypeDefinitionOperand_DoesNotStackOverflow()
    {
        var image = BuildSyntheticEntryImage(
            SyntheticTokenInstruction.LdToken,
            (metadata, _) =>
            {
                var loop = metadata.AddTypeDefinition(
                    TypeAttributes.NestedPublic,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("Loop"),
                    baseType: default,
                    fieldList: MetadataTokens.FieldDefinitionHandle(1),
                    methodList: MetadataTokens.MethodDefinitionHandle(1));
                // Declares the type as nested inside itself -> GetDeclaringType() cycles.
                metadata.AddNestedType(loop, loop);
                return loop;
            });

        var diff = SyntheticEntryDiff(image, image);

        Assert.True(diff.IsExact);
    }

    [Fact]
    public void Compare_SelfReferentialTypeReferenceInTypeSpec_DoesNotStackOverflow()
    {
        var image = BuildSyntheticEntryImage(
            SyntheticTokenInstruction.LdToken,
            (metadata, _) =>
            {
                metadata.AddTypeReference(
                    MetadataTokens.TypeReferenceHandle(1),
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("Loop"));
                // ELEMENT_TYPE_CLASS (0x12) of the cyclic TypeRef (TypeDefOrRef coded token
                // (row 1 << 2) | tag 1 = 0x05) -> the signature provider climbs the same cycle.
                var spec = new BlobBuilder();
                spec.WriteByte(0x12);
                spec.WriteByte(0x05);
                return metadata.AddTypeSpecification(metadata.GetOrAddBlob(spec));
            });

        var diff = SyntheticEntryDiff(image, image);

        Assert.True(diff.IsExact);
    }

    [Fact]
    public void Compare_SelfNestedTypeDefinitionInTypeSpec_DoesNotStackOverflow()
    {
        var image = BuildSyntheticEntryImage(
            SyntheticTokenInstruction.LdToken,
            (metadata, _) =>
            {
                var loop = metadata.AddTypeDefinition(
                    TypeAttributes.NestedPublic,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("Loop"),
                    baseType: default,
                    fieldList: MetadataTokens.FieldDefinitionHandle(1),
                    methodList: MetadataTokens.MethodDefinitionHandle(1));
                metadata.AddNestedType(loop, loop);
                // ELEMENT_TYPE_CLASS (0x12) of the self-nested TypeDef (coded token
                // (row 2 << 2) | tag 0 = 0x08).
                var spec = new BlobBuilder();
                spec.WriteByte(0x12);
                spec.WriteByte(0x08);
                return metadata.AddTypeSpecification(metadata.GetOrAddBlob(spec));
            });

        var diff = SyntheticEntryDiff(image, image);

        Assert.True(diff.IsExact);
    }

    [Fact]
    public void Compare_CompiledFixtureSwitchRetarget_ReportsChangedSwitch()
    {
        var diff = DiffFixtureDiff("SwitchRetarget");

        var removedSwitchTargets = SingleOperand(diff, IlDiffKind.Remove, "switch", IlOperandIdentityKind.SwitchTargets);
        var addedSwitchTargets = SingleOperand(diff, IlDiffKind.Add, "switch", IlOperandIdentityKind.SwitchTargets);
        Assert.Equal(
            removedSwitchTargets.Value.Split(',').Length,
            addedSwitchTargets.Value.Split(',').Length);
        Assert.NotEqual(removedSwitchTargets.Value, addedSwitchTargets.Value);
    }

    [Fact]
    public void Compare_CompiledFixtureTryCatchAvailability_SurfacesOperationRows()
    {
        Assert.Empty(ExceptionRegionShapes(FixtureCatalog.DiffPair.Old, "TryCatchAvailability"));
        var newRegion = Assert.Single(ExceptionRegionShapes(FixtureCatalog.DiffPair.New, "TryCatchAvailability"));
        Assert.Equal(ExceptionRegionKind.Catch, newRegion.Kind);

        var diff = DiffFixtureDiff("TryCatchAvailability");

        Assert.False(diff.IsExact);
        Assert.Null(diff.Failure);
        Assert.Contains(diff.Rows, row => row.Operation.OpcodeFamily == "leave");
        Assert.Contains(diff.Rows, row => row.Operation.OpcodeFamily == "pop");
    }

    [Fact]
    public void Compare_CompiledFixtureFinallyAvailability_SurfacesOperationRows()
    {
        Assert.Empty(ExceptionRegionShapes(FixtureCatalog.DiffPair.Old, "FinallyAvailability"));
        var newRegion = Assert.Single(ExceptionRegionShapes(FixtureCatalog.DiffPair.New, "FinallyAvailability"));
        Assert.Equal(ExceptionRegionKind.Finally, newRegion.Kind);

        var diff = DiffFixtureDiff("FinallyAvailability");

        Assert.False(diff.IsExact);
        Assert.Null(diff.Failure);
        Assert.Contains(diff.Rows, row => row.Operation.OpcodeFamily == "endfinally");
    }

    [Fact]
    public void Compare_CompiledFixtureTryCatchRegionShape_SurfacesOperationRows()
    {
        var oldRegion = Assert.Single(ExceptionRegionShapes(FixtureCatalog.DiffPair.Old, "TryCatchRegionShape"));
        var newRegion = Assert.Single(ExceptionRegionShapes(FixtureCatalog.DiffPair.New, "TryCatchRegionShape"));
        Assert.Equal(ExceptionRegionKind.Catch, oldRegion.Kind);
        Assert.Equal(ExceptionRegionKind.Catch, newRegion.Kind);
        Assert.NotEqual(oldRegion.TryLength, newRegion.TryLength);

        var diff = DiffFixtureDiff("TryCatchRegionShape");

        Assert.False(diff.IsExact);
        Assert.Null(diff.Failure);
        Assert.Contains(diff.Rows, row => row.Operation.OpcodeFamily == "call");
    }

    [Fact]
    public void Compare_CompiledFixtureRepeatedCallOneOccurrence_ReportsOnlyChangedCall()
    {
        var diff = DiffFixtureDiff("RepeatedCallOneOccurrence");

        var callRows = diff.Rows
            .Where(row => row.Operation.OpcodeFamily == "call")
            .ToArray();
        Assert.Collection(
            callRows,
            removed =>
            {
                Assert.Equal(IlDiffKind.Remove, removed.Kind);
                Assert.Contains("::Abs(", removed.Operation.Operand?.Value, StringComparison.Ordinal);
            },
            added =>
            {
                Assert.Equal(IlDiffKind.Add, added.Kind);
                Assert.Contains("::Sign(", added.Operation.Operand?.Value, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void Compare_CompiledFixtureSlotLocalShapeNearMiss_SurfacesRawSlotRows()
    {
        var diff = DiffFixtureDiff("SlotLocalShapeNearMiss");

        Assert.False(diff.IsExact);
        Assert.Null(diff.Failure);
        var localRows = diff.Rows
            .Where(IsRawLocalRow)
            .ToArray();
        Assert.Collection(
            localRows,
            removed => Assert.Equal(IlDiffKind.Remove, removed.Kind),
            added => Assert.Equal(IlDiffKind.Add, added.Kind));
    }

    [Fact]
    public void Printer_CompiledFixtureTokenRows_PreservesTypedOperandDisplay()
    {
        var diff = DiffFixtureDiff("FieldToken");
        var source = diff.Rows.Single(row =>
            row.Kind == IlDiffKind.Remove
            && row.Operation.OpcodeFamily == "stfld"
            && row.Operation.Operand?.Value.Contains("::InstanceA", StringComparison.Ordinal) == true);

        var display = IlDiffPrinter.ToDisplayRow(source);

        Assert.Equal(source.HunkId, display.HunkId);
        Assert.Equal(source.Kind, display.Kind);
        Assert.Equal(source.Operation.Offset, display.RawOffset);
        Assert.Equal(source.Operation.OpcodeFamily, display.OpcodeFamily);
        Assert.Equal(IlOperandIdentityKind.Token, display.OperandKind);
        Assert.Equal(source.Operation.Operand?.Value, display.OperandValue);
        Assert.Equal(source.Operation.Display, display.Operation);
        Assert.Equal(source.Message, display.Message);
        Assert.Contains("stfld", display.UnifiedLine, StringComparison.Ordinal);
        Assert.Contains("::InstanceA", display.UnifiedLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_TokenOperandWithoutMetadata_FailsClosed()
    {
        var left = DiffFixtureMethod(FixtureCatalog.DiffPair.Old, "StringToken");
        var right = DiffFixtureMethod(FixtureCatalog.DiffPair.New, "StringToken");

        var diff = IlBodyDiff.Compare(left, right);

        Assert.False(diff.IsExact);
        Assert.NotNull(diff.Failure);
        Assert.Contains("requires a MetadataReader-backed comparison", diff.Failure, StringComparison.Ordinal);
        var failure = Assert.Single(diff.FailureRows);
        Assert.Equal(IlDiffFailureKind.TokenResolutionFailure, failure.Kind);
        Assert.Equal("old", failure.Side);
        Assert.Contains("requires a MetadataReader-backed comparison", failure.Message, StringComparison.Ordinal);
        Assert.Empty(diff.Rows);
    }

    [Fact]
    public void Compare_CompiledFixtureMultipleHunks_AssignsSeparateHunkIds()
    {
        var diff = DiffFixtureDiff("MultipleHunks");

        Assert.Collection(
            diff.Rows,
            firstRemoved =>
            {
                Assert.Equal(0, firstRemoved.HunkId);
                Assert.Equal(IlDiffKind.Remove, firstRemoved.Kind);
                Assert.Equal("ldc.i4", firstRemoved.Operation.OpcodeFamily);
                Assert.Equal("1", firstRemoved.Operation.Operand?.Value);
            },
            firstAdded =>
            {
                Assert.Equal(0, firstAdded.HunkId);
                Assert.Equal(IlDiffKind.Add, firstAdded.Kind);
                Assert.Equal("ldc.i4", firstAdded.Operation.OpcodeFamily);
                Assert.Equal("2", firstAdded.Operation.Operand?.Value);
            },
            secondRemoved =>
            {
                Assert.Equal(1, secondRemoved.HunkId);
                Assert.Equal(IlDiffKind.Remove, secondRemoved.Kind);
                Assert.Equal("ldc.i4", secondRemoved.Operation.OpcodeFamily);
                Assert.Equal("3", secondRemoved.Operation.Operand?.Value);
            },
            secondAdded =>
            {
                Assert.Equal(1, secondAdded.HunkId);
                Assert.Equal(IlDiffKind.Add, secondAdded.Kind);
                Assert.Equal("ldc.i4", secondAdded.Operation.OpcodeFamily);
                Assert.Equal("4", secondAdded.Operation.Operand?.Value);
            });
    }

    [Fact]
    public void Compare_CompiledFixtureBranchTargetOffsetShift_DoesNotMarkBranchChanged()
    {
        var diff = DiffFixtureDiff("BranchTargetOffsetShift");

        Assert.NotEmpty(diff.Rows);
        Assert.Contains(diff.Rows, row => row.Kind == IlDiffKind.Add && row.Operation.OpcodeFamily == "call");
        Assert.DoesNotContain(diff.Rows, IsBranchRow);
    }

    [Fact]
    public void Compare_CompiledFixtureBranchRetarget_ReportsChangedBranch()
    {
        var diff = DiffFixtureDiff("BranchRetarget");

        Assert.Contains(diff.Rows, row => row.Kind == IlDiffKind.Remove && IsBranchRow(row));
        Assert.Contains(diff.Rows, row => row.Kind == IlDiffKind.Add && IsBranchRow(row));
    }

    [Fact]
    public void Compare_RetargetedBranchAndRemovedInstruction_ReportsInProgramOrder()
    {
        var left = MethodInstructions.Decode([0x2b, 0x03, 0x00, 0x16, 0x2a, 0x17, 0x2a], 7, []);
        var right = MethodInstructions.Decode([0x2b, 0x00, 0x16, 0x2a, 0x17, 0x2a], 6, []);

        var diff = IlBodyDiff.Compare(left, right);

        Assert.Collection(
            diff.Rows,
            removedBranch =>
            {
                Assert.Equal(IlDiffKind.Remove, removedBranch.Kind);
                Assert.Equal(0, removedBranch.Operation.Offset);
                Assert.Equal("br", removedBranch.Operation.OpcodeFamily);
                Assert.Equal("IL_0005", removedBranch.Operation.Operand?.Value);
                Assert.Equal("Removed IL branch 'br IL_0005'", removedBranch.Message);
            },
            addedBranch =>
            {
                Assert.Equal(IlDiffKind.Add, addedBranch.Kind);
                Assert.Equal(0, addedBranch.Operation.Offset);
                Assert.Equal("br", addedBranch.Operation.OpcodeFamily);
                Assert.Equal("IL_0002", addedBranch.Operation.Operand?.Value);
                Assert.Equal("Added IL branch 'br IL_0002'", addedBranch.Message);
            },
            removedNop =>
            {
                Assert.Equal(IlDiffKind.Remove, removedNop.Kind);
                Assert.Equal(2, removedNop.Operation.Offset);
                Assert.Equal("nop", removedNop.Operation.OpcodeFamily);
            });
    }

    [Fact]
    public void Compare_MalformedBody_ReportsFailure()
    {
        var malformed = MethodInstructions.Decode([0xfe], 1, []);
        var valid = MethodInstructions.Decode([0x2a], 1, []);

        var diff = IlBodyDiff.Compare(malformed, valid);

        Assert.False(diff.IsExact);
        Assert.NotNull(diff.Failure);
        var failure = Assert.Single(diff.FailureRows);
        Assert.Equal(IlDiffFailureKind.DecodeFailure, failure.Kind);
        Assert.Equal("old", failure.Side);
    }

    static bool IsBranchRow(IlDiffRow row)
        => row.Operation.OpcodeFamily.StartsWith("br", StringComparison.Ordinal);

    static bool IsRawLocalRow(IlDiffRow row)
        => row.Operation.Operand?.Kind == IlOperandIdentityKind.Slot
            || row.Operation.OpcodeFamily.StartsWith("ldloc.", StringComparison.Ordinal)
            || row.Operation.OpcodeFamily.StartsWith("stloc.", StringComparison.Ordinal);

    static void AssertTokenOperandPair(
        IlBodyDiffResult diff,
        string opcodeFamily,
        string removedOperandFragment,
        string addedOperandFragment)
    {
        _ = SingleTokenOperand(diff, IlDiffKind.Remove, opcodeFamily, removedOperandFragment);
        _ = SingleTokenOperand(diff, IlDiffKind.Add, opcodeFamily, addedOperandFragment);
    }

    static IlOperandIdentity SingleTokenOperand(
        IlBodyDiffResult diff,
        IlDiffKind kind,
        string opcodeFamily,
        string operandFragment)
        => SingleOperand(diff, kind, opcodeFamily, IlOperandIdentityKind.Token, operandFragment);

    static IlOperandIdentity SingleOperand(
        IlBodyDiffResult diff,
        IlDiffKind kind,
        string opcodeFamily,
        IlOperandIdentityKind operandKind,
        string? operandFragment = null)
    {
        var row = Assert.Single(diff.Rows, row =>
            row.Kind == kind
            && row.Operation.OpcodeFamily == opcodeFamily
            && row.Operation.Operand?.Kind == operandKind
            && (operandFragment is null || row.Operation.Operand.Value.Contains(operandFragment, StringComparison.Ordinal)));
        return Assert.IsType<IlOperandIdentity>(row.Operation.Operand);
    }

    static ExceptionRegionShape[] ExceptionRegionShapes(FixtureDefinition fixture, string name)
    {
        using var stream = File.OpenRead(fixture.AssemblyPath());
        using var peReader = new PEReader(stream);
        var metadataReader = peReader.GetMetadataReader();
        var body = DiffFixtureMethodBody(peReader, metadataReader, name);
        return body.ExceptionRegions
            .Select(region => new ExceptionRegionShape(
                region.Kind,
                region.TryOffset,
                region.TryLength,
                region.HandlerOffset,
                region.HandlerLength))
            .ToArray();
    }

    readonly record struct ExceptionRegionShape(
        ExceptionRegionKind Kind,
        int TryOffset,
        int TryLength,
        int HandlerOffset,
        int HandlerLength);

    static IlBodyDiffResult DiffFixtureDiff(
        string name,
        IlBodyDiffOptions options = IlBodyDiffOptions.None)
    {
        using var oldStream = File.OpenRead(FixtureCatalog.DiffPair.OldAssemblyPath());
        using var newStream = File.OpenRead(FixtureCatalog.DiffPair.NewAssemblyPath());
        using var oldPe = new PEReader(oldStream);
        using var newPe = new PEReader(newStream);
        var oldReader = oldPe.GetMetadataReader();
        var newReader = newPe.GetMetadataReader();
        return IlBodyDiff.Compare(
            oldReader,
            DiffFixtureMethodBody(oldPe, oldReader, name),
            newReader,
            DiffFixtureMethodBody(newPe, newReader, name),
            options);
    }

    static MethodInstructions DiffFixtureMethod(FixtureDefinition fixture, string name)
    {
        using var stream = File.OpenRead(fixture.AssemblyPath());
        using var peReader = new PEReader(stream);
        var metadataReader = peReader.GetMetadataReader();
        return MethodInstructions.Decode(DiffFixtureMethodBody(peReader, metadataReader, name));
    }

    static MethodBodyBlock DiffFixtureMethodBody(PEReader peReader, MetadataReader metadataReader, string name)
    {
        foreach (var handle in metadataReader.MethodDefinitions)
        {
            var method = metadataReader.GetMethodDefinition(handle);
            if (metadataReader.GetString(method.Name) == name)
                return peReader.GetMethodBody(method.RelativeVirtualAddress);
        }

        throw new InvalidOperationException($"Could not find method '{name}'.");
    }

    enum SyntheticTokenInstruction
    {
        Call,
        LdToken,
    }

    static byte[] BuildGenericMethodTokenImage(int genericArity)
        => BuildSyntheticEntryImage(
            SyntheticTokenInstruction.LdToken,
            (metadata, _) =>
            {
                var target = metadata.AddMethodDefinition(
                    MethodAttributes.Public | MethodAttributes.Static,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString("M"),
                    AddVoidMethodSignature(metadata, SignatureCallingConvention.Default, genericParameterCount: genericArity),
                    bodyOffset: 0,
                    parameterList: MetadataTokens.ParameterHandle(1));
                for (int i = 0; i < genericArity; i++)
                {
                    metadata.AddGenericParameter(
                        target,
                        GenericParameterAttributes.None,
                        metadata.GetOrAddString($"T{i}"),
                        i);
                }

                return target;
            });

    static byte[] BuildAssemblyReferenceCallImage(Version dependencyVersion)
        => BuildSyntheticEntryImage(
            SyntheticTokenInstruction.Call,
            (metadata, _) =>
            {
                var dependency = metadata.AddAssemblyReference(
                    metadata.GetOrAddString("Dep"),
                    dependencyVersion,
                    culture: default,
                    publicKeyOrToken: default,
                    flags: default,
                    hashValue: default);
                var type = metadata.AddTypeReference(
                    dependency,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("T"));
                return metadata.AddMemberReference(
                    type,
                    metadata.GetOrAddString("M"),
                    AddVoidMethodSignature(metadata));
            });

    static byte[] BuildSyntheticEntryImage(
        SyntheticTokenInstruction tokenInstruction,
        Func<MetadataBuilder, TypeDefinitionHandle, EntityHandle> addToken)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Synthetic.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Synthetic"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        var type = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var token = addToken(metadata, type);

        var il = new BlobBuilder();
        var instructions = new InstructionEncoder(il, new ControlFlowBuilder());
        switch (tokenInstruction)
        {
            case SyntheticTokenInstruction.Call:
                instructions.Call(token);
                instructions.OpCode(ILOpCode.Ret);
                break;
            case SyntheticTokenInstruction.LdToken:
                instructions.OpCode(ILOpCode.Ldtoken);
                instructions.Token(token);
                instructions.OpCode(ILOpCode.Pop);
                instructions.OpCode(ILOpCode.Ret);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(tokenInstruction), tokenInstruction, null);
        }

        var methodBodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(methodBodies);
        int entryBodyOffset = bodyEncoder.AddMethodBody(instructions, maxStack: 8);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Entry"),
            AddVoidMethodSignature(metadata),
            entryBodyOffset,
            MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static BlobHandle AddFunctionPointerParameterSignature(
        MetadataBuilder metadata,
        SignatureCallingConvention functionPointerConvention)
        => AddVoidMethodSignature(
            metadata,
            SignatureCallingConvention.Default,
            parameterCount: 1,
            encodeParameters: parameters =>
            {
                var pointer = parameters
                    .AddParameter()
                    .Type()
                    .FunctionPointer(functionPointerConvention, FunctionPointerAttributes.None);
                pointer.Parameters(0, returnType => returnType.Void(), _ => { });
            });

    static BlobHandle AddUnmanagedFunctionPointerParameterSignature(
        MetadataBuilder metadata,
        string callConvTypeName)
        => AddVoidMethodSignature(
            metadata,
            SignatureCallingConvention.Default,
            parameterCount: 1,
            encodeParameters: parameters =>
            {
                var systemRuntime = metadata.AddAssemblyReference(
                    metadata.GetOrAddString("System.Runtime"),
                    new Version(11, 0, 0, 0),
                    culture: default,
                    publicKeyOrToken: default,
                    flags: default,
                    hashValue: default);
                var modifier = metadata.AddTypeReference(
                    systemRuntime,
                    metadata.GetOrAddString("System.Runtime.CompilerServices"),
                    metadata.GetOrAddString(callConvTypeName));
                var pointer = parameters
                    .AddParameter()
                    .Type()
                    .FunctionPointer(SignatureCallingConvention.Unmanaged, FunctionPointerAttributes.None);
                pointer.Parameters(
                    0,
                    returnType =>
                    {
                        returnType.CustomModifiers().AddModifier(modifier, isOptional: true);
                        returnType.Void();
                    },
                    _ => { });
            });

    static BlobHandle AddVoidMethodSignature(
        MetadataBuilder metadata,
        SignatureCallingConvention convention = SignatureCallingConvention.Default,
        int genericParameterCount = 0,
        int parameterCount = 0,
        Action<ParametersEncoder>? encodeParameters = null)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(convention, genericParameterCount, isInstanceMethod: false)
            .Parameters(
                parameterCount,
                returnType => returnType.Void(),
                parameters => encodeParameters?.Invoke(parameters));
        return metadata.GetOrAddBlob(signature);
    }

    static IlBodyDiffResult SyntheticEntryDiff(byte[] oldImage, byte[] newImage)
    {
        using var oldStream = new MemoryStream(oldImage);
        using var newStream = new MemoryStream(newImage);
        using var oldPe = new PEReader(oldStream);
        using var newPe = new PEReader(newStream);
        var oldReader = oldPe.GetMetadataReader();
        var newReader = newPe.GetMetadataReader();
        return IlBodyDiff.Compare(
            oldReader,
            DiffFixtureMethodBody(oldPe, oldReader, "Entry"),
            newReader,
            DiffFixtureMethodBody(newPe, newReader, "Entry"));
    }

}
