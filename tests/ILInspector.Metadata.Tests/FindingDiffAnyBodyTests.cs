using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;

using ILInspector.ILDiff;
using ILInspector.Instructions;

using Inspector.Findings;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// The Body axis decides IL as canonical IL comparison does under the Body
/// policy: encodings canonical comparison treats as equal are
/// <c>Unchanged</c>, differences it shows are <c>Changed</c>, a dependency's
/// version is not an input, and what it cannot decode is
/// <c>Indeterminate</c>. Every verdict is checked against canonical comparison
/// over the same two bodies.
/// </summary>
public sealed class FindingDiffAnyBodyTests
{
    const string Namespace = "Cases";

    /// <summary>
    /// One Type per case; <c>equal</c> is canonical comparison's verdict and
    /// therefore the Body axis's.
    /// </summary>
    public static TheoryData<string, bool> EncodingCases => new()
    {
        { "BrShortVsLong", true },
        { "BrtrueShortVsLong", true },
        { "LeaveShortVsLong", true },
        { "LdcShortVsLong", true },
        { "LdcMacroVsShort", true },
        { "LdcMinusOneMacroVsLong", true },
        { "ShiftedBranchOffsets", true },
        { "ShiftedSwitchOffsets", true },
        { "LdargMacroVsShort", false },
        { "LdargShortVsLong", false },
        { "LdcValueChanged", false },
        { "LdcShortVsMacroOfAnotherValue", false },
        { "BranchRetargeted", false },
        { "SwitchArmCountChanged", false },
        { "CallTargetChanged", false },
        { "StringChanged", false },
        { "InstructionInserted", false },
        { "CalliSignatureTokenRenumbered", false },
    };

    [Theory]
    [MemberData(nameof(EncodingCases))]
    public void CanonicalEncodings_DecideAsCanonicalComparisonDoes(string name, bool equal)
    {
        (byte[] before, byte[] after) = EncodingImages.Value;
        FindingDiffAnyTypeState state = Single(CompareImages(before, after), $"{Namespace}.{name}");

        Assert.Equal(FindingDiffAnyState.Unchanged, state.Api);
        Assert.Equal(equal ? FindingDiffAnyState.Unchanged : FindingDiffAnyState.Changed, state.Body);
    }

    [Fact]
    public void CanonicalEncodings_AgreeWithCanonicalComparisonPerMethod()
    {
        (byte[] before, byte[] after) = EncodingImages.Value;
        AssertPerMethodAgreement(before, after, decided: true);
    }

    /// <summary>
    /// The dependency versions gate: a pair whose only difference is the
    /// version of a non-platform referenced assembly is <c>Unchanged</c> on
    /// both axes for every Type, and Member Body Diff's IL view under the Body
    /// policy shows no row for it either.
    /// </summary>
    [Fact]
    public void DependencyVersion_IsNotABodyChange()
    {
        byte[] before = DependencyImage(new Version(8, 0, 0, 0));
        byte[] after = DependencyImage(new Version(9, 0, 0, 0));

        FindingDiffAnyResult result = CompareImages(before, after);
        Assert.NotEmpty(result.Types);
        Assert.All(result.Types, type =>
            Assert.Equal((FindingDiffAnyState.Unchanged, FindingDiffAnyState.Unchanged), (type.Api, type.Body)));
        AssertPerMethodAgreement(before, after, decided: true);

        using var beforeReader = new PEReader(new MemoryStream(before, writable: false));
        using var afterReader = new PEReader(new MemoryStream(after, writable: false));
        IlMemberEndpointComparison view = CompareMemberEndpoints(beforeReader, afterReader, IlBodyDiffPolicy.Body);
        Assert.True(view.MemberDiff!.Diff.IsExact);
        Assert.Empty(view.MemberDiff.Diff.Rows);
        Assert.False(CompareMemberEndpoints(beforeReader, afterReader, IlBodyDiffNormalization.None).MemberDiff!.Diff.IsExact);
    }

    [Fact]
    public void BranchIntoAnInstruction_IsIndeterminate()
    {
        // br.s +2 targets the operand of ldc.i4, which starts no instruction.
        byte[] body = [0x2B, 0x02, 0x20, 0x00, 0x00, 0x00, 0x00, 0x26, 0x2A];
        AssertUndecidable(body, body);
    }

    [Fact]
    public void BranchIntoAPrefixedInstruction_IsIndeterminate()
    {
        // br.s +2 targets the ldind.i4 that volatile. prefixes.
        byte[] body = [0x2B, 0x02, 0xFE, 0x13, 0x4A, 0x26, 0x2A];
        AssertUndecidable(body, body);
    }

    [Fact]
    public void BodyEndingWithAPrefix_IsIndeterminate()
    {
        byte[] body = [0x00, 0xFE, 0x13]; // nop; volatile.
        AssertUndecidable(body, body);
    }

    [Fact]
    public void UndefinedOpcodeOnOneSide_IsIndeterminate()
    {
        // The undefined opcode is the first difference, so no difference is
        // decided before it.
        AssertUndecidable([0x00, 0x2A], [0x24, 0x2A]);
    }

    [Fact]
    public void RetargetedBranchBeforeAnUndefinedOpcode_IsIndeterminate()
    {
        // The branches target different instructions, which is decided only
        // once the instruction map is complete; the undefined opcode comes first.
        AssertUndecidable(
            [0x2B, 0x00, 0x00, 0x00, 0x24, 0x2A],
            [0x2B, 0x01, 0x00, 0x00, 0x24, 0x2A]);
    }

    /// <summary>
    /// Exception clauses compare as canonical comparison's clause atoms do:
    /// by kind, by catch Type, and by extents as aligned instructions.
    /// </summary>
    public static TheoryData<string, bool> ClauseCases => new()
    {
        { "CatchTypeChanged", false },
        { "TryBoundaryMoved", false },
        { "ShiftedClauseOffsets", true },
        { "NestedClausesIdentical", true },
        { "FilterIdentical", true },
        { "FaultIdentical", true },
        { "FatClauseIdentical", true },
        { "FinallyVsCatchAll", false },
        { "ClauseRemoved", false },
    };

    [Theory]
    [MemberData(nameof(ClauseCases))]
    public void ExceptionClauses_DecideAsCanonicalComparisonDoes(string name, bool equal)
    {
        (byte[] before, byte[] after) = ClauseImages.Value;
        FindingDiffAnyTypeState state = Single(CompareImages(before, after), $"{Namespace}.{name}");

        Assert.Equal(FindingDiffAnyState.Unchanged, state.Api);
        Assert.Equal(equal ? FindingDiffAnyState.Unchanged : FindingDiffAnyState.Changed, state.Body);
    }

    [Fact]
    public void ExceptionClauses_AgreeWithCanonicalComparisonPerMethod()
    {
        (byte[] before, byte[] after) = ClauseImages.Value;
        AssertPerMethodAgreement(before, after, decided: true);
    }

    [Fact]
    public void RawBodyView_MatchesMaterializedBodies()
    {
        (byte[] encodingBefore, byte[] encodingAfter) = EncodingImages.Value;
        (byte[] clauseBefore, byte[] clauseAfter) = ClauseImages.Value;
        foreach (byte[] image in new[] { encodingBefore, encodingAfter, clauseBefore, clauseAfter })
        {
            using var pe = new PEReader(new MemoryStream(image, writable: false));
            AssertRawBodiesMatch(pe);
        }

        using var runtime = new PEReader(File.OpenRead(typeof(object).Assembly.Location));
        AssertRawBodiesMatch(runtime);
    }

    [Fact]
    public void MalformedExceptionSectionBeforeAnIlDifference_IsIndeterminate()
    {
        byte[] before = ClauseImage(after: false);
        byte[] after = (byte[])before.Clone();
        using (var pe = new PEReader(new MemoryStream(after, writable: false)))
        {
            MetadataReader metadata = pe.GetMetadataReader();
            TypeDefinitionHandle type = metadata.TypeDefinitions.Single(handle =>
                metadata.StringComparer.Equals(
                    metadata.GetTypeDefinition(handle).Name,
                    "FilterIdentical"));
            int rva = metadata.GetMethodDefinition(
                metadata.GetTypeDefinition(type).GetMethods().Single())
                .RelativeVirtualAddress;
            int bodyOffset = RvaToFileOffset(pe.PEHeaders, rva);
            ushort flagsAndSize = BinaryPrimitives.ReadUInt16LittleEndian(
                after.AsSpan(bodyOffset, 2));
            int headerSize = (flagsAndSize >> 12) * 4;
            int codeSize = BinaryPrimitives.ReadInt32LittleEndian(
                after.AsSpan(bodyOffset + 4, 4));
            after[bodyOffset + headerSize] = (byte)ILOpCode.Nop;
            after[bodyOffset + Align4(headerSize + codeSize)] = 0;
        }

        FindingDiffAnyTypeState state = Single(
            CompareImages(before, after),
            $"{Namespace}.FilterIdentical");
        Assert.Equal(FindingDiffAnyState.Unchanged, state.Api);
        Assert.Equal(FindingDiffAnyState.Indeterminate, state.Body);
    }

    static void AssertRawBodiesMatch(PEReader pe)
    {
        MetadataReader metadata = pe.GetMetadataReader();
        foreach (MethodDefinitionHandle handle in metadata.MethodDefinitions)
        {
            int rva = metadata.GetMethodDefinition(handle).RelativeVirtualAddress;
            if (rva == 0)
                continue;

            MethodBodyBlock expected = pe.GetMethodBody(rva);
            RawMethodBodyView actual = RawMethodBodyView.Read(pe, rva);
            BlobReader expectedIL = expected.GetILReader();
            BlobReader actualIL = actual.IL;
            Assert.Equal(
                expectedIL.ReadBytes(expectedIL.Length),
                actualIL.ReadBytes(actualIL.Length));

            var actualRegions = new List<RawExceptionRegion>();
            RawExceptionRegionEnumerator regions = actual.ExceptionRegions;
            while (regions.MoveNext(out RawExceptionRegion region))
                actualRegions.Add(region);

            Assert.Equal(expected.ExceptionRegions.Length, actualRegions.Count);
            for (int i = 0; i < actualRegions.Count; i++)
            {
                ExceptionRegion x = expected.ExceptionRegions[i];
                RawExceptionRegion y = actualRegions[i];
                Assert.Equal(x.Kind, y.Kind);
                Assert.Equal(x.TryOffset, y.TryOffset);
                Assert.Equal(x.TryLength, y.TryLength);
                Assert.Equal(x.HandlerOffset, y.HandlerOffset);
                Assert.Equal(x.HandlerLength, y.HandlerLength);
                Assert.Equal(x.CatchType, y.CatchType);
                Assert.Equal(x.FilterOffset, y.FilterOffset);
            }
        }
    }

    static int RvaToFileOffset(PEHeaders headers, int rva)
    {
        int sectionIndex = headers.GetContainingSectionIndex(rva);
        Assert.True(sectionIndex >= 0);
        SectionHeader section = headers.SectionHeaders[sectionIndex];
        return checked(rva - section.VirtualAddress + section.PointerToRawData);
    }

    static int Align4(int value) => checked((value + 3) & ~3);

    [Fact]
    public void TrailingUndefinedOpcode_IsIndeterminate()
    {
        // The longer stream continues with an opcode that cannot be decoded,
        // so no difference is decided before the fault.
        AssertUndecidable([0x2A], [0x2A, 0x24]);
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0xFE }, new byte[] { 0x00, 0x2A })] // nop; a two-byte escape cut short
    [InlineData(new byte[] { 0x20, 0x01, 0x00 }, new byte[] { 0x20, 0x01, 0x00, 0x00, 0x00 })] // ldc.i4 cut short
    [InlineData(new byte[] { 0x2B }, new byte[] { 0x2B, 0x00 })] // br.s with no displacement
    public void BodyEndingInsideAnInstruction_IsIndeterminate(byte[] cut, byte[] whole)
    {
        // The reader reports a body whose IL length cuts an opcode or operand
        // short as an invalid program; the walk reads it as a body it cannot
        // decode, on one side and on both, before any difference is decided.
        AssertUndecidable(cut, cut);
        AssertUndecidable(cut, whole);
        AssertUndecidable(whole, cut);
    }

    [Fact]
    public void BodyEndingInsideAnInstructionAfterADifference_IsChanged()
    {
        // nop vs ret differ at the first opcode; the cut escape after it is
        // never read.
        FindingDiffAnyResult result = CompareRaw([0x00, 0xFE], [0x2A, 0xFE]);
        Assert.Equal(FindingDiffAnyState.Changed, Single(result, "N.C").Body);
        AssertPerMethodAgreement(RawImage([0x00, 0xFE]), RawImage([0x2A, 0xFE]), decided: true);
    }

    [Fact]
    public void TrailingInstruction_IsChanged()
    {
        FindingDiffAnyResult result = CompareRaw([0x2A], [0x2A, 0x00]); // ret vs ret; nop
        Assert.Equal(FindingDiffAnyState.Changed, Single(result, "N.C").Body);
        AssertPerMethodAgreement(RawImage([0x2A]), RawImage([0x2A, 0x00]), decided: true);
    }

    /// <summary>
    /// Roslyn emits a same-module vararg call as a member reference whose
    /// parent is the method definition. Canonical comparison cannot resolve
    /// that parent, so the body is undecidable for both engines.
    /// </summary>
    [Fact]
    public void VarargCallSiteWithAMethodParent_IsIndeterminate()
    {
        byte[] image = VarargImage();
        FindingDiffAnyTypeState state = Single(CompareImages(image, image), "N.C");
        Assert.Equal(FindingDiffAnyState.Unchanged, state.Api);
        Assert.Equal(FindingDiffAnyState.Indeterminate, state.Body);
        Assert.Equal(FindingDiffAnyState.Unchanged, Single(CompareImages(image, image), "N.V").Body);
        AssertPerMethodAgreement(image, image, decided: false);
    }

    [Fact]
    public void TrailingInstructionAfterAnUndecodableTarget_IsIndeterminate()
    {
        // The shorter stream completes holding a target at its end, which
        // starts no instruction, before the trailing nop is read.
        AssertUndecidable([0x2B, 0x01, 0x2A], [0x2B, 0x01, 0x2A, 0x00]);
    }

    [Fact]
    public void MisalignedTargetBeforeAnUndecodableOne_IsIndeterminate()
    {
        // Target 0 lands on different instructions; target 1 of the second
        // body is the end of the body. Decodability is checked first.
        AssertUndecidable(
            [0x2B, 0x00, 0x2B, 0x00, 0x2A],
            [0x2B, 0x02, 0x2B, 0x01, 0x2A]);
    }

    /// <summary>
    /// An image with no <c>#US</c> stream, as facades and reference assemblies
    /// are built, compares on both axes; its bodies hold no user strings.
    /// </summary>
    [Fact]
    public void ImageWithoutUserStrings_IsCompared()
    {
        string? path = Directory.GetFiles(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "*.dll")
            .Order(StringComparer.Ordinal)
            .FirstOrDefault(candidate =>
            {
                try
                {
                    // A facade that only forwards Types has no Type rows to decide.
                    using var pe = new PEReader(File.OpenRead(candidate));
                    return pe.HasMetadata
                        && pe.GetMetadataReader().GetHeapSize(HeapIndex.UserString) == 0
                        && pe.GetMetadataReader().TypeDefinitions.Count > 1;
                }
                catch (BadImageFormatException)
                {
                    return false;
                }
            });
        Assert.SkipWhen(path is null, "No runtime assembly without a #US stream.");
        byte[] image = File.ReadAllBytes(path!);

        FindingDiffAnyResult result = CompareImages(image, image);
        Assert.NotEmpty(result.Types);
        Assert.All(result.Types, type =>
            Assert.Equal((FindingDiffAnyState.Unchanged, FindingDiffAnyState.Unchanged), (type.Api, type.Body)));
    }

    [Fact]
    public void BranchToTheEndOfTheBody_IsIndeterminate()
    {
        // The end of the body starts no instruction, so the decoder reports
        // an external control-flow target and canonical comparison fails.
        byte[] body = [0x2B, 0x02, 0x00, 0x2A]; // br.s +2 (to 4, the end); nop; ret
        AssertUndecidable(body, body);
    }

    static void AssertUndecidable(byte[] before, byte[] after)
    {
        FindingDiffAnyResult result = CompareRaw(before, after);
        FindingDiffAnyTypeState state = Single(result, "N.C");
        Assert.Equal(FindingDiffAnyState.Unchanged, state.Api);
        Assert.Equal(FindingDiffAnyState.Indeterminate, state.Body);
        AssertPerMethodAgreement(RawImage(before), RawImage(after), decided: false);
    }

    /// <summary>
    /// Every method pair's verdict equals canonical comparison's, and the pair
    /// is decided or undecidable as the test expects.
    /// </summary>
    static void AssertPerMethodAgreement(byte[] before, byte[] after, bool decided)
    {
        using var beforeReader = new PEReader(new MemoryStream(before, writable: false));
        using var afterReader = new PEReader(new MemoryStream(after, writable: false));
        List<FindingDiffAny.BodyVerdict> verdicts = FindingDiffAny.BodyVerdicts(beforeReader, afterReader);
        Assert.NotEmpty(verdicts);
        Assert.All(verdicts, verdict =>
        {
            FindingDiffAnyState canonical = FindingDiffAnyTests.CanonicalBodyVerdict(beforeReader, afterReader, verdict);
            Assert.True(
                canonical == verdict.Body,
                $"{verdict.Type} {verdict.Method}: canonical {canonical}, Finding Diff Any {verdict.Body}.");
            if (decided)
                Assert.NotEqual(FindingDiffAnyState.Indeterminate, verdict.Body);
        });
        if (!decided)
            Assert.Contains(verdicts, verdict => verdict.Body == FindingDiffAnyState.Indeterminate);
    }

    /// <summary>
    /// The census cursor's completeness gate over the fixture pair: for every
    /// method of both images, draining the cursor over the method's raw IL
    /// yields the atoms and terminal the decoded body's census yields, and the
    /// atoms are canonical comparison's operations.
    /// </summary>
    [Fact]
    public void Cursor_DrainsEveryFixtureMethodAsCanonicalizationDoes()
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        foreach (string path in new[] { pair.OldAssemblyPath(), pair.NewAssemblyPath() })
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            MetadataReader reader = pe.GetMetadataReader();
            int complete = 0;
            foreach (MethodDefinitionHandle handle in reader.MethodDefinitions)
            {
                var subject = new FindingSubject($"0x{MetadataTokens.GetToken(handle):X8}", reader.GetString(reader.GetMethodDefinition(handle).Name));
                FindingInspection<CanonicalIlOperation> raw = IlCensusCursor.Create(pe, reader, handle, subject, IlBodyDiffPolicy.Body).Drain();
                int rva = reader.GetMethodDefinition(handle).RelativeVirtualAddress;
                MethodInstructions? body = rva == 0 ? null : MethodInstructions.Decode(pe.GetMethodBody(rva));
                FindingInspection<CanonicalIlOperation> decoded = IlFindings.Inspect(body, reader, subject, IlBodyDiffPolicy.Body);

                Assert.Equal(decoded, raw);
                if (raw.Value is FindingInspection<CanonicalIlOperation>.Complete census)
                {
                    Assert.True(IlBodyDiff.TryCanonicalize(body!, reader, IlBodyDiffPolicy.Body, out var operations, out _));
                    Assert.Equal<CanonicalIlOperation>(operations, census.Findings.Select(atom => atom.Payload));
                    complete++;
                }
            }

            Assert.True(complete > 10, $"{complete} complete censuses in {Path.GetFileName(path)}");
        }
    }

    /// <summary>
    /// The canonical order gate: for each encoding case, the two cursors'
    /// atoms are equal in canonical bytes exactly when canonical comparison
    /// equates the bodies, so the encodings it folds yield one atom sequence.
    /// </summary>
    [Theory]
    [MemberData(nameof(EncodingCases))]
    public void Cursor_YieldsEqualAtoms_ForEncodingsCanonicalComparisonEquates(string name, bool equal)
    {
        (byte[] before, byte[] after) = EncodingImages.Value;
        using var beforeReader = new PEReader(new MemoryStream(before, writable: false));
        using var afterReader = new PEReader(new MemoryStream(after, writable: false));

        Assert.Equal(equal, AtomsEqual(
            DrainCase(beforeReader, name, IlBodyDiffPolicy.Body),
            DrainCase(afterReader, name, IlBodyDiffPolicy.Body)));
    }

    /// <summary>
    /// A pair that differs only in a non-platform dependency's version yields
    /// equal atoms under the Body policy for every method, and not under the
    /// default policy, which keeps the assembly.
    /// </summary>
    [Fact]
    public void Cursor_YieldsEqualAtoms_AcrossADependencyVersionBump()
    {
        byte[] before = DependencyImage(new Version(8, 0, 0, 0));
        byte[] after = DependencyImage(new Version(9, 0, 0, 0));
        using var beforeReader = new PEReader(new MemoryStream(before, writable: false));
        using var afterReader = new PEReader(new MemoryStream(after, writable: false));
        MetadataReader a = beforeReader.GetMetadataReader();
        MetadataReader b = afterReader.GetMetadataReader();
        Assert.Equal(a.MethodDefinitions.Count, b.MethodDefinitions.Count);

        bool anyDifferByAssembly = false;
        foreach ((MethodDefinitionHandle x, MethodDefinitionHandle y) in a.MethodDefinitions.Zip(b.MethodDefinitions))
        {
            Assert.True(AtomsEqual(
                Atoms(IlCensusCursor.Create(beforeReader, a, x, new FindingSubject("M", "M"), IlBodyDiffPolicy.Body).Drain()),
                Atoms(IlCensusCursor.Create(afterReader, b, y, new FindingSubject("M", "M"), IlBodyDiffPolicy.Body).Drain())));
            anyDifferByAssembly |= !AtomsEqual(
                Atoms(IlCensusCursor.Create(beforeReader, a, x, new FindingSubject("M", "M")).Drain()),
                Atoms(IlCensusCursor.Create(afterReader, b, y, new FindingSubject("M", "M")).Drain()));
        }

        Assert.True(anyDifferByAssembly);
    }

    static ImmutableArray<CanonicalIlOperation> DrainCase(PEReader pe, string name, IlBodyDiffNormalization policy)
    {
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle type = reader.TypeDefinitions.Single(handle =>
            reader.StringComparer.Equals(reader.GetTypeDefinition(handle).Namespace, Namespace)
            && reader.StringComparer.Equals(reader.GetTypeDefinition(handle).Name, name));
        MethodDefinitionHandle method = reader.GetTypeDefinition(type).GetMethods().Single();
        return Atoms(IlCensusCursor.Create(pe, reader, method, new FindingSubject($"{name}::M", "M"), policy).Drain());
    }

    static ImmutableArray<CanonicalIlOperation> Atoms(FindingInspection<CanonicalIlOperation> inspection)
        => [.. Assert.IsType<FindingInspection<CanonicalIlOperation>.Complete>(inspection.Value).Findings.Select(atom => atom.Payload)];

    static bool AtomsEqual(ImmutableArray<CanonicalIlOperation> a, ImmutableArray<CanonicalIlOperation> b)
        => a.Length == b.Length && a.Zip(b).All(pair => IlBodyDiff.AtomsEqual(pair.First, pair.Second));

    static IlMemberEndpointComparison CompareMemberEndpoints(
        PEReader before,
        PEReader after,
        IlBodyDiffNormalization normalization)
    {
        var subject = new IlMemberDiffSubject("N.C::M", "M");
        return IlAssemblyDiff.CompareMemberEndpoints(
            new IlMemberDiffEndpoint.Present(subject, before, before.GetMetadataReader(), FirstMethod(before)),
            new IlMemberDiffEndpoint.Present(subject, after, after.GetMetadataReader(), FirstMethod(after)),
            normalization);
    }

    static MethodDefinitionHandle FirstMethod(PEReader pe)
        => pe.GetMetadataReader().MethodDefinitions.First();

    static FindingDiffAnyTypeState Single(FindingDiffAnyResult result, string fullName)
        => Assert.Single(result.Types, type => type.FullName == fullName);

    static FindingDiffAnyResult CompareRaw(byte[] before, byte[] after)
        => CompareImages(RawImage(before), RawImage(after));

    /// <summary>Whole and stepped comparisons agree; the whole result is returned.</summary>
    static FindingDiffAnyResult CompareImages(byte[] beforeImage, byte[] afterImage)
    {
        using var beforeReader = new PEReader(new MemoryStream(beforeImage, writable: false));
        using var afterReader = new PEReader(new MemoryStream(afterImage, writable: false));
        FindingDiffAnyResult whole = FindingDiffAny.Compare(beforeReader, afterReader);

        var stepped = new FindingDiffAnyComparison();
        bool complete = false;
        while (!complete)
        {
            using var stepBefore = new PEReader(new MemoryStream(beforeImage, writable: false));
            using var stepAfter = new PEReader(new MemoryStream(afterImage, writable: false));
            complete = stepped.Step(stepBefore, stepAfter, TimeSpan.Zero);
        }
        Assert.Equal(whole.Types, stepped.Result!.Types);
        return whole;
    }

    /// <summary>One public Type <c>N.C</c> whose public static method <c>M</c> has the given IL.</summary>
    static byte[] RawImage(byte[] body)
    {
        var image = new ImageBuilder("Raw", "11111111-1111-1111-1111-111111111111");
        image.AddType("N", "C", ("M", image.VoidSignature, il => il.CodeBuilder.WriteBytes(body)));
        return image.Build();
    }

    /// <summary>
    /// <c>N.V::Invoke(int, ...)</c> is a vararg method; <c>N.C::M</c> calls it
    /// through a member reference whose parent is <c>Invoke</c> itself.
    /// </summary>
    static byte[] VarargImage()
    {
        var image = new ImageBuilder("Vararg", "44444444-4444-4444-4444-444444444444");
        var declaration = new BlobBuilder();
        new BlobEncoder(declaration).MethodSignature(SignatureCallingConvention.VarArgs).Parameters(
            1, returnType => returnType.Void(), parameters => parameters.AddParameter().Type().Int32());
        MethodDefinitionHandle invoke = MetadataTokens.MethodDefinitionHandle(image.NextMethod);
        image.AddType("N", "V", ("Invoke", image.Metadata.GetOrAddBlob(declaration), il => il.OpCode(ILOpCode.Ret)));
        var callSite = new BlobBuilder();
        new BlobEncoder(callSite).MethodSignature(SignatureCallingConvention.VarArgs).Parameters(
            2,
            returnType => returnType.Void(),
            parameters =>
            {
                parameters.AddParameter().Type().Int32();
                parameters = parameters.StartVarArgs();
                parameters.AddParameter().Type().Int64();
            });
        MemberReferenceHandle site = image.Metadata.AddMemberReference(
            invoke, image.Metadata.GetOrAddString("Invoke"), image.Metadata.GetOrAddBlob(callSite));
        image.AddType("N", "C", ("M", image.VoidSignature, il =>
        {
            il.OpCode(ILOpCode.Ldc_i4_1);
            il.OpCode(ILOpCode.Ldc_i8);
            il.CodeBuilder.WriteInt64(2);
            il.Call(site);
            il.OpCode(ILOpCode.Ret);
        }));
        return image.Build();
    }

    static readonly Lazy<(byte[] Before, byte[] After)> ClauseImages =
        new(() => (ClauseImage(after: false), ClauseImage(after: true)));

    /// <summary>
    /// The exception clause pair: a Type per case, each holding one method
    /// <c>M</c> that calls <c>Callees.Callee1</c> inside a protected region.
    /// </summary>
    static byte[] ClauseImage(bool after)
    {
        var image = new ImageBuilder(
            "Clauses",
            after ? "66666666-6666-6666-6666-666666666666" : "55555555-5555-5555-5555-555555555555");
        AssemblyReferenceHandle runtime = image.Metadata.AddAssemblyReference(
            image.Metadata.GetOrAddString("System.Runtime"), new Version(10, 0, 0, 0), default, default, default, default);
        TypeReferenceHandle systemObject = image.Metadata.AddTypeReference(
            runtime, image.Metadata.GetOrAddString("System"), image.Metadata.GetOrAddString("Object"));
        TypeReferenceHandle systemException = image.Metadata.AddTypeReference(
            runtime, image.Metadata.GetOrAddString("System"), image.Metadata.GetOrAddString("Exception"));
        TypeReferenceHandle invalidOperation = image.Metadata.AddTypeReference(
            runtime, image.Metadata.GetOrAddString("System"), image.Metadata.GetOrAddString("InvalidOperationException"));
        (MethodDefinitionHandle callee, _) = image.AddCallees();

        image.AddCase("CatchTypeChanged", il => Catch(il, callee, after ? systemException : invalidOperation, ILOpCode.Br_s, widerTry: false));
        image.AddCase("TryBoundaryMoved", il => Catch(il, callee, invalidOperation, ILOpCode.Br_s, widerTry: after));
        image.AddCase("ShiftedClauseOffsets", il => Catch(il, callee, invalidOperation, after ? ILOpCode.Br : ILOpCode.Br_s, widerTry: false));
        image.AddCase("NestedClausesIdentical", il => NestedCatchInFinally(il, callee, invalidOperation));
        image.AddCase("FilterIdentical", il => Filter(il, callee, invalidOperation));
        image.AddCase("FaultIdentical", il => Fault(il, callee));
        image.AddCase("FatClauseIdentical", il => FatFinally(il));
        image.AddCase("FinallyVsCatchAll", il =>
        {
            if (after)
                Catch(il, callee, systemObject, ILOpCode.Br_s, widerTry: false);
            else
                Finally(il, callee);
        });
        image.AddCase("ClauseRemoved", il =>
        {
            if (after)
            {
                // The same instructions, with no clause over them.
                LabelHandle end = il.DefineLabel();
                il.Call(callee);
                il.Branch(ILOpCode.Br_s, end);
                il.OpCode(ILOpCode.Pop);
                il.Branch(ILOpCode.Br_s, end);
                il.MarkLabel(end);
                il.OpCode(ILOpCode.Ret);
            }
            else
            {
                LabelHandle tryStart = il.DefineLabel();
                LabelHandle tryEnd = il.DefineLabel();
                LabelHandle handlerStart = il.DefineLabel();
                LabelHandle handlerEnd = il.DefineLabel();
                LabelHandle end = il.DefineLabel();
                il.MarkLabel(tryStart);
                il.Call(callee);
                il.Branch(ILOpCode.Leave_s, end);
                il.MarkLabel(tryEnd);
                il.MarkLabel(handlerStart);
                il.OpCode(ILOpCode.Pop);
                il.Branch(ILOpCode.Leave_s, end);
                il.MarkLabel(handlerEnd);
                il.MarkLabel(end);
                il.OpCode(ILOpCode.Ret);
                il.ControlFlowBuilder!.AddCatchRegion(tryStart, tryEnd, handlerStart, handlerEnd, invalidOperation);
            }
        });
        return image.Build();
    }

    /// <summary>A branch, then a call inside a catch region whose try may start at the branch.</summary>
    static void Catch(InstructionEncoder il, MethodDefinitionHandle callee, EntityHandle catchType, ILOpCode branch, bool widerTry)
    {
        LabelHandle tryStart = il.DefineLabel();
        LabelHandle tryEnd = il.DefineLabel();
        LabelHandle handlerStart = il.DefineLabel();
        LabelHandle handlerEnd = il.DefineLabel();
        LabelHandle end = il.DefineLabel();
        LabelHandle next = il.DefineLabel();
        if (widerTry)
            il.MarkLabel(tryStart);
        il.Branch(branch, next);
        il.MarkLabel(next);
        if (!widerTry)
            il.MarkLabel(tryStart);
        il.Call(callee);
        il.Branch(ILOpCode.Leave_s, end);
        il.MarkLabel(tryEnd);
        il.MarkLabel(handlerStart);
        il.OpCode(ILOpCode.Pop);
        il.Branch(ILOpCode.Leave_s, end);
        il.MarkLabel(handlerEnd);
        il.MarkLabel(end);
        il.OpCode(ILOpCode.Ret);
        il.ControlFlowBuilder!.AddCatchRegion(tryStart, tryEnd, handlerStart, handlerEnd, catchType);
    }

    static void Finally(InstructionEncoder il, MethodDefinitionHandle callee)
    {
        LabelHandle tryStart = il.DefineLabel();
        LabelHandle tryEnd = il.DefineLabel();
        LabelHandle handlerStart = il.DefineLabel();
        LabelHandle handlerEnd = il.DefineLabel();
        LabelHandle end = il.DefineLabel();
        LabelHandle next = il.DefineLabel();
        il.Branch(ILOpCode.Br_s, next);
        il.MarkLabel(next);
        il.MarkLabel(tryStart);
        il.Call(callee);
        il.Branch(ILOpCode.Leave_s, end);
        il.MarkLabel(tryEnd);
        il.MarkLabel(handlerStart);
        il.OpCode(ILOpCode.Nop);
        il.OpCode(ILOpCode.Endfinally);
        il.MarkLabel(handlerEnd);
        il.MarkLabel(end);
        il.OpCode(ILOpCode.Ret);
        il.ControlFlowBuilder!.AddFinallyRegion(tryStart, tryEnd, handlerStart, handlerEnd);
    }

    static void Fault(InstructionEncoder il, MethodDefinitionHandle callee)
    {
        LabelHandle tryStart = il.DefineLabel();
        LabelHandle tryEnd = il.DefineLabel();
        LabelHandle handlerStart = il.DefineLabel();
        LabelHandle handlerEnd = il.DefineLabel();
        LabelHandle end = il.DefineLabel();
        il.MarkLabel(tryStart);
        il.Call(callee);
        il.Branch(ILOpCode.Leave_s, end);
        il.MarkLabel(tryEnd);
        il.MarkLabel(handlerStart);
        il.OpCode(ILOpCode.Endfinally);
        il.MarkLabel(handlerEnd);
        il.MarkLabel(end);
        il.OpCode(ILOpCode.Ret);
        il.ControlFlowBuilder!.AddFaultRegion(tryStart, tryEnd, handlerStart, handlerEnd);
    }

    static void Filter(
        InstructionEncoder il,
        MethodDefinitionHandle callee,
        EntityHandle filterType)
    {
        LabelHandle tryStart = il.DefineLabel();
        LabelHandle tryEnd = il.DefineLabel();
        LabelHandle filterStart = il.DefineLabel();
        LabelHandle handlerStart = il.DefineLabel();
        LabelHandle handlerEnd = il.DefineLabel();
        LabelHandle end = il.DefineLabel();
        il.MarkLabel(tryStart);
        il.Call(callee);
        il.Branch(ILOpCode.Leave_s, end);
        il.MarkLabel(tryEnd);
        il.MarkLabel(filterStart);
        il.OpCode(ILOpCode.Isinst);
        il.Token(filterType);
        il.OpCode(ILOpCode.Ldnull);
        il.OpCode(ILOpCode.Cgt_un);
        il.OpCode(ILOpCode.Endfilter);
        il.MarkLabel(handlerStart);
        il.OpCode(ILOpCode.Pop);
        il.Branch(ILOpCode.Leave_s, end);
        il.MarkLabel(handlerEnd);
        il.MarkLabel(end);
        il.OpCode(ILOpCode.Ret);
        il.ControlFlowBuilder!.AddFilterRegion(
            tryStart,
            tryEnd,
            handlerStart,
            handlerEnd,
            filterStart);
    }

    static void FatFinally(InstructionEncoder il)
    {
        LabelHandle tryStart = il.DefineLabel();
        LabelHandle tryEnd = il.DefineLabel();
        LabelHandle handlerStart = il.DefineLabel();
        LabelHandle handlerEnd = il.DefineLabel();
        LabelHandle end = il.DefineLabel();
        il.MarkLabel(tryStart);
        for (int i = 0; i < 300; i++)
            il.OpCode(ILOpCode.Nop);
        il.Branch(ILOpCode.Leave, end);
        il.MarkLabel(tryEnd);
        il.MarkLabel(handlerStart);
        il.OpCode(ILOpCode.Endfinally);
        il.MarkLabel(handlerEnd);
        il.MarkLabel(end);
        il.OpCode(ILOpCode.Ret);
        il.ControlFlowBuilder!.AddFinallyRegion(tryStart, tryEnd, handlerStart, handlerEnd);
    }

    static void NestedCatchInFinally(InstructionEncoder il, MethodDefinitionHandle callee, EntityHandle catchType)
    {
        LabelHandle tryStart = il.DefineLabel();
        LabelHandle tryEnd = il.DefineLabel();
        LabelHandle handlerStart = il.DefineLabel();
        LabelHandle handlerEnd = il.DefineLabel();
        LabelHandle innerTryStart = il.DefineLabel();
        LabelHandle innerTryEnd = il.DefineLabel();
        LabelHandle innerHandlerStart = il.DefineLabel();
        LabelHandle innerHandlerEnd = il.DefineLabel();
        LabelHandle end = il.DefineLabel();
        il.MarkLabel(tryStart);
        il.MarkLabel(innerTryStart);
        il.Call(callee);
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
        il.ControlFlowBuilder!.AddCatchRegion(innerTryStart, innerTryEnd, innerHandlerStart, innerHandlerEnd, catchType);
        il.ControlFlowBuilder!.AddFinallyRegion(tryStart, tryEnd, handlerStart, handlerEnd);
    }

    static readonly Lazy<(byte[] Before, byte[] After)> EncodingImages =
        new(() => (EncodingImage(after: false), EncodingImage(after: true)));

    /// <summary>
    /// The canonical encodings pair: a Type per case, each holding one method
    /// <c>M</c>, beside the callees and the stand-alone signature they use.
    /// </summary>
    static byte[] EncodingImage(bool after)
    {
        var image = new ImageBuilder(
            "Encodings",
            after ? "22222222-2222-2222-2222-222222222222" : "11111111-1111-1111-1111-111111111111");
        AssemblyReferenceHandle library = image.Metadata.AddAssemblyReference(
            image.Metadata.GetOrAddString("Dep"), new Version(1, 0, 0, 0), default, default, default, default);
        if (after)
        {
            // An extra reference row renumbers every later TypeRef token.
            image.Metadata.AddTypeReference(library, image.Metadata.GetOrAddString("Dep"), image.Metadata.GetOrAddString("Padding"));
        }
        TypeReferenceHandle probe = image.Metadata.AddTypeReference(
            library, image.Metadata.GetOrAddString("Dep"), image.Metadata.GetOrAddString("Probe"));
        var callSite = new BlobBuilder();
        new BlobEncoder(callSite).MethodSignature().Parameters(
            1, returnType => returnType.Void(), parameters => parameters.AddParameter().Type().Type(probe, isValueType: false));
        StandaloneSignatureHandle signature = image.Metadata.AddStandaloneSignature(image.Metadata.GetOrAddBlob(callSite));
        UserStringHandle one = image.Metadata.GetOrAddUserString("one");
        UserStringHandle two = image.Metadata.GetOrAddUserString("two");

        (MethodDefinitionHandle callee1, MethodDefinitionHandle callee2) = image.AddCallees();
        ILOpCode br = after ? ILOpCode.Br : ILOpCode.Br_s;
        image.AddCase("BrShortVsLong", il => BranchToRet(il, br));
        image.AddCase("BrtrueShortVsLong", il => ConditionalBranch(il, after ? ILOpCode.Brtrue : ILOpCode.Brtrue_s));
        image.AddCase("LeaveShortVsLong", il => BranchToRet(il, after ? ILOpCode.Leave : ILOpCode.Leave_s));
        image.AddCase("LdcShortVsLong", il => LoadConstant(il, after ? ILOpCode.Ldc_i4 : ILOpCode.Ldc_i4_s, 5));
        image.AddCase("LdcMacroVsShort", il => LoadConstant(il, after ? ILOpCode.Ldc_i4_s : ILOpCode.Ldc_i4_5, 5));
        image.AddCase("LdcMinusOneMacroVsLong", il => LoadConstant(il, after ? ILOpCode.Ldc_i4 : ILOpCode.Ldc_i4_m1, -1));
        image.AddCase("ShiftedBranchOffsets", il => Shifted(il, br));
        image.AddCase("ShiftedSwitchOffsets", il => Switch(il, 2, after ? ILOpCode.Br : ILOpCode.Br_s));
        image.AddCase("LdargMacroVsShort", il => LoadArgument(il, after ? ILOpCode.Ldarg_s : ILOpCode.Ldarg_1));
        image.AddCase("LdargShortVsLong", il => LoadArgument(il, after ? ILOpCode.Ldarg : ILOpCode.Ldarg_s));
        image.AddCase("LdcValueChanged", il => LoadConstant(il, ILOpCode.Ldc_i4, after ? 6 : 5));
        image.AddCase("LdcShortVsMacroOfAnotherValue", il => LoadConstant(il, after ? ILOpCode.Ldc_i4_6 : ILOpCode.Ldc_i4_s, after ? 6 : 5));
        image.AddCase("BranchRetargeted", il => Retarget(il, first: !after));
        image.AddCase("SwitchArmCountChanged", il => Switch(il, after ? 3 : 2, ILOpCode.Br_s));
        image.AddCase("CallTargetChanged", il =>
        {
            il.Call(after ? callee2 : callee1);
            il.OpCode(ILOpCode.Ret);
        });
        image.AddCase("StringChanged", il =>
        {
            il.LoadString(after ? two : one);
            il.OpCode(ILOpCode.Pop);
            il.OpCode(ILOpCode.Ret);
        });
        image.AddCase("InstructionInserted", il =>
        {
            il.OpCode(ILOpCode.Nop);
            if (after)
                il.OpCode(ILOpCode.Nop);
            il.OpCode(ILOpCode.Ret);
        });
        image.AddCase("CalliSignatureTokenRenumbered", il =>
        {
            il.OpCode(ILOpCode.Ldnull);
            il.OpCode(ILOpCode.Ldnull);
            il.OpCode(ILOpCode.Calli);
            il.Token(signature);
            il.OpCode(ILOpCode.Ret);
        });
        return image.Build();
    }

    /// <summary>
    /// A Type whose public method takes, calls into, and names a Type of a
    /// non-platform referenced assembly at the given version.
    /// </summary>
    static byte[] DependencyImage(Version referenceVersion)
    {
        var image = new ImageBuilder("Consumer", "33333333-3333-3333-3333-333333333333");
        AssemblyReferenceHandle library = image.Metadata.AddAssemblyReference(
            image.Metadata.GetOrAddString("Library.One"), referenceVersion, default, default, default, default);
        TypeReferenceHandle probe = image.Metadata.AddTypeReference(
            library, image.Metadata.GetOrAddString("Dep"), image.Metadata.GetOrAddString("Probe"));
        MemberReferenceHandle target = image.Metadata.AddMemberReference(
            probe, image.Metadata.GetOrAddString("Target"), image.VoidSignature);
        var parameterSignature = new BlobBuilder();
        new BlobEncoder(parameterSignature).MethodSignature().Parameters(
            1, returnType => returnType.Void(), parameters => parameters.AddParameter().Type().Type(probe, isValueType: false));
        image.AddType("N", "C", ("M", image.Metadata.GetOrAddBlob(parameterSignature), il =>
        {
            il.Call(target);
            il.OpCode(ILOpCode.Ldtoken);
            il.Token(probe);
            il.OpCode(ILOpCode.Pop);
            il.OpCode(ILOpCode.Ret);
        }));
        return image.Build();
    }

    static void BranchToRet(InstructionEncoder il, ILOpCode branch)
    {
        LabelHandle end = il.DefineLabel();
        il.Branch(branch, end);
        il.OpCode(ILOpCode.Nop);
        il.MarkLabel(end);
        il.OpCode(ILOpCode.Ret);
    }

    static void ConditionalBranch(InstructionEncoder il, ILOpCode branch)
    {
        LabelHandle end = il.DefineLabel();
        il.LoadArgument(0);
        il.Branch(branch, end);
        il.OpCode(ILOpCode.Nop);
        il.MarkLabel(end);
        il.OpCode(ILOpCode.Ret);
    }

    /// <summary>A short or long first branch shifts the offset of every later target.</summary>
    static void Shifted(InstructionEncoder il, ILOpCode firstBranch)
    {
        LabelHandle middle = il.DefineLabel();
        LabelHandle end = il.DefineLabel();
        il.Branch(firstBranch, middle);
        il.OpCode(ILOpCode.Nop);
        il.MarkLabel(middle);
        il.LoadArgument(0);
        il.Branch(ILOpCode.Brfalse_s, end);
        il.OpCode(ILOpCode.Nop);
        il.MarkLabel(end);
        il.OpCode(ILOpCode.Ret);
    }

    static void Retarget(InstructionEncoder il, bool first)
    {
        LabelHandle a = il.DefineLabel();
        LabelHandle b = il.DefineLabel();
        il.Branch(ILOpCode.Br_s, first ? a : b);
        il.MarkLabel(a);
        il.OpCode(ILOpCode.Nop);
        il.MarkLabel(b);
        il.OpCode(ILOpCode.Ret);
    }

    /// <summary>A switch whose arms follow a branch whose encoding shifts their offsets.</summary>
    static void Switch(InstructionEncoder il, int arms, ILOpCode skipBranch)
    {
        il.LoadArgument(0);
        LabelHandle[] labels = [.. Enumerable.Range(0, arms).Select(_ => il.DefineLabel())];
        SwitchInstructionEncoder table = il.Switch(arms);
        foreach (LabelHandle label in labels)
            table.Branch(label);
        LabelHandle skip = il.DefineLabel();
        il.Branch(skipBranch, skip);
        il.MarkLabel(skip);
        foreach (LabelHandle label in labels)
        {
            il.MarkLabel(label);
            il.OpCode(ILOpCode.Nop);
        }
        il.OpCode(ILOpCode.Ret);
    }

    static void LoadConstant(InstructionEncoder il, ILOpCode opCode, int value)
    {
        il.OpCode(opCode);
        if (opCode == ILOpCode.Ldc_i4_s)
            il.CodeBuilder.WriteSByte((sbyte)value);
        else if (opCode == ILOpCode.Ldc_i4)
            il.CodeBuilder.WriteInt32(value);
        il.OpCode(ILOpCode.Pop);
        il.OpCode(ILOpCode.Ret);
    }

    static void LoadArgument(InstructionEncoder il, ILOpCode opCode)
    {
        il.OpCode(opCode);
        if (opCode == ILOpCode.Ldarg_s)
            il.CodeBuilder.WriteByte(1);
        else if (opCode == ILOpCode.Ldarg)
            il.CodeBuilder.WriteUInt16(1);
        il.OpCode(ILOpCode.Pop);
        il.OpCode(ILOpCode.Ret);
    }

    /// <summary>A minimal image: one module and assembly, public Types with public static methods.</summary>
    sealed class ImageBuilder
    {
        readonly BlobBuilder _il = new();
        readonly MethodBodyStreamEncoder _bodies;
        int _nextMethod = 1;

        public ImageBuilder(string assemblyName, string mvid)
        {
            _bodies = new MethodBodyStreamEncoder(_il);
            Metadata.AddModule(
                0,
                Metadata.GetOrAddString($"{assemblyName}.dll"),
                Metadata.GetOrAddGuid(new Guid(mvid)),
                default,
                default);
            Metadata.AddAssembly(
                Metadata.GetOrAddString(assemblyName),
                new Version(1, 0, 0, 0),
                default,
                default,
                0,
                AssemblyHashAlgorithm.Sha1);
            Metadata.AddTypeDefinition(
                default,
                default,
                Metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            var signature = new BlobBuilder();
            new BlobEncoder(signature).MethodSignature().Parameters(0, returnType => returnType.Void(), _ => { });
            VoidSignature = Metadata.GetOrAddBlob(signature);
        }

        public MetadataBuilder Metadata { get; } = new();

        public BlobHandle VoidSignature { get; }

        /// <summary>The row the next added method takes.</summary>
        public int NextMethod => _nextMethod;

        public (MethodDefinitionHandle, MethodDefinitionHandle) AddCallees()
        {
            MethodDefinitionHandle first = MetadataTokens.MethodDefinitionHandle(_nextMethod);
            AddType(
                Namespace,
                "Callees",
                ("Callee1", VoidSignature, il => il.OpCode(ILOpCode.Ret)),
                ("Callee2", VoidSignature, il => il.OpCode(ILOpCode.Ret)));
            return (first, MetadataTokens.MethodDefinitionHandle(MetadataTokens.GetRowNumber(first) + 1));
        }

        public void AddCase(string name, Action<InstructionEncoder> emit)
            => AddType(Namespace, name, ("M", VoidSignature, emit));

        public void AddType(
            string ns,
            string name,
            params (string Name, BlobHandle Signature, Action<InstructionEncoder> Emit)[] methods)
        {
            Metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed,
                Metadata.GetOrAddString(ns),
                Metadata.GetOrAddString(name),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(_nextMethod));
            foreach ((string methodName, BlobHandle signature, Action<InstructionEncoder> emit) in methods)
            {
                var code = new InstructionEncoder(new BlobBuilder(), new ControlFlowBuilder());
                emit(code);
                Metadata.AddMethodDefinition(
                    MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                    MethodImplAttributes.IL,
                    Metadata.GetOrAddString(methodName),
                    signature,
                    _bodies.AddMethodBody(code, maxStack: 8),
                    default);
                _nextMethod++;
            }
        }

        public byte[] Build()
        {
            var image = new BlobBuilder();
            new ManagedPEBuilder(
                    new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll | Characteristics.ExecutableImage),
                    new MetadataRootBuilder(Metadata),
                    _il)
                .Serialize(image);
            return image.ToArray();
        }
    }
}
