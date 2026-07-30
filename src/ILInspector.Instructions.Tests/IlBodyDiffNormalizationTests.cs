using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Instructions.Tests;

public class IlBodyDiffNormalizationTests
{
    // Derived from the enum rather than restated, so a normalization added
    // without coverage here still flows into every AllNormalizations test.
    static readonly IlBodyDiffNormalization AllNormalizations =
        Enum.GetValues<IlBodyDiffNormalization>()
            .Aggregate(IlBodyDiffNormalization.None, (all, option) => all | option);

    /// <summary>
    /// Every declared option must be accepted by <see cref="IlBodyDiff.Compare"/>,
    /// which rejects any flag outside its internal <c>SupportedNormalizations</c>
    /// mask. This is the wiring gate: declaring an enum member without adding it
    /// to that mask makes every caller that requests it throw, and this fails
    /// rather than letting the gap surface at a call site.
    /// </summary>
    [Fact]
    public void EveryDeclaredNormalization_IsAcceptedByCompare()
    {
        var body = Decode([0x2a]); // ret

        foreach (var option in Enum.GetValues<IlBodyDiffNormalization>())
        {
            var result = Record.Exception(() => IlBodyDiff.Compare(body, body, option));
            Assert.True(result is null, $"{option} was rejected by Compare: {result?.Message}");
            }
    }

    [Fact]
    public void NormalizeVariableLayout_ToleratesLocalMacroAndSlotLayout()
    {
        var macro = Decode([0x06, 0x2a]); // ldloc.0; ret
        var explicitSlot = Decode([0x11, 0x07, 0x2a]); // ldloc.s 7; ret

        Assert.False(IlBodyDiff.Compare(macro, explicitSlot).IsExact);
        Assert.True(IlBodyDiff.Compare(
            macro,
            explicitSlot,
            IlBodyDiffNormalization.NormalizeVariableLayout).IsExact);
    }

    [Fact]
    public void NormalizeVariableLayout_ToleratesArgumentMacroAndSlotLayout()
    {
        var macro = Decode([0x02, 0x2a]); // ldarg.0; ret
        var explicitSlot = Decode([0x0e, 0x07, 0x2a]); // ldarg.s 7; ret

        Assert.False(IlBodyDiff.Compare(macro, explicitSlot).IsExact);
        Assert.True(IlBodyDiff.Compare(
            macro,
            explicitSlot,
            IlBodyDiffNormalization.NormalizeVariableLayout).IsExact);
    }

    [Fact]
    public void NormalizeVariableLayout_DoesNotFoldArgumentValueAndAddressLoads()
    {
        var valueLoad = Decode([0x02, 0x2a]); // ldarg.0; ret
        var addressLoad = Decode([0x0f, 0x00, 0x2a]); // ldarga.s 0; ret

        var diff = IlBodyDiff.Compare(
            valueLoad,
            addressLoad,
            IlBodyDiffNormalization.NormalizeVariableLayout);

        Assert.False(diff.IsExact);
        Assert.Contains(diff.Rows, row => row.Operation.OpcodeFamily == "ldarg");
        Assert.Contains(diff.Rows, row => row.Operation.OpcodeFamily == "ldarga");
    }

    [Fact]
    public void AllOptions_PreserveNumericOperandChanges()
    {
        var five = Decode([0x1b, 0x2a]); // ldc.i4.5; ret
        var seven = Decode([0x1d, 0x2a]); // ldc.i4.7; ret

        var diff = IlBodyDiff.Compare(five, seven, AllNormalizations);

        Assert.False(diff.IsExact);
        Assert.Equal(IlBodyDiffOutcome.OperandDiff, diff.Outcome);
        Assert.Contains(diff.Rows, row => row.Operation.Operand?.Value == "5");
        Assert.Contains(diff.Rows, row => row.Operation.Operand?.Value == "7");
    }

    [Fact]
    public void AllOptions_PreserveBranchTopologyChanges()
    {
        var firstTarget = Decode([0x2b, 0x03, 0x00, 0x2a, 0x00, 0x2a]);
        var secondTarget = Decode([0x2b, 0x01, 0x00, 0x2a, 0x00, 0x2a]);

        var diff = IlBodyDiff.Compare(firstTarget, secondTarget, AllNormalizations);

        Assert.False(diff.IsExact);
        Assert.Equal(IlBodyDiffOutcome.OperandDiff, diff.Outcome);
        Assert.Equal(2, diff.Rows.Length);
        Assert.All(diff.Rows, row => Assert.Equal("br", row.Operation.OpcodeFamily));
    }

    [Fact]
    public void NormalizePlatformAssemblyScope_ToleratesPlatformReferenceScopeChanges()
    {
        var defaultDiff = CompareCallImages("System.Runtime", "System.Private.CoreLib");
        var normalizedDiff = CompareCallImages(
            "System.Runtime",
            "System.Private.CoreLib",
            IlBodyDiffNormalization.NormalizePlatformAssemblyScope);

        Assert.False(defaultDiff.IsExact);
        Assert.True(normalizedDiff.IsExact);
    }

    [Fact]
    public void CompareStreams_AggregatesOperandDiffOutcome()
    {
        using var oldStream = new MemoryStream(BuildCallImage("Old", "Library.One"));
        using var newStream = new MemoryStream(BuildCallImage("New", "Library.Two"));

        var result = IlAssemblyDiff.CompareStreams(
            oldStream,
            "old.dll",
            newStream,
            "new.dll").Diff;

        Assert.Equal(1, result.ComparedBodyCount);
        Assert.Equal(0, result.PairExactCount);
        Assert.Equal(1, result.PairOperandDiffCount);
        Assert.Equal(0, result.PairOpcodeDiffCount);
        Assert.Equal(0, result.PairUnavailableCount);
        Assert.Equal(1, result.ChangedBodyCount);
        Assert.Equal(IlBodyDiffOutcome.OperandDiff, Assert.Single(result.Examples).Diff.Outcome);
    }

    [Fact]
    public void CompareStreams_AppliesRequestedNormalization()
    {
        using var oldStream = new MemoryStream(BuildCallImage("Old", "System.Runtime"));
        using var newStream = new MemoryStream(BuildCallImage("New", "System.Private.CoreLib"));

        var result = IlAssemblyDiff.CompareStreams(
            oldStream,
            "old.dll",
            newStream,
            "new.dll",
            normalization: IlBodyDiffNormalization.NormalizePlatformAssemblyScope).Diff;

        Assert.Equal(1, result.PairExactCount);
        Assert.Equal(0, result.ChangedBodyCount);
    }

    [Fact]
    public void NormalizePlatformAssemblyScope_PreservesNonPlatformReferenceIdentity()
    {
        var diff = CompareCallImages(
            "Library.One",
            "Library.Two",
            IlBodyDiffNormalization.NormalizePlatformAssemblyScope);

        Assert.False(diff.IsExact);
    }

    [Fact]
    public void NormalizePlatformAssemblyScope_PreservesPlatformLikeStringLiterals()
    {
        var diff = CompareImages(
            BuildStringImage("Old", "[System.Runtime]"),
            BuildStringImage("New", "[System.Private.CoreLib]"),
            IlBodyDiffNormalization.NormalizePlatformAssemblyScope);

        Assert.False(diff.IsExact);
        Assert.Contains(diff.Rows, row => row.Operation.Operand?.Value.Contains("System.Runtime", StringComparison.Ordinal) == true);
        Assert.Contains(diff.Rows, row => row.Operation.Operand?.Value.Contains("System.Private.CoreLib", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void NormalizeCurrentAssemblyScope_ToleratesCurrentAssemblyNameChanges()
    {
        var oldImage = BuildCallImage("System.Old");
        var newImage = BuildCallImage("System.New");

        Assert.False(CompareImages(oldImage, newImage).IsExact);
        Assert.False(CompareImages(
            oldImage,
            newImage,
            IlBodyDiffNormalization.NormalizePlatformAssemblyScope).IsExact);
        Assert.True(CompareImages(
            oldImage,
            newImage,
            IlBodyDiffNormalization.NormalizeCurrentAssemblyScope).IsExact);
    }

    [Fact]
    public void NormalizeCurrentAssemblyScope_ToleratesDirectAndAssemblyRefSelfReferences()
    {
        var directImage = BuildCallImage("System.Runtime");
        var assemblyRefImage = BuildCallImage("System.Runtime", "System.Runtime");

        Assert.False(CompareImages(directImage, assemblyRefImage).IsExact);
        Assert.False(CompareImages(
            directImage,
            assemblyRefImage,
            IlBodyDiffNormalization.NormalizePlatformAssemblyScope).IsExact);
        Assert.True(CompareImages(
            directImage,
            assemblyRefImage,
            IlBodyDiffNormalization.NormalizeCurrentAssemblyScope).IsExact);
        Assert.True(CompareImages(
            directImage,
            assemblyRefImage,
            AllNormalizations).IsExact);
    }

    /// <summary>
    /// The gate for #3503. ``StatementBodyLambdaInsideIf`` failed
    /// <c>FidelityGateTests.NoNewFidelityDiffsBeyondKnownDocket</c> with
    /// identical opcodes and only <c>&lt;&gt;9__103_0</c> vs
    /// <c>&lt;&gt;9__128_0</c> differing, because recompiling a reconstructed
    /// unit renumbers the containing method.
    /// </summary>
    [Theory]
    [InlineData("<>9__103_0", "<>9__128_0")]                          // lambda cache field
    [InlineData("<Run>b__103_0", "<Run>b__128_0")]                    // lambda method
    [InlineData("<Run>g__Local|103_0", "<Run>g__Local|128_0")]        // local function
    [InlineData("<.ctor>b__103_0", "<.ctor>b__128_0")]                // lambda in a constructor
    [InlineData("<Run>g__A__B|103_0", "<Run>g__A__B|128_0")]          // local name containing `__`
    [InlineData("<<Run>b__103_0>b__104_1", "<<Run>b__128_0>b__129_1")] // lambda nested in a lambda
    [InlineData("<<Run>b__103_0>d__1", "<<Run>b__128_0>d__1")]        // enclosing name declined, inner still normalizes
    public void NormalizeSynthesizedMemberOrdinals_ToleratesContainingMethodRenumbering(
        string oldName,
        string newName)
    {
        Assert.False(CompareMemberNames(oldName, newName).IsExact);
        Assert.True(CompareMemberNames(
            oldName,
            newName,
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact);
    }

    /// <summary>
    /// The negative half of #3503: only the containing-method ordinal is
    /// non-evidence. Every other component of a closure name still identifies
    /// which lambda a body binds to, so a real mis-binding must keep diffing.
    /// Two forms are documented limitations rather than desired outcomes:
    /// state-machine names (<c>&lt;Name&gt;d__N</c>), whose ordinal is their
    /// only distinguishing component, and display classes
    /// (<c>&lt;&gt;c__DisplayClassN_M</c>), which do carry the same
    /// compilation-unit ordinal and so can still produce a false positive for
    /// a capturing lambda. Widening to display classes is deliberately out of
    /// scope here; the corpus has no remaining row that needs it.
    /// </summary>
    [Theory]
    [InlineData("<Run>b__103_0", "<Run>b__103_1")]              // different lambda in the same method
    [InlineData("<Run>b__103_0", "<Walk>b__103_0")]             // different containing method
    [InlineData("<Run>g__Local|103_0", "<Run>g__Other|103_0")]  // different local function
    [InlineData("<Run>d__103", "<Run>d__128")]                  // state machine: not normalized
    [InlineData("Grab__103_0", "Grab__128_0")]                  // authored name, not synthesized
    [InlineData("<b__1_0>b__103_0", "<b__2_0>b__128_0")]        // authored enclosing name that looks synthesized
    [InlineData("<>c__DisplayClass103_0", "<>c__DisplayClass128_0")] // display class: known limitation, see remarks
    [InlineData("<Run>b__103_0_extra", "<Run>b__128_0_extra")]  // trailing text: not a closure name
    [InlineData("<Run>g__Local|103_0x", "<Run>g__Local|128_0x")] // trailing text after a local function
    [InlineData("<Run>b__103_0$x", "<Run>b__128_0$x")]          // trailing `$`, which some producers emit
    [InlineData("<Run>b__103_0\u00e9", "<Run>b__128_0\u00e9")]  // trailing letter (Lu/Ll)
    [InlineData("<Run>b__103_0\u16ee", "<Run>b__128_0\u16ee")]  // trailing letter number (Nl)
    [InlineData("<Run>b__103_0\u0301", "<Run>b__128_0\u0301")]  // trailing combining mark (Mn)
    [InlineData("<Run>b__103_0\u0903", "<Run>b__128_0\u0903")]  // trailing combining mark (Mc)
    [InlineData("<Run>b__103_0\u203f", "<Run>b__128_0\u203f")]  // trailing connector punctuation (Pc)
    [InlineData("<Run>b__103_0\u200c", "<Run>b__128_0\u200c")]  // trailing format character (Cf)
    [InlineData("<Run>b__103_0\U00010400", "<Run>b__128_0\U00010400")] // trailing supplementary-plane letter
    public void NormalizeSynthesizedMemberOrdinals_PreservesEveryOtherNameComponent(
        string oldName,
        string newName)
    {
        Assert.False(CompareMemberNames(
            oldName,
            newName,
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact);
    }

    /// <summary>
    /// Member names come from untrusted metadata, and the threat model
    /// requires recursion over hostile input to be bounded
    /// (docs/design/untrusted-data-threat-model.md), so the enclosing-name
    /// recursion stops at <c>MaxNestingDepth</c> (16). This pins the
    /// boundary's observable behavior: an ordinal nested within the cap is
    /// still normalized, and one nested past it stays literal. Degrading to
    /// literal can only cost a false positive, never a masked difference.
    /// </summary>
    [Fact]
    public void NormalizeSynthesizedMemberOrdinals_StopsNormalizingPastTheNestingCap()
    {
        Assert.True(CompareMemberNames(
            Nest(depth: 20, differingLevel: 0),
            Nest(depth: 20, differingLevel: 0, shift: 500),
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact,
            "The outermost ordinal is within the cap and must still normalize.");

        Assert.False(CompareMemberNames(
            Nest(depth: 20, differingLevel: 19),
            Nest(depth: 20, differingLevel: 19, shift: 500),
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact,
            "An ordinal nested past the cap must stay literal rather than silently comparing equal.");
    }

    /// <summary>
    /// Builds a name nested <paramref name="depth"/> levels deep where only
    /// the ordinal at <paramref name="differingLevel"/> (counted from the
    /// outermost) is moved by <paramref name="shift"/>.
    /// </summary>
    static string Nest(int depth, int differingLevel, int shift = 0)
    {
        string name = "Run";
        for (int level = depth - 1; level >= 0; level--)
        {
            int ordinal = 100 + (level == differingLevel ? shift : 0);
            name = $"<{name}>b__{ordinal}_0";
        }

        return name;
    }

    [Fact]
    public void NormalizeSynthesizedMemberOrdinals_PreservesSynthesizedLikeStringLiterals()
    {
        var diff = CompareImages(
            BuildStringImage("Old", "<>9__103_0"),
            BuildStringImage("New", "<>9__128_0"),
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals);

        Assert.False(diff.IsExact);
    }

    /// <summary>
    /// The option is scoped to a member's simple name, so a type operand keeps
    /// its ordinal even when the type name is spelled like a closure. Applying
    /// the rewrite to the formatted operand string instead would let it reach
    /// declaring types, parameter types, and generic arguments, collapsing
    /// references to genuinely distinct types.
    /// </summary>
    [Theory]
    [InlineData("<Run>b__103_0", "<Run>b__128_0")]
    [InlineData("<>9__103_0", "<>9__128_0")]
    [InlineData("<>c__DisplayClass103_0", "<>c__DisplayClass128_0")]
    public void NormalizeSynthesizedMemberOrdinals_LeavesTypeOperandsAlone(
        string oldTypeName,
        string newTypeName)
    {
        Assert.False(CompareDeclaringTypeNames(
            oldTypeName,
            newTypeName,
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact);
    }

    /// <summary>
    /// Each candidate <c>&lt;</c> starts its own forward scan for the <c>&gt;</c>
    /// that closes it, so a name built from unbalanced <c>&lt;</c> characters
    /// costs O(n²) without a budget. Member names come from untrusted metadata
    /// and the threat model requires CPU amplification to be bounded
    /// (docs/design/untrusted-data-threat-model.md). This pins the budget's
    /// observable behavior instead of its timing: a closure name buried behind
    /// a few unbalanced <c>&lt;</c> characters still normalizes, and one buried
    /// behind enough of them to exhaust the budget is declined rather than
    /// scanned. Declining can only cost a false positive, never a masked
    /// difference.
    /// </summary>
    [Fact]
    public void NormalizeSynthesizedMemberOrdinals_BoundsScanWorkOnUnbalancedNames()
    {
        Assert.True(CompareMemberNames(
            Buried(unbalanced: 4, ordinal: 103),
            Buried(unbalanced: 4, ordinal: 128),
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact,
            "Within the scan budget a buried closure name must still normalize.");

        Assert.False(CompareMemberNames(
            Buried(unbalanced: 200, ordinal: 103),
            Buried(unbalanced: 200, ordinal: 128),
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact,
            "Past the scan budget the name must be declined rather than scanned quadratically.");
    }

    /// <summary>
    /// A closure name preceded by <paramref name="unbalanced"/> <c>&lt;</c>
    /// characters that never close, each of which starts its own failing scan.
    /// </summary>
    static string Buried(int unbalanced, int ordinal)
        => new string('<', unbalanced) + $"<Run>b__{ordinal}_0";

    /// <summary>
    /// The local function form searches forward for the <c>|</c> that
    /// terminates the local's own name, and that search runs to the end of the
    /// string when there is none. Charging it to the same budget as the angle
    /// scan is what stops a name built from <c>&lt;&gt;g__.</c> repeats from
    /// costing O(n²): those repeats close their angles immediately, so the
    /// angle scan alone never notices them.
    /// </summary>
    [Fact]
    public void NormalizeSynthesizedMemberOrdinals_ChargesTheLocalFunctionSeparatorScan()
    {
        Assert.False(CompareMemberNames(
            LocalFunctionFiller(repeats: 200, ordinal: 103),
            LocalFunctionFiller(repeats: 200, ordinal: 128),
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact,
            "A separator search that runs to the end of the string must consume the scan budget.");
    }

    /// <summary>
    /// Budget exhaustion has to decline the whole name however it is reached.
    /// When it happens on the last candidate in the string the scan loop just
    /// ends, so the in-loop check never runs again; without a check after the
    /// loop the leading closure name would stay rewritten and two names that
    /// differ only past the exhaustion point would compare equal. These
    /// constants were found by searching for an input the two behaviors
    /// disagree on.
    /// </summary>
    [Fact]
    public void NormalizeSynthesizedMemberOrdinals_DeclinesWhenTheBudgetRunsOutOnTheLastCandidate()
    {
        Assert.False(CompareMemberNames(
            LocalFunctionFiller(repeats: 22, ordinal: 103, trailing: 353),
            LocalFunctionFiller(repeats: 22, ordinal: 128, trailing: 353),
            IlBodyDiffNormalization.NormalizeSynthesizedMemberOrdinals).IsExact,
            "A budget exhausted on the last candidate must decline the whole name, not leave it half-rewritten.");
    }

    /// <summary>
    /// A closure name followed by <paramref name="repeats"/> local-function
    /// prefixes that never supply a <c>|</c>, then <paramref name="trailing"/>
    /// characters containing no <c>&lt;</c>.
    /// </summary>
    static string LocalFunctionFiller(int repeats, int ordinal, int trailing = 0)
        => $"<Run>b__{ordinal}_0"
            + string.Concat(Enumerable.Repeat("<>g__.", repeats))
            + new string('A', trailing);

    [Fact]
    public void Compare_RejectsUndefinedOptions()
    {
        var body = Decode([0x2a]);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => IlBodyDiff.Compare(body, body, (IlBodyDiffNormalization)(1 << 10)));
    }

    static IlBodyDiffResult CompareMemberNames(
        string oldMemberName,
        string newMemberName,
        IlBodyDiffNormalization normalization = IlBodyDiffNormalization.None)
        => CompareImages(
            BuildCallImage("Same", "Library.Probe", oldMemberName),
            BuildCallImage("Same", "Library.Probe", newMemberName),
            normalization);

    static IlBodyDiffResult CompareDeclaringTypeNames(
        string oldTypeName,
        string newTypeName,
        IlBodyDiffNormalization normalization = IlBodyDiffNormalization.None)
        => CompareImages(
            BuildCallImage("Same", "Library.Probe", "Target", oldTypeName),
            BuildCallImage("Same", "Library.Probe", "Target", newTypeName),
            normalization);

    static MethodInstructions Decode(byte[] il)
        => MethodInstructions.Decode(il, il.Length, exceptionRegions: []);

    static IlBodyDiffResult CompareCallImages(
        string oldReference,
        string newReference,
        IlBodyDiffNormalization normalization = IlBodyDiffNormalization.None)
        => CompareImages(
            BuildCallImage("Old", oldReference),
            BuildCallImage("New", newReference),
            normalization);

    static IlBodyDiffResult CompareImages(
        byte[] oldImage,
        byte[] newImage,
        IlBodyDiffNormalization normalization = IlBodyDiffNormalization.None)
    {
        using var oldPe = new PEReader(new MemoryStream(oldImage));
        using var newPe = new PEReader(new MemoryStream(newImage));
        var oldReader = oldPe.GetMetadataReader();
        var newReader = newPe.GetMetadataReader();
        var oldMethod = MetadataTokens.MethodDefinitionHandle(1);
        var newMethod = MetadataTokens.MethodDefinitionHandle(1);
        return IlAssemblyDiff.CompareMembers(
            oldPe,
            oldReader,
            oldMethod,
            newPe,
            newReader,
            newMethod,
            normalization: normalization).Diff;
    }

    static byte[] BuildCallImage(
        string assemblyName,
        string? referenceAssemblyName = null,
        string? memberName = null,
        string? typeName = null)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString($"{assemblyName}.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        EntityHandle target;
        if (referenceAssemblyName is null)
        {
            target = MetadataTokens.MethodDefinitionHandle(1);
        }
        else
        {
            bool selfReference = referenceAssemblyName == assemblyName;
            var reference = metadata.AddAssemblyReference(
                metadata.GetOrAddString(referenceAssemblyName),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
            var type = metadata.AddTypeReference(
                reference,
                selfReference ? default : metadata.GetOrAddString("System"),
                metadata.GetOrAddString(typeName ?? (selfReference ? "C" : "Probe")));
            target = metadata.AddMemberReference(
                type,
                metadata.GetOrAddString(memberName ?? (selfReference ? "Caller" : "Target")),
                metadata.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }));
        }

        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il, new ControlFlowBuilder());
        encoder.Call(target);
        encoder.OpCode(ILOpCode.Ret);
        var methodBodies = new BlobBuilder();
        int bodyOffset = new MethodBodyStreamEncoder(methodBodies).AddMethodBody(encoder);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Caller"),
            metadata.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildStringImage(string assemblyName, string value)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString($"{assemblyName}.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
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
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il, new ControlFlowBuilder());
        encoder.LoadString(metadata.GetOrAddUserString(value));
        encoder.OpCode(ILOpCode.Pop);
        encoder.OpCode(ILOpCode.Ret);
        var methodBodies = new BlobBuilder();
        int bodyOffset = new MethodBodyStreamEncoder(methodBodies).AddMethodBody(encoder);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Caller"),
            metadata.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));

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
