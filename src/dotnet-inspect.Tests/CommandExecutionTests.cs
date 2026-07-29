using System.Buffers;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using DotnetInspector.Fixtures;
using DotnetInspector.Commands;
using DotnetInspector.Core;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Tests;

/// <summary>
/// Integration tests that verify actual command execution produces correct output.
/// Uses platform libraries and the test assembly itself as data sources — no network required.
/// </summary>
[Collection("Console")]
[Trait("Speed", "Slow")]
public partial class CommandExecutionTests
{
    private static readonly string TestAssemblyPath =
        typeof(CommandExecutionTests).Assembly.Location;

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
        File.WriteAllText(Path.Combine(packageRoot, readmeFile), readmeText);
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
            var packageRoot = Path.Combine(tempDir, "packages", package.Id.ToLowerInvariant(), package.Version.ToLowerInvariant());
            var files = new List<string> { $"{package.Id.ToLowerInvariant()}.nuspec" };
            if (!package.OmitReadme)
                files.Add(package.ReadmeFile.Replace('\\', '/'));
            if (package.AgentsText != null)
                files.Add("AGENTS.md");
            if (package.ProjectText != null)
                files.Add("PROJECT.md");
            files.AddRange((package.Skills ?? []).Select(skill => skill.Path.Replace('\\', '/')));

            var fileEntries = string.Join(", ", files.Select(JsonString));
            return $"{JsonString($"{package.Id}/{package.Version}")}: {{ \"type\": \"package\", \"path\": {JsonString(packageRoot.Replace('\\', '/'))}, \"files\": [ {fileEntries} ] }}";
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
            args = CommandLineBuilder.PreprocessArgs(args);
            // Mirror Program.cs: the stale `--head N`/`--tail N` spelling is a raw-token
            // question, so it is answered by the product before parsing rather than by
            // a validator. Call the same product method the entry point calls; do not
            // reimplement the check here.
            if (CommandLineBuilder.TryGetStaleArgumentError(args, out var staleArgumentError))
            {
                Console.Error.WriteLine($"Error: {staleArgumentError}");
                return 1;
            }

            var root = CommandLineBuilder.CreateRootCommand();
            var result = root.Parse(args);
            // Mirror Program.cs: surface parse/validation errors (including the --rows
            // head/tail window validator) as a clean "Error: ..." line on stderr with
            // exit 1, instead of letting InvokeAsync print usage help.
            if (result.Errors.Count > 0)
            {
                foreach (var error in result.Errors)
                {
                    var message = error.Message.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                        ? error.Message
                        : $"Error: {error.Message}";
                    Console.Error.WriteLine(message);
                }
                return 1;
            }
            try
            {
                return await CommandLineBuilder.InvokeAsync(result);
            }
            catch (RowWindowValidationException ex)
            {
                // Defensive: matches the Program.cs safety-net catch.
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        });
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

    [Theory]
    [InlineData("library")]
    [InlineData("type")]
    [InlineData("member")]
    public async Task PerformanceTriagePredicates_DoNotAlterDiscovery(string command)
    {
        string[] BaseArgs() => command switch
        {
            "library" => [command, TestAssemblyPath, "--discover"],
            "type" => [command, nameof(OutputFormatterTests), "--library", TestAssemblyPath, "--discover"],
            "member" => [command, nameof(OutputFormatterTests), "--library", TestAssemblyPath, "--discover"],
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null)
        };

        var baseline = await RunAppAsync(BaseArgs());
        var filtered = await RunAppAsync([.. BaseArgs(), "--loop"]);
        var whereFiltered = await RunAppAsync([.. BaseArgs(), "--where", "Shape=box-value-type"]);

        Assert.Equal(0, baseline.Exit);
        Assert.Equal(0, filtered.Exit);
        Assert.Equal(0, whereFiltered.Exit);
        Assert.Equal(baseline.Output, filtered.Output);
        Assert.Equal(baseline.Output, whereFiltered.Output);
    }

    [Fact]
    public async Task PerformanceTriageShape_UnknownShapeReportsValidShapes()
    {
        var (exit, output, error) = await RunAppAsync("library", TestAssemblyPath, "--triage-shape", "typo-shape", "--tsv");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Unknown Performance Triage shape 'typo-shape'", error);
        Assert.Contains("capturing-delegate", error);
    }

    [Fact]
    public async Task PerformanceTriageAllocationFanout_ReportsOncePathQuantity()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "--triage-shape", "allocation-fanout",
            "--where", "Member=*CreateAllocationFanout*",
            "--where", "OncePaths>=4",
            "--order-by", "OncePaths desc",
            "--json",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("\"shape\": \"allocation-fanout\"", output);
        Assert.Contains("\"provenance\": \"aggregate\"", output);
        Assert.Contains("\"direct_sites\": 1", output);
        Assert.Contains("\"once_paths\": 4", output);
    }

    [Fact]
    public async Task PerformanceTriageAllocationFanout_IsOptIn()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Performance Triage",
            "--where", "Member=*CreateAllocationFanout*",
            "--json",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("\"shape\": \"allocation-fanout\"", output);
    }

    [Fact]
    public async Task PerformanceTriageAllocationFanout_IncludesDirectCallerLoopEvidence()
    {
        string assemblyPath = FixtureCatalog.AnalysisCallerLoop.AssemblyPath();
        var (exit, output, error) = await RunAppAsync(
            "library", assemblyPath,
            "--triage-shape", "allocation-fanout",
            "--where", "Member=*BoxDirect*",
            "--where", "CallerLoop=direct",
            "--json",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("\"shape\": \"allocation-fanout\"", output);
        Assert.Contains("\"caller_loop\": \"direct\"", output);
        Assert.Contains("\"caller_loop_depth\": 1", output);
        Assert.Contains("\"direct_sites\": 1", output);
    }

    [Fact]
    public async Task ResourceTriage_IsExplicitAndUsesExactBoundaryEvidence()
    {
        var defaultResult = await RunAppAsync(
            "library",
            TestAssemblyPath,
            "-v:m",
            "--tips",
            "q");
        var markdown = await RunAppAsync(
            "library",
            TestAssemblyPath,
            "-S",
            SectionNames.ArrayPoolEscapes,
            "--tips",
            "q");
        var jsonl = await RunAppAsync(
            "library",
            TestAssemblyPath,
            "-S",
            SectionNames.ArrayPoolEscapes,
            "--jsonl",
            "--tips",
            "q");
        var tsv = await RunAppAsync(
            "library",
            TestAssemblyPath,
            "-S",
            SectionNames.ArrayPoolEscapes,
            "--tsv",
            "--tips",
            "q");
        var empty = await RunAppAsync(
            "library",
            FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
            "-S",
            SectionNames.ArrayPoolEscapes,
            "--tips",
            "q");

        Assert.Equal(0, defaultResult.Exit);
        Assert.DoesNotContain(SectionNames.ArrayPoolEscapes, defaultResult.Output);

        Assert.Equal(0, markdown.Exit);
        Assert.Empty(markdown.Error);
        Assert.Contains("## Array Pool Escapes", markdown.Output);
        Assert.Contains("ReadBeforeReturn", markdown.Output);
        Assert.DoesNotContain(
            nameof(ResourceTriageFixture.TransformWithUnrelatedReadAfterReturn),
            markdown.Output);
        Assert.Contains("analysis.resource-lifecycle", markdown.Output);
        Assert.Contains("pool-churn-on-exception", markdown.Output);
        Assert.Contains("System.IO.Stream::Read", markdown.Output);

        Assert.Equal(0, jsonl.Exit);
        Assert.Empty(jsonl.Error);
        Assert.Contains("\"finding\":\"analysis.resource-lifecycle\"", jsonl.Output);
        Assert.Contains("\"provenance\":\"exact\"", jsonl.Output);
        Assert.Contains("\"actionability\":\"untrusted-input boundary\"", jsonl.Output);
        Assert.Contains("\"acquire_il\":\"IL_", jsonl.Output);
        Assert.Contains("\"boundary_il\":\"IL_", jsonl.Output);

        Assert.Equal(0, tsv.Exit);
        Assert.Empty(tsv.Error);
        Assert.StartsWith(
            "member\tcandidate\tfinding\tprovenance\tresource\tshape\timpact\tactionability\tboundary\tacquire_il\tboundary_il",
            tsv.Output);

        Assert.Equal(0, empty.Exit);
        Assert.Empty(empty.Error);
        // An empty escape scan is silently suppressed (row-presence ShowWhenProperty, no EmptyText) —
        // matching the Performance sections, so absence means "no candidates", never a noisy header.
        Assert.DoesNotContain("## Array Pool Escapes", empty.Output);
        Assert.DoesNotContain(
            "No actionable resource lifecycle candidates found.",
            empty.Output);
    }

    [Fact]
    public async Task PerformanceTriageWhere_FiltersRowsByField()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Performance Triage",
            "--where", "Allocation=boxed *",
            "--where", "Path=straight-line",
            "--json",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("\"shape\": \"box-value-type\"", output);
        Assert.Contains("\"allocation\": \"boxed System.Int32\"", output);
        Assert.Contains("\"path\": \"straight-line\"", output);
        Assert.DoesNotContain("\"shape\": \"stackalloc-candidate\"", output);
    }

    [Fact]
    public async Task PerformanceTriage_ExposesAndFiltersFindingProvenance()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Performance Triage",
            "--where", "Finding=analysis.allocation",
            "--where", "Operation=box",
            "--top", "1",
            "--json",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("\"candidate\": \"pt~", output);
        Assert.Contains("\"finding\": \"analysis.allocation\"", output);
        Assert.Contains("\"provenance\": \"exact\"", output);
        Assert.Contains("\"operation\": \"box\"", output);
        Assert.Contains("\"token\": \"0x", output);
    }

    [Theory]
    [InlineData("library")]
    [InlineData("type")]
    [InlineData("member")]
    public async Task PerformanceTriage_ExposesDirectCallerLoopEvidenceWithoutChangingLocalLoop(string command)
    {
        const string typeName = "ILInspector.Analysis.CallerLoopFixtures.CallerLoopFixture";
        string assemblyPath = FixtureCatalog.AnalysisCallerLoop.AssemblyPath();
        string[] sourceArgs = command switch
        {
            "library" => [command, assemblyPath],
            "type" => [command, typeName, "--library", assemblyPath],
            "member" => [command, typeName, "BoxDirect", "--library", assemblyPath],
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        };

        var (exit, output, error) = await RunAppAsync(
            [
                .. sourceArgs,
                "-S", "Performance Triage",
                "--where", "CallerLoop=direct",
                "--where", "CallerLoopDepth>=1",
                "--order-by", "CallerLoopDepth desc",
                command == "library" ? "--json" : "--jsonl",
                "--tips", "q",
            ]);

        // Library scope surfaces rich diagnostics in the nested `performance` JSON (pretty-printed);
        // type/member scope keeps the flat triage row (compact JSONL).
        string sep = command == "library" ? ": " : ":";
        string depth = command == "library" ? "\"caller_loop_depth\": 1" : "\"caller_loop_depth\":\"1\"";

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("BoxDirect(int)", output);
        Assert.Contains($"\"caller_loop\"{sep}\"direct\"", output);
        Assert.Contains(depth, output);
        Assert.Contains("InvokeDirectInLoop", output);
        Assert.Contains($"\"loop\"{sep}\"\"", output);
        Assert.Contains($"\"candidate\"{sep}\"pt~", output);
    }

    [Theory]
    [InlineData("asc")]
    [InlineData("desc")]
    public async Task PerformanceTriageOrderByCallerLoopDepth_PutsMissingEvidenceLast(string direction)
    {
        string assemblyPath = FixtureCatalog.AnalysisCallerLoop.AssemblyPath();
        var (exit, output, error) = await RunAppAsync(
            "library", assemblyPath,
            "-S", "Performance Triage",
            "--order-by", $"CallerLoopDepth {direction}",
            "--top", "1",
            "--json",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("\"caller_loop\": \"direct\"", output);
        Assert.Contains("\"caller_loop_depth\": 1", output);
    }

    [Fact]
    public async Task PerformanceTriageWhere_NormalizesMetadataToken()
    {
        var baseline = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Performance Triage",
            "--where", "Finding=analysis.allocation",
            "--top", "1",
            "--json",
            "--tips", "q");

        Assert.Equal(0, baseline.Exit);
        Assert.Empty(baseline.Error);
        string token = FirstPerformanceRow(baseline.Output).GetProperty("token").GetString()!;
        string unpaddedToken = $"0x{token[2..].TrimStart('0')}";

        var filtered = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Performance Triage",
            "--where", $"Token={unpaddedToken}",
            "--json",
            "--tips", "q");

        Assert.Equal(0, filtered.Exit);
        Assert.Empty(filtered.Error);
        Assert.Contains($"\"token\": \"{token}\"", filtered.Output);
    }

    [Fact]
    public async Task PerformanceTriageWhere_MemberAcceptsDisplayedShortSignature()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", nameof(FactsTableFixture),
            "--library", TestAssemblyPath,
            "-S", "Performance Triage",
            "--where", "Member=BoxInt(int)",
            "--tsv",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("BoxInt(int)", output);
        Assert.Contains("\tbox-value-type\t", output);
    }

    [Fact]
    public async Task PerformanceTriageOrderBy_OrdersBeforeTop()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Performance: Boxing",
            "--where", "Shape=box-value-type",
            "--order-by", "RootReach desc",
            "--top", "1",
            "--json",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output.Trim());
        var boxing = document.RootElement.GetProperty("performance").GetProperty("boxing");
        Assert.Equal(1, boxing.GetArrayLength());
        Assert.Equal("box-value-type", boxing[0].GetProperty("shape").GetString());
    }

    [Fact]
    public async Task PerformanceTriageOrderBy_AcceptsHumanColumnNames()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Performance Triage",
            "--where", "Path Confidence=dominates-return",
            "--order-by", "Root Reach desc",
            "--top", "1",
            "--tsv",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var rows = output.TrimEnd().Split('\n');
        Assert.Single(rows.Skip(1));
    }

    [Fact]
    public async Task PerformanceTriageWhere_FiltersByPostDominance()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Performance Triage",
            "--where", "Post Dominance=return-post-dominates",
            "--order-by", "PostDominance desc,RootReach desc",
            "--json",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var rows = PerformanceRows(output);
        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.Equal(
            "return-post-dominates",
            row.GetProperty("post_dominance").GetString()));
    }

    [Fact]
    public async Task PerformanceTriageCount_AppliesWhereBeforeTop()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "Performance: Boxing",
            "--where", "Shape=box-value-type",
            "--top", "1",
            "--count",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.True(int.TryParse(output.Trim(), out var count), output);
        Assert.True(count > 1, $"expected post-filter count before --top, got {count}");
    }

    // ===== Performance sections (kind-scoped decomposition of the library "Performance Triage" monolith) =====

    [Fact]
    public async Task PerformanceSections_NotInDefaultView()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-v:m", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("## Performance", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task PerformanceSection_SingleKind_RendersOnlyThatKind()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Performance: Boxing", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Performance: Boxing", output);
        Assert.DoesNotContain("## Performance: Arrays", output);
        Assert.DoesNotContain("## Performance: Closures", output);
    }

    [Fact]
    public async Task PerformanceAsyncEvidence_RendersAsCodeSpan_WithoutHtmlEscapingCompilerName()
    {
        // Evidence embeds compiler-generated names with angle brackets (e.g. the async state
        // machine <GetAsyncEnumerator>d__1). It must render as a code span like the Member and
        // Allocation columns, showing the brackets literally — not HTML-escaped as &lt;/&gt;.
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Performance: Async", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Performance: Async", output);
        Assert.Contains("async state-machine allocation (<", output);
        Assert.DoesNotContain("async state-machine allocation (&lt;", output);

        // Machine output stays raw (no code-span markup, unescaped brackets).
        var (tsvExit, tsv, _) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Performance: Async", "--tsv", "--tips", "q");
        Assert.Equal(0, tsvExit);
        Assert.Contains("async state-machine allocation (<", tsv);
        Assert.DoesNotContain("&lt;", tsv);
    }

    [Fact]
    public async Task PerformanceTriageEvidence_TypeScope_RendersAsCodeSpan_WithoutHtmlEscapingGenerics()
    {
        // The type/member Performance Triage lens has the same Evidence column; a generic value-type
        // box (box System.Memory<T>) must render as a code span with literal angle brackets, not the
        // HTML-escaped &lt;T&gt;. The Allocation column already renders it literally.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text.Json.Serialization.Converters.MemoryConverter",
            "--platform", "System.Text.Json", "--all", "-S", "Performance Triage", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("box System.Memory<T>", output);
        Assert.DoesNotContain("box System.Memory&lt;T&gt;", output);
    }

    [Fact]
    public async Task PerformanceGroup_RendersMultipleKindSections()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "@Performance", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Performance: Boxing", output);
        Assert.Contains("## Performance: Arrays", output);
        Assert.Contains("## Performance: Closures and Delegates", output);
    }

    [Fact]
    public async Task PerformanceGroup_JsonEmitsNestedProjection_NotRetiredMonolithKey()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "@Performance", "--json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);

        using var document = JsonDocument.Parse(output.Trim());
        Assert.False(
            document.RootElement.TryGetProperty("optimization_opportunities", out _),
            "retired monolith key must be absent");

        var performance = document.RootElement.GetProperty("performance");
        Assert.True(performance.TryGetProperty("boxing", out var boxing));
        Assert.True(boxing.GetArrayLength() > 0);
        Assert.True(performance.TryGetProperty("arrays", out _));
    }

    [Fact]
    public async Task PerformanceKind_AbsentWhenEmpty()
    {
        // A tiny fixture assembly with no async candidates: the async kind must be absent, not an
        // empty section (il-offset gating parity).
        var (exit, output, error) = await RunAppAsync(
            "library", FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
            "-S", "Performance: Async", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("## Performance: Async", output);
    }

    [Fact]
    public async Task PerformanceGroup_CountEmitsPerKindMap()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "@Performance", "--count", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("| Section | Count |", output);
        Assert.Contains("Performance: Boxing", output);
        // Empty kinds still report a zero row so agents can cheaply probe the whole category.
        Assert.Contains("Performance: Other", output);
    }

    [Fact]
    public async Task PerformanceLegacyName_RedirectsToGroup()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Performance", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Performance: Boxing", output);
        Assert.Contains("## Performance: Arrays", output);
    }

    [Fact]
    public async Task PerformanceGroup_TabularRendersSingleKindLabeledTable()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "@Performance", "--tsv", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // The flattened group renders as one self-describing table: exactly one header, and its
        // leading column is the kind label so each row says which performance kind it belongs to.
        var headerCount = lines.Count(line => line.StartsWith("kind\t", StringComparison.Ordinal));
        Assert.Equal(1, headerCount);
        Assert.StartsWith("kind\tmember\t", lines[0]);
        // Rows from more than one kind are present and labeled (e.g. Boxing and Arrays).
        var kinds = lines.Skip(1).Select(l => l.Split('\t')[0]).Distinct().ToList();
        Assert.Contains("Boxing", kinds);
        Assert.Contains("Arrays", kinds);
    }

    [Fact]
    public async Task PerformanceGroup_JsonlEmitsOnlyValidRecords_NoBlankSeparators()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "@Performance", "--jsonl", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(lines);
        // Every emitted line must parse as JSON (no blank inter-section separators), and each record
        // must carry its kind label so the flattened stream is self-describing.
        foreach (var line in output.Replace("\r", "").Split('\n'))
        {
            if (line.Length == 0)
                continue;
            using var doc = JsonDocument.Parse(line);
            Assert.True(doc.RootElement.TryGetProperty("kind", out _));
        }
        Assert.DoesNotContain(output.TrimEnd('\n').Split('\n'), line => line.Length == 0);
    }

    [Fact]
    public async Task PerformanceGroup_NoHeaderTabular_PreservesEveryRow_NoBlankSeparators()
    {
        // With --no-header the flattened table emits no header line, so every line is a data row and
        // identical rows must all survive. Row count must match the with-header data-row count, and
        // no blank section separators may leak into the stream.
        var withHeader = await RunAppAsync(
            "library", "System.Text.Json", "-S", "@Performance", "--tsv", "--order-by", "Allocation", "--tips", "q");
        var noHeader = await RunAppAsync(
            "library", "System.Text.Json", "-S", "@Performance", "--tsv", "--no-header", "--order-by", "Allocation", "--tips", "q");

        Assert.Equal(0, withHeader.Exit);
        Assert.Equal(0, noHeader.Exit);

        var dataRows = withHeader.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(line => !line.StartsWith("kind\t", StringComparison.Ordinal));
        var noHeaderRows = noHeader.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(dataRows, noHeaderRows.Length);
        Assert.DoesNotContain(noHeader.Output.TrimEnd('\n').Split('\n'), line => line.Length == 0);
    }

    [Fact]
    public async Task PerformanceTriageShape_SectionMappingIsCaseInsensitive()
    {
        // A differently-cased --triage-shape is accepted by validation; it must resolve to the same
        // kind section its findings bucket into, not silently route to Performance: Other.
        var lower = await RunAppAsync(
            "library", "System.Text.Json", "--triage-shape", "box-value-type", "--count", "--tips", "q");
        var upper = await RunAppAsync(
            "library", "System.Text.Json", "--triage-shape", "BOX-VALUE-TYPE", "--count", "--tips", "q");

        Assert.Equal(0, lower.Exit);
        Assert.Equal(0, upper.Exit);
        Assert.True(int.TryParse(lower.Output.Trim(), out var lowerCount) && lowerCount > 0, lower.Output);
        Assert.Equal(lower.Output.Trim(), upper.Output.Trim());
    }

    [Fact]
    public async Task PerformanceSections_CatalogHidden_CategoryDiscoverableAndDrillable()
    {
        // Bare -D (effective discovery) must not list the kind-scoped performance sections at the top
        // level — the @Performance category is their single discoverable entrypoint — yet they must
        // stay reachable by drilling into that category.
        var bare = await RunAppAsync("library", "System.Text.Json", "-D", "--tips", "q");
        Assert.Equal(0, bare.Exit);
        var bareSectionNames = bare.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.Contains("| section", StringComparison.Ordinal))
            .Select(ExtractSectionName)
            .ToArray();
        Assert.DoesNotContain(bareSectionNames, n => n.StartsWith("Performance: ", StringComparison.Ordinal));
        Assert.Contains(bare.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            l => ExtractSectionName(l) == "@Performance" && l.Contains("category", StringComparison.Ordinal));

        var drill = await RunAppAsync("library", "System.Text.Json", "-D", "@Performance", "--tips", "q");
        Assert.Equal(0, drill.Exit);
        Assert.Contains("Performance: Boxing", drill.Output);
        Assert.Contains("Performance: Other", drill.Output);
    }

    [Fact]
    public async Task PerformanceGroup_TableRendersOneHeader_AndRowsCapCountsDataRowsOnly()
    {
        // The flattened pretty table must be one aligned table: exactly one header regardless of how
        // many kinds contribute, and a --rows cap must yield header + N data rows (embedded per-kind
        // headers previously inflated the count and stole a row slot).
        const int cap = 5;
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "@Performance", "--table", "--rows", cap.ToString(),
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var headerCount = lines.Count(l => l.StartsWith("Kind", StringComparison.Ordinal) && l.Contains("Member", StringComparison.Ordinal));
        Assert.Equal(1, headerCount);
        // One header + cap data rows.
        Assert.Equal(cap + 1, lines.Length);
    }

    [Fact]
    public async Task PerformanceTriageWhere_UnknownFieldReportsSuggestion()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "--where", "Allocaton=boxed *",
            "--tsv",
            "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Field 'Allocaton' is not filterable", error);
        Assert.Contains("Allocation", error);
    }

    [Theory]
    [InlineData("RootReach>=abc", "expects an integer")]
    [InlineData("Confidence>=bogus", "expects one of low, medium, high")]
    public async Task PerformanceTriageWhere_InvalidValuesReportDiagnostics(string predicate, string expected)
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "--where", predicate,
            "--tsv",
            "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(expected, error);
    }

    [Fact]
    public async Task PerformanceTriageOrderBy_TriageCompositeMustBeStandalone()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "--order-by", "Triage desc,RootReach desc",
            "--tsv",
            "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Triage is a composite order", error);
    }

    [Fact]
    public async Task PerformanceTriageOrderBy_EmptyTermsReportDiagnostic()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "--order-by", ",",
            "--tsv",
            "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--order-by requires at least one field", error);
    }

    [Theory]
    [InlineData("DotnetInspector.Tests.CommandExecutionTests.NestedDrillTarget")]
    [InlineData("DotnetInspector.Tests.CommandExecutionTests+NestedDrillTarget")]
    public async Task TypeCommand_AllowsDrillingNonPublicNestedTypes(string typeName)
    {
        var (exit, output, error) = await RunAppAsync(
            "type", typeName,
            "--library", TestAssemblyPath,
            "--all",
            "-S", "Member Index",
            "-n", "80");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("NestedDrillTarget", output);
        Assert.Contains(".ctor", output);
    }

    [Theory]
    [InlineData("DotnetInspector.Tests.CommandExecutionTests.NestedDrillTarget")]
    [InlineData("DotnetInspector.Tests.CommandExecutionTests+NestedDrillTarget")]
    public async Task MemberCommand_AllowsDrillingNonPublicNestedConstructors(string typeName)
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeName, ".ctor:1",
            "--library", TestAssemblyPath,
            "--all",
            "-S", "Decompiled Source",
            "-n", "80");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("NestedDrillTarget", output);
        Assert.Contains("Value = value", output);
    }

    [Theory]
    [InlineData("DotnetInspector.Tests.CommandExecutionTests.NestedDrillTarget..ctor")]
    [InlineData("DotnetInspector.Tests.CommandExecutionTests.NestedDrillTarget..ctor:1")]
    [InlineData("DotnetInspector.Tests.CommandExecutionTests+NestedDrillTarget..ctor")]
    [InlineData("DotnetInspector.Tests.CommandExecutionTests+NestedDrillTarget..ctor:1")]
    public async Task MemberCommand_AllowsCopiedNestedConstructorSelector(string selector)
    {
        var (exit, output, error) = await RunAppAsync(
            "member", selector,
            "--library", TestAssemblyPath,
            "--all",
            "-S", "Decompiled Source",
            "-n", "80");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("NestedDrillTarget", output);
        Assert.Contains("Value = value", output);
    }

    [Theory]
    [InlineData("DotnetInspector.Tests.CommandExecutionTests.NestedDrillTarget")]
    [InlineData("DotnetInspector.Tests.CommandExecutionTests+NestedDrillTarget")]
    public async Task MemberCommand_AllowsCopiedNestedConstructorDigestSelector(string typeName)
    {
        var index = await RunAppAsync(
            "type", typeName,
            "--library", TestAssemblyPath,
            "--all",
            "-S", "Member Index",
            "-n", "80");
        Assert.Equal(0, index.Exit);
        var stable = index.Output
            .Split('\n')
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length >= 3 && parts[1] == "`.ctor`")
            .Select(parts => parts[2].Trim('`'))
            .Single();

        var selector = $"{typeName}.{stable}";
        var (exit, output, error) = await RunAppAsync(
            "member", selector,
            "--library", TestAssemblyPath,
            "--all",
            "-S", "Decompiled Source",
            "-n", "80");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("NestedDrillTarget", output);
        Assert.Contains("Value = value", output);
    }

    [Fact]
    public async Task MemberCommand_PlacesTypedConstructorChainOnDeclaration()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "DotnetInspector.Tests.CommandExecutionTests+ConstructorChainTarget", ".ctor:1",
            "--library", TestAssemblyPath,
            "--all",
            "-S", "Decompiled Source",
            "-n", "80");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("ConstructorChainTarget(int value) : base(value)", output);
        Assert.DoesNotContain("\n    : base(value)", output);
    }

    [Fact]
    public async Task InitializerOnlyConstructor_ProjectsThroughMemberAndTypeCommands()
    {
        string typeName = typeof(CommandInitializerOnlyFixture).FullName!;
        var member = await RunAppAsync(
            "member", typeName, ".ctor:1",
            "--library", TestAssemblyPath,
            "-S", "Decompiled Source",
            "--tips", "q");
        var type = await RunAppAsync(
            "type", typeName,
            "--library", TestAssemblyPath,
            "-S", "Decompiled Source",
            "--tips", "q");

        Assert.Equal(0, member.Exit);
        Assert.Empty(member.Error);
        Assert.Contains("CommandInitializerOnlyFixture()", member.Output);
        Assert.DoesNotContain("Value = 42", member.Output);
        Assert.DoesNotContain("DEC0003", member.Output);

        Assert.Equal(0, type.Exit);
        Assert.Empty(type.Error);
        Assert.Contains("public int Value = 42;", type.Output);
        Assert.DoesNotContain("DEC0003", type.Output);
    }

    [Fact]
    public async Task MemberCommand_DoesNotInferAsyncFromRenderedText()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "DotnetInspector.Tests.CommandExecutionTests+AwaitTextTarget", "AwaitText:1",
            "--library", TestAssemblyPath,
            "--all",
            "-S", "Decompiled Source",
            "-n", "80");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("\"await\"", output);
        Assert.DoesNotContain(" async ", output);
    }

    // ── bare router ───────────────────────────────────────────────────

    [Fact]
    public async Task BareName_PlatformLibrary_RoutesToLibrary()
    {
        var (exit, output, error) = await RunAppAsync("System.Text.Json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("# System.Text.Json.dll", output);
        Assert.Contains("## Library Info", output);
    }

    [Fact]
    public async Task BareName_PlatformNamespacePrefix_RoutesToTypePrefixBrowse()
    {
        var (exit, output, error) = await RunAppAsync("System.Text", "--tips", "q", "-n", "12");

        Assert.Equal(0, exit);
        Assert.Contains("Showing best-effort platform prefix matches for 'System.Text'", error);
        Assert.Contains("# System.Text", output);
        Assert.Contains("Source: Platform", output);
    }

    [Fact]
    public async Task BareName_ExactNuGetPackageId_RoutesToPackage()
    {
        var (exit, output, error) = await RunAppAsync("System.CommandLine", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("# System.CommandLine", output);
        Assert.Contains("## Package Info", output);
        Assert.DoesNotContain("Library: System.CommandLine.dll | Types:", output);
    }

    [Fact]
    public async Task BareName_CommandTypo_SuggestsCommandWithoutNuGetLookup()
    {
        var (exit, output, error) = await RunAppAsync("packag", "--tips", "q");

        Assert.NotEqual(0, exit);
        Assert.Empty(output);
        Assert.Contains("Error: Unknown command 'packag'.", error);
        Assert.Contains("Did you mean:", error);
        Assert.Contains("  package", error);
        Assert.DoesNotContain("Package 'packag' not found", error);
        Assert.DoesNotContain("Network traffic", error);
    }

    // ── api command ──────────────────────────────────────────────────

    [Fact]
    public async Task Api_PlatformLibrary_ListsTypes()
    {
        var options = new ApiOptions { PlatformAssembly = "System.Text.Json" };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => ApiCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
    }

    [Fact]
    public async Task Api_PlatformLibrary_WithTypeFilter_ShowsMembers()
    {
        var options = new ApiOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer"
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => ApiCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("Serialize", output);
        Assert.Contains("Deserialize", output);
    }

    [Fact]
    public async Task Api_PlatformLibrary_JsonOutput()
    {
        var options = new ApiOptions
        {
            PlatformAssembly = "System.Text.Json",
            JsonOutput = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => ApiCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);

        // Should be valid JSON
        var doc = JsonDocument.Parse(output);
        Assert.NotNull(doc);
    }

    [Fact]
    public async Task Api_PlatformLibrary_Table()
    {
        var options = new ApiOptions
        {
            PlatformAssembly = "System.Text.Json",
            Tabular = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => ApiCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);

        // Tabular format produces compact row output
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length > 1, "Expected multiple lines of type output");
    }

    [Fact]
    public async Task Type_SingleType_SelectClasses_ShowsSelectError()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Tabular = true,
            Select = ["Classes"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Contains("Select value 'Classes' not found", error);
    }

    [Fact]
    public async Task Type_SingleType_NoQuery_DefaultsToShape()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer"
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // Default single-type invocation renders the tree shape.
        Assert.Contains("├─", output);
        Assert.Contains("Inherits", output);
    }

    [Fact]
    public async Task Type_SingleType_NormalVerbosity_StaysShapeAndExpandsOverloads()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text.Json.JsonSerializer", "-v:n", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("├─", output);
        Assert.Contains("Methods (10 logical, 107 overloads)", output);
        Assert.Contains("Deserialize<TValue>(System.IO.Stream utf8Json", output);
        Assert.DoesNotContain("Deserialize (40 overloads)", output);
        Assert.DoesNotContain("# System.Text.Json.JsonSerializer", output);
    }

    [Fact]
    public async Task Type_SingleType_QuietVerbosity_RequiresMarkdown()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text.Json.JsonSerializer", "-v:q", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("-v:q is not supported by the type shape renderer", error);
        Assert.Contains("--markdown -v:q", error);
    }

    [Fact]
    public async Task Type_SingleType_MarkdownQuiet_RendersCompactSectionView()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text.Json.JsonSerializer", "--markdown", "-v:q", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("# System.Text.Json.JsonSerializer", output);
        Assert.Contains("Kind: class", output);
        Assert.DoesNotContain("├─", output);
    }

    [Fact]
    public async Task Type_SingleType_MarkdownMinimal_IncludesLibraryContext()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Collections.FrozenDictionary", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("# System.Collections.Frozen.FrozenDictionary", output);
        Assert.Contains("Library: System.Collections.Immutable", output);
        Assert.Contains("Source: Platform", output);
        Assert.Contains("## Method Groups", output);
    }

    [Fact]
    public async Task Type_PrefixBrowse_InferredPlatformTypo_ListsBestEffortMatches()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Runtime.CompilerService", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort prefix matches", error);
        Assert.Contains("System.Runtime.CompilerService", error);
        Assert.Contains("System.Runtime.CompilerServices.CompilerGeneratedAttribute", output);
    }

    [Fact]
    public async Task Router_PrefixBrowse_InferredPlatformTypo_ListsBestEffortMatches()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Runtime.CompilerService", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort prefix matches", error);
        Assert.Contains("System.Runtime.CompilerService", error);
        Assert.Contains("System.Runtime.CompilerServices.CompilerGeneratedAttribute", output);
    }

    [Fact]
    public async Task Type_PlatformPrefixBrowse_UnresolvedNamespace_ListsPlatformMatches()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort platform prefix matches", error);
        Assert.Contains("System.Text", error);
        Assert.Contains("System.Text.StringBuilder", output);
        Assert.Contains("System.Text.Json.JsonSerializer", output);
        Assert.DoesNotContain("Package 'System.Text' not found", error);
    }

    [Fact]
    public async Task Type_PlatformPrefixBrowse_WildcardNote_DoesNotDoubleStar()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text*", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("System.Text.StringBuilder", output);
        Assert.Contains("find \"System.Text*\" --platform", error);
        Assert.DoesNotContain("System.Text**", error);
    }

    [Fact]
    public async Task Type_PlatformPrefixBrowse_AllMissProjection_ReportsCleanError()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text", "--table", "--columns", "Library", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("No columns matched projection: Library", error);
        Assert.DoesNotContain("Unhandled exception", error);
    }

    [Fact]
    public async Task Type_PlatformPrefixBrowse_PartialProjection_WarnsForMissingColumn()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text", "--table", "--columns", "Type,Library,Members", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("System.Text.StringBuilder", output);
        Assert.Contains("note: 1 field has no data: Library", error);
    }

    [Fact]
    public async Task Type_BareSimpleTypeMiss_UsesPlatformFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "Regex", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("# System.Text.RegularExpressions.Regex", output);
        Assert.Contains("Library: System.Text.RegularExpressions", output);
        Assert.Contains("Note: Type 'Regex' resolved via platform find", error);
    }

    [Fact]
    public async Task Router_BareSimpleTypeMiss_UsesPlatformFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "Regex", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("# System.Text.RegularExpressions.Regex", output);
        Assert.Contains("Library: System.Text.RegularExpressions", output);
        Assert.Contains("Note: Type 'Regex' resolved via platform find", error);
    }

    [Fact]
    public async Task Router_BareGenericTypeMiss_UsesPlatformFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "List<T>", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("# System.Collections.Generic.List&lt;T&gt;", output);
        Assert.Contains("Library: System.Collections", output);
        Assert.Contains("Note: Type 'List<T>' resolved via platform find", error);
    }

    [Fact]
    public async Task Router_VoidKeyword_UsesPlatformFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "void", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Package 'void'", error);
    }

    [Fact]
    public async Task Router_FullyQualifiedPlatformMember_UsesPlatformMemberFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.String.IndexOf", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("IndexOf", output);
        Assert.Contains("public int IndexOf(char value)", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Member_FullyQualifiedPlatformMember_UsesPlatformMemberFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.String.IndexOf", "--table", "-S", "Member Index", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("IndexOf:1", output);
        Assert.Contains("IndexOf~", output);
        Assert.Contains("M:System.String.IndexOf(char)", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_SimplePlatformMember_UsesPlatformMemberFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "String.IndexOf", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("IndexOf", output);
        Assert.Contains("public int IndexOf(char value)", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_PrimitiveKeywordMember_UsesPlatformMemberFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "string.IndexOf", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("IndexOf", output);
        Assert.Contains("public int IndexOf(char value)", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_NumericKeywordMember_UsesPlatformMemberFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "int.Parse", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Parse", output);
        Assert.Contains("Return Type", output);
        Assert.Contains("int", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_BooleanKeywordMember_UsesPlatformMemberFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "bool.TryParse", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("TryParse", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_ObjectKeywordMember_UsesPlatformMemberFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "object.GetType", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("GetType", output);
        Assert.Contains("System.Type GetType()", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_GenericMemberSelector_NormalizesGenericTypeArguments()
    {
        var (exit, output, error) = await RunAppAsync(
            "JsonSerializer.Deserialize<TValue>", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Deserialize", output);
        Assert.Contains("Deserialize<TValue>", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Member_GenericMemberSelector_NormalizesGenericTypeArguments()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "-m", "Deserialize<TValue>", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Deserialize", output);
        Assert.Contains("Deserialize<TValue>", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Member_MethodsTable_OmitsDecodeColumn()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "-m", "Serialize", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Serialize", output);
        // The Decode degradation marker is never a default table column; it surfaces on stderr.
        Assert.DoesNotContain("Decode", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Member_MethodsTable_ShowsAlwaysOnDigestColumn()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "-m", "Serialize", "--tips", "q");

        Assert.Equal(0, exit);
        // The durable ~digest handle is always shown as a Digest column in the default member table.
        Assert.Contains("| Name | Digest | Signature | Description |", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Member_CompactSummaryTables_OmitDecodeColumn()
    {
        // The compact per-kind summary tables (Constructors, Properties, Fields, Method
        // Groups) also drop the empty Decode degradation column; degradation surfaces on stderr.
        var (exit, output, error) = await RunAppAsync(
            "System.Text.Json.JsonSerializerOptions", "-m", "8", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("## Constructors", output);
        Assert.Contains("## Properties", output);
        Assert.DoesNotContain("Decode", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Member_Json_CarriesDigestAndCanonicalSignature()
    {
        // The agent-facing JSON must expose the durable overload handle (digest) and the
        // doc-ID canonical signature, not just the human-facing Markdown Digest column.
        var (exit, output, error) = await RunAppAsync(
            "System.Text.Json.JsonSerializer.Serialize:1", "--json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("\"digest\": \"1dc14dd1fb\"", output);
        Assert.Contains("\"canonical_signature\": \"M:System.Text.Json.JsonSerializer.Serialize", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Member_JsonLimit_SelectsSameOverloadDigestAsTable()
    {
        // -m N over --json applies the same display ordering as the Markdown table, so the
        // selected overload's digest matches across the two experiences.
        var table = await RunAppAsync(
            "System.Text.Json.JsonSerializer.Serialize", "-m", "1", "--tips", "q");
        var json = await RunAppAsync(
            "System.Text.Json.JsonSerializer.Serialize", "-m", "1", "--json", "--tips", "q");

        Assert.Equal(0, table.Exit);
        Assert.Equal(0, json.Exit);
        var tableDigest = System.Text.RegularExpressions.Regex.Match(table.Output, "`([0-9a-f]{10})`");
        Assert.True(tableDigest.Success, "Expected a ~digest in the Methods table Digest column.");
        Assert.Contains($"\"digest\": \"{tableDigest.Groups[1].Value}\"", json.Output);
    }

    [Fact]
    public async Task Member_Limit_SelectsSameOverloadInTableAndMemberIndex()
    {
        // The default member table and the Member Index apply -m N over the same ordering,
        // so a one-member limit selects the same overload (same ~digest) in both views.
        var table = await RunAppAsync(
            "System.Text.Json.JsonSerializer.Serialize", "-m", "1", "--tips", "q");
        var index = await RunAppAsync(
            "System.Text.Json.JsonSerializer.Serialize", "-m", "1", "-S", "Member Index", "--tips", "q");

        Assert.Equal(0, table.Exit);
        Assert.Equal(0, index.Exit);
        Assert.Empty(table.Error);
        Assert.Empty(index.Error);

        var digest = System.Text.RegularExpressions.Regex.Match(table.Output, "`([0-9a-f]{10})`");
        Assert.True(digest.Success, "Expected a ~digest in the Methods table Digest column.");
        // The Member Index Stable selector for the same overload is Name~<digest>.
        Assert.Contains($"~{digest.Groups[1].Value}", index.Output);
    }

    [Fact]
    public async Task Member_OverloadDetail_ShowsDigestAndCanonicalSignature()
    {
        // Drilling into a single overload (even via the non-durable positional :N) must surface
        // the durable Digest and the Canonical Signature so the reference can be upgraded.
        var (exit, output, error) = await RunAppAsync(
            "System.Text.Json.JsonSerializer.Serialize:6", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("## Signature", output);
        Assert.Contains("| Signature | Digest | Canonical Signature | Description |", output);
        Assert.Contains("M:System.Text.Json.JsonSerializer.Serialize", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_ConstructorSelector_NormalizesCtorAlias()
    {
        var (exit, output, error) = await RunAppAsync(
            "String.ctor", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains(".ctor", output);
        Assert.Contains("public String(", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_DoubleDotConstructorSelector_NormalizesCtorAlias()
    {
        var (exit, output, error) = await RunAppAsync(
            "List<T>..ctor", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains(".ctor", output);
        Assert.Contains("public List(", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_DoubleDotConstructorSelector_PreservesOverloadIndex()
    {
        var (exit, output, error) = await RunAppAsync(
            "List<T>..ctor:3", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains(".ctor", output);
        Assert.Contains("(int)", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_IndexerSelector_NormalizesThisAlias()
    {
        var (exit, output, error) = await RunAppAsync(
            "String.this[]", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Chars", output);
        Assert.Contains("this[int index]", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_IndexerSelector_NormalizesThisAliasWithIllustrativeArgument()
    {
        var (exit, output, error) = await RunAppAsync(
            "String.this[0]", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Chars", output);
        Assert.Contains("this[int index]", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_GenericIndexerSelector_NormalizesThisAliasWithTypeArgument()
    {
        var (exit, output, error) = await RunAppAsync(
            "Dictionary<TKey,TValue>.this[TKey]", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Item", output);
        Assert.Contains("this[TKey key]", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_OperatorSelector_NormalizesOperatorAlias()
    {
        var (exit, output, error) = await RunAppAsync(
            "DateTime.operator+", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("operator +", output);
        Assert.Contains("public static System.DateTime operator +", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_ConversionSelector_NormalizesImplicitAlias()
    {
        var (exit, output, error) = await RunAppAsync(
            "Decimal.implicit", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("implicit operator", output);
        Assert.Contains("public static implicit operator System.Decimal", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Type_OperatorMemberFilter_NormalizesOperatorAlias()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "DateTime", "-m", "operator+", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("operator +", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Member_ConstructorSelector_NormalizesCtorAlias()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "String", "-m", "ctor", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains(".ctor", output);
        Assert.Contains("public String(", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Type_BareSimpleTypeMiss_PrefersPlatformTypeOverSameNamedPackage()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonSerializer", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("# System.Text.Json.JsonSerializer", output);
        Assert.Contains("Library: System.Text.Json", output);
        Assert.DoesNotContain("Package 'JsonSerializer'", error);
        Assert.Contains("Note: Type 'JsonSerializer' resolved via platform find", error);
    }

    [Fact]
    public async Task Type_BareSimpleTypeMiss_PrefersExactNonGenericMatch()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "FrozenDictionary", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("# System.Collections.Frozen.FrozenDictionary", output);
        Assert.Contains("Library: System.Collections.Immutable", output);
        Assert.Contains("## Method Groups", output);
        Assert.DoesNotContain("## Type Parameters", output);
        Assert.Contains("Note: Type 'FrozenDictionary' resolved via platform find", error);
    }

    [Fact]
    public async Task Type_BareCoreLibSimpleName_PrefersNonGenericExactMatch()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "Task", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("# System.Threading.Tasks.Task", output);
        Assert.DoesNotContain("# System.Threading.Tasks.Task&lt;TResult&gt;", output);
    }

    [Fact]
    public async Task Member_BareSimpleTypeMiss_UsesPlatformFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "Regex", "-m", "Match", "-S", "Member Index", "--rows", "4", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("# System.Text.RegularExpressions.Regex", output);
        Assert.Contains("`Match:1`", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Rows_TailWindowDiffersFromHeadWindow()
    {
        // A real command must wire ParseRows and honor the tail branch: the last-N
        // window selects a different endpoint than the first-N window.
        var head = await RunAppAsync(
            "type", "System.String", "-S", "Member Index", "--rows", "2", "--tsv", "--tips", "q");
        var tail = await RunAppAsync(
            "type", "System.String", "-S", "Member Index", "--rows", "2", "--tail", "--tsv", "--tips", "q");

        Assert.Equal(0, head.Exit);
        Assert.Equal(0, tail.Exit);
        Assert.Empty(head.Error);
        Assert.Empty(tail.Error);
        var headLines = head.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var tailLines = tail.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // header + exactly 2 data rows in each.
        Assert.Equal(3, headLines.Length);
        Assert.Equal(3, tailLines.Length);
        // Same header row, different data rows.
        Assert.Equal(headLines[0], tailLines[0]);
        Assert.NotEqual(headLines[1], tailLines[1]);
    }

    [Fact]
    public async Task Rows_EqualsSyntaxAppliesTheWindow()
    {
        // The =-syntax reaches the option value by a different path than a separate
        // token, and the arg-preprocessor token scan does not see it. Reading the
        // parse result rather than the raw tokens is what keeps the two equivalent.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.String", "-S", "Member Index", "--rows=2", "--tail", "--tsv", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal(3, output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task Rows_RejectsBothHeadAndTail()
    {
        // --head and --tail name opposite ends, so asking for both is a contradiction
        // rather than a narrower window. The =-syntax spelling must be caught too.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.String", "-S", "Member Index", "--rows=3", "--head", "--tail", "--tsv", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--head and --tail select opposite ends", error, StringComparison.Ordinal);
        // Pin the failure shape, not just the message: a swallowed BuildRowWindow
        // throw would dump a stack trace that also contains the message and exits 1.
        Assert.DoesNotContain("Unhandled exception", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rows_FollowedByAnotherOption_BlamesTheMissingValueNotTheOption()
    {
        // Bare --rows used to mean "interpret -n as rows", which put the count on a
        // different flag than the unit. It is now an error -- but System.CommandLine
        // hands a required-argument option the next token regardless, so the spec
        // arrives as "--tsv". The error must name the missing selection rather than
        // sending a reader off to fix the spelling of --tsv.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.String", "-S", "Member Index", "--rows", "--tsv", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--rows requires a row selection, but '--tsv' is another option", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rows_AtTheEndOfTheCommandLine_ReportsTheMissingValue()
    {
        // Nothing follows --rows here, so System.CommandLine has no token to bind and
        // reports the missing argument itself. The validator must not read the value
        // in this state: doing so throws out of the validator, which surfaced as a
        // stack trace *and* exit code 0 -- a failure invisible to any caller checking
        // the exit code.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.String", "-S", "Member Index", "--rows");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Required argument missing for option: '--rows'", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rows_RangeSelectsTheRowsItNames_NotACountFromTheStart()
    {
        // The distinction the grammar exists for. Over the same table, `4` takes the
        // first four rows and `2..4` takes three rows starting at the second, so the
        // two must not resolve to the same window.
        var count = await RunAppAsync(
            "type", "System.String", "-S", "Member Index", "--rows", "4", "--tsv", "--tips", "q");
        var range = await RunAppAsync(
            "type", "System.String", "-S", "Member Index", "--rows", "2..4", "--tsv", "--tips", "q");

        Assert.Equal(0, count.Exit);
        Assert.Equal(0, range.Exit);
        var countLines = count.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var rangeLines = range.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(5, countLines.Length);   // header + 4
        Assert.Equal(4, rangeLines.Length);   // header + 3
        // The range starts one row later, so its first data row is the count's second.
        Assert.Equal(countLines[2], rangeLines[1]);
    }

    [Fact]
    public async Task Rows_StartPlusCountTakesOneMoreRowThanTheSameDigitsAsARange()
    {
        // 2..4 is three rows and 2+4 is four; identical digits, different extents.
        var range = await RunAppAsync(
            "type", "System.String", "-S", "Member Index", "--rows", "2..4", "--tsv", "--tips", "q");
        var plus = await RunAppAsync(
            "type", "System.String", "-S", "Member Index", "--rows", "2+4", "--tsv", "--tips", "q");

        Assert.Equal(0, range.Exit);
        Assert.Equal(0, plus.Exit);
        Assert.Equal(4, range.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(5, plus.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task Rows_RejectsADirectionOnARange()
    {
        // A range already says which rows to keep, so a direction is not a narrower
        // request but a second, conflicting answer to the same question.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.String", "-S", "Member Index", "--rows", "2..4", "--tail", "--tsv", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("already names which rows to keep", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rows_ExplainsTheColonFormRatherThanFailingToParseIt()
    {
        // 2:10 carries Python slice semantics (0-based, end-exclusive) and would
        // differ from 2..10 by a row at each edge, so a generic parse error would
        // leave a reader thinking the digits were wrong rather than the operator.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.String", "-S", "Member Index", "--rows", "2:10", "--tsv", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("':'", error, StringComparison.Ordinal);
        Assert.Contains("2..10 is nine rows", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValuedTailFlag_IsReportedAsAMigration_NotBoundAsAPositional()
    {
        // --tail used to carry the count. It is now a bool, so `--tail 20` would
        // otherwise leave "20" to bind as a positional and send the command looking
        // for a package by that name -- a confusing failure at an unrelated task.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.String", "-S", "Member Index", "--tail", "20", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("'--tail 20' is no longer valid", error, StringComparison.Ordinal);
        Assert.Contains("-n 20 --tail", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValuedTailFlag_AfterEndOfOptions_IsNotReportedAsAMigration()
    {
        // After `--` everything is positional, so `--tail` is a literal argument and
        // not a direction flag at all. The migration guard scans raw tokens, so it
        // would otherwise claim a stale spelling for something that was never a flag.
        var (exit, output, error) = await RunAppAsync(
            "library", "--", "--tail", "5", "--tips", "q");

        Assert.DoesNotContain("is no longer valid", error, StringComparison.Ordinal);
        Assert.DoesNotContain("is no longer valid", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Find_NamespaceExactMiss_RetriesAsPrefix()
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "System.Text", "--platform", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("No exact matches for 'System.Text'", error);
        Assert.Contains("System.Text*", error);
        Assert.Contains("StringBuilder", output);
        Assert.Contains("System.Text.Json", output);
        Assert.DoesNotContain("TextInfo", output);
    }

    [Fact]
    public async Task Diff_TypeFilter_LongNamespacePrefixMatchesChangedTypes()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--package", "System.Text.Json@9.0.0..10.0.0",
            "-t", "System.Text.Json.Serialization", "--additive", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Summary", output);
        Assert.Contains("5 additive", output);
        Assert.Contains("JsonKnownReferenceHandler", output);
        Assert.DoesNotContain("JsonSerializer |", output);
    }

    [Fact]
    public async Task Diff_TypeFilter_ShortNamespacePrefixMatchesChangedTypes()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--package", "System.Text.Json@9.0.0..10.0.0",
            "-t", "Serialization", "--additive", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("5 additive", output);
        Assert.Contains("JsonKnownReferenceHandler", output);
        Assert.DoesNotContain("JsonSerializer |", output);
    }

    [Fact]
    public async Task Diff_TypeFilter_NoMatches_PrintsNote()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--package", "System.Text.Json@9.0.0..10.0.0",
            "-t", "DefinitelyMissingNamespace", "--additive", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Summary", output);
        Assert.Contains("no changes", output);
        Assert.Contains("type filter matched no changed types", error);
    }

    [Fact]
    public async Task Diff_FindingTransitions_ConfirmsPackageTypeIntroduction()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--package", "System.Text.Json@8.0.6..9.0.0",
            "-t", "System.Text.Json.Schema.JsonSchemaExporter",
            "-S", "Finding Transitions", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("PairFinding.Added", output);
        Assert.Contains("api.type", output);
        Assert.Contains("System.Text.Json.Schema.JsonSchemaExporter", output);
        Assert.Contains("8.0.6", output);
        Assert.Contains("9.0.0", output);
        Assert.Contains("absent", output);
        Assert.Contains("present", output);
    }

    [Fact]
    public async Task Diff_FindingTransitions_ConfirmsAllocationIntroduction()
    {
        var oldPath = FixtureCatalog.DiffPair.OldAssemblyPath();
        var newPath = FixtureCatalog.DiffPair.NewAssemblyPath();

        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", $"{oldPath}..{newPath}",
            "-t", "DiffFixtureSample.DiffSample",
            "-m", "RegressesAllocInLoop",
            "--finding", "analysis.allocation",
            "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("PairFinding.Added", output);
        Assert.Contains("analysis.allocation", output);
        Assert.Contains("RegressesAllocInLoop", output);
        Assert.Contains("absent", output);
        Assert.Contains("present", output);
    }

    [Fact]
    public async Task Diff_FindingTransitions_ConfirmsCallSiteIntroduction()
    {
        var oldPath = FixtureCatalog.DiffPair.OldAssemblyPath();
        var newPath = FixtureCatalog.DiffPair.NewAssemblyPath();

        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", $"{oldPath}..{newPath}",
            "-t", "DiffFixtureSample.DiffSample",
            "-m", "RegressesAllocInLoop",
            "--finding", "analysis.call-site",
            "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("PairFinding.Added", output);
        Assert.Contains("analysis.call-site", output);
        Assert.Contains("RegressesAllocInLoop", output);
        Assert.Contains(".Add(", output);
        Assert.Contains("absent", output);
        Assert.Contains("present", output);
    }

    [Fact]
    public async Task Diff_FindingTransitions_ConfirmsUnsafetyIntroduction()
    {
        var oldPath = FixtureCatalog.DiffPair.OldAssemblyPath();
        var newPath = FixtureCatalog.DiffPair.NewAssemblyPath();

        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", $"{oldPath}..{newPath}",
            "-t", "DiffFixtureSample.DiffSample",
            "-m", "AddsUnsafe",
            "--finding", "analysis.unsafety",
            "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("PairFinding.Added", output);
        Assert.Contains("analysis.unsafety", output);
        Assert.Contains("AddsUnsafe", output);
        Assert.Contains("StackAlloc", output);
        Assert.Contains("absent", output);
        Assert.Contains("present", output);
    }

    [Theory]
    [InlineData("csharp.line", "return 1;", "return 2;")]
    [InlineData("il.op", "ldc.i4 1", "ldc.i4 2")]
    public async Task Diff_FindingTransitions_ConfirmsImplementationOccurrenceChanges(
        string descriptor,
        string oldEvidence,
        string newEvidence)
    {
        var oldPath = FixtureCatalog.DiffPair.OldAssemblyPath();
        var newPath = FixtureCatalog.DiffPair.NewAssemblyPath();

        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", $"{oldPath}..{newPath}",
            "-t", "DiffFixtureSample.DiffSample",
            "-m", "ConstantValue",
            "--finding", descriptor,
            "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("PairFinding.Removed", output);
        Assert.Contains("PairFinding.Added", output);
        Assert.Contains(descriptor, output);
        Assert.Contains(oldEvidence, output);
        Assert.Contains(newEvidence, output);
    }

    [Theory]
    [InlineData("csharp.line")]
    [InlineData("il.op")]
    public async Task Diff_FindingTransitions_ImplementationFindingsRequireOneMemberBeforeAcquisition(
        string descriptor)
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", "missing-old.dll..missing-new.dll",
            "-t", "Sample.Widget",
            "--finding", descriptor,
            "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains($"--finding {descriptor} requires exactly one --member", error);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diff_FindingTransitions_AllocationRequiresOneMemberBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", "missing-old.dll..missing-new.dll",
            "-t", "Sample.Widget",
            "--finding", "analysis.allocation",
            "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("requires exactly one --member", error);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diff_FindingTransitions_CallSiteRequiresOneMemberBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", "missing-old.dll..missing-new.dll",
            "-t", "Sample.Widget",
            "--finding", "analysis.call-site",
            "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--finding analysis.call-site requires exactly one --member",
            error);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diff_FindingTransitions_UnsafetyRequiresOneMemberBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", "missing-old.dll..missing-new.dll",
            "-t", "Sample.Widget",
            "--finding", "analysis.unsafety",
            "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--finding analysis.unsafety requires exactly one --member",
            error);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diff_FindingTransitions_RejectsUnknownDescriptorBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", "missing-old.dll..missing-new.dll",
            "-t", "Sample.Widget",
            "--finding", "analysis.unknown",
            "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Unsupported Finding descriptor 'analysis.unknown'", error);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diff_FindingTransitions_RequiresFocusedTargetBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", "missing-old.dll..missing-new.dll",
            "-S", "Finding Transitions", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Finding Transitions requires --type", error);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diff_FindingTransitions_RejectsCompatibilityFiltersBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", "missing-old.dll..missing-new.dll",
            "-t", "Sample.Widget", "-S", "Finding Transitions",
            "--additive", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("cannot be combined", error);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diff_FindingTransitions_RejectsImplementationDiffBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", "missing-old.dll..missing-new.dll",
            "-t", "Sample.Widget",
            "-S", "Finding Transitions",
            "-S", "Implementation Diff",
            "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Finding Transitions must be selected by itself", error);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diff_FindingTransitions_ImpliedSelectionRejectsCompositionBeforeAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff", "--library", "missing-old.dll..missing-new.dll",
            "-t", "Sample.Widget",
            "-m", "HotPath",
            "--finding", "analysis.allocation",
            "-S", "Analysis Diff",
            "--json", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Finding Transitions must be selected by itself", error);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diff_FindingTransitions_IsDiscoverableAndSelectableForLocalPair()
    {
        var oldPath = FixtureCatalog.DiffPair.OldAssemblyPath();
        var newPath = FixtureCatalog.DiffPair.NewAssemblyPath();
        var range = $"{oldPath}..{newPath}";

        var (discoverExit, discoverOutput, discoverError) = await RunAppAsync(
            "diff", "--library", range, "-D", "--tips", "q");
        var (selectExit, selectOutput, selectError) = await RunAppAsync(
            "diff", "--library", range,
            "-t", "DiffFixtureSample.DiffSample",
            "-S", "Finding Transitions", "--table", "--tips", "q");

        Assert.Equal(0, discoverExit);
        Assert.Empty(discoverError);
        Assert.Contains("Finding Transitions", discoverOutput);
        Assert.Equal(0, selectExit);
        Assert.Empty(selectError);
        Assert.Contains("PairFinding.Present", selectOutput);
        Assert.Contains("DiffFixtureSample.DiffSample", selectOutput);
    }

    [Fact]
    public async Task Router_PlatformPrefixBrowse_UnresolvedNamespace_ListsPlatformMatches()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Text", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort platform prefix matches", error);
        Assert.Contains("System.Text", error);
        Assert.Contains("System.Text.StringBuilder", output);
        Assert.Contains("System.Text.Json.JsonSerializer", output);
        Assert.DoesNotContain("Package 'system.text' not found", error);
    }

    [Fact]
    public async Task Router_ExactPlatformAssembly_StillRoutesToLibrary()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Runtime", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Field", output);
        Assert.Contains("Value", output);
        Assert.Contains("Name", output);
        Assert.Contains("System.Runtime", output);
        Assert.Contains("Type Forwarders", output);
    }

    [Fact]
    public async Task SourceCommand_RemovedFromRoot()
    {
        var (exit, _, error) = await RunAppAsync("source", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("Unrecognized command or argument 'source'", error);
    }

    [Fact]
    public async Task BrowsableUrlsAlias_Removed()
    {
        var (exit, _, error) = await RunAppAsync(
            "member", "JsonConvert", "--package", "Newtonsoft.Json@13.0.4",
            "-m", "SerializeObject", "-S", "Source Locations", "--browsable-urls", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("Unrecognized option '--browsable-urls'", error);
    }

    [Fact]
    public async Task Type_ExactPlatformAssembly_DoesNotUseWidePlatformPrefixBrowse()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Collections", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("System.Collections.Generic.Dictionary<TKey, TValue>", output);
        Assert.DoesNotContain("System.Collections.Immutable.ImmutableArray", output);
        Assert.DoesNotContain("best-effort platform prefix matches", error);
    }

    [Fact]
    public async Task Type_PlatformPrefixBrowse_NarrowSourceMissFallsBackToWidePlatformMatches()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Collections.Frozen", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort platform prefix matches", error);
        Assert.Contains("System.Collections.Frozen", error);
        Assert.Contains("System.Collections.Frozen.FrozenDictionary", output);
        Assert.Contains("System.Collections.Frozen.FrozenSet", output);
    }

    [Fact]
    public async Task Router_PlatformPrefixBrowse_NarrowSourceMissFallsBackToWidePlatformMatches()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Collections.Frozen", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort platform prefix matches", error);
        Assert.Contains("System.Collections.Frozen", error);
        Assert.Contains("System.Collections.Frozen.FrozenDictionary", output);
        Assert.Contains("System.Collections.Frozen.FrozenSet", output);
    }

    [Fact]
    public async Task RelationshipCommands_NamespacePrefixInputs_PrintPrefixBrowseHint()
    {
        var (implementsExit, implementsOutput, implementsError) = await RunAppAsync(
            "implements", "System.Text", "--tips", "q");

        Assert.Equal(0, implementsExit);
        Assert.Empty(implementsOutput);
        Assert.Contains("looks like a namespace prefix", implementsError);
        Assert.Contains("type System.Text", implementsError);
        Assert.Contains("find \"System.Text*\" --platform", implementsError);

        var (extensionsExit, extensionsOutput, extensionsError) = await RunAppAsync(
            "extensions", "System.Text", "--tips", "q");

        Assert.Equal(0, extensionsExit);
        Assert.Contains("No extension methods found", extensionsOutput);
        Assert.Contains("looks like a namespace prefix", extensionsError);
        Assert.Contains("type System.Text", extensionsError);
        Assert.Contains("find \"System.Text*\" --platform", extensionsError);
    }

    [Fact]
    public async Task Depends_NamespacePrefixInput_PrintsPrefixBrowseHint()
    {
        var (dependsExit, dependsOutput, dependsError) = await RunAppAsync(
            "depends", "System.Text", "--tips", "q");

        Assert.Equal(1, dependsExit);
        Assert.Empty(dependsOutput);
        Assert.Contains("Could not resolve 'System.Text'", dependsError);
        Assert.Contains("looks like a namespace prefix", dependsError);
        Assert.Contains("type System.Text", dependsError);
        Assert.Contains("find \"System.Text*\" --platform", dependsError);
    }

    [Fact]
    public async Task Member_NamespacePrefixInput_PrintsPrefixBrowseHint()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Text", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("member requires a type name", error);
        Assert.Contains("looks like a namespace prefix", error);
        Assert.Contains("type System.Text", error);
        Assert.Contains("find \"System.Text*\" --platform", error);
    }

    [Fact]
    public async Task Type_PrefixBrowse_ExplicitPlatformNamespace_ListsBestEffortMatches()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text.Json.Serialization", "--platform", "System.Text.Json", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort prefix matches", error);
        Assert.Contains("System.Text.Json.Serialization", error);
        Assert.Contains("System.Text.Json.Serialization.JsonConverter", output);
    }

    [Fact]
    public async Task Type_PrefixBrowse_ExplicitLibraryNamespace_ListsBestEffortMatches()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "DotnetInspector.Tests.Sample", "--library", TestAssemblyPath, "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort prefix matches", error);
        Assert.Contains("DotnetInspector.Tests.Sample", error);
        Assert.Contains("DotnetInspector.Tests.SampleClassForTesting", output);
        Assert.Contains("DotnetInspector.Tests.SampleGenericClass", output);
    }

    [Fact]
    public async Task Type_PrefixBrowse_ExplicitPackageNamespace_ListsBestEffortMatches()
    {
        var (packagePath, tempDir) = CreateLocalPrimaryLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "type", "DotnetInspector.Tests.Sample", "--package", packagePath,
                "--library", "Test.Primary.dll", "--table", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Contains("best-effort prefix matches", error);
            Assert.Contains("DotnetInspector.Tests.Sample", error);
            Assert.Contains("DotnetInspector.Tests.SampleClassForTesting", output);
            Assert.Contains("DotnetInspector.Tests.SampleGenericClass", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task TypeListing_FacadePlatformLibrary_ShowsTypeForwardingDescription()
    {
        var (assemblyPath, _, _, error) = PlatformResolver.ResolveAssembly("System.Runtime");
        if (assemblyPath == null || error != null)
        {
            Assert.Skip($"System.Runtime not available: {error}");
            return;
        }

        Assert.SkipUnless(PlatformResolver.IsFacadeOnlyAssembly(assemblyPath),
            "System.Runtime is not facade-only in this runtime.");

        var (exit, output, runError) = await RunAppAsync(
            "type", "--platform", "System.Runtime", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(runError);
        Assert.Contains("This is a type-forwarding library", output);
    }

    [Fact]
    public async Task TypeListing_NonFacadePlatformLibrary_DoesNotShowTypeForwardingDescription()
    {
        var (assemblyPath, _, _, error) = PlatformResolver.ResolveAssembly("System.Text.Json");
        if (assemblyPath == null || error != null)
        {
            Assert.Skip($"System.Text.Json not available: {error}");
            return;
        }

        Assert.False(PlatformResolver.IsFacadeOnlyAssembly(assemblyPath));

        var (exit, output, runError) = await RunAppAsync(
            "type", "--platform", "System.Text.Json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(runError);
        Assert.DoesNotContain("This is a type-forwarding library", output);
    }

    [Fact]
    public async Task Type_SingleType_SelectSection_RendersSectionNotShape()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Properties"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // Selection produces a focused section view, not the tree shape.
        Assert.Contains("## Properties", output);
        Assert.DoesNotContain("├─", output);
    }

    [Fact]
    public async Task Type_SingleType_SelectWithColumns_ProjectsColumns()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Properties"],
            Columns = ["Name"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Properties", output);
        Assert.Contains("| Name |", output);
        Assert.DoesNotContain("Return Type", output);
        Assert.DoesNotContain("├─", output);
    }

    [Fact]
    public async Task Type_SingleType_ExplicitShapeWithSelect_WarnsAndKeepsShape()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            ShapeOutput = true,
            ShapeExplicitlySet = true,
            Select = ["Properties"]
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("--shape does not support", error);
        Assert.Contains("├─", output);
    }

    [Fact]
    public async Task Type_SingleType_SelectEmptySection_WritesNote()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Values"]  // enum-only section; JsonSerializer is a class
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("section 'Values' has no data", error);
    }

    [Fact]
    public async Task Type_SingleType_SelectPopulatedSection_NoEmptyNote()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Methods"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.DoesNotContain("has no data", error);
    }

    [Fact]
    public async Task Type_SingleType_JsonWithSelect_ScopesToSection()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            JsonOutput = true,
            Select = ["Properties"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(output);
        var members = doc.RootElement.GetProperty("members");
        Assert.True(members.GetArrayLength() > 0);
        foreach (var m in members.EnumerateArray())
            Assert.Equal("property", m.GetProperty("kind").GetString());
        // Non-selected facets are scoped out.
        Assert.Empty(doc.RootElement.GetProperty("interfaces").EnumerateArray());
    }

    [Fact]
    public async Task Type_SingleType_JsonWithSelectEmptySection_EmptyMembersAndNote()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            JsonOutput = true,
            Select = ["Values"]
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(output);
        Assert.Empty(doc.RootElement.GetProperty("members").EnumerateArray());
        Assert.Contains("section 'Values' has no data", error);
    }

    [Fact]
    public async Task Type_SingleType_DiscoverMethods_Schema_ListsAllColumns()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Methods"],
            Schema = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("Name", output);
        Assert.Contains("Signature", output);
    }

    [Fact]
    public async Task Type_SingleType_Discover_DefaultsToDiscoverableSections()
    {
        // -D with no --schema now defaults to effective discovery: it resolves the source
        // and lists only sections that actually have data (the empty-section footgun fix).
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = []
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Method Groups | section |", output);
        Assert.Contains("| Custom Attributes | section |", output);
    }

    [Fact]
    public async Task Type_SingleType_DiscoverSchema_ListsAllStaticSections()
    {
        // --schema opts back out to the cheap, offline static schema listing, which
        // includes sections that may have no data (e.g. Custom Attributes, Fields).
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = [],
            Schema = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Custom Attributes | section |", output);
        Assert.Contains("| Fields | section |", output);
    }

    [Fact]
    public async Task Type_SingleType_DiscoverEffective_OnlyShowsSectionsWithData()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = []
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Method Groups | section |", output);
        Assert.Contains("| Methods | section (verbose) |", output);
        Assert.Contains("| Source Files | section (opt-in) |", output);
        Assert.DoesNotContain("| Fields | section |", output);
    }

    [Fact]
    public async Task Type_SingleType_DiscoverEffective_IncludesSelectableCodeSections()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = []
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Properties | section |", output);
        Assert.Contains("| Method Groups | section |", output);
        Assert.Contains("| Decompiled Source | section |", output);
        Assert.Contains("| Original Source | section |", output);
        Assert.Contains("| IL | section |", output);
        Assert.DoesNotContain("| Facts | section", output);
    }

    [Fact]
    public async Task Type_SingleType_SourceFilesSection_RendersTypeSourceUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Text.Json.JsonSerializer", "-S", "Source Files", "--tips", "q", "-n", "28");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Source Files", output);
        Assert.Contains("| Url |", output);
        Assert.Contains("JsonSerializer.Write.String.cs", output);
    }

    [Fact]
    public async Task Type_SingleType_DiscoverEffective_IncludesSelectableCustomAttributesSection()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = []
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Method Groups | section |", output);
        Assert.Contains("| Custom Attributes | section |", output);
    }

    [Fact]
    public async Task Type_DiscoverEmptySection_Effective_ReportsNoDataNote()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Custom Attributes"]
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        // A valid-but-empty section reports a clear "no data" note rather than the
        // misleading "Section not found", and exits 0.
        Assert.Equal(0, exit);
        Assert.Contains("section 'Custom Attributes' has no data", error);
        Assert.DoesNotContain("not found", error);
        Assert.DoesNotContain("| Name | column |", output);
    }

    [Fact]
    public async Task Type_DiscoverUnknownSection_Effective_ReportsNotFound()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Bogus"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        // A genuinely unknown section still reports "not found" (with suggestions) and exits 1.
        Assert.Equal(1, exit);
        Assert.Contains("Section 'Bogus' not found", error);
    }

    [Fact]
    public async Task Type_DiscoverSection_WithoutEffective_HidesSelectColumn()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Properties"],
            Schema = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // The historical Select overload-index column is no longer queryable; selectors
        // live in the dedicated Member Index section.
        Assert.DoesNotContain("| Select | column |", output);
        Assert.Contains("| Name | column |", output);
    }

    [Fact]
    public async Task Member_DiscoverSection_ShowIndex_ListsMemberIndexColumns()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Member Index"],
            Select = ["Member Index"],
            Schema = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // Selecting the dedicated Member Index section renders its selector/identity columns.
        Assert.Contains("| Stable | column |", output);
        Assert.Contains("| Canonical Signature | column |", output);
    }

    [Fact]
    public async Task Member_DiscoverEffective_HidesSelectColumn()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Methods"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // The historical Select column is not queryable, so effective discovery
        // must not list it (regression: member effective used to leak it).
        Assert.DoesNotContain("| Select | column |", output);
        Assert.Contains("| Name | column |", output);
    }

    [Fact]
    public async Task Member_DiscoverEffective_ListsMethodsAlternate()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = []
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Method Groups | section |", output);
        Assert.Contains("| Methods | section (verbose) |", output);
    }

    [Fact]
    public async Task Member_DiscoverEffective_ShowIndexAtNormal_ListsMemberIndexColumns()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Member Index"],
            Select = ["Member Index"],
            Verbosity = Verbosity.Normal
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // Selecting the Member Index section renders it at Normal verbosity too.
        Assert.Contains("| Stable | column |", output);
        Assert.Contains("| Canonical Signature | column |", output);
    }

    [Fact]
    public async Task Member_SourceLocations_Group_RendersSelectorsAndSourceRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--platform", "System.Text.Json",
            "-m", "Serialize", "-S", "Source Locations", "--rows", "6", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Source Locations", output);
        Assert.Contains("| Selector | Signature | File | Line | End Line | Url |", output);
        Assert.Contains("`Serialize:1`", output);
        Assert.Contains("JsonSerializer.Write.String.cs", output);
        Assert.Contains("raw.githubusercontent.com", output);
        Assert.DoesNotContain("## Member Index", output);
    }

    [Fact]
    public async Task Member_SourceLocations_SelectedSignature_RendersWithoutSelectorColumn()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--platform", "System.Text.Json",
            "Serialize:1", "-S", "Source Locations", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Source Locations", output);
        Assert.Contains("| Signature | File | Line | End Line | Url |", output);
        Assert.DoesNotContain("| Selector |", output);
        Assert.Contains("JsonSerializer.Write.String.cs", output);
    }

    [Fact]
    public async Task Member_SourceLocations_PropertyAccessor_ResolvesFromAccessorSequencePoints()
    {
        // A property has no MethodDef of its own; its authored source is located through its
        // accessor's PDB sequence points, reported against the owning property (#3278).
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializerOptions", "--platform", "System.Text.Json",
            "MaxDepth", "-S", "Source Locations", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Source Locations", output);
        Assert.Contains("public int MaxDepth { get; set; }", output);
        Assert.Contains("JsonSerializerOptions.cs", output);
        Assert.Contains("raw.githubusercontent.com", output);
    }

    [Fact]
    public async Task Member_OriginalSource_PropertyAccessorOrdinals_ResolveGetterAndSetterSeparately()
    {
        // Ordinal 1 addresses the getter and 2 the setter, matching the accessor addressing the
        // body sections use, so each renders its own authored accessor source (#3278).
        var (getterExit, getterOutput, getterError) = await RunAppAsync(
            "member", "JsonSerializerOptions", "--platform", "System.Text.Json",
            "MaxDepth:1", "-S", "Original Source", "--tips", "q");

        Assert.Equal(0, getterExit);
        Assert.Empty(getterError);
        Assert.Contains("## Original Source", getterOutput);
        Assert.Contains("get => _maxDepth;", getterOutput);
        Assert.DoesNotContain("_maxDepth = value;", getterOutput);

        var (setterExit, setterOutput, setterError) = await RunAppAsync(
            "member", "JsonSerializerOptions", "--platform", "System.Text.Json",
            "MaxDepth:2", "-S", "Original Source", "--tips", "q");

        Assert.Equal(0, setterExit);
        Assert.Empty(setterError);
        Assert.Contains("## Original Source", setterOutput);
        Assert.Contains("_maxDepth = value;", setterOutput);
    }

    [Fact]
    public async Task Member_OriginalSource_BodylessMember_ExplainsWhyThereIsNoSource()
    {
        // An abstract method has no IL body, so it has no authored source to resolve. That is a
        // complete answer, not a failure: say so and keep exit 0 rather than rendering nothing
        // and leaving the caller unable to tell success from silent failure (#3299).
        var (abstractExit, abstractOutput, abstractError) = await RunAppAsync(
            "member", "JsonConverter<T>", "--platform", "System.Text.Json",
            "Read", "-S", "Original Source", "--tips", "q");

        Assert.Equal(0, abstractExit);
        Assert.Empty(abstractError);
        Assert.Contains("## Original Source", abstractOutput);
        Assert.Contains("has no IL body", abstractOutput);

        // An interface method is bodyless for a different metadata reason and gets the same answer.
        var (interfaceExit, interfaceOutput, interfaceError) = await RunAppAsync(
            "member", "IJsonOnDeserialized", "--platform", "System.Text.Json",
            "OnDeserialized", "-S", "Original Source", "--tips", "q");

        Assert.Equal(0, interfaceExit);
        Assert.Empty(interfaceError);
        Assert.Contains("## Original Source", interfaceOutput);
        Assert.Contains("has no IL body", interfaceOutput);
    }

    [Fact]
    public async Task Member_OriginalSource_MemberWithBody_DoesNotClaimTheMemberIsBodyless()
    {
        // Close negative: a member that does have a body still renders its authored source, so
        // the bodyless explanation never displaces real source (#3299).
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializerOptions", "--platform", "System.Text.Json",
            "MaxDepth:1", "-S", "Original Source", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("get => _maxDepth;", output);
        Assert.DoesNotContain("has no IL body", output);
    }

    [Fact]
    public async Task Member_SourceDiff_BodylessMember_ReportsOriginalSourceUnavailable()
    {
        // The bodyless explanation is prose about the member, not source text, so the diff must
        // report its "before" side unavailable rather than diffing the explanation (#3299).
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConverter<T>", "--platform", "System.Text.Json",
            "Read", "-S", "Source Diff", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Source Diff", output);
        Assert.Contains("Original Source unavailable", output);
        Assert.DoesNotContain("has no IL body", output);
    }

    [Fact]
    public async Task Member_SourceDiff_PropertyAccessor_ComparesAuthoredSourceToAccessorBody()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializerOptions", "--platform", "System.Text.Json",
            "MaxDepth:2", "-S", "Source Diff", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Source Diff", output);
        Assert.Contains("--- Original Source", output);
        Assert.Contains("+++ Decompiled Source", output);
        // The decompiled side is the accessor's own body, spelled with its metadata name.
        Assert.Contains("set_MaxDepth", output);
    }

    [Fact]
    public async Task Member_SourceLocations_BareSelectedSignature_EmitsSingleUrl()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConvert", "--package", "Newtonsoft.Json@13.0.4",
            "SerializeObject:1", "-S", "Source Locations", "--bare", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("https://raw.githubusercontent.com/JamesNK/Newtonsoft.Json/", output);
        Assert.Contains("JsonConvert.cs", output);
        Assert.DoesNotContain("## Source Locations", output);
        Assert.DoesNotContain("| Url |", output);
    }

    [Fact]
    public async Task Member_SourceLocations_BareGroup_EmitsUrlColumn()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConvert", "--package", "Newtonsoft.Json@13.0.4",
            "-m", "SerializeObject", "-S", "Source Locations", "--bare", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length > 1);
        Assert.All(lines, line =>
        {
            Assert.StartsWith("https://raw.githubusercontent.com/JamesNK/Newtonsoft.Json/", line);
            Assert.Contains("JsonConvert.cs", line);
        });
        Assert.DoesNotContain("## Source Locations", output);
        Assert.DoesNotContain("| Url |", output);
    }

    [Fact]
    public async Task Type_SourceFiles_Bare_EmitsUrlColumn()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--bare", "--raw", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var lines = output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains(lines, line => line.EndsWith("/Src/Newtonsoft.Json/JsonReader.cs", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.EndsWith("/Src/Newtonsoft.Json/JsonReader.Async.cs", StringComparison.Ordinal));
        Assert.All(lines, line => Assert.StartsWith("https://raw.githubusercontent.com/JamesNK/Newtonsoft.Json/", line));
        Assert.DoesNotContain("url", lines);
    }

    [Fact]
    public async Task Type_SourceFiles_Urls_EmitsUrlColumn()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--urls", "--raw", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => Assert.StartsWith("https://raw.githubusercontent.com/JamesNK/Newtonsoft.Json/", line));
    }

    [Fact]
    public async Task Type_SourceFiles_UrlsJsonArray_EmitsSingleArrayDocument()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--urls", "--json-array", "--raw", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var rows = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal(1, rows[0].GetProperty("row").GetInt32());
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.cs", rows[0].GetProperty("url").GetString());
        Assert.Equal(2, rows[1].GetProperty("row").GetInt32());
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.Async.cs", rows[1].GetProperty("url").GetString());
    }

    [Theory]
    [InlineData("--value")]
    [InlineData("--urls")]
    [InlineData("--paths")]
    [InlineData("--print")]
    public async Task WholeSurfaceListing_DroppedProjection_FailsLoudly(string projectionFlag)
    {
        // The whole-surface type listing is a name table that exposes no printable payload, so a
        // payload projection cannot be honored. It used to render the full unprojected listing and
        // then trip the audit (#3390); it now (#3386) rejects the projection up front with a
        // targeted message, before rendering, so the audit never has to fire a second line.
        var (exit, output, error) = await RunAppAsync(
            "type", "--library", TestAssemblyPath, "-S", "Classes", projectionFlag);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("not supported when listing types", error);
        Assert.Contains(projectionFlag, error);
        Assert.DoesNotContain("produced unprojected output", error);
    }

    [Fact]
    public async Task ProjectionAudit_DoesNotFireForHelp()
    {
        // --help short-circuits rendering, so the projection is not dropped; it is moot.
        var (exit, _, error) = await RunAppAsync(
            "type", "--library", TestAssemblyPath, "-S", "Classes", "--value", "--help");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("produced unprojected output", error);
    }

    [Fact]
    public async Task ProjectionAudit_DoesNotFireWhenProjectionIsRejected()
    {
        // A command that rejects an unsupported projection has already reported the problem;
        // the audit must not add a second, misleading "this is a bug" line on top of it.
        var (exit, _, error) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", "Signals", "--urls");

        Assert.Equal(1, exit);
        Assert.DoesNotContain("produced unprojected output", error);
    }

    [Fact]
    public async Task ProjectionAudit_DoesNotFireForHonoredCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", "References", "--count");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("produced unprojected output", error);
        Assert.True(int.TryParse(output.Trim(), out _), $"expected a bare count, got: {output}");
    }

    [Fact]
    public async Task TypeListing_ColumnProjectionWithJson_IsRejected()
    {
        // #3386: --columns/--fields select table columns; document --json has no column-slicing
        // facility. The combination used to silently drop the column filter and emit the whole
        // typed document; it now fails closed instead.
        var (exit, output, error) = await RunAppAsync(
            "type", "--platform", "System.Runtime", "-S", "Interfaces", "--columns", "Type", "--json");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("cannot be combined with --json", error);
        Assert.DoesNotContain("produced unprojected output", error);
    }

    [Fact]
    public async Task TypeListing_PayloadProjection_IsRejected()
    {
        // #3386: the type-listing surface exposes no printable payload, so a payload projection
        // used to dump the whole ~20 MB surface and then trip the projection audit. It now fails
        // closed before rendering, and the audit must not add a second, misleading line.
        var (exit, output, error) = await RunAppAsync(
            "type", "--platform", "System.Runtime", "-S", "Classes", "--value", "--json");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("not supported when listing types", error);
        Assert.DoesNotContain("produced unprojected output", error);
    }

    [Fact]
    public async Task SingleType_ColumnProjectionWithJson_IsRejected()
    {
        // #3386: the same rejection applies on the single-type path.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.String", "--platform", "System.Runtime", "-S", "Methods", "--fields", "Name", "--json");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("cannot be combined with --json", error);
    }

    [Fact]
    public async Task Member_ColumnProjectionWithJson_IsRejected()
    {
        // #3386: the member path shares the single-type writer, so it inherits the rejection.
        var (exit, output, error) = await RunAppAsync(
            "member", "System.String", "--platform", "System.Runtime", "-S", "Methods", "--fields", "Name", "--json");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("cannot be combined with --json", error);
    }

    [Fact]
    public async Task ColumnProjectionWithValue_UnderJson_StillComposes()
    {
        // #3386 boundary: a scalar payload projection composes with --json (--fields picks which
        // column feeds --value), so this must remain honored rather than swept into the rejection.
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Library Info", "--fields", "Assembly Version", "--value", "--json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("\"value\"", output);
        Assert.DoesNotContain("cannot be combined with --json", error);
    }

    [Fact]
    public async Task GlobListing_Discovery_IsHonoredNotRejected()
    {
        // #3386 regression guard: the glob (and prefix-browse) fallback routes ignored -D
        // discovery and fell through to WriteFullApiOutput. Once that path rejects --fields+--json,
        // a discovery request there would have been rejected with a misleading column message.
        // Discovery must be dispatched before the projection guard, matching the main listing path.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.*", "--library", TestAssemblyPath, "-D", "Classes", "--fields", "Type", "--json");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("cannot be combined with --json", error);
        Assert.Contains("\"kind\":\"column\"", output);
    }

    [Fact]
    public async Task Router_RewrittenCommand_IsAudited()
    {
        // The router captures projection flags as raw tokens, so the outer invocation records
        // nothing. It used to invoke the rewritten parse directly, bypassing the audit, which
        // left every bare-mode invocation unguarded. It now goes through the choke point.
        var (exit, _, error) = await RunAppAsync("Regex", "--count", "--print", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("--count cannot be combined with --print", error);
    }

    [Fact]
    public void ProjectionAudit_NestedInvocationDoesNotDiscardOuterRequest()
    {
        // Invocations nest: the router invokes the command it rewrites to. An inner invocation
        // must not consume the outer one's request, or the outer verify finds nothing to check
        // and a dropped projection escapes.
        var root = CommandLineBuilder.CreateRootCommand();
        var outer = root.Parse(["library", TestAssemblyPath, "-S", "References", "--count"]);
        var inner = root.Parse(["library", TestAssemblyPath, "-S", "References"]);

        try
        {
            using (ProjectionAudit.BeginRequest(outer))
            {
                using (ProjectionAudit.BeginRequest(inner))
                {
                    Assert.Equal(0, ProjectionAudit.Verify(0));
                }

                Assert.Equal(1, ProjectionAudit.Verify(0));
            }
        }
        finally
        {
            ProjectionAudit.ResetForTesting();
        }
    }

    [Fact]
    public void ProjectionAudit_TracksProjectionDeclaredByAnAncestorCommand()
    {
        // `package --count search <id>` binds --count to the parent command, which the parser
        // accepts. Inspecting only the executing command missed it, so the subcommand rendered
        // its full payload and exited 0 with the projection silently discarded.
        var result = CommandLineBuilder.CreateRootCommand()
            .Parse(["package", "--count", "search", "Newtonsoft.Json"]);

        try
        {
            ProjectionAudit.BeginRequest(result);

            Assert.Equal(1, ProjectionAudit.Verify(0));
        }
        finally
        {
            ProjectionAudit.ResetForTesting();
        }
    }

    [Fact]
    public void ProjectionAudit_RejectsConflictDeclaredByAnAncestorCommand()
    {
        var result = CommandLineBuilder.CreateRootCommand()
            .Parse(["package", "--count", "--print", "search", "Newtonsoft.Json"]);

        Assert.False(ProjectionAudit.ValidateExclusive(result));
    }

    [Fact]
    public void ProjectionAudit_WrongFlagDoesNotSatisfyRequest()
    {
        // The print writer also serves --bare, so an untyped "honored" signal would let it
        // satisfy an unrelated recorded --count and let that drop escape.
        var result = CommandLineBuilder.CreateRootCommand()
            .Parse(["library", TestAssemblyPath, "-S", "References", "--count"]);

        try
        {
            ProjectionAudit.BeginRequest(result);
            ProjectionAudit.MarkHonored(ProjectionAudit.Print);

            Assert.Equal(1, ProjectionAudit.Verify(0));
        }
        finally
        {
            ProjectionAudit.ResetForTesting();
        }
    }

    [Fact]
    public void ProjectionAudit_MatchingFlagSatisfiesRequest()
    {
        var result = CommandLineBuilder.CreateRootCommand()
            .Parse(["library", TestAssemblyPath, "-S", "References", "--count"]);

        try
        {
            ProjectionAudit.BeginRequest(result);
            ProjectionAudit.MarkHonored(ProjectionAudit.Count);

            Assert.Equal(0, ProjectionAudit.Verify(0));
        }
        finally
        {
            ProjectionAudit.ResetForTesting();
        }
    }

    [Fact]
    public void ProjectionAudit_HelpTokenAsOptionValueDoesNotDisableAudit()
    {
        // '/h' here is the value of --type, not a help request. Matching raw token text
        // rather than option tokens would silently disable the audit for the invocation.
        var result = CommandLineBuilder.CreateRootCommand()
            .Parse(["type", "--library", TestAssemblyPath, "-S", "Classes", "--value", "--type", "/h"]);

        try
        {
            ProjectionAudit.BeginRequest(result);

            Assert.Equal(1, ProjectionAudit.Verify(0));
        }
        finally
        {
            ProjectionAudit.ResetForTesting();
        }
    }

    [Fact]
    public async Task ProjectionFlags_AreMutuallyExclusive()
    {
        // Two projections cannot both shape one payload, so honoring either one would
        // discard the other.
        var (exit, output, error) = await RunAppAsync(
            "type", "--library", TestAssemblyPath, "-S", "Classes", "--count", "--print");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--count cannot be combined with --print", error);
    }

    // ---- Lens-mode payload projections (issues #3395, #3396, #3398) ----
    //
    // Each of these modes renders its own payload and returns before the section pipeline, so
    // the pipeline's projection dispatch never runs for them. Every test here asserts the
    // projected payload rather than just a zero exit: the defect being guarded against is an
    // accepted projection that produces well-formed but unprojected output.

    [Fact]
    public async Task Discover_Count_CountsDiscoveredRows()
    {
        var (listExit, listOutput, _) = await RunAppAsync("project", "-D", "");
        Assert.Equal(0, listExit);
        // Data rows only: the markdown table adds a header and a separator line.
        var expected = listOutput.ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.StartsWith("| ", StringComparison.Ordinal)) - 2;
        Assert.True(expected > 0, "Discovery must list rows for this test to prove anything.");

        var (exit, output, error) = await RunAppAsync("project", "-D", "", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal(expected, int.Parse(output.Trim(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Discover_Count_CountsDiscoveredRowsForLibrary()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", "", "--count", "-S", "Library Info", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.True(int.Parse(output.Trim(), CultureInfo.InvariantCulture) > 0);
    }

    [Fact]
    public async Task Discover_Count_CountsDiscoveredRowsRatherThanTheDocument()
    {
        // Effective discovery renders discovered rows, but the command's own --count branch sat
        // ahead of the discovery branch and counted the inspection document instead — exiting 0
        // with a plausible number for a different payload. Pin the count to the payload.
        var (listExit, listOutput, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", "", "--tips", "q");
        Assert.Equal(0, listExit);

        var rows = listOutput.Split('\n').Count(l => l.StartsWith("| ", StringComparison.Ordinal)) - 2;
        Assert.True(rows > 0, "Discovery must list rows for this test to prove anything.");

        var (exit, output, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", "", "--count", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Equal(rows, int.Parse(output.Trim(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Discover_ShapeProjection_IsRefusedRatherThanAnsweredFromTheDocument()
    {
        var (exit, _, error) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", "", "--value", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("--value is not available with -D/--discover", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Discover_Count_CountsDiscoveredRowsForTypeSchema()
    {
        // The type/member discovery path branches in the command definition, before an options
        // record exists, so it reads the request from the parse result instead.
        var (exit, output, error) = await RunAppAsync("type", "--schema", "-D", "", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.True(int.Parse(output.Trim(), CultureInfo.InvariantCulture) > 0);
    }

    [Fact]
    public async Task Discover_Count_CountsStaticSchemaDiscoveryForLibrary()
    {
        // Static -D --schema returns before the library is resolved, a separate early return from
        // the effective-discovery one below it.
        var (countExit, countOutput, countError) = await RunAppAsync(
            "library", TestAssemblyPath, "--schema", "-D", "--count", "--tips", "q");

        Assert.Equal(0, countExit);
        Assert.Empty(countError);

        var (listExit, listOutput, _) = await RunAppAsync(
            "library", TestAssemblyPath, "--schema", "-D", "--tips", "q");
        Assert.Equal(0, listExit);

        // The count must match the payload it stands in for: rendered rows less header and separator.
        var rows = listOutput.Split('\n').Count(l => l.StartsWith("| ", StringComparison.Ordinal)) - 2;
        Assert.Equal(rows, int.Parse(countOutput.Trim(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task IlOffsetsFile_Count_CountsCoordinateRows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path,
            """
            first 0x06000001+0x1
            second 0x06000001+0x6
            """,
            TestContext.Current.CancellationToken);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library", TestAssemblyPath, "--il-offsets", path, "--count", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal("2", output.Trim());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    // Head, tail, an absolute range, an open range, and a window wider than the batch.
    [InlineData(new[] { "--rows", "2" }, 2)]
    [InlineData(new[] { "--rows", "2", "--tail" }, 2)]
    [InlineData(new[] { "--rows", "2..3" }, 2)]
    [InlineData(new[] { "--rows", "3.." }, 1)]
    [InlineData(new[] { "--rows", "9" }, 3)]
    public async Task IlOffsetsFile_Count_CountsTheWindowItRenders(string[] window, int expected)
    {
        // --rows narrows the rendered table, so it has to narrow --count identically.
        // Counting the unwindowed batch exits 0 with a plausible number describing a
        // payload the caller never asked for, which the projection audit cannot see.
        var path = Path.Combine(Path.GetTempPath(), $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path,
            """
            first 0x06000001+0x1
            second 0x06000001+0x6
            third 0x06000002+0x0
            """,
            TestContext.Current.CancellationToken);
        try
        {
            string[] head = ["library", TestAssemblyPath, "--il-offsets", path];
            string[] tail = ["--tips", "q"];

            var (renderExit, rendered, renderError) = await RunAppAsync([.. head, .. window, "--jsonl", .. tail]);
            var (countExit, counted, countError) = await RunAppAsync([.. head, .. window, "--count", .. tail]);

            Assert.Equal(0, renderExit);
            Assert.Equal(0, countExit);
            Assert.Empty(renderError);
            Assert.Empty(countError);

            // --jsonl emits exactly one object per rendered data row, so it states the
            // payload size without depending on how the table is formatted.
            var renderedRows = rendered
                .Split('\n')
                .Count(line => line.TrimStart().StartsWith('{'));

            // Guard against the window emptying the table, which would let a broken
            // count agree with a payload that proves nothing.
            Assert.Equal(expected, renderedRows);
            Assert.Equal(renderedRows, int.Parse(counted.Trim(), CultureInfo.InvariantCulture));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task IlOffsetsFile_Count_WindowsTheSameRowsTheTableKeeps()
    {
        // A count can match the rendered row total while describing different rows.
        // Head and tail must therefore be shown to select genuinely different labels,
        // otherwise a window applier that ignored direction would still look correct.
        var path = Path.Combine(Path.GetTempPath(), $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path,
            """
            first 0x06000001+0x1
            second 0x06000002+0x0
            """,
            TestContext.Current.CancellationToken);
        try
        {
            var (headExit, headOut, _) = await RunAppAsync(
                "library", TestAssemblyPath, "--il-offsets", path, "--rows", "1", "--head", "--tips", "q");
            var (tailExit, tailOut, _) = await RunAppAsync(
                "library", TestAssemblyPath, "--il-offsets", path, "--rows", "1", "--tail", "--tips", "q");

            Assert.Equal(0, headExit);
            Assert.Equal(0, tailExit);
            Assert.Contains("first", headOut, StringComparison.Ordinal);
            Assert.DoesNotContain("second", headOut, StringComparison.Ordinal);
            Assert.Contains("second", tailOut, StringComparison.Ordinal);
            Assert.DoesNotContain("first", tailOut, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task IlOffsetsFile_Count_DoesNotRequireASectionFilter()
    {
        // --count here counts coordinate rows, not section rows, so demanding -S would force
        // the caller to name a section the batch does not render.
        var path = Path.Combine(Path.GetTempPath(), $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "only 0x06000001+0x1\n", TestContext.Current.CancellationToken);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library", TestAssemblyPath, "--il-offsets", path, "--count", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.DoesNotContain("requires -S/--select", error);
            Assert.Equal("1", output.Trim());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task IlOffsetsFile_ShapeProjection_IsRefusedWithItsActualReason()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "only 0x06000001+0x1\n", TestContext.Current.CancellationToken);
        try
        {
            var (exit, _, error) = await RunAppAsync(
                "library", TestAssemblyPath, "--il-offsets", path, "--value", "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Contains("--value is not available with --il-offsets", error);
            // Not the section-count complaint, which is not the actual problem here.
            Assert.DoesNotContain("requires -S/--select", error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Print_ProjectsTheSelectedDocumentSectionAndRefusesUnprintableOnes()
    {
        // --print names the section whose rows carry the document, like every other payload
        // projection. There is no per-document flag to disagree with the selection.
        var (exit, output, _) = await RunAppAsync(
            "package", "Newtonsoft.Json@13.0.4", "-S", "Package README file", "--print");

        Assert.Equal(0, exit);
        Assert.NotEmpty(output);

        // Printability is a row capability. The whole-package listing also contains assemblies
        // and images, so it declares no printable payload and is refused rather than guessing.
        var (selectedExit, selectedOutput, selectedError) = await RunAppAsync(
            "package", "Newtonsoft.Json@13.0.4", "-S", "Files", "--print");

        Assert.Equal(1, selectedExit);
        Assert.Empty(selectedOutput);
        Assert.Contains("exactly one printable section", selectedError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Content_Count_CountsMatchedFilesBecauseThePayloadIsAVector()
    {
        // --content renders text, but it yields one structured row per matched file rather than a
        // single document, so it counts. The multi-match case is what proves it is not a scalar.
        var (rowsExit, rowsOutput, _) = await RunAppAsync(
            "package", "Newtonsoft.Json@13.0.4", "--content", "--path", "*.md", "--jsonl");
        Assert.Equal(0, rowsExit);
        var rows = rowsOutput.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.True(rows > 1, "The pattern must match more than one file for this test to prove anything.");

        var (exit, output, _) = await RunAppAsync(
            "package", "Newtonsoft.Json@13.0.4", "--content", "--path", "*.md", "--count");

        Assert.Equal(0, exit);
        Assert.Equal(rows, int.Parse(output.Trim(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Content_Count_IsZeroWhenNoFileMatches()
    {
        // A path that matches nothing still produces one row so the render can show it as absent.
        // Counting rows would report one match where there were none.
        var (exit, output, _) = await RunAppAsync(
            "package", "Newtonsoft.Json@13.0.4", "--content", "--path", "no-such-file.zzz", "--count");

        Assert.Equal(0, exit);
        Assert.Equal(0, int.Parse(output.Trim(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Content_Count_CountsMatchesNotAbsentPlaceholders()
    {
        // A package that matches nothing still renders a placeholder block, so the default
        // render emits one more row than there are files. The count follows the files:
        // --skip-empty drops the placeholder, and then the render and the count agree
        // exactly. Counting placeholders would report a match that never happened.
        var (withFile, withDir) = CreateLocalReadmePackage("Test.Count.HasAgents", "README.md", "readme", "agents");
        var (withoutFile, withoutDir) = CreateLocalReadmePackage("Test.Count.NoAgents", "README.md", "readme");
        try
        {
            var (renderExit, rendered, _) = await RunAppAsync(
                "package", withFile, withoutFile, "--path", "@agents", "--content", "--jsonl");
            var (skipExit, skipped, _) = await RunAppAsync(
                "package", withFile, withoutFile, "--path", "@agents", "--content", "--skip-empty", "--jsonl");
            var (countExit, counted, _) = await RunAppAsync(
                "package", withFile, withoutFile, "--path", "@agents", "--content", "--count");

            Assert.Equal(0, renderExit);
            Assert.Equal(0, skipExit);
            Assert.Equal(0, countExit);

            static int Rows(string output) =>
                output.Split('\n').Count(line => line.TrimStart().StartsWith('{'));

            // The placeholder is a rendered row, so the default render is deliberately larger.
            Assert.Equal(2, Rows(rendered));
            Assert.Equal(1, Rows(skipped));
            Assert.Equal(1, int.Parse(counted.Trim(), CultureInfo.InvariantCulture));
        }
        finally
        {
            Directory.Delete(withDir, recursive: true);
            Directory.Delete(withoutDir, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Discover_Count_EqualsTheRowsDiscoveryRenders(bool schema)
    {
        // The projection and the render each build their own row list, so a filter added to one
        // call and not the other makes --count answer a row set the command never renders --
        // at exit 0, with the audit satisfied because a count really was written.
        string[] args = schema
            ? ["library", TestAssemblyPath, "--schema", "-D", ""]
            : ["library", TestAssemblyPath, "-D", ""];

        var (countExit, countOutput, _) = await RunAppAsync([.. args, "--count"]);
        var (rowsExit, rowsOutput, _) = await RunAppAsync([.. args, "--jsonl"]);

        Assert.Equal(0, countExit);
        Assert.Equal(0, rowsExit);

        var rendered = rowsOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.StartsWith('{'));

        Assert.True(rendered > 0, "the probe must render rows, or it proves nothing.");
        Assert.Equal(rendered, int.Parse(countOutput.Trim(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Versions_IncludeUnlisted_Count_MatchesTheListingItRenders()
    {
        // --include-unlisted renders through a separate listing path, so it needs its own
        // projection dispatch; without one the count is dropped and the audit fails the run.
        var (countExit, countOutput, _) = await RunAppAsync(
            "package", "Newtonsoft.Json", "--versions", "--include-unlisted", "--count", "--tips", "q");
        var (rowsExit, rowsOutput, _) = await RunAppAsync(
            "package", "Newtonsoft.Json", "--versions", "--include-unlisted", "--jsonl", "--tips", "q");

        Assert.Equal(0, countExit);
        Assert.Equal(0, rowsExit);

        var rendered = rowsOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        Assert.True(rendered > 0, "the probe must render rows, or it proves nothing.");
        Assert.Equal(rendered, int.Parse(countOutput.Trim(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Discover_Print_RefusesInsteadOfPrintingTheGroundingDocument()
    {
        // The grounding branch sat ahead of the discovery branch, so --print fell into it and
        // returned the readme at exit 0 -- an unrelated payload for a projection of discovery.
        var (exit, output, error) = await RunAppAsync(
            "package", "Newtonsoft.Json@13.0.4", "-D", "", "--print");

        Assert.Equal(1, exit);
        Assert.Contains("-D/--discover", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Json.NET", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LensCount_WritesToTheRequestedOutputFile()
    {
        // A count is the command's payload, so --out has to apply to it. Writing it to stdout
        // instead leaves the requested file absent and silently ignores the option.
        var path = Path.Combine(Path.GetTempPath(), $"lens-count-{Guid.NewGuid():N}.txt");

        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", "Newtonsoft.Json@13.0.4", "--tfms", "--count", "--out", path);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(path), "--out was ignored: the requested file was never written.");
            Assert.Equal(8, int.Parse(File.ReadAllText(path).Trim(), CultureInfo.InvariantCulture));
            Assert.Empty(output.Trim());
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task Discover_ShapeProjection_ReportsTheLensRefusalWithoutRequiringASelection()
    {
        // The ordinary shape gate ran first and reported a missing -S, which is not the actual
        // problem: discovery renders its own payload and cannot answer a column projection.
        var (exit, _, error) = await RunAppAsync(
            "type", "--library", TestAssemblyPath, "-D", "--value", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("--value is not available with -D/--discover", error, StringComparison.Ordinal);
        Assert.DoesNotContain("requires -S/--select", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadmeSection_Count_CountsTheRowItRenders()
    {
        // The README section is a listing of one row, so --count answers over the same rows the
        // section renders rather than over the document body.
        var (exit, output, _) = await RunAppAsync(
            "package", "Newtonsoft.Json@13.0.4", "-S", "Package README file", "--count");
        var (pathsExit, pathsOutput, _) = await RunAppAsync(
            "package", "Newtonsoft.Json@13.0.4", "-S", "Package README file", "--paths");

        Assert.Equal(0, exit);
        Assert.Equal(0, pathsExit);
        Assert.Equal("1", output.Trim());
        Assert.Single(pathsOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task LensMode_SectionFilter_IsRefusedRatherThanIgnored()
    {
        // -S was previously accepted and then ignored by the lens, and --count required it,
        // so the mode was reachable only through a filter it did not honor.
        var (exit, output, error) = await RunAppAsync(
            "package", "Newtonsoft.Json", "--versions", "1", "-S", "Files", "--count");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("-S/--select is not available with --versions", error);
    }

    [Fact]
    public async Task Tfms_Count_CountsTheListedFrameworks()
    {
        var (listExit, listOutput, _) = await RunAppAsync("package", "Newtonsoft.Json@13.0.4", "--tfms");
        Assert.Equal(0, listExit);
        var expected = listOutput.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.True(expected > 0, "The package must list frameworks for this test to prove anything.");

        var (exit, output, error) = await RunAppAsync("package", "Newtonsoft.Json@13.0.4", "--tfms", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal(expected, int.Parse(output.Trim(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Tfms_ShapeProjection_IsRefused()
    {
        var (exit, output, error) = await RunAppAsync("package", "Newtonsoft.Json@13.0.4", "--tfms", "--value");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--value is not available with --tfms", error);
    }

    [Fact]
    public async Task Layout_Count_CountsFilesRatherThanRenderedTreeLines()
    {
        // The tree adds a line per directory, so a count taken from the rendered output would
        // not equal the number of files the lens actually lists. The package carries 16 files
        // under lib/ plus LICENSE.md, the nuspec, packageIcon.png, and README.md; the nuspec is
        // counted because this branch makes it a reachable package file.
        var (exit, output, error) = await RunAppAsync("package", "Newtonsoft.Json@13.0.4", "--layout", "--count");
        var (renderExit, rendered, _) = await RunAppAsync("package", "Newtonsoft.Json@13.0.4", "--layout");

        Assert.Equal(0, exit);
        Assert.Equal(0, renderExit);
        Assert.Empty(error);

        var count = int.Parse(output.Trim(), CultureInfo.InvariantCulture);
        Assert.Equal(20, count);

        // The point of the lens count is that it is a file count, not a line count. Pin the
        // relationship rather than only the literal, so a tree that grows directory nodes
        // cannot start agreeing with the count by coincidence.
        var renderedLines = rendered.Split('\n').Count(line => line.Trim().Length > 0);
        Assert.True(
            renderedLines > count,
            $"expected the tree to render more lines ({renderedLines}) than the {count} files it counts");
        Assert.Contains("Newtonsoft.Json.nuspec", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllLibraries_EmptyMatch_CountsZeroRatherThanReturningSilently()
    {
        // An empty match short-circuits ahead of the render path, which is exactly where a
        // projection goes missing without an empty-result probe to catch it. The section has to
        // be one this package genuinely has no rows for, not an unknown name: an unknown -S is
        // rejected before the render path is ever reached, so it would prove nothing here.
        var (exit, output, _) = await RunAppAsync(
            "package", "Newtonsoft.Json@13.0.4", "--all-libraries", "-S", "Non-normalized Paths",
            "--count", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Equal(0, int.Parse(output.Trim(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Versions_Count_CountsVersionsRatherThanPrintingOne()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Newtonsoft.Json", "--versions", "1", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        // The defect printed the version itself here, which parses as neither a count nor a
        // failure, so assert the count and not merely a zero exit.
        Assert.Equal("1", output.Trim());
    }


    [Fact]
    public async Task ProjectionFlags_ConflictIsMootUnderHelp()
    {
        // Help renders no payload, so there is nothing for two projections to fight over.
        // Rejecting the combination here would turn a working help request into an error.
        var (exit, output, _) = await RunAppAsync(
            "type", "--library", TestAssemblyPath, "-S", "Classes", "--count", "--print", "--help");

        Assert.Equal(0, exit);
        Assert.Contains("Usage", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Find_Count_ComposesWithJson()
    {
        // Found by the projection audit: --json was resolved before --count, so a count
        // request was answered with the full unprojected result set and exit 0.
        var (exit, output, _) = await RunAppAsync(
            "find", "Cache", "--library", TestAssemblyPath, "--count", "--json");

        Assert.Equal(0, exit);
        Assert.True(int.TryParse(output.Trim(), out _), $"expected a bare count, got: {output}");
    }

    [Theory]
    [InlineData("--fields")]
    [InlineData("--columns")]
    public async Task Find_ColumnProjectionWithJson_IsRejected(string projectionFlag)
    {
        // Same category error as #3386: --fields/--columns select table columns, but find's
        // --json emits the full per-result objects and has no column-slicing facility, so the
        // combination used to silently drop the column filter. It now fails closed.
        var (exit, output, error) = await RunAppAsync(
            "find", "Cache", "--library", TestAssemblyPath, projectionFlag, "Type", "--json");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("cannot be combined with --json", error);
    }

    [Fact]
    public async Task Find_MemberSearch_ColumnProjectionWithJson_IsRejected()
    {
        // The member-search path shares ExecuteAsync's guard, so it rejects too.
        var (exit, output, error) = await RunAppAsync(
            "find", "Cache", "--members", "--library", TestAssemblyPath, "--fields", "Member", "--json");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("cannot be combined with --json", error);
    }

    [Fact]
    public async Task Find_ColumnProjectionWithJsonl_IsHonored()
    {
        // Boundary: the row-oriented formats keep honoring column projection. Only document
        // --json is rejected.
        var (exit, _, error) = await RunAppAsync(
            "find", "Cache", "--library", TestAssemblyPath, "--columns", "Type", "--jsonl");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("cannot be combined with --json", error);
    }

    [Fact]
    public async Task Find_ColumnProjectionWithCountAndJson_IsHonored()
    {
        // --count reduces to a scalar and is excluded from the rejection.
        var (exit, output, error) = await RunAppAsync(
            "find", "Cache", "--library", TestAssemblyPath, "--fields", "Type", "--count", "--json");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("cannot be combined with --json", error);
        Assert.True(int.TryParse(output.Trim(), out _), $"expected a bare count, got: {output}");
    }

    [Fact]
    public async Task Find_Discovery_ColumnProjectionWithJson_IsHonored()
    {
        // The -D discovery branch honors projection itself and returns before the guard, so a
        // discovery request carrying --fields/--json must not be rejected.
        var (exit, output, error) = await RunAppAsync(
            "find", "Cache", "--library", TestAssemblyPath, "-D", "Results", "--fields", "Type", "--json");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("cannot be combined with --json", error);
        Assert.Contains("\"kind\":\"column\"", output);
    }

    [Fact]
    public async Task Implements_Count_ComposesWithJson()
    {
        var (exit, output, _) = await RunAppAsync(
            "implements", "IDisposable", "--library", TestAssemblyPath, "--count", "--json");

        Assert.Equal(0, exit);
        Assert.True(int.TryParse(output.Trim(), out _), $"expected a bare count, got: {output}");
    }

    [Fact]
    public async Task Extensions_Count_ComposesWithJson()
    {
        var (exit, output, _) = await RunAppAsync(
            "extensions", "String", "--library", TestAssemblyPath, "--count", "--json");

        Assert.Equal(0, exit);
        Assert.True(int.TryParse(output.Trim(), out _), $"expected a bare count, got: {output}");
    }

    [Fact]
    public async Task Depends_Count_ComposesWithJson()
    {
        // The type with dependencies matters: a type with none short-circuits before the
        // JSON branch, which is why an earlier probe using such a type saw no defect.
        var (exit, output, _) = await RunAppAsync(
            "depends", "SampleGenericClass`1", "--library", TestAssemblyPath, "--count", "--json");

        Assert.Equal(0, exit);
        Assert.True(int.TryParse(output.Trim(), out var count), $"expected a bare count, got: {output}");
        Assert.True(count > 0, "fixture must have dependencies for this to be a meaningful regression test");
    }

    [Fact]
    public async Task Type_SourceFiles_Value_RowSelectsUrl()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--value", "--row", "2", "--raw", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.Async.cs", output.Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Library_SourceFiles_Urls_RowSelectsUrl()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Source Files", "--urls", "--row", "2", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("https://raw.githubusercontent.com/dotnet/dotnet/", output.Trim());
    }

    [Fact]
    public async Task Type_SourceFiles_UrlsRejectsRowsMode()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--urls", "--rows", "1", "--raw", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--rows cannot be combined with --urls", error);
    }

    [Fact]
    public async Task Type_SourceFiles_PrintRequiresRowWhenMultipleUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--print", "--raw", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("selected section has 2 rows; use --row N|first|last to choose one row", error);
    }

    [Fact]
    public async Task Type_SourceFiles_PrintRowFetchesSelectedSource()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--print", "--row", "2", "--jsonl", "--raw", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var line = Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using var document = JsonDocument.Parse(line);
        Assert.Equal(2, document.RootElement.GetProperty("row").GetInt32());
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.Async.cs", document.RootElement.GetProperty("url").GetString());
        Assert.Contains("ReadAsInt32Async", document.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public async Task Type_SourceFiles_PrintRowFirstFetchesFirstSource()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--print", "--row", "first", "--jsonl", "--raw", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var line = Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using var document = JsonDocument.Parse(line);
        Assert.Equal(1, document.RootElement.GetProperty("row").GetInt32());
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.cs", document.RootElement.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Type_SourceFiles_PrintRowLastFetchesLastSource()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--print", "--row", "last", "--jsonl", "--raw", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var line = Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using var document = JsonDocument.Parse(line);
        Assert.Equal(2, document.RootElement.GetProperty("row").GetInt32());
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.Async.cs", document.RootElement.GetProperty("url").GetString());
    }


    [Fact]
    public async Task Type_SourceFiles_PrintJsonArrayEmitsSelectedRowAsSingleElementArray()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--print", "--row", "1", "--json-array", "--raw", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var rows = document.RootElement.EnumerateArray().ToArray();
        var single = Assert.Single(rows);
        Assert.Equal(1, single.GetProperty("row").GetInt32());
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.cs", single.GetProperty("url").GetString());
        Assert.Contains("JsonReader", single.GetProperty("content").GetString());
    }

    /// <summary>
    /// <c>--json</c> selects an output format and <c>--print</c> selects an output shape,
    /// so they compose: the projection owns the request and the plain type surface must not
    /// claim it. Regression for #3379, where the type-surface early return preceded the
    /// projection dispatch and silently discarded <c>--print</c> with exit 0.
    /// </summary>
    [Fact]
    public async Task Type_SourceFiles_PrintJson_EmitsSelectedDocumentNotTypeSurface()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--print", "--row", "1", "--json", "--raw", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(1, document.RootElement.GetProperty("row").GetInt32());
        Assert.Equal("Source Files", document.RootElement.GetProperty("section").GetString());
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.cs", document.RootElement.GetProperty("url").GetString());
        Assert.Contains("JsonReader", document.RootElement.GetProperty("content").GetString());
        Assert.False(document.RootElement.TryGetProperty("metadata_name", out _));
    }

    /// <summary>
    /// Cardinality validation belongs to the projection, so it must run under <c>--json</c> too.
    /// </summary>
    [Fact]
    public async Task Type_SourceFiles_PrintJson_RequiresRowWhenMultipleUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--print", "--json", "--raw", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("selected section has 2 rows; use --row N|first|last to choose one row", error);
    }

    [Fact]
    public async Task Type_SourceFiles_UrlsJson_EmitsProjectedRowsNotTypeSurface()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--urls", "--json", "--raw", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var rows = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, rows.Length);
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.cs", rows[0].GetProperty("url").GetString());
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.Async.cs", rows[1].GetProperty("url").GetString());
    }

    [Fact]
    public async Task Type_SourceFiles_ValueJson_EmitsSelectedRowNotTypeSurface()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--value", "--row", "2", "--json", "--raw", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(2, document.RootElement.GetProperty("row").GetInt32());
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.Async.cs", document.RootElement.GetProperty("value").GetString());
    }

    /// <summary>
    /// A failed acquisition of the selected row must stay visible rather than degrade into
    /// success-shaped output. Only the transport is substituted, so the real SourceFetcher
    /// still applies scheme restriction, caching, and status handling.
    /// </summary>
    [Fact]
    public async Task Type_SourceFiles_PrintRow_FetchFailureIsHardError()
    {
        using var client = new HttpClient(new NotFoundHandler());
        string cacheDir = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-fetch-failure-{Guid.NewGuid():N}");
        try
        {
            DotnetInspector.Core.HttpClientFactory.SetUntrustedFetchForTesting(client);
            NuGetCache.Initialize("dotnet-inspect", basePath: cacheDir);
            var (exit, output, error) = await RunAppAsync(
                "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
                "-S", "Source Files", "--print", "--row", "2", "--raw", "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("failed to fetch the document for row 2 from", error);
            Assert.Contains("/Src/Newtonsoft.Json/JsonReader.Async.cs", error);
        }
        finally
        {
            DotnetInspector.Core.HttpClientFactory.SetUntrustedFetchForTesting(null);
            NuGetCache.Initialize("dotnet-inspect");
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
        }
    }

    /// <summary>
    /// The acquisition guarantee must hold under <c>--json</c> as well; before #3379 this
    /// combination exited 0 with the type surface and never attempted the fetch.
    /// </summary>
    [Fact]
    public async Task Type_SourceFiles_PrintRowJson_FetchFailureIsHardError()
    {
        using var client = new HttpClient(new NotFoundHandler());
        string cacheDir = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-fetch-failure-json-{Guid.NewGuid():N}");
        try
        {
            DotnetInspector.Core.HttpClientFactory.SetUntrustedFetchForTesting(client);
            NuGetCache.Initialize("dotnet-inspect", basePath: cacheDir);
            var (exit, output, error) = await RunAppAsync(
                "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
                "-S", "Source Files", "--print", "--row", "2", "--json", "--raw", "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("failed to fetch the document for row 2 from", error);
        }
        finally
        {
            DotnetInspector.Core.HttpClientFactory.SetUntrustedFetchForTesting(null);
            NuGetCache.Initialize("dotnet-inspect");
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
        }
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

    [Fact]
    public async Task Type_JsonArrayRequiresProjectionShape()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--json-array", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--json-array requires --value, --urls, --paths, or --print", error);
    }

    [Fact]
    public async Task Type_CountAndBare_ComposesForTableSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonConvert", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Member Index", "--count", "--bare", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("67", output.Trim());
    }

    [Fact]
    public async Task Type_CountAndBare_ComposesForVectorSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--count", "--bare", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("2", output.Trim());
    }

    [Fact]
    public async Task Member_SourceLocations_UnpinnedSnupkgPackage_ResolvesSourceRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConvert", "--package", "Newtonsoft.Json",
            "-m", "SerializeObject", "-S", "Source Locations", "--rows", "6", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Source Locations", output);
        Assert.Contains("`SerializeObject:1`", output);
        Assert.Contains("JsonConvert.cs", output);
        Assert.Contains("raw.githubusercontent.com/JamesNK/Newtonsoft.Json", output);
    }

    [Fact]
    public async Task Member_SourceLocations_UrlsJsonl_RowSelectsUrl()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConvert", "--package", "Newtonsoft.Json@13.0.4",
            "-m", "SerializeObject", "-S", "Source Locations", "--urls", "--row", "2", "--jsonl", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var line = Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using var document = JsonDocument.Parse(line);
        Assert.Equal(2, document.RootElement.GetProperty("row").GetInt32());
        Assert.Contains("JsonConvert.cs", document.RootElement.GetProperty("url").GetString());
        Assert.Equal("SerializeObject:2", document.RootElement.GetProperty("label").GetString());
    }

    [Fact]
    public async Task Member_SourceLocations_Paths_EmitsSourcePaths()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConvert", "--package", "Newtonsoft.Json@13.0.4",
            "-m", "SerializeObject", "-S", "Source Locations", "--paths", "--row", "1", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("/_/Src/Newtonsoft.Json/JsonConvert.cs", output.Trim());
    }

    [Fact]
    public async Task Member_SourceLocations_Value_DecodesCodeMarkup()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--platform", "System.Text.Json",
            "-m", "Serialize", "-S", "Source Locations", "--fields", "Signature", "--value", "--row", "1", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Serialize<TValue>", output);
        Assert.DoesNotContain("&lt;", output);
        Assert.DoesNotContain("<code>", output);
    }

    [Fact]
    public async Task Member_SourceLocations_PrintRowFetchesSourceFile()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConvert", "--package", "Newtonsoft.Json@13.0.4",
            "-m", "SerializeObject", "-S", "Source Locations", "--print", "--row", "1", "--jsonl", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var line = Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using var document = JsonDocument.Parse(line);
        Assert.Equal(1, document.RootElement.GetProperty("row").GetInt32());
        Assert.Contains("JsonConvert.cs", document.RootElement.GetProperty("url").GetString());
        Assert.Contains("SerializeObject", document.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public async Task Member_SourceLocations_Tsv_RepeatsStartLineForSingleLineMethods()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConvert", "--package", "Newtonsoft.Json@13.0.4",
            "-m", "SerializeObject", "-S", "Source Locations", "--tsv", "--no-headers", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);

        var rows = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t'))
            .Where(cells => cells.Length >= 5)
            .ToDictionary(cells => cells[0], cells => (Line: cells[3], EndLine: cells[4]));

        Assert.Equal(("532", "532"), rows["SerializeObject:1"]);
        Assert.Equal(("548", "548"), rows["SerializeObject:2"]);
        Assert.Equal(("581", "585"), rows["SerializeObject:4"]);
    }

    [Fact]
    public async Task Member_SourceLocations_BlobUrls_RendersBrowserUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConvert", "--package", "Newtonsoft.Json@13.0.4",
            "-m", "SerializeObject", "-S", "Source Locations", "--blob", "--tsv", "--no-headers", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("github.com/JamesNK/Newtonsoft.Json/blob/", output);
        Assert.DoesNotContain("raw.githubusercontent.com", output);
    }

    [Fact]
    public async Task Member_SourceLocations_RawOverridesBlob()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConvert", "--package", "Newtonsoft.Json@13.0.4",
            "-m", "SerializeObject", "-S", "Source Locations", "--blob", "--raw", "--tsv", "--no-headers", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("raw.githubusercontent.com/JamesNK/Newtonsoft.Json", output);
        Assert.DoesNotContain("github.com/JamesNK/Newtonsoft.Json/blob/", output);
    }

    [Fact]
    public async Task Member_SourceLocations_Discovery_DoesNotAcquirePdb()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--platform", "System.Text.Json",
            "-m", "Serialize", "-D", "Source Locations", "--verbose", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("| File | column |", output);
        Assert.Contains("| Line | column |", output);
        Assert.DoesNotContain("Loaded PDB", error);
        Assert.DoesNotContain("MSDL symbol", error);
    }

    [Fact]
    public async Task Member_SelectedOverload_DefaultShowsSignatureOnly()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToElement" },
            OverloadIndex = 1
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Signature", output);
        Assert.Contains("public static System.Text.Json.JsonElement SerializeToElement<TValue>(TValue value, System.Text.Json.JsonSerializerOptions? options = null)", output);
        Assert.Contains("Type: System.Text.Json.JsonSerializer", output);
        Assert.DoesNotContain("## Methods", output);
        Assert.DoesNotContain("## Decompiled Source", output);
        Assert.DoesNotContain("## Original Source", output);
        Assert.DoesNotContain("## IL", output);
    }

    [Fact]
    public async Task Member_ListDefault_RendersCompactSummaryRows()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer"
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Properties", output);
        Assert.Contains("## Method Groups", output);
        Assert.Contains("| Name | Return Type | Overloads |", output);
        Assert.Contains("| SerializeToNode | System.Text.Json.Nodes.JsonNode? | 5 |", output);
        Assert.DoesNotContain("| Name | Signature | Description |", output);
    }

    [Fact]
    public async Task Member_FilteredDefault_RendersOverloadRows()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToNode" },
            Select = ["Member Index"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Member Index", output);
        Assert.Contains("| Selector | Stable | Canonical Signature |", output);
        Assert.Contains("`SerializeToNode:1`", output);
        Assert.Contains("SerializeToNode~", output);
        Assert.Contains("M:System.Text.Json.JsonSerializer.SerializeToNode(", output);
        Assert.DoesNotContain("## Method Groups", output);
        Assert.DoesNotContain("| Name | Return Type | Overloads |", output);
    }

    [Fact]
    public async Task Member_SingleOverloadFilter_DefaultStaysOverloadInventory()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializerOptions",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GetConverter" },
            Select = ["Member Index"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Member Index", output);
        Assert.Contains("| Selector | Stable | Canonical Signature |", output);
        Assert.Contains("`GetConverter`", output);
        Assert.DoesNotContain("## Signature", output);

        options = options with { Select = ["Methods"] };
        (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Methods", output);
        Assert.Contains("| Name | Digest | Signature |", output);
        Assert.DoesNotContain("## Signature", output);
    }

    [Fact]
    public async Task Member_SingleOverloadFilter_MixedInfoAndDetailSelect_RendersDetail()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializerOptions",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GetConverter" },
            Select = ["Info", "IL"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## IL", output);
        Assert.Contains("IL_0000:", output);
    }

    [Fact]
    public async Task Member_SingleOverloadFilter_SelectSignature_RendersSignature()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializerOptions",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GetConverter" },
            Select = ["Signature"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Signature", output);
        Assert.Contains("public System.Text.Json.Serialization.JsonConverter GetConverter(System.Type typeToConvert)", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_DiscoverEffective_ListsEnabledDetailSections()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToElement" },
            OverloadIndex = 1,
            Discover = []
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Signature | section |", output);
        Assert.Contains("| Decompiled Source | section |", output);
        Assert.Contains("| Annotated Source | section (opt-in) |", output);
        Assert.Contains("| Original Source | section |", output);
        Assert.Contains("| IL | section |", output);
        Assert.Contains("| Calls | section (opt-in) |", output);
        Assert.Contains("| Callers | section (opt-in) |", output);
        Assert.Contains("| Unsafe Operations | section (opt-in) |", output);
        Assert.Contains("| Call Graph | section (opt-in) |", output);
        Assert.Contains("| Facts | section (opt-in) |", output);
        Assert.DoesNotContain("IR (Stages)", output);
        Assert.DoesNotContain("| Methods | section |", output);
    }

    [Fact]
    public async Task Member_DumpStages_IsNotRegistered()
    {
        var (exit, _, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json", "--dump-stages", "--tips", "q");

        Assert.NotEqual(0, exit);
        Assert.Contains("Unrecognized option '--dump-stages'", error);
    }

    [Fact]
    public async Task Member_SelectedOverload_DiscoverSchema_ListsDetailSectionsWithCallGraphOptIn()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToElement" },
            OverloadIndex = 1,
            Discover = [],
            Schema = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Original Source | section |", output);
        Assert.Contains("| Calls | section (opt-in) |", output);
        Assert.Contains("| Callers | section (opt-in) |", output);
        Assert.Contains("| Call Graph | section (opt-in) |", output);
        Assert.Contains("| Facts | section (opt-in) |", output);
        Assert.Contains("| Unsafe Operations | section (opt-in) |", output);
    }

    [Fact]
    public async Task Member_BareNameCallGraph_AutoSelectsSingleOverload()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallGraphFixture).FullName!, "--library", TestAssemblyPath,
            nameof(MemberCallGraphFixture.Inner), "-S", "Call Graph", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Call Graph", output);
        Assert.Contains(nameof(MemberCallGraphFixture.RootCall), output);
        Assert.DoesNotContain("Select value 'Call Graph' not found", error);
    }

    [Fact]
    public async Task Member_CallGraphCount_AnswersRowLoweringNotRenderedTree()
    {
        string[] baseArgs =
        [
            "member", typeof(MemberCallGraphFixture).FullName!, "--library", TestAssemblyPath,
            nameof(MemberCallGraphFixture.Inner), "-S", "Call Graph", "--tips", "q",
        ];

        var (tableExit, tableOutput, tableError) = await RunAppAsync([.. baseArgs, "--table"]);
        Assert.Equal(0, tableExit);
        Assert.Empty(tableError);

        // The lowering itself supplies the expected number, so this follows the declared
        // row unit rather than pinning a literal that a row-unit change would silently pass.
        var loweredRows = tableOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Count(line => !string.IsNullOrWhiteSpace(line));
        Assert.True(loweredRows > 0, "fixture must produce a non-empty graph");

        var (countExit, countOutput, countError) = await RunAppAsync([.. baseArgs, "--count"]);
        Assert.Equal(0, countExit);
        Assert.Empty(countError);
        Assert.Equal(loweredRows, int.Parse(countOutput.Trim(), CultureInfo.InvariantCulture));

        // The count answers the graph's row lowering, so it does not change with the
        // rendering the caller selected.
        var (tableCountExit, tableCountOutput, _) = await RunAppAsync([.. baseArgs, "--table", "--count"]);
        Assert.Equal(0, tableCountExit);
        Assert.Equal(loweredRows, int.Parse(tableCountOutput.Trim(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Member_BareNameCallGraph_AmbiguousOverloadReportsSelectorHint()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath,
            nameof(MemberCallsFixture.Overloaded), "-S", "Call Graph", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("section 'Call Graph' requires a single selected overload", error);
        Assert.Contains("Overloaded~<digest>", error);
        Assert.Contains("Overloaded:1 through Overloaded:2", error);
        Assert.DoesNotContain("Select value 'Call Graph' not found", error);
    }

    [Fact]
    public async Task Member_BareNameCallersWithCallerScope_AutoSelectsSingleOverload()
    {
        var testDirectory = Path.GetDirectoryName(TestAssemblyPath)!;
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallGraphFixture).FullName!, "--library", TestAssemblyPath,
            nameof(MemberCallGraphFixture.Inner), "-S", "Callers", "--bin", testDirectory, "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Callers", output);
        Assert.Contains(nameof(MemberCallGraphFixture.Mid), output);
    }

    [Fact]
    public async Task Member_BareNameCallersWithCallerScope_AmbiguousOverloadReportsSelectorHint()
    {
        var testDirectory = Path.GetDirectoryName(TestAssemblyPath)!;
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath,
            nameof(MemberCallsFixture.Overloaded), "-S", "Callers", "--bin", testDirectory, "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("section 'Callers' requires a single selected overload", error);
        Assert.Contains("Overloaded~<digest>", error);
        Assert.Contains("Overloaded:1 through Overloaded:2", error);
    }

    [Fact]
    public async Task Member_BareNameCallerScope_AutoSelectsSingleOverloadWithoutExplicitSection()
    {
        var testDirectory = Path.GetDirectoryName(TestAssemblyPath)!;
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallGraphFixture).FullName!, "--library", TestAssemblyPath,
            nameof(MemberCallGraphFixture.Inner), "--bin", testDirectory, "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Callers", output);
        Assert.Contains(nameof(MemberCallGraphFixture.Mid), output);
    }

    [Fact]
    public async Task Member_BareNameCallerScope_AmbiguousOverloadReportsSelectorHint()
    {
        var testDirectory = Path.GetDirectoryName(TestAssemblyPath)!;
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath,
            nameof(MemberCallsFixture.Overloaded), "--bin", testDirectory, "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("section 'Callers' requires a single selected overload", error);
        Assert.Contains("Overloaded~<digest>", error);
        Assert.Contains("Overloaded:1 through Overloaded:2", error);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectDecompiledSource_RendersPlainCSharp()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToElement" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Decompiled Source" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Decompiled Source", output);
        Assert.Contains("```csharp", output);
        Assert.DoesNotMatch(@"// IL_[0-9A-Fa-f]{4}: ", output);
    }

    [Fact]
    public async Task Member_KeywordParameterNames_EscapesSignatureAndDecompiledSourceHeader()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(SampleKeywordParameterHost).FullName!, "--library", TestAssemblyPath,
            nameof(SampleKeywordParameterHost.Instance), "-S", "Signature,Decompiled Source", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("public int Instance(int @object, string @class)", output);
        Assert.DoesNotContain("public int Instance(int object, string class)", output);
        Assert.Contains("@object + @class.Length", output);
    }

    [Fact]
    public async Task Member_RefReadonlyReturn_PreservesDecompiledSourceHeader()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(SampleRefReadonlyReturnHost).FullName!, "--library", TestAssemblyPath,
            nameof(SampleRefReadonlyReturnHost.ChooseReadonly), "-S", "Signature,Decompiled Source", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("public static ref readonly int ChooseReadonly(in int left, in int right, bool chooseLeft)", output);
        Assert.DoesNotContain("public static ref int ChooseReadonly", output);
        Assert.Contains("return ref left;", output);
        Assert.Contains("return ref right;", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectAnnotatedSource_RendersMixedView()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToElement" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Annotated Source" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Annotated Source", output);
        Assert.Contains("```csharp", output);
        Assert.Matches(@"// IL_[0-9A-Fa-f]{4}: ", output);
    }

    [Fact]
    public async Task Member_HostileIlOperand_StaysInsideMarkdownAndJsonCodeSections()
    {
        const string injected = "public int Injected() => 42; //";
        const string unsafeOperand = "field\n    public int Injected() => 42; //";
        const string safeOperand = "field     public int Injected() => 42; //";
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-hostile-il-command-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var dllPath = Path.Combine(tempDir, "HostileIlOperand.dll");
            WriteHostileIlOperandAssembly(dllPath);

            var (markdownExit, markdown, markdownError) = await RunAppAsync(
                "member", "Hostile.Target", "--library", dllPath,
                "GetCount:1", "-S", "Annotated Source,IL", "--tips", "q");

            Assert.Equal(0, markdownExit);
            Assert.Empty(markdownError);
            Assert.DoesNotContain(unsafeOperand, markdown, StringComparison.Ordinal);
            Assert.Equal(2, markdown.Split(safeOperand, StringSplitOptions.None).Length - 1);
            Assert.DoesNotContain(
                markdown.ReplaceLineEndings("\n").Split('\n'),
                line => line.TrimStart().StartsWith(injected, StringComparison.Ordinal));

            foreach (string section in new[] { "Annotated Source", "IL" })
            {
                var (jsonExit, json, jsonError) = await RunAppAsync(
                    "member", "Hostile.Target", "--library", dllPath,
                    "GetCount:1", "-S", section, "--print", "--json-array", "--tips", "q");

                Assert.Equal(0, jsonExit);
                Assert.Empty(jsonError);
                using var document = JsonDocument.Parse(json);
                var stringValues = string.Join("\n", JsonStrings(document.RootElement));
                Assert.DoesNotContain(unsafeOperand, stringValues, StringComparison.Ordinal);
                Assert.Contains(safeOperand, stringValues, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// The <c>--focus</c> caret gesture is a fact renderer, so it inherits the
    /// fold in <c>AnnotationText.Format</c> — including across the line wrapping
    /// it applies, where each wrapped chunk gets its own <c>//</c> gutter. This
    /// pins the interaction between the caret gesture and the fact fold, which
    /// arrived on separate branches and first met here.
    /// </summary>
    [Fact]
    public async Task Member_HostileFactDetail_StaysInsideCommentsUnderFocus()
    {
        const string injected = "public int Injected() => 42; //";
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-hostile-fact-command-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var dllPath = Path.Combine(tempDir, "HostileFactDetail.dll");
            WriteHostileFactDetailAssembly(dllPath);

            var (exit, output, error) = await RunAppAsync(
                "member", "Hostile.Target", "--library", dllPath,
                "Make", "-S", "Annotated Source", "--focus", "allocation", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);

            // Non-vacuity: the caret gesture and the fact must both render, or
            // there would be no untrusted text on the surface under test.
            Assert.Contains("^^^^", output);
            Assert.Contains("alloc.new(", output);

            foreach (string line in output.ReplaceLineEndings("\n").Split('\n'))
            {
                int payload = line.IndexOf(injected, StringComparison.Ordinal);
                if (payload < 0)
                    continue;
                int comment = line.IndexOf("//", StringComparison.Ordinal);
                Assert.InRange(comment, 0, payload);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Member_AnnotatedSource_WithoutFocus_UsesNoCaretGesture()
    {
        var (exit, output, _) = await RunAppAsync(
            "member", typeof(CommandCaretGestureFixture).FullName!, "--library", TestAssemblyPath,
            "Pump:1", "-S", "Annotated Source", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("## Annotated Source", output);
        Assert.DoesNotContain("^^^^", output);
    }

    [Fact]
    public async Task Member_AnnotatedSource_UnknownFocus_SaysSoAndNamesTheAvailableFamilies()
    {
        // Promotion never hides a fact, so an unmatched focus renders exactly
        // like no focus at all. Without the note a typo is indistinguishable
        // from an honest absence.
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CommandCaretGestureFixture).FullName!, "--library", TestAssemblyPath,
            "Pump:1", "-S", "Annotated Source", "--focus", "alocation", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("^^^^", output);
        Assert.Contains("--focus 'alocation' matched no facts here", error);
        Assert.Contains("allocation", error);
    }

    [Fact]
    public async Task Member_AnnotatedSource_MatchedFocus_SaysNothing()
    {
        var (exit, _, error) = await RunAppAsync(
            "member", typeof(CommandCaretGestureFixture).FullName!, "--library", TestAssemblyPath,
            "Pump:1", "-S", "Annotated Source", "--focus", "allocation", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("matched no facts", error);
    }

    /// <summary>
    /// Every projection that can carry a caret block, each through a different
    /// formatting call. Two properties per case: the caret actually renders (so
    /// the case is not vacuous), and the hoist marker — an in-band control
    /// character — never survives into output.
    /// </summary>
    [Theory]
    [InlineData("CommandCaretGestureFixture", "Pump:1", "Annotated Source", "allocation")]
    [InlineData("CommandCaretGestureFixture", "Pump:1", "Annotated Source", "alloc")]
    [InlineData("CommandCaretGestureFixture", "Make:1", "Annotated Source", "allocation")]
    [InlineData("CostOverlayFixture", "Caller", "Cost Overlay", "cost")]
    [InlineData("CostOverlayFixture", "CallsExceptionOnly", "Semantics Overlay", "semantics")]
    public async Task Member_Focus_RendersCaretsAndNeverLeaksTheHoistMarker(
        string fixture, string selector, string section, string focus)
    {
        string typeName = fixture == "CostOverlayFixture"
            ? typeof(CostOverlayFixture).FullName!
            : typeof(CommandCaretGestureFixture).FullName!;

        var (exit, output, _) = await RunAppAsync(
            "member", typeName, "--library", TestAssemblyPath,
            selector, "--index", "1", "--all", "-S", section, "--focus", focus, "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("^^^^", output);
        Assert.DoesNotContain(ILInspector.Decompiler.Annotations.AnnotationCaret.HoistMarker, output);

        var lines = output.ReplaceLineEndings("\n").Split('\n');
        foreach (int i in Enumerable.Range(0, lines.Length)
            .Where(i => lines[i].Contains("^^^^", StringComparison.Ordinal)))
        {
            string statement = lines[i - 1];
            Assert.StartsWith("//", lines[i], StringComparison.Ordinal);
            Assert.Equal(
                statement.Length - statement.AsSpan().TrimStart().Length,
                lines[i].IndexOf('^'));
        }
    }

    [Fact]
    public async Task Type_DoesNotOfferFocus()
    {
        // The caret gesture only renders into member sections (Annotated
        // Source, Cost Overlay, Semantics Overlay). Offering --focus on `type`
        // would be a switch that cannot change any output there.
        var (exit, _, error) = await RunAppAsync(
            "type", typeof(CommandCaretGestureFixture).FullName!, "--library", TestAssemblyPath,
            "--focus", "allocation", "--tips", "q");

        Assert.NotEqual(0, exit);
        Assert.Contains("--focus", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Member_AnnotatedSource_FocusPromotesFactsToAlignedCaretComments()
    {
        var (exit, output, _) = await RunAppAsync(
            "member", typeof(CommandCaretGestureFixture).FullName!, "--library", TestAssemblyPath,
            "Pump:1", "-S", "Annotated Source", "--focus", "allocation", "--tips", "q");

        Assert.Equal(0, exit);
        var lines = output.ReplaceLineEndings("\n").Split('\n');
        var caretIndexes = Enumerable.Range(0, lines.Length)
            .Where(i => lines[i].Contains("^^^^", StringComparison.Ordinal))
            .ToList();

        // The fixture allocates at the body's base column and again inside a
        // loop, so both depths are exercised.
        Assert.True(caretIndexes.Count >= 2, $"expected carets at two depths, got {caretIndexes.Count}");
        Assert.True(
            caretIndexes.Select(i => lines[i].IndexOf('^')).Distinct().Count() >= 2,
            "the two carets must sit at different columns");

        foreach (int i in caretIndexes)
        {
            string line = lines[i];

            // The block is spliced into a ```csharp fence, so it must stay
            // comments, and it sits on the member declaration column.
            Assert.StartsWith("//", line, StringComparison.Ordinal);

            // Carets point at the statement on the preceding line, exactly.
            string statement = lines[i - 1];
            Assert.Equal(
                statement.Length - statement.AsSpan().TrimStart().Length,
                line.IndexOf('^'));
            Assert.Equal(statement.Trim().Length, line.Count(c => c == '^'));
        }

        // The hoist marker is an internal layout signal; it must never survive
        // into rendered output, where it would print as a control character.
        Assert.DoesNotContain(ILInspector.Decompiler.Annotations.AnnotationCaret.HoistMarker, output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectSourceDiff_RendersOriginalVsDecompiledDiff()
    {
        using var stream = File.OpenRead(TestAssemblyPath);
        using var peReader = new PEReader(stream);
        var api = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
        var type = Assert.Single(api.Types, t => t.FullName == typeof(CommandExecutionSourceDiffFixture).FullName);
        var member = Assert.Single(type.Members, m => m.Name == nameof(CommandExecutionSourceDiffFixture.AddOne));
        type.Members = [member];

        var options = new MemberOptions
        {
            AssemblyPath = TestAssemblyPath,
            DllPath = TestAssemblyPath,
            TypeName = type.FullName,
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { nameof(CommandExecutionSourceDiffFixture.AddOne) },
            OverloadIndex = member.DeclaringOverloadIndex ?? 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SectionNames.SourceDiff },
            MethodSource = new MethodSourceContext(
                """
                public int AddOne(int value)
                {
                    return value + 2;
                }
                """,
                SourceUrl: null)
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => ApiCommand.WriteTypeOutputAsync(type, foundIn: "dotnet-inspect.Tests", packageName: null, packageVersion: null, apiSource: null, selectedTfm: null, options));

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Source Diff", output);
        Assert.Contains("```diff", output);
        Assert.Contains("--- Original Source", output);
        Assert.Contains("+++ Decompiled Source", output);
        Assert.Contains("-    return value + 2;", output);
        Assert.Contains("+public int AddOne(int value) => value + 1;", output);
        Assert.DoesNotContain("## Original Source", output);
        Assert.DoesNotContain("## Decompiled Source", output);
    }

    [Fact]
    public async Task Member_SingleOverload_SourceCategory_IncludesSourceDiff()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CommandExecutionSourceDiffFixture).FullName!, "--library", TestAssemblyPath,
            nameof(CommandExecutionSourceDiffFixture.AddOne), "-S", "@Source", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Decompiled Source", output);
        Assert.Contains("## Annotated Source", output);
        Assert.Contains("## Source Diff", output);
        Assert.Contains("## IL", output);
    }

    [Fact]
    public async Task Member_NonPublicMethod_UnderIncludeAll_RendersBodyAndIL()
    {
        // #1323: a non-public method selected under --all must render its body and IL, not
        // report "has no IL body". The body-load path counts overloads on the same
        // visibility basis the member index numbered them on (all overloads under --all).
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath,
            "--all", "InternalHelper:1", "-S", "Decompiled Source,IL", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Decompiled Source", output);
        Assert.Contains("## IL", output);
        Assert.DoesNotContain("has no IL body", output);
        Assert.Contains("InternalHelper", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SourceCategory_IncludesIlAndNoLoweredSource()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath,
            "Overloaded:2", "-S", "@Source", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Decompiled Source", output);
        Assert.Contains("## Annotated Source", output);
        Assert.Contains("## IL", output);
        Assert.DoesNotContain("## Lowered Source", output);
    }

    [Fact]
    public async Task Member_SelectDecompiledSource_RequestTrace_RecordsDecompileOutcome()
    {
        // The app converts the decompiler's telemetry-free trace shape into a
        // request-trace breadcrumb: a decompile.method stage carrying the
        // fidelity outcome and the symbol source the render actually consulted.
        using var diagram = RequestMermaidDiagram.Start();

        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToElement" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Decompiled Source" }
        };

        var (exit, _, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        var mermaid = diagram.ToMermaid();
        Assert.Matches(@"decompile\.method<br/>SerializeToElement \(\w+, pdb:\w+\)", mermaid);
    }

    [Fact]
    public async Task Member_SelectDecompiledSourceAndFacts_RequestTraceKeepsDecompileOutcome()
    {
        using var diagram = RequestMermaidDiagram.Start();

        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToElement" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Decompiled Source", "Facts" }
        };

        var (exit, _, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        var mermaid = diagram.ToMermaid();
        Assert.Matches(@"decompile\.method<br/>SerializeToElement \(\w+, pdb:\w+\)", mermaid);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectFacts_RendersHiddenFactSection()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToElement" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Facts" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // Facts is explicitly selected, so the section renders even when the
        // method body has no hidden facts (positive-only: the empty-state notes
        // absence of findings, never asserts the method is fact-free).
        Assert.Contains("## Facts", output);
        Assert.Contains("No hidden facts found", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectFacts_RendersStructuredResearchRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FactsTableFixture).FullName!, "--library", TestAssemblyPath,
            nameof(FactsTableFixture.BoxInt), "-S", "Facts", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Facts", output);
        Assert.Contains("| Member | IL | Cs Line | Anchor | Category | Id | Detail | Conditionality |", output);
        Assert.Contains("FactsTableFixture::BoxInt", output);
        Assert.Contains("`IL_", output);
        Assert.Contains("| offset | Allocation | alloc.box | `int; alloc=boxed System.Int32; path=straight-line; path-confidence=dominates-return; post-dominance=return-post-dominates; escape=escapes; escape-kind=escapes-return; multiplicity=once` | Always |", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectFacts_TsvIncludesStructuredColumns()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FactsTableFixture).FullName!, "--library", TestAssemblyPath,
            nameof(FactsTableFixture.BoxInt), "-S", "Facts", "--tsv", "--no-headers", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("FactsTableFixture::BoxInt\tIL_", output);
        Assert.Contains("\toffset\tAllocation\talloc.box\tint; alloc=boxed System.Int32; path=straight-line; path-confidence=dominates-return; post-dominance=return-post-dominates; escape=escapes; escape-kind=escapes-return; multiplicity=once\tAlways", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectFidelityCauses_ReportsCompleteEmptyCensus()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FactsTableFixture).FullName!, "--library", TestAssemblyPath,
            nameof(FactsTableFixture.BoxInt), "-S", "Fidelity Causes", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Complete", output);
        Assert.Contains("decompiler fidelity is Full", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectFidelityCauses_EmptyBodyIsComplete()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FidelityCauseFixture).FullName!, "--library", TestAssemblyPath,
            nameof(FidelityCauseFixture.EmptyBody),
            "-S", "Decompiled Source,Fidelity Causes,Annotated Source,Cost Overlay,Semantics Overlay",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Decompiled Source", output);
        Assert.Contains("## Annotated Source", output);
        Assert.Contains("## Cost Overlay", output);
        Assert.Contains("## Semantics Overlay", output);
        Assert.Contains("public static void EmptyBody()", output);
        Assert.Contains("Complete", output);
        Assert.Contains("decompiler fidelity is Full", output);
        Assert.DoesNotContain(ILInspector.Decompiler.DiagnosticIds.EmptyOutput, output);
        Assert.DoesNotContain("Failed", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectFidelityCauses_ImporterCrashIsFailed()
    {
        var assemblyPath = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-failed-{Guid.NewGuid():N}.dll");
        try
        {
            WriteFidelityFailureAssembly(assemblyPath);

            var (exit, output, error) = await RunAppAsync(
                "member", "FidelityFailedFixture.Malformed", "InvalidCall",
                "--library", assemblyPath,
                "-S", "Fidelity Causes", "--table", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("Failed", output);
            Assert.Contains(ILInspector.Decompiler.DiagnosticIds.InternalError, output);
            Assert.Contains("importer crash", output);
            Assert.DoesNotContain("Absent", output);
        }
        finally
        {
            File.Delete(assemblyPath);
        }
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectFidelityCauses_ReportsAbsentBody()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(SamplePInvokeClass).FullName!, "--library", TestAssemblyPath,
            nameof(SamplePInvokeClass.GetCurrentProcessId), "--all",
            "-S", "Fidelity Causes", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Absent", output);
        Assert.Contains("no decompiler IR body", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectFidelityCauses_RendersTypedCause()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FidelityCauseFixture).FullName!, "--library", TestAssemblyPath,
            nameof(FidelityCauseFixture.TypedReferenceType),
            "-S", "Fidelity Causes", "--tsv", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("state\tcode\tlocation\tnode_kind\tnode\tdiscriminator\treason", output);
        Assert.Contains("Complete", output);
        Assert.Contains("DEC0004", output);
        Assert.Matches(@"IL_[0-9A-F]{4}", output);
        Assert.Contains("mkrefany", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_DiscoverSchema_ListsFidelityCauses()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FidelityCauseFixture).FullName!, "--library", TestAssemblyPath,
            nameof(FidelityCauseFixture.EmptyBody), "-D", "--schema");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("| Fidelity Causes | section (opt-in) |", output);
    }

    [Fact]
    public async Task Member_FidelityCauses_IsExplicitOnly_NotShownAtDetailed()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FactsTableFixture).FullName!, "--library", TestAssemblyPath,
            nameof(FactsTableFixture.BoxInt), "-v:d", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("Fidelity Causes", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectAppliedTaste_DefaultRenderReportsNoChoices()
    {
        // The byte-divergent style lenses are opt-in (off by shipped default), so a
        // default member render applies no configurable style choice: the explicitly
        // selected Applied Taste section renders its empty-state note, not a row.
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FactsTableFixture).FullName!, "--library", TestAssemblyPath,
            nameof(FactsTableFixture.BoxInt), "-S", "Applied Taste", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Applied Taste", output);
        Assert.Contains("No recorded style choices were applied to this member.", output);
        Assert.DoesNotContain("byte-divergent", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_DiscoverSchema_ListsAppliedTaste()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FactsTableFixture).FullName!, "--library", TestAssemblyPath,
            nameof(FactsTableFixture.BoxInt), "-D", "--schema");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("| Applied Taste | section (opt-in) |", output);
    }

    [Fact]
    public async Task Member_AppliedTaste_IsExplicitOnly_NotShownAtDetailed()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FactsTableFixture).FullName!, "--library", TestAssemblyPath,
            nameof(FactsTableFixture.BoxInt), "-v:d", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("Applied Taste", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectFacts_IncludesResearchHeaderFacts()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FactsHeaderFixture).FullName!, "--library", TestAssemblyPath,
            nameof(FactsHeaderFixture.Hot), "--all", "-S", "Facts", "--tsv", "--no-headers", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("FactsHeaderFixture::Hot\t\t\tmember-header\tCost\tcost.method\t", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectFacts_IncludesExplicitSemanticsFacts()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!, "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.CallsExceptionOnly), "--index", "1", "--all", "-S", "Facts", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("semantics.callee", output);
        Assert.Contains("may-throw FormatException", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectCostOverlay_RendersExplicitCostFacts()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!, "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.Caller), "--index", "1", "--all", "-S", "Cost Overlay", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Cost Overlay", output);
        Assert.Contains("cost.callee", output);
        Assert.Contains("alloc-loop", output);
        Assert.DoesNotContain("cost.method(root-reach 1", output);
    }

    [Fact]
    public async Task Member_CostOverlay_IsExplicitOnly_NotShownAtDetailed()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!, "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.Caller), "--index", "1", "--all", "-v:d", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("## Cost Overlay", output);
        Assert.DoesNotContain("cost.callee", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_CostOverlay_BareRendersPayload()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!, "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.Caller), "--index", "1", "--all", "-S", "Cost Overlay", "--bare", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("## Cost Overlay", output);
        Assert.Contains("cost.callee", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_DiscoveryListsCostOverlayAsOptIn()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!, "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.Caller), "--index", "1", "--all", "-D", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Cost Overlay        section (opt-in)", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectSemanticsOverlay_RendersExplicitFacts()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!, "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.CallsExceptionOnly), "--index", "1", "--all", "-S", "Semantics Overlay", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Semantics Overlay", output);
        Assert.Contains("semantics.callee", output);
        Assert.Contains("may-throw FormatException", output);
        Assert.DoesNotContain("cost.callee", output);
    }

    [Fact]
    public async Task Member_SemanticsOverlay_IsExplicitOnly_NotShownAtDetailed()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!, "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.CallsExceptionOnly), "--index", "1", "--all", "-v:d", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("## Semantics Overlay", output);
        Assert.DoesNotContain("semantics.callee", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SemanticsOverlay_BareRendersPayload()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!, "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.CallsStackalloc), "--index", "1", "--all", "-S", "Semantics Overlay", "--bare", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("## Semantics Overlay", output);
        Assert.Contains("safety.callee", output);
        Assert.Contains("stackalloc", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_DiscoveryListsSemanticsOverlayAsOptIn()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!, "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.CallsExceptionOnly), "--index", "1", "--all", "-D", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Semantics Overlay   section (opt-in)", output);
    }

    [Fact]
    public async Task Type_Discovery_DoesNotListCostOverlay()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", typeof(CostOverlayFixture).FullName!, "--library", TestAssemblyPath, "-D", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("Cost Overlay", output);
        Assert.DoesNotContain("Semantics Overlay", output);
    }

    [Fact]
    public async Task Member_Facts_IsExplicitOnly_NotShownAtDetailed()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToElement" },
            OverloadIndex = 1,
            Verbosity = Verbosity.Detailed
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // Facts is opt-in: the inline Annotated Source view shows the same facts
        // for humans, so the structured table never auto-renders, even at -v:d.
        Assert.DoesNotContain("## Facts", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_NormalShowsLocalImplementationSections()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToNode" },
            OverloadIndex = 1,
            Verbosity = Verbosity.Normal
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Signature", output);
        Assert.Contains("## Custom Attributes", output);
        Assert.Contains("## Decompiled Source", output);
        Assert.Contains("## IL", output);
        Assert.DoesNotContain("## Annotated Source", output);
        Assert.DoesNotContain("## Original Source", output);
        // WriteNode<TValue>'s first parameter is `in TValue`; the call site
        // passes it without a keyword (an explicit `ref` here is CS1615). The
        // operand renders bare, not as the old `ref value` spelling.
        Assert.Contains("WriteNode<TValue>(value", output);
        Assert.DoesNotContain("WriteNode<TValue>(ref value", output);
        Assert.DoesNotContain("ref ref", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectDecompiledSource_RendersLoweredCSharp()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToElement" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Decompiled Source" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Decompiled Source", output);
        Assert.Contains("GetTypeInfo", output);
        Assert.Contains("WriteElement", output);
        Assert.DoesNotContain("## Original Source", output);
        Assert.Contains("public static System.Text.Json.JsonElement SerializeToElement<TValue>(TValue value, System.Text.Json.JsonSerializerOptions? options = null)", output);
    }

    [Fact]
    public async Task Member_SelectDecompiledSource_UsesExpressionBodiedSyntaxForTableReturn()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath,
            "CallsInterfaceItem", "-S", "Decompiled Source", "--bare");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("public static int CallsInterfaceItem(System.Collections.Generic.IList<int> values) => values[0];", output);
        Assert.DoesNotContain("{", output);
    }

    [Fact]
    public async Task Member_SelectedOperator_SelectDecompiledSource_RendersCSharpOperatorDeclaration()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Private.CoreLib",
            TypeName = "String",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "op_Equality" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Decompiled Source" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("public static bool operator ==(string? a, string? b)", output);
        Assert.DoesNotContain("public static bool op_Equality", output);
    }

    [Fact]
    public async Task Member_SelectedCheckedOperator_SelectDecompiledSource_RendersCSharpCheckedOperatorDeclaration()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Private.CoreLib",
            TypeName = "Int128",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "op_CheckedAddition" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Decompiled Source" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("public static System.Int128 operator checked +(System.Int128 left, System.Int128 right)", output);
        Assert.DoesNotContain("System.Int128 checked operator +", output);
    }

    [Fact]
    public async Task Member_SelectedCheckedConversion_SelectDecompiledSource_RendersCSharpCheckedConversionDeclaration()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Private.CoreLib",
            TypeName = "Int128",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "op_CheckedExplicit" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Decompiled Source" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("public static explicit operator checked System.Int128(double value)", output);
        Assert.DoesNotContain("checked explicit operator System.Int128", output);
    }

    [Fact]
    public async Task Member_SelectedExtensionMethod_SelectDecompiledSource_RendersThisParameter()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Linq",
            TypeName = "Enumerable",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Where" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Decompiled Source" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("public static System.Collections.Generic.IEnumerable<TSource> Where<TSource>(this System.Collections.Generic.IEnumerable<TSource> source, System.Func<TSource, bool> predicate)", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectDecompiledSource_UsesDisplayedOverloadIndex()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Private.CoreLib",
            TypeName = "Enum",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Parse" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Decompiled Source" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("public static TEnum Parse<TEnum>(System.ReadOnlySpan<char> value)", output);
        Assert.DoesNotContain("public static object Parse(System.Type enumType, string value)", output);
    }

    [Fact]
    public async Task Member_SelectedGenericTypeConstructor_SelectDecompiledSource_RendersUngenericConstructorName()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Private.CoreLib",
            TypeName = "List<T>",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".ctor" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Decompiled Source" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("public List()", output);
        Assert.DoesNotContain("public List<T>()", output);
    }

    [Fact]
    public async Task Member_TsvWithMultipleSelectedSections_ReturnsError()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Methods", "Properties"],
            Tabular = true,
            Tsv = true,
            TabularExplicitlySet = true
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Contains("Selection matches 2 sections", error);
        Assert.Contains("--table, --tsv, and --jsonl display one section at a time", error);
    }

    [Fact]
    public async Task Member_NarrowedMethods_TsvProjectsOverloadRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            "-m", "Serialize", "-S", "Member Index",
            "--columns", "Stable;Canonical Signature", "--tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("stable\tcanonical_signature", output);
        Assert.Contains("Serialize~", output);
        Assert.Contains("M:System.Text.Json.JsonSerializer.Serialize<TValue>", output);
        Assert.DoesNotContain('`', output);
        Assert.DoesNotContain("return_type", output);
        Assert.DoesNotContain("overloads", output);
    }

    [Fact]
    public async Task Member_NarrowedMethods_TableRendersOverloadRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            "-m", "Serialize", "-S", "Member Index", "--table");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Stable", output);
        Assert.Contains("Canonical Signature", output);
        Assert.Contains("Serialize:1", output);
        Assert.Contains("Serialize~", output);
        Assert.DoesNotContain('`', output);
        Assert.DoesNotContain("Return Type", output);
        Assert.DoesNotContain("Overloads", output);
    }

    [Fact]
    public async Task Member_NarrowedMethods_JsonlProjectsOverloadRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            "-m", "Serialize", "-S", "Member Index",
            "--columns", "Stable;Canonical Signature", "--jsonl");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(lines);

        using var first = JsonDocument.Parse(lines[0]);
        Assert.True(first.RootElement.TryGetProperty("stable", out var selector));
        Assert.True(first.RootElement.TryGetProperty("canonical_signature", out var signature));
        Assert.Contains("M:System.Text.Json.JsonSerializer.Serialize", signature.GetString());
        Assert.StartsWith("Serialize~", selector.GetString());
        Assert.DoesNotContain('`', output);
        Assert.DoesNotContain("return_type", output);
        Assert.DoesNotContain("overloads", output);
    }

    [Fact]
    public async Task Member_NarrowedMethods_StableSelectorRoundTripsToSignature()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            "-m", "Serialize", "-S", "Member Index",
            "--columns", "Stable", "--tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var stableSelector = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .First();
        Assert.StartsWith("Serialize~", stableSelector);

        (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            stableSelector, "-S", "Signature", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Signature", output);
        Assert.Contains("public static string Serialize", output);
    }

    [Fact]
    public async Task Member_NarrowedMethods_UnknownProjectedColumnWarnsWithDiscoveryHint()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            "-m", "Serialize", "-S", "Member Index",
            "--columns", "Stable;Canonical Signature;Obsolete", "--tsv");

        Assert.Equal(0, exit);
        Assert.StartsWith("stable\tcanonical_signature", output);
        Assert.Contains("warning: column 'Obsolete' not found in section 'Member Index'", error);
        Assert.Contains("Run -D \"Member Index\" to list available columns.", error);
    }

    [Fact]
    public async Task Member_NarrowedMethods_OptionGatedProjectedColumnWarnsWithDiscoveryHint()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            "-m", "Serialize",
            "--columns", "Select;Signature", "--tsv");

        Assert.Equal(0, exit);
        Assert.StartsWith("signature", output);
        Assert.Contains("warning: column 'Select' not found in section 'Methods'", error);
        Assert.Contains("Run -D \"Methods\" to list available columns.", error);
    }

    [Fact]
    public async Task Type_DecompiledSource_RendersWholeTypeListing()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Collections.Generic.Stack", "--platform", "System.Collections",
            "-S", "Decompiled Source");

        Assert.Equal(0, exit);
        Assert.Contains("namespace System.Collections.Generic;", output);
        Assert.Contains("public class Stack<T>", output);
        Assert.Contains("private T[] _array;", output);
        Assert.Contains("public void Push(T item)", output);
        Assert.Contains("public bool TryPop([System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out T result)", output);
        // Using hoisting: qualified names shorten against the metadata
        // namespace tables; the directives appear at the top.
        Assert.Contains("using System.Runtime.CompilerServices;", output);
        Assert.Contains(": IEnumerable<T>, IEnumerable, ICollection, IReadOnlyCollection<T>", output);
        Assert.Contains("RuntimeHelpers.IsReferenceOrContainsReferences", output);
        Assert.DoesNotContain("System.Collections.Generic.IEnumerable<T>", output);
        // Explicit interface property implementations render as properties,
        // not their accessor methods.
        Assert.Contains("bool ICollection.IsSynchronized => false;", output);
        Assert.DoesNotContain("get_IsSynchronized", output);
    }

    [Fact]
    public async Task Type_GenericInstantiation_PreservesNestedTypeSuffix()
    {
        // #1154: an instantiated nested type (Dictionary`2.Enumerator) must keep
        // its nested segment instead of collapsing to Dictionary<TKey, TValue>.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Collections.Generic.Dictionary`2", "--platform", "System.Private.CoreLib",
            "-S", "Methods");

        Assert.Equal(0, exit);
        Assert.Contains("Dictionary<TKey, TValue>.Enumerator GetEnumerator()", output);
        Assert.DoesNotContain("Dictionary<TKey, TValue> GetEnumerator()", output);
    }

    [Fact]
    public async Task Type_DecompiledSource_UsesExpressionBodiedSyntaxForTableMembers()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath,
            "-S", "Decompiled Source", "--bare");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("public static int CallsInterfaceItem(IList<int> values) => values[0];", output);
        Assert.Contains("    public static void CallsWriteLineTwice()\n    {", output.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task TypeListing_NestedTypes_ShowDeclaringTypeContext()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "--platform", "System.Collections", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("System.Collections.Generic.SortedDictionary<TKey, TValue>.KeyCollection", output);
        Assert.Contains("System.Collections.Generic.SortedDictionary<TKey, TValue>.ValueCollection", output);
        Assert.Contains("System.Collections.Generic.Stack<T>.Enumerator", output);
        Assert.DoesNotContain("class   KeyCollection", output);
        Assert.DoesNotContain("struct  Enumerator", output);
    }

    [Fact]
    public async Task TypeListing_NestedDelegate_ShowsFullDeclaringTypeContext()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text.Json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("`System.Text.Json.Serialization.Metadata.FSharpCoreReflectionProxy.StructGetter<TStruct, TResult>`", output);
        Assert.DoesNotContain("| `StructGetter<TStruct, TResult>` |", output);
    }

    [Fact]
    public async Task Type_DecompiledSource_Enum_RendersValuesListing()
    {
        // Enums have no method bodies; the listing renders the declaration
        // and values — following the ref assembly's type forwarder to the
        // defining assembly.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.DayOfWeek", "--platform", "System.Runtime",
            "-S", "Decompiled Source", "--bare");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("public enum DayOfWeek", output);
        Assert.Contains("Sunday = 0,", output);
        Assert.Contains("Saturday = 6,", output);
    }

    [Fact]
    public async Task Type_DecompiledSource_Bare_EmitsBareListing()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Collections.Generic.Stack", "--platform", "System.Collections",
            "-S", "Decompiled Source", "--bare");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        // Bare C#: no markdown heading, no section title, no code fence, no tips.
        Assert.StartsWith("using System.Collections;", output);
        Assert.Contains("namespace System.Collections.Generic;", output);
        Assert.DoesNotContain("# ", output);
        Assert.DoesNotContain("```", output);
        Assert.DoesNotContain("Tips:", output);
    }

    [Fact]
    public async Task Type_BareWithoutSection_Errors()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "String", "--platform", "System.Private.CoreLib", "--bare", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--bare requires exactly one -S section", error);
    }

    [Fact]
    public async Task Member_NonMethodRows_RenderFullDeclarations()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.IO.Stream", "--platform", "System.Runtime",
            "-m", "CanRead", "-S", "Properties",
            "--columns", "Name;Signature", "--tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("CanRead\tpublic abstract bool CanRead { get; }", output);

        (exit, output, error) = await RunAppAsync(
            "member", "System.String", "--platform", "System.Runtime",
            "-m", "Empty", "-S", "Fields",
            "--columns", "Name;Signature", "--tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Empty\tpublic static readonly string Empty", output);

        (exit, output, error) = await RunAppAsync(
            "member", "System.Math", "--platform", "System.Runtime",
            "-m", "DivRem", "-S", "Methods",
            "--columns", "Name;Signature", "--tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("DivRem\tpublic static", output);
        Assert.DoesNotContain("System.Runtime.Versioning.NonVersionable", output);

        (exit, output, error) = await RunAppAsync(
            "member", "System.AppDomain", "--platform", "System.Runtime",
            "-m", "AssemblyLoad", "-S", "Events",
            "--columns", "Name;Signature", "--tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("AssemblyLoad\tpublic event System.AssemblyLoadEventHandler? AssemblyLoad", output);

        (exit, output, error) = await RunAppAsync(
            "member", "System.String", "--platform", "System.Runtime",
            "-m", "Chars", "-S", "Properties",
            "--columns", "Name;Signature;Description", "--tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Chars\tpublic char this[int index] { get; }", output);
        Assert.Contains("Gets the Char object at a specified position in the current String object.", output);
    }

    [Fact]
    public async Task Member_OutParameter_RendersOutModifier()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Text.Json.JsonElement", "--platform", "System.Text.Json",
            "-m", "TryGetBytesFromBase64", "-S", "Methods",
            "--columns", "Name;Signature", "--tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("TryGetBytesFromBase64\tpublic bool TryGetBytesFromBase64([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? value)", output);
    }

    [Fact]
    public async Task Member_NarrowedMethods_DescriptionsMatchOverloads()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Text.Json.JsonSerializer", "--package", "System.Text.Json@10.0.0",
            "-m", "Serialize", "-S", "Methods",
            "--columns", "Signature;Description", "--tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("public static string Serialize<TValue>(TValue value, System.Text.Json.JsonSerializerOptions? options = null)\tConverts the value of a type specified by a generic type parameter into a JSON string.", output);
        Assert.Contains("public static void Serialize<TValue>(System.IO.Stream utf8Json, TValue value, System.Text.Json.JsonSerializerOptions? options = null)\tConverts the provided value to UTF-8 encoded JSON text and write it to the Stream.", output);
    }

    [Fact]
    public async Task Member_NarrowedMethods_DescriptionsPreserveGenericAndArrayOverloadShape()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.String", "--platform", "System.Runtime",
            "-m", "Join", "-S", "Methods",
            "--columns", "Signature;Description", "--tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("public static string Join(char separator, params System.ReadOnlySpan<object?> values)\tConcatenates the string representations of a span of objects", output);
        Assert.Contains("public static string Join(char separator, params System.ReadOnlySpan<string?> value)\tConcatenates a span of strings", output);
        Assert.Contains("public static string Join(char separator, string?[] value, int startIndex, int count)\tConcatenates an array of strings", output);
    }

    [Fact]
    public async Task Member_ObsoleteMethod_RendersObsoleteAttributeInlineInSignature()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "SampleObsoleteHost", "--library", TestAssemblyPath,
            "-m", "OldMethod", "-S", "Methods",
            "--columns", "Name;Signature", "--tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("OldMethod\t[Obsolete(\"Use NewMethod instead.\")] public void OldMethod()", output);
    }

    [Fact]
    public async Task Member_MixedKindFilter_TsvUsesUnifiedTableRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            "-m", "Serialize", "-m", "IsReflectionEnabledByDefault", "--tsv", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("kind\tname\treturn_type\tdetail", output);
        Assert.Contains("property\tIsReflectionEnabledByDefault\tbool\tget", output);
        Assert.Contains("method\tSerialize\tvoid\t15", output);
        Assert.DoesNotContain("\n\n", output);
    }

    [Fact]
    public async Task Member_SelectMethodsAndEvents_PreservesPipelineOrder()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "AppDomain", "--platform", "System.Private.CoreLib",
            "-S", "Methods,Events", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.True(
            output.IndexOf("## Methods", StringComparison.Ordinal)
            < output.IndexOf("## Events", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Member_StringBareSelect_RendersLearnMemberOrder()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib", "-S", "--tips", "q", "--rows", "3");

        Assert.Equal(0, exit);
        Assert.Empty(error);

        string[] headings =
        [
            "## Constructors",
            "## Fields",
            "## Properties",
            "## Method Groups",
            "## Operators",
            "## Explicit Interface Implementations",
            "## Extension Methods"
        ];

        var previous = -1;
        foreach (var heading in headings)
        {
            var current = output.IndexOf(heading, StringComparison.Ordinal);
            Assert.True(current > previous, $"{heading} was not after the previous heading.");
            previous = current;
        }
    }

    [Fact]
    public async Task Member_StringSelectSpecialMemberKinds_RendersRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "-S", "Operators,Explicit Interface Implementations,Extension Methods",
            "--tips", "q", "--rows", "3");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Operators", output);
        Assert.Contains("operator ==", output);
        Assert.Contains("## Explicit Interface Implementations", output);
        Assert.Contains("System.Collections.IEnumerable.GetEnumerator", output);
        Assert.Contains("## Extension Methods", output);
        Assert.Contains("AsMemory", output);
    }

    [Fact]
    public async Task Member_StringSupplementalSelectors_RoundTrip()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "-S", "Explicit Interface Implementations,Member Index", "--tips", "q", "--rows", "4");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("`explicit:System.IConvertible.ToBoolean`", output);
        Assert.Contains("explicit:System.IConvertible.ToBoolean~", output);

        (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "explicit:System.IConvertible.ToBoolean:1", "-S", "Signature", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("bool System.IConvertible.ToBoolean", output);

        (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "explicit:System.IConvertible.ToBoolean:1", "-S", "IL", "--tips", "q", "-n", "12");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## IL", output);
        Assert.Contains("System.Convert::ToBoolean", output);

        (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "explicit:System.IConvertible.ToBoolean:1", "-S", "Decompiled Source", "--tips", "q", "-n", "12");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("bool System.IConvertible.ToBoolean", output);
        Assert.DoesNotContain("public bool System.IConvertible.ToBoolean", output);

        (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "-S", "Extension Methods,Member Index", "--tips", "q", "--rows", "4");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("`extension:AsMemory:1`", output);
        Assert.Contains("extension:AsMemory~", output);

        (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "extension:AsMemory:1", "-S", "IL", "--tips", "q", "-n", "12");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## IL", output);
        Assert.Contains("IL_0000:", output);

        (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "extension:Normalize:1", "-S", "Signature", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("public static string Normalize(this string strInput)", output);
        Assert.DoesNotContain("string Normalize()", output);

        (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "extension:Normalize", "--json");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var doc = JsonDocument.Parse(output);
        foreach (var member in doc.RootElement.GetProperty("members").EnumerateArray())
            Assert.Equal("extension-method", member.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Type_StringShape_RendersLearnMemberOrder()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "String", "--platform", "System.Private.CoreLib", "--shape");

        Assert.Equal(0, exit);
        Assert.Empty(error);

        string[] headings =
        [
            "Constructors",
            "Fields",
            "Properties",
            "Methods",
            "Operators",
            "Explicit Interface Implementations",
            "Extension Methods"
        ];

        var previous = -1;
        foreach (var heading in headings)
        {
            var current = output.IndexOf($"─ {heading}", StringComparison.Ordinal);
            Assert.True(current > previous, $"{heading} was not after the previous heading.");
            previous = current;
        }
    }

    [Fact]
    public async Task Type_StaticClass_RendersStaticClassModifierOnly()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Math", "--shape", "--tips", "q", "-n", "1");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("static class System.Math", output, StringComparison.Ordinal);
        Assert.DoesNotContain("static abstract sealed class", output);
    }

    [Fact]
    public async Task Find_Count_WithNamedPlatformLibrary_RendersOnlyCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "JsonSerializer", "--platform", "System.Text.Json", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public async Task Find_Count_WithNamedPlatformLibraryBeforeTarget_RendersOnlyCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "--platform", "System.Text.Json", "JsonSerializer", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public async Task Find_Count_WithNamedPlatformLibraryBeforeOptionAndTarget_RendersOnlyCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "--platform", "System.Text.Json", "--count", "JsonSerializer");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public async Task Find_Count_WithNamedPlatformLibraryAfterOptionAndTarget_RendersOnlyCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "--tfm", "net10.0", "JsonSerializer", "--platform", "System.Text.Json", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public async Task Find_Count_WithRootOptionBeforeCommandAndNamedPlatformLibrary_RendersOnlyCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "-v:q", "find", "JsonSerializer", "--platform", "System.Text.Json", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public async Task Find_Count_ComposesBareAndNamedPlatformScopes()
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "JsonSerializer", "--platform", "--platform", "System.Linq", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public async Task RelationshipCommands_Count_RendersOnlyCount()
    {
        var (implementsExit, implementsOutput, implementsError) = await RunAppAsync(
            "implements", "IDisposable", "--platform", "--count");
        var (extensionsExit, extensionsOutput, extensionsError) = await RunAppAsync(
            "extensions", "IEnumerable<T>", "--platform", "--count");
        var (dependsExit, dependsOutput, dependsError) = await RunAppAsync(
            "depends", "System.Int128", "--count");

        Assert.Equal(0, implementsExit);
        Assert.Empty(implementsError);
        Assert.True(int.Parse(implementsOutput.Trim()) > 0);

        Assert.Equal(0, extensionsExit);
        Assert.Empty(extensionsError);
        Assert.True(int.Parse(extensionsOutput.Trim()) > 0);

        Assert.Equal(0, dependsExit);
        Assert.Empty(dependsError);
        Assert.True(int.Parse(dependsOutput.Trim()) > 0);
    }

    [Fact]
    public async Task Depends_Count_WithEmptyDependencyTree_RendersZero()
    {
        var (exit, output, error) = await RunAppAsync(
            "depends", "System.Object", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("0", output.Trim());
    }

    [Fact]
    public async Task Depends_Count_WithEmptyLibraryDependencyTree_RendersZero()
    {
        var (exit, output, error) = await RunAppAsync(
            "depends", "--library", "System.Private.CoreLib", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("0", output.Trim());
    }

    [Fact]
    public async Task RelationshipCommands_BarePlatformBeforeTarget_RendersOnlyCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "extensions", "--platform", "IEnumerable<T>", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.True(int.Parse(output.Trim()) > 0);
    }

    [Fact]
    public async Task RelationshipCommands_NamedPlatformLibrary_IsAccepted()
    {
        var (exit, output, error) = await RunAppAsync(
            "extensions", "IEnumerable<T>", "--platform", "System.Linq", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Matches(@"^\d+$", output.Trim());
    }

    [Fact]
    public async Task RelationshipCommands_NamedPlatformLibrary_FormatsSourceAsFrameworkAtVersion()
    {
        var (exit, output, error) = await RunAppAsync(
            "extensions", "IEnumerable<T>", "--platform", "System.Linq", "-v:n", "--tips", "q", "-n", "12");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("runtime@", output);
        Assert.DoesNotContain("@runtime", output);
    }

    [Fact]
    public async Task Extensions_FailedAssemblyWarnsAndPreservesSuccessfulResults()
    {
        var corruptPath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-corrupt-{Guid.NewGuid():N}.dll");
        try
        {
            await File.WriteAllTextAsync(
                corruptPath,
                "not a PE image",
                TestContext.Current.CancellationToken);

            var (exit, output, error) = await RunAppAsync(
                "extensions",
                "MetadataReader",
                "--library", typeof(MetadataFindings).Assembly.Location,
                "--library", corruptPath,
                "--count",
                "--tips", "q");

            Assert.Equal(0, exit);
            Assert.True(int.Parse(output.Trim()) > 0);
            Assert.Contains(
                "Warning: Extension member inspection failed",
                error,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(corruptPath);
        }
    }

    [Fact]
    public async Task Extensions_JsonlAfterPackage_RendersJsonlAndDoesNotWarnAboutPackageFlag()
    {
        var (exit, output, error) = await RunAppAsync(
            "extensions", "IEnumerable<T>", "--platform", "System.Linq", "--jsonl", "--rows", "2", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("{", output.TrimStart());
        Assert.DoesNotContain("# Extension Methods", output);
    }

    [Fact]
    public async Task Router_BareHelp_ExplainsPackageRouting()
    {
        var (exit, output, error) = await RunAppAsync("frobnicate", "--help");

        Assert.Equal(0, exit);
        Assert.Contains("Inspect a NuGet package", output);
        Assert.Contains("interpreting bare token 'frobnicate' as a package or platform target", error);
        Assert.Contains("dotnet-inspect --help", error);
    }

    [Fact]
    public async Task Router_HelpBeforeBareToken_StillExplainsPackageRouting()
    {
        var (exit, output, error) = await RunAppAsync("--help", "frobnicate");

        Assert.Equal(0, exit);
        Assert.Contains("Inspect a NuGet package", output);
        Assert.DoesNotContain("Auto-route bare input", output);
        Assert.Contains("interpreting bare token 'frobnicate' as a package or platform target", error);
    }

    [Fact]
    public async Task Router_BareDiscoveryHelp_DoesNotCallOptionABareToken()
    {
        var (exit, output, error) = await RunAppAsync("-S", "Methods", "--help");

        Assert.Equal(0, exit);
        Assert.Contains("Discover types in a package or library", output);
        Assert.DoesNotContain("interpreting bare token '-S'", error);
    }

    [Fact]
    public async Task Router_OutputFlagBeforeBareToken_KeepsBareTokenAsRouteTarget()
    {
        var (exit, output, error) = await RunAppAsync("--json", "frobnicate");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Package 'frobnicate' not found", error);
        Assert.DoesNotContain("Package '--json' not found", error);
    }

    [Fact]
    public async Task Router_ValueOptionBeforeBareToken_SkipsOptionValueWhenFindingRouteTarget()
    {
        var (exit, output, error) = await RunAppAsync("--type", "Widget", "frobnicate");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Package 'frobnicate' not found", error);
        Assert.DoesNotContain("Package 'Widget' not found", error);
    }

    [Fact]
    public async Task Router_MemberOptionBeforeBareToken_SkipsMemberValueWhenFindingRouteTarget()
    {
        var (exit, output, error) = await RunAppAsync("--member", "Keep", "frobnicate");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.DoesNotContain("Package 'Keep' not found", error);
        Assert.DoesNotContain("Unrecognized option '--member'", error);
    }

    [Fact]
    public async Task Type_BareStringAlias_RendersCoreLibString()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "string", "--shape", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("System.String", output);
        Assert.Contains("─ Methods", output);
    }

    [Theory]
    [InlineData("Dictionary<TKey,TValue>")]
    [InlineData("Dictionary`2")]
    public async Task Type_BareDictionaryGeneric_RendersCoreLibDictionary(string typeName)
    {
        var (exit, output, error) = await RunAppAsync(
            "type", typeName, "--shape", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("System.Collections.Generic.Dictionary<TKey, TValue>", output);
        Assert.Contains("void Add(TKey key, TValue value)", output);
    }

    [Fact]
    public async Task Member_BareStringAliasWithMemberFilter_RendersStringMembers()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "string", "-m", "Normalize", "-S", "Methods", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("# System.String", output);
        Assert.Contains("## Methods", output);
        Assert.Contains("Normalize(", output);
    }

    [Fact]
    public async Task Member_EnumValueFilter_TsvAppliesMemberFilter()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "DayOfWeek", "--platform", "System.Private.CoreLib",
            "-m", "Friday", "--tsv", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("name\tvalue", output);
        Assert.Contains("Friday\t5", output);
        Assert.DoesNotContain("Sunday", output);
        Assert.DoesNotContain("Saturday", output);
    }

    [Fact]
    public async Task Library_TsvWithMultipleSelectedSections_ReturnsError()
    {
        var options = new LibraryOptions
        {
            PlatformAssembly = "System.Text.Json",
            Select = ["Library Info", "Signals"],
            Tabular = true,
            Tsv = true,
            TabularExplicitlySet = true
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Contains("Selection matches 2 sections", error);
        Assert.Contains("--table, --tsv, and --jsonl display one section at a time", error);
    }

    [Fact]
    public async Task Type_SelectWithUnknownColumn_ReturnsError()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Properties"],
            Columns = ["Bogus"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Contains("column 'Bogus' not found in section 'Properties'", error);
        Assert.Contains("No columns matched projection: Bogus", error);
    }

    [Fact]
    public async Task Type_SelectWithSelectColumn_ReturnsErrorWhenNotRendered()
    {
        // Select is a historical schema column, but the active table shape has no matching
        // column, so strict projection returns an error.
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Properties"],
            Columns = ["Select"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Contains("No columns matched projection: Select", error);
    }

    [Fact]
    public async Task Type_SelectWithColumnNotShownAtVerbosity_ReturnsError()
    {
        // Signature is valid in the static schema but does not render at the default
        // verbosity. The active table shape has no matching column, so strict projection
        // returns an error.
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Properties"],
            Columns = ["Signature"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Contains("No columns matched projection: Signature", error);
    }

    [Fact]
    public async Task Type_SelectWithValidColumn_NoWarning()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Properties"],
            Columns = ["Name"]
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Name |", output);
        Assert.DoesNotContain("not found", error);
        Assert.DoesNotContain("no data", error);
    }

    [Fact]
    public async Task Type_DiscoverSection_Effective_DropsSelectColumn()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Properties"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // Effective discovery reports only columns that actually render. The historical
        // Select column must not appear.
        Assert.DoesNotContain("| Select | column |", output);
        Assert.Contains("| Name | column |", output);
    }

    [Fact]
    public async Task Type_DiscoverSection_Effective_ReflectsVerbosityColumns()
    {
        // At default (Minimal) verbosity, Properties renders the summary row (Return Type/Accessors).
        var minimal = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Properties"]
        };
        var (exitMin, minOutput, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(minimal));

        Assert.Equal(0, exitMin);
        Assert.Contains("| Return Type | column |", minOutput);
        Assert.DoesNotContain("| Signature | column |", minOutput);

        // At Detailed verbosity, Properties renders the full member row (Signature, no Return Type).
        var detailed = minimal with { Verbosity = Verbosity.Detailed };
        var (exitDet, detOutput, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(detailed));

        Assert.Equal(0, exitDet);
        Assert.Contains("| Signature | column |", detOutput);
        Assert.DoesNotContain("| Return Type | column |", detOutput);
    }

    [Fact]
    public async Task Api_NonexistentPackage_ShowsError()
    {
        var options = new ApiOptions { PackagePath = "NonexistentPackage123456" };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => ApiCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task Api_LocalAssembly_ListsTypes()
    {
        var options = new ApiOptions { AssemblyPath = TestAssemblyPath };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => ApiCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("CommandExecutionTests", output);
    }

    // ── find command ─────────────────────────────────────────────────

    [Fact]
    public async Task Find_PlatformLibrary_FindsType()
    {
        var options = new FindOptions
        {
            Pattern = "JsonSerializer",
            PlatformAssemblies = ["System.Text.Json"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
    }

    [Fact]
    public async Task Find_Members_ExplicitFlag_RendersMembersSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "Serialize", "--members", "--platform", "System.Text.Json");

        Assert.Equal(0, exit);
        Assert.Contains("## Members", output);
        Assert.Contains("Find member: Serialize", output);
        Assert.Contains("System.Text.Json.JsonSerializer", output);
    }

    [Fact]
    public async Task Find_Members_LeadingDotShortcut_MatchesExplicitFlag()
    {
        var (dotExit, dotOutput, _) = await RunAppAsync(
            "find", ".Serialize", "--platform", "System.Text.Json");
        var (flagExit, flagOutput, _) = await RunAppAsync(
            "find", "Serialize", "--members", "--platform", "System.Text.Json");

        Assert.Equal(0, dotExit);
        Assert.Equal(0, flagExit);
        // The leading-dot sentinel enables the member lens and strips the dot, so both spellings
        // produce identical output.
        Assert.Equal(flagOutput, dotOutput);
    }

    [Fact]
    public async Task Find_Members_Count_EmitsPositiveCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "find", ".Serialize", "--platform", "System.Text.Json", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.True(int.TryParse(output.Trim(), out var count));
        Assert.True(count >= 1, $"expected at least one member, got {count}");
    }

    [Fact]
    public async Task Find_Members_Json_EmitsMemberFields()
    {
        var (exit, output, _) = await RunAppAsync(
            "find", ".Serialize", "--platform", "System.Text.Json", "--json");

        Assert.Equal(0, exit);
        Assert.Contains("\"member\":\"Serialize\"", output);
        Assert.Contains("\"declaring_type\":\"System.Text.Json.JsonSerializer\"", output);
    }

    [Fact]
    public async Task Find_Members_NoMatches_ReportsNoMembers()
    {
        var (exit, _, error) = await RunAppAsync(
            "find", ".ZzzNoSuchMemberName", "--platform", "System.Text.Json");

        Assert.Equal(0, exit);
        Assert.Contains("No members found", error);
    }

    [Fact]
    public async Task Find_Members_LeadingDotCtor_FindsConstructors()
    {
        // ".ctor"/".cctor" are the only real metadata member names beginning with a dot; the
        // leading-dot sentinel must preserve an exact (case-insensitive) match for them (not strip
        // to a non-matching "ctor"), while any leading-dot glob is treated purely as the member-lens
        // sentinel and stripped — so ".c*" searches members named c* rather than only constructors.
        var ctor = await RunAppAsync("find", ".ctor", "--platform", "System.Text.Json", "--count");
        var upper = await RunAppAsync("find", ".CTOR", "--platform", "System.Text.Json", "--count");
        var dotGlob = await RunAppAsync("find", ".c*", "--platform", "System.Text.Json", "--count");
        var memberGlob = await RunAppAsync("find", "c*", "--members", "--platform", "System.Text.Json", "--count");

        Assert.Equal(0, ctor.Item1);
        Assert.Empty(ctor.Item3);
        Assert.True(int.TryParse(ctor.Item2.Trim(), out var count));
        Assert.True(count >= 1, $"expected at least one constructor, got {count}");

        // Exact constructor preservation is case-insensitive.
        Assert.Equal(ctor.Item2.Trim(), upper.Item2.Trim());

        // A leading-dot glob is a member-lens shortcut, not a constructor-only query: ".c*" must
        // resolve to the same set as the explicit "c*" member search, not collapse to constructors.
        Assert.Equal(memberGlob.Item2.Trim(), dotGlob.Item2.Trim());
        Assert.True(int.TryParse(dotGlob.Item2.Trim(), out var dotGlobCount));
        Assert.True(dotGlobCount > count, $"expected .c* ({dotGlobCount}) to exceed constructors ({count})");
    }

    [Fact]
    public async Task Member_PackageLibrarySelector_ResolvesBareLibraryName()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime", "System.Text.RegularExpressions");
        try
        {
            var options = new MemberOptions
            {
                TypeName = "RegexOptions",
                PackagePath = packagePath,
                AssemblyPath = "System.Text.RegularExpressions"
            };

            var (exit, output, _) = await ConsoleCapture.RunAsync(
                () => MemberCommand.ExecuteAsync(options));

            Assert.Equal(0, exit);
            Assert.Contains("RegexOptions", output);
            Assert.Contains("None", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Member_PackageTypeResolution_SearchesAcrossPackageLibraries()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime", "System.Text.RegularExpressions");
        try
        {
            var options = new MemberOptions
            {
                TypeName = "RegexOptions",
                PackagePath = packagePath,
                Verbosity = Verbosity.Minimal
            };

            var (exit, output, _) = await ConsoleCapture.RunAsync(
                () => MemberCommand.ExecuteAsync(options));

            Assert.Equal(0, exit);
            Assert.Contains("RegexOptions", output);
            Assert.Contains("Compiled", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Library_ToolPointerPackage_ResolvesAnyPayloadAssembly()
    {
        var (packagePath, _, tempDir) = CreateLocalToolPackageSet();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library", "Test.Tool.dll", "--package", packagePath, "-S", "Library Info");

            Assert.Equal(0, exit);
            Assert.Contains("# Test.Tool.dll", output);
            Assert.Contains("| Name | dotnet-inspect.Tests |", output);
            Assert.DoesNotContain("No DLLs found", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Library_ToolRidPackage_ResolvesSiblingAnyPayloadAssembly()
    {
        var (_, packagePath, tempDir) = CreateLocalToolPackageSet();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library", "Test.Tool.dll", "--package", packagePath, "-S", "Library Info");

            Assert.Equal(0, exit);
            Assert.Contains("# Test.Tool.dll", output);
            Assert.Contains("| Name | dotnet-inspect.Tests |", output);
            Assert.DoesNotContain("No DLLs found", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Find_NoPattern_ShowsError()
    {
        var options = new FindOptions
        {
            Pattern = "",
            PlatformAssemblies = ["System.Text.Json"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Contains("No pattern", error);
    }

    // ── library command ─────────────────────────────────────────────

    [Fact]
    public async Task Assembly_PlatformLibrary_ShowsInfo()
    {
        var options = new LibraryOptions { PlatformAssembly = "System.Text.Json" };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("System.Text.Json", output);
    }

    [Fact]
    public async Task Assembly_SingleSectionCount_WritesInteger()
    {
        var options = new LibraryOptions
        {
            PlatformAssembly = "System.Text.Json",
            Select = ["Async*"],
            Count = true
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.True(int.TryParse(output.Trim(), out var count), output);
        Assert.True(count > 0);
        Assert.DoesNotContain("#", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task Assembly_CountWithoutSingleSection_Errors()
    {
        var options = new LibraryOptions
        {
            PlatformAssembly = "System.Text.Json",
            Count = true
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(CountOutput.SectionRequiredMessage, error);
    }

    [Fact]
    public async Task Type_SingleSectionCount_WritesInteger()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Methods"],
            Count = true
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.True(int.TryParse(output.Trim(), out var count), output);
        Assert.True(count > 0);
        Assert.DoesNotContain("#", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task Assembly_LocalAssembly_ShowsInfo()
    {
        var options = new LibraryOptions { AssemblyName = TestAssemblyPath };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("dotnet-inspect.Tests", output);
    }

    [Fact]
    public async Task Assembly_Signals_ShowsMetadataSignalsOnly()
    {
        var options = new LibraryOptions
        {
            PlatformAssembly = "System.Text.Json",
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Signals" }
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Signals", output);
        Assert.Contains("IsTrimmable", output);
        Assert.Contains("IsAotCompatible", output);
        Assert.Contains("Direct assembly references", output);
        Assert.DoesNotContain("Public key token", output);
        Assert.DoesNotContain("| Dependencies | Direct assembly references | 0 |", output);
        Assert.DoesNotContain("| Signals | Scope |", output);
        Assert.DoesNotContain("## Library Info", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task Assembly_SignalsSectionSelection_PopulatesReferenceSignals()
    {
        var options = new LibraryOptions
        {
            PlatformAssembly = "System.Text.Json",
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Signals" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("Direct assembly references", output);
        Assert.DoesNotContain("| Dependencies | Direct assembly references | 0 |", output);
    }

    [Fact]
    public async Task NoArguments_Commands_ReportMissingInputConsistently()
    {
        // Issue #1690: every command reports a missing required argument the Unix way —
        // a concise error on stderr with a non-zero exit, not full help with exit 0.
        foreach (var command in new[] { "type", "member", "find", "depends", "extensions", "implements" })
        {
            var (exit, output, error) = await RunAppAsync(command, "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("Error:", error);
            Assert.Contains($"dotnet-inspect {command} --help", error);
        }
    }

    [Fact]
    public async Task LibraryCommand_NonexistentLibraryPath_ReportsFileNotFound()
    {
        // Regression for issue #1690: a missing local library path must report a file error,
        // not be misclassified as a NuGet package.
        var (exit, output, error) = await RunAppAsync("library", "./does-not-exist/MyLib.dll");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("File not found: ./does-not-exist/MyLib.dll", error);
        Assert.DoesNotContain("Package", error);
    }

    [Fact]
    public async Task LibraryCommand_BareSelect_RendersFixedOverview()
    {
        var (exit, output, _) = await RunAppAsync("library", "System.Text.Json", "-S");

        Assert.Equal(0, exit);
        // Bare -S is the network-free FIXED overview: only the structurally-fixed, network-free
        // fact tables, whose membership is package-independent (Library Info, Signals, Symbols).
        // Signals/Symbols are symbol-dependent but read an embedded/adjacent/cached PDB with no
        // network access, so they belong to the fixed overview.
        Assert.Contains("## Library Info", output);
        Assert.Contains("## Signals", output);
        Assert.Contains("## Symbols", output);
        // Package-growing sections (Terse/Informative) are deliberately excluded — their presence
        // would depend on the specific package, breaking the "same set for every target" contract.
        Assert.DoesNotContain("## References", output);
        Assert.DoesNotContain("## Custom Attributes", output);
        Assert.DoesNotContain("## Resources", output);
        Assert.DoesNotContain("## Type Forwarders", output);
        // Verbose sections stay out too (they appear only at -v:d).
        Assert.DoesNotContain("## Async Methods", output);
        Assert.DoesNotContain("## Extension Methods", output);
        // The availability row was removed from Signals; the overview stays network-free.
        Assert.DoesNotContain("SourceLink availability", output);
    }

    [Fact]
    public async Task LibraryCommand_SelectReferences_RendersReferenceRows()
    {
        // Regression: References declared a null scanner key, meaning "data always collected",
        // but assembly references are extracted only on demand. Selecting the section reported
        // "no data" for every assembly.
        var (exit, output, _) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "References", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("## References", output);
        Assert.Contains("| System.Runtime |", output);
        Assert.DoesNotContain("no data", output);
    }

    [Fact]
    public async Task LibraryCommand_SelectDependencies_RendersTransitiveTree()
    {
        // Regression: Dependencies named the "TransitiveRefs" scanner, but no scanner was
        // registered under that key and the section additionally gated on the --dependencies
        // view-routing flag, so -S Dependencies could never produce data.
        var (exit, output, _) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Dependencies", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("## Dependencies", output);
        Assert.Contains("System.Runtime", output);
        Assert.DoesNotContain("no data", output);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverTreeShapedSection_ExplainsTheEmptySchema()
    {
        // Regression: -D asks what rows a section has, and a tree-shaped section answered with
        // nothing at all -- exit 0, no stdout, no stderr -- which reads exactly like a section
        // that has no fields. Both discovery paths return before the row-projection note, and
        // discovery selects through Discover rather than IncludeSections, so neither the
        // projection test nor the explicit-selection test reached it.
        foreach (var extra in new[] { Array.Empty<string>(), new[] { "--schema" } })
        {
            string[] args = ["library", "System.Text.Json", "-D", "Dependencies", .. extra, "--tips", "q"];
            var (exit, _, error) = await RunAppAsync(args);

            Assert.Equal(0, exit);
            Assert.Contains("not row-shaped", error, StringComparison.Ordinal);
            Assert.Contains("Dependencies", error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task LibraryCommand_DiscoverRowShapedSection_StaysQuiet()
    {
        // Positive control for the note above: a section that does have rows must not draw it,
        // and neither must bare -D, which names no section at all. Without these the note could
        // fire unconditionally and the test above would still pass.
        var (rowExit, rowOutput, rowError) = await RunAppAsync(
            "library", "System.Text.Json", "-D", "References", "--tips", "q");

        Assert.Equal(0, rowExit);
        Assert.NotEmpty(rowOutput);
        Assert.DoesNotContain("not row-shaped", rowError, StringComparison.Ordinal);

        var (bareExit, bareOutput, bareError) = await RunAppAsync(
            "library", "System.Text.Json", "-D", "--tips", "q");

        Assert.Equal(0, bareExit);
        Assert.NotEmpty(bareOutput);
        Assert.DoesNotContain("not row-shaped", bareError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverSectionExcludedBySelection_StaysQuiet()
    {
        // -S narrows effective discovery, so a section it excludes is not "matched". Without the
        // intersection this printed the note for Dependencies in the same breath as the pipeline
        // reporting Dependencies had no data for the query -- a note about a section the user had
        // just excluded.
        var (exit, _, error) = await RunAppAsync(
            "library", "System.Text.Json", "-D", "Dependencies", "-S", "References", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("not row-shaped", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LibraryCommand_SelectDependenciesAndReferences_RendersBoth()
    {
        // The flat list and the tree are independent lenses. References used to blank itself
        // whenever a tree existed, which suppressed it when both were selected together.
        var (exit, output, _) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Dependencies,References", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("## Dependencies", output);
        Assert.Contains("## References", output);
        Assert.DoesNotContain("no data", output);
    }

    [Fact]
    public async Task LibraryCommand_Dependencies_StaysOutOfDefaultViewsAndDiscoverable()
    {
        // Building the tree reads every referenced assembly transitively, so it is opt-in; it
        // must still be reachable from -D or the only way to find it is to already know it exists.
        var (detailExit, detailOutput, _) = await RunAppAsync(
            "library", "System.Text.Json", "-v:d", "--tips", "q");
        var (discoverExit, discoverOutput, _) = await RunAppAsync(
            "library", "System.Text.Json", "-D", "--tips", "q");
        var (doorExit, doorOutput, _) = await RunAppAsync(
            "library", "System.Text.Json", "-D", "@Dependencies", "--tips", "q");
        var (allExit, allOutput, _) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "@All", "--tips", "q");

        Assert.Equal(0, detailExit);
        Assert.DoesNotContain("## Dependencies", detailOutput);

        // ExplicitOnly keeps the unbounded closure out of @All, and @All membership doubles as
        // the top-level -D listing test -- so discovery runs through the category door instead.
        Assert.Equal(0, discoverExit);
        Assert.Contains("| @Dependencies | category", discoverOutput);
        Assert.Equal(0, doorExit);
        Assert.Contains("Dependencies", doorOutput);
        Assert.Equal(0, allExit);
        Assert.DoesNotContain("## Dependencies", allOutput);
    }

    // A tree section has data but no row projection, so the row-oriented modes render nothing.
    // Silence there is indistinguishable from a successful empty result, which is the failure mode
    // AGENTS.md forbids ("do not turn rendering failures into success-shaped empty output").
    [Theory]
    [InlineData("--table")]
    [InlineData("--tsv")]
    [InlineData("--jsonl")]
    [InlineData("--count")]
    public async Task LibraryCommand_TreeSectionInRowMode_SaysItCannotBeProjected(string mode)
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Dependencies", mode, "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("cannot be projected to rows", error);
        Assert.Contains("Dependencies", error);
        // The note is diagnostic, so it must not contaminate the data stream.
        Assert.DoesNotContain("cannot be projected to rows", output);
    }

    [Fact]
    public async Task LibraryCommand_RowShapedSection_DoesNotClaimMissingRowProjection()
    {
        // Close negative: a genuinely tabular section must never trip the note, and the default
        // Markdown mode must not warn either -- the tree renders perfectly well there.
        var (tableExit, tableOutput, tableError) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "References", "--table", "--tips", "q");
        var (mdExit, _, mdError) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Dependencies", "--tips", "q");

        Assert.Equal(0, tableExit);
        Assert.DoesNotContain("cannot be projected to rows", tableError);
        Assert.Contains("System.Collections", tableOutput);
        Assert.Equal(0, mdExit);
        Assert.DoesNotContain("cannot be projected to rows", mdError);
    }

    [Fact]
    public async Task LibraryCommand_PlatformFacade_LibraryInfoShowsFacadeAssemblyYes()
    {
        var (assemblyPath, _, _, error) = PlatformResolver.ResolveAssembly("System.Runtime.CompilerServices.Unsafe");
        if (assemblyPath == null || error != null)
        {
            Assert.Skip($"System.Runtime.CompilerServices.Unsafe not available: {error}");
            return;
        }

        Assert.SkipUnless(PlatformResolver.IsFacadeOnlyAssembly(assemblyPath),
            "System.Runtime.CompilerServices.Unsafe is not facade-only in this runtime.");

        var (exit, output, runError) = await RunAppAsync(
            "library", "System.Runtime.CompilerServices.Unsafe", "-S", "Library Info", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(runError);
        Assert.Contains("| Facade | Yes |", output);
    }

    [Fact]
    public async Task LibraryCommand_PlatformNonFacade_LibraryInfoShowsFacadeAssemblyNo()
    {
        var (assemblyPath, _, _, error) = PlatformResolver.ResolveAssembly("System.Text.Json");
        if (assemblyPath == null || error != null)
        {
            Assert.Skip($"System.Text.Json not available: {error}");
            return;
        }

        Assert.False(PlatformResolver.IsFacadeOnlyAssembly(assemblyPath));

        var (exit, output, runError) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Library Info", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(runError);
        Assert.Contains("| Facade | No |", output);
    }

    [Fact]
    public async Task LibraryCommand_NonPlatformLibraryInfo_DoesNotShowFacadeAssembly()
    {
        var (exit, output, runError) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", "Library Info", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(runError);
        Assert.DoesNotContain("| Facade |", output);
    }

    [Fact]
    public async Task LibraryCommand_Value_UsesEffectiveLibraryInfoFieldNames()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Library Info", "--fields", "Assembly Version", "--value", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Matches(@"^\d+\.\d+\.\d+\.\d+$", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_Value_RejectsNonDiscoveredFieldName()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Library Info", "--fields", "TFM", "--value", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("field 'TFM' not found in section 'Library Info'", error);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverLibraryInfo_FiltersFieldsToRenderedRows()
    {
        var (selectExit, selectOutput, selectError) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Library Info", "--tips", "q");
        var (discoverExit, discoverOutput, discoverError) = await RunAppAsync(
            "library", "System.Text.Json", "-D", "Library Info", "--tips", "q");

        Assert.Equal(0, selectExit);
        Assert.Equal(0, discoverExit);
        Assert.Empty(selectError);
        Assert.Empty(discoverError);
        Assert.Equal(
            selectOutput.Contains("| Architecture |", StringComparison.Ordinal),
            discoverOutput.Contains("| Architecture | field |", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LibraryCommand_DiscoverLibraryInfo_DoesNotKeepSubstringOnlyFields()
    {
        var (assemblyPath, _, _, error) = PlatformResolver.ResolveAssembly("System.Runtime");
        if (assemblyPath == null || error != null)
        {
            Assert.Skip($"System.Runtime not available: {error}");
            return;
        }

        Assert.SkipUnless(PlatformResolver.IsFacadeOnlyAssembly(assemblyPath),
            "System.Runtime is not facade-only in this runtime.");

        var (selectExit, selectOutput, selectError) = await RunAppAsync(
            "library", "System.Runtime", "-S", "Library Info", "--tips", "q");
        var (discoverExit, discoverOutput, discoverError) = await RunAppAsync(
            "library", "System.Runtime", "-D", "Library Info", "--tips", "q");
        var (multiDiscoverExit, multiDiscoverOutput, multiDiscoverError) = await RunAppAsync(
            "library", "System.Runtime", "-D", "Library Info,Async Methods", "--tips", "q");

        Assert.Equal(0, selectExit);
        Assert.Equal(0, discoverExit);
        Assert.Equal(0, multiDiscoverExit);
        Assert.Empty(selectError);
        Assert.Empty(discoverError);
        Assert.Contains("section 'Async Methods' has no data", multiDiscoverError);
        Assert.DoesNotContain("| Methods |", selectOutput);
        Assert.DoesNotContain("| Methods | field |", discoverOutput);
        Assert.DoesNotContain("| Methods | field |", multiDiscoverOutput);
        Assert.Contains("| Async Methods | field |", discoverOutput);
        Assert.Contains("| Extension Methods | field |", discoverOutput);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverEffective_RendersMarkdownTable()
    {
        var (exit, output, _) = await RunAppAsync("library", "System.Text.Json", "-D");

        Assert.Equal(0, exit);
        Assert.Contains("| Name | Kind |", output);
        Assert.Contains("| Library Info | section |", output);
        // The curated -D catalog drops the internal (verbose)/(opt-in) markers: every
        // effective section is listed with the bare "section" kind.
        Assert.Contains("| Async Methods | section |", output);
        Assert.Contains("| Custom Attributes | section |", output);
        Assert.DoesNotContain("section (opt-in)", output);
        Assert.DoesNotContain("section (verbose)", output);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverDetailedTree_UsesCuratedCatalog()
    {
        // -D auto-promotes to a tree at detailed verbosity. That tree must use the same curated
        // catalog as the flat -D listing: categories-first, no @All/@Default/@Hidden poles, and
        // no internal (verbose)/(opt-in) annotations. Regression guard for the tree branch wiring.
        var (exit, output, _) = await RunAppAsync("library", "System.Text.Json", "-D", "-v:d");

        Assert.Equal(0, exit);
        Assert.Contains("@Audit (category)", output);
        Assert.Contains("@SourceLink (category)", output);
        Assert.DoesNotContain("(opt-in)", output);
        Assert.DoesNotContain("(verbose)", output);
        Assert.DoesNotContain("@All", output);
        Assert.DoesNotContain("@Default", output);
        Assert.DoesNotContain("@Hidden", output);
    }

    [Fact]
    public async Task CliDiscoverySections_AreSelectable()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var diffV1 = FixtureCatalog.DiffPair.OldAssemblyPath();
            var diffV2 = FixtureCatalog.DiffPair.NewAssemblyPath();

            List<string[]> commands =
            [
                ["library", TestAssemblyPath],
                ["type", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath],
                ["type", typeof(EmptyDiscoveryFixture).FullName!, "--library", TestAssemblyPath],
                ["member", typeof(MemberCallsFixture).FullName!, nameof(MemberCallsFixture.CallsInterfaceItem), "--library", TestAssemblyPath],
                ["member", typeof(MemberCallsFixture).FullName!, nameof(MemberCallsFixture.Overloaded), "--library", TestAssemblyPath],
                ["package", packagePath],
                ["diff", "--library", $"{diffV1}..{diffV2}", "-t", "DiffFixtureSample.DiffSample"]
            ];

            foreach (var command in commands)
            {
                var discoveryArgs = command.Concat(["-D", "--tips", "q"]).ToArray();
                var (discoverExit, discoverOutput, discoverError) = await RunAppAsync(discoveryArgs);
                Assert.Equal(0, discoverExit);

                var sections = ExtractDiscoveryRows(discoverOutput)
                    .Where(row => row.Kind.StartsWith("section", StringComparison.OrdinalIgnoreCase))
                    .Select(row => row.Name)
                    .ToArray();
                if (!IsNoMemberTypeDiscoveryCommand(command))
                    Assert.NotEmpty(sections);

                foreach (var section in sections)
                {
                    var selectArgs = BuildDiscoverySelectionArgs(command, section);
                    var (selectExit, selectOutput, selectError) = await RunAppAsync(selectArgs);
                    var selectionSucceeded = selectExit == 0
                        || IsSourceIntegrityStatusResult(command, section, selectExit, selectOutput);
                    Assert.True(selectionSucceeded,
                        $"{command[0]} -S '{section}' failed after being listed by -D. Discovery stderr: {discoverError}. Selection stderr: {selectError}");
                    if (RequiresRealDataDiscoveryGuard(command))
                    {
                        Assert.False(string.IsNullOrWhiteSpace(selectOutput),
                            $"{command[0]} -S '{section}' produced no data after being listed by -D.");
                        Assert.DoesNotContain("has no data", selectOutput, StringComparison.OrdinalIgnoreCase);
                        Assert.DoesNotContain("has no data", selectError, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
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
        if (command is ["library", ..] && section == "Context: Source Location")
            args.AddRange(["--il-offset", "0x06000041+0x0"]);
        args.AddRange(["-S", section, "--table", "--tips", "q", "-n", "40"]);
        return [.. args];
    }

    private static bool RequiresRealDataDiscoveryGuard(string[] command)
        => IsNoMemberTypeDiscoveryCommand(command)
           || command is ["member", _, var memberName, ..]
                && memberName == nameof(MemberCallsFixture.Overloaded);

    private static bool IsNoMemberTypeDiscoveryCommand(string[] command)
        => command is ["type", var typeName, ..]
           && typeName == typeof(EmptyDiscoveryFixture).FullName;

    [Fact]
    public async Task MemberDiscovery_MultiOverload_DoesNotListSingleOverloadSections()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallsFixture).FullName!, nameof(MemberCallsFixture.Overloaded),
            "--library", TestAssemblyPath, "-D", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Tip:", error);

        var sections = ExtractDiscoveryRows(output)
            .Where(row => row.Kind.StartsWith("section", StringComparison.OrdinalIgnoreCase))
            .Select(row => row.Name)
            .ToArray();

        foreach (var section in SingleOverloadDiscoverySections)
            Assert.DoesNotContain(section, sections);
    }

    [Fact]
    public async Task TypeDiscovery_NoMemberType_DoesNotListMethodBodySections()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", typeof(EmptyDiscoveryFixture).FullName!, "--library", TestAssemblyPath, "-D", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Tip:", error);

        var sections = ExtractDiscoveryRows(output)
            .Where(row => row.Kind.StartsWith("section", StringComparison.OrdinalIgnoreCase))
            .Select(row => row.Name)
            .ToArray();

        Assert.DoesNotContain("Top Leverage", sections);
        Assert.DoesNotContain("Performance Triage", sections);
        Assert.DoesNotContain("Facts", sections);
        Assert.DoesNotContain("Cost Overlay", sections);
        Assert.DoesNotContain("Semantics Overlay", sections);
        Assert.DoesNotContain("IL", sections);
        Assert.DoesNotContain("Source Files", sections);
    }

    private static readonly string[] SingleOverloadDiscoverySections =
    [
        "Signature",
        "Custom Attributes",
        "Decompiled Source",
        "Annotated Source",
        "Cost Overlay",
        "Semantics Overlay",
        "Original Source",
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

    [Fact]
    public async Task LibraryCommand_DiscoverEffective_GroupsSourceLinkUnderSourceLinkDoor()
    {
        // SourceLink discovery is symbol-dependent: the SourceLink family only lists under -D
        // when a local PDB (embedded, adjacent, or already in the symbol cache) exposes a
        // SourceLink document — network-free. Newtonsoft's PDB is external (snupkg), so warm the
        // symbol cache first with an explicit render; discovery then resolves it cache-only.
        var (warmExit, _, _) = await RunAppAsync(
            "library", "--package", "Newtonsoft.Json", "-S", "SourceLink: Availability", "--tips", "q");
        Assert.Equal(0, warmExit);

        // Curated catalog: SourceLink audit sections are not @All members, so bare -D lists them
        // under the @SourceLink door, never at the top level.
        var (exit, output, error) = await RunAppAsync(
            "library", "--package", "Newtonsoft.Json", "-D", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Tip:", error);
        Assert.DoesNotContain("SourceLink: Availability", output);
        Assert.DoesNotContain("SourceLink: Missing Files", output);
        Assert.DoesNotContain("SourceLink: Integrity", output);
        Assert.Contains("@SourceLink", output);
        // @Hidden is a schema-only pole: it never appears as a bare -D category row.
        Assert.DoesNotContain("@Hidden", output);

        var (sourceExit, sourceOutput, sourceError) = await RunAppAsync(
            "library", "--package", "Newtonsoft.Json", "-D", "@SourceLink", "--table", "--tips", "q");

        Assert.Equal(0, sourceExit);
        Assert.DoesNotContain("Tip:", sourceError);
        Assert.Contains("SourceLink: Files", sourceOutput);
        Assert.Contains("SourceLink: Availability", sourceOutput);
        Assert.Contains("SourceLink: Missing Files", sourceOutput);
        // The whole SourceLink: prefix family sits behind its own door, Integrity included: a
        // prefix advertises category membership, so a prefixed section reachable only through the
        // @Hidden pole was a discoverability hole. Integrity costs one extra GET+hash pass
        // (~+0.3-0.4s cold on these libraries), which the door's other unbounded members already
        // imply, so completing the family does not change the door's cost class.
        Assert.Contains("SourceLink: Integrity", sourceOutput);
    }

    [Fact]
    public async Task LibraryCommand_RenderSelectHidden_IsRejectedAsDiscoveryOnly()
    {
        // @Hidden is a discovery-only pole: -S @Hidden must be rejected (exit 1) so it cannot fan
        // out to its unbounded members as a group. Discovery (-D @Hidden) and exact-name render
        // (-S "Top Leverage") remain the supported entrypoints.
        var (exit, _, error) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", "@Hidden", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("discovery-only", error);

        // -D @Hidden still lists the pole's members (no rejection).
        var (discoverExit, discoverOutput, _) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", "@Hidden", "--table", "--tips", "q");

        Assert.Equal(0, discoverExit);
        Assert.Contains(SectionNames.TopLeverage, discoverOutput);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverSchema_GroupsOptInSections()
    {
        var (exit, output, _) = await RunAppAsync("library", "System.Text.Json", "-D", "--schema");

        Assert.Equal(0, exit);

        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains("section", StringComparison.Ordinal))
            .ToArray();
        var names = lines.Select(ExtractSectionName).ToArray();

        // --schema is the exhaustive escape hatch: every section is listed with the bare
        // "section" kind. The curated catalog dropped the internal (verbose)/(opt-in) markers.
        Assert.DoesNotContain("section (opt-in)", output);
        Assert.DoesNotContain("section (verbose)", output);
        Assert.Contains("Symbols", names);

        // Unlike the -D top level, --schema surfaces the whole catalog: the surface opt-ins, the
        // source/audit sections, the footguns, the kind-scoped performance sub-group, and the
        // coordinate-gated IL-context sections.
        foreach (var expected in new[]
                 {
                     "Async Methods", "Custom Attributes", "Extension Methods", "Type Forwarders",
                     "Union Types", "P/Invoke Methods", "Non-normalized Paths", "Top Leverage",
                     "Unsafe Members", "SourceLink: Files", "SourceLink: Availability",
                     "SourceLink: Missing Files", "SourceLink: Integrity", "Context: Member",
                     "Integration: Opportunities"
                 })
        {
            Assert.Contains(expected, names);
        }

        Assert.Contains(names, name => name.StartsWith("Performance: ", StringComparison.Ordinal));
        Assert.Contains(names, name => name.StartsWith("Integration: ", StringComparison.Ordinal));

        // The topical category doors lead the catalog: they are exactly the seven doors, in
        // alphabetical order, and every category row precedes every section row. @Metadata and
        // @Dependencies are among them because --schema surfaces the whole catalog, including the
        // explicit-only lenses the curated top-level -D still leaves out.
        var categoryLines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains("category", StringComparison.Ordinal))
            .ToArray();
        var categoryNames = categoryLines.Select(ExtractSectionName).ToArray();
        Assert.Equal(
            new[] { "@Audit", "@Dependencies", "@Integrations", "@Metadata", "@Performance", "@SourceLink", "@Surface" },
            categoryNames);

        var raw = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var lastCategoryIndex = Array.FindLastIndex(raw, line => line.Contains("category", StringComparison.Ordinal));
        var firstSectionIndex = Array.FindIndex(raw, line => line.Contains("section", StringComparison.Ordinal));
        Assert.True(lastCategoryIndex >= 0 && firstSectionIndex >= 0);
        Assert.True(lastCategoryIndex < firstSectionIndex, "category doors must lead the section catalog");

        // Computed/internal poles are never user-facing: @Hidden, @Default and @All dissolved.
        Assert.DoesNotContain(categoryLines, line => ExtractSectionName(line) == "@Hidden");
        Assert.DoesNotContain(categoryLines, line => ExtractSectionName(line) == "@Default");
        Assert.DoesNotContain(categoryLines, line => ExtractSectionName(line) == "@All");
        Assert.DoesNotContain(categoryLines, line => ExtractSectionName(line) == "@Switches");

        Assert.DoesNotContain(lines, line => line.StartsWith("Missing Source Files", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.StartsWith("Source Integrity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LibraryCommand_DiscoverPerformanceTriage_ListsRenderableColumns()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", "Performance: Boxing", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("not found", error);
        // Tight markdown columns (rich diagnostics moved to nested --json).
        Assert.Contains("| Member | column |", output);
        Assert.Contains("| Evidence | column |", output);
        Assert.Contains("| Allocation | column |", output);
        Assert.Contains("| Loop | column |", output);
        Assert.Contains("| Reach | column |", output);
        Assert.Contains("| Weight | column |", output);
        Assert.Contains("| Confidence | column |", output);
        // Row-query fields remain discoverable (shared triage filter/sort engine).
        Assert.Contains("| Triage desc | default-order |", output);
        Assert.Contains("| Loop desc | order-step |", output);
        Assert.Contains("| Shape | filterable |", output);
        Assert.Contains("| RootReach | sortable |", output);
        Assert.Contains("| OncePaths | sortable |", output);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverCategoryDoor_ListsMembersAlphabetically()
    {
        // Drilling into a category door (-D @Category) lists its members alphabetically, the same
        // single rule as the flat -D catalog and rendered sections. @Performance is the strong
        // case: its declared order (PerformanceKinds.Sections) is deliberately non-alphabetical,
        // so an alpha listing proves the sort is applied rather than incidental.
        var (exit, output, _) = await RunAppAsync(
            "library", "System.Text.Json", "-D", "@Performance");

        Assert.Equal(0, exit);

        var members = output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains("| section", StringComparison.Ordinal))
            .Select(ExtractSectionName)
            .ToArray();

        Assert.NotEmpty(members);
        Assert.All(members, name => Assert.StartsWith("Performance: ", name));
        Assert.Equal(
            members.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToArray(),
            members);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverResourceTriage_ListsRenderableColumns()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            TestAssemblyPath,
            "-D",
            SectionNames.ArrayPoolEscapes,
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("not found", error);
        Assert.Contains("| Member | column |", output);
        Assert.Contains("| Candidate | column |", output);
        Assert.Contains("| Finding | column |", output);
        Assert.Contains("| Actionability | column |", output);
        Assert.Contains("| Boundary | column |", output);
        Assert.Contains("| Acquire IL | column |", output);
        Assert.Contains("| Boundary IL | column |", output);
    }

    [Fact]
    public async Task LibraryCommand_SourceFilesSection_RendersTypeUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.CommandLine.dll", "--package", "System.CommandLine",
            "-S", "SourceLink: Files", "--tips", "q", "-n", "18");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## SourceLink: Files", output);
        Assert.Contains("| Type | Url |", output);
        Assert.Contains("System.CommandLine.Command", output);
        Assert.Contains("Command.cs", output);
    }

    [Fact]
    public async Task LibraryCommand_SourceFilesSection_TypeFilterAndBlobUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--package", "Newtonsoft.Json",
            "-S", "Source Files", "-t", "JsonConvert", "--blob", "--tsv", "--no-headers", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Newtonsoft.Json.JsonConvert", output);
        Assert.Contains("github.com/JamesNK/Newtonsoft.Json/blob/", output);
        Assert.DoesNotContain("Newtonsoft.Json.JsonSerializer\t", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetFlag_ImplicitlySelectsSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Source Location", output);
        Assert.Contains("| Field | Value |", output);
        Assert.Contains("| Method | System.HexConverter.FromChar |", output);
        Assert.Contains("| Token | 0x6000001 |", output);
        Assert.Contains("| IL Offset | 0x0 |", output);
        Assert.Contains("HexConverter.cs", output);
        Assert.Contains("## Context: Member", output);
        Assert.Contains("## Context: Instruction", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetsFile_RendersCoordinateSummary()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path,
            """
            # label coordinate
            profiler-sample 0x06000001+0x1
            return-address 0x06000001+0x6
            """,
            TestContext.Current.CancellationToken);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library", TestAssemblyPath, "--il-offsets", path, "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("## IL Coordinates", output);
            Assert.Contains("Coordinate", output);
            Assert.Contains("IL Offset", output);
            Assert.Contains("Meaning", output);
            Assert.Contains("Evidence", output);
            Assert.Contains("profiler-sample", output);
            Assert.Contains("callsite", output);
            Assert.Contains("return-address", output);
            Assert.Contains("return address", output);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetsFile_RejectsBadCoordinateLine()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path,
            """
            bad debugger frame
            good 0x06000001+0x1
            """,
            TestContext.Current.CancellationToken);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library", TestAssemblyPath, "--il-offsets", path, "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(error);
            Assert.Contains(Path.GetFileName(path), output);
            Assert.Contains("expected a MethodDef token + IL offset coordinate", output);
            Assert.Contains("good", output);
            Assert.Contains("callsite", output);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetsFile_JsonUsesSnakeCaseEnvelope()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "sample 0x06000001+0x1", TestContext.Current.CancellationToken);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library", TestAssemblyPath, "--il-offsets", path, "--json", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("\"rows\"", output);
            Assert.Contains("\"il_offset\"", output);
            Assert.DoesNotContain("\"ILOffset\"", output);
            Assert.DoesNotContain("\"Coordinate\"", output);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LibraryCommand_SourceLocationSectionSelector_UsesFlagParameter()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "-S", "Context: Source Location", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Source Location", output);
        Assert.Contains("| Field | Value |", output);
        Assert.Contains("| Method | System.HexConverter.FromChar |", output);
        Assert.Contains("| Token | 0x6000001 |", output);
        Assert.Contains("| IL Offset | 0x0 |", output);
        Assert.Contains("HexConverter.cs", output);
    }

    [Fact]
    public async Task LibraryCommand_LegacyILOffsetSectionSelector_ResolvesSourceLocation()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "-S", "IL Offset", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Source Location", output);
        Assert.DoesNotContain("## IL Offset", output);
    }

    [Fact]
    public async Task LibraryCommand_LegacyILOffsetSectionSelector_RequiresFlagParameter()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "-S", "IL Offset", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("IL coordinate sections require --il-offset", error);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetDiscovery_IsCoordinateScoped()
    {
        var (withoutExit, withoutOutput, withoutError) = await RunAppAsync(
            "library", "--platform", "System.Text.Json", "-D", "--table", "--tips", "q");
        var (withExit, withOutput, withError) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "-D", "--table", "--tips", "q");

        Assert.Equal(0, withoutExit);
        Assert.Equal(0, withExit);
        Assert.Empty(withoutError);
        Assert.Empty(withError);
        Assert.DoesNotContain("Context: Source Location", withoutOutput);
        Assert.DoesNotContain("Context: Member", withoutOutput);
        Assert.DoesNotContain("Context: Instruction", withoutOutput);
        Assert.DoesNotContain("Context: Exception", withoutOutput);
        Assert.DoesNotContain("Context: Callsite", withoutOutput);
        Assert.DoesNotContain("Context: Return Address", withoutOutput);
        Assert.Contains("Context: Source Location", withOutput);
        Assert.Contains("Context: Member", withOutput);
        Assert.Contains("Context: Instruction", withOutput);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetMemberContext_RendersMemberFacts()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "-S", "Context: Member", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Member", output);
        Assert.Contains("| Type | System.HexConverter |", output);
        Assert.Contains("| Type Kind | class |", output);
        Assert.Contains("| Member | System.HexConverter.FromChar |", output);
        Assert.Contains("| Signature | int FromChar(int c) |", output);
        Assert.Contains("| Static | Yes |", output);
        Assert.Contains("| Metadata Token | 0x6000001 |", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetMemberContext_ValueProjectsType()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "-S", "Context: Member", "--fields", "Type", "--value", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("System.HexConverter", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetMemberContext_ShowsAsyncKind()
    {
        var token = typeof(ILOffsetAsyncFixture).GetMethod(nameof(ILOffsetAsyncFixture.StateMachineAsync))!.MetadataToken;
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "--il-offset", $"0x{token:X}+0x0", "-S", "Context: Member", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("| Member | DotnetInspector.Tests.CommandExecutionTests.ILOffsetAsyncFixture.StateMachineAsync |", output);
        Assert.Contains("| Async | Runtime |", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetInstructionContext_RendersInstructionFacts()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "-S", "Context: Instruction", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Instruction", output);
        Assert.Contains("| IL Offset | 0x0 |", output);
        Assert.Contains("| Boundary | Exact |", output);
        Assert.Contains("| Opcode | ldarg.0 |", output);
        Assert.Contains("| Operand Kind | None |", output);
        Assert.Contains("| Next Offset | 0x1 |", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetInstructionContext_ValueProjectsOpcode()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "-S", "Context: Instruction", "--fields", "Opcode", "--value", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("ldarg.0", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetInstructionContext_RequiresInstructionBoundary()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x2", "-S", "Context: Instruction", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("not an instruction boundary", error);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetBareReport_RequiresInstructionBoundary()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x2", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("not an instruction boundary", error);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetMemberContext_AllowsNonInstructionBoundary()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x2", "-S", "Context: Member", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Member", output);
        Assert.Contains("| Member | System.HexConverter.FromChar |", output);
        Assert.DoesNotContain("## Context: Instruction", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetInstructionContext_FormatsFloatOperands()
    {
        var token = typeof(ILOffsetFloatFixture).GetMethod(nameof(ILOffsetFloatFixture.FloatConstant))!.MetadataToken;
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "--il-offset", $"0x{token:X}+0x0", "-S", "Context: Instruction", "--fields", "Operand", "--value", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1.5", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetExceptionContext_RendersContainingRegion()
    {
        var token = typeof(ILOffsetExceptionFixture).GetMethod(nameof(ILOffsetExceptionFixture.TryCatch))!.MetadataToken;
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "--il-offset", $"0x{token:X}+0x1", "-S", "Context: Exception", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Exception", output);
        Assert.Contains("| Region | Context | Clause | Try Range | Handler Range |", output);
        Assert.Contains("| 1 | try | catch |", output);
        Assert.Contains("System.DivideByZeroException", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetExceptionContext_ValueProjectsClause()
    {
        var token = typeof(ILOffsetExceptionFixture).GetMethod(nameof(ILOffsetExceptionFixture.TryCatch))!.MetadataToken;
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "--il-offset", $"0x{token:X}+0x1", "-S", "Context: Exception", "--fields", "Clause", "--value", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("catch", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetCallsiteContext_RendersCallsite()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x1", "-S", "Context: Callsite", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Callsite", output);
        Assert.Contains("| Call Offset | IL_0001 |", output);
        Assert.Contains("| Opcode | call |", output);
        Assert.Contains("| Call Kind | direct |", output);
        Assert.Contains("| Callee | System.HexConverter::get_CharToHexLookup() |", output);
        Assert.Contains("| Return Address | IL_0006 |", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetCallsiteContext_ValueProjectsCallee()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x1", "-S", "Context: Callsite", "--fields", "Callee", "--value", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("System.HexConverter::get_CharToHexLookup()", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetReturnAddressContext_RendersPreviousCall()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x6", "-S", "Context: Return Address", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Return Address", output);
        Assert.Contains("| IL Offset | IL_0006 |", output);
        Assert.Contains("| Call Offset | IL_0001 |", output);
        Assert.Contains("| Opcode | call |", output);
        Assert.Contains("| Callee | System.HexConverter::get_CharToHexLookup() |", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetReturnAddressContext_ValueProjectsCallOffset()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x6", "-S", "Context: Return Address", "--fields", "Call Offset", "--value", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("IL_0001", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetReturnAddressContext_RequiresInstructionBoundary()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x2", "-S", "Context: Return Address", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("not an instruction boundary", error);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetReturnAddressContext_IgnoresMethodPointerFallthrough()
    {
        var token = typeof(ILOffsetFunctionPointerFixture).GetMethod(nameof(ILOffsetFunctionPointerFixture.CreateDelegate))!.MetadataToken;
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "--il-offset", $"0x{token:X}+0x10", "-S", "Context: Return Address", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("# dotnet-inspect.Tests.dll", output);
        Assert.DoesNotContain("## Context: Return Address", output);
        Assert.Contains("matched section has no data", error);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetCount_ReturnsSingletonLocationCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "-S", "Context: Source Location", "--count", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetValue_ProjectsResolvedLine()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "-S", "Context: Source Location", "--fields", "Line", "--value", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Matches(@"^\d+$", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetPrint_PrintsResolvedSourceLine()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "-S", "Context: Source Location", "--print", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("## Context: Source Location", output);
        Assert.Contains("CharToHexLookup", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetPrintJsonArray_EmitsPrintableDocument()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "-S", "Context: Source Location", "--print", "--json-array", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("[", output.Trim());
        Assert.Contains("\"section\":\"Context: Source Location\"", output);
        Assert.Contains("\"label\":\"System.HexConverter.FromChar\"", output);
        Assert.Contains("CharToHexLookup", output);
    }


    [Fact]
    public async Task LibraryCommand_IlOffsetCountRejectsPrint()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "-S", "Context: Source Location", "--count", "--print", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--count cannot be combined with --print", error);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetPrint_DoesNotReadLocalPdbPath()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "secret-local-file-line", TestContext.Current.CancellationToken);
            var result = new ILOffsetProjection
            {
                Method = "Attacker.Method",
                File = tempFile,
                Line = 1
            };

            var (content, error) = await LibraryCommand.ReadILOffsetSourceLineForTestsAsync(result);

            Assert.Null(content);
            Assert.Contains("no printable source body", error);
            Assert.DoesNotContain("secret-local-file-line", error);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetSectionSelector_RequiresFlagParameter()
    {
        var (exit, _, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "-S", "Context: Source Location", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("IL coordinate sections require --il-offset", error);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetParameterizedSectionSelector_IsRejected()
    {
        var (exit, _, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "-S", "Context: Source Location:0x06000001+0x0", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("IL offset parameters belong in --il-offset", error);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetWildcardSelectionWithoutValue_DoesNotRequireFlag()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "-S", "*", "-n", "8", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("IL coordinate sections require", error);
        Assert.Contains("##", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetFlag_ErrorsWhenSelectedSectionsExcludeILOffset()
    {
        var (exit, _, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "-S", "Library Info", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("--il-offset requires an IL coordinate section", error);
    }

    [Fact]
    public async Task LibraryCommand_ExtractResources_RejectsTraversalWithFailureExit()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("resource-extraction-command-");
        try
        {
            var assemblyPath = Path.Combine(tempDirectory.FullName, "MaliciousResources.dll");
            var outputPath = Path.Combine(tempDirectory.FullName, "output");
            var escapedPath = Path.Combine(tempDirectory.FullName, "escaped.txt");
            WriteResourceAssembly(
                assemblyPath,
                ("safe.txt", "safe"u8.ToArray()),
                ("../escaped.txt", "escaped"u8.ToArray()));

            var (exit, output, error) = await RunAppAsync(
                "library", assemblyPath,
                "--extract-resources", outputPath,
                "--json",
                "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("safe relative extraction path", error);
            Assert.False(Directory.Exists(outputPath));
            Assert.False(File.Exists(escapedPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_SwitchesSection_DetectsFeatureSwitchDefinitions()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Switches", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Switches", output);
        Assert.Contains("| Kind | Switch | API |", output);
        Assert.Contains("| Feature Switch | `System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault` | `System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault` |", output);
        Assert.Contains("| AppContext | `System.Text.Json.Serialization.RespectNullableAnnotationsDefault` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverSwitchesCategory_ListsSwitchesSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-D", "@Surface", "--table");

        Assert.Equal(0, exit);
        Assert.Contains("Switches", output);
        Assert.DoesNotContain("Integrations", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverSwitchesCategory_DetectsAppContextOnlyAssembly()
    {
        var assemblyPath = typeof(AppContextSwitchFixture).Assembly.Location;
        using (var stream = File.OpenRead(assemblyPath))
        using (var peReader = new PEReader(stream))
            Assert.Empty(SwitchScanner.Scan(peReader));

        var (exit, output, error) = await RunAppAsync(
            "library", assemblyPath, "-D", "@Surface", "--table");

        Assert.Equal(0, exit);
        Assert.Contains("Switches", output);
        Assert.DoesNotContain("Integrations", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task AppContextSwitchProjection_UsesRawStringsAndLeavesDeduplicationToConsumer()
    {
        var assemblyPath = typeof(AppContextSwitchFixture).Assembly.Location;
        using var session = AssemblyInspectionSession.Open(assemblyPath);
        var occurrences = AppContextSwitchProjectionProducer.Produce(session.MethodBodies);

        Assert.Contains(
            occurrences,
            occurrence => occurrence.Switch == @"DotnetInspector.Fixtures.Literal\nSwitch");
        Assert.Equal(
            2,
            occurrences.Count(
                occurrence => occurrence.Switch == "DotnetInspector.Fixtures.Duplicate"));
        Assert.DoesNotContain(
            occurrences,
            occurrence => occurrence.Switch == "DotnetInspector.Fixtures.Lookalike");

        var (exit, output, error) = await RunAppAsync(
            "library", assemblyPath, "-S", "Switches", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Equal(
            1,
            output.Split(
                "DotnetInspector.Fixtures.Duplicate",
                StringSplitOptions.None).Length - 1);
        Assert.Contains(@"DotnetInspector.Fixtures.Literal\nSwitch", output);
        Assert.DoesNotContain("DotnetInspector.Fixtures.Lookalike", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverAuditCategory_ListsAuditWorkflowSections()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-D", "@Audit", "--table");

        Assert.Equal(0, exit);
        Assert.Contains("Signals", output);
        Assert.Contains("Symbols", output);
        Assert.DoesNotContain("Switches", output);
        Assert.DoesNotContain("Integrations", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_IntegrationOpportunities_ForAwsS3_ShowsCloudClientSuggestions()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "AWSSDK.S3", "--library", "-S", "Integration: Opportunities", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Opportunities", output);
        Assert.Contains("| Integration | API | Integration Type | Look For |", output);
        Assert.Contains("| Aspire | `Amazon.S3.AmazonS3Client` | AppHost resource builder | IResourceBuilder&lt;T&gt;, Add*, *Resource |", output);
        Assert.Contains("| Dependency Injection | `Amazon.S3.AmazonS3Client` | IServiceCollection registration | IServiceCollection, Add* |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_IntegrationOpportunities_ForCognito_ShowsAuthenticationSuggestion()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Amazon.Extensions.CognitoAuthentication", "--library", "-S", "Integration: Opportunities", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Opportunities", output);
        Assert.Contains("| Authentication | `Amazon.Extensions.CognitoAuthentication.CognitoUser` | Authentication/Identity registration | AuthenticationBuilder, Add*Identity*, Add*Cognito* |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_IntegrationOpportunities_ForNpgsql_ShowsResourceSuggestions()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Npgsql", "--library", "-S", "Integration: Opportunities", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Opportunities", output);
        Assert.Contains("| Aspire | `Npgsql.NpgsqlConnection` | AppHost resource builder | IResourceBuilder&lt;T&gt;, Add*, *Resource |", output);
        Assert.Contains("| Health Checks | `Npgsql.NpgsqlConnection` | IHealthChecksBuilder registration | IHealthChecksBuilder, Add* |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_IntegrationOpportunities_ForAzureAppConfiguration_ShowsConfigurationSuggestion()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Azure.Data.AppConfiguration", "--library", "-S", "Integration: Opportunities", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Opportunities", output);
        Assert.Contains("| Configuration | `Azure.Data.AppConfiguration.ConfigurationClient` | IConfigurationBuilder source | IConfigurationBuilder, AddAzureAppConfiguration |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_ConfigurationIntegration_ForSystemsManager_ShowsConfigurationApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Amazon.Extensions.Configuration.SystemsManager", "--library", "-S", "Integration: Configuration", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Configuration", output);
        Assert.Contains("| Kind | API |", output);
        Assert.Contains("| Configuration Source | `Microsoft.Extensions.Configuration.SystemsManagerExtensions.AddSystemsManager(...)` |", output);
        Assert.Contains("| Configuration Source | `Microsoft.Extensions.Configuration.AppConfigExtensions.AddAppConfig(...)` |", output);
        Assert.Contains("| Provider | `Amazon.Extensions.Configuration.SystemsManager.SystemsManagerConfigurationProvider` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_ConfigurationIntegration_ForJson_ShowsConfigurationProviderShape()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.Configuration.Json", "--library", "-S", "Integration: Configuration", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Configuration", output);
        Assert.Contains("| Configuration Source | `Microsoft.Extensions.Configuration.JsonConfigurationExtensions.AddJsonFile(...)` |", output);
        Assert.Contains("| Configuration Source | `Microsoft.Extensions.Configuration.JsonConfigurationExtensions.AddJsonStream(...)` |", output);
        Assert.Contains("| Provider | `Microsoft.Extensions.Configuration.Json.JsonConfigurationProvider` |", output);
        Assert.Contains("| Source | `Microsoft.Extensions.Configuration.Json.JsonConfigurationSource` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_ConfigurationIntegration_ForUserSecrets_ShowsConfigurationApi()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.Configuration.UserSecrets", "--library", "-S", "Integration: Configuration", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Configuration", output);
        Assert.Contains("| API |", output);
        Assert.Contains("| `Microsoft.Extensions.Configuration.UserSecretsConfigurationExtensions.AddUserSecrets(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_ConfigurationIntegration_ForBinder_ShowsBindingApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.Configuration.Binder", "--library", "-S", "Integration: Configuration", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Configuration", output);
        Assert.Contains("| Binding | `Microsoft.Extensions.Configuration.ConfigurationBinder.Bind(...)` |", output);
        Assert.Contains("| Binding | `Microsoft.Extensions.Configuration.ConfigurationBinder.GetValue(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_ConfigurationIntegration_ForOptionsConfiguration_ShowsOptionsBindingApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.Options.ConfigurationExtensions", "--library", "-S", "Integration: Configuration", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Configuration", output);
        Assert.Contains("| `Microsoft.Extensions.DependencyInjection.OptionsBuilderConfigurationExtensions.BindConfiguration(...)` |", output);
        Assert.Contains("| `Microsoft.Extensions.DependencyInjection.OptionsConfigurationServiceCollectionExtensions.Configure(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_DependencyInjectionIntegration_ForScrutor_ShowsScanningAndDecorationApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Scrutor", "--library", "-S", "Integration: Dependency Injection", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Dependency Injection", output);
        Assert.Contains("| Assembly Scanning | `Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.Scan(...)` |", output);
        Assert.Contains("| Decoration | `Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.Decorate(...)` |", output);
        Assert.Contains("| Decoration | `Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.TryDecorate(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OptionsIntegration_ForValidationPackage_ShowsValidationApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "ReHackt.Extensions.Options.Validation", "--library", "-S", "Integration: Options", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Options", output);
        Assert.Contains("| `Microsoft.Extensions.DependencyInjection.OptionsBuilderValidationExtensions.ValidateDataAnnotationsRecursively(...)` |", output);
        Assert.Contains("| `Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.ConfigureAndValidate(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_HealthChecksIntegration_ForAspNetCoreMiddleware_ShowsUseHealthChecks()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.AspNetCore.Diagnostics.HealthChecks", "--library", "-S", "Integration: Health Checks", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Health Checks", output);
        Assert.Contains("| `Microsoft.AspNetCore.Builder.HealthCheckApplicationBuilderExtensions.UseHealthChecks(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_HostingIntegration_ForHostedServiceRegistration_ShowsHostedServiceApi()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "App.Metrics.Extensions.Hosting", "--library", "-S", "Integration: Hosting", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Hosting", output);
        Assert.Contains("| `Microsoft.Extensions.DependencyInjection.ServiceCollectionMetricsReportingExtensions.AddMetricsReportingHostedService(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OpenApiIntegration_ForAnnotations_ShowsAnnotationSupport()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Swashbuckle.AspNetCore.Annotations", "--library", "-S", "Integration: OpenAPI", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: OpenAPI", output);
        Assert.Contains("| Annotation | `Swashbuckle.AspNetCore.Annotations.SwaggerOperationAttribute` |", output);
        Assert.Contains("| Configuration | `Microsoft.Extensions.DependencyInjection.AnnotationsSwaggerGenOptionsExtensions.EnableAnnotations(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OpenTelemetryIntegration_ForSerilogSink_ShowsOtlpLoggingApi()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Serilog.Sinks.OpenTelemetry", "--library", "-S", "Integration: OpenTelemetry", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: OpenTelemetry", output);
        Assert.Contains("| Logging | `Serilog.OpenTelemetryLoggerConfigurationExtensions.OpenTelemetry(...)` |", output);
        Assert.Contains("| OpenTelemetry | `Serilog.Sinks.OpenTelemetry.OpenTelemetrySinkOptions` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AuthenticationIntegration_ForOpenIddictValidation_ShowsValidationApi()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "OpenIddict.Validation.AspNetCore", "--library", "-S", "Integration: Authentication", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Authentication", output);
        Assert.Contains("| Validation | `Microsoft.Extensions.DependencyInjection.OpenIddictValidationAspNetCoreExtensions.UseAspNetCore(...)` |", output);
        Assert.Contains("| Validation | `OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreHandler` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AuthenticationIntegration_ForBlazorAuthorization_ShowsAuthenticationStateApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.AspNetCore.Components.Authorization", "--library", "-S", "Integration: Authentication", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Authentication", output);
        Assert.Contains("| Authentication State | `Microsoft.Extensions.DependencyInjection.CascadingAuthenticationStateServiceCollectionExtensions.AddCascadingAuthenticationState(...)` |", output);
        Assert.Contains("| Authorization UI | `Microsoft.AspNetCore.Components.Authorization.AuthorizeView` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AuthenticationIntegration_ForGraphQlPackages_ShowsAuthorizationBuilderApis()
    {
        var (hotChocolateExit, hotChocolateOutput, hotChocolateError) = await RunAppAsync(
            "package", "HotChocolate.Authorization", "--library", "-S", "Integration: Authentication", "--rows", "20");
        var (graphQlExit, graphQlOutput, graphQlError) = await RunAppAsync(
            "package", "GraphQL.Authorization", "--library", "-S", "Integration: Authentication", "--rows", "20");

        Assert.Equal(0, hotChocolateExit);
        Assert.Contains("## Integration: Authentication", hotChocolateOutput);
        Assert.Contains("| Authorization | `Microsoft.Extensions.DependencyInjection.AuthorizeRequestExecutorBuilder.AddAuthorizationCore(...)` |", hotChocolateOutput);
        Assert.Contains("| Handler | `HotChocolate.Authorization.IAuthorizationHandler` |", hotChocolateOutput);
        Assert.DoesNotContain("Tip:", hotChocolateError);

        Assert.Equal(0, graphQlExit);
        Assert.Contains("## Integration: Authentication", graphQlOutput);
        Assert.Contains("| Authorization | `GraphQL.AuthorizationGraphQLBuilderExtensions.AddAuthorization(...)` |", graphQlOutput);
        Assert.Contains("| Requirement | `GraphQL.Authorization.IAuthorizationRequirement` |", graphQlOutput);
        Assert.DoesNotContain("Tip:", graphQlError);
    }

    [Fact]
    public async Task LibraryCommand_OpenTelemetrySection_ForDiagnosticSource_Renders()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Diagnostics.DiagnosticSource", "-S", "Integration: OpenTelemetry");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: OpenTelemetry", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_LibraryInfo_CountsStarterApiOnlyIntegrations()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "AWSSDK.Extensions.Bedrock.MEAI", "--library", "-S", "Library Info");

        Assert.Equal(0, exit);
        Assert.Contains("## Library Info", output);
        Assert.Contains("| Integrations | 1 |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverIntegrationsCategory_ListsRenderableIntegrationSections()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.AI", "--library", "-D", "@Integrations", "--table");

        Assert.Equal(0, exit);
        Assert.Contains("Integration: AI", output);
        Assert.Contains("Integration: Dependency Injection", output);
        Assert.DoesNotContain("Integration: Configuration", output);
        Assert.DoesNotContain("Integration: Logging", output);
        Assert.DoesNotContain("Integration: OpenTelemetry", output);
        Assert.DoesNotContain("Integration: Options", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_SelectIntegrationsCategory_RendersIntegrationSections()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.AI", "--library", "-S", "@Integrations", "--rows", "6");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: AI", output);
        Assert.Contains("## Integration: Dependency Injection", output);
        Assert.DoesNotContain("## Integration: Logging", output);
        Assert.DoesNotContain("## Integration: OpenTelemetry", output);
        Assert.DoesNotContain("## Integration: Options", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_SelectRetiredIntegrationsRollup_ResolvesToIntegrationsCategory()
    {
        // "Integrations" was a rollup section before the per-integration decomposition. It keeps
        // resolving as a category alias, exactly like the retired "Performance Triage" monolith.
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.AI", "--library", "-S", "Integrations", "--rows", "6");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("not found", error);
        Assert.Contains("## Integration: AI", output);
        Assert.Contains("## Integration: Dependency Injection", output);
    }

    [Fact]
    public async Task LibraryCommand_LoggingSection_ForLoggingAbstractions_Renders()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "Microsoft.Extensions.Logging.Abstractions", "-S", "Integration: Logging");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Logging", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AISection_DetectsAiCurrencyTypes()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.AI.Abstractions", "--library", "-S", "Integration: AI", "--rows", "80");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: AI", output);
        Assert.Contains("| Kind | Type |", output);
        Assert.DoesNotContain("| API |", output);
        Assert.Contains("| Chat | `Microsoft.Extensions.AI.IChatClient` |", output);
        Assert.Contains("| Embeddings | `Microsoft.Extensions.AI.IEmbeddingGenerator` |", output);
        Assert.Contains("| Tools | `Microsoft.Extensions.AI.AITool` |", output);
        Assert.DoesNotContain("Assembly Reference", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AISection_ForAspireOpenAI_ShowsStarterApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Aspire.OpenAI", "--library", "-S", "Integration: AI", "--rows", "40");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: AI", output);
        Assert.Contains("| Kind | API |", output);
        Assert.Contains("AspireOpenAIExtensions.AddOpenAIClient(...)", output);
        Assert.Contains("AspireOpenAIClientBuilderChatClientExtensions.AddChatClient(...)", output);
        Assert.Contains("AspireOpenAIClientBuilderEmbeddingGeneratorExtensions.AddEmbeddingGenerator(...)", output);
        Assert.Contains("Aspire.OpenAI.AspireOpenAIClientBuilder", output);
        Assert.Contains("Aspire.OpenAI.OpenAISettings", output);
        Assert.DoesNotContain("Microsoft.Extensions.AI.IChatClient", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AISection_ForMicrosoftExtensionsAIOpenAI_ShowsAdapterApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.AI.OpenAI", "--library", "-S", "@Integrations", "--rows", "40");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: AI", output);
        Assert.Contains("| Kind | API |", output);
        Assert.Contains("| Chat | `Microsoft.Extensions.AI.OpenAIClientExtensions.AsIChatClient(...)` |", output);
        Assert.Contains("| Embeddings | `Microsoft.Extensions.AI.OpenAIClientExtensions.AsIEmbeddingGenerator(...)` |", output);
        Assert.Contains("| Images | `Microsoft.Extensions.AI.OpenAIClientExtensions.AsIImageGenerator(...)` |", output);
        Assert.Contains("| Realtime | `Microsoft.Extensions.AI.OpenAIRealtimeClient` |", output);
        Assert.Contains("| Speech to Text | `Microsoft.Extensions.AI.OpenAIClientExtensions.AsISpeechToTextClient(...)` |", output);
        Assert.Contains("| Text to Speech | `Microsoft.Extensions.AI.OpenAIClientExtensions.AsITextToSpeechClient(...)` |", output);
        Assert.Contains("| Tools | `OpenAI.Responses.MicrosoftExtensionsAIResponsesExtensions.AsAITool(...)` |", output);
        Assert.DoesNotContain("Dependency Injection", output);
        Assert.DoesNotContain("Assembly Reference", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_IntegrationsCategory_ForAspireOpenAI_ShowsStarterIntegrations()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Aspire.OpenAI", "--library", "-S", "@Integrations", "--rows", "40");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: AI", output);
        Assert.Contains("## Integration: OpenTelemetry", output);
        Assert.Contains("## Integration: Hosting", output);
        Assert.DoesNotContain("## Integration: Aspire", output);
        Assert.DoesNotContain("## Integration: Dependency Injection", output);
        Assert.DoesNotContain("## Integration: Logging", output);
        Assert.DoesNotContain("## Integration: Options", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AspireSection_ForAspireHostingRedis_ShowsResourceCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Aspire.Hosting.Redis", "--library", "-S", "Integration: Aspire", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Aspire", output);
        Assert.Contains("| Kind | API |", output);
        Assert.Contains("| Resource Builder | `Aspire.Hosting.RedisBuilderExtensions.AddRedis(...)` |", output);
        Assert.Contains("| Resource | `Aspire.Hosting.ApplicationModel.RedisResource` |", output);
        Assert.DoesNotContain("IDistributedApplicationBuilder", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_IntegrationsCategory_ForAspireHostingRedis_RendersAspireSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Aspire.Hosting.Redis", "--library", "-S", "@Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Aspire", output);
        Assert.Contains("RedisBuilderExtensions.AddRedis(...)", output);
        Assert.DoesNotContain("Dependency Injection", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_HostingSection_ForAspireOpenAI_ShowsStarterApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Aspire.OpenAI", "--library", "-S", "Integration: Hosting");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Hosting", output);
        Assert.Contains("| API |", output);
        Assert.Contains("AspireOpenAIExtensions.AddOpenAIClient(...)", output);
        Assert.Contains("AspireOpenAIExtensions.AddKeyedOpenAIClient(...)", output);
        Assert.DoesNotContain("IHostApplicationBuilder", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OpenTelemetrySection_ForAspireKafka_ShowsTelemetryControls()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Aspire.Confluent.Kafka", "--library", "-S", "@Integrations", "--rows", "40");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: OpenTelemetry", output);
        Assert.Contains("| Kind | API |", output);
        Assert.Contains("| Metrics | `Aspire.Confluent.Kafka.KafkaConsumerSettings.DisableMetrics` |", output);
        Assert.Contains("| Metrics | `Aspire.Confluent.Kafka.KafkaProducerSettings.DisableMetrics` |", output);
        Assert.Contains("| Tracing | `Aspire.Confluent.Kafka.KafkaConsumerSettings.DisableTracing` |", output);
        Assert.Contains("| Tracing | `Aspire.Confluent.Kafka.KafkaProducerSettings.DisableTracing` |", output);
        Assert.DoesNotContain("OpenTelemetry.Instrumentation.ConfluentKafka", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_LoggingSection_DetectsLoggingPrimitives()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "Microsoft.Extensions.Logging.Abstractions", "-S", "Integration: Logging");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Logging", output);
        Assert.Contains("| Type |", output);
        Assert.Contains("| `Microsoft.Extensions.Logging.ILogger` |", output);
        Assert.DoesNotContain("| Kind |", output);
        Assert.Contains("Microsoft.Extensions.Logging.ILogger", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_LoggingSection_ForAwsLogger_ShowsProviderApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "AWS.Logger.AspNetCore", "--library", "-S", "@Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Logging", output);
        Assert.Contains("| API |", output);
        Assert.Contains("AWSLoggerBuilderExtensions.AddAWSProvider(...)", output);
        Assert.Contains("AWSLoggerFactoryExtensions.AddAWSProvider(...)", output);
        Assert.DoesNotContain("| Type |", output);
        Assert.DoesNotContain("AWSLoggerBuilderExtensions` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_LoggingSection_ForSerilog_ShowsProviderApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Serilog.Extensions.Logging", "--library", "-S", "Integration: Logging", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Logging", output);
        Assert.Contains("| API |", output);
        Assert.Contains("SerilogLoggingBuilderExtensions.AddSerilog(...)", output);
        Assert.Contains("SerilogLoggerFactoryExtensions.AddSerilog(...)", output);
        Assert.DoesNotContain("SerilogLoggingBuilderExtensions` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_DependencyInjectionSection_ShowsActionableTypesOnly()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.AI", "--library", "-S", "Integration: Dependency Injection");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Dependency Injection", output);
        Assert.Contains("| API |", output);
        Assert.Contains("ChatClientBuilderServiceCollectionExtensions.AddChatClient(...)", output);
        Assert.Contains("EmbeddingGeneratorBuilderServiceCollectionExtensions.AddEmbeddingGenerator(...)", output);
        Assert.DoesNotContain("| Kind |", output);
        Assert.DoesNotContain("Assembly Reference", output);
        Assert.DoesNotContain("Microsoft.Extensions.DependencyInjection.IServiceCollection", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_DependencyInjectionSection_ForAzureClients_ShowsServiceRegistrationApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.Azure", "--library", "-S", "Integration: Dependency Injection", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Dependency Injection", output);
        Assert.Contains("| API |", output);
        Assert.Contains("AzureClientServiceCollectionExtensions.AddAzureClients(...)", output);
        Assert.Contains("AzureClientServiceCollectionExtensions.AddAzureClientsCore(...)", output);
        Assert.DoesNotContain("AzureClientServiceCollectionExtensions` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_HealthChecksSection_ForSqlServer_ShowsHealthCheckBuilderApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "AspNetCore.HealthChecks.SqlServer", "--library", "-S", "@Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Dependency Injection", output);
        Assert.Contains("## Integration: Health Checks", output);
        Assert.Contains("| API |", output);
        Assert.Contains("SqlServerHealthCheckBuilderExtensions.AddSqlServer(...)", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AuthenticationSection_ForJwtBearer_ShowsSchemeCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.AspNetCore.Authentication.JwtBearer", "--library", "-S", "@Integrations", "--rows", "40");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Authentication", output);
        Assert.Contains("| Authentication | `Microsoft.Extensions.DependencyInjection.JwtBearerExtensions.AddJwtBearer(...)` |", output);
        Assert.Contains("| Configuration | `Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions` |", output);
        Assert.Contains("| Configuration | `Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AuthenticationSection_ForAuthenticationCore_ShowsMiddlewareCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.AspNetCore.Authentication", "--library", "-S", "Integration: Authentication", "--rows", "40");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Authentication", output);
        Assert.Contains("| Authentication | `Microsoft.Extensions.DependencyInjection.AuthenticationServiceCollectionExtensions.AddAuthentication(...)` |", output);
        Assert.Contains("| Middleware | `Microsoft.AspNetCore.Builder.AuthAppBuilderExtensions.UseAuthentication(...)` |", output);
        Assert.Contains("| Configuration | `Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AuthenticationSection_ForAuthorization_ShowsAuthorizationCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.AspNetCore.Authorization", "--library", "-S", "Integration: Authentication", "--rows", "40");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Authentication", output);
        Assert.Contains("| Authorization | `Microsoft.Extensions.DependencyInjection.AuthorizationServiceCollectionExtensions.AddAuthorizationCore(...)` |", output);
        Assert.Contains("| Builder | `Microsoft.AspNetCore.Authorization.AuthorizationBuilder` |", output);
        Assert.Contains("| Configuration | `Microsoft.AspNetCore.Authorization.AuthorizationOptions` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AuthenticationSection_ForAwsCognitoIdentity_ShowsIdentityCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Amazon.AspNetCore.Identity.Cognito", "--library", "-S", "@Integrations", "--rows", "30");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Authentication", output);
        Assert.Contains("| API |", output);
        Assert.Contains("CognitoServiceCollectionExtensions.AddCognitoIdentity(...)", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OpenApiSection_ForSwashbuckle_ShowsOpenApiCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Swashbuckle.AspNetCore.Swagger", "--library", "-S", "@Integrations", "--rows", "30");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: OpenAPI", output);
        Assert.Contains("| Configuration | `Swashbuckle.AspNetCore.Swagger.SwaggerOptions` |", output);
        Assert.Contains("| Endpoint | `Microsoft.AspNetCore.Builder.SwaggerBuilderExtensions.MapSwagger(...)` |", output);
        Assert.Contains("| Middleware | `Microsoft.AspNetCore.Builder.SwaggerBuilderExtensions.UseSwagger(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OpenApiSection_ForMicrosoftOpenApi_ShowsServiceAndEndpointApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.AspNetCore.OpenApi", "--library", "-S", "Integration: OpenAPI", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: OpenAPI", output);
        Assert.Contains("| Configuration | `Microsoft.AspNetCore.OpenApi.OpenApiOptions` |", output);
        Assert.Contains("| Endpoint | `Microsoft.AspNetCore.Builder.OpenApiEndpointRouteBuilderExtensions.MapOpenApi(...)` |", output);
        Assert.Contains("| Service Registration | `Microsoft.Extensions.DependencyInjection.OpenApiServiceCollectionExtensions.AddOpenApi(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AspNetCoreSection_ForSerilog_ShowsMiddlewareCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Serilog.AspNetCore", "--library", "-S", "Integration: ASP.NET Core", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: ASP.NET Core", output);
        Assert.Contains("| Kind | API |", output);
        Assert.Contains("| Configuration | `Serilog.AspNetCore.RequestLoggingOptions` |", output);
        Assert.Contains("| Middleware | `Serilog.SerilogApplicationBuilderExtensions.UseSerilogRequestLogging(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AspNetCoreSection_ForHangfire_ShowsEndpointAndMiddlewareCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Hangfire.AspNetCore", "--library", "-S", "Integration: ASP.NET Core", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: ASP.NET Core", output);
        Assert.Contains("| Endpoint | `Hangfire.HangfireEndpointRouteBuilderExtensions.MapHangfireDashboard(...)` |", output);
        Assert.Contains("| Middleware | `Hangfire.HangfireApplicationBuilderExtensions.UseHangfireDashboard(...)` |", output);
        Assert.Contains("| Middleware | `Hangfire.HangfireApplicationBuilderExtensions.UseHangfireServer(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AspNetCoreSection_ForGrpc_ShowsEndpointCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Grpc.AspNetCore.Server", "--library", "-S", "@Integrations", "--rows", "30");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: ASP.NET Core", output);
        Assert.Contains("| Endpoint | `Microsoft.AspNetCore.Builder.GrpcEndpointRouteBuilderExtensions.MapGrpcService(...)` |", output);
        Assert.Contains("## Integration: Dependency Injection", output);
        Assert.Contains("GrpcServicesExtensions.AddGrpc(...)", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AspNetCoreSection_ForAzureDataProtectionBlobs_ShowsDataProtectionCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Azure.Extensions.AspNetCore.DataProtection.Blobs@1.5.3", "--all-libraries", "-S", "Integration: ASP.NET Core", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: ASP.NET Core", output);
        Assert.Contains("| API |", output);
        Assert.Contains("Microsoft.AspNetCore.DataProtection.AzureStorageBlobDataProtectionBuilderExtensions.PersistKeysToAzureBlobStorage(...)", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AspNetCoreSection_ForAzureDataProtectionKeys_ShowsDataProtectionCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Azure.Extensions.AspNetCore.DataProtection.Keys@1.6.3", "--all-libraries", "-S", "Integration: ASP.NET Core", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: ASP.NET Core", output);
        Assert.Contains("| API |", output);
        Assert.Contains("Microsoft.AspNetCore.DataProtection.AzureDataProtectionKeyVaultKeyBuilderExtensions.ProtectKeysWithAzureKeyVault(...)", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_HostingSection_ForMassTransit_ShowsHostBuilderApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "MassTransit", "--library", "-S", "Integration: Hosting", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Hosting", output);
        Assert.Contains("| API |", output);
        Assert.Contains("DependencyInjectionHostingExtensions.UseMassTransit(...)", output);
        Assert.Contains("DependencyInjectionHostingExtensions.UseMediator(...)", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OpenTelemetrySection_ForAzureMonitorExporter_ShowsBuilderApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Azure.Monitor.OpenTelemetry.Exporter", "--library", "-S", "Integration: OpenTelemetry", "--rows", "30");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: OpenTelemetry", output);
        Assert.Contains("| Logging | `Azure.Monitor.OpenTelemetry.Exporter.AzureMonitorExporterExtensions.AddAzureMonitorLogExporter(...)` |", output);
        Assert.Contains("| Metrics | `Azure.Monitor.OpenTelemetry.Exporter.AzureMonitorExporterExtensions.AddAzureMonitorMetricExporter(...)` |", output);
        Assert.Contains("| OpenTelemetry | `Azure.Monitor.OpenTelemetry.Exporter.OpenTelemetryBuilderExtensions.UseAzureMonitorExporter(...)` |", output);
        Assert.Contains("| Tracing | `Azure.Monitor.OpenTelemetry.Exporter.AzureMonitorExporterExtensions.AddAzureMonitorTraceExporter(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_HttpClientDiagnostics_ShowsUserFacingHttpClientCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.Http.Diagnostics", "--library", "-S", "@Integrations", "--rows", "30");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("OpenTelemetry", output);
        Assert.Contains("## Integration: HTTP Client", output);
        Assert.Contains("| Kind | API |", output);
        var diagnosticsRow = "| HTTP Diagnostics | `Microsoft.Extensions.Http.Diagnostics.HttpDependencyMetadataResolver` |";
        var latencyRow = "| HTTP Latency | `Microsoft.Extensions.DependencyInjection.HttpClientLatencyTelemetryExtensions.AddHttpClientLatencyTelemetry(...)` |";
        var loggingRow = "| HTTP Logging | `Microsoft.Extensions.DependencyInjection.HttpClientLoggingHttpClientBuilderExtensions.AddExtendedHttpClientLogging(...)` |";
        Assert.Contains(diagnosticsRow, output);
        Assert.Contains(latencyRow, output);
        Assert.Contains(loggingRow, output);
        Assert.True(output.IndexOf(diagnosticsRow, StringComparison.Ordinal)
            < output.IndexOf(latencyRow, StringComparison.Ordinal));
        Assert.True(output.IndexOf(latencyRow, StringComparison.Ordinal)
            < output.IndexOf(loggingRow, StringComparison.Ordinal));
        Assert.Contains("| HTTP Logging | `Microsoft.Extensions.DependencyInjection.HttpClientLoggingHttpClientBuilderExtensions.AddExtendedHttpClientLogging(...)` |", output);
        Assert.Contains("HttpClientLoggingHttpClientBuilderExtensions.AddExtendedHttpClientLogging(...)", output);
        Assert.Contains("| HTTP Logging | `Microsoft.Extensions.Http.Logging.LoggingOptions` |", output);
        Assert.Contains("Microsoft.Extensions.Http.Logging.IHttpClientLogEnricher", output);
        Assert.Contains("Microsoft.Extensions.Http.Logging.LoggingOptions", output);
        Assert.DoesNotContain("Microsoft.Extensions.Telemetry.Internal", output);
        Assert.DoesNotContain("| `Microsoft.Extensions.DependencyInjection.HttpClientLoggingServiceCollectionExtensions` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OpenTelemetrySection_DetectsDiagnosticSourcePrimitives()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Diagnostics.DiagnosticSource", "-S", "Integration: OpenTelemetry");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: OpenTelemetry", output);
        Assert.Contains("| Kind | Type |", output);
        Assert.Contains("| Tracing | `System.Diagnostics.ActivitySource` |", output);
        Assert.Contains("| Metrics | `System.Diagnostics.Metrics.Meter` |", output);
        Assert.Contains("| Metrics | `System.Diagnostics.Metrics.UpDownCounter<T>` |", output);
        Assert.Contains("System.Diagnostics.ActivitySource", output);
        Assert.Contains("System.Diagnostics.Metrics.Meter", output);
        Assert.DoesNotContain("UpDownCounter&#96;1", output);
        Assert.DoesNotContain("Tip:", error);
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

    private static List<(string Name, string Kind)> ExtractDiscoveryRows(string output)
    {
        List<(string Name, string Kind)> rows = [];
        foreach (var line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
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

    [Fact]
    public async Task LibraryCommand_DetailedOutput_RendersSectionsAlphabetically()
    {
        var (exit, output, _) = await RunAppAsync("library", "System.Text.Json", "-v:d");

        Assert.Equal(0, exit);

        var sectionHeaders = output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
            .Select(line => line[3..])
            .ToArray();

        Assert.NotEmpty(sectionHeaders);

        // Every section renders in a single alphabetical order — there is no trailing cluster.
        // The kind-scoped "Performance:" buckets sort among the rest by their full heading (so
        // they still group under the shared prefix, now in alpha position, not pinned to the end).
        Assert.Equal(
            sectionHeaders.OrderBy(h => h, StringComparer.OrdinalIgnoreCase).ToArray(),
            sectionHeaders);
    }

    [Fact]
    public async Task LibraryCommand_CountMap_RendersSectionsAlphabetically()
    {
        // The --count section map must follow the same single alphabetical order as the rendered
        // sections; it previously used registration order via AllSectionNames.
        var (exit, output, _) = await RunAppAsync("library", "System.Text.Json", "--count", "-S", "@Performance");

        Assert.Equal(0, exit);

        var sections = output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("| ", StringComparison.Ordinal)
                && !line.StartsWith("| Section ", StringComparison.Ordinal)
                && !line.StartsWith("| ---", StringComparison.Ordinal))
            .Select(line => line.Split('|')[1].Trim())
            .ToArray();

        Assert.NotEmpty(sections);
        Assert.Equal(
            sections.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray(),
            sections);
    }

    [Fact]
    public async Task Assembly_Signals_LocalUnsafeAssembly_FocusesOnNewMemorySafetyModel()
    {
        var options = new LibraryOptions
        {
            AssemblyName = TestAssemblyPath,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Signals" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Memory safety | Memory safety model | Not marked |", output);
        Assert.Contains("| Memory safety | RequiresUnsafe members | 0 | RequiresUnsafeAttribute |", output);
        Assert.DoesNotContain("Legacy /unsafe", output);
        Assert.Contains("| Interop | P/Invoke methods | 2 | all PInvokeImpl metadata |", output);
    }

    [Fact]
    public async Task LibraryCommand_Signals_ShowsSignalsOnly()
    {
        var (exit, output, error) = await RunAppAsync("library", TestAssemblyPath, "-S", "Signals");

        Assert.Equal(0, exit);
        Assert.Contains("## Signals", output);
        Assert.DoesNotContain("Source audit", output);
        Assert.DoesNotContain("Legacy /unsafe", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_PlatformSignals_DownloadsPdbByDefault()
    {
        var (exit, output, _) = await RunAppAsync("library", "System.Text.Json", "-S", "Signals");

        Assert.Equal(0, exit);
        Assert.Contains("## Signals", output);
        // Signals authorizes PDB acquisition: SourceLink resolves.
        Assert.DoesNotContain("PDB not checked", output);
    }

    [Fact]
    public async Task LibraryCommand_Signals_ChecksSymbolsByDefault()
    {
        var (exit, output, error) = await RunAppAsync("library", TestAssemblyPath, "-S", "Signals");

        Assert.Equal(0, exit);
        Assert.Contains("## Signals", output);
        Assert.DoesNotContain("PDB not checked", output);
        Assert.DoesNotContain("Source audit", output);
        Assert.DoesNotContain("| Signals | Scope |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_ExplicitSourceLinkAudit_IncludesSourceLinkAuditSection()
    {
        // Target a platform assembly with known SourceLink data so the audit deterministically
        // renders. The local test assembly's SourceLink presence is environment-dependent
        // (SDK 8+ auto-enables SourceLink only when building inside a git repo), so it cannot
        // reliably exercise the section (#675).
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Signals,SourceLink: Availability,SourceLink: Missing Files");

        Assert.Equal(0, exit);
        Assert.Contains("## Signals", output);
        Assert.Contains("## SourceLink: Availability", output);
        Assert.Contains("Source Files", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_PlatformVersion_UsesPlatformRuntimeRoute()
    {
        // Decouple from the host's running runtime version (#1256). Probe an installed
        // shared runtime version the resolver can find rather than binding to wherever
        // the test host's System.Private.CoreLib happens to live, which fails on
        // preview/self-contained hosts whose running version isn't a discoverable
        // shared framework.
        var (_, installedVersion, frameworkError) = PlatformResolver.ResolveRuntimeFramework("runtime");
        Assert.SkipWhen(
            installedVersion is null,
            $"No installed Microsoft.NETCore.App shared runtime found: {frameworkError}");

        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "--version", installedVersion!, "-v:q");

        Assert.Equal(0, exit);
        Assert.Contains("Source: Platform", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task Package_DiscoverSection_ListsPackageInfoFields()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Published.PackageInfoDiscovery",
            "README.md",
            "# Test package");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-D", "Package Info", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("| Authors | field |", output);
            Assert.Contains("| Version | field |", output);
            Assert.DoesNotContain("| Published | field |", output);

            var (projectExit, projected, projectError) = await RunAppAsync(
                "package", packagePath, "-S", "Package Info", "--fields", "Authors,Version", "-v:q", "--tips", "q");

            Assert.Equal(0, projectExit);
            Assert.DoesNotContain("not found", projectError);
            Assert.Contains("Authors", projected);
            Assert.Contains("tests", projected);
            Assert.Contains("Version", projected);
            Assert.Contains("1.0.0", projected);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_DiscoverSchema_ListsPublishedPackageInfoField()
    {
        var (exit, output, error) = await RunAppAsync("package", "-D", "Package Info", "--schema", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("| Published | field |", output);
    }

    [Fact]
    public async Task Package_DiscoverSection_ListsSignalsColumns()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-D", "Signals", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.DoesNotContain("not found", error);
            Assert.Contains("| Area | column |", output);
            Assert.Contains("| Signal | column |", output);
            Assert.Contains("| Value | column |", output);
            Assert.Contains("| Evidence | column |", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_DiscoverTree_UsesDiscoveryTreeNotFileTree()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.DiscoveryTree",
            "README.md",
            "# Test package");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-D", "--tree", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("Package Info", output);
            Assert.Contains("Authors", output);
            Assert.DoesNotContain("README.md", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_TreeWithoutDiscovery_ReportsLayoutAlternative()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.TreeAlias",
            "README.md",
            "# Test package");
        try
        {
            var (exit, _, error) = await RunAppAsync("package", packagePath, "--tree", "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Contains("requires -D/--discover", error);
            Assert.Contains("Use --layout", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_LibraryFlag_BareSelectsUnambiguousLibrary()
    {
        var (packagePath, tempDir) = CreateLocalPrimaryLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--library", "-S", "Library Info");

            Assert.Equal(0, exit);
            Assert.Contains("# Test.Primary.dll", output);
            Assert.Contains("## Library Info", output);
            Assert.DoesNotContain("## Package Info", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_LibraryFlag_ExplicitSelectsLibrary()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--library", "Latest.Two.dll", "-S", "Library Info");

            Assert.Equal(0, exit);
            Assert.Contains("# Latest.Two.dll", output);
            Assert.Contains("## Library Info", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_LibraryFlag_BareReportsAmbiguousLibraries()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "--library");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("contains multiple libraries", error);
            Assert.Contains("lib/net10.0/Latest.One.dll", error);
            Assert.Contains("lib/net10.0/Latest.Two.dll", error);
            Assert.Contains("dotnet-inspect package", error);
            Assert.Contains("--library <dll>", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_RejectsHiddenRenderSelector()
    {
        // The embedded-library render path resolves -S against the same curated LibrarySections
        // pipeline, so it enforces the same @Hidden discovery-only guard as the direct library
        // command: -S @Hidden must be rejected before any resolution/fetch, while exact-name
        // render of a @Hidden member stays allowed.
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, _, error) = await RunAppAsync(
                "package", packagePath, "--all-libraries", "-S", "@Hidden", "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Contains("discovery-only", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_TreeSectionCountSaysItCannotBeProjected()
    {
        // The all-libraries render path counts the same row projection as the direct library
        // command, so a tree-shaped section must explain its zero there too rather than looking
        // like a successful empty count.
        var (exit, output, error) = await RunAppAsync(
            "package", "Newtonsoft.Json@13.0.3", "--all-libraries", "-S", "Dependencies",
            "--count", "--tips", "q");

        if (exit != 0)
        {
            Assert.Skip($"Newtonsoft.Json@13.0.3 not available offline: {error}");
            return;
        }

        Assert.Contains("cannot be projected to rows", error);
        Assert.DoesNotContain("cannot be projected to rows", output);
    }

    [Fact]
    public async Task PackageCommand_DependenciesLens_CountAnswersTheGraphsRowLowering()
    {
        // #3406. --dependencies used to exit 1 telling the caller to select a section, which did
        // not describe what they asked for. Its payload is a graph, and a graph's row lowering is
        // one row per dependency edge -- which is exactly the set of lines the rendered tree
        // prints below its root line. Derive the expectation from the rendering rather than
        // pinning a literal, so the count and the tree cannot drift apart.
        string[] baseArgs = ["package", "Microsoft.Extensions.Logging@9.0.0", "--dependencies", "--tips", "q"];

        var (treeExit, treeOutput, treeError) = await RunAppAsync(baseArgs);
        if (treeExit != 0)
        {
            Assert.Skip($"Microsoft.Extensions.Logging@9.0.0 not available offline: {treeError}");
            return;
        }

        var treeLines = treeOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains('\u251c') || line.Contains('\u2514'))
            .Count();
        Assert.True(treeLines > 0, "fixture package must render a non-empty dependency tree");

        var (countExit, countOutput, _) = await RunAppAsync([.. baseArgs, "--count"]);
        Assert.Equal(0, countExit);
        Assert.Equal(treeLines, int.Parse(countOutput.Trim(), CultureInfo.InvariantCulture));

        // The transitive graph is deeper than its first tier, so this must not be the root count.
        Assert.True(treeLines > treeOutput
            .Split('\n')
            .Count(line => line.StartsWith('\u251c') || line.StartsWith('\u2514')));
    }

    [Fact]
    public async Task PackageCommand_DependenciesLens_CountIsSeparateFromTheDependenciesSection()
    {
        // The flat Dependencies section lists declared dependencies across every TFM group; the
        // lens resolves the transitive graph of one group. They are different questions, so their
        // counts are allowed to differ and neither may be answered with the other's number.
        var (lensExit, lensOutput, lensError) = await RunAppAsync(
            "package", "Microsoft.Extensions.Logging@9.0.0", "--dependencies", "--count", "--tips", "q");
        if (lensExit != 0)
        {
            Assert.Skip($"Microsoft.Extensions.Logging@9.0.0 not available offline: {lensError}");
            return;
        }

        var (sectionExit, sectionOutput, _) = await RunAppAsync(
            "package", "Microsoft.Extensions.Logging@9.0.0", "-S", "Dependencies", "--count", "--tips", "q");
        Assert.Equal(0, sectionExit);

        Assert.NotEqual(sectionOutput.Trim(), lensOutput.Trim());
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_RendersLibraryInfoPerHighestTfmLibrary()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--all-libraries", "-S", "Library Info", "--rows", "20");

            Assert.Equal(0, exit);
            Assert.Contains("## Library Info (lib/net10.0/Latest.One.dll)", output);
            Assert.Contains("## Library Info (lib/net10.0/Latest.Two.dll)", output);
            Assert.DoesNotContain("## Library Info (lib/net8.0/Older.dll)", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_TfmAllIncludesEveryTfmLibrary()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--all-libraries", "--tfm", "all", "-S", "Library Info", "--rows", "12");

            Assert.Equal(0, exit);
            Assert.Contains("## Library Info (lib/net8.0/Older.dll)", output);
            Assert.Contains("## Library Info (lib/net10.0/Latest.One.dll)", output);
            Assert.Contains("## Library Info (lib/net10.0/Latest.Two.dll)", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_TfmAll_PerformanceGroupTabular_IsKindLabeledPerAssembly()
    {
        // Regression: the multi-assembly renderer (WriteLibraryResults, reached via `library --tfm
        // all` when a library ships under several TFMs) must apply the same @Performance flattening
        // as the single-assembly path — each assembly emits one self-describing Kind-labeled table,
        // never per-kind headers without a Kind column.
        var tempDir = Path.Combine(Path.GetTempPath(), $"perf-multitfm-{Guid.NewGuid():N}");
        try
        {
            var content = Path.Combine(tempDir, "content");
            foreach (var tfm in new[] { "net8.0", "net10.0" })
            {
                var dir = Path.Combine(content, "lib", tfm);
                Directory.CreateDirectory(dir);
                File.Copy(TestAssemblyPath, Path.Combine(dir, "Lib.dll"));
            }
            var packagePath = Path.Combine(tempDir, "Perf.MultiTfm.1.0.0.nupkg");
            ZipFile.CreateFromDirectory(content, packagePath);

            var (exit, output, _) = await RunAppAsync(
                "library", "Lib.dll", "--package", packagePath, "--tfm", "all",
                "-S", "@Performance", "--tsv", "--rows", "3", "--tips", "q");

            Assert.Equal(0, exit);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            // One flattened Kind-labeled table per TFM assembly; no bare per-kind "member\t" header.
            Assert.Contains(lines, l => l.StartsWith("kind\tmember\t", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, l => l.StartsWith("member\t", StringComparison.Ordinal));
            var kindHeaders = lines.Count(l => l.StartsWith("kind\tmember\t", StringComparison.Ordinal));
            Assert.Equal(2, kindHeaders);
            // Every data row is self-describing: its first field is a real kind label.
            var kindLabels = PerformanceKinds.Sections.Select(PerformanceKinds.KindLabel).ToHashSet(StringComparer.Ordinal);
            var dataRows = lines.Where(l => !l.StartsWith("kind\t", StringComparison.Ordinal)).ToArray();
            Assert.NotEmpty(dataRows);
            Assert.All(dataRows, l => Assert.Contains(l.Split('\t')[0], kindLabels));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_AggregatesIntegrationsWithLibraryProvenance()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage(
            "Microsoft.Extensions.Configuration",
            "Microsoft.Extensions.Configuration.Json");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--all-libraries", "-S", "@Integrations", "--rows", "40");

            Assert.Equal(0, exit);
            Assert.Contains("## Integration: Configuration", output);
            Assert.Contains("| Library | Kind | API |", output);
            Assert.Contains("Microsoft.Extensions.Configuration.dll", output);
            Assert.Contains("Microsoft.Extensions.Configuration.Json.dll", output);
            Assert.Contains("Microsoft.Extensions.Configuration.JsonConfigurationExtensions.AddJsonFile(...)", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_TsvEmitsIntegrationRowsWithLibraryProvenance()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage(
            "Microsoft.Extensions.Configuration",
            "Microsoft.Extensions.Configuration.Json");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--all-libraries", "-S", "Integration: Configuration", "--tsv");

            Assert.Equal(0, exit);
            Assert.Contains("package\tversion\tlibrary\ttfm\tkind\tapi", output);
            Assert.Contains("Microsoft.Extensions.Configuration.dll", output);
            Assert.Contains("Microsoft.Extensions.Configuration.Json.dll", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_TsvRejectsIntegrationCategory()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage(
            "Microsoft.Extensions.Configuration",
            "Microsoft.Extensions.Configuration.Json");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--all-libraries", "-S", "@Integrations", "--tsv");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("requires one concrete section", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_JsonlEmitsIntegrationRowsWithLibraryProvenance()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage(
            "Microsoft.Extensions.Configuration",
            "Microsoft.Extensions.Configuration.Json");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--all-libraries", "-S", "Integration: Configuration", "--jsonl");

            Assert.Equal(0, exit);
            var documents = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonDocument.Parse(line))
                .ToArray();
            Assert.Contains(documents, document =>
                document.RootElement.GetProperty("library").GetString()?.EndsWith("Microsoft.Extensions.Configuration.Json.dll", StringComparison.Ordinal) == true);
            Assert.All(documents, document =>
                Assert.False(document.RootElement.TryGetProperty("section", out _)));
            Assert.DoesNotContain("Tip:", error);

            foreach (var document in documents)
                document.Dispose();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_AllLibraries_CannotCombineWithLibrary()
    {
        var (packagePath, tempDir) = CreateLocalPrimaryLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--all-libraries", "--library");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("--all-libraries cannot be combined with --library", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Router_LibraryFlag_RoutesPackageToLibraryInspection()
    {
        var (packagePath, tempDir) = CreateLocalPrimaryLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(packagePath, "--library", "-S", "Library Info");

            Assert.Equal(0, exit);
            Assert.Contains("# Test.Primary.dll", output);
            Assert.Contains("## Library Info", output);
            Assert.DoesNotContain("## Package Info", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Router_FacadePlatformBareName_RoutesToForwardedType()
    {
        var (assemblyPath, _, _, error) = PlatformResolver.ResolveAssembly("System.Runtime.CompilerServices.Unsafe");
        if (assemblyPath == null || error != null)
        {
            Assert.Skip($"System.Runtime.CompilerServices.Unsafe not available: {error}");
            return;
        }

        Assert.SkipUnless(PlatformResolver.IsFacadeOnlyAssembly(assemblyPath),
            "System.Runtime.CompilerServices.Unsafe is not facade-only in this runtime.");

        var (exit, output, runError) = await RunAppAsync(
            "System.Runtime.CompilerServices.Unsafe", "--markdown", "-v:q", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(runError);
        Assert.Contains("# System.Runtime.CompilerServices.Unsafe", output);
        Assert.DoesNotContain("# System.Runtime.CompilerServices.Unsafe.dll", output);
        Assert.Contains("Kind: class", output);
    }

    [Fact]
    public async Task Router_NonFacadePlatformBareName_RoutesToLibrary()
    {
        var (assemblyPath, _, _, error) = PlatformResolver.ResolveAssembly("System.Text.Json");
        if (assemblyPath == null || error != null)
        {
            Assert.Skip($"System.Text.Json not available: {error}");
            return;
        }

        Assert.False(PlatformResolver.IsFacadeOnlyAssembly(assemblyPath));

        var (exit, output, runError) = await RunAppAsync(
            "System.Text.Json", "--markdown", "-v:q", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(runError);
        Assert.Contains("# System.Text.Json.dll", output);
        Assert.Contains("Name: System.Text.Json", output);
    }

    [Fact]
    public async Task LibraryCommand_PlatformVersion_DoesNotFallbackToPackageVersion()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "--version", "0.0.0-definitely-not-installed", "-S", "Signals");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("not found", error);
    }

    // ── package command ──────────────────────────────────────────────

    [Fact]
    public async Task Package_NonexistentPackage_ShowsError()
    {
        var options = new InspectionOptions
        {
            PackageArgs = ["NonexistentPackage123456"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task Package_Detailed_DoesNotShowSignalsByDefault()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var options = new InspectionOptions
            {
                PackageArgs = [packagePath],
                Verbosity = Verbosity.Detailed
            };

            var (exit, output, error) = await ConsoleCapture.RunAsync(
                () => PackageCommand.ExecuteAsync(options));

            Assert.Equal(0, exit);
            Assert.DoesNotContain("## Signals", output);
            Assert.Contains("## Manifest", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Discover_DefaultsToEffectiveSchema()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-D");

            Assert.Equal(0, exit);
            Assert.Contains("Package Info", output);
            Assert.Contains("| Signals | section |", output);
            Assert.Contains("Manifest", output);
            // SourceLink: Files is reachable through its door rather than the top-level
            // catalog, so the door is what discovery has to advertise.
            Assert.Contains("| @SourceLink | category |", output);
            Assert.DoesNotContain("| SourceLink: Files | section |", output);
            Assert.DoesNotContain("Vulnerabilities", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Discover_OrdersRowsByDiscoveryGroup()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Discovery",
            "README.md",
            "# Test package");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-D", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            var rows = ExtractDiscoveryRows(output);

            Assert.Contains(rows, row => row.Name == "Package files" && row.Kind == "section");

            var regular = rows.Where(row => row.Kind == "section").Select(row => row.Name).ToArray();
            var categories = rows.Where(row => row.Kind == "category").Select(row => row.Name).ToArray();

            Assert.Equal(regular.OrderBy(name => name, StringComparer.OrdinalIgnoreCase), regular);
            Assert.Equal(categories.OrderBy(name => name, StringComparer.OrdinalIgnoreCase), categories);
            // Curated catalogs lead with the topical doors, then the sections, and no longer
            // annotate rows as opt-in: the size-class and cost axes carry that instead.
            Assert.True(rows.FindLastIndex(row => row.Kind == "category") < rows.FindIndex(row => row.Kind == "section"));
            Assert.DoesNotContain(rows, row => row.Kind == "section (opt-in)");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_DiscoverSchema_ListsStaticSchema()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-D", "--schema");

            Assert.Equal(0, exit);
            Assert.Contains("Package Info", output);
            Assert.Contains("Signals", output);
            Assert.Contains("SourceLink: Files", output);
            Assert.Contains("Manifest", output);
            Assert.Contains("Vulnerabilities", output);
            // @All/@Default/@Hidden are internal computed poles, not doors: curated discovery
            // advertises only the real category doors.
            Assert.Contains("| @Files | category |", output);
            Assert.Contains("| @SourceLink | category |", output);
            Assert.DoesNotContain("@All", output);
            Assert.DoesNotContain("@Default", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_SourceFilesSection_RendersLibraryTypeUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "System.CommandLine",
            "-S", "Source Files", "--tips", "q", "-n", "18");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## SourceLink: Files", output);
        Assert.Contains("| Library | Type | Url |", output);
        Assert.Contains("lib/net8.0/System.CommandLine.dll", output);
        Assert.Contains("System.CommandLine.Command", output);
        Assert.Contains("Command.cs", output);
    }

    [Fact]
    public async Task Package_SourceFilesSection_TypeFilterAndBlobUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Newtonsoft.Json",
            "-S", "Source Files", "-t", "JsonConvert", "--blob", "--tsv", "--no-headers", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("lib/net6.0/Newtonsoft.Json.dll\tNewtonsoft.Json.JsonConvert\t", output);
        Assert.Contains("github.com/JamesNK/Newtonsoft.Json/blob/", output);
        Assert.DoesNotContain("Newtonsoft.Json.JsonSerializer\t", output);
    }

    [Fact]
    public async Task Package_SourceFilesSection_Bare_EmitsUrlColumn()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "-t", "JsonReader", "--bare", "--raw", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var lines = output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains(lines, line => line.EndsWith("/Src/Newtonsoft.Json/JsonReader.cs", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.EndsWith("/Src/Newtonsoft.Json/JsonReader.Async.cs", StringComparison.Ordinal));
        Assert.All(lines, line => Assert.StartsWith("https://raw.githubusercontent.com/JamesNK/Newtonsoft.Json/", line));
    }

    [Fact]
    public async Task Package_LibrarySourceFilesSection_PreservesTypeFilterAndBlobUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Newtonsoft.Json", "--library",
            "-S", "Source Files", "-t", "JsonConvert", "--blob", "--tsv", "--no-headers", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("Name: Newtonsoft.Json", output);
        Assert.Contains("Newtonsoft.Json.JsonConvert\t", output);
        Assert.Contains("github.com/JamesNK/Newtonsoft.Json/blob/", output);
        Assert.DoesNotContain("Newtonsoft.Json.JsonSerializer\t", output);
    }

    [Fact]
    public async Task Package_SourceFilesSection_NewtonsoftJson_DoesNotBleedJTokenRowsAcrossTypes()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Newtonsoft.Json",
            "-S", "Source Files", "--tsv", "--no-headers", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);

        var rows = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t'))
            .Where(cells => cells.Length >= 3)
            .Select(cells => (Library: cells[0], Type: cells[1], Url: cells[2]))
            .ToList();

        var jTokenRows = rows.Where(row => row.Type == "Newtonsoft.Json.Linq.JToken").ToArray();
        Assert.NotEmpty(jTokenRows);
        Assert.All(jTokenRows, row => Assert.Contains("/Linq/JToken", row.Url));
        Assert.DoesNotContain(jTokenRows, row => row.Url.Contains("JTokenReader.cs", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(jTokenRows, row => row.Url.Contains("JValue.cs", StringComparison.OrdinalIgnoreCase));

        var jTokenReaderRows = rows.Where(row => row.Type == "Newtonsoft.Json.Linq.JTokenReader").ToArray();
        Assert.NotEmpty(jTokenReaderRows);
        Assert.All(jTokenReaderRows, row => Assert.Contains("/Linq/JTokenReader.cs", row.Url));
    }

    [Fact]
    public async Task Package_BareSelect_RendersInfoPreset()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S");

            Assert.Equal(0, exit);
            Assert.Contains("## Package Info", output);
            Assert.Contains("## Manifest", output);
            // Package-growing sections stay out of the fixed overview...
            Assert.DoesNotContain("## Dependencies", output);
            Assert.DoesNotContain("## Target Frameworks", output);
            Assert.DoesNotContain("## Package files", output);
            // ...as do the network-bound ones, however small their row set.
            Assert.DoesNotContain("## Signals", output);
            Assert.DoesNotContain("## Statistics", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Member_BareSelect_RendersInfoPreset()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Text.Json.JsonSerializer.SerializeToNode:1", "-S");

        Assert.Equal(0, exit);
        Assert.Contains("## Signature", output);
        Assert.Contains("## Decompiled Source", output);
        Assert.DoesNotContain("## IL", output);
        Assert.DoesNotContain("## Original Source", output);
        Assert.True(output.IndexOf("## Signature", StringComparison.Ordinal) < output.IndexOf("## Decompiled Source", StringComparison.Ordinal));
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task MemberList_BareSelect_RendersCompactSummaryPreset()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Text.Json.JsonSerializer", "-S");

        Assert.Equal(0, exit);
        Assert.Contains("## Properties", output);
        Assert.Contains("## Method Groups", output);
        Assert.Contains("| Name | Return Type | Overloads |", output);
        Assert.Contains("| SerializeToNode | System.Text.Json.Nodes.JsonNode? | 5 |", output);
        Assert.DoesNotContain("| Name | Signature | Description |", output);
        Assert.DoesNotContain("## Decompiled Source", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task MemberList_ExplicitPackageQualifiedType_DoesNotSplitTrailingTypeName()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Text.Json.JsonSerializer", "--package", "System.Text.Json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Type 'System.Text.Json' not found", error);
        Assert.Contains("# System.Text.Json.JsonSerializer", output);
        Assert.Contains("## Method Groups", output);
    }

    [Fact]
    public async Task MemberList_QualifiedPlatformTypeTypo_SuggestsType()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Text.Json.JsonSerializizer", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Type 'JsonSerializizer' not found.", error);
        Assert.Contains("System.Text.Json.JsonSerializer", error);
        Assert.DoesNotContain("member requires a type name", error);
    }

    [Fact]
    public async Task Member_FilteredGenericMethod_RendersMethodTypeParameters()
    {
        var (exit, output, _) = await RunAppAsync(
            "member", "System.Text.Json.JsonSerializer", "--package", "System.Text.Json",
            "-m", "Serialize", "--rows", "10", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Serialize<TValue>(TValue value", output);
    }

    [Fact]
    public async Task Member_GenericMethodSelector_FiltersByMethodArity()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberGenericSelectorFixture).FullName!, "--library", TestAssemblyPath,
            "GenericChoice<T>", "-S", "Signature", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("GenericChoice<T>(T value)", output);
        Assert.DoesNotContain("GenericChoice(string value)", output);
    }

    [Fact]
    public async Task Member_GenericMethodSelector_FiltersInventoryByMethodArity()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberGenericSelectorFixture).FullName!, "--library", TestAssemblyPath,
            "GenericChoice<T>", "--rows", "10", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("GenericChoice<T>(T value)", output);
        Assert.DoesNotContain("GenericChoice(string value)", output);
    }

    [Fact]
    public async Task Member_GenericMethodSelector_MemberIndexSelectorRoundTrips()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberGenericSelectorFixture).FullName!, "--library", TestAssemblyPath,
            "GenericChoice<T>", "-S", "Member Index", "--table");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var selector = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("GenericChoice", StringComparison.Ordinal))
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0])
            .Single();
        Assert.Contains(':', selector);

        (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberGenericSelectorFixture).FullName!, "--library", TestAssemblyPath,
            selector, "-S", "Signature", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("GenericChoice<T>(T value)", output);
        Assert.DoesNotContain("GenericChoice(string value)", output);
    }

    [Theory]
    [InlineData("GenericChoice<T>")]
    [InlineData("GenericChoice<T>:1")]
    public async Task Member_DottedGenericMethodSelector_FiltersByMethodArity(string memberSelector)
    {
        var (exit, output, error) = await RunAppAsync(
            "member", $"{typeof(MemberGenericSelectorFixture).FullName!}.{memberSelector}",
            "--library", TestAssemblyPath,
            "-S", "Signature", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("GenericChoice<T>(T value)", output);
        Assert.DoesNotContain("GenericChoice(string value)", output);
    }

    [Fact]
    public async Task Member_DottedGenericMethodSelector_FiltersInventoryByMethodArity()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", $"{typeof(MemberGenericSelectorFixture).FullName!}.GenericChoice<T>",
            "--library", TestAssemblyPath,
            "--rows", "10", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("GenericChoice<T>(T value)", output);
        Assert.DoesNotContain("GenericChoice(string value)", output);
    }

    [Fact]
    public async Task Member_GenericTypeName_DoesNotAdmitMemberDetailSections()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Collections.Generic.List<T>",
            "--platform", "System.Private.CoreLib",
            "-S", "Signature", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Select value 'Signature' not found.", error);
    }

    /// <summary>
    /// The package command drops the computed <c>@All</c> and <c>@Default</c> poles
    /// (<see cref="DotnetInspector.Sections.SectionPipeline{TModel}.WithoutComputedPoles"/>):
    /// its sections are reachable by name, by topical door, and by verbosity, so a pole that
    /// renders a superset nobody asked for is a surface no discovery output describes. This is
    /// the gate that keeps them from being reintroduced.
    /// </summary>
    [Fact]
    public async Task Package_ComputedPoles_AreNotResolvable()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (allExit, _, allError) = await RunAppAsync("package", packagePath, "-S", "@All");
            Assert.Equal(1, allExit);
            Assert.Contains("'@All' not found", allError, StringComparison.Ordinal);

            // @Default is still the internal encoding of bare -S, so it must not resolve as a
            // category while bare -S keeps working.
            var (comboExit, _, comboError) = await RunAppAsync("package", packagePath, "-S", "@Default,Manifest");
            Assert.Equal(0, comboExit);
            Assert.Contains("'@Default' not found", comboError, StringComparison.Ordinal);

            var (bareExit, bareOutput, _) = await RunAppAsync("package", packagePath, "-S");
            Assert.Equal(0, bareExit);
            Assert.Contains("## Package Info", bareOutput);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Manifest_RendersBasicPackageManifestRows()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Manifest");

            Assert.Equal(0, exit);
            Assert.Contains("## Manifest", output);
            Assert.DoesNotContain("| Info | Schema |", output);
            Assert.Contains("| Info | Package | Test.MultiLib", output);
            Assert.Contains("| Info | Version | 1.0.0 |", output);
            Assert.DoesNotContain("| RID Package |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Manifest_RendersToolManifestRows()
    {
        var (exit, output, error) = await RunAppAsync("package", "Azure.Mcp", "-S", "Manifest");

        Assert.Equal(0, exit);
        Assert.Contains("## Manifest", output);
        Assert.Contains("| Info | Manifest Version | 2 |", output);
        Assert.DoesNotContain("| Info | Schema |", output);
        Assert.Contains("| Info | Commands |", output);
        Assert.Contains("| RID Package | linux-x64 | Azure.Mcp.linux-x64 | yes |", output);
        Assert.DoesNotContain("## RID Packages", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task Package_LibraryFiles_RendersAllLibFiles()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            // The lib/ slice is no longer its own section: --path is the scoping mechanism.
            var (exit, output, error) = await RunAppAsync("package", packagePath, "--path", "lib/**");

            Assert.Equal(0, exit);
            Assert.Contains("| Path | Size |", output);
            Assert.Contains("| lib/net10.0/Latest.One.dll |", output);
            Assert.Contains("| lib/net10.0/Latest.One.xml | 7 |", output);
            Assert.Contains("| lib/net10.0/Latest.Two.dll |", output);
            Assert.Contains("| lib/net8.0/Older.dll |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_NormalOutput_RendersLibraryFileSizes()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Package files");

            Assert.Equal(0, exit);
            Assert.Contains("## Package files", output);
            Assert.Contains("| lib/net10.0/Latest.One.xml | 7 |", output);
            Assert.DoesNotContain("| lib/net10.0/Latest.One.xml | 0 |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
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

    [Fact]
    public async Task Package_FilesFamily_RendersEachDocumentKind()
    {
        var (packagePath, tempDir) = CreateLocalLayoutPackage();
        try
        {
            var (readmeExit, readmeOutput, _) = await RunAppAsync("package", packagePath, "-S", "Package README file");
            Assert.Equal(0, readmeExit);
            Assert.Contains("## Package README file", readmeOutput);
            Assert.Contains("| README.md |", readmeOutput);
            Assert.DoesNotContain("| lib/net8.0/Layout.dll |", readmeOutput);

            var (nuspecExit, nuspecOutput, _) = await RunAppAsync("package", packagePath, "-S", "Package nuspec file");
            Assert.Equal(0, nuspecExit);
            Assert.Contains("## Package nuspec file", nuspecOutput);
            // The manifest section is a path listing, not the document itself.
            Assert.Contains("| Test.Layout.nuspec |", nuspecOutput);
            Assert.DoesNotContain("<package xmlns", nuspecOutput);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Files_IncludesTheNuspecManifest()
    {
        var (packagePath, tempDir) = CreateLocalLayoutPackage();
        try
        {
            // Regression: the manifest used to be classified as zip plumbing, which made it
            // unreachable through Files, --path, and --layout alike.
            var (exit, output, _) = await RunAppAsync("package", packagePath, "-S", "Files");
            Assert.Equal(0, exit);
            Assert.Contains("Test.Layout.nuspec", output);

            var (pathExit, pathOutput, _) = await RunAppAsync("package", packagePath, "--path", "Test.Layout.nuspec");
            Assert.Equal(0, pathExit);
            Assert.Contains("Test.Layout.nuspec", pathOutput);

            var (layoutExit, layoutOutput, _) = await RunAppAsync("package", packagePath, "--layout");
            Assert.Equal(0, layoutExit);
            Assert.Contains("Test.Layout.nuspec", layoutOutput);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_FilesNuspec_PrintRendersTheManifestDocument()
    {
        var (packagePath, tempDir) = CreateLocalLayoutPackage();
        try
        {
            var (exit, output, _) = await RunAppAsync("package", packagePath, "-S", "Files: Nuspec", "--print");

            Assert.Equal(0, exit);
            Assert.Contains("<package xmlns", output);
            Assert.Contains("<id>Test.Layout</id>", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_FilesCategory_DropsEmptyMembersButStillCountsThem()
    {
        // CreateLocalLayoutPackage ships a README and a manifest but no skills/.
        var (packagePath, tempDir) = CreateLocalLayoutPackage();
        try
        {
            var (renderExit, renderOutput, _) = await RunAppAsync("package", packagePath, "-S", "@Files");
            Assert.Equal(0, renderExit);
            Assert.Contains("## Package nuspec file", renderOutput);
            Assert.Contains("## Package README file", renderOutput);
            Assert.DoesNotContain("## Package skill files", renderOutput);

            // --count reports the whole category, including the members that rendered nothing.
            var (countExit, countOutput, _) = await RunAppAsync("package", packagePath, "-S", "@Files", "--count");
            Assert.Equal(0, countExit);
            Assert.Contains("| Package skill files | 0 |", countOutput);
            Assert.Contains("| Package nuspec file | 1 |", countOutput);
            Assert.Contains("| Package README file | 1 |", countOutput);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_LegacyFileSectionNames_StillResolve()
    {
        var (packagePath, tempDir) = CreateLocalLayoutPackage();
        try
        {
            // "Grounding" was this section's canonical name, not a nickname, so scripts
            // spelling it must keep working after the rename.
            var (groundingExit, groundingOutput, _) = await RunAppAsync("package", packagePath, "-S", "Grounding");
            Assert.Equal(0, groundingExit);
            Assert.Contains("## Package README file", groundingOutput);

            var (nuspecExit, nuspecOutput, _) = await RunAppAsync("package", packagePath, "-S", "Files: Nuspec");
            Assert.Equal(0, nuspecExit);
            Assert.Contains("## Package nuspec file", nuspecOutput);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_PackageInfoReadme_UsesBestReadme()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.BestReadme.Info", "README.md", "readme", "agents");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Package Info");

            Assert.Equal(0, exit);
            Assert.Contains("| Readme | README.md |", output);
            Assert.DoesNotContain("| Readme | AGENTS.md |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_PackageReadme_RendersSingleBestReadmeFile()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.BestReadme.Section", "README.md", "readme", "agents");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Package README file");

            Assert.Equal(0, exit);
            Assert.Contains("## Package README file", output);
            Assert.Contains("| Path | Size |", output);
            Assert.Contains("| README.md | 6 |", output);
            Assert.DoesNotContain("| AGENTS.md | 6 |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_ReadmeSection_LegacyReadmeSelectorStillResolves()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Grounding.Alias", "README.md", "readme", "agents");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Package README");

            Assert.Equal(0, exit);
            Assert.Contains("## Package README file", output);
            Assert.Contains("| README.md | 6 |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Print_PrintsBestReadmeContent()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Print.Grounding",
            "README.md",
            "readme",
            "agents",
            null,
            ("00-FIRST.txt", "wrong file"));
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Package README file", "--print", "--bare");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal("readme", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Value_PrintsPackageInfoField()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Value.PackageInfo", "README.md", "readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "-S", "Package Info", "--fields", "Version", "--value");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal("1.0.0", output.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_PrintRequiresSingleSelectedSection()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Print.Requires.Select", "README.md", "readme", "agents");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "--print");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("--print requires -S/--select to match exactly one printable section", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Readme_PrintsBestReadmeContent()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.BestReadme.Content", "README.md", "readme", "agents");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Package README file", "--print");

            Assert.Equal(0, exit);
            Assert.Contains("readme", output);
            Assert.DoesNotContain("agents", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Readme_Bare_PrintsBestReadmeBody()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.BestReadme.Bare", "README.md", "readme", "agents");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Package README file", "--print", "--bare");

            Assert.Equal(0, exit);
            Assert.Empty(error);

            // --print emits the document, not a rendering of it: no trailing newline is added
            // to a document that does not end with one. The readme lens used to append one,
            // which is the same class of edit that rewrote links inside the XML manifest.
            Assert.Equal("readme", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_PackageReadmeSection_Bare_PrintsReadmeBody()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.PackageReadme.Bare", "PACKAGE.md", "package docs");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Package README file", "--bare", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal("package docs\n", output.ReplaceLineEndings("\n"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Content_Bare_PrintsSingleSelectedFileBody()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Content.Bare", "README.md", "readme", "agents body");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "--path", "@readme", "--content", "--bare");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal("readme\n", output.ReplaceLineEndings("\n"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Content_Bare_IgnoresEnvironmentRowFormat()
    {
        var originalFormat = Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Content.BareEnv", "README.md", "readme", "agents body");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", "table");
            var (exit, output, error) = await RunAppAsync("package", packagePath, "--path", "@readme", "--content", "--bare");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal("readme\n", output.ReplaceLineEndings("\n"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", originalFormat);
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Readme_DefaultNormalizesGithubBlobLinksToRaw()
    {
        const string readme = """
            [code](https://github.com/owner/repo/blob/main/src/File.cs)
            ![image](https://github.com/owner/repo/blob/main/images/logo.png)
            """;
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Readme.RawLinks", "README.md", readme);
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Package README file", "--print");

            Assert.Equal(0, exit);
            Assert.Contains("https://raw.githubusercontent.com/owner/repo/main/src/File.cs", output);
            Assert.Contains("https://raw.githubusercontent.com/owner/repo/main/images/logo.png", output);
            Assert.DoesNotContain("github.com/owner/repo/blob", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Readme_BlobLeavesMarkdownLinksVerbatim()
    {
        const string readme = "[code](https://github.com/owner/repo/blob/main/src/File.cs)";
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Readme.BlobLinks", "README.md", readme);
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Package README file", "--print", "--blob");

            Assert.Equal(0, exit);
            Assert.Contains("https://github.com/owner/repo/blob/main/src/File.cs", output);
            Assert.DoesNotContain("raw.githubusercontent.com", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_ReadmePrint_ReportsTheSelectedDocumentInThePayload()
    {
        // The selected readme used to be reported through an InfoTracker side channel that only
        // the bespoke readme printer wrote. The generic print projection carries the path in the
        // payload instead, so provenance survives without a printer of its own.
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.BestReadme.Info", "README.md", "readme", "agents");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "-S", "Package README file", "--print", "--json");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Equal("README.md", document.RootElement.GetProperty("path").GetString());
            Assert.Equal("readme", document.RootElement.GetProperty("content").GetString()?.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_NuspecPrint_EmitsTheManifestExactlyAsShipped()
    {
        // The manifest is XML, so none of the Markdown treatment the README gets may touch it.
        // The URL here is the shape the blob-to-raw rewriter matches, and it matches bare URLs
        // anywhere in the text -- an XML element is not a Markdown link, but the regex cannot
        // tell. A rewritten manifest still parses and still looks right, which is why this is
        // pinned byte-for-byte against what the package actually contains.
        const string ReleaseNotes = "https://github.com/owner/repo/blob/main/CHANGELOG.md";
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Nuspec.Verbatim",
            "README.md",
            "readme",
            null,
            $"\n    <releaseNotes>See {ReleaseNotes} for details</releaseNotes>");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "-S", "Package nuspec file", "--print", "--bare");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains(ReleaseNotes, output, StringComparison.Ordinal);
            Assert.DoesNotContain("raw.githubusercontent.com", output, StringComparison.Ordinal);

            using var archive = ZipFile.OpenRead(packagePath);
            var entry = Assert.Single(archive.Entries, e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            using var entryStream = entry.Open();
            using var shipped = new MemoryStream();
            entryStream.CopyTo(shipped);

            // Compare bytes, not decoded text. A StreamReader would strip a byte order mark from
            // both sides and agree that a document three bytes shorter than the shipped one was
            // identical to it, which is the assertion failing to test what the name claims.
            Assert.Equal(shipped.ToArray(), Encoding.UTF8.GetBytes(output));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_NuspecPrint_KeepsAByteOrderMarkThePackageShipped()
    {
        // ReadAllText consumes a byte order mark, so a document that ships with one would be
        // printed three bytes shorter than it exists in the package -- silently, and invisibly
        // in any text comparison, because a StreamReader strips it from the expectation too.
        // Real packages ship BOM'd manifests (EntityFramework does), and a caller printing a
        // manifest to hash or diff it is asking for the bytes, not for an equivalent document.
        var tempDir = Path.Combine(Path.GetTempPath(), $"package-test-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(tempDir, "content");
        Directory.CreateDirectory(packageRoot);
        var nuspec = """
            <?xml version="1.0" encoding="utf-8"?>
            <package>
              <metadata>
                <id>Test.Bom.Nuspec</id>
                <version>1.0.0</version>
                <authors>tests</authors>
                <description>test package</description>
              </metadata>
            </package>
            """;
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var shipped = (byte[])[.. encoding.GetPreamble(), .. encoding.GetBytes(nuspec)];
        File.WriteAllBytes(Path.Combine(packageRoot, "Test.Bom.Nuspec.nuspec"), shipped);
        var packagePath = Path.Combine(tempDir, "Test.Bom.Nuspec.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);

        try
        {
            Assert.Equal(0xEF, shipped[0]);

            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "-S", "Package nuspec file", "--print", "--bare");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal(shipped, Encoding.UTF8.GetBytes(output));
            Assert.StartsWith("\uFEFF", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_NuspecPrint_RefusesMarkdownScopes()
    {
        // Frontmatter is a Markdown construct. XML can never carry it, so returning the whole
        // manifest or an empty document would both report success for a question that was not
        // answered.
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Nuspec.Scope", "README.md", "readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "-S", "Package nuspec file", "--print", "--frontmatter");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("apply to Markdown documents", error, StringComparison.Ordinal);
            Assert.Contains("Test.Nuspec.Scope.nuspec", error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Content_LeavesNonMarkdownFilesVerbatim()
    {
        // Same rewriter, reached through --content instead of --print. An MSBuild comment is
        // not a Markdown link either.
        const string Link = "https://github.com/owner/repo/blob/main/docs/config.md";
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Content.Props",
            "README.md",
            $"[docs]({Link})",
            null,
            null,
            ("build/Test.props", $"<Project>\n  <!-- See {Link} -->\n</Project>"));
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", packagePath, "--content", "--path", "build/Test.props", "--bare");

            Assert.Equal(0, exit);
            Assert.Contains(Link, output, StringComparison.Ordinal);
            Assert.DoesNotContain("raw.githubusercontent.com", output, StringComparison.Ordinal);

            // The README in the same package still gets the Markdown treatment, so this is a
            // rule about the document's kind rather than the rewriter being switched off.
            var (readmeExit, readmeOutput, _) = await RunAppAsync(
                "package", packagePath, "-S", "Package README file", "--print", "--bare");

            Assert.Equal(0, readmeExit);
            Assert.Contains("raw.githubusercontent.com", readmeOutput, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_SkillsPrint_ResolvesCardinalityWithoutAPrinterOfItsOwn()
    {
        // Skill documents never had a bespoke printer. They are printable because the section
        // lists rows that declare documents, which is the whole point of the generic path:
        // cardinality, --row, and the guidance error all come from PrintProjectionOutput.
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Skills.Print",
            "README.md",
            "readme",
            null,
            null,
            ("skills/alpha/SKILL.md", "# Alpha skill"),
            ("skills/beta/SKILL.md", "# Beta skill"));
        try
        {
            var (ambiguousExit, ambiguousOutput, ambiguousError) = await RunAppAsync(
                "package", packagePath, "-S", "Package skill files", "--print");

            Assert.Equal(1, ambiguousExit);
            Assert.Empty(ambiguousOutput);
            Assert.Contains("2 printable rows", ambiguousError, StringComparison.Ordinal);

            var (exit, output, _) = await RunAppAsync(
                "package", packagePath, "-S", "Package skill files", "--print", "--row", "2", "--bare");

            Assert.Equal(0, exit);
            Assert.Equal("# Beta skill", output);

            // --row addresses the rendered position, so it must agree with the section listing.
            var (pathsExit, pathsOutput, _) = await RunAppAsync(
                "package", packagePath, "-S", "Package skill files", "--paths");

            Assert.Equal(0, pathsExit);
            var paths = pathsOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, paths.Length);
            Assert.EndsWith("beta/SKILL.md", paths[1].Trim(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_ReadmePrint_NamesTheEmptySectionWhenThePackageShipsNoSuchDocument()
    {
        // The generic writer can only say "selected section" because an empty payload has no row
        // to name it from. A package ships several document kinds, so the caller needs to know
        // which one is absent -- the bespoke printer used to say so, and that must not be lost.
        var (packagePath, tempDir) = CreateLocalPackageWithoutReadme("Test.NoReadme.Print");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "-S", "Package README file", "--print");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("Package README file", error, StringComparison.Ordinal);

            // The nuspec is always present, so the empty refusal must be about the selected
            // section rather than about printing being unavailable on this package.
            var (nuspecExit, nuspecOutput, _) = await RunAppAsync(
                "package", packagePath, "-S", "Package nuspec file", "--print", "--bare");

            Assert.Equal(0, nuspecExit);
            Assert.Contains("Test.NoReadme.Print", nuspecOutput, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_RemovedReadmeFlag_PointsAtItsReplacement()
    {
        // A removed spelling answered with "Unrecognized option" is true but leaves the caller to
        // find the replacement. This repo already answers the stale '--head N' spelling with its
        // replacement, so a removed flag does the same -- for every form that used to accept it.
        foreach (var args in new[]
        {
            new[] { "package", "Newtonsoft.Json@13.0.3", "--readme" },
            ["Newtonsoft.Json@13.0.3", "--readme"],
            // The removed option was boolean, so the parser also took --readme=true. Answering
            // only the bare spelling would leave the assigned one on a bare parser complaint.
            ["package", "Newtonsoft.Json@13.0.3", "--readme=true"],
            new[] { "package", "Newtonsoft.Json@13.0.3", "--readme", "--json" }
        })
        {
            var (exit, output, error) = await RunAppAsync(args);

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("no longer valid", error, StringComparison.Ordinal);
            Assert.Contains("--print", error, StringComparison.Ordinal);
        }
    }

    [Theory]
    // A name with no dot in it says nothing about the document's kind, so the role answers. The
    // directory a nested readme sits in may carry dots without that being a claim about the file.
    [InlineData("README")]
    [InlineData("docs/GUIDE")]
    [InlineData("docs/v1.0/GUIDE")]
    public async Task Package_ExtensionlessReadme_IsStillTreatedAsMarkdown(string readmePath)
    {
        // The readme's kind comes from its role, not its extension: the manifest declared this
        // file as the readme, and NuGet renders it as Markdown. Keying only on the extension
        // would refuse --frontmatter and drop blob-to-raw rewriting for a document that is
        // genuinely Markdown, which is a capability the bespoke readme printer had.
        var tempDir = Path.Combine(Path.GetTempPath(), $"package-test-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(tempDir, "content");
        Directory.CreateDirectory(packageRoot);
        var readmeFullPath = Path.Combine(packageRoot, readmePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(readmeFullPath)!);
        File.WriteAllText(
            readmeFullPath,
            "---\ntitle: Demo\n---\n\nSee https://github.com/owner/repo/blob/main/x.md for more.\n");
        File.WriteAllText(Path.Combine(packageRoot, "Test.Extensionless.nuspec"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package>
              <metadata>
                <id>Test.Extensionless</id>
                <version>1.0.0</version>
                <authors>tests</authors>
                <description>test package</description>
                <readme>{readmePath}</readme>
              </metadata>
            </package>
            """);
        var packagePath = Path.Combine(tempDir, "Test.Extensionless.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);

        try
        {
            var (scopeExit, scopeOutput, scopeError) = await RunAppAsync(
                "package", packagePath, "-S", "Package README file", "--print", "--frontmatter", "--bare");

            Assert.Equal(0, scopeExit);
            Assert.Empty(scopeError);
            Assert.Contains("title: Demo", scopeOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("See https://", scopeOutput, StringComparison.Ordinal);

            var (exit, output, _) = await RunAppAsync(
                "package", packagePath, "-S", "Package README file", "--print", "--bare");

            Assert.Equal(0, exit);
            Assert.Contains("raw.githubusercontent.com", output, StringComparison.Ordinal);

            // The nuspec in the same package is not the readme, so it stays verbatim. Role, not
            // a blanket relaxation of the rule, is what makes the readme Markdown.
            var (nuspecExit, nuspecOutput, _) = await RunAppAsync(
                "package", packagePath, "-S", "Package nuspec file", "--print", "--bare");

            Assert.Equal(0, nuspecExit);
            Assert.Contains($"<readme>{readmePath}</readme>", nuspecOutput, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_DeclaredReadme_KeepsItsRoleWhenTheConventionalNameAlsoExists()
    {
        // ResolvePackageReadme prefers README.md so the README section shows the document a reader
        // expects, but the manifest still declares docs/GUIDE a readme. Which file the section
        // displays is a presentation choice; the manifest declaration is what makes the document
        // Markdown, so scoping and link rewriting have to follow the declaration.
        var tempDir = Path.Combine(Path.GetTempPath(), $"package-test-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(tempDir, "content");
        Directory.CreateDirectory(Path.Combine(packageRoot, "docs"));
        File.WriteAllText(Path.Combine(packageRoot, "README.md"), "conventional readme\n");
        File.WriteAllText(
            Path.Combine(packageRoot, "docs", "GUIDE"),
            "---\ntitle: Declared\n---\n\nSee https://github.com/owner/repo/blob/main/x.md for more.\n");
        File.WriteAllText(Path.Combine(packageRoot, "Test.DeclaredReadme.nuspec"), """
            <?xml version="1.0" encoding="utf-8"?>
            <package>
              <metadata>
                <id>Test.DeclaredReadme</id>
                <version>1.0.0</version>
                <authors>tests</authors>
                <description>test package</description>
                <readme>docs/GUIDE</readme>
              </metadata>
            </package>
            """);
        var packagePath = Path.Combine(tempDir, "Test.DeclaredReadme.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);

        try
        {
            var (scopeExit, scopeOutput, scopeError) = await RunAppAsync(
                "package", packagePath, "--content", "--path", "docs/GUIDE", "--frontmatter", "--bare");

            Assert.Equal(0, scopeExit);
            Assert.Empty(scopeError);
            Assert.Contains("title: Declared", scopeOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("See https://", scopeOutput, StringComparison.Ordinal);

            // The role is carried by the declaration alone, so a sibling that the manifest does
            // not name stays verbatim and keeps its blob link.
            var (plainExit, plainOutput, _) = await RunAppAsync(
                "package", packagePath, "--content", "--path", "docs/GUIDE", "--bare");

            Assert.Equal(0, plainExit);
            Assert.Contains("raw.githubusercontent.com", plainOutput, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    // A stated suffix, obviously.
    [InlineData("images/logo.png")]
    // A stray trailing dot does not unstate it.
    [InlineData("images/logo.png.")]
    // A suffix spelled as a hidden basename. Telling this apart from a hidden word like .README
    // needs a list of known suffixes that would go stale, so a dot is read conservatively: the
    // cost of guessing wrong here is a corrupted file at exit 0, and there it is a loud refusal.
    [InlineData(".png")]
    [InlineData(".README")]
    public async Task Package_DeclaredNonMarkdownReadme_IsStillNotMarkdown(string readmePath)
    {
        // A manifest can declare anything as the readme, including a file that names itself
        // something else. The role answers a document's kind only where the name is silent;
        // letting a declaration override a stated kind would run the link rewriter over a PNG
        // and hand back a corrupted file, which is the outcome this command prevents.
        var tempDir = Path.Combine(Path.GetTempPath(), $"package-test-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(tempDir, "content");
        var declared = Path.Combine(packageRoot, readmePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(declared)!);
        File.WriteAllText(declared, "PNGish see https://github.com/owner/repo/blob/main/x.md here\n");
        File.WriteAllText(Path.Combine(packageRoot, "Test.BinaryReadme.nuspec"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package>
              <metadata>
                <id>Test.BinaryReadme</id>
                <version>1.0.0</version>
                <authors>tests</authors>
                <description>test package</description>
                <readme>{readmePath}</readme>
              </metadata>
            </package>
            """);
        var packagePath = Path.Combine(tempDir, "Test.BinaryReadme.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);

        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", packagePath, "--content", "--path", readmePath, "--bare");

            Assert.Equal(0, exit);
            Assert.Equal(File.ReadAllText(declared), output);
            Assert.Contains("github.com/owner/repo/blob/main", output, StringComparison.Ordinal);

            // And a Markdown scope over it is refused rather than answered from the whole file.
            var (scopeExit, scopeOutput, scopeError) = await RunAppAsync(
                "package", packagePath, "--content", "--path", readmePath, "--frontmatter", "--bare");

            Assert.Equal(1, scopeExit);
            Assert.Empty(scopeOutput);
            Assert.Contains("is not Markdown", scopeError, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_ReadmeInAValuePosition_IsNotMistakenForTheRemovedFlag()
    {
        // The replacement guidance answers a parse failure, not a token scan. '--out --readme'
        // names an output file and parses, so second-guessing it would refuse a valid request in
        // the name of explaining an option the caller never used.
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.ReadmeValue", "README.md", "value body");
        var outputPath = Path.Combine(tempDir, "--readme");
        try
        {
            var (exit, _, error) = await RunAppAsync(
                "package", packagePath, "-S", "Package README file", "--print", "--bare", "--out", outputPath);

            Assert.Equal(0, exit);
            Assert.DoesNotContain("no longer valid", error, StringComparison.Ordinal);
            Assert.True(File.Exists(outputPath));
            Assert.Contains("value body", File.ReadAllText(outputPath), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_ReadmeTip_RecommendsAGestureThatActuallyRuns()
    {
        // Removing a flag leaves the suggestions that named it behind, and a tip is a command the
        // user is invited to paste. Parse the gesture out of the emitted tip and run it, so the
        // tip cannot drift into naming an option the parser no longer recognizes.
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Tip.Readme", "README.md", "tip readme body");
        try
        {
            var (_, _, tipError) = await RunAppAsync("package", packagePath, "-T:d");

            var tipLine = tipError
                .Split('\n')
                .FirstOrDefault(line => line.Contains("# view README", StringComparison.Ordinal));
            Assert.NotNull(tipLine);

            var gesture = tipLine!.Split('#')[0].Trim();
            Assert.StartsWith("package ", gesture, StringComparison.Ordinal);
            Assert.Contains("--print", gesture, StringComparison.Ordinal);

            // Re-split the way a shell would, so the quoted section name survives as one token.
            var args = System.Text.RegularExpressions.Regex
                .Matches(gesture, "\"[^\"]*\"|\\S+")
                .Select(match => match.Value.Trim('"'))
                .ToArray();
            args[1] = packagePath;

            var (exit, output, error) = await RunAppAsync(args);

            Assert.Equal(0, exit);
            Assert.DoesNotContain("Unrecognized option", error, StringComparison.Ordinal);
            Assert.Contains("tip readme body", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Signals_ReportsAgentDocumentation()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.AgentDocs.Signal", "README.md", "readme", "agents");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Signals");

            Assert.Equal(0, exit);
            Assert.Contains("| Documentation | Agent documentation | Yes | AGENTS.md |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathReadme_TsvCombinesRowsWithPackageColumn()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.First", "PACKAGE.md", "first readme");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Second", "docs-readme.md", "second readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "@readme", "--tsv");

            Assert.Equal(0, exit);
            Assert.Contains("package\tversion\tpath\tsize", output);
            Assert.Contains("Test.First\t1.0.0\tPACKAGE.md\t12", output);
            Assert.Contains("Test.Second\t1.0.0\tdocs-readme.md\t13", output);
            Assert.DoesNotContain("not a valid package version", error);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathReadme_TsvIncludesEmptyPackageRow()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.HasReadme", "README.md", "readme");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.NoMatch", "README.md", "readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "MISSING.md", "--tsv");

            Assert.Equal(0, exit);
            Assert.Contains("package\tversion\tpath\tsize", output);
            Assert.Contains("Test.HasReadme\t1.0.0\t\t", output);
            Assert.Contains("Test.NoMatch\t1.0.0\t\t", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathReadme_JsonlIncludesEmptyPackageObject()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Jsonl.HasReadme", "README.md", "readme");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Jsonl.NoMatch", "README.md", "readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "MISSING.md", "--jsonl");

            Assert.Equal(0, exit);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);
            Assert.Contains("\"package\":\"Test.Jsonl.HasReadme\"", lines[0]);
            Assert.Contains("\"version\":\"1.0.0\"", lines[0]);
            Assert.Contains("\"path\":\"\"", lines[0]);
            Assert.Contains("\"package\":\"Test.Jsonl.NoMatch\"", lines[1]);
            Assert.Contains("\"version\":\"1.0.0\"", lines[1]);
            Assert.Contains("\"path\":\"\"", lines[1]);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_PathReadme_JsonEmitsNumericSizeForDeclaredReadme()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Json.Readme", "PACKAGE.md", "declared readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--path", "@readme", "--json");

            Assert.Equal(0, exit);
            using var document = JsonDocument.Parse(output);
            var file = Assert.Single(document.RootElement.GetProperty("files").EnumerateArray());
            Assert.Equal("PACKAGE.md", file.GetProperty("path").GetString());
            Assert.Equal(JsonValueKind.Number, file.GetProperty("size").ValueKind);
            Assert.Equal(15, file.GetProperty("size").GetInt64());
            Assert.False(file.TryGetProperty("is_readme", out _));
            Assert.False(file.TryGetProperty("is_agents", out _));
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_PathReadme_JsonlEmitsNumericSizeForDeclaredReadme()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Jsonl.Readme", "PACKAGE.md", "declared readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--path", "@readme", "--jsonl");

            Assert.Equal(0, exit);
            var line = Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            using var document = JsonDocument.Parse(line);
            Assert.Equal("PACKAGE.md", document.RootElement.GetProperty("path").GetString());
            Assert.Equal(JsonValueKind.Number, document.RootElement.GetProperty("size").ValueKind);
            Assert.Equal(15, document.RootElement.GetProperty("size").GetInt64());
            Assert.False(document.RootElement.TryGetProperty("is_readme", out _));
            Assert.False(document.RootElement.TryGetProperty("is_agents", out _));
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathReadme_JsonlEmitsNumericSizeForDeclaredReadme()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Multi.Jsonl.Readme", "PACKAGE.md", "declared readme");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Multi.Jsonl.Empty", "README.md", "readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "PACKAGE.md", "--jsonl");

            Assert.Equal(0, exit);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);

            using var hit = JsonDocument.Parse(lines[0]);
            Assert.Equal("Test.Multi.Jsonl.Readme", hit.RootElement.GetProperty("package").GetString());
            Assert.Equal("1.0.0", hit.RootElement.GetProperty("version").GetString());
            Assert.Equal("PACKAGE.md", hit.RootElement.GetProperty("path").GetString());
            Assert.Equal(JsonValueKind.Number, hit.RootElement.GetProperty("size").ValueKind);
            Assert.Equal(15, hit.RootElement.GetProperty("size").GetInt64());
            Assert.False(hit.RootElement.TryGetProperty("is_readme", out _));

            using var empty = JsonDocument.Parse(lines[1]);
            Assert.Equal("Test.Multi.Jsonl.Empty", empty.RootElement.GetProperty("package").GetString());
            Assert.Equal("", empty.RootElement.GetProperty("path").GetString());
            Assert.Equal(JsonValueKind.Null, empty.RootElement.GetProperty("size").ValueKind);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathAgents_TsvResolvesAgentsRows()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Agents.One", "README.md", "readme", "agents one");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Agents.Two", "README.md", "readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "@agents", "--tsv");

            Assert.Equal(0, exit);
            Assert.Contains("package\tversion\tpath\tsize", output);
            Assert.Contains("Test.Agents.One\t1.0.0\tAGENTS.md\t10", output);
            Assert.Contains("Test.Agents.Two\t1.0.0\t\t", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathFirst_UsesFirstMatchingSelector()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Match.First", "README.md", "readme", "agents");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Match.Second", "README.md", "readme");
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "@agents", "--path", "@readme", "--match", "first", "--tsv");

            Assert.Equal(0, exit);
            Assert.Contains("Test.Match.First\t1.0.0\tAGENTS.md\t6", output);
            Assert.Contains("Test.Match.Second\t1.0.0\tREADME.md\t6", output);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathAll_ReturnsAllMatchingSelectors()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Match.All", "README.md", "readme", "agents");
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", packagePath, packagePath, "--path", "@agents", "--path", "README.md", "--tsv");

            Assert.Equal(0, exit);
            Assert.Contains("Test.Match.All\t1.0.0\tAGENTS.md\t6", output);
            Assert.Contains("Test.Match.All\t1.0.0\tREADME.md\t6", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathSkipEmpty_OmitsEmptyPackages()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Skip.HasAgents", "README.md", "readme", "agents");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Skip.NoAgents", "README.md", "readme");
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "@agents", "--skip-empty", "--tsv");

            Assert.Equal(0, exit);
            Assert.Contains("Test.Skip.HasAgents", output);
            Assert.DoesNotContain("Test.Skip.NoAgents", output);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathReadme_CountIncludesEmptyPackageRows()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Count.HasReadme", "README.md", "readme");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Count.NoMatch", "README.md", "readme");
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "MISSING.md", "--tsv", "--count");

            Assert.Equal(0, exit);
            Assert.Equal("2", output.Trim());
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_PathContent_PrintsSelectedFileWithSeparator()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Content.Agents", "README.md", "readme", "agents body");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--path", "@agents", "--content");

            Assert.Equal(0, exit);
            Assert.Contains("------------ Test.Content.Agents :: AGENTS.md ------------", output);
            Assert.Contains("agents body", output);
            Assert.DoesNotContain("readme", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathContent_PreservesEmptyPackageBlock()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Content.HasAgents", "README.md", "readme", "agents");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Content.NoAgents", "README.md", "readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "@agents", "--content");

            Assert.Equal(0, exit);
            Assert.Contains("------------ Test.Content.HasAgents :: AGENTS.md ------------", output);
            Assert.Contains("agents", output);
            Assert.Contains("------------ Test.Content.NoAgents :: <absent> ------------", output);
            Assert.Contains("(absent)", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathContentSkipEmpty_OmitsAbsentBlock()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Content.SkipHasAgents", "README.md", "readme", "agents");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Content.SkipNoAgents", "README.md", "readme");
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "@agents", "--content", "--skip-empty");

            Assert.Equal(0, exit);
            Assert.Contains("Test.Content.SkipHasAgents :: AGENTS.md", output);
            Assert.DoesNotContain("Test.Content.SkipNoAgents", output);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_PathContent_JsonlIncludesContentField()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Content.Jsonl", "README.md", "readme", "line one\nline two");
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", packagePath, "--path", "@agents", "--content", "--jsonl");

            Assert.Equal(0, exit);
            var line = Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            using var document = JsonDocument.Parse(line);
            Assert.Equal("Test.Content.Jsonl", document.RootElement.GetProperty("package").GetString());
            Assert.Equal("1.0.0", document.RootElement.GetProperty("version").GetString());
            Assert.Equal("AGENTS.md", document.RootElement.GetProperty("path").GetString());
            Assert.True(document.RootElement.GetProperty("found").GetBoolean());
            Assert.Equal("line one\nline two", document.RootElement.GetProperty("content").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_ReadmeFrontmatter_PrintsOnlyYamlHeader()
    {
        var readme = """
            ---
            name: test
            description: resident
            ---
            # Body
            """;
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Readme.Frontmatter", "README.md", readme);
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", packagePath, "-S", "Package README file", "--print", "--frontmatter");

            Assert.Equal(0, exit);
            Assert.Contains("name: test", output);
            Assert.Contains("description: resident", output);
            Assert.DoesNotContain("# Body", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_ReadmeBody_PrintsContentAfterYamlHeader()
    {
        var readme = """
            ---
            name: test
            ---
            # Body
            """;
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Readme.Body", "README.md", readme);
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", packagePath, "-S", "Package README file", "--print", "--body");

            Assert.Equal(0, exit);
            Assert.Contains("# Body", output);
            Assert.DoesNotContain("name: test", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_ReadmePrintJsonl_CarriesTheSelectedDocumentPath()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Readme.Jsonl", "PACKAGE.md", "package docs");
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", packagePath, "-S", "Package README file", "--print", "--jsonl");

            Assert.Equal(0, exit);
            var line = Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            using var document = JsonDocument.Parse(line);

            // Which document was selected is part of the payload rather than a side channel, so
            // a caller can tell PACKAGE.md from README.md without parsing rendered text.
            Assert.Equal("Package README file", document.RootElement.GetProperty("section").GetString());
            Assert.Equal("PACKAGE.md", document.RootElement.GetProperty("path").GetString());
            Assert.Equal(1, document.RootElement.GetProperty("row").GetInt32());
            Assert.Equal("package docs", document.RootElement.GetProperty("content").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_ReadmeFrontmatter_AllowsMultiplePackages()
    {
        var firstReadme = """
            ---
            name: first
            ---
            # First Body
            """;
        var secondReadme = """
            ---
            name: second
            ---
            # Second Body
            """;
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.MultiReadme.First", "README.md", firstReadme);
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.MultiReadme.Second", "README.md", secondReadme);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--content", "--path", "@readme", "--frontmatter");

            Assert.Equal(0, exit);
            Assert.Contains("name: first", output);
            Assert.Contains("name: second", output);
            Assert.DoesNotContain("# First Body", output);
            Assert.DoesNotContain("# Second Body", output);
            Assert.DoesNotContain("cannot be combined", error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_PathContentFrontmatter_PrintsOnlyYamlHeader()
    {
        var agents = """
            ---
            name: agents
            ---
            # Agent body
            """;
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Content.Frontmatter", "README.md", "readme", agents);
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", packagePath, "--path", "@agents", "--content", "--frontmatter");

            Assert.Equal(0, exit);
            Assert.Contains("name: agents", output);
            Assert.DoesNotContain("# Agent body", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_AgentsIndex_EmitsDirectDependencyAgentsFrontmatter()
    {
        var agents = """
            ---
            name: Markout guidance
            description: Prefer Markout tables for structured markdown.
            ---
            # Body
            """;
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Agents", "1.2.3", "README.md", "readme", agents),
            new ProjectDocPackage("Test.Project.NoAgents", "4.5.6", "README.md", "readme"));

        try
        {
            var (exit, output, error) = await RunAppAsync(
                "project", projectPath, "--agents-index", "--jsonl");

            Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);
            using var agentsDocument = JsonDocument.Parse(lines.Single(line => line.Contains("Test.Project.Agents")));
            Assert.Equal("Test.Project.Agents", agentsDocument.RootElement.GetProperty("package").GetString());
            Assert.Equal("1.2.3", agentsDocument.RootElement.GetProperty("version").GetString());
            Assert.Equal("Markout guidance", agentsDocument.RootElement.GetProperty("name").GetString());
            Assert.Equal("Prefer Markout tables for structured markdown.", agentsDocument.RootElement.GetProperty("description").GetString());
            Assert.Equal("AGENTS.md", agentsDocument.RootElement.GetProperty("path").GetString());

            using var emptyDocument = JsonDocument.Parse(lines.Single(line => line.Contains("Test.Project.NoAgents")));
            Assert.Equal("", emptyDocument.RootElement.GetProperty("name").GetString());
            Assert.Equal("", emptyDocument.RootElement.GetProperty("description").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_AgentsIndex_FoldsBlockScalarDescription()
    {
        var agents = """
            ---
            name: markout
            description: >-
              Source-generated .NET serializer that renders objects as Markdown.
              Reach for it when a CLI needs structured, agent-readable output
              instead of hand-built strings.
            ---
            # Body
            """;
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Folded", "1.0.0", "README.md", "readme", agents));

        try
        {
            var (exit, output, error) = await RunAppAsync(
                "project", projectPath, "--agents-index", "--jsonl");

            Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Single());
            Assert.Equal("markout", document.RootElement.GetProperty("name").GetString());
            Assert.Equal(
                "Source-generated .NET serializer that renders objects as Markdown. Reach for it when a CLI needs structured, agent-readable output instead of hand-built strings.",
                document.RootElement.GetProperty("description").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsSection_EmitsSkillRows()
    {
        var querySkill = """
            ---
            name: Query guidance
            description: Find APIs from restored dependencies.
            ---
            # Query skill
            """;
        var sourceSkill = """
            ---
            name: Source guidance
            description: Inspect SourceLink-backed files.
            ---
            # Source skill
            """;
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Skills.Query", "1.2.3", "README.md", "readme", Skills:
                [new ProjectSkillDoc("skills/query/SKILL.md", querySkill)]),
            new ProjectDocPackage("Test.Project.Skills.Source", "4.5.6", "README.md", "readme", Skills:
                [new ProjectSkillDoc("skills/source/SKILL.md", sourceSkill)]));

        try
        {
            var (exit, output, error) = await RunAppAsync(
                "project", projectPath, "-S", "Skills", "--jsonl");

            Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);

            using var queryDocument = JsonDocument.Parse(lines.Single(line => line.Contains("Test.Project.Skills.Query")));
            Assert.Equal("Test.Project.Skills.Query", queryDocument.RootElement.GetProperty("package").GetString());
            Assert.Equal("skills/query/SKILL.md", queryDocument.RootElement.GetProperty("path").GetString());
            Assert.Equal("Query guidance", queryDocument.RootElement.GetProperty("name").GetString());
            Assert.Equal("Find APIs from restored dependencies.", queryDocument.RootElement.GetProperty("description").GetString());

            using var sourceDocument = JsonDocument.Parse(lines.Single(line => line.Contains("Test.Project.Skills.Source")));
            Assert.Equal("skills/source/SKILL.md", sourceDocument.RootElement.GetProperty("path").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsPaths_EmitsSkillPaths()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Paths.One", "1.0.0", "README.md", "one", Skills:
                [new ProjectSkillDoc("skills/SKILL.md", "one")]),
            new ProjectDocPackage("Test.Project.Paths.Two", "1.0.0", "README.md", "two", Skills:
                [new ProjectSkillDoc("skills/two/SKILL.md", "two")]));

        try
        {
            var (exit, output, error) = await RunAppAsync(
                "project", projectPath, "-S", "Skills", "--paths");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal(["skills/SKILL.md", "skills/two/SKILL.md"], output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsValue_UsesSelectedField()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Value.One", "1.2.3", "README.md", "one", Skills:
                [new ProjectSkillDoc("skills/value/SKILL.md", "one")]));

        try
        {
            var (exit, output, error) = await RunAppAsync(
                "project", projectPath, "-S", "Skills", "--fields", "Version", "--value");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal("1.2.3", output.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsPrint_PrintsFirstSkillDocument()
    {
        var skill = """
            ---
            name: selected
            ---
            # Skill guidance
            """;
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Print", "2.0.0", "README.md", "# README body", Skills:
                [new ProjectSkillDoc("skills/selected/SKILL.md", skill)]));

        try
        {
            var (exit, output, error) = await RunAppAsync(
                "project", projectPath, "-S", "Skills", "--print", "--body");

            Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            Assert.Contains("# Skill guidance", output);
            Assert.DoesNotContain("name: selected", output);
            Assert.DoesNotContain("# README body", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsPrint_SkipsDependenciesWithoutSkills()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("A.Project.NoSkills", "1.0.0", "README.md", "readme"),
            new ProjectDocPackage("B.Project.HasSkills", "1.0.0", "README.md", "readme", Skills:
                [new ProjectSkillDoc("skills/selected/SKILL.md", "selected")]));

        try
        {
            var (exit, output, error) = await RunAppAsync(
                "project", projectPath, "-S", "Skills", "--print");

            Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            Assert.Equal("selected", output.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsSection_EmptyRendersNoSkillsNote()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("A.Project.NoSkills", "1.0.0", "README.md", "readme"));

        try
        {
            var (exit, output, error) = await RunAppAsync(
                "project", projectPath, "-S", "Skills");

            Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            Assert.Contains("No skills found", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Library_TopLeverageSection_WithTopFilter_RendersSingleSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", "Top Leverage", "--top", "1", "--tsv", "--tips", "q");

        Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
        Assert.Empty(error);
        Assert.Contains("member\tcallers\troot_reach", output);
        Assert.DoesNotContain("candidate", output);
    }

    [Fact]
    public async Task Type_UnknownSelectValue_ListsAvailableSections()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.String", "-S", "ZzzNoSuchSection", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("not found", error);
        Assert.Contains("Available sections:", error);
        Assert.Contains("Run with -D to discover sections", error);
    }

    [Fact]
    public async Task Type_MemberIndexSection_OmitsEmptyDecodeColumn()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text.StringBuilder", "-S", "Member Index", "--tips", "q");

        Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
        Assert.Empty(error);
        Assert.Contains("| Selector | Stable | Canonical Signature |", output);
        Assert.DoesNotContain("Decode", output);
    }

    [Fact]
    public async Task Implements_TypeColumn_RendersGenericNameAsCodeSpan()
    {
        var (exit, output, error) = await RunAppAsync(
            "implements", "System.Text.Json.Serialization.JsonConverter",
            "--platform", "System.Text.Json", "--tips", "q");

        Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
        Assert.Contains("`System.Text.Json.Serialization.JsonConverter<T>`", output);
        Assert.DoesNotContain("JsonConverter&#96;1", output);
    }

    [Fact]
    public async Task AsyncMethods_DeclaringTypeAndSignature_RenderAsCodeSpansWithExpandedArity()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--section", "Async Methods", "-v:d", "--tips", "q");

        Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
        // Generic declaring types expand arity to a C#-friendly code span (no raw `2, no escaped angle brackets).
        Assert.Contains(
            "`System.Text.Json.Serialization.Converters.IAsyncEnumerableOfTConverter<T1, T2>.BufferedAsyncEnumerable`",
            output);
        // Generic signatures render as code spans with literal angle brackets.
        Assert.Contains("`System.Collections.Generic.IAsyncEnumerator<TElement> GetAsyncEnumerator", output);
        Assert.DoesNotContain("&#96;", output);
        Assert.DoesNotContain("&lt;", output);
        Assert.DoesNotContain("&gt;", output);
    }

    [Fact]
    public async Task AsyncMethods_MachineOutput_KeepsRawValuesWithoutCodeMarkup()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--section", "Async Methods", "--jsonl");

        Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
        Assert.Contains(
            "\"declaring_type\":\"System.Text.Json.Serialization.Converters.IAsyncEnumerableOfTConverter<T1, T2>.BufferedAsyncEnumerable\"",
            output);
        Assert.DoesNotContain("<code>", output);
        Assert.DoesNotContain("`", output);
    }

    [Fact]
    public async Task PInvokeMethods_DeclaringTypeAndSignature_RenderAsCodeSpansWithFunctionPointerPunctuation()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "Interop.User32.EnumWindows P/Invokes exist only in the Windows build of System.Diagnostics.Process.");
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Diagnostics.Process",
            "--section", "P/Invoke Methods", "-v:d", "--tips", "q");

        Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
        Assert.Contains("`Interop.User32`", output);
        // Function-pointer signature renders as a code span with literal punctuation (no escaped angle brackets).
        Assert.Contains(
            "`Interop.BOOL EnumWindows(delegate* unmanaged<nint, nint, Interop.BOOL> callback, nint extraData)`",
            output);
        Assert.DoesNotContain("&#96;", output);
        Assert.DoesNotContain("&lt;", output);
        Assert.DoesNotContain("&gt;", output);
    }

    [Fact]
    public async Task PInvokeMethods_MachineOutput_KeepsRawValuesWithoutCodeMarkup()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "Interop.User32.EnumWindows P/Invokes exist only in the Windows build of System.Diagnostics.Process.");
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Diagnostics.Process",
            "--section", "P/Invoke Methods", "--jsonl");

        Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
        Assert.Contains("\"declaring_type\":\"Interop.User32\"", output);
        Assert.Contains(
            "\"signature\":\"Interop.BOOL EnumWindows(delegate* unmanaged<nint, nint, Interop.BOOL> callback, nint extraData)\"",
            output);
        Assert.DoesNotContain("<code>", output);
        Assert.DoesNotContain("`", output);
    }

    [Fact]
    public async Task Project_SkillsPrint_JsonlIsSingleCompactRecord()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Print.Jsonl", "1.0.0", "README.md", "readme", Skills:
                [new ProjectSkillDoc("skills/jsonl/SKILL.md", "selected")]));

        try
        {
            var (exit, output, error) = await RunAppAsync(
                "project", projectPath, "-S", "Skills", "--print", "--jsonl");

            Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Single(lines);
            using var document = JsonDocument.Parse(lines[0]);
            Assert.Equal("selected", document.RootElement.GetProperty("content").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsPrint_RequiresRowWhenMultipleSkillDocuments()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Print.One", "1.0.0", "README.md", "readme", Skills:
                [new ProjectSkillDoc("skills/one/SKILL.md", "one")]),
            new ProjectDocPackage("Test.Project.Print.Two", "1.0.0", "README.md", "readme", Skills:
                [new ProjectSkillDoc("skills/two/SKILL.md", "two")]));

        try
        {
            var (exit, output, error) = await RunAppAsync(
                "project", projectPath, "-S", "Skills", "--print");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("selected section has 2 printable rows; use --row N|first|last to choose one row", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsPrint_RowSelectionPrintsSelectedDocument()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Print.One", "1.0.0", "README.md", "readme", Skills:
                [new ProjectSkillDoc("skills/one/SKILL.md", "one")]),
            new ProjectDocPackage("Test.Project.Print.Two", "1.0.0", "README.md", "readme", Skills:
                [new ProjectSkillDoc("skills/two/SKILL.md", "two")]));

        try
        {
            var (exit, output, error) = await RunAppAsync(
                "project", projectPath, "-S", "Skills", "--print", "--row", "2");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal("two", output.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsPrint_RowSelectionCountsPrintableRowsOnly()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("A.Project.NoSkills", "1.0.0", "README.md", "readme"),
            new ProjectDocPackage("B.Project.FirstPrintable", "1.0.0", "README.md", "readme", Skills:
                [new ProjectSkillDoc("skills/first/SKILL.md", "first")]),
            new ProjectDocPackage("C.Project.SecondPrintable", "1.0.0", "README.md", "readme", Skills:
                [new ProjectSkillDoc("skills/second/SKILL.md", "second")]));

        try
        {
            var (exit, output, error) = await RunAppAsync(
                "project", projectPath, "-S", "Skills", "--print", "--row", "1");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal("first", output.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsPrintAll_IsNoLongerRecognized()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.PrintAll.One", "1.0.0", "README.md", "readme", Skills:
                [new ProjectSkillDoc("skills/one/SKILL.md", "one")]),
            new ProjectDocPackage("Test.Project.PrintAll.Two", "1.0.0", "README.md", "readme", Skills:
                [new ProjectSkillDoc("skills/two/SKILL.md", "two")]));

        try
        {
            var (exit, output, error) = await RunAppAsync(
                "project", projectPath, "-S", "Skills", "--print-all");

            Assert.NotEqual(0, exit);
            Assert.DoesNotContain("--- Test.Project.PrintAll.One", output);
            Assert.Contains("--print-all", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsBare_PrintsFirstSkillDocument()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Bare", "1.0.0", "README.md", "readme", Skills:
                [new ProjectSkillDoc("skills/bare/SKILL.md", "selected")]));

        try
        {
            var (exit, output, error) = await RunAppAsync(
                "project", projectPath, "-S", "Skills", "--bare");

            Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            Assert.Equal("selected", output.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsBare_MultipleDocuments_PrintsFirstPrintableDocument()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("A.Project.NoSkills", "1.0.0", "README.md", "readme"),
            new ProjectDocPackage("B.Project.FirstPrintable", "1.0.0", "README.md", "readme", Skills:
                [new ProjectSkillDoc("skills/first/SKILL.md", "first")]),
            new ProjectDocPackage("C.Project.SecondPrintable", "1.0.0", "README.md", "readme", Skills:
                [new ProjectSkillDoc("skills/second/SKILL.md", "second")]));

        try
        {
            var (exit, output, error) = await RunAppAsync(
                "project", projectPath, "-S", "Skills", "--bare");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal("first", output.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_Columns_ReturnsClearUnsupportedError()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Columns", "1.0.0", "README.md", "readme", Skills:
                [new ProjectSkillDoc("skills/selected/SKILL.md", "selected")]));

        try
        {
            var (exit, output, error) = await RunAppAsync(
                "project", projectPath, "-S", "Skills", "--columns", "Package");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("project does not currently support --columns or --fields", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_PrintRequiresSingleSelectedSection()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Print.Requires.Select", "1.0.0", "README.md", "readme"));

        try
        {
            var (exit, output, error) = await RunAppAsync(
                "project", projectPath, "--print");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("--print requires -S/--select to match exactly one printable section", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsCount_CountsSkillFiles()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Count.One", "1.0.0", "README.md", "readme", Skills:
                [
                    new ProjectSkillDoc("skills/one/SKILL.md", "one"),
                    new ProjectSkillDoc("skills/two/SKILL.md", "two")
                ]),
            new ProjectDocPackage("Test.Project.Count.None", "1.0.0", "README.md", "readme"));

        try
        {
            var (exit, output, error) = await RunAppAsync(
                "project", projectPath, "-S", "Skills", "--count");

            Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            Assert.Equal("2\n", output.ReplaceLineEndings("\n"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_Discover_ListsSkillsSection()
    {
        var (exit, output, error) = await RunAppAsync("project", "-D", "Skills");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Package", output);
        Assert.Contains("Description", output);
    }

    [Fact]
    public async Task Project_Readme_PrefersReadmeOverAgentsAndProjectMd()
    {
        var agents = """
            ---
            name: selected
            ---
            # Agent guidance
            """;
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Readme", "2.0.0", "README.md", "# README body", agents, ProjectText: "# PROJECT body"));

        try
        {
            var (exit, output, error) = await RunAppAsync(
                "project", projectPath, "--readme", "Test.Project.Readme");

            Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            Assert.Contains("# README body", output);
            Assert.DoesNotContain("# Agent guidance", output);
            Assert.DoesNotContain("# PROJECT body", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_Readme_FallsBackToProjectMd()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.ProjectMd", "2.0.0", "README.md", "", ProjectText: "# PROJECT body", OmitReadme: true));

        try
        {
            var (exit, output, error) = await RunAppAsync(
                "project", projectPath, "--readme", "Test.Project.ProjectMd");

            Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            Assert.Contains("# PROJECT body", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_Readme_NormalizesGithubBlobLinksToRaw()
    {
        const string readme = "See https://github.com/owner/repo/blob/main/docs/guide.md";
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.RawLinks", "1.0.0", "README.md", readme));

        try
        {
            var (exit, output, error) = await RunAppAsync(
                "project", projectPath, "--readme", "Test.Project.RawLinks");

            Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            Assert.Contains("https://raw.githubusercontent.com/owner/repo/main/docs/guide.md", output);
            Assert.DoesNotContain("github.com/owner/repo/blob", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_JsonEmitsArray()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Json.One", "README.md", "one");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Json.Two", "README.md", "two");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", firstPackage, secondPackage, "--json");

            Assert.Equal(0, exit);
            using var doc = JsonDocument.Parse(output);
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
            Assert.Equal(2, doc.RootElement.GetArrayLength());
            Assert.Equal("Test.Json.One", doc.RootElement[0].GetProperty("package_name").GetString());
            Assert.Equal("Test.Json.Two", doc.RootElement[1].GetProperty("package_name").GetString());
            Assert.DoesNotContain("not a valid package version", error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_TableCombinesPackageInfoRows()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Info.One", "README.md", "one");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Info.Two", "README.md", "two");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", firstPackage, secondPackage, "--table");

            Assert.Equal(0, exit);
            Assert.Contains("Package", output);
            Assert.Contains("Field", output);
            Assert.Contains("Value", output);
            Assert.Contains("Test.Info.One", output);
            Assert.Contains("Test.Info.Two", output);
            Assert.Contains("Version", output);
            Assert.DoesNotContain("not a valid package version", error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_DetailedOutput_RendersSectionsAlphabetically()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, _) = await RunAppAsync("package", packagePath, "-v:d");

            Assert.Equal(0, exit);

            var sectionHeaders = output
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
                .Select(line => line[3..])
                .ToArray();

            Assert.NotEmpty(sectionHeaders);
            Assert.Equal(sectionHeaders.OrderBy(h => h, StringComparer.OrdinalIgnoreCase).ToArray(), sectionHeaders);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Signals_ShowsMetadataSignalsOnly()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var options = new InspectionOptions
            {
                PackageArgs = [packagePath],
                IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Signals" }
            };

            var (exit, output, error) = await ConsoleCapture.RunAsync(
                () => PackageCommand.ExecuteAsync(options));

            Assert.Equal(0, exit);
            Assert.Contains("## Signals", output);
            Assert.Contains("Supported TFM", output);
            Assert.Contains("Portable", output);
            Assert.Contains("README", output);
            Assert.Contains("License", output);
            Assert.DoesNotContain("Dependency groups", output);
            Assert.Contains("Direct dependencies", output);
            Assert.DoesNotContain("| Signals | Scope |", output);
            Assert.Contains("Known vulnerabilities", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageCommand_Signals_ShowsSignalsOnly()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Signals");

            Assert.Equal(0, exit);
            Assert.Contains("## Signals", output);
            Assert.DoesNotContain("| Signals | Scope |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Signals_RendersRegistryBackedRows()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Signals");

            Assert.Equal(0, exit);
            Assert.Contains("## Signals", output);
            Assert.Contains("Known vulnerabilities", output);
            Assert.Contains("Dependencies with vulnerabilities", output);
            Assert.Contains("Deprecated dependencies", output);
            Assert.DoesNotContain("| Signals | Scope |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
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

public sealed class CommandExecutionSourceDiffFixture
{
    public int AddOne(int value)
    {
        return value + 1;
    }
}

/// <summary>
/// Two allocations at different depths: one on the body's own base column and
/// one nested inside a loop. The caret gesture must point exactly at both, which
/// is only possible because the caret block is hoisted out of the body indent.
/// </summary>
public sealed class CommandCaretGestureFixture
{
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
}

public sealed class CommandInitializerOnlyFixture
{
    public int Value = 42;
}

public static class FactsTableFixture
{
    public static object BoxInt(int value) => value;
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
