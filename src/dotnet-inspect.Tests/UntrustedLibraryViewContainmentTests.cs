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
using DotnetInspector.Output;
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

    /// <summary>
    /// The Type Info section's <c>Type Parameters</c> row, which is its own
    /// channel and not covered by the type-spelling cases above.
    /// <para>
    /// The row is producer-contained, but one level further out than the other
    /// columns: <c>BuildTypeView</c> composes the summary from
    /// <see cref="TypeParameter.DisplayName"/>, and it is that property -- not
    /// the renderer and not <c>Name</c>, which stays raw as identity -- that
    /// calls <c>ContainComposedName</c>. Because the containment sits on a
    /// display projection rather than at the point of use, nothing about
    /// <c>BuildTypeView</c> shows that it happened, and
    /// <c>MarkoutRowContainmentTests</c> pins the column as
    /// <c>NotSelfContaining</c>, which does not distinguish a contained
    /// producer from a genuine residual. So the only thing that would notice
    /// <c>DisplayName</c> losing its containment is a test that reads the
    /// rendered row. This is that test: deleting the <c>ContainComposedName</c>
    /// call compiles cleanly and fails here, and here alone.
    /// </para>
    /// <para>
    /// This section also has hotter exposure than the inline type-parameter
    /// field it shares a residual entry with: that field is <c>topFieldsOnly</c>
    /// (quiet), while Type Info renders whenever member detail is off.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TypeInfoSection_WithHostileTypeParameterName_RendersNoHazard()
    {
        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => ApiCommand.ExecuteAsync(new ApiOptions
            {
                AssemblyPath = _path,
                TypeName = "GenericType",
                Select = ["Type Info"],
            }));

        Assert.Equal(0, exit);

        // Non-vacuity is two claims, not one: the hostile text must reach the
        // output at all, and it must reach it through this section rather than
        // through the tree the default view renders instead.
        Assert.Contains("## Type Info", output, StringComparison.Ordinal);
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

        // Hostile *type parameter* name, and a hostile *constraint* type name.
        // These are the Type Info section's "Type Parameters" row, which is a
        // distinct channel from the ones above, and the row is a mix rather than
        // uniformly contained: the parameter half arrives through
        // TypeParameter.DisplayName, which contains, while the constraint half is
        // composed separately by CSharpDeclarationWriter.FormatConstraintList.
        // The section also renders whenever the view is not member detail, where
        // the inline type-parameter field is quiet-only, so it is more exposed
        // than the inline twin it borrows its residual entry from.
        var generic = module.DefineType(
            "GenericType", TypeAttributes.Public | TypeAttributes.Class);
        var typeParams = generic.DefineGenericParameters($"T{Hazard}INJECTED");
        typeParams[0].SetInterfaceConstraints(hostileInterfaceType);
        generic.CreateType();

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
                // The Source Location section itself. It was the one class in
                // this family that rendered its strings raw, and this gate --
                // which enumerated every sibling below -- did not name it, so
                // a hostile PDB document path and SourceLink URL reached the
                // table uncontained while the gate stayed green. Enumerating
                // by hand is what failed; the fields are listed here so the
                // omission cannot repeat silently.
                Method = H("METHOD"),
                Token = H("TOKEN"),
                ILOffset = H("ILOFFSET"),
                MatchedOffset = H("MATCHEDOFFSET"),
                File = H("FILE"),
                Url = H("URL"),
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
                     "INJECTEDMETHOD", "INJECTEDTOKEN", "INJECTEDILOFFSET",
                     "INJECTEDMATCHEDOFFSET", "INJECTEDFILE", "INJECTEDURL",
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
    private static Task<(int exit, string output, string error)> RunAppAsync(params string[] args)
        => HostileCli.RunAsync(args);
}

/// <summary>
/// Pins the boundary between the attribute decode-plausibility scan and
/// containment (issue #3319).
/// </summary>
/// <remarks>
/// <c>AttributeReader.TryGetAttributeDisplayValue</c> drops an attribute value
/// whole when it holds a control character, on the theory that the blob was not
/// really a string. That scan is narrower than the rendering-hazard set, and a
/// reviewer reasonably reads the disagreement as a bug and "fixes" it by
/// widening the scan to <c>IsRenderingHazard</c>.
///
/// That would be a regression, and this is the gate that says so. Widening the
/// scan makes these values vanish from the member listing, so the marker
/// assertions below fail. Containment, not suppression, is what keeps this
/// text safe -- and the hazard assertion proves containment is doing that job,
/// so the drop is not load-bearing for safety.
/// </remarks>
[Collection("Console")]
public class AttributeValueRetentionTests
{
    [Theory]
    [InlineData("INJECTEDOBSOLETEBIDI")]
    [InlineData("INJECTEDOBSOLETELSONLY")]
    public async Task ObsoleteMessage_WithHazardButNoControlCharacter_IsRenderedContainedNotDropped(string marker)
    {
        string[] args =
        [
            "member", "HostileLiterals",
            "--library", FixtureCatalog.HostileLiterals.AssemblyPath(),
            "--all", "-v:d",
        ];

        var (exit, output, _) = await HostileCli.RunAsync(args);

        Assert.Equal(0, exit);

        // Retention: the value must still reach the listing. This fails if the
        // plausibility scan is widened to the hazard set.
        Assert.Contains(marker, output, StringComparison.Ordinal);

        // Containment: and it must be safe there, which is why retaining it is
        // the right call in the first place.
        HostileOutputAssert.NoRenderingHazard(output, string.Join(' ', args));
        HostileOutputAssert.NoLineSplit(output, [marker]);
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

    private static Task<(int exit, string output, string error)> RunAppAsync(params string[] args)
        => HostileCli.RunAsync(args);
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
    /// The subject a command echoes back is its own channel.
    /// </summary>
    /// <remarks>
    /// Every case here renders the caller's subject into a heading, a column,
    /// or a diagnostic rather than into a result row. Round 12 contained the
    /// rows and left all of these raw, which is why they are gated separately:
    /// a green row assertion says nothing about the heading above it.
    ///
    /// The subject is untrusted despite arriving on the command line. The
    /// threat model is an agent that reads a type or package name out of
    /// inspected metadata and feeds it straight back into the next command, so
    /// a hostile name reaches these channels without a human ever typing it.
    /// </remarks>
    public static TheoryData<string, string[], string> SubjectEchoChannels() => new()
    {
        // The subject must resolve wherever the channel only renders on a hit,
        // or the case proves nothing: `implements` prints no heading when the
        // interface is missing, and `find` renders no Pattern column when
        // nothing matches. Both were caught doing exactly that.
        { "extensions", ["extensions", "Derived" + Hazard + "INJECTEDDERIVED"], "INJECTEDDERIVED" },
        { "implements", ["implements", "RelNs.IFace" + Hazard + "INJECTEDSUBJECT"], "INJECTEDSUBJECT" },
        // A single pattern renders the subject in the heading; only a
        // multi-pattern search renders the Pattern column. They are different
        // owners, so both are exercised.
        { "find-title", ["find", "Derived" + Hazard + "INJECTEDDERIVED"], "INJECTEDDERIVED" },
        { "find-pattern", ["find", "Derived" + Hazard + "INJECTEDDERIVED,*"], "INJECTEDDERIVED" },
        { "depends", ["depends", "Missing" + Hazard + "INJECTEDMISSING"], "INJECTEDMISSING" },
    };

    [Theory]
    [MemberData(nameof(SubjectEchoChannels))]
    public async Task EchoedSubject_WithHostileName_RendersNoHazard(string channel, string[] command, string marker)
    {
        // -v:d so columns that only appear in the detailed table -- notably
        // find's Pattern column -- are actually rendered.
        var (_, output, error) = await RunAppAsync([.. command, "--library", _path, "-v:d"]);

        var combined = output + "\n" + error;

        // Non-vacuity: the subject must actually have been echoed somewhere, or
        // the hazard scan below is running over output that never carried it.
        HostileOutputAssert.MarkersRendered(combined, channel, marker);

        HostileOutputAssert.NoRenderingHazard(combined, channel);
        HostileOutputAssert.NoLineSplit(combined, [marker]);
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

        // `implements` only renders its heading when the interface resolves, so
        // a hostile interface that nothing implements would leave that channel
        // unexercised.
        var hostileInterface = module.DefineType(
            $"RelNs.IFace{Hazard}INJECTEDSUBJECT",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        var builtInterface = hostileInterface.CreateType();

        // The implementer is hostile too: its row columns are a separate owner
        // from the heading, and a benign implementer would leave them ungated.
        var implementer = module.DefineType(
            $"RelNs.Implementer{Hazard}INJECTEDIMPLEMENTER",
            TypeAttributes.Public | TypeAttributes.Class);
        implementer.AddInterfaceImplementation(builtInterface);
        implementer.CreateType();

        ab.Save(path);
    }

    private static Task<(int exit, string output, string error)> RunAppAsync(params string[] args)
        => HostileCli.RunAsync(args);
}

/// <summary>
/// Gates the <c>implements</c> row columns whose upstream is raw. The
/// end-to-end relationship gate cannot reach <c>Library</c>: that column is the
/// inspected assembly's file stem, and a hostile file name is not something a
/// test can put on a real filesystem. This exercises the row's own owner
/// instead, which is where issue #3319 placed containment.
/// </summary>
public class ImplementerRowContainmentTests
{
    [Fact]
    public void ImplementerRow_WithHostileLibrary_ContainsHazard()
    {
        var view = ImplementsOutputFormatter.BuildView(
            "IFace",
            [
                new ImplementerResult
                {
                    TypeName = "Ty\u202EINJECTEDTYPE",
                    Kind = "class",
                    Relationship = "implements",
                    Assembly = "Lib\u202EINJECTEDLIBRARY"
                }
            ]);

        var row = Assert.Single(view.Rows!);
        HostileOutputAssert.NoRenderingHazard(row.Library, "Library");
        HostileOutputAssert.NoRenderingHazard(row.Type, "Type");
        Assert.Contains("INJECTEDLIBRARY", row.Library, StringComparison.Ordinal);
        Assert.Contains("INJECTEDTYPE", row.Type, StringComparison.Ordinal);
    }
}

/// <summary>
/// Gates the C# spellings the API views compose from a type's own name.
/// </summary>
/// <remarks>
/// Round 14 found two of these still raw. Both sit next to already-contained
/// text, which is why neither showed up earlier: the constructor signature is
/// inside a <c>csharp</c> code fence whose heading was contained, and the
/// finalizer tree node is the one branch of a two-branch select whose other
/// branch contains. A fence and a tree gutter are both structures a line
/// terminator can forge a way out of (issue #3319).
/// </remarks>
[Collection("Console")]
public class UntrustedDeclarationSpellingContainmentTests : IDisposable
{
    private const string Hazard = "\u202E";
    private readonly string _dir;
    private readonly string _path;

    public UntrustedDeclarationSpellingContainmentTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "HostileDecl_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "HostileDecl.dll");
        WriteHostileAssembly(_path);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ConstructorSignature_WithHostileTypeName_RendersNoHazard()
    {
        var (_, output, error) = await HostileCli.RunAsync(
            "member", $"DeclNs.Bad{Hazard}INJECTEDCTOR", "--library", _path, "--ctor", "-v:n");

        var combined = output + "\n" + error;
        HostileOutputAssert.MarkersRendered(combined, "ctor", "INJECTEDCTOR");
        HostileOutputAssert.NoRenderingHazard(combined, "ctor");
        HostileOutputAssert.NoLineSplit(combined, ["INJECTEDCTOR"]);
    }

    [Fact]
    public async Task FinalizerShapeNode_WithHostileTypeName_RendersNoHazard()
    {
        var (_, output, error) = await HostileCli.RunAsync(
            "type", $"DeclNs.Bad{Hazard}INJECTEDCTOR", "--library", _path, "--shape");

        var combined = output + "\n" + error;
        // The finalizer node spells `~Bad<hazard>INJECTEDCTOR()`, so the marker
        // proves the node rendered rather than being satisfied by the title.
        Assert.Contains("~Bad", combined, StringComparison.Ordinal);
        HostileOutputAssert.MarkersRendered(combined, "shape", "INJECTEDCTOR");
        HostileOutputAssert.NoRenderingHazard(combined, "shape");
    }

    private static void WriteHostileAssembly(string path)
    {
        var name = new AssemblyName("HostileDecl") { Version = new Version(1, 0, 0, 0) };
        var ab = new PersistedAssemblyBuilder(name, typeof(object).Assembly);
        var module = ab.DefineDynamicModule("HostileDecl");

        var type = module.DefineType(
            $"DeclNs.Bad{Hazard}INJECTEDCTOR",
            TypeAttributes.Public | TypeAttributes.Class,
            typeof(object));
        type.DefineDefaultConstructor(MethodAttributes.Public);

        // A real override of Object.Finalize -- a plain method named "Finalize"
        // is not recognised as a finalizer, which would leave the node under
        // test unrendered and the gate vacuous.
        var finalizer = type.DefineMethod(
            "Finalize",
            MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.ReuseSlot,
            CallingConventions.HasThis,
            typeof(void),
            Type.EmptyTypes);
        finalizer.GetILGenerator().Emit(OpCodes.Ret);
        type.DefineMethodOverride(
            finalizer,
            typeof(object).GetMethod("Finalize", BindingFlags.Instance | BindingFlags.NonPublic)!);
        type.CreateType();

        ab.Save(path);
    }
}

/// <summary>
/// Gates the rows that carry untrusted text through paths the section views do
/// not own: the scalar/URL/path projections behind <c>--value</c>, and the two
/// diff rows whose subject spelling comes straight from metadata.
/// </summary>
/// <remarks>
/// These are gated at their records rather than end to end because each has
/// many producers -- fourteen for <c>ShapeProjectionRow</c> alone -- and the
/// record is the single owner the fix placed containment on (issue #3319).
/// </remarks>
public class UntrustedRowContainmentTests
{
    private const string Hazard = "\u202E";

    [Fact]
    public void ShapeProjectionRow_WithHostileText_ContainsEveryUntrustedField()
    {
        var row = new ShapeProjectionRow(
            1,
            "Package Info",
            $"Value{Hazard}INJECTEDVALUE",
            Label: $"Label{Hazard}INJECTEDLABEL",
            Url: $"https://x/{Hazard}INJECTEDURL",
            Path: $"docs/{Hazard}INJECTEDPATH");

        HostileOutputAssert.NoRenderingHazard(row.Value, "Value");
        HostileOutputAssert.NoRenderingHazard(row.Label!, "Label");
        HostileOutputAssert.NoRenderingHazard(row.Url!, "Url");
        HostileOutputAssert.NoRenderingHazard(row.Path!, "Path");
        Assert.Contains("INJECTEDVALUE", row.Value, StringComparison.Ordinal);
        Assert.Contains("INJECTEDLABEL", row.Label!, StringComparison.Ordinal);
        Assert.Contains("INJECTEDURL", row.Url!, StringComparison.Ordinal);
        Assert.Contains("INJECTEDPATH", row.Path!, StringComparison.Ordinal);
    }

    [Fact]
    public void ImplementationDiffRow_WithHostileText_ContainsMemberAndEvidence()
    {
        var row = new ImplementationDiffRow(
            $"Ty{Hazard}INJECTEDMEMBER.M",
            "IlBody",
            "Changed",
            "modified",
            $"ldstr \"{Hazard}INJECTEDEVIDENCE\"");

        HostileOutputAssert.NoRenderingHazard(row.Member, "Member");
        HostileOutputAssert.NoRenderingHazard(row.Evidence, "Evidence");
        Assert.Contains("INJECTEDMEMBER", row.Member, StringComparison.Ordinal);
        Assert.Contains("INJECTEDEVIDENCE", row.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void FindingTransitionRow_WithHostileText_ContainsTargetVersionsAndDetail()
    {
        var row = new FindingTransitionRow(
            "FindingComparison.Complete",
            "Descriptor.Id",
            $"Ty{Hazard}INJECTEDTARGET",
            $"1.0.0{Hazard}INJECTEDFROM",
            $"2.0.0{Hazard}INJECTEDTO",
            "Present",
            "Absent",
            $"failed: {Hazard}INJECTEDDETAIL");

        HostileOutputAssert.NoRenderingHazard(row.Target, "Target");
        HostileOutputAssert.NoRenderingHazard(row.From, "From");
        HostileOutputAssert.NoRenderingHazard(row.To, "To");
        HostileOutputAssert.NoRenderingHazard(row.Detail!, "Detail");
        Assert.Contains("INJECTEDTARGET", row.Target, StringComparison.Ordinal);
        Assert.Contains("INJECTEDFROM", row.From, StringComparison.Ordinal);
        Assert.Contains("INJECTEDTO", row.To, StringComparison.Ordinal);
        Assert.Contains("INJECTEDDETAIL", row.Detail!, StringComparison.Ordinal);
    }
}

/// <summary>
/// Gates the two stderr channels round 14 found raw: the CLI's <c>Error:</c>
/// line and the <c>Note:</c> hints.
/// </summary>
/// <remarks>
/// An error message quotes the subject that failed, so it carries the subject's
/// hazards onto a terminal where an ANSI escape is executed rather than shown.
/// The hint channel additionally needed its own payload: the <c>depends</c>
/// subject-echo case uses a dotted name, which fails
/// <c>LooksLikeBareTypeName</c> and never reaches the hint at all -- the gate
/// was there but the branch was not (issue #3319).
/// </remarks>
[Collection("Console")]
public class UntrustedErrorChannelContainmentTests
{
    [Theory]
    // A bare (undotted) name reaches the bare-type hint; a dotted `System.`
    // name reaches the namespace-prefix hint. Neither reaches the other.
    [InlineData("depends", "Ty\u001BINJECTEDBARE", "INJECTEDBARE")]
    [InlineData("depends", "System.Ty\u001BINJECTEDPREFIX", "INJECTEDPREFIX")]
    // `depends` resolves its own failure through the graph-result record, which
    // round 13 already contained, so it cannot gate the shared `Error:` writer.
    // `type` reaches that writer, and only that case fails when it is tampered.
    [InlineData("type", "Ty\u001BINJECTEDERRLINE", "INJECTEDERRLINE")]
    public async Task ErrorAndHintChannels_WithHostileSubject_RenderNoHazard(string command, string subject, string marker)
    {
        var (_, output, error) = await HostileCli.RunAsync(command, subject);

        var combined = output + "\n" + error;
        HostileOutputAssert.MarkersRendered(combined, "stderr", marker);
        HostileOutputAssert.NoRenderingHazard(combined, "stderr");
        HostileOutputAssert.NoLineSplit(combined, [marker]);
    }
}

/// <summary>
/// Gate for <see cref="LibraryInspectionView"/> derived from the model's shape
/// rather than from a list of fields someone remembered to write down.
/// </summary>
/// <remarks>
/// Every hand-written fixture in this file is a bet that its author enumerated
/// the whole surface, and that bet has now lost twice on the same view. The
/// second time is the instructive one: <see cref="UntrustedILOffsetContainmentTests"/>
/// says in its own comment that "enumerating by hand is what failed... the
/// fields are listed here so the omission cannot repeat silently", and the very
/// next line hard-codes <c>FileName = "hostile.dll"</c>. The view's <c>File</c>
/// heading, its <c>Name</c>, <c>Version</c>, <c>TFM</c>, <c>Arch</c>,
/// <c>Source</c>, and <c>Library</c> fields were all raw, and a reviewer read a
/// bidi override straight off a heading on stdout while every gate here was
/// green.
///
/// So the fixture is generated from <see cref="LibraryInspection"/> by
/// reflection: every string anywhere in the graph carries the hazard, and a
/// property added to the model is hostile the day it is added. That is the same
/// move that fixed the stderr scan -- derive the scope from the structure, so
/// the set cannot go stale -- and it is the only version of this test that
/// stays true as the model grows.
///
/// The boundary: this gate covers <see cref="LibraryInspection"/> only. The
/// other view roots still hold their containment by construction-site
/// discipline rather than by the type -- roughly 290 raw string members, none
/// of which a real hostile assembly reaches today. That is an unverified
/// invariant, not a verified one, and it is tracked as #3463. Pointing this
/// fixture at another view root is the intended way to close it.
/// </remarks>
public class LibraryViewShapeDerivedContainmentTests
{
    private const string Hazard = "\u000B";
    private const string Hostile = $"H{Hazard}INJECTED";

    /// <summary>
    /// The members a reflection fixture cannot reach, and why.
    /// </summary>
    /// <remarks>
    /// Three shapes, none of which a harness may paper over:
    ///
    /// <list type="bullet">
    /// <item><description>
    /// <c>LibraryInspection.Switches</c> and its twenty siblings project over a
    /// <c>[Union]</c> <c>FindingInspection&lt;T&gt;</c>. Reaching them needs the
    /// producer that builds a populated <c>Complete</c> case -- product
    /// behavior. A fixture that synthesized one would be a second
    /// implementation of the analyzers, which is the harness boundary this
    /// repository draws. Tracked in #3463.
    /// </description></item>
    /// <item><description>
    /// <c>TypeRef</c> has no public constructor; it is factory-built from
    /// metadata handles.
    /// </description></item>
    /// <item><description>
    /// <c>ApiSignature.PublicAccessorsSummary</c> and
    /// <c>TypeParameter.DisplayName</c> are computed from members the walk
    /// already fills, so they carry the hostile text transitively rather than
    /// being set.
    /// </description></item>
    /// </list>
    ///
    /// This is a declared boundary, not a silent one. The set is asserted
    /// exactly, so a member that falls out of reach later fails here instead of
    /// shrinking the gate.
    /// </remarks>
    private static readonly string[] OutOfReach =
    [
        "ApiSignature.PublicAccessorsSummary (String): string with no setter",
        "LibraryInspection.AI (List`1): computed projection still null after the walk",
        "LibraryInspection.AspNetCore (List`1): computed projection still null after the walk",
        "LibraryInspection.Aspire (List`1): computed projection still null after the walk",
        "LibraryInspection.AssemblyAttributeInspection (FindingInspection`1): computed projection still null after the walk",
        "LibraryInspection.Authentication (List`1): computed projection still null after the walk",
        "LibraryInspection.Configuration (List`1): computed projection still null after the walk",
        "LibraryInspection.CustomAttributes (List`1): computed projection still null after the walk",
        "LibraryInspection.DependencyInjection (List`1): computed projection still null after the walk",
        "LibraryInspection.ExtensionMemberInspection (FindingInspection`1): computed projection still null after the walk",
        "LibraryInspection.ExtensionMethods (List`1): computed projection still null after the walk",
        "LibraryInspection.HealthChecks (List`1): computed projection still null after the walk",
        "LibraryInspection.Hosting (List`1): computed projection still null after the walk",
        "LibraryInspection.HttpClient (List`1): computed projection still null after the walk",
        "LibraryInspection.InspectionFailures (List`1): computed projection still null after the walk",
        "LibraryInspection.Integrations (List`1): computed projection still null after the walk",
        "LibraryInspection.Logging (List`1): computed projection still null after the walk",
        "LibraryInspection.OpenApi (List`1): computed projection still null after the walk",
        "LibraryInspection.OpenTelemetry (List`1): computed projection still null after the walk",
        "LibraryInspection.Options (List`1): computed projection still null after the walk",
        "LibraryInspection.Resources (List`1): computed projection still null after the walk",
        "LibraryInspection.Switches (List`1): computed projection still null after the walk",
        "LibraryInspection.TypeForwarders (List`1): computed projection still null after the walk",
        "LibraryInspection.UnionTypes (List`1): computed projection still null after the walk",
        "MemberRef.DeclaringType (TypeRef): no public constructor",
        "MemberRef.OpenReturnType (TypeRef): no public constructor",
        "MemberRef.OpenSignatureParameters (ImmutableArray`1): computed projection still empty after the walk",
        "MemberRef.OpenSignatureReturn (TypeRef): computed projection still null after the walk",
        "MemberRef.ReturnType (TypeRef): no public constructor",
        "MethodIdentity.DeclaringType (TypeRef): no public constructor",
        "MethodIdentity.ReturnType (TypeRef): no public constructor",
        "TypeParameter.DisplayName (String): string with no setter",
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryStringInTheModel_RendersContained(bool topFieldsOnly)
    {
        var inspection = new LibraryInspection();
        var walk = new Walk();
        walk.Fill(inspection, depth: 0);
        walk.Settle();

        // Non-vacuity for the filler itself: a reflection walk that silently
        // stopped early would leave a clean model and prove nothing.
        Assert.True(walk.Filled > 40, $"the filler only reached {walk.Filled} strings, so it is not covering the model");

        // And a walk that quietly declined a type covers less than it did
        // yesterday while reporting the same green. The first version of this
        // walker declined every positional record -- which is the shape of every
        // row in the view -- and every ILInspector.Findings payload, so it
        // reached the root scalars and nothing else. It still passed its own
        // count check. So the walker reports what it could not enter, and the
        // report is asserted as a *set*: an entry that appears is an
        // unannounced gap, and an entry that disappears is coverage this pin
        // should have claimed. Both directions fail.
        Assert.Equal(OutOfReach, walk.Declined.Distinct().Order(StringComparer.Ordinal).ToArray());

        var output = MarkoutSerializer.Serialize(
            new LibraryInspectionView(inspection, topFieldsOnly), InspectionContext.Default);

        // Non-vacuity for the render: the hostile text has to have reached the
        // output, or the hazard scan below is asserting nothing.
        int rendered = 0;
        for (int i = output.IndexOf("INJECTED", StringComparison.Ordinal); i >= 0;
             i = output.IndexOf("INJECTED", i + 1, StringComparison.Ordinal))
        {
            rendered++;
        }

        Assert.True(rendered > 0, "no hostile text rendered, so this gate proves nothing about the view");

        HostileOutputAssert.NoRenderingHazard(output, "library-view-shape");
        HostileOutputAssert.NoLineSplit(output, "INJECTED");
    }

    /// <summary>
    /// One traversal of a view model: sets every reachable string to the
    /// hostile value, creating nested models and single-element lists on the
    /// way, and records every member it could not enter.
    /// </summary>
    /// <remarks>
    /// Numbers and booleans are left alone. Several sections are gated on a
    /// count being positive, so filling those would render more -- but it would
    /// also invent states the analyzers never produce, and a gate that fails on
    /// an impossible model teaches nothing. The strings are the untrusted part;
    /// they are what this walks.
    ///
    /// <see cref="Declined"/> is the part that keeps the walk honest. A
    /// reflection filler's coverage is invisible from its result: it renders a
    /// model either way, and a type it cannot construct simply contributes no
    /// hostile text. Reporting the refusals converts that silence into a
    /// failing test naming the exact member.
    /// </remarks>
    private sealed class Walk
    {
        public int Filled { get; private set; }

        public List<string> Declined { get; } = [];

        private List<(object Target, PropertyInfo Property, string Site)> Deferred { get; } = [];

        /// <summary>
        /// Re-reads every deferred computed projection now that the findings
        /// behind them are filled, and declines the ones still empty.
        /// </summary>
        public void Settle()
        {
            foreach (var (target, property, site) in Deferred)
            {
                object? value = property.GetValue(target);

                if (value is System.Collections.IList list)
                {
                    if (list.Count == 0)
                    {
                        Declined.Add($"{site}: computed projection still empty after the walk");
                        continue;
                    }

                    foreach (object? item in list)
                    {
                        if (item is not null && IsProductModel(item.GetType()))
                        {
                            Fill(item, depth: 1);
                        }
                    }

                    continue;
                }

                if (value is null)
                {
                    Declined.Add($"{site}: computed projection still null after the walk");
                    continue;
                }

                Fill(value, depth: 1);
            }
        }

        /// <summary>
        /// True for an expression-bodied or otherwise computed property, false
        /// for an auto-property.
        /// </summary>
        /// <remarks>
        /// The discriminator is the compiler-generated backing field, which is
        /// what actually distinguishes <c>public List&lt;T&gt; Xs { get; } = [];</c>
        /// -- fillable in place -- from <c>public List&lt;T&gt;? Xs => GetOrCreate(...)</c>,
        /// which must not be touched until its source is populated.
        /// </remarks>
        private static bool IsComputed(PropertyInfo property) =>
            !property.CanWrite
                && property.DeclaringType?.GetField(
                    $"<{property.Name}>k__BackingField",
                    BindingFlags.NonPublic | BindingFlags.Instance) is null;

        /// <summary>Depth bound, stated so truncation is a declared limit rather than an accident.</summary>
        private const int MaxDepth = 6;

        public void Fill(object target, int depth)
        {
            if (depth > MaxDepth)
            {
                return;
            }

            var declaring = target.GetType();
            foreach (var property in declaring.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                string site = $"{declaring.Name}.{property.Name} ({type.Name})";

                if (type == typeof(string))
                {
                    if (property.CanWrite)
                    {
                        property.SetValue(target, Hostile);
                        Filled++;
                    }
                    else if (property.GetValue(target) is string already && already.Contains(Hazard, StringComparison.Ordinal))
                    {
                        // A get-only string the primary constructor already
                        // took the hostile value for. Declining it would report
                        // the covered case as a gap.
                        Filled++;
                    }
                    else
                    {
                        Declined.Add($"{site}: string with no setter");
                    }

                    continue;
                }

                if (type.IsPrimitive || type.IsEnum || type == typeof(DateTime) || type == typeof(decimal)
                    || type == typeof(TimeSpan) || type == typeof(Guid) || type == typeof(Uri)
                    || type == typeof(Version) || type == typeof(object))
                {
                    continue;
                }

                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                {
                    FillList(target, property, type, site, depth);
                    continue;
                }

                if (type.IsArray)
                {
                    FillArray(target, property, type, site, depth);
                    continue;
                }

                if (IsComputed(property))
                {
                    Deferred.Add((target, property, site));
                    continue;
                }

                var existing = property.GetValue(target);

                // Decide on the runtime type when there is a value. The union
                // cases hang off `object? Value`, and judging by the declared
                // type skipped every one of them.
                if (existing is not null && !IsProductModel(type))
                {
                    type = existing.GetType();
                }

                if (!IsProductModel(type))
                {
                    // Not ours to construct; it carries no untrusted text of its own.
                    continue;
                }

                if (existing is null)
                {
                    if (!property.CanWrite)
                    {
                        Declined.Add($"{site}: product model, null, no setter");
                        continue;
                    }

                    if (!TryCreate(type, depth, out existing, out string why))
                    {
                        Declined.Add($"{site}: {why}");
                        continue;
                    }

                    property.SetValue(target, existing);
                }
                else if (IsSharedSingleton(existing))
                {
                    // Not ours to write on. See IsSharedSingleton.
                    if (property.CanWrite && TryCreate(type, depth, out object? replacement, out _)
                        && replacement is not null)
                    {
                        // A private copy carries the untrusted text without
                        // touching the shared one, so coverage is kept.
                        property.SetValue(target, replacement);
                        existing = replacement;
                    }
                    else
                    {
                        Declined.Add($"{site}: shared singleton, not the walk's to mutate");
                        continue;
                    }
                }

                Fill(existing!, depth + 1);
            }
        }

        private void FillList(object target, PropertyInfo property, Type type, string site, int depth)
        {
            // A get-only list is the dominant shape here -- twenty-one sections
            // spell `public List<T> Switches { get; } = [];` -- and the first
            // version of this walk skipped every one of them on the grounds
            // that it could not assign the property. It did not need to: the
            // list is already there, so it fills that one in place. Skipping
            // them cost the gate the async, extension, switch, union, resource,
            // attribute, forwarder, and every integration section at once.
            if (IsComputed(property))
            {
                // A computed projection -- `Switches => GetOrCreate(... SwitchInspection ...)`
                // -- is null only because its source finding is still empty, and
                // filling that finding is what makes it appear. It must not even
                // be *read* here: GetOrCreate latches on first read, so reading
                // it mid-walk pins the empty answer for good. It is re-read once
                // the walk is done, and only a projection still empty then is
                // genuinely unreachable.
                Deferred.Add((target, property, site));
                return;
            }

            var existing = property.GetValue(target) as System.Collections.IList;

            // The list is always assigned, even when its element cannot be
            // filled. Leaving one null produces a model no analyzer emits,
            // and the view then throws on it -- a failure that says nothing
            // about containment.
            var element = type.GetGenericArguments()[0];
            var list = existing ?? (System.Collections.IList)Activator.CreateInstance(type)!;

            if (element == typeof(string))
            {
                list.Add(Hostile);
                Filled++;
            }
            else if (IsProductModel(element))
            {
                if (TryCreate(element, depth, out object? item, out string why))
                {
                    Fill(item!, depth + 1);
                    list.Add(item);
                }
                else
                {
                    Declined.Add($"{site}: element {element.Name}: {why}");
                }
            }

            if (property.CanWrite)
            {
                property.SetValue(target, list);
            }
        }

        private void FillArray(object target, PropertyInfo property, Type type, string site, int depth)
        {
            if (!property.CanWrite)
            {
                Declined.Add($"{site}: array with no setter");
                return;
            }

            var element = type.GetElementType()!;

            if (element == typeof(string))
            {
                property.SetValue(target, new[] { Hostile });
                Filled++;
                return;
            }

            if (!IsProductModel(element))
            {
                return;
            }

            if (!TryCreate(element, depth, out object? item, out string why))
            {
                Declined.Add($"{site}: element {element.Name}: {why}");
                return;
            }

            Fill(item!, depth + 1);
            var array = Array.CreateInstance(element, 1);
            array.SetValue(item, 0);
            property.SetValue(target, array);
        }

        /// <summary>
        /// Constructs a model, using its primary constructor when it has no
        /// parameterless one.
        /// </summary>
        /// <remarks>
        /// Requiring a parameterless constructor is what made the first version
        /// of this walk vacuous. Every row in these views is a positional
        /// record, and a positional record has no parameterless constructor, so
        /// the filter excluded precisely the types the containment work was
        /// about while the count check stayed green.
        /// </remarks>
        private bool TryCreate(Type type, int depth, out object? instance, out string why)
        {
            instance = null;
            why = string.Empty;

            var parameterless = type.GetConstructor(Type.EmptyTypes);
            if (parameterless is not null && !type.IsValueType)
            {
                instance = parameterless.Invoke(null);
                return true;
            }

            // A finding union exposes one constructor per case. `Complete` is
            // the case that carries payloads, and payloads are the untrusted
            // part; picking by parameter count would pick an arbitrary case and
            // walk an empty one.
            var ctor = type.GetConstructors()
                .Where(c => c.IsPublic)
                .OrderByDescending(c => c.GetParameters() is [{ ParameterType.Name: "Complete" }] ? 1 : 0)
                .ThenByDescending(c => c.GetParameters().Length)
                .FirstOrDefault();

            if (ctor is null)
            {
                why = "no public constructor";
                return false;
            }

            var parameters = ctor.GetParameters();
            var args = new object?[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                args[i] = MakeArgument(
                    Nullable.GetUnderlyingType(parameters[i].ParameterType) ?? parameters[i].ParameterType,
                    depth);
            }

            try
            {
                instance = ctor.Invoke(args);
                return true;
            }
            catch (Exception ex)
            {
                why = $"constructor threw {ex.InnerException?.GetType().Name ?? ex.GetType().Name}";
                return false;
            }
        }

        /// <summary>
        /// A value for one constructor parameter.
        /// </summary>
        /// <remarks>
        /// Value types get the same treatment as classes when they declare a
        /// parameterized constructor. Defaulting them was enough on its own to
        /// empty every finding payload: <c>FindingKey</c> is a record struct
        /// that rejects a null identity, so <c>default</c> made every
        /// <c>Finding&lt;T&gt;</c> constructor throw, every <c>Complete</c>
        /// case come out empty, and every projection over it render nothing --
        /// while the walk reported only that it could not construct something.
        /// </remarks>
        private object? MakeArgument(Type type, int depth)
        {
            if (type == typeof(string))
            {
                Filled++;
                return Hostile;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var list = (System.Collections.IList)Activator.CreateInstance(type)!;
                var element = type.GetGenericArguments()[0];

                if (element == typeof(string))
                {
                    list.Add(Hostile);
                    Filled++;
                }
                else if (IsProductType(element) && TryCreate(element, depth + 1, out object? item, out _) && item is not null)
                {
                    Fill(item, depth + 1);
                    list.Add(item);
                }

                return list;
            }

            if (type.IsArray)
            {
                return Array.CreateInstance(type.GetElementType()!, 0);
            }

            if (IsImmutableArray(type))
            {
                return BuildImmutableArray(type, depth);
            }

            if (IsProductType(type))
            {
                // Passing null here is what made every finding payload
                // unreachable: the union guards its cases with
                // ArgumentNullException, so the whole subtree was recorded
                // as "constructor threw" and skipped.
                if (TryCreate(type, depth + 1, out object? nested, out _) && nested is not null)
                {
                    if (!type.IsValueType)
                    {
                        Fill(nested, depth + 1);
                    }

                    return nested;
                }

                return type.IsValueType ? Activator.CreateInstance(type) : null;
            }

            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        private static bool IsImmutableArray(Type type) =>
            type.IsGenericType
                && type.GetGenericTypeDefinition() == typeof(System.Collections.Immutable.ImmutableArray<>);

        /// <summary>
        /// A one-element immutable array, with its element filled.
        /// </summary>
        private object? BuildImmutableArray(Type type, int depth)
        {
            var element = type.GetGenericArguments()[0];
            var builder = Array.CreateInstance(element, 1);

            if (element == typeof(string))
            {
                builder.SetValue(Hostile, 0);
                Filled++;
            }
            else if (IsProductType(element)
                && TryCreate(element, depth + 1, out object? item, out _)
                && item is not null)
            {
                Fill(item, depth + 1);
                builder.SetValue(item, 0);
            }
            else
            {
                builder = Array.CreateInstance(element, 0);
            }

            var create = typeof(System.Collections.Immutable.ImmutableArray)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "Create" && m.GetParameters() is [{ ParameterType.IsArray: true }])
                .MakeGenericMethod(element);

            return create.Invoke(null, [builder]);
        }

        /// <summary>
        /// A model this repository owns.
        /// </summary>
        /// <remarks>
        /// Scoped by assembly rather than by namespace prefix. The prefix
        /// version tested for <c>DotnetInspector</c> and so silently excluded
        /// every <c>ILInspector.Findings</c> payload -- the async, extension,
        /// switch, union, and resource sections -- which is most of what the
        /// view renders.
        /// </remarks>
        private static bool IsProductModel(Type type) =>
            type.IsClass
                && !type.IsAbstract
                && type != typeof(string)
                && IsProductAssembly(type.Assembly);

        /// <summary>Anything this repository declares, including record structs.</summary>
        private static bool IsProductType(Type type) =>
            !type.IsAbstract
                && !type.IsEnum
                && !type.IsPrimitive
                && type != typeof(string)
                && IsProductAssembly(type.Assembly);

        /// <summary>
        /// Repository-owned assemblies, by the two prefixes this repository
        /// names its assemblies with.
        /// </summary>
        /// <remarks>
        /// Naming the two assemblies the model root lives in was still too
        /// narrow: the payloads are <c>FindingInspection&lt;T&gt;</c> values in
        /// <c>ILInspector.Findings</c>, a third assembly, so the switch, union,
        /// resource, forwarder, and attribute sections were all outside the
        /// walk.
        /// </remarks>
        /// <summary>
        /// True when <paramref name="value"/> is also reachable from a public
        /// static member of its own type -- the <c>Foo.Default</c> singleton
        /// shape.
        /// </summary>
        /// <remarks>
        /// This walk exists to write untrusted text into every string a view
        /// can render, and it does that by mutating instances in place. That is
        /// sound for objects the walk reaches through a model it built, and
        /// silently destructive for one the *product* shares process-wide.
        ///
        /// <c>PerformanceTriageOptions.Default</c> is the case that caught it.
        /// The walk reached it through a property that defaults to the
        /// singleton, filled its filter and sort fields with
        /// <c>U+202E</c>-bearing text, and left it that way for the rest of the
        /// process. Five <c>OutputFormatterTests</c> cases then failed, because
        /// the optimization-opportunity rows were being filtered by a predicate
        /// made of hostile garbage -- and they failed <em>only</em> when run in
        /// the same process as this class, which is what made it look like a
        /// product regression rather than a harness one. In isolation every one
        /// of them passed.
        ///
        /// Reading a public static member of a type already instantiated here
        /// costs nothing and needs no allow list. Where the property can be
        /// assigned, a private copy is substituted so the coverage this walk
        /// exists for is kept rather than traded away for the isolation.
        /// </remarks>
        private static bool IsSharedSingleton(object value)
        {
            var type = value.GetType();

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                if (property.GetIndexParameters().Length == 0
                    && property.CanRead
                    && ReferenceEquals(property.GetValue(null), value))
                {
                    return true;
                }
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (ReferenceEquals(field.GetValue(null), value))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsProductAssembly(Assembly assembly)
        {
            string? name = assembly.GetName().Name;
            return name is not null
                && (name.StartsWith("DotnetInspector", StringComparison.Ordinal)
                    || name.StartsWith("ILInspector", StringComparison.Ordinal)
                    || name.Equals("dotnet-inspect", StringComparison.Ordinal));
        }
    }
}
