using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public sealed class PdbLocalNameScopeTests
{
    [Fact]
    public void ReusedSlotWithDifferentScopeNames_ExposesCurrentLastNameLoss()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
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

            Assert.Equal("second", Assert.Single(function!.LocalNames));

            IrPasses.Run(function);
            string output = CSharpPrinter.Print(function).Output!;

            // #5617: the importer currently collapses scope-qualified names to one
            // entry per slot, so the later name is incorrectly used in both scopes.
            Assert.DoesNotContain("first", output);
            Assert.Contains("int second;", output);
            Assert.Equal(
                2,
                output.Split("Escape(ref second)", StringSplitOptions.None).Length - 1);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static (string AssemblyPath, string PdbPath) WriteFixture(string directory)
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
        localSignature.WriteBytes(new byte[] { 0x07, 0x01, 0x08 });
        StandaloneSignatureHandle localSignatureHandle =
            metadata.AddStandaloneSignature(metadata.GetOrAddBlob(localSignature));

        var methodBodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(methodBodies);
        int methodBodyOffset = bodyEncoder.AddMethodBody(
            new InstructionEncoder(MethodInstructions()),
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
        LocalVariableHandle first = pdbMetadata.AddLocalVariable(
            LocalVariableAttributes.None,
            0,
            pdbMetadata.GetOrAddString("first"));
        LocalVariableHandle second = pdbMetadata.AddLocalVariable(
            LocalVariableAttributes.None,
            0,
            pdbMetadata.GetOrAddString("second"));
        pdbMetadata.AddLocalScope(methodHandle, default, first, default, 3, 11);
        pdbMetadata.AddLocalScope(methodHandle, default, second, default, 14, 13);
        var pdbBuilder = new PortablePdbBuilder(
            pdbMetadata,
            ImmutableArray.Create(rowCounts),
            default);
        var pdbImage = new BlobBuilder();
        pdbBuilder.Serialize(pdbImage);
        File.WriteAllBytes(pdbPath, pdbImage.ToArray());

        return (assemblyPath, pdbPath);
    }

    static BlobBuilder MethodInstructions()
    {
        // M(bool, int) reuses slot 0 in two branches. Taking its address keeps the
        // slot visible through raising so the misapplied PDB name reaches output.
        var instructions = new BlobBuilder();
        instructions.WriteBytes(
            new byte[]
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
