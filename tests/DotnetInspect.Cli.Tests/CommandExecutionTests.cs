using DotnetInspector.Cache;
using System.Buffers;
using System.Buffers.Binary;
using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DotnetInspector.Fixtures;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.EmbeddedFixtures;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Views;
using ILInspector.Analysis;
using Inspector.Findings;
using ILInspector.Metadata;
using ILInspector.Research;
using InertText;
using Markout;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Integration tests that verify actual command execution produces correct output.
/// Uses platform libraries and the test assembly itself as data sources — no network required.
/// </summary>
[Collection("Console")]
[Trait("Speed", "Slow")]
public partial class CommandExecutionTests
{
    private const string PackageFixtureFeed =
        "https://nuget.pkg.github.com/richlander/index.json";
    private const string PackageFixtureId =
        "DotnetInspect.TestAssets.ToolV2";
    private const string PackageFixtureVersion = "1.0.0";
    private const string PackageFixtureUserEnvironmentVariable =
        "DOTNET_INSPECT_PACKAGE_FIXTURE_USER";
    private const string PackageFixtureTokenEnvironmentVariable =
        "DOTNET_INSPECT_PACKAGE_FIXTURE_TOKEN";

    private static readonly string TestAssemblyPath =
        typeof(CommandExecutionTests).Assembly.Location;

    private static void AssertLibraryAsset(string output, string assemblyName)
    {
        string field = Assert.Single(
            output.Split([" | ", "\n"], StringSplitOptions.None),
            value => value.StartsWith("Library: ", StringComparison.Ordinal));
        AssertLibraryAssetPath(field["Library: ".Length..], assemblyName);
    }

    private static void AssertLibraryAssetPath(string path, string assemblyName)
    {
        Assert.True(Path.IsPathFullyQualified(path), $"Expected an acquired asset path: {path}");
        Assert.Equal(assemblyName + ".dll", Path.GetFileName(path));
        Assert.True(File.Exists(path), $"Expected the acquired asset to exist: {path}");
    }

    private static void AssertPlatformTypeInfo(string output, string assemblyName)
    {
        Assert.Contains("## Type Info", output);
        string[] rows = output.Split('\n');
        string libraryRow = Assert.Single(
            rows, row => row.StartsWith("| Library | ", StringComparison.Ordinal));
        AssertLibraryAssetPath(libraryRow.Split('|')[2].Trim(), assemblyName);
        Assert.Single(rows, row => row.StartsWith("| TFM | ", StringComparison.Ordinal));
        Assert.Single(rows, row => row.StartsWith("| Version | ", StringComparison.Ordinal));
        Assert.Contains("| Source | Platform |", output);
    }

    private static int CountRenderedMarkdownTableRows(string markdown) =>
        MarkdownTableTestOracle.CountRows(markdown);

    private static Dictionary<string, int> CountRenderedMarkdownTableRowsBySection(
        string markdown) =>
        MarkdownTableTestOracle.CountRowsBySection(markdown);

    private static class ResourceTriageFixture
    {
        public static int ReadBeforeReturn(Stream stream)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(16);
            int read = stream.Read(buffer, 0, 16);
            ArrayPool<byte>.Shared.Return(buffer);
            return read;
        }

        public static int TransformWithUnrelatedReadAfterReturn(Stream stream)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(16);
            int written = System.Text.Encoding.UTF8.GetBytes(
                "value",
                0,
                5,
                buffer,
                0);
            ArrayPool<byte>.Shared.Return(buffer);
            _ = stream.ReadByte();
            return written;
        }
    }

    private static void WriteFidelityFailureAssembly(string path)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("FidelityFailedFixture.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("FidelityFailedFixture"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            metadata.GetOrAddString("FidelityFailedFixture"),
            metadata.GetOrAddString("Malformed"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var il = new BlobBuilder();
        var instructions = new InstructionEncoder(il, new ControlFlowBuilder());
        // The out-of-range method token is valid IL encoding but forces the
        // importer down its diagnosed crash path when resolving the call.
        instructions.Call(MetadataTokens.MethodDefinitionHandle(999));
        instructions.OpCode(ILOpCode.Ret);
        var methodBodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(methodBodies);
        var bodyOffset = bodyEncoder.AddMethodBody(instructions, maxStack: 8);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("InvalidCall"),
            metadata.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        File.WriteAllBytes(path, image.ToArray());
    }

    private static void WriteModuleConstraintAssembly(string path)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(Path.GetFileName(path)),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("ModuleConstraintFixture"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        ModuleReferenceHandle module =
            metadata.AddModuleReference(
                metadata.GetOrAddString("Other.netmodule"));
        TypeReferenceHandle constraint =
            metadata.AddTypeReference(
                module,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Constraint"));
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle holder =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Holder`1"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        GenericParameterHandle parameter =
            metadata.AddGenericParameter(
                holder,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                0);
        metadata.AddGenericParameterConstraint(
            parameter,
            constraint);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        File.WriteAllBytes(path, image.ToArray());
    }

    private static void WriteNetmodule(
        string path,
        string? assemblyReference = null)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName:
                metadata.GetOrAddString(
                    Path.GetFileName(path)),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList:
                MetadataTokens.FieldDefinitionHandle(1),
            methodList:
                MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Widget"),
            baseType: default,
            fieldList:
                MetadataTokens.FieldDefinitionHandle(1),
            methodList:
                MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddFieldDefinition(
            FieldAttributes.Public,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(
                new byte[] { 0x06, 0x08 }));
        if (assemblyReference is not null)
        {
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(assemblyReference),
                new Version(0, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        File.WriteAllBytes(path, image.ToArray());
    }

    private static void WriteBlankAssemblyNameAssembly(string path)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(Path.GetFileName(path)),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(" "),
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
            TypeAttributes.Public,
            metadata.GetOrAddString("Azure.Test"),
            metadata.GetOrAddString("ExampleClient"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        File.WriteAllBytes(path, image.ToArray());
    }

    private static void WriteReferenceFixtureAssembly(
        string path,
        string assemblyName,
        params string[] references)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(Path.GetFileName(path)),
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
        foreach (string reference in references)
        {
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(reference),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        }
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        File.WriteAllBytes(path, image.ToArray());
    }

    private static void WriteMalformedAssemblyReferenceNameAssembly(
        string path)
    {
        WriteReferenceFixtureAssembly(
            path,
            "Malformed.Reference.Root",
            "System.Runtime");
        byte[] bytes = File.ReadAllBytes(path);
        using var peReader = new PEReader(
            new MemoryStream(bytes, writable: false));
        MetadataReader reader = peReader.GetMetadataReader();
        Assert.True(
            reader.GetHeapSize(HeapIndex.Blob) <= ushort.MaxValue
            && reader.GetHeapSize(HeapIndex.String) <= ushort.MaxValue);
        int assemblyReferenceNameOffset =
            peReader.PEHeaders.MetadataStartOffset
            + reader.GetTableMetadataOffset(TableIndex.AssemblyRef)
            + (4 * sizeof(ushort))
            + sizeof(uint)
            + sizeof(ushort);
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(
                assemblyReferenceNameOffset,
                sizeof(ushort)),
            ushort.MaxValue);
        File.WriteAllBytes(path, bytes);
    }

    private static (string RootPath, string TempDir)
        CreateIdentifierConfusionReferenceGraph()
    {
        const string directName = "\u0405ystem.Direct";
        const string transitiveName = "Micr\u03BFsoft.Transitive";
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"identifier-reference-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        WriteReferenceFixtureAssembly(
            Path.Combine(tempDir, $"{directName}.dll"),
            directName);
        WriteReferenceFixtureAssembly(
            Path.Combine(tempDir, $"{transitiveName}.dll"),
            transitiveName);
        WriteReferenceFixtureAssembly(
            Path.Combine(tempDir, "Bridge.dll"),
            "Bridge",
            transitiveName);
        string rootPath = Path.Combine(tempDir, "Root.dll");
        WriteReferenceFixtureAssembly(rootPath, "Root", directName, "Bridge");
        return (rootPath, tempDir);
    }

    private static (string PackagePath, string TempDir)
        CreateIdentifierConfusionReferencePackage()
    {
        const string directName = "\u0405ystem.Direct";
        const string transitiveName = "Micr\u03BFsoft.Transitive";
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"identifier-reference-package-test-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(tempDir, "content");
        var libraryDirectory = Path.Combine(packageRoot, "lib", "net8.0");
        Directory.CreateDirectory(libraryDirectory);

        WriteReferenceFixtureAssembly(
            Path.Combine(libraryDirectory, $"{directName}.dll"),
            directName);
        WriteReferenceFixtureAssembly(
            Path.Combine(libraryDirectory, $"{transitiveName}.dll"),
            transitiveName);
        WriteReferenceFixtureAssembly(
            Path.Combine(libraryDirectory, "Bridge.dll"),
            "Bridge",
            transitiveName);
        WriteReferenceFixtureAssembly(
            Path.Combine(libraryDirectory, "Root.dll"),
            "Root",
            directName,
            "Bridge");

        var packagePath = Path.Combine(tempDir, "Identifier.Reference.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);
        return (packagePath, tempDir);
    }

    private static void WriteMalformedTypeNameAssembly(
        string path,
        bool includeHealthyType = false)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(Path.GetFileName(path)),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("MalformedIntegrations"),
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
        if (includeHealthyType)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Good"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        }
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Example"),
            metadata.GetOrAddString("BrokenType"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        byte[] bytes = image.ToArray();

        using var peReader = new PEReader(
            new MemoryStream(bytes, writable: false));
        MetadataReader reader = peReader.GetMetadataReader();
        int typeNameOffset =
            peReader.PEHeaders.MetadataStartOffset
            + reader.GetTableMetadataOffset(TableIndex.TypeDef)
            + (reader.GetTableRowSize(TableIndex.TypeDef)
                * (includeHealthyType ? 2 : 1))
            + sizeof(uint);
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(typeNameOffset, sizeof(ushort)),
            ushort.MaxValue);
        File.WriteAllBytes(path, bytes);
    }

    private static void WritePartiallyMalformedTypeNameAssembly(
        string path) =>
        WriteMalformedTypeNameAssembly(
            path,
            includeHealthyType: true);

    private static void WriteMalformedAdjacencyAssembly(
        string path,
        bool malformedAssemblyReference,
        bool referenceFromPublicSurface = false)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(Path.GetFileName(path)),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("MalformedAdjacency"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        BlobHandle token = default;
        if (malformedAssemblyReference)
        {
            var tokenBytes = new BlobBuilder();
            tokenBytes.WriteUInt32(0x01020304);
            token = metadata.GetOrAddBlob(tokenBytes);
        }

        AssemblyReferenceHandle target =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("Target"),
                new Version(1, 0, 0, 0),
                default,
                token,
                default,
                default);
        TypeReferenceHandle healthyBase = referenceFromPublicSurface
            ? metadata.AddTypeReference(
                target,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Base"))
            : default;
        if (!malformedAssemblyReference)
        {
            metadata.AddExportedType(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("NotAForwarder"),
                target,
                typeDefinitionId: 0);
        }

        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Healthy"),
            healthyBase,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        File.WriteAllBytes(path, image.ToArray());
    }

    private static void WriteMissingConstraintAssembly(string path)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(Path.GetFileName(path)),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("MissingConstraint"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle missingAssembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("MissingConstraintDependency"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle missingBase =
            metadata.AddTypeReference(
                missingAssembly,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Base"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Healthy"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle consumer =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Consumer`1"),
                default,
                MetadataTokens.FieldDefinitionHandle(2),
                MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddFieldDefinition(
            FieldAttributes.Public,
            metadata.GetOrAddString("HealthyValue"),
            metadata.GetOrAddBlob(
                new byte[] { 0x06, 0x08 }));
        metadata.AddFieldDefinition(
            FieldAttributes.Public,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(
                new byte[] { 0x06, 0x08 }));
        GenericParameterHandle parameter =
            metadata.AddGenericParameter(
                consumer,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
        metadata.AddGenericParameterConstraint(
            parameter,
            missingBase);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        File.WriteAllBytes(path, image.ToArray());
    }

    private static void WriteOverflowingMetadataStreamCountAssembly(
        string sourcePath,
        string destinationPath)
    {
        byte[] bytes = File.ReadAllBytes(sourcePath);
        using var peReader = new PEReader(
            new MemoryStream(bytes, writable: false));
        int metadataStart = peReader.PEHeaders.MetadataStartOffset;
        int versionLength = BinaryPrimitives.ReadInt32LittleEndian(
            bytes.AsSpan(metadataStart + 12, sizeof(int)));
        int streamCountOffset =
            metadataStart
            + 16
            + versionLength
            + sizeof(ushort);
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(streamCountOffset, sizeof(ushort)),
            ushort.MaxValue);
        File.WriteAllBytes(destinationPath, bytes);
    }

    private static void WriteTruncatedMetadataTableAssembly(
        string sourcePath,
        string destinationPath)
    {
        byte[] bytes = File.ReadAllBytes(sourcePath);
        int metadataStart;
        using (var peReader = new PEReader(
                   new MemoryStream(bytes, writable: false)))
        {
            metadataStart = peReader.PEHeaders.MetadataStartOffset;
        }

        int versionLength = BinaryPrimitives.ReadInt32LittleEndian(
            bytes.AsSpan(metadataStart + 12, sizeof(int)));
        int cursor =
            metadataStart + 16 + AlignTo4(versionLength);
        int streamCount = BinaryPrimitives.ReadUInt16LittleEndian(
            bytes.AsSpan(cursor + 2, sizeof(ushort)));
        cursor += 4;
        for (int index = 0; index < streamCount; index++)
        {
            int sizeOffset = cursor + 4;
            int nameStart = cursor + 8;
            int nameEnd = Array.IndexOf(bytes, (byte)0, nameStart);
            string name = Encoding.ASCII.GetString(
                bytes,
                nameStart,
                nameEnd - nameStart);
            if (name is "#~" or "#-")
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    bytes.AsSpan(sizeOffset, sizeof(int)),
                    sizeof(int));
                File.WriteAllBytes(destinationPath, bytes);
                return;
            }

            cursor = nameStart + AlignTo4(nameEnd - nameStart + 1);
        }

        throw new InvalidOperationException(
            "The source assembly has no metadata table stream.");
    }

    private static int AlignTo4(int value)
        => (value + 3) & ~3;

    private static void WriteHostileIlOperandAssembly(string path)
    {
        var assemblyName = new AssemblyName("HostileIlOperand");
        var assemblyBuilder = new System.Reflection.Emit.PersistedAssemblyBuilder(
            assemblyName, typeof(object).Assembly);
        var moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);
        var typeBuilder = moduleBuilder.DefineType(
            "Hostile.Target",
            TypeAttributes.Public | TypeAttributes.Class);
        var field = typeBuilder.DefineField(
            "field\n    public int Injected() => 42; //",
            typeof(int),
            FieldAttributes.Public);
        var method = typeBuilder.DefineMethod(
            "GetCount",
            MethodAttributes.Public,
            typeof(int),
            Type.EmptyTypes);
        var il = method.GetILGenerator();
        il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
        il.Emit(System.Reflection.Emit.OpCodes.Ldfld, field);
        il.Emit(System.Reflection.Emit.OpCodes.Ret);
        typeBuilder.CreateType();
        assemblyBuilder.Save(path);
    }

    private static void WriteRuntimeAccessorAssembly(string path)
    {
        var assemblyName = new AssemblyName("RuntimeAccessor");
        var assemblyBuilder = new System.Reflection.Emit.PersistedAssemblyBuilder(
            assemblyName, typeof(object).Assembly);
        var moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);
        var typeBuilder = moduleBuilder.DefineType(
            "RuntimeAccessor.Target",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract);
        typeBuilder.DefineDefaultConstructor(MethodAttributes.Public);

        var property = typeBuilder.DefineProperty(
            "Value",
            PropertyAttributes.None,
            typeof(int),
            Type.EmptyTypes);
        var getter = typeBuilder.DefineMethod(
            "get_Value",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            typeof(int),
            Type.EmptyTypes);
        getter.SetImplementationFlags(MethodImplAttributes.Runtime);
        property.SetGetMethod(getter);

        var setter = typeBuilder.DefineMethod(
            "set_Value",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            typeof(void),
            [typeof(int)]);
        setter.GetILGenerator().Emit(System.Reflection.Emit.OpCodes.Ret);
        property.SetSetMethod(setter);

        var @event = typeBuilder.DefineEvent(
            "Changed",
            EventAttributes.None,
            typeof(Action));
        var adder = typeBuilder.DefineMethod(
            "add_Changed",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            typeof(void),
            [typeof(Action)]);
        adder.SetImplementationFlags(MethodImplAttributes.Runtime);
        @event.SetAddOnMethod(adder);

        var remover = typeBuilder.DefineMethod(
            "remove_Changed",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            typeof(void),
            [typeof(Action)]);
        remover.GetILGenerator().Emit(System.Reflection.Emit.OpCodes.Ret);
        @event.SetRemoveOnMethod(remover);

        var mixedProperty = typeBuilder.DefineProperty(
            "MixedValue",
            PropertyAttributes.None,
            typeof(int),
            Type.EmptyTypes);
        var abstractGetter = typeBuilder.DefineMethod(
            "get_MixedValue",
            MethodAttributes.Public | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig | MethodAttributes.Virtual
                | MethodAttributes.NewSlot | MethodAttributes.Abstract,
            typeof(int),
            Type.EmptyTypes);
        mixedProperty.SetGetMethod(abstractGetter);

        var concreteSetter = typeBuilder.DefineMethod(
            "set_MixedValue",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            typeof(void),
            [typeof(int)]);
        concreteSetter.GetILGenerator().Emit(System.Reflection.Emit.OpCodes.Ret);
        mixedProperty.SetSetMethod(concreteSetter);

        var mixedEvent = typeBuilder.DefineEvent(
            "MixedChanged",
            EventAttributes.None,
            typeof(Action));
        var abstractAdder = typeBuilder.DefineMethod(
            "add_MixedChanged",
            MethodAttributes.Public | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig | MethodAttributes.Virtual
                | MethodAttributes.NewSlot | MethodAttributes.Abstract,
            typeof(void),
            [typeof(Action)]);
        mixedEvent.SetAddOnMethod(abstractAdder);

        var concreteRemover = typeBuilder.DefineMethod(
            "remove_MixedChanged",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            typeof(void),
            [typeof(Action)]);
        concreteRemover.GetILGenerator().Emit(System.Reflection.Emit.OpCodes.Ret);
        mixedEvent.SetRemoveOnMethod(concreteRemover);

        typeBuilder.CreateType();
        assemblyBuilder.Save(path);
    }

    private static void WriteHostileFactDetailAssembly(string path)
    {
        var assemblyName = new AssemblyName("HostileFactDetail");
        var assemblyBuilder = new System.Reflection.Emit.PersistedAssemblyBuilder(
            assemblyName, typeof(object).Assembly);
        var moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);

        // The allocated type's name becomes the alloc.new fact's detail.
        var allocated = moduleBuilder.DefineType(
            "Evil\n    public int Injected() => 42; //",
            TypeAttributes.Public | TypeAttributes.Class);
        var allocatedCtor = allocated.DefineDefaultConstructor(MethodAttributes.Public);
        var typeBuilder = moduleBuilder.DefineType(
            "Hostile.Target",
            TypeAttributes.Public | TypeAttributes.Class);
        var method = typeBuilder.DefineMethod(
            "Make",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            Type.EmptyTypes);
        var il = method.GetILGenerator();
        il.Emit(System.Reflection.Emit.OpCodes.Newobj, allocatedCtor);
        il.Emit(System.Reflection.Emit.OpCodes.Ret);
        allocated.CreateType();
        typeBuilder.CreateType();
        assemblyBuilder.Save(path);
    }

    private static void WriteApiDiffQueryAssembly(string path, bool includeAddedMethod)
    {
        var assemblyName = new AssemblyName("ApiDiffQueryFixture");
        var assemblyBuilder = new System.Reflection.Emit.PersistedAssemblyBuilder(
            assemblyName,
            typeof(object).Assembly);
        var moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);
        var typeBuilder = moduleBuilder.DefineType(
            "Sample.Widget",
            TypeAttributes.Public | TypeAttributes.Class);
        typeBuilder.DefineDefaultConstructor(MethodAttributes.Public);

        if (includeAddedMethod)
        {
            var method = typeBuilder.DefineMethod(
                "Added",
                MethodAttributes.Public,
                typeof(void),
                Type.EmptyTypes);
            method.GetILGenerator().Emit(System.Reflection.Emit.OpCodes.Ret);
        }

        typeBuilder.CreateType();
        assemblyBuilder.Save(path);
    }

    private static void WriteResourceAssembly(
        string path,
        params (string Name, byte[] Content)[] resources)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(Path.GetFileName(path)),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(Path.GetFileNameWithoutExtension(path)),
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

        var resourceData = new BlobBuilder();
        foreach (var (name, content) in resources)
        {
            int offset = resourceData.Count;
            resourceData.WriteInt32(content.Length);
            resourceData.WriteBytes(content);
            metadata.AddManifestResource(
                ManifestResourceAttributes.Public,
                metadata.GetOrAddString(name),
                default,
                (uint)offset);
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            managedResources: resourceData,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        File.WriteAllBytes(path, image.ToArray());
    }

    private sealed class NestedDrillTarget
    {
        public NestedDrillTarget(int value) => Value = value;

        public int Value { get; }
    }

    private abstract class ConstructorChainBase
    {
        protected ConstructorChainBase(int value)
        {
            GC.KeepAlive(value);
        }
    }

    private sealed class ConstructorChainTarget : ConstructorChainBase
    {
        public ConstructorChainTarget(int value)
            : base(value)
        {
        }
    }

    private sealed class AwaitTextTarget
    {
        public string AwaitText() => "await";
    }

    private sealed class ILOffsetAsyncFixture
    {
        public async Task<int> StateMachineAsync()
        {
            await Task.Yield();
            return 42;
        }
    }

    private sealed class ILOffsetFloatFixture
    {
        public float FloatConstant() => 1.5f;
    }

    private sealed class ILOffsetExceptionFixture
    {
        public int TryCatch(int value)
        {
            try
            {
                return 100 / value;
            }
            catch (DivideByZeroException)
            {
                return -1;
            }
        }

        public int NestedTryCatch(int value)
        {
            try
            {
                try
                {
                    return 100 / value;
                }
                catch (DivideByZeroException)
                {
                    return -1;
                }
            }
            finally
            {
                GC.KeepAlive(value);
            }
        }

        public int FilteredCatch(int value)
        {
            try
            {
                return 100 / value;
            }
            catch (DivideByZeroException) when (value == 0)
            {
                return -1;
            }
        }
    }

    private sealed class ILOffsetFunctionPointerFixture
    {
        public Func<int> CreateDelegate() => Target;

        private static int Target() => 1;
    }

    private static (string PackagePath, string TempDir) CreateLocalRefPackage(params string[] assemblyNames)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"package-test-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(tempDir, "content");
        string? tfm = null;

        foreach (var assemblyName in assemblyNames)
        {
            var (path, _, _, error) = PlatformResolver.ResolveAssembly(assemblyName);
            Assert.True(error == null && path != null, $"Could not resolve platform assembly '{assemblyName}': {error}");

            tfm ??= Path.GetFileName(Path.GetDirectoryName(path!));
            var targetDir = Path.Combine(packageRoot, "ref", tfm!);
            Directory.CreateDirectory(targetDir);
            File.Copy(path!, Path.Combine(targetDir, Path.GetFileName(path!)));
        }

        var packagePath = Path.Combine(tempDir, "Test.MultiLib.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);
        return (packagePath, tempDir);
    }

    private static (string AssemblyPath, string SourcePath, string FixtureDir)
        CreateNoSourceLinkDiscoveryAssembly()
    {
        const string source =
            """
            namespace DiscoveryFixtures;

            public static class NoSourceLink
            {
                public static int Overloaded(int value) => value;
                public static string Overloaded(string value) => value;
            }
            """;

        var fixtureDir = Path.Combine(
            AppContext.BaseDirectory,
            $"no-sourcelink-discovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixtureDir);

        try
        {
            var assemblyPath = Path.Combine(fixtureDir, "NoSourceLinkDiscovery.dll");
            var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
            var sourcePath = Path.Combine(fixtureDir, "NoSourceLinkDiscovery.cs");
            var sourceEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            File.WriteAllText(sourcePath, source, sourceEncoding);
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path));
            var compilation = CSharpCompilation.Create(
                "NoSourceLinkDiscovery",
                [
                    CSharpSyntaxTree.ParseText(
                        SourceText.From(source, sourceEncoding),
                        new CSharpParseOptions(LanguageVersion.Preview),
                        path: sourcePath)
                ],
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    deterministic: true));

            using (var assembly = File.Create(assemblyPath))
            using (var pdb = File.Create(pdbPath))
            {
                var result = compilation.Emit(
                    assembly,
                    pdbStream: pdb,
                    options: new EmitOptions(
                        debugInformationFormat: DebugInformationFormat.PortablePdb,
                        pdbFilePath: Path.GetFileName(pdbPath)));
                Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            }

            using var sourceLink = SourceLinkService.Open(assemblyPath);
            Assert.True(sourceLink.HasPdb);
            Assert.False(sourceLink.HasSourceLink);
            return (assemblyPath, sourcePath, fixtureDir);
        }
        catch
        {
            Directory.Delete(fixtureDir, recursive: true);
            throw;
        }
    }

    private static (string AssemblyPath, string FixtureDir)
        CreateEmbeddedSourceLinkDiscoveryAssembly()
    {
        const string source =
            """
            namespace DiscoveryFixtures;

            public static class EmbeddedSourceLink
            {
                public static int Value() => 42;
            }
            """;
        var fixtureDir = Path.Combine(
            AppContext.BaseDirectory,
            $"embedded-sourcelink-discovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixtureDir);

        try
        {
            var assemblyPath = Path.Combine(
                fixtureDir,
                "EmbeddedSourceLinkDiscovery.dll");
            var references =
                ((string)AppContext.GetData(
                    "TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path =>
                    MetadataReference.CreateFromFile(path));
            var compilation = CSharpCompilation.Create(
                "EmbeddedSourceLinkDiscovery",
                [
                    CSharpSyntaxTree.ParseText(
                        SourceText.From(source, Encoding.UTF8),
                        new CSharpParseOptions(
                            LanguageVersion.Preview),
                        path: "/_/EmbeddedSourceLinkDiscovery.cs")
                ],
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    deterministic: true));

            using (var assembly = File.Create(assemblyPath))
            using (var sourceLink = new MemoryStream(
                Encoding.UTF8.GetBytes(
                    """{"documents":{"/_/*":"https://example.test/*"}}""")))
            {
                EmitResult result = compilation.Emit(
                    assembly,
                    sourceLinkStream: sourceLink,
                    options: new EmitOptions(
                        debugInformationFormat:
                            DebugInformationFormat.Embedded,
                        pdbFilePath:
                            "EmbeddedSourceLinkDiscovery.pdb"));
                Assert.True(
                    result.Success,
                    string.Join(
                        Environment.NewLine,
                        result.Diagnostics));
            }

            using var service =
                SourceLinkService.Open(assemblyPath);
            Assert.True(service.Context.HasEmbeddedPdb);
            Assert.True(service.HasSourceLink);
            return (assemblyPath, fixtureDir);
        }
        catch
        {
            Directory.Delete(
                fixtureDir,
                recursive: true);
            throw;
        }
    }

    private static (string AssemblyPath, string FixtureDir)
        CreateIncompleteUnsafeDiscoveryAssembly()
    {
        const string source =
            """
            namespace DiscoveryFixtures;

            public static class IncompleteUnsafeDiscovery
            {
                public static int Broken(int value)
                {
                    int local = value;
                    return local + 1;
                }
            }
            """;

        var fixtureDir = Path.Combine(
            AppContext.BaseDirectory,
            $"incomplete-unsafe-discovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixtureDir);

        try
        {
            var assemblyPath = Path.Combine(
                fixtureDir,
                "IncompleteUnsafeDiscovery.dll");
            var references =
                ((string)AppContext.GetData(
                    "TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path =>
                    MetadataReference.CreateFromFile(path));
            var compilation = CSharpCompilation.Create(
                "IncompleteUnsafeDiscovery",
                [
                    CSharpSyntaxTree.ParseText(
                        SourceText.From(source, Encoding.UTF8),
                        new CSharpParseOptions(
                            LanguageVersion.Preview))
                ],
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Debug,
                    deterministic: true));

            using (var assembly = File.Create(assemblyPath))
            {
                EmitResult result =
                    compilation.Emit(assembly);
                Assert.True(
                    result.Success,
                    string.Join(
                        Environment.NewLine,
                        result.Diagnostics));
            }

            byte[] image = File.ReadAllBytes(assemblyPath);
            using var peReader = new PEReader(
                new MemoryStream(
                    image,
                    writable: false));
            MetadataReader reader =
                peReader.GetMetadataReader();
            MethodDefinition method = reader.MethodDefinitions
                .Select(reader.GetMethodDefinition)
                .Single(method =>
                    reader.GetString(method.Name) == "Broken");
            MethodBodyBlock body =
                peReader.GetMethodBody(
                    method.RelativeVirtualAddress);
            Assert.False(body.LocalSignature.IsNil);

            int sectionIndex =
                peReader.PEHeaders.GetContainingSectionIndex(
                    method.RelativeVirtualAddress);
            SectionHeader section =
                peReader.PEHeaders.SectionHeaders[sectionIndex];
            int headerOffset =
                section.PointerToRawData
                + method.RelativeVirtualAddress
                - section.VirtualAddress;
            Assert.Equal(3, image[headerOffset] & 3);
            BinaryPrimitives.WriteInt32LittleEndian(
                image.AsSpan(
                    headerOffset + 8,
                    sizeof(int)),
                0x11FFFFFE);
            File.WriteAllBytes(
                assemblyPath,
                image);
            return (assemblyPath, fixtureDir);
        }
        catch
        {
            Directory.Delete(
                fixtureDir,
                recursive: true);
            throw;
        }
    }

    private static string CompileBodyStateFixture(
        string fixtureDir,
        string assemblyName,
        string source)
    {
        Directory.CreateDirectory(fixtureDir);
        string assemblyPath = Path.Combine(fixtureDir, $"{assemblyName}.dll");
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                deterministic: true));

        using var assembly = File.Create(assemblyPath);
        EmitResult result = compilation.Emit(assembly);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return assemblyPath;
    }

    private static int FindMethodToken(
        string assemblyPath,
        string typeName,
        string methodName,
        int parameterCount)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();

        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            string fullName = string.IsNullOrEmpty(reader.GetString(type.Namespace))
                ? reader.GetString(type.Name)
                : $"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}";
            if (!StringComparer.Ordinal.Equals(fullName, typeName))
                continue;

            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                if (!StringComparer.Ordinal.Equals(reader.GetString(method.Name), methodName))
                    continue;

                int declaredParameterCount = method
                    .GetParameters()
                    .Count(handle => reader.GetParameter(handle).SequenceNumber > 0);
                if (declaredParameterCount == parameterCount)
                    return MetadataTokens.GetToken(methodHandle);
            }
        }

        throw new InvalidOperationException(
            $"Method '{typeName}.{methodName}' with {parameterCount} parameter(s) was not found.");
    }

    private static (string PackagePath, string TempDir)
        CreateLocalIntegrationOpportunityPackage(string tfm = "net10.0")
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"package-test-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(tempDir, "content");
        var libDir = Path.Combine(packageRoot, "lib", tfm);
        Directory.CreateDirectory(libDir);
        File.Copy(
            typeof(Npgsql.NpgsqlConnection).Assembly.Location,
            Path.Combine(libDir, "IntegrationOpportunityFixture.dll"));

        var packagePath = Path.Combine(
            tempDir,
            "Test.IntegrationOpportunity.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);
        return (packagePath, tempDir);
    }

    private static (string PackagePath, string TempDir)
        CreateLocalMultiLibraryIntegrationOpportunityPackage()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"package-test-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(tempDir, "content");
        var libDir = Path.Combine(packageRoot, "lib", "net10.0");
        Directory.CreateDirectory(libDir);
        string assemblyPath = typeof(Npgsql.NpgsqlConnection).Assembly.Location;
        File.Copy(
            assemblyPath,
            Path.Combine(libDir, "IntegrationOpportunity.One.dll"));
        File.Copy(
            assemblyPath,
            Path.Combine(libDir, "IntegrationOpportunity.Two.dll"));

        var packagePath = Path.Combine(
            tempDir,
            "Test.MultiLibraryIntegrationOpportunity.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);
        return (packagePath, tempDir);
    }

    private static (string PackagePath, string TempDir) CreateLocalLibPackage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"package-test-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(tempDir, "content");
        var net8Dir = Path.Combine(packageRoot, "lib", "net8.0");
        var net10Dir = Path.Combine(packageRoot, "lib", "net10.0");
        Directory.CreateDirectory(net8Dir);
        Directory.CreateDirectory(net10Dir);
        File.Copy(TestAssemblyPath, Path.Combine(net8Dir, "Older.dll"));
        File.Copy(TestAssemblyPath, Path.Combine(net10Dir, "Latest.One.dll"));
        File.Copy(TestAssemblyPath, Path.Combine(net10Dir, "Latest.Two.dll"));
        File.WriteAllText(Path.Combine(net10Dir, "Latest.One.xml"), "<doc />");

        var packagePath = Path.Combine(tempDir, "Test.LibraryFiles.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);
        return (packagePath, tempDir);
    }

    private static (string PackagePath, string TempDir) CreateLocalDependencyPackage()
    {
        var package = CreateLocalReadmePackage(
            "Test.DependencyGroups",
            "README.md",
            "readme",
            extraNuspecMetadata:
            """
            <dependencies>
              <group targetFramework="net8.0">
                <dependency id="Test.Dependency.One" version="[1.0.0]" />
              </group>
              <group targetFramework="net9.0">
                <dependency id="Test.Dependency.One" version="[1.0.0]" />
                <dependency id="Test.Dependency.Two" version="[1.0.0]" />
              </group>
              <group targetFramework="net10.0" />
            </dependencies>
            """);
        CreateLocalFeedPackage(
            package.TempDir,
            "Test.Dependency.One",
            """
            <dependencies>
              <group targetFramework="net9.0">
                <dependency id="Test.Dependency.Shared" version="[1.0.0]" />
              </group>
            </dependencies>
            """);
        CreateLocalFeedPackage(
            package.TempDir,
            "Test.Dependency.Two",
            """
            <dependencies>
              <group targetFramework="net9.0">
                <dependency id="Test.Dependency.Shared" version="[1.0.0]" />
              </group>
            </dependencies>
            """);
        CreateLocalFeedPackage(
            package.TempDir,
            "Test.Dependency.Shared");
        return package;
    }

    private static void CreateLocalFeedPackage(
        string feed,
        string id,
        string extraNuspecMetadata = "")
    {
        string packageRoot = Path.Combine(
            feed,
            $"{id}-content-{Guid.NewGuid():N}");
        Directory.CreateDirectory(packageRoot);
        File.WriteAllText(Path.Combine(packageRoot, $"{id}.nuspec"), $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <package>
              <metadata>
                <id>{{id}}</id>
                <version>1.0.0</version>
                <authors>tests</authors>
                <description>test dependency package</description>{{extraNuspecMetadata}}
              </metadata>
            </package>
            """);
        ZipFile.CreateFromDirectory(
            packageRoot,
            Path.Combine(feed, $"{id}.1.0.0.nupkg"));
    }

    private static (string PackagePath, string TempDir) CreateLocalReadmePackage(
        string id,
        string readmeFile,
        string readmeText,
        string? agentsText = null,
        string? extraNuspecMetadata = null,
        params (string Path, string Content)[] extraFiles)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"package-test-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(tempDir, "content");
        Directory.CreateDirectory(packageRoot);
        var readmePath = Path.Combine(packageRoot, readmeFile.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(readmePath)!);
        File.WriteAllText(readmePath, readmeText);
        if (agentsText != null)
            File.WriteAllText(Path.Combine(packageRoot, "AGENTS.md"), agentsText);
        foreach (var (path, content) in extraFiles)
        {
            var fullPath = Path.Combine(packageRoot, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }
        File.WriteAllText(Path.Combine(packageRoot, $"{id}.nuspec"), $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <package>
              <metadata>
                <id>{{id}}</id>
                <version>1.0.0</version>
                <authors>tests</authors>
                <description>test package</description>
                <readme>{{readmeFile}}</readme>{{extraNuspecMetadata}}
              </metadata>
            </package>
            """);

        var packagePath = Path.Combine(tempDir, $"{id}.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);
        return (packagePath, tempDir);
    }

    /// <summary>
    /// A package that ships a nuspec and a library but no README, so a README selection resolves
    /// to a section with zero rows rather than to a missing section.
    /// </summary>
    private static (string PackagePath, string TempDir) CreateLocalPackageWithoutReadme(string id)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"package-test-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(tempDir, "content");
        Directory.CreateDirectory(Path.Combine(packageRoot, "lib", "net8.0"));
        File.WriteAllText(Path.Combine(packageRoot, "lib", "net8.0", $"{id}.dll"), "not a real assembly");
        File.WriteAllText(Path.Combine(packageRoot, $"{id}.nuspec"), $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <package>
              <metadata>
                <id>{{id}}</id>
                <version>1.0.0</version>
                <authors>tests</authors>
                <description>test package</description>
              </metadata>
            </package>
            """);

        var packagePath = Path.Combine(tempDir, $"{id}.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);
        return (packagePath, tempDir);
    }

    private sealed record ProjectSkillDoc(string Path, string Text);

    private static ProjectSkillDoc CompliantProjectSkill(string path, string body)
    {
        var segments = path.Replace('\\', '/').Split('/');
        var name = segments[^2];
        return new ProjectSkillDoc(
            path,
            $"---\nname: {name}\ndescription: Test package skill guidance.\n---\n{body}");
    }

    private sealed record ProjectDocPackage(
        string Id,
        string Version,
        string ReadmeFile,
        string ReadmeText,
        string? AgentsText = null,
        ProjectSkillDoc[]? Skills = null,
        string? ProjectText = null,
        bool OmitReadme = false);

    private static (string ProjectPath, string TempDir) CreateProjectWithPackageDocs(params ProjectDocPackage[] packages)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"project-doc-test-{Guid.NewGuid():N}");
        var projectDir = Path.Combine(tempDir, "App");
        var objDir = Path.Combine(projectDir, "obj");
        Directory.CreateDirectory(objDir);

        var projectPath = Path.Combine(projectDir, "App.csproj");
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        foreach (var package in packages)
        {
            var packageRoot = Path.Combine(tempDir, "packages", package.Id.ToLowerInvariant(), package.Version.ToLowerInvariant());
            Directory.CreateDirectory(packageRoot);

            if (!package.OmitReadme)
            {
                var readmePath = Path.Combine(packageRoot, package.ReadmeFile.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(readmePath)!);
                File.WriteAllText(readmePath, package.ReadmeText);
            }
            if (package.AgentsText != null)
                File.WriteAllText(Path.Combine(packageRoot, "AGENTS.md"), package.AgentsText);
            if (package.ProjectText != null)
                File.WriteAllText(Path.Combine(packageRoot, "PROJECT.md"), package.ProjectText);
            foreach (var skill in package.Skills ?? [])
            {
                var skillPath = Path.Combine(packageRoot, skill.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(skillPath)!);
                File.WriteAllText(skillPath, skill.Text);
            }

            File.WriteAllText(Path.Combine(packageRoot, $"{package.Id.ToLowerInvariant()}.nuspec"), $$"""
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <id>{{package.Id}}</id>
                    <version>{{package.Version}}</version>
                    <authors>tests</authors>
                    <description>test package</description>
                    <readme>{{package.ReadmeFile}}</readme>
                  </metadata>
                </package>
                """);
        }

        var targetEntries = string.Join(",\n", packages.Select(package =>
            $"{JsonString($"{package.Id}/{package.Version}")}: {{}}"));
        var libraryEntries = string.Join(",\n", packages.Select(package =>
        {
            var files = new List<string> { $"{package.Id.ToLowerInvariant()}.nuspec" };
            if (!package.OmitReadme)
                files.Add(package.ReadmeFile.Replace('\\', '/'));
            if (package.AgentsText != null)
                files.Add("AGENTS.md");
            if (package.ProjectText != null)
                files.Add("PROJECT.md");
            files.AddRange((package.Skills ?? []).Select(skill => skill.Path.Replace('\\', '/')));

            var fileEntries = string.Join(", ", files.Select(JsonString));
            var packagePath =
                $"{package.Id.ToLowerInvariant()}/{package.Version.ToLowerInvariant()}";
            return $"{JsonString($"{package.Id}/{package.Version}")}: {{ \"type\": \"package\", \"path\": {JsonString(packagePath)}, \"files\": [ {fileEntries} ] }}";
        }));
        var dependencyEntries = string.Join(",\n", packages.Select(package =>
            $"{JsonString(package.Id)}: {{ \"target\": \"Package\", \"version\": {JsonString($"[{package.Version}, )")} }}"));
        File.WriteAllText(Path.Combine(objDir, "project.assets.json"), $$"""
            {
              "targets": {
                "net10.0": {
                  {{targetEntries}}
                }
              },
              "libraries": {
                {{libraryEntries}}
              },
              "project": {
                "frameworks": {
                  "net10.0": {
                    "dependencies": {
                      {{dependencyEntries}}
                    }
                  }
                }
              }
            }
            """);

        return (projectPath, tempDir);
    }

    private static string JsonString(string value) => JsonSerializer.Serialize(value);

    private static (string PackagePath, string TempDir) CreateLocalPrimaryLibPackage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"package-test-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(tempDir, "content");
        var libDir = Path.Combine(packageRoot, "lib", "net10.0");
        Directory.CreateDirectory(libDir);
        File.Copy(TestAssemblyPath, Path.Combine(libDir, "Test.Primary.dll"));

        var packagePath = Path.Combine(tempDir, "Test.Primary.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);
        return (packagePath, tempDir);
    }

    private static (string PointerPackagePath, string RidPackagePath, string TempDir) CreateLocalToolPackageSet()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tool-package-test-{Guid.NewGuid():N}");

        var pointerRoot = Path.Combine(tempDir, "pointer");
        var pointerToolsDir = Path.Combine(pointerRoot, "tools", "net10.0", "any");
        Directory.CreateDirectory(pointerToolsDir);
        File.WriteAllText(Path.Combine(pointerToolsDir, "DotnetToolSettings.xml"), """
            <DotNetCliTool Version="2">
              <Commands>
                <Command Name="test-tool" EntryPoint="Test.Tool.dll" Runner="dotnet" />
              </Commands>
              <RuntimeIdentifierPackages>
                <RuntimeIdentifierPackage RuntimeIdentifier="linux-x64" Id="Test.Tool.linux-x64" />
                <RuntimeIdentifierPackage RuntimeIdentifier="any" Id="Test.Tool.any" />
              </RuntimeIdentifierPackages>
            </DotNetCliTool>
            """);

        var payloadRoot = Path.Combine(tempDir, "payload");
        var payloadToolsDir = Path.Combine(payloadRoot, "tools", "net10.0", "any");
        Directory.CreateDirectory(payloadToolsDir);
        File.Copy(TestAssemblyPath, Path.Combine(payloadToolsDir, "Test.Tool.dll"));

        var ridRoot = Path.Combine(tempDir, "rid");
        var ridToolsDir = Path.Combine(ridRoot, "tools", "any", "linux-x64");
        Directory.CreateDirectory(ridToolsDir);
        File.WriteAllText(Path.Combine(ridToolsDir, "DotnetToolSettings.xml"), """
            <DotNetCliTool Version="2">
              <Commands>
                <Command Name="test-tool" EntryPoint="test-tool" Runner="executable" />
              </Commands>
            </DotNetCliTool>
            """);

        var pointerPackagePath = Path.Combine(tempDir, "Test.Tool.1.0.0.nupkg");
        var payloadPackagePath = Path.Combine(tempDir, "Test.Tool.any.1.0.0.nupkg");
        var ridPackagePath = Path.Combine(tempDir, "Test.Tool.linux-x64.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(pointerRoot, pointerPackagePath);
        ZipFile.CreateFromDirectory(payloadRoot, payloadPackagePath);
        ZipFile.CreateFromDirectory(ridRoot, ridPackagePath);

        return (pointerPackagePath, ridPackagePath, tempDir);
    }

    public CommandExecutionTests()
    {
        NuGetCache.Initialize("dotnet-inspect");
    }

    private static Task<(int Exit, string Output, string Error)> RunAppAsync(params string[] args)
    {
        return ConsoleCapture.RunAsync(async () =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            if (CommandLineBuilder.TryGetRemovedCommandError(
                    args,
                    out var removedCommandError))
            {
                CommandError.Write(removedCommandError!);
                return 1;
            }

            // Mirror Program.cs: stale spellings are raw-token questions, so
            // the product answers them before parsing rather than in a validator.
            if (CommandLineBuilder.TryGetStaleArgumentError(
                    args,
                    root,
                    out var staleArgumentError))
            {
                CommandError.Write(staleArgumentError!);
                return 1;
            }

            args = CommandLineBuilder.PreprocessArgs(args, root);
            return await CommandLineBuilder.InvokeAsync(root.Parse(args), args);
        });
    }

    private static async Task<(int Exit, string Output, string Error)>
        RunProjectFixtureAsync(
            string projectPath,
            params string[] args)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException(
                "The project fixture has no containing directory.");
        string packagesRoot = Path.Combine(
            Directory.GetParent(projectDirectory)?.FullName
                ?? throw new InvalidOperationException(
                    "The project fixture has no package root."),
            "packages");
        string? original =
            Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        Environment.SetEnvironmentVariable(
            "NUGET_PACKAGES",
            packagesRoot);
        try
        {
            return await RunAppAsync(
                ["project", projectPath, .. args]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "NUGET_PACKAGES",
                original);
        }
    }

    private static async Task<(int Exit, string Output, string Error)>
        RunAppInDirectoryAsync(
            string workingDirectory,
            params string[] args)
        => await RunAppInDirectoryWithEnvironmentAsync(
            workingDirectory,
            environment: null,
            args);

    private static async Task<(int Exit, string Output, string Error)>
        RunAppInDirectoryWithEnvironmentAsync(
            string workingDirectory,
            IReadOnlyDictionary<string, string?>? environment,
            params string[] args)
    {
        var startInfo = new ProcessStartInfo(
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                ?? "dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(typeof(CommandLineBuilder).Assembly.Location);
        foreach (string arg in args)
            startInfo.ArgumentList.Add(arg);
        if (environment is not null)
        {
            foreach ((string name, string? value) in environment)
                startInfo.Environment[name] = value;
        }

        using Process process = Process.Start(startInfo)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output, await error);
    }

    private static IEnumerable<string> JsonStrings(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            yield return element.GetString()!;
            yield break;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                foreach (var value in JsonStrings(item))
                    yield return value;
            yield break;
        }

        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
                foreach (var value in JsonStrings(property.Value))
                    yield return value;
    }

    private static void SkipUnlessAspNetCoreAvailable()
    {
        var (referencePath, _, _) = PlatformResolver.ResolveFramework("aspnetcore");
        Assert.SkipUnless(referencePath is not null, "ASP.NET Core reference pack is not available.");
    }

    private static bool IsFacadeAssembly(string assemblyPath) =>
        PlatformResolver.ClassifyAssemblySurface(assemblyPath)
            is AssemblySurfaceClassificationOutcome.Classified classified
        && classified.Classification.Kind == AssemblySurfaceKind.Facade;

    /// <summary>
    /// The labels <c>Type Info</c> can render, read off the section's own declaration. Markout
    /// titles a property with <c>[MarkoutPropertyName]</c> when present and splits PascalCase
    /// otherwise.
    /// </summary>
    private static IReadOnlyCollection<string> DeclaredTypeInfoLabels()
    {
        var labels = typeof(TypeInfoSection)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property =>
                property.GetCustomAttribute<MarkoutPropertyNameAttribute>()?.Name
                    ?? SplitPascalCase(property.Name))
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(labels);
        return labels;

        static string SplitPascalCase(string name) =>
            Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", " ");
    }

    private static async Task<List<string>> RenderTypeInfoLabelsAsync(string assembly, string typeName)
    {
        var options = new TypeOptions
        {
            PlatformAssembly = assembly,
            TypeName = typeName,
            Select = [SectionNames.TypeInfo]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);

        return output.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith('|') && !line.StartsWith("| ---") && line != "| Field | Value |")
            .Select(line => line.Split('|', StringSplitOptions.RemoveEmptyEntries)[0].Trim())
            .ToList();
    }

    private static List<string> SectionHeadings(string output) =>
        [.. output.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
            .Select(line => line[3..].Trim())];

    private static List<string> ParseFirstColumn(string output, string headerLabel)
    {
        return output.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith('|'))
            .Select(line => line.Split('|', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } cells
                ? cells[0].Trim()
                : string.Empty)
            .Where(cell => cell.Length > 0 && cell != headerLabel && !cell.StartsWith('-'))
            .ToList();
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
            {
                RequestMessage = request,
            });
    }

    private sealed class SourceResponseHandler(byte[] body, string? finalUrl = null)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
                RequestMessage = finalUrl is null
                    ? request
                    : new HttpRequestMessage(HttpMethod.Get, finalUrl),
            });
    }

    private static int CountMarkdownDataRows(string markdown)
    {
        var count = 0;
        var afterSeparator = false;
        foreach (string line in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            if (!line.StartsWith('|'))
            {
                afterSeparator = false;
                continue;
            }

            bool isSeparator = line
                .Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(cell => cell.Trim().Trim(':'))
                .All(cell => cell.Length > 0 && cell.All(character => character == '-'));
            if (isSeparator)
            {
                afterSeparator = true;
                continue;
            }

            if (afterSeparator)
                count++;
        }

        return count;
    }

    private static bool IsSourceIntegrityStatusResult(
        string[] command,
        string section,
        int exitCode,
        string output)
        => command is ["library", ..]
           && section == "SourceLink: Integrity"
           && exitCode == 1
           && output.Contains("Status", StringComparison.Ordinal);

    private static string[] BuildDiscoverySelectionArgs(string[] command, string section)
    {
        List<string> args = [.. command];
        args.AddRange(section == SectionNames.FindingCensus
            ? ["-S", section, "--tips", "q"]
            : ["-S", section, "--table", "--tips", "q", "-n", "40"]);
        return [.. args];
    }

    private static readonly HashSet<string> TypeUnprobedDiscoverySections =
        ApiMemberSectionDescriptors.CreatePipeline().GetUnprobedSections();

    private static readonly HashSet<string> MemberOverloadUnprobedDiscoverySections =
        ApiMemberOverloadSectionDescriptors.CreatePipeline().GetUnprobedSections();

    private static bool RequiresNonEmptyDiscoveryResult(string[] command, string section)
    {
        // ProbeEffectiveness=false deliberately allows -D to list a structurally applicable
        // section whose -S result is empty. Derive that exemption from the same pipeline
        // declaration while preserving the non-empty guard for probed sections. The generated
        // no-SourceLink overload fixture gates this distinction without depending on checkout
        // source acquisition (#3464).
        if (section == SectionNames.FindingCensus)
            return true;

        if (IsNoMemberTypeDiscoveryCommand(command))
            return !TypeUnprobedDiscoverySections.Contains(section);

        return command is ["member", _, var memberName, ..]
               && memberName == nameof(MemberCallsFixture.Overloaded)
               && !MemberOverloadUnprobedDiscoverySections.Contains(section);
    }

    private static bool IsNoMemberTypeDiscoveryCommand(string[] command)
        => command is ["type", var typeName, ..]
           && typeName == typeof(EmptyDiscoveryFixture).FullName;

    private static readonly string[] SingleOverloadDiscoverySections =
    [
        "Signature",
        "Custom Attributes",
        "Decompiled Source",
        "Annotated Source",
        "Annotated Source Document",
        "Finding Census",
        "Cost Overlay",
        "Semantics Overlay",
        "PDB Source",
        "Source Diff",
        "Calls",
        "Callers",
        "Call Graph",
        "Unsafe Operations",
        "Top Leverage",
        "Performance Triage",
        "Facts",
        "IL"
    ];

    private static (int Token, int Offset) FindIlCoordinate(
        Type type,
        string methodName,
        ILOpCode opcode)
    {
        // This suite inspects its own assembly, whose MethodDef ordering changes
        // whenever tests are added.
        MethodInfo method = type.GetMethod(
            methodName,
            BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.Static
                | BindingFlags.DeclaredOnly)!;
        byte[] il = method.GetMethodBody()!.GetILAsByteArray()!;
        int offset = InstructionDecoder.Decode(il)
            .FirstOrDefault(instruction => instruction.OpCode == opcode)
            ?.Offset
            ?? -1;
        Assert.True(
            offset >= 0,
            $"opcode {opcode} not found in {methodName}");
        return (method.MetadataToken, offset);
    }

    /// <summary>
    /// Asserts the block-separation invariant over one rendered aggregate Library document:
    /// it opens with its title heading, every <c>##</c> heading is preceded by exactly one blank
    /// line, and it carries no trailing whitespace.
    /// </summary>
    private static void AssertBlocksSeparatedByOneBlankLine(string output, string expectedTitlePrefix)
    {
        Assert.DoesNotContain('\r', output);

        // The document's own end, not the harness's. OutputFormatter.WriteLfLine appends the
        // terminating newline itself, so asserting output ends with one asserts WriteLfLine;
        // strip it and require what the assembling TrimEnd is actually for -- that the document
        // carries no trailing whitespace. Every block contributes a trailing blank line, so
        // without that TrimEnd the document would end "\n\n\n".
        Assert.EndsWith("\n", output, StringComparison.Ordinal);
        var document = output[..^1];
        Assert.Equal(document.TrimEnd(), document);

        var lines = document.Split('\n');

        // The document's start, which the heading loop cannot reach: the title block is one
        // heading line naming the package, followed by exactly one blank line. A bare
        // StartsWith("# ") would admit anything prepended above the title that is itself a heading.
        Assert.StartsWith(expectedTitlePrefix, lines[0], StringComparison.OrdinalIgnoreCase);
        Assert.True(
            lines[1].Length == 0,
            $"expected a blank line after the title, found '{lines[1]}'");

        var headings = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("## ", StringComparison.Ordinal))
                continue;

            headings++;
            Assert.True(i >= 2, $"'{lines[i]}' has no room for a preceding blank line");
            Assert.True(
                lines[i - 1].Length == 0,
                $"expected a blank line before '{lines[i]}', found '{lines[i - 1]}'");
            Assert.True(
                lines[i - 2].Length != 0,
                $"expected exactly one blank line before '{lines[i]}', found more than one");
        }

        Assert.True(
            headings >= 2,
            $"expected at least two section headings, so that separation between blocks is "
                + $"exercised and not just the boundary below the title, got:\n{output}");
    }

    private static string ExtractSectionName(string line)
    {
        if (line.StartsWith('|'))
        {
            var cells = line.Split('|', StringSplitOptions.TrimEntries);
            return cells.Length > 1 ? cells[1] : line.Trim();
        }

        var marker = line.IndexOf("  section", StringComparison.Ordinal);
        return marker >= 0 ? line[..marker].TrimEnd() : line.TrimEnd();
    }

    private static void AssertOnlyPerformanceAnalysisWarnings(
        string error,
        string? terminalMessage = null)
    {
        string[] lines = error.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);
        int warningCount = terminalMessage is null
            ? lines.Length
            : lines.Length - 1;

        Assert.True(warningCount > 0, error);
        if (terminalMessage is not null)
        {
            Assert.Equal(terminalMessage, lines[^1]);
        }

        Assert.All(
            lines.Take(warningCount),
            line =>
            {
                Assert.StartsWith(
                    "Warning: performance analysis incomplete for ",
                    line,
                    StringComparison.Ordinal);
                Assert.Contains(
                    ": BadImageFormatException: ",
                    line,
                    StringComparison.Ordinal);
            });
    }

    private static JsonElement FirstPerformanceRow(string json)
    {
        var rows = PerformanceRows(json);
        return rows.Count > 0
            ? rows[0]
            : throw new InvalidOperationException("no performance rows in output");
    }

    private static List<JsonElement> PerformanceRows(string json)
    {
        using var document = JsonDocument.Parse(json.Trim());
        List<JsonElement> rows = [];
        if (document.RootElement.TryGetProperty("performance", out var performance))
        {
            foreach (var kind in performance.EnumerateObject())
            {
                if (kind.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var row in kind.Value.EnumerateArray())
                    {
                        rows.Add(row.Clone());
                    }
                }
            }
        }

        return rows;
    }

    /// <summary>
    /// Splits captured CLI output into non-empty lines independently of the host's line ending.
    /// </summary>
    /// <remarks>
    /// Product printers emit LF on every platform, while some console paths still terminate with
    /// the ambient newline. Splitting on <see cref="Environment.NewLine"/> therefore yields a
    /// single line on Windows against LF output, which does not fail loudly — it makes every
    /// per-line assertion vacuous, so <c>Where(...)</c> filters simply match nothing. Splitting on
    /// '\n' and trimming '\r' is correct for either ending and stays correct as the remaining
    /// CRLF console paths move to LF in #3596.
    /// </remarks>
    private static string[] SplitOutputLines(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 0)
            .ToArray();

    private static List<(string Name, string Kind)> ExtractDiscoveryRows(string output)
    {
        List<(string Name, string Kind)> rows = [];
        foreach (var line in SplitOutputLines(output))
        {
            if (!line.StartsWith('|'))
                continue;
            var cells = line.Split('|', StringSplitOptions.TrimEntries);
            if (cells.Length < 4 || cells[1] == "Name" || cells[1].All(ch => ch == '-'))
                continue;
            rows.Add((cells[1], cells[2]));
        }

        return rows;
    }

    private static string? TryExtractSectionBody(string output, string sectionName)
    {
        // Blank lines are significant to body extraction, so this splits without
        // RemoveEmptyEntries rather than reusing SplitOutputLines — but for the same
        // ending-agnostic reason documented there.
        var lines = output.Split('\n').Select(l => l.TrimEnd('\r'));
        var header = "## " + sectionName;
        List<string> body = [];
        var inSection = false;
        var found = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (inSection)
                    break;
                inSection = string.Equals(line, header, StringComparison.Ordinal);
                found |= inSection;
                continue;
            }

            if (inSection)
                body.Add(line);
        }

        return found ? string.Join('\n', body).Trim() : null;
    }

    static LibraryInspection FailedOptimizationInspection(
        bool composedBodyShapes = false)
    {
        var inspection = new LibraryInspection
        {
            FileName = "broken.dll",
            BodyKindQueryOptions = composedBodyShapes
                ? new BodyKindQueryOptions
                {
                    Kind = "ArrayCreationExpression",
                }
                : BodyKindQueryOptions.Default,
            PerformanceTriageOptions = composedBodyShapes
                ? new PerformanceTriageOptions
                {
                    Shapes = ["small-array"],
                }
                : PerformanceTriageOptions.Default,
        };
        LibraryMetadataService.ApplyOptimizationOpportunitiesResult(
            inspection.FileName,
            inspection,
            new Output.VerboseLogger(false),
            new OptimizationOpportunitiesResult.Failed(
                new IOException("body index failed")));
        return inspection;
    }

    static LibraryInspection FailedResourceTriageInspection()
    {
        var subject = new FindingSubject("fixture", "fixture");
        return new LibraryInspection
        {
            FileName = "Lib.dll",
            ResourceLifecycleInspection =
                new FindingInspection<ResourceLifecycleOccurrence>.Failed(
                    new InspectionError(
                        subject,
                        AnalysisFindings.ResourceLifecycleDescriptor,
                        "fixture failure")),
        };
    }

    /// <summary>
    /// A package exercising every <c>Files:</c> family root at once: <c>lib/</c>, <c>ref/</c>,
    /// <c>runtimes/</c>, a markdown file, and the <c>.nuspec</c> manifest.
    /// </summary>
    private static (string PackagePath, string TempDir) CreateLocalLayoutPackage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"package-test-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(tempDir, "content");
        var libDir = Path.Combine(packageRoot, "lib", "net8.0");
        var refDir = Path.Combine(packageRoot, "ref", "net8.0");
        var runtimeDir = Path.Combine(packageRoot, "runtimes", "win-x64", "native");
        Directory.CreateDirectory(libDir);
        Directory.CreateDirectory(refDir);
        Directory.CreateDirectory(runtimeDir);
        File.Copy(TestAssemblyPath, Path.Combine(libDir, "Layout.dll"));
        File.Copy(TestAssemblyPath, Path.Combine(refDir, "Layout.dll"));
        File.WriteAllText(Path.Combine(runtimeDir, "layout.native.txt"), "native");
        File.WriteAllText(Path.Combine(packageRoot, "README.md"), "readme");
        File.WriteAllText(
            Path.Combine(packageRoot, "Test.Layout.nuspec"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>Test.Layout</id>
                <version>1.0.0</version>
                <authors>Tests</authors>
                <description>Layout fixture</description>
              </metadata>
            </package>
            """);

        var packagePath = Path.Combine(tempDir, "Test.Layout.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);
        return (packagePath, tempDir);
    }

    private static void AssertContainmentWarning(
        string error,
        string source)
    {
        Assert.Contains(
            $"Warning: Skill document '{source}' was omitted because 1 text range requires containment.",
            error,
            StringComparison.Ordinal);
        Assert.Contains("U+202E (Format)", error, StringComparison.Ordinal);
        Assert.DoesNotContain("\u202E", error, StringComparison.Ordinal);
    }
}

public interface EmptyDiscoveryFixture
{
}

public sealed class MemberGenericSelectorFixture
{
    public string GenericChoice(string value) => value;
    public T GenericChoice<T>(T value) => value;
}

public interface IGenericExplicitInterfaceFixture<T>
{
    void Map<U>(U value);
    void Map<U, V>(U first, V second);
}

public interface IBodylessFindingCensusFixture
{
    void Run();
}

public sealed class GenericExplicitInterfaceFixture<T>
    : IGenericExplicitInterfaceFixture<T>
{
    void IGenericExplicitInterfaceFixture<T>.Map<U>(U value)
    {
    }

    void IGenericExplicitInterfaceFixture<T>.Map<U, V>(U first, V second)
    {
    }
}

public sealed class Operators<T>
{
    public T Apply(T value) => value;
    public TResult Convert<TResult>(T value) => default!;
    public TResult Convert<TResult>(IEnumerable<T> values) => default!;
}

public interface ICommandExecutionReadonlyValue
{
    int Value { get; set; }
}

public struct CommandExecutionReadonlySourceDiffFixture : ICommandExecutionReadonlyValue
{
    int _value;

    public CommandExecutionReadonlySourceDiffFixture(int value) => _value = value;

    public readonly int Value => _value;

    readonly int ICommandExecutionReadonlyValue.Value
    {
        get => _value;
        set => GC.KeepAlive(value);
    }
}

public interface ISourceDiffPropertyShapeFixture
{
    Dictionary<string, int> Map { get; }
    (int Count, string Name) Pair { get; }
    ref readonly int Reference { get; }
    int Initial { get; init; }
    int Item { get; set; }
    int Chars { get; set; }
}

public sealed class SourceDiffPropertyShapeFixture : ISourceDiffPropertyShapeFixture
{
    Dictionary<string, int> _map = [];
    (int Count, string Name) _pair = (1, "value");
    int _value;

    public SourceDiffPropertyShapeFixture(int value) => _value = value;

    Dictionary<string, int> ISourceDiffPropertyShapeFixture.Map => _map;

    (int Count, string Name) ISourceDiffPropertyShapeFixture.Pair => _pair;

    ref readonly int ISourceDiffPropertyShapeFixture.Reference => ref _value;

    int ISourceDiffPropertyShapeFixture.Initial
    {
        get => _value;
        init => _value = value;
    }

    int ISourceDiffPropertyShapeFixture.Item
    {
        get => _value;
        set => _value = value;
    }

    int ISourceDiffPropertyShapeFixture.Chars
    {
        get => _value;
        set => _value = value;
    }
}

public interface ISourceDiffIndexerFixture
{
    [System.Runtime.CompilerServices.IndexerName("Lookup")]
    int this[int index] { get; set; }
}

public sealed class SourceDiffIndexerFixture : ISourceDiffIndexerFixture
{
    int _value;

    int ISourceDiffIndexerFixture.this[int index]
    {
        get => _value + index;
        set => _value = value - index;
    }
}

public sealed class CommandExecutionSourceDiffFixture
{
    public int AddOne(int value)
    {
        return value + 1;
    }

}

public sealed class ConstructorSourceCaseFixture
{
    readonly object _gate = new();

    public ConstructorSourceCaseFixture()
    {
        GC.KeepAlive(_gate);
    }
}

/// <summary>
/// Two allocations at different depths: one on the body's own base column and
/// one nested inside a loop. The caret gesture must point exactly at both, which
/// is only possible because the caret block is hoisted out of the body indent.
/// </summary>
public sealed class CommandCaretGestureFixture
{
    public static int DisposedOnlyUsingResource()
    {
        int value = 0;
        using (new MemoryStream())
        {
            value = 1;
        }
        return value;
    }

    public string Pump(int n)
    {
        var sink = new List<object>();
        for (int i = 0; i < n; i++)
        {
            sink.Add(new object());
        }
        return sink.Count.ToString();
    }

    public string Make() => new object().ToString() ?? "";

    public static bool StringEqual(string left, string right) => left == right;

    public static int ReadMatrix(int[,] values, int row, int column) => values[row, column];

    public static int[,] MakeMatrix() => new int[2, 3];

    // Four boxes on one line, at four distinct IL offsets: the shape that makes
    // a line's facts disagree about what to underline. System.Tuple`8.Equals is
    // the extreme of it in the wild, with 16 facts on one line.
    public static bool DenseBoxes<T1, T2, T3, T4>(
        EqualityComparer<object> comparer, T1 a1, T2 a2, T3 a3, T4 a4,
        object b1, object b2, object b3, object b4)
        => comparer.Equals(a1, b1) && comparer.Equals(a2, b2)
            && comparer.Equals(a3, b3) && comparer.Equals(a4, b4);
}

public sealed class CommandInitializerOnlyFixture
{
    public int Value = 42;
}

public static class FactsTableFixture
{
    public static object BoxInt(int value) => value;

    public static object[] MultipleFacts(int value)
        => [value, new object()];
}

public static class FactsHeaderFixture
{
    public static int Hot(int value) => value + 1;

    public static int Caller01(int value) => Hot(value);
    public static int Caller02(int value) => Hot(value);
    public static int Caller03(int value) => Hot(value);
    public static int Caller04(int value) => Hot(value);
    public static int Caller05(int value) => Hot(value);
    public static int Caller06(int value) => Hot(value);
    public static int Caller07(int value) => Hot(value);
    public static int Caller08(int value) => Hot(value);
    public static int Caller09(int value) => Hot(value);
    public static int Caller10(int value) => Hot(value);
    public static int Caller11(int value) => Hot(value);
    public static int Caller12(int value) => Hot(value);
    public static int Caller13(int value) => Hot(value);
    public static int Caller14(int value) => Hot(value);
    public static int Caller15(int value) => Hot(value);
    public static int Caller16(int value) => Hot(value);
    public static int Caller17(int value) => Hot(value);
    public static int Caller18(int value) => Hot(value);
    public static int Caller19(int value) => Hot(value);
    public static int Caller20(int value) => Hot(value);
}

public static class CostOverlayFixture
{
    public static int Caller(int count) => HotCallee(count);

    public static int LowSignal(int value) => value + 1;

    public static int CallsLowSignal(int value) => LowSignal(value);

    public static int HotCallee(int count)
    {
        int total = 0;
        for (int i = 0; i < count; i++)
            total += new object().GetHashCode();
        return total;
    }

    public static int CallsExceptionOnly(string value) => ExceptionOnly(value);

    public static int ExceptionOnly(string value)
    {
        if (value.Length == 0)
            throw new FormatException();
        return value.Length;
    }

    public static int CallsStackalloc(int value) => Stackalloc(value);

    public static int Stackalloc(int value)
    {
        Span<int> values = stackalloc int[1];
        values[0] = value;
        return values[0];
    }

    public static unsafe int CallsPointerDeref(int* value)
        => PointerDeref(value);

    public static unsafe int PointerDeref(int* value)
        => *value;
}

public static class FidelityCauseFixture
{
    public static void EmptyBody()
    {
    }

    public static Type TypedReferenceType(ref int value)
    {
        TypedReference reference = __makeref(value);
        return __reftype(reference);
    }
}
