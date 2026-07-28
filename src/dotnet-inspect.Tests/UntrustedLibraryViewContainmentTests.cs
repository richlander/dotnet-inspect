using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Fixtures;
using DotnetInspector.CommandLine;
using CoreFactory = DotnetInspector.Core.HttpClientFactory;
using DotnetInspector.Models;
using DotnetInspector.Views;
using ILInspector.Research;
using Markout;

namespace DotnetInspector.Tests;

/// <summary>
/// Gate for the <c>library</c> channel (issue #3319). The library view renders
/// untrusted assembly metadata — async, P/Invoke, and extension method names,
/// their declaring types, and the P/Invoke module — into Markdown table cells.
/// A name carrying a vertical tab, ANSI escape, or bidi override escapes its
/// cell and injects text that reads as genuine tool output.
///
/// This gate asserts on the <em>rendered output text</em> rather than on the
/// view object graph, so unlike a reflective walk it cannot be evaded by a
/// hostile string that is reachable only through a field, an internal type, a
/// dictionary key, or a lazily-rendered string.
/// </summary>
[Collection("Console")]
public class UntrustedLibraryViewContainmentTests : IDisposable
{
    private const string Hazard = "\v";
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"HostileLibrary_{Guid.NewGuid():N}.dll");

    public UntrustedLibraryViewContainmentTests() => WriteHostileLibrary(_path);

    public void Dispose()
    {
        try { File.Delete(_path); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("Async Methods")]
    [InlineData("P/Invoke Methods")]
    [InlineData("Extension Methods")]
    public async Task LibrarySection_WithHostileMetadataNames_RendersNoHazard(string section)
    {
        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = _path,
                IncludeSections = [section],
                Markdown = true,
                Verbosity = Verbosity.Detailed,
            }));

        Assert.Equal(0, exit);

        // Non-vacuity: a section that rendered nothing would pass trivially.
        Assert.Contains("INJECTED", output, StringComparison.Ordinal);
        AssertNoHazard(output);
        AssertNoLineSplit(output);
    }

    /// <summary>
    /// The sections this fixture actually seeds, each with the marker that
    /// proves it rendered. The gate below derives both its section selection
    /// and its assertions from this one table, so a section cannot be added to
    /// the selection without also declaring the marker that keeps it honest.
    /// An earlier version listed Custom Attributes, Type Forwarders, Union
    /// Types, References, and Symbols as well; <see cref="WriteHostileLibrary"/>
    /// seeds none of them, so those five sections rendered empty and could not
    /// have failed however containment regressed (found by adversarial review).
    /// They are covered instead by
    /// <see cref="UntrustedStringLiteralContainmentTests"/>, which uses a
    /// compiled fixture — <see cref="System.Reflection.Emit.PersistedAssemblyBuilder"/>
    /// cannot emit manifest resources at all, and the attribute blobs it emits
    /// do not decode back.
    /// </summary>
    private static readonly (string Section, string Marker)[] SeededSections =
    [
        ("Async Methods", "INJECTEDASYNC"),
        ("P/Invoke Methods", "INJECTEDPI"),
        ("Extension Methods", "INJECTEDEXT"),
    ];

    [Fact]
    public async Task LibraryAllSections_WithHostileMetadataNames_RendersNoHazard()
    {
        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = _path,
                IncludeSections = SeededSections.Select(s => s.Section).ToHashSet(StringComparer.OrdinalIgnoreCase),
                Markdown = true,
                Verbosity = Verbosity.Detailed,
            }));

        Assert.Equal(0, exit);

        // Per-channel non-vacuity. A single global "INJECTED" check would be
        // satisfied by the async section alone, letting a regression confined to
        // another channel pass vacuously (found by adversarial review).
        foreach (var (section, marker) in SeededSections)
        {
            Assert.True(
                output.Contains(marker, StringComparison.Ordinal),
                $"section '{section}' rendered nothing hostile, so this gate proves nothing about it");
        }

        AssertNoHazard(output);
        AssertNoLineSplit(output);
    }

    /// <summary>
    /// Line-integrity oracle. <see cref="AssertNoHazard"/> deliberately permits
    /// CR and LF because they are legitimate structure in rendered Markdown, so
    /// on its own it would accept a regression that rewrote the hazard as a raw
    /// newline — which is exactly the line injection this issue is about (found
    /// by adversarial review). Every hostile name here has the shape
    /// <c>prefix + hazard + INJECTED…</c>, and every correct containment spelling
    /// keeps the two halves adjacent: sanitization yields <c>prefix_INJECTED…</c>
    /// and visible escaping yields <c>prefix\u000BINJECTED…</c>. So the character
    /// immediately before each marker must still be an identifier character.
    /// </summary>
    private static void AssertNoLineSplit(string output)
    {
        for (int i = output.IndexOf("INJECTED", StringComparison.Ordinal); i >= 0;
             i = output.IndexOf("INJECTED", i + 1, StringComparison.Ordinal))
        {
            char before = i == 0 ? '\0' : output[i - 1];
            if (!char.IsLetterOrDigit(before) && before != '_')
            {
                Assert.Fail(
                    $"hostile name was split before its marker at index {i} "
                    + $"(preceding character U+{(int)before:X4}): "
                    + output.Substring(Math.Max(0, i - 60), Math.Min(120, output.Length - Math.Max(0, i - 60))));
            }
        }
    }

    private static void AssertNoHazard(string output)
    {
        HostileOutputAssert.NoRenderingHazard(output, "UntrustedLibraryViewContainmentTests");
    }

    /// <summary>
    /// The harness spells the hazard set out rather than calling
    /// <c>CSharpIdentifier.IsRenderingHazard</c>, so that a wrong answer from
    /// the product cannot make this gate agree with it. Line feed and carriage
    /// return are legitimate structure in rendered Markdown, so only the
    /// remaining controls and the bidi set are hazards here.
    /// </summary>
    private static bool IsHazard(char c) => HostileOutputAssert.IsForbidden(c);

    private static void WriteHostileLibrary(string path)
    {
        var name = new AssemblyName("HostileLibrary") { Version = new Version(1, 0, 0, 0) };
        var ab = new PersistedAssemblyBuilder(name, typeof(object).Assembly);
        var module = ab.DefineDynamicModule("HostileLibrary");

        var asyncCtor = typeof(AsyncStateMachineAttribute).GetConstructor([typeof(Type)])!;
        var extensionCtor = typeof(ExtensionAttribute).GetConstructor(Type.EmptyTypes)!;

        // Async method: hostile method name, declaring type, and namespace.
        var asyncType = module.DefineType(
            $"Hostile{Hazard}INJECTEDASYNC.Async{Hazard}INJECTEDASYNC",
            TypeAttributes.Public | TypeAttributes.Class);
        var asyncMethod = asyncType.DefineMethod(
            $"DoWork{Hazard}INJECTEDASYNC", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        asyncMethod.GetILGenerator().Emit(OpCodes.Ret);
        asyncMethod.SetCustomAttribute(new CustomAttributeBuilder(asyncCtor, [typeof(object)]));
        asyncType.CreateType();

        // Extension method: hostile method name and extension class.
        var extensionType = module.DefineType(
            $"Hostile{Hazard}INJECTEDEXT.Extensions{Hazard}INJECTEDEXT",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.Abstract);
        extensionType.SetCustomAttribute(new CustomAttributeBuilder(extensionCtor, []));
        var extensionMethod = extensionType.DefineMethod(
            $"Extend{Hazard}INJECTEDEXT",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(string)]);
        extensionMethod.DefineParameter(1, ParameterAttributes.None, $"value{Hazard}INJECTEDEXT");
        extensionMethod.GetILGenerator().Emit(OpCodes.Ret);
        extensionMethod.SetCustomAttribute(new CustomAttributeBuilder(extensionCtor, []));
        extensionType.CreateType();

        // P/Invoke: hostile method name and module name.
        var nativeType = module.DefineType(
            $"Hostile{Hazard}INJECTEDPI.Native{Hazard}INJECTEDPI",
            TypeAttributes.Public | TypeAttributes.Class);
        var pinvoke = nativeType.DefinePInvokeMethod(
            $"Call{Hazard}INJECTEDPI",
            $"module{Hazard}INJECTEDPI.dll",
            $"entry{Hazard}INJECTEDPI",
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.PinvokeImpl,
            CallingConventions.Standard,
            typeof(int),
            Type.EmptyTypes,
            CallingConvention.StdCall,
            CharSet.Ansi);
        pinvoke.SetImplementationFlags(MethodImplAttributes.PreserveSig);
        nativeType.CreateType();

        ab.Save(path);
    }
}

/// <summary>
/// Gate for the type-spelling channels found in the sixth adversarial round
/// (issue #3319): a hostile type name reaches output as a parameter type, a
/// return type, a base type, an implemented interface, and — on the error path —
/// as a "Did you mean:" suggestion. Like its sibling above, this gate asserts on
/// rendered output text, so it cannot be evaded by a hostile string that the
/// reflective view walk cannot reach.
/// </summary>
[Collection("Console")]
public class UntrustedTypeSpellingContainmentTests : IDisposable
{
    private const string Hazard = "\v";
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"HostileSpelling_{Guid.NewGuid():N}.dll");

    public UntrustedTypeSpellingContainmentTests() => WriteHostileAssembly(_path);

    public void Dispose()
    {
        try { File.Delete(_path); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("NormalType")]   // hostile parameter and return type in the signature
    [InlineData("DerivedType")]  // hostile base type and implemented interface
    public async Task TypeView_WithHostileTypeSpelling_RendersNoHazard(string typeName)
    {
        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => ApiCommand.ExecuteAsync(new ApiOptions
            {
                AssemblyPath = _path,
                TypeName = typeName,
                Verbosity = Verbosity.Detailed,
            }));

        Assert.Equal(0, exit);
        Assert.Contains("INJECTED", output, StringComparison.Ordinal);
        AssertNoHazard(output);
        AssertNoLineSplit(output);
    }

    [Fact]
    public async Task TypeNotFoundSuggestions_WithHostileTypeNames_RenderNoHazard()
    {
        var (_, _, error) = await ConsoleCapture.RunAsync(
            () => ApiCommand.ExecuteAsync(new ApiOptions
            {
                AssemblyPath = _path,
                TypeName = "HostileINJECTE",
                Verbosity = Verbosity.Detailed,
            }));

        // Non-vacuity: the suggestion list must actually echo the hostile names.
        Assert.Contains("INJECTED", error, StringComparison.Ordinal);
        AssertNoHazard(error);
        AssertNoLineSplit(error);
    }

    /// <summary>
    /// Line-integrity oracle; see the sibling explanation on
    /// <see cref="UntrustedLibraryViewContainmentTests"/>. <see cref="AssertNoHazard"/>
    /// permits CR and LF as legitimate Markdown structure, so it alone would
    /// accept a regression that rewrote the hazard as a raw newline.
    /// </summary>
    private static void AssertNoLineSplit(string output)
    {
        for (int i = output.IndexOf("INJECTED", StringComparison.Ordinal); i >= 0;
             i = output.IndexOf("INJECTED", i + 1, StringComparison.Ordinal))
        {
            char before = i == 0 ? '\0' : output[i - 1];
            if (!char.IsLetterOrDigit(before) && before != '_')
            {
                Assert.Fail(
                    $"hostile name was split before its marker at index {i} "
                    + $"(preceding character U+{(int)before:X4})");
            }
        }
    }

    private static void AssertNoHazard(string output)
    {
        HostileOutputAssert.NoRenderingHazard(output, "UntrustedLibraryViewContainmentTests");
    }

    /// <summary>
    /// Spelled out rather than calling the product's own hazard predicate, so a
    /// wrong answer from the product cannot make this gate agree with it.
    /// </summary>
    private static bool IsHazard(char c) => HostileOutputAssert.IsForbidden(c);

    private static void WriteHostileAssembly(string path)
    {
        var name = new AssemblyName("HostileSpelling") { Version = new Version(1, 0, 0, 0) };
        var ab = new PersistedAssemblyBuilder(name, typeof(object).Assembly);
        var module = ab.DefineDynamicModule("HostileSpelling");

        var hostile = module.DefineType(
            $"Hostile{Hazard}INJECTED", TypeAttributes.Public | TypeAttributes.Class);
        hostile.DefineDefaultConstructor(MethodAttributes.Public);
        var hostileType = hostile.CreateType();

        var hostileInterface = module.DefineType(
            $"IHostile{Hazard}INJECTED",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        var hostileInterfaceType = hostileInterface.CreateType();

        // Hostile type name as both a parameter type and a return type.
        var normal = module.DefineType("NormalType", TypeAttributes.Public | TypeAttributes.Class);
        var method = normal.DefineMethod(
            "Method2", MethodAttributes.Public, hostileType, [hostileType]);
        method.DefineParameter(1, ParameterAttributes.None, "arg0");
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        normal.CreateType();

        // Hostile type name as a base type and as an implemented interface.
        var derived = module.DefineType(
            "DerivedType", TypeAttributes.Public | TypeAttributes.Class, hostileType);
        derived.AddInterfaceImplementation(hostileInterfaceType);
        derived.CreateType();

        ab.Save(path);
    }
}

/// <summary>
/// Gate for the <c>--il-offset</c> projection sections of the library view
/// (issue #3319). These sections render assembly-derived text — the caught
/// exception type, the allocated and churned types, the callee, the member
/// signature, and the decoded instruction operand — and were the one family of
/// <c>LibraryInspectionView</c> rows that reached output without containment,
/// because unlike the performance and resource-triage rows they do not route
/// through <see cref="DotnetInspector.Output.MarkoutInline"/>.
///
/// The projection is a plain settable model, so this gate drives the view
/// directly rather than through a synthesized assembly: <c>PersistedAssemblyBuilder</c>
/// cannot emit the IL-offset analysis state these sections consume.
/// </summary>
public class UntrustedILOffsetContainmentTests
{
    private const string Hazard = "\v";

    [Fact]
    public void ILOffsetSections_WithHostileMetadataText_RenderNoHazard()
    {
        string H(string tag) => $"Hostile{Hazard}INJECTED{tag}";

        var inspection = new LibraryInspection
        {
            FileName = "hostile.dll",
            ILOffset = new ILOffsetProjection
            {
                MemberContext = new ILOffsetMemberContext
                {
                    Type = H("TYPE"),
                    Member = H("MEMBER"),
                    Signature = H("SIG"),
                },
                InstructionContext = new ILOffsetInstructionContext
                {
                    Operand = H("OPERAND"),
                },
                ExceptionContext = [new ILOffsetExceptionContext { Region = 0, CaughtType = H("CAUGHT") }],
                CallsiteContext = new ILOffsetCallsiteContext { Callee = H("CALLEE") },
                ReturnAddressContext = new ILOffsetReturnAddressContext { Callee = H("RETCALLEE") },
                AllocationContext = [new ILOffsetAllocationContext { AllocatedType = H("ALLOC"), ChurnedType = H("CHURN") }],
                SafetyContext = [new ILOffsetSafetyContext { Operation = H("SAFETY") }],
                CostContext = [new ILOffsetCostContext { Operation = H("COST") }],
            },
        };

        var output = MarkoutSerializer.Serialize(
            new LibraryInspectionView(inspection), InspectionContext.Default);

        // Per-channel non-vacuity: each section must actually have rendered its
        // hostile text, or its containment would be asserted vacuously.
        foreach (var marker in new[]
                 {
                     "INJECTEDTYPE", "INJECTEDMEMBER", "INJECTEDSIG", "INJECTEDOPERAND",
                     "INJECTEDCAUGHT", "INJECTEDCALLEE", "INJECTEDRETCALLEE",
                     "INJECTEDALLOC", "INJECTEDCHURN", "INJECTEDSAFETY", "INJECTEDCOST",
                 })
        {
            Assert.Contains(marker, output, StringComparison.Ordinal);
        }

        HostileOutputAssert.NoRenderingHazard(output, "UntrustedLibraryViewContainmentTests");

        // Line integrity: the hazard must not have become a raw newline, which
        // the hazard scan above deliberately permits as Markdown structure.
        for (int i = output.IndexOf("INJECTED", StringComparison.Ordinal); i >= 0;
             i = output.IndexOf("INJECTED", i + 1, StringComparison.Ordinal))
        {
            char before = i == 0 ? '\0' : output[i - 1];
            Assert.True(
                char.IsLetterOrDigit(before) || before == '_',
                $"hostile text was split before its marker at index {i} (U+{(int)before:X4})");
        }
    }
}

/// <summary>
/// Gate for the string-literal channels found in the seventh adversarial round
/// (issue #3319): a parameter default value, an <c>[Obsolete]</c> message, and a
/// custom attribute argument are all attacker-controlled text rendered inside a
/// C# string literal.
///
/// The escapers guarding them tested <see cref="char.IsControl(char)"/>, which is
/// <see langword="false"/> for the Unicode bidi overrides (they are category
/// <c>Cf</c>, not <c>Cc</c>) — so <c>U+202E</c> reached the terminal raw and
/// produced literal Trojan Source text inside otherwise plausible output.
///
/// This gate uses a compiler-produced fixture rather than an emitted one: the
/// attribute-message blob a <c>PersistedAssemblyBuilder</c> writes does not
/// decode back, so an emitted assembly renders a bare <c>[Obsolete]</c> and
/// would gate the channel vacuously.
/// </summary>
[Collection("Console")]
public class UntrustedStringLiteralContainmentTests
{
    /// <summary>
    /// Each case is a separate escaper. Attribute and default-value text goes
    /// through the metadata literal escapers, method-body literals go through
    /// the decompiler printer, and doc text goes through the doc model. They
    /// were fixed in three different rounds because a green result on one says
    /// nothing about the others.
    /// </summary>
    public static TheoryData<string, string?, string[]> LiteralChannels() => new()
    {
        {
            "HostileLiterals",
            "Decompiled Source",
            new[] { "INJECTEDDEFAULT", "INJECTEDOBSOLETE", "INJECTEDATTRIBUTE" }
        },
        {
            "HostileBodyLiterals",
            "Decompiled Source",
            new[] { "INJECTEDBODYLITERAL" }
        },
        {
            // Doc text renders in the member listing, not behind a section flag.
            "HostileDocs",
            null,
            new[] { "INJECTEDTYPEDOC", "INJECTEDMEMBERDOC" }
        },
    };

    [Theory]
    [MemberData(nameof(LiteralChannels))]
    public async Task StringLiteralChannels_WithHostileText_RenderNoHazard(
        string typeName,
        string? section,
        string[] markers)
    {
        string[] args = section is null
            ? ["member", typeName, "--library", FixtureCatalog.HostileLiterals.AssemblyPath(), "-v:d"]
            : ["type", typeName, "--library", FixtureCatalog.HostileLiterals.AssemblyPath(), "-S", section, "-v:d"];

        var (exit, output, _) = await RunAppAsync(args);

        Assert.Equal(0, exit);

        // Per-channel non-vacuity: each hostile literal must actually have
        // rendered, or the hazard scan below would pass on output that never
        // carried it.
        foreach (var marker in markers)
        {
            Assert.Contains(marker, output, StringComparison.Ordinal);
        }

        AssertNoHazard(output);
        AssertNoLineSplit(output, markers);
    }

    /// <summary>
    /// The hazard scan permits '\n' by construction, so on its own it accepts
    /// the exact injection this issue is about: a containment that rewrote a
    /// hazard as a newline would pass it. Correct containment leaves the marker
    /// welded to its prefix, so the character before it is never a line break.
    /// </summary>
    private static void AssertNoLineSplit(string output, string[] markers)
    {
        foreach (var marker in markers)
        {
            int at = output.IndexOf(marker, StringComparison.Ordinal);
            while (at > 0)
            {
                char before = output[at - 1];
                Assert.False(
                    before is '\n' or '\r' or '\u0085' or '\u2028' or '\u2029',
                    $"{marker} starts a new line: containment split the text it was embedded in");
                at = output.IndexOf(marker, at + marker.Length, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// An oracle independent of the product: this repeats the hazard set from
    /// the issue rather than calling <c>CSharpIdentifier.IsRenderingHazard</c>,
    /// so tampering the product predicate cannot silently make this pass.
    /// </summary>
    private static void AssertNoHazard(string output)
    {
        HostileOutputAssert.NoRenderingHazard(output, "UntrustedLibraryViewContainmentTests");
    }

    /// <summary>
    /// Assembly-level attribute text (Company, Product, Copyright) reaches the
    /// "Library Info" table. It is a separate channel from the member-level
    /// literals above because it never passes through a C# literal escaper --
    /// it is read straight from the attribute blob into a view property.
    /// </summary>
    [Fact]
    public async Task LibraryInfo_WithHostileAssemblyAttributes_RendersNoHazard()
    {
        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixtureCatalog.HostileLiterals.AssemblyPath(),
                IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Library Info" },
                Markdown = true,
                Verbosity = Verbosity.Detailed,
            }));

        Assert.Equal(0, exit);

        string[] markers = ["INJECTEDCOMPANY", "INJECTEDPRODUCT", "INJECTEDCOPYRIGHT"];
        foreach (var marker in markers)
        {
            Assert.Contains(marker, output, StringComparison.Ordinal);
        }

        AssertNoHazard(output);
        AssertNoLineSplit(output, markers);
    }

    /// <summary>Mirrors the Program.cs entry point, so this gate exercises the
    /// same argument path a user drives.</summary>
    private static async Task<(int exit, string output, string error)> RunAppAsync(params string[] args)
    {
        return await ConsoleCapture.RunAsync(async () =>
        {
            CoreFactory.Initialize(offline: true);
            CoreFactory.ResetSharedForTesting();
            args = CommandLineBuilder.PreprocessArgs(args);
            var root = CommandLineBuilder.CreateRootCommand();
            return await root.Parse(args).InvokeAsync();
        });
    }
}

/// <summary>
/// Gate for the <c>package</c> channel (issue #3319). A .nupkg is untrusted
/// input in exactly the way an assembly is: its nuspec text and ZIP entry names
/// are chosen by whoever built it, and both reach rendered Markdown.
/// </summary>
/// <remarks>
/// The package is built here rather than checked in because a .nupkg carrying
/// these characters is a binary blob whose payload would be invisible in review,
/// and because the nuspec cannot be authored as an MSBuild-packed project: XML
/// 1.0 cannot represent U+000B at all, so the vertical-tab case only exists in a
/// hand-written archive.
/// </remarks>
[Collection("Console")]
public class UntrustedPackageContainmentTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"HostilePkg_{Guid.NewGuid():N}");
    private readonly string _path;

    public UntrustedPackageContainmentTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "Hostile.Pkg.1.0.0.nupkg");
        WriteHostilePackage(_path);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private static void WriteHostilePackage(string path)
    {
        const string Bidi = "\u202E";
        var nuspec = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>Pkg{Bidi}INJECTEDPKGID</id>
                <version>1.0.0</version>
                <authors>Auth{Bidi}INJECTEDAUTHOR</authors>
                <description>Desc{Bidi}INJECTEDDESC here.</description>
                <dependencies>
                  <group targetFramework="net8.0">
                    <dependency id="Dep{Bidi}INJECTEDDEPID" version="1.0.0" />
                  </group>
                </dependencies>
              </metadata>
            </package>
            """;

        using var archive = new System.IO.Compression.ZipArchive(
            File.Create(path), System.IO.Compression.ZipArchiveMode.Create);

        Write(archive, "Hostile.Pkg.nuspec", nuspec);
        Write(archive, "lib/net8.0/Good.dll", "MZ");
        // The TFM is a folder name, so it is attacker-chosen too.
        Write(archive, "lib/net9.0\u000BINJECTEDTFM/Good.dll", "MZ");
        // A ZIP entry name is not XML, so it can carry the vertical tab too.
        Write(archive, $"docs/Path{Bidi}INJECTEDPATH.md", "# doc");
        Write(archive, "docs/Vtab\u000BINJECTEDVPATH.md", "# doc");

        static void Write(System.IO.Compression.ZipArchive archive, string name, string content)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(content);
        }
    }

    [Theory]
    [InlineData(Verbosity.Normal)]
    [InlineData(Verbosity.Detailed)]
    public async Task PackageMetadata_WithHostileNuspec_RendersNoHazard(Verbosity verbosity)
    {
        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(new InspectionOptions
            {
                PackageArgs = [_path],
                Verbosity = verbosity,
                TipLevel = TipLevel.Quiet,
            }));

        Assert.Equal(0, exit);

        // Per-channel non-vacuity. The description renders as a prose block and
        // the authors as a table cell; they take different containment paths.
        HostileOutputAssert.MarkersRendered(
            output, "package", "INJECTEDAUTHOR", "INJECTEDDESC", "INJECTEDPKGID");

        HostileOutputAssert.NoRenderingHazard(output, "package");
    }

    /// <summary>
    /// The file tree the <c>--layout</c> flag renders is a separate channel from
    /// the package's file table, and it renders the ZIP entry name straight into
    /// the tree gutter.
    /// </summary>
    [Fact]
    public async Task PackageLayout_WithHostileEntryNames_RendersNoHazard()
    {
        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(new InspectionOptions
            {
                PackageArgs = [_path],
                ListLayout = true,
                TipLevel = TipLevel.Quiet,
            }));

        Assert.Equal(0, exit);

        foreach (var marker in new[] { "INJECTEDPATH", "INJECTEDVPATH" })
        {
            Assert.True(
                output.Contains(marker, StringComparison.Ordinal),
                $"'{marker}' never rendered, so this gate proves nothing about its channel");
        }

        HostileOutputAssert.NoRenderingHazard(output, "package --layout");
    }

}

/// <summary>
/// Gate for the Symbols and Non-normalized Paths channels (issue #3319).
/// </summary>
/// <remarks>
/// The CodeView and SourceLink records these render come out of the inspected
/// binary, so the build path, repository URL, and publisher are chosen by
/// whoever produced the assembly. A crafted path fabricated another list line,
/// recolored the terminal, and reordered visible text.
///
/// This gates the section objects rather than an end-to-end run: synthesizing a
/// PDB whose CodeView path carries these characters is not something the
/// fixture builder can currently do. That is a real limit on this gate — it
/// proves the containment is on the properties every producer assigns through,
/// not that a hostile PDB on disk renders clean.
/// </remarks>
public class UntrustedSymbolsSectionContainmentTests
{
    [Fact]
    public void SymbolsSection_WithHostilePaths_ContainsEveryRenderedField()
    {
        const string Hostile = "/a/\u000BINJVT\u001B[31m\nINJNL\u2028INJLS\u202EINJRLO.pdb";

        var section = new SymbolsSection
        {
            PdbPath = Hostile,
            PdbLocation = Hostile,
            PdbFormat = Hostile,
            Builder = Hostile,
            Publisher = Hostile,
            Repository = Hostile,
            RepositoryUrl = Hostile,
            Signature = Hostile,
            SourceLink = Hostile,
            SymbolServer = Hostile,
        };

        foreach (var text in new[]
        {
            section.PdbPath, section.PdbLocation, section.PdbFormat, section.Builder,
            section.Publisher, section.Repository, section.RepositoryUrl,
            section.Signature, section.SourceLink, section.SymbolServer,
        })
        {
            Assert.NotNull(text);
            HostileOutputAssert.MarkersRendered(text!, "Symbols", "INJVT", "INJNL", "INJLS", "INJRLO");
            Assert.DoesNotContain(text!, c => c is '\n' or '\r' || HostileOutputAssert.IsForbidden(c));
        }
    }
}

/// <summary>
/// SourceLink URLs and document paths are chosen by whoever built the inspected
/// assembly, so they are untrusted text on exactly the same footing as a
/// metadata name (issue #3319). They reach three separate row records —
/// <c>SourceFileRow</c>, <c>TypeSourceFileRow</c>, and
/// <c>MemberSourceLocationRow</c> — through three different commands, and each
/// was uncontained until this gate existed.
/// </summary>
/// <remarks>
/// The hostile map is a real SourceLink blob emitted by the compiler into the
/// fixture's portable PDB. It is JSON rather than XML, which is why it can
/// carry U+000B at all: the fixture's other hostile text has to be spelled in
/// C# because XML 1.0 cannot encode a vertical tab even as a character
/// reference.
/// </remarks>
[Collection("Console")]
public class UntrustedSourceLinkContainmentTests
{
    public static TheoryData<string[]> SourceLinkChannels() => new()
    {
        // SourceFileRow.
        new[] { "library", FixtureCatalog.HostileLiterals.AssemblyPath(), "-S", "Source Files" },
        // TypeSourceFileRow.
        new[]
        {
            "type", "HostileLiterals", "--library", FixtureCatalog.HostileLiterals.AssemblyPath(),
            "-S", "Source Files",
        },
        // MemberSourceLocationRow, which also carries the document path in File.
        new[]
        {
            "member", "HostileLiterals", "Marked:1", "--library", FixtureCatalog.HostileLiterals.AssemblyPath(),
            "-S", "Source Locations",
        },
    };

    [Theory]
    [MemberData(nameof(SourceLinkChannels))]
    public async Task SourceLinkChannels_WithHostileMap_RenderNoHazard(string[] args)
    {
        var (exit, output, _) = await RunAppAsync(args);

        Assert.Equal(0, exit);

        // Non-vacuity: the hostile URL must actually have rendered. Without
        // this the hazard scan below passes on a "no SourceLink data" message.
        HostileOutputAssert.MarkersRendered(output, string.Join(' ', args), "INJECTEDSOURCELINK");
        HostileOutputAssert.NoRenderingHazard(output, string.Join(' ', args));
    }

    private static async Task<(int exit, string output, string error)> RunAppAsync(params string[] args)
    {
        return await ConsoleCapture.RunAsync(async () =>
        {
            CoreFactory.Initialize(offline: true);
            CoreFactory.ResetSharedForTesting();
            args = CommandLineBuilder.PreprocessArgs(args);
            var root = CommandLineBuilder.CreateRootCommand();
            return await root.Parse(args).InvokeAsync();
        });
    }
}

/// <summary>
/// Gate for the relationship and search commands found in the twelfth
/// adversarial round (issue #3319): <c>extensions</c> rows, <c>find --members</c>
/// rows, <c>depends</c> tree labels, and the IL <c>ldstr</c> operand.
/// </summary>
/// <remarks>
/// These four are grouped because they are the channels that no view-shaped
/// gate could have reached: two build their rows in an output formatter rather
/// than a view, one writes labels straight into a terminal tree gutter, and one
/// lives in a metadata escaper that had restated the hazard set instead of
/// sharing it.
/// </remarks>
[Collection("Console")]
public class UntrustedRelationshipContainmentTests : IDisposable
{
    private const string Hazard = "\u202E";

    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"HostileRel_{Guid.NewGuid():N}.dll");

    public UntrustedRelationshipContainmentTests() => WriteHostileAssembly(_path);

    public void Dispose()
    {
        try { File.Delete(_path); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string, string[]> Channels() => new()
    {
        { "extensions", ["INJECTEDEXTCLASS", "INJECTEDEXTMETHOD"] },
        // INJECTEDPARAM only ever appears inside the composed Signature column,
        // so it fails independently of the Member and Type columns.
        { "find", ["INJECTEDEXTMETHOD", "INJECTEDEXTCLASS", "INJECTEDPARAM"] },
        { "depends", ["INJECTEDBASE"] },
    };

    [Theory]
    [MemberData(nameof(Channels))]
    public async Task RelationshipChannels_WithHostileNames_RenderNoHazard(string channel, string[] markers)
    {
        string[] args = channel switch
        {
            "extensions" => ["extensions", "String", "--library", _path],
            "find" => ["find", "*", "--members", "--library", _path],
            _ => ["depends", $"Derived{Hazard}INJECTEDDERIVED", "--library", _path],
        };

        var (exit, output, _) = await RunAppAsync(args);

        Assert.Equal(0, exit);
        HostileOutputAssert.MarkersRendered(output, channel, markers);
        HostileOutputAssert.NoRenderingHazard(output, channel);
    }

    /// <summary>
    /// The IL view is a separate escaper from the C# literal escapers and from
    /// the decompiled-source printer, so a green result on either says nothing
    /// about it. The literal here is compiler-produced: an emitted one does not
    /// decode back.
    /// </summary>
    [Fact]
    public async Task IlStringOperand_WithHostileLiteral_RendersNoHazard()
    {
        var (exit, output, _) = await RunAppAsync(
            "member",
            "HostileBodyLiterals",
            "Literal:1",
            "--library",
            FixtureCatalog.HostileLiterals.AssemblyPath(),
            "-S",
            "IL");

        Assert.Equal(0, exit);
        HostileOutputAssert.MarkersRendered(output, "IL", "ldstr", "INJECTEDBODYLITERAL");
        HostileOutputAssert.NoRenderingHazard(output, "IL");
    }

    private static void WriteHostileAssembly(string path)
    {
        var name = new AssemblyName("HostileRel") { Version = new Version(1, 0, 0, 0) };
        var ab = new PersistedAssemblyBuilder(name, typeof(object).Assembly);
        var module = ab.DefineDynamicModule("HostileRel");
        var extensionCtor = typeof(ExtensionAttribute).GetConstructor(Type.EmptyTypes)!;

        var extensionType = module.DefineType(
            $"RelNs.Ext{Hazard}INJECTEDEXTCLASS",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.Abstract);
        extensionType.SetCustomAttribute(new CustomAttributeBuilder(extensionCtor, []));
        var extensionMethod = extensionType.DefineMethod(
            $"Extend{Hazard}INJECTEDEXTMETHOD",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(string)]);
        extensionMethod.DefineParameter(1, ParameterAttributes.None, "value");
        extensionMethod.GetILGenerator().Emit(OpCodes.Ret);
        extensionMethod.SetCustomAttribute(new CustomAttributeBuilder(extensionCtor, []));
        extensionType.CreateType();

        var baseType = module.DefineType(
            $"RelNs.Base{Hazard}INJECTEDBASE",
            TypeAttributes.Public | TypeAttributes.Class);
        var built = baseType.CreateType();

        var paramType = module.DefineType(
            $"RelNs.Param{Hazard}INJECTEDPARAM",
            TypeAttributes.Public | TypeAttributes.Class);
        var builtParam = paramType.CreateType();

        var derived = module.DefineType(
            $"Derived{Hazard}INJECTEDDERIVED",
            TypeAttributes.Public | TypeAttributes.Class,
            built);
        var takesHostile = derived.DefineMethod(
            "TakesHostile", MethodAttributes.Public, typeof(void), [builtParam]);
        takesHostile.GetILGenerator().Emit(OpCodes.Ret);
        derived.CreateType();

        ab.Save(path);
    }

    private static async Task<(int exit, string output, string error)> RunAppAsync(params string[] args)
    {
        return await ConsoleCapture.RunAsync(async () =>
        {
            CoreFactory.Initialize(offline: true);
            CoreFactory.ResetSharedForTesting();
            args = CommandLineBuilder.PreprocessArgs(args);
            var root = CommandLineBuilder.CreateRootCommand();
            return await root.Parse(args).InvokeAsync();
        });
    }
}
