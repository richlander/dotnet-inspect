using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace ILInspector.Decompiler.Tests;

public sealed class PdbLocalNameScopeTests
{
    [Fact]
    public void ReusedSlotWithDifferentScopeNames_PreservesBothIdentities()
    {
        string directory = Path.Combine(
            "artifacts",
            $"dotnet-inspect-pdb-slot-name-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            (string assemblyPath, string pdbPath) = WriteFixture(directory);

            using (var pdbStream = File.OpenRead(pdbPath))
            using (MetadataReaderProvider provider =
                MetadataReaderProvider.FromPortablePdbStream(pdbStream))
            {
                MetadataReader pdb = provider.GetMetadataReader();
                var rows = pdb.GetLocalScopes(MetadataTokens.MethodDefinitionHandle(1))
                    .SelectMany(scopeHandle =>
                    {
                        LocalScope scope = pdb.GetLocalScope(scopeHandle);
                        return scope.GetLocalVariables().Select(variableHandle =>
                        {
                            LocalVariable variable = pdb.GetLocalVariable(variableHandle);
                            return (
                                variable.Index,
                                Name: pdb.GetString(variable.Name),
                                scope.StartOffset,
                                scope.EndOffset);
                        });
                    })
                    .ToArray();

                Assert.Equal(
                    [
                        (0, "first", 3, 14),
                        (0, "second", 14, 27),
                    ],
                    rows);
            }

            using var source = MetadataSource.Open(assemblyPath, pdbPath);
            var function = IrImporter.Import(source, "Probe.SlotReuse", "M");

            Assert.NotNull(function);
            Assert.Equal(["first", "second"], function.LocalNames);
            Assert.Equal([true, true], function.LocalDeclaredInNestedScope);
            Assert.Equal([1, 2], function.LocalDeclarations.Select(d => d.VariableRowId));
            Assert.Equal([0, 0], function.LocalDeclarationBindings.Select(d => d!.SlotIndex));
            Assert.Empty(function.LocalNameImportCauses);
            Assert.Equal([0, 1], function.Descendants.OfType<StoreLocal>().Select(n => n.Index));
            function.CheckInvariant(true);
            function.ValidateArgumentBindings();

            IrPasses.Run(function);
            string output = CSharpPrinter.Print(function).Output!;

            Assert.Contains("int first", output);
            Assert.Contains("int second", output);
            Assert.Contains("Escape(ref first)", output);
            Assert.Contains("Escape(ref second)", output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("first", 3, 17, "second", 14, 13)]
    [InlineData("same", 3, 24, "same", 3, 24)]
    [InlineData("first", 3, 9, "second", 14, 13)]
    [InlineData("first", 5, 9, "second", 14, 13)]
    [InlineData("first", 2, 12, "second", 14, 13)]
    [InlineData("first", 3, 11, "second", 14, 14)]
    public void AmbiguousOrUncoveredRanges_RetainRowsAndPhysicalStorage(
        string first, int firstStart, int firstLength, string second, int secondStart, int secondLength)
    {
        using var fixture = new Fixture(rows:
        [
            new(first, firstStart, firstLength),
            new(second, secondStart, secondLength),
        ]);
        AssertDeclined(fixture.Import(), expectedRows: 2);
    }

    [Fact]
    public void ApproximatePdbNames_FlattenAmbiguousRowsWithoutChangingFidelity()
    {
        using var fixture = new Fixture(rows:
        [
            new("first", 3, 17),
            new("second", 14, 13),
        ]);
        var function = fixture.Import();
        Assert.Equal(2, function.LocalNameImportCauses.Length);
        IrPasses.Run(function);

        DecompilerResult strict = CSharpPrinter.Print(function);
        DecompilerResult approximate = CSharpPrinter.Print(
            function,
            new PrinterOptions { ApproximatePdbLocalNames = true });

        Assert.Contains("V_0", strict.Output);
        Assert.DoesNotContain("first", strict.Output);
        Assert.Contains("first", approximate.Output);
        Assert.DoesNotContain("second", approximate.Output);
        Assert.Equal(DecompilationFidelity.Partial, strict.Fidelity);
        Assert.Equal(DecompilationFidelity.Partial, approximate.Fidelity);
        Assert.Equal(
            function.LocalNameImportCauses,
            FidelityRemarks.CollectCauses(function)
                .Where(cause =>
                    cause.Discriminator
                        == DecompilerFidelityDiscriminators.ScopedLocalNameUnavailable)
                .ToArray());
        Assert.True(approximate.Metadata.EffectiveOptions.ApproximatePdbLocalNames);
        DecompilerDecision decision = Assert.Single(
            approximate.Metadata.Decisions,
            decision => decision.RuleId == "approximate-pdb-local-name");
        Assert.Equal(DecompilerDecisionCategories.Taste, decision.Category);
        Assert.Equal("V_0", decision.Subject);
        Assert.Equal("first", decision.NewValue);
        Assert.Contains("fidelity is unchanged", decision.Detail);
    }

    [Fact]
    public void DisjointEqualTextRows_RemainSeparateIdentities()
    {
        using var fixture = new Fixture(rows: [new("same", 3, 11), new("same", 14, 13)]);
        var function = fixture.Import();
        Assert.Equal(["same", "same"], function.LocalNames);
        Assert.Equal([1, 2], function.LocalDeclarationBindings.Select(d => d!.VariableRowId));
        Assert.Equal([1, 2], function.LocalDeclarationBindings.Select(d => d!.ScopeRowId));
        Assert.Empty(function.LocalNameImportCauses);
        function.CheckInvariant(true);
        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Equal(2, output.Split("Escape(ref same)", StringSplitOptions.None).Length - 1);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void LoadReachingOnlyOtherScopesStore_DoesNotSplit()
    {
        using var fixture = new Fixture(
            instructions: [0x03, 0x0A, 0x06, 0x26, 0x06, 0x2A],
            rows: [new("first", 0, 4), new("second", 4, 2)]);
        AssertDeclined(fixture.Import(), 2);
    }

    [Fact]
    public void InitializerBeforeSecondScope_DoesNotGuessItsBinding()
    {
        using var fixture = new Fixture(
            instructions: [0x03, 0x0A, 0x06, 0x26, 0x03, 0x0A, 0x06, 0x2A],
            rows: [new("first", 0, 4), new("second", 6, 2)]);
        AssertDeclined(fixture.Import(), 2);
    }

    [Fact]
    public void ReentryAfterOtherScopeStore_DoesNotReuseOldAssignmentProof()
    {
        using var fixture = new Fixture(
            instructions: [0x03, 0x0A, 0x2B, 0x02, 0x06, 0x2A, 0x17, 0x0A, 0x2B, 0xFA],
            rows: [new("first", 0, 6), new("second", 6, 4)]);
        AssertDeclined(fixture.Import(), 2);
    }

    [Fact]
    public void SequentialAddressTakenScopes_DoNotInferEscapeFreedomFromVoidCall()
    {
        using var fixture = new Fixture(
            instructions:
            [
                0x03, 0x0A, 0x12, 0x00, 0x28, 0x02, 0x00, 0x00, 0x06, 0x06, 0x26,
                0x03, 0x0A, 0x12, 0x00, 0x28, 0x02, 0x00, 0x00, 0x06, 0x06, 0x2A,
            ],
            rows: [new("first", 0, 11), new("second", 11, 11)]);
        AssertDeclined(fixture.Import(), 2);
    }

    [Fact]
    public void RetainedByRefAliasAcrossScopes_DoesNotSplitAliasedStorage()
    {
        using var fixture = new Fixture(
            instructions:
            [
                0x03, 0x0A, 0x12, 0x00, 0x0B, 0x06, 0x26,
                0x03, 0x0A, 0x07, 0x17, 0x54, 0x06, 0x2A,
            ],
            rows: [new("first", 0, 7), new("second", 7, 7)],
            localSignature: [0x07, 0x02, 0x08, 0x10, 0x08]);
        var function = fixture.Import();
        Assert.Equal(2, function.Locals.Length);
        Assert.Null(function.LocalNames[0]);
        Assert.All(function.Descendants.OfType<StoreLocal>().Where(s => s.SourceOffset is 1 or 8),
            s => Assert.Equal(0, s.Index));
        Assert.Equal(0, Assert.Single(function.Descendants.OfType<LoadLocalAddress>()).Index);
        Assert.Equal(2, function.LocalNameImportCauses.Length);
        function.CheckInvariant(true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PinnedAndRefLocals_DoNotSplit(bool pinned)
    {
        using var fixture = pinned
            ? new Fixture(localSignature: [0x07, 0x01, 0x45, 0x08])
            : new Fixture(
                instructions:
                [
                    0x0F, 0x01, 0x0A, 0x06, 0x4A, 0x26,
                    0x0F, 0x01, 0x0A, 0x06, 0x4A, 0x2A,
                ],
                rows: [new("first", 0, 6), new("second", 6, 6)],
                localSignature: [0x07, 0x01, 0x10, 0x08]);
        AssertDeclined(fixture.Import(), 2);
    }

    [Fact]
    public void NestedReferences_AreAllReplacedWithExactInstructionProvenance()
    {
        using var fixture = new Fixture(
            instructions:
            [
                0x03, 0x0A, 0x06, 0x26,
                0x03, 0x0A, 0x06, 0x06, 0x58, 0x0A, 0x06, 0x2A,
            ],
            rows: [new("first", 0, 4), new("second", 4, 8)]);
        var function = fixture.Import();
        Assert.Equal(["first", "second"], function.LocalNames);
        Assert.Equal([(0, 1), (1, 5), (1, 9)],
            function.Descendants.OfType<StoreLocal>().Select(n => (n.Index, n.SourceOffset)));
        Assert.Equal([(1, 6), (1, 7), (1, 10)],
            function.Descendants.OfType<LoadLocal>().Select(n => (n.Index, n.SourceOffset)));
        Assert.Empty(function.LocalNameImportCauses);
        function.CheckInvariant(true);
        function.ValidateArgumentBindings();
        Assert.All(function.Descendants.OfType<LoadArgument>(),
            argument => Assert.Same(function.Signature.Parameters[argument.Index], argument.Parameter));

        int extra = function.AddLocal(TypeRef.CoreLib("System", "Int32"));
        Assert.Equal(2, extra);
        Assert.Equal(function.Locals.Length, function.LocalDeclarationBindings.Length);
        Assert.Null(function.LocalDeclarationBindings[extra]);
        function.ResetLocals([TypeRef.CoreLib("System", "Int32")], [null]);
        Assert.Empty(function.LocalDeclarationBindings);
        Assert.Equal(2, function.LocalDeclarations.Length);
    }

    [Fact]
    public void RejectedTrial_DoesNotChangeGraphOrSharedDiagnostics()
    {
        using var fixture = new Fixture(
            instructions: [0x03, 0x0A, 0x06, 0x26, 0x06, 0x2A],
            rows: [new("first", 0, 4), new("second", 4, 2)]);
        using var source = MetadataSource.Open(fixture.AssemblyPath, fixture.PdbPath);
        var imported = MethodImporter.Import(source, "Probe.SlotReuse", "M")!;
        var raw = imported with
        {
            Body = imported.Body with { LocalDeclarations = [], LocalNames = [] },
        };
        var function = IrImporter.Build(source, raw, new GenericScope([], []));
        var nodes = function.Descendants.ToArray();
        var diagnostics = function.Diagnostics.ToArray();
        var locals = function.Locals;
        ScopedLocalImport.Apply(function, imported.Body);
        Assert.Equal(nodes, function.Descendants);
        Assert.Equal(diagnostics, function.Diagnostics);
        Assert.Equal(locals, function.Locals);
        Assert.Equal(2, function.LocalNameImportCauses.Length);
        function.CheckInvariant(true);
        function.ValidateArgumentBindings();
    }

    [Fact]
    public void HiddenAndOutOfRangeRows_AreRetainedWithoutSelectingTheirNames()
    {
        using var fixture = new Fixture(rows:
        [
            new("first", 3, 11),
            new("second", 14, 13),
            new("hidden", 14, 13, Attributes: LocalVariableAttributes.DebuggerHidden),
            new("invalidSlot", 14, 13, Slot: 7),
        ]);
        var function = fixture.Import();
        Assert.Equal(4, function.LocalDeclarations.Length);
        Assert.Equal(["first", "second"], function.LocalNames);
        Assert.Equal(LocalVariableAttributes.DebuggerHidden, function.LocalDeclarations[2].Attributes);
        Assert.Equal(7, Assert.Single(function.LocalNameImportCauses).Location.LocalIndex);
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void ApproximatePdbNames_DeclineHiddenAndMalformedRows()
    {
        using var hidden = new Fixture(rows:
        [
            new(
                "hidden",
                3,
                11,
                Attributes: LocalVariableAttributes.DebuggerHidden),
        ]);
        var hiddenFunction = hidden.Import();
        IrPasses.Run(hiddenFunction);
        DecompilerResult hiddenResult = CSharpPrinter.Print(
            hiddenFunction,
            new PrinterOptions { ApproximatePdbLocalNames = true });
        Assert.Contains("V_0", hiddenResult.Output);
        Assert.DoesNotContain("hidden", hiddenResult.Output);

        using var malformed = new Fixture(rows:
        [
            new("outside", 3, 100),
        ]);
        var malformedFunction = malformed.Import();
        Assert.Null(Assert.Single(malformedFunction.PdbLocalNameCandidates));
        IrPasses.Run(malformedFunction);
        DecompilerResult malformedResult = CSharpPrinter.Print(
            malformedFunction,
            new PrinterOptions { ApproximatePdbLocalNames = true });
        Assert.Contains("V_0", malformedResult.Output);
        Assert.DoesNotContain("outside", malformedResult.Output);
        Assert.DoesNotContain(
            malformedResult.Metadata.Decisions,
            decision => decision.RuleId == "approximate-pdb-local-name");
    }

    [Fact]
    public void IncompleteScopeEvidence_IsRetainedAndReportedWithoutSplitting()
    {
        using var fixture = new Fixture();
        using var source = MetadataSource.Open(fixture.AssemblyPath, fixture.PdbPath);
        var imported = MethodImporter.Import(source, "Probe.SlotReuse", "M")!;
        imported = imported with
        {
            Body = imported.Body with
            {
                LocalDeclarations = [imported.Body.LocalDeclarations[0]],
                LocalDeclarationsAreComplete = false,
            },
        };
        var function = IrImporter.Build(source, imported, new GenericScope([], []));
        Assert.Single(function.Locals);
        Assert.Single(function.LocalDeclarations);
        Assert.Null(Assert.Single(function.LocalNames));
        Assert.Null(Assert.Single(function.LocalDeclarationBindings));
        Assert.Single(function.LocalNameImportCauses);
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        function.CheckInvariant(true);

        DecompilerResult approximate = CSharpPrinter.Print(
            function,
            new PrinterOptions { ApproximatePdbLocalNames = true });
        Assert.Contains("V_0", approximate.Output);
        Assert.DoesNotContain("first", approximate.Output);
        Assert.DoesNotContain(
            approximate.Metadata.Decisions,
            decision => decision.RuleId == "approximate-pdb-local-name");
    }

    [Fact]
    public void ExceptionRegions_DeclineRatherThanTreatDebugScopesAsLifetimes()
    {
        using var fixture = new Fixture();
        using var source = MetadataSource.Open(fixture.AssemblyPath, fixture.PdbPath);
        var imported = MethodImporter.Import(source, "Probe.SlotReuse", "M")!;
        var raw = imported with { Body = imported.Body with { LocalDeclarations = [] } };
        var function = IrImporter.Build(source, raw, new GenericScope([], []));
        var nodes = function.Descendants.ToArray();
        function.Regions = [new HandlerRegion(HandlerKind.Finally, 0, 14, 14, 13, 0, null)];
        ScopedLocalImport.Apply(function, imported.Body);
        Assert.Equal(nodes, function.Descendants);
        Assert.Single(function.Locals);
        Assert.Equal(2, function.LocalNameImportCauses.Length);
        function.CheckInvariant(true);
    }

    [Fact]
    public void MissingPdb_KeepsThePhysicalSlotAndOriginalRawImport()
    {
        using var fixture = new Fixture();
        File.Delete(fixture.PdbPath);
        var function = fixture.Import();
        Assert.Single(function.Locals);
        Assert.Empty(function.LocalNames);
        Assert.Empty(function.LocalDeclarations);
        Assert.Empty(function.LocalDeclarationBindings);
        Assert.Empty(function.LocalDeclaredInNestedScope);
        Assert.Empty(function.LocalNameImportCauses);
        Assert.All(function.Descendants.OfType<StoreLocal>(), s => Assert.Equal(0, s.Index));
        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!;
        Assert.DoesNotContain("first", output);
        Assert.DoesNotContain("second", output);
        Assert.Equal(2, output.Split("Escape(ref V_0)", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void ReleaseCompilerDistinctSlots_PreservesExactScopeRows()
    {
        using var fixture = new Fixture();
        const string text = """
            namespace Probe;
            public static class SlotReuse
            {
                public static int M(bool condition, int value)
                {
                    if (condition)
                    {
                        int first = value;
                        Escape(ref first);
                        return first;
                    }
                    else
                    {
                        int second = value + 1;
                        Escape(ref second);
                        return second;
                    }
                }
                static void Escape(ref int value) => value++;
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(text,
            new CSharpParseOptions(LanguageVersion.Preview),
            path: "ScopedImportFixture.cs", encoding: Encoding.UTF8,
            cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create(
            "ScopeQualifiedLocal", [tree], RoslynTestReferences.TrustedPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));
        using (var pe = File.Create(fixture.AssemblyPath))
        using (var pdb = File.Create(fixture.PdbPath))
        {
            var result = compilation.Emit(pe, pdb,
                options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb),
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        }

        using var source = MetadataSource.Open(fixture.AssemblyPath, fixture.PdbPath);
        var imported = MethodImporter.Import(source, "Probe.SlotReuse", "M")!;
        Assert.Equal(2, imported.Body.Locals.Length);
        Assert.Equal(["first", "second"], imported.Body.LocalDeclarations.Select(d => d.Name));
        Assert.Equal([0, 1], imported.Body.LocalDeclarations.Select(d => d.SlotIndex));
        var function = IrImporter.Import(source, "Probe.SlotReuse", "M")!;
        Assert.Equal(["first", "second"], function.LocalNames);
        Assert.Empty(function.LocalNameImportCauses);
        function.CheckInvariant(true);
        function.ValidateArgumentBindings();
        IrPasses.Run(function);
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("Escape(ref first)", output);
        Assert.Contains("Escape(ref second)", output);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    static void AssertDeclined(IrFunction function, int expectedRows)
    {
        Assert.Single(function.Locals);
        Assert.Null(Assert.Single(function.LocalNames));
        Assert.Equal(expectedRows, function.LocalDeclarations.Length);
        Assert.Equal(expectedRows, function.LocalNameImportCauses.Length);
        Assert.All(function.LocalNameImportCauses,
            cause => Assert.Equal(DecompilerFidelityDiscriminators.ScopedLocalNameUnavailable,
                cause.Discriminator));
        Assert.All(function.Descendants.OfType<LoadLocal>(), n => Assert.Equal(0, n.Index));
        Assert.All(function.Descendants.OfType<StoreLocal>(), n => Assert.Equal(0, n.Index));
        Assert.All(function.Descendants.OfType<LoadLocalAddress>(), n => Assert.Equal(0, n.Index));
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        function.CheckInvariant(true);
        IrPasses.Run(function);
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        Assert.Equal(expectedRows, FidelityRemarks.CollectCauses(function).Count(
            cause => cause.Discriminator == DecompilerFidelityDiscriminators.ScopedLocalNameUnavailable));
        function.CheckInvariant(true);
    }

    internal sealed record ScopeRow(
        string Name, int Start, int Length, int Slot = 0,
        LocalVariableAttributes Attributes = LocalVariableAttributes.None);

    internal sealed class Fixture : IDisposable
    {
        readonly string _directory = Path.Combine("artifacts", $"scoped-import-{Guid.NewGuid():N}");
        public string AssemblyPath { get; }
        public string PdbPath { get; }

        public Fixture(byte[]? instructions = null, ScopeRow[]? rows = null, byte[]? localSignature = null)
        {
            Directory.CreateDirectory(_directory);
            (AssemblyPath, PdbPath) = WriteFixture(_directory, instructions, rows, localSignature);
        }

        public IrFunction Import()
        {
            using var source = MetadataSource.Open(AssemblyPath, PdbPath);
            return IrImporter.Import(source, "Probe.SlotReuse", "M")!;
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }

    static (string AssemblyPath, string PdbPath) WriteFixture(
        string directory, byte[]? instructions = null, ScopeRow[]? rows = null, byte[]? signature = null)
    {
        string assemblyPath = Path.Combine(directory, "ScopeQualifiedLocal.dll");
        string pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");

        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("ScopeQualifiedLocal.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("ScopeQualifiedLocal"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            metadata.GetOrAddString("Probe"),
            metadata.GetOrAddString("SlotReuse"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var localSignature = new BlobBuilder();
        localSignature.WriteBytes(signature ?? [0x07, 0x01, 0x08]);
        StandaloneSignatureHandle localSignatureHandle =
            metadata.AddStandaloneSignature(metadata.GetOrAddBlob(localSignature));

        var methodBodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(methodBodies);
        int methodBodyOffset = bodyEncoder.AddMethodBody(
            new InstructionEncoder(MethodInstructions(instructions)),
            maxStack: 2,
            localVariablesSignature: localSignatureHandle,
            attributes: MethodBodyAttributes.InitLocals);
        var escapeInstructions = new BlobBuilder();
        escapeInstructions.WriteByte(0x2A);
        int escapeBodyOffset = bodyEncoder.AddMethodBody(
            new InstructionEncoder(escapeInstructions),
            maxStack: 0);

        var methodSignature = new BlobBuilder();
        methodSignature.WriteBytes(new byte[] { 0x00, 0x02, 0x08, 0x02, 0x08 });
        MethodDefinitionHandle methodHandle = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(methodSignature),
            methodBodyOffset,
            MetadataTokens.ParameterHandle(1));
        var escapeSignature = new BlobBuilder();
        escapeSignature.WriteBytes(new byte[] { 0x00, 0x01, 0x01, 0x10, 0x08 });
        metadata.AddMethodDefinition(
            MethodAttributes.Private | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Escape"),
            metadata.GetOrAddBlob(escapeSignature),
            escapeBodyOffset,
            MetadataTokens.ParameterHandle(1));

        var peBuilder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            methodBodies,
            flags: CorFlags.ILOnly);
        var peImage = new BlobBuilder();
        peBuilder.Serialize(peImage);
        File.WriteAllBytes(assemblyPath, peImage.ToArray());

        int[] rowCounts = new int[64];
        using (var stream = File.OpenRead(assemblyPath))
        using (var pe = new PEReader(stream))
        {
            MetadataReader reader = pe.GetMetadataReader();
            foreach (TableIndex table in Enum.GetValues<TableIndex>())
            {
                if ((uint)table < (uint)rowCounts.Length)
                    rowCounts[(int)table] = reader.GetTableRowCount(table);
            }
        }

        var pdbMetadata = new MetadataBuilder();
        pdbMetadata.AddMethodDebugInformation(default, default);
        pdbMetadata.AddMethodDebugInformation(default, default);
        foreach (var row in rows ?? [new("first", 3, 11), new("second", 14, 13)])
        {
            var variable = pdbMetadata.AddLocalVariable(
                row.Attributes, row.Slot, pdbMetadata.GetOrAddString(row.Name));
            pdbMetadata.AddLocalScope(methodHandle, default, variable, default, row.Start, row.Length);
        }
        var pdbBuilder = new PortablePdbBuilder(
            pdbMetadata,
            ImmutableArray.Create(rowCounts),
            default);
        var pdbImage = new BlobBuilder();
        pdbBuilder.Serialize(pdbImage);
        File.WriteAllBytes(pdbPath, pdbImage.ToArray());

        return (assemblyPath, pdbPath);
    }

    static BlobBuilder MethodInstructions(byte[]? bytes = null)
    {
        // M(bool, int) reuses slot 0 in two branches. Taking its address keeps the
        // slot visible through raising so the misapplied PDB name reaches output.
        var instructions = new BlobBuilder();
        instructions.WriteBytes(
            bytes ?? new byte[]
            {
                0x02,
                0x2C, 0x0B,
                0x03,
                0x0A,
                0x12, 0x00,
                0x28, 0x02, 0x00, 0x00, 0x06,
                0x06,
                0x2A,
                0x03,
                0x17,
                0x58,
                0x0A,
                0x12, 0x00,
                0x28, 0x02, 0x00, 0x00, 0x06,
                0x06,
                0x2A,
            });
        return instructions;
    }
}
