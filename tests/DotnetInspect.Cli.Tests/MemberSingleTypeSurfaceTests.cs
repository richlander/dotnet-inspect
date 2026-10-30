using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Sections;
using Collision.DotnetInspect.Cli.Tests;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public class MemberSingleTypeSurfaceTests
{
    public static TheoryData<string, int, bool> SectionCases()
    {
        var cases = new TheoryData<string, int, bool>();
        foreach (string section in (string[])
            [
                SectionNames.Signature,
                SectionNames.IL,
                SectionNames.CustomAttributes,
                SectionNames.ExceptionRegions,
                SectionNames.SourceLocations,
                SectionNames.FidelityCauses,
            ])
        {
            foreach (int ordinal in (int[])[1, 2, 3])
            {
                cases.Add(section, ordinal, false);
                cases.Add(section, ordinal, true);
            }
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(SectionCases))]
    public async Task Section_MatchesCompleteSurfaceRoute(
        string section,
        int ordinal,
        bool includeAll)
    {
        string[] args = MemberArgs(
            $"{nameof(SingleTypeSurfaceFixture.Overloaded)}:{ordinal}",
            includeAll);

        // Member-local sections load only the selected Type's declarations;
        // adding Calls keeps the complete surface. Two sections keep both
        // requests in the same Markdown section layout.
        string companion = section == SectionNames.Signature
            ? SectionNames.IL
            : SectionNames.Signature;
        string[] sections = ["-S", section, "-S", companion];
        var single = await RunCliAsync([.. args, .. sections]);
        var complete = await RunCliAsync(
            [.. args, .. sections, "-S", SectionNames.Calls]);

        Assert.Equal(complete.ExitCode, single.ExitCode);
        Assert.Equal(complete.Error, single.Error);
        Assert.Equal(
            SectionBlock(complete.Output, section),
            SectionBlock(single.Output, section));
    }

    [Theory]
    [InlineData(SectionNames.Signature)]
    [InlineData(SectionNames.IL)]
    [InlineData(SectionNames.CustomAttributes)]
    [InlineData(SectionNames.ExceptionRegions)]
    [InlineData(SectionNames.SourceLocations)]
    [InlineData(SectionNames.FidelityCauses)]
    public async Task Section_DoesNotBuildTheApiSurface(string section)
    {
        // The complete member route resolves the assembly's type forwarders
        // while building its API surface and reports that under --verbose.
        var result = await RunCliAsync(
            [
                .. MemberArgs(
                    $"{nameof(SingleTypeSurfaceFixture.Overloaded)}:2",
                    includeAll: false),
                "-S",
                section,
                "--verbose",
            ]);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("forwarded types", result.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MemberInventory_UsesSelectedTypeDeclarations(bool count)
    {
        string[] terminal = count
            ? ["-S", SectionNames.Overloads, "--count"]
            : ["--json"];
        var result = await RunCliAsync(
            [
                "member",
                typeof(SingleTypeSurfaceFixture).FullName!,
                "--library",
                typeof(SingleTypeSurfaceFixture).Assembly.Location,
                "-m",
                nameof(SingleTypeSurfaceFixture.Overloaded),
                .. terminal,
                "--verbose",
            ]);
        var complete = AssemblyReader.ExtractApiSurface(
            typeof(SingleTypeSurfaceFixture).Assembly.Location)!;
        var expected = complete.Types
            .Single(type => type.FullName == typeof(SingleTypeSurfaceFixture).FullName)
            .Members.Where(member => member.Name == nameof(SingleTypeSurfaceFixture.Overloaded))
            .Select(member => member.MetadataToken)
            .Order()
            .ToArray();

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("forwarded types", result.Error);
        if (count)
        {
            Assert.Equal(expected.Length.ToString(), result.Output.Trim());
        }
        else
        {
            using var document = JsonDocument.Parse(result.Output);
            Assert.Equal(
                expected,
                document.RootElement.GetProperty("members")
                    .EnumerateArray()
                    .Select(member => (int?)member.GetProperty("metadata_token").GetInt32())
                    .Order()
                    .ToArray());
        }
    }

    [Fact]
    public async Task JsonInventory_ExtensionMemberKeepsCompleteSurface()
    {
        var result = await RunCliAsync(
            [
                "member",
                typeof(SingleTypeSurfaceReceiver).FullName!,
                "--library",
                typeof(SingleTypeSurfaceReceiver).Assembly.Location,
                "-m",
                "SurfaceExtension",
                "--json",
                "--verbose",
            ]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("forwarded types", result.Error);
        using var document = JsonDocument.Parse(result.Output);
        var member = Assert.Single(
            document.RootElement.GetProperty("members").EnumerateArray());
        Assert.Equal("SurfaceExtension", member.GetProperty("name").GetString());
        Assert.Equal("extension-method", member.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task SuffixCollidingType_MatchesCompleteSurfaceRoute()
    {
        // Surface Type lookup also admits Collision.DotnetInspect.Cli.Tests
        // .SuffixWidget for this name, so the request keeps the complete route.
        string[] args =
        [
            "member",
            $"{typeof(SuffixWidget).FullName}.{nameof(SuffixWidget.Go)}:1",
            "--library",
            typeof(SuffixWidget).Assembly.Location,
            "-S",
            SectionNames.Signature,
            "-S",
            SectionNames.IL,
        ];

        var single = await RunCliAsync(args);
        var complete = await RunCliAsync([.. args, "-S", SectionNames.Calls]);

        Assert.Equal(complete.ExitCode, single.ExitCode);
        Assert.Equal(complete.Error, single.Error);
        foreach (string section in (string[])[SectionNames.Signature, SectionNames.IL])
        {
            Assert.Equal(
                SectionBlock(complete.Output, section),
                SectionBlock(single.Output, section));
        }
    }

    [Fact]
    public async Task GenericAritySibling_UsesSelectedTypeSurface()
    {
        // Surface Type lookup prefers the exact ArityWidget over the base-name
        // match ArityWidget`1, so the sibling does not force the complete route.
        string[] args =
        [
            "member",
            $"{typeof(ArityWidget).FullName}.{nameof(ArityWidget.Go)}:1",
            "--library",
            typeof(ArityWidget).Assembly.Location,
            "-S",
            SectionNames.Signature,
            "-S",
            SectionNames.IL,
        ];

        var single = await RunCliAsync([.. args, "--verbose"]);
        var complete = await RunCliAsync([.. args, "-S", SectionNames.Calls]);

        Assert.Equal(0, single.ExitCode);
        Assert.DoesNotContain("forwarded types", single.Error);
        Assert.Equal(complete.ExitCode, single.ExitCode);
        foreach (string section in (string[])[SectionNames.Signature, SectionNames.IL])
        {
            Assert.Equal(
                SectionBlock(complete.Output, section),
                SectionBlock(single.Output, section));
        }
    }

    [Theory]
    [InlineData(MalformedRow.NestedTypeCycle)]
    [InlineData(MalformedRow.ExportedTypeCycle)]
    [InlineData(MalformedRow.ExportedTypeOutOfRange)]
    public async Task MalformedUnrelatedRow_MatchesCompleteSurfaceRoute(
        MalformedRow row)
    {
        // A rejected name traversal elsewhere in the image keeps the complete
        // route, which reports the row and still renders the selected member.
        using var directory =
            new TemporaryTestDirectory("dotnet-inspect-single-type-");
        string library = Path.Combine(directory.FullName, "Malformed.dll");
        File.WriteAllBytes(library, MalformedImage(row));
        string[] args =
        [
            "member",
            "Ns.Widget.M:1",
            "--library",
            library,
            "-S",
            SectionNames.Signature,
            "-S",
            SectionNames.IL,
        ];

        var single = await RunCliAsync(args);
        var complete = await RunCliAsync([.. args, "-S", SectionNames.Calls]);

        Assert.Contains("IL_0000: ret", single.Output);
        Assert.Equal(complete.ExitCode, single.ExitCode);
        Assert.Equal(complete.Error, single.Error);
        foreach (string section in (string[])[SectionNames.Signature, SectionNames.IL])
        {
            Assert.Equal(
                SectionBlock(complete.Output, section),
                SectionBlock(single.Output, section));
        }
    }

    public enum MalformedRow
    {
        NestedTypeCycle,
        ExportedTypeCycle,
        ExportedTypeOutOfRange,
    }

    static byte[] MalformedImage(MalformedRow row)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Malformed.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Malformed"),
            new Version(1, 0, 0, 0),
            default,
            default,
            0,
            AssemblyHashAlgorithm.Sha1);
        var runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            metadata.GetOrAddBlob(
                new byte[] { 0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a }),
            default,
            default);
        var objectType = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"));

        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(0, returnType => returnType.Void(), _ => { });
        var ilStream = new BlobBuilder();
        var code = new InstructionEncoder(new BlobBuilder());
        code.OpCode(ILOpCode.Ret);
        int bodyOffset = new MethodBodyStreamEncoder(ilStream).AddMethodBody(code);

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var method = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset,
            default);
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            metadata.GetOrAddString("Ns"),
            metadata.GetOrAddString("Widget"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            method);

        switch (row)
        {
            case MalformedRow.NestedTypeCycle:
                var loop = metadata.AddTypeDefinition(
                    TypeAttributes.NestedPublic | TypeAttributes.Class,
                    default,
                    metadata.GetOrAddString("Loop"),
                    objectType,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(2));
                metadata.AddNestedType(loop, loop);
                break;
            case MalformedRow.ExportedTypeCycle:
                metadata.AddExportedType(
                    TypeAttributes.NestedPublic,
                    default,
                    metadata.GetOrAddString("Widget"),
                    MetadataTokens.ExportedTypeHandle(1),
                    0);
                break;
            case MalformedRow.ExportedTypeOutOfRange:
                metadata.AddExportedType(
                    TypeAttributes.NestedPublic,
                    default,
                    metadata.GetOrAddString("Other"),
                    MetadataTokens.ExportedTypeHandle(5),
                    0);
                break;
        }

        var image = new BlobBuilder();
        new ManagedPEBuilder(
                new PEHeaderBuilder(
                    imageCharacteristics:
                        Characteristics.Dll | Characteristics.ExecutableImage),
                new MetadataRootBuilder(metadata),
                ilStream)
            .Serialize(image);
        return image.ToArray();
    }

    [Fact]
    public async Task OutOfScopeType_KeepsCompleteSurfaceSuggestions()
    {
        // The ordinal engages the selected-Type surface, which is empty for a
        // hidden Type without --all; the complete surface then reports it.
        string[] args =
        [
            "member",
            $"{typeof(HiddenSingleTypeSurfaceFixture).FullName}."
                + $"{nameof(HiddenSingleTypeSurfaceFixture.Call)}:1",
            "--library",
            typeof(HiddenSingleTypeSurfaceFixture).Assembly.Location,
            "-S",
            SectionNames.IL,
        ];

        var result = await RunCliAsync(args);
        var complete = await RunCliAsync([.. args, "-S", SectionNames.Calls]);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Did you mean", result.Error + result.Output);
        Assert.Equal(complete.ExitCode, result.ExitCode);
        Assert.Equal(complete.Output, result.Output);
        Assert.Equal(complete.Error, result.Error);
    }

    static string[] MemberArgs(string member, bool includeAll) =>
    [
        "member",
        $"{typeof(SingleTypeSurfaceFixture).FullName}.{member}",
        "--library",
        typeof(SingleTypeSurfaceFixture).Assembly.Location,
        .. includeAll ? ["--all"] : Array.Empty<string>(),
    ];

    static string? SectionBlock(string output, string section)
    {
        string heading = $"## {section}";
        string[] lines = output.ReplaceLineEndings("\n").Split('\n');
        int start = Array.FindIndex(lines, line => line == heading);
        if (start < 0)
        {
            return null;
        }

        int end = Array.FindIndex(
            lines,
            start + 1,
            line => line.StartsWith("## ", StringComparison.Ordinal));
        return string.Join(
            '\n',
            lines[start..(end < 0 ? lines.Length : end)]).TrimEnd();
    }

    static Task<(int ExitCode, string Output, string Error)> RunCliAsync(
        params string[] args) =>
        ConsoleCapture.RunAsync(() =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            string[] processed =
                CommandLineBuilder.PreprocessArgs(args, root);
            return CommandLineBuilder.InvokeAsync(
                root.Parse(processed),
                processed);
        });
}

public static class SingleTypeSurfaceFixture
{
    [Obsolete("first")]
    public static int Overloaded(int value)
    {
        try
        {
            return checked(value * 2);
        }
        catch (OverflowException)
        {
            return 0;
        }
    }

    public static void Overloaded(string value)
    {
        try
        {
            Console.WriteLine(value);
        }
        finally
        {
            Console.WriteLine("done");
        }
    }

    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void Overloaded(uint value, params object[] rest) =>
        Console.WriteLine(value + rest.Length);

    internal static void Overloaded(string first, string second) =>
        Console.WriteLine(first + second);
}

internal static class HiddenSingleTypeSurfaceFixture
{
    public static void Call() =>
        Console.WriteLine("hidden type");
}

public static class SuffixWidget
{
    public static string Go() => "exact";
}

public static class ArityWidget
{
    public static string Go() => "plain";
}

public static class ArityWidget<T>
{
    public static string Go() => typeof(T).Name;
}

public sealed class SingleTypeSurfaceReceiver;

public static class SingleTypeSurfaceExtensions
{
    public static int SurfaceExtension(this SingleTypeSurfaceReceiver receiver) => 7;
}
