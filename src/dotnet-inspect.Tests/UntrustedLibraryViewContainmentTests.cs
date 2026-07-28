using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DotnetInspector.Commands;
using DotnetInspector.Options;

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
    }

    [Fact]
    public async Task LibraryAllSections_WithHostileMetadataNames_RendersNoHazard()
    {
        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = _path,
                IncludeSections =
                [
                    "Async Methods", "P/Invoke Methods", "Extension Methods",
                    "Custom Attributes", "Type Forwarders", "Union Types",
                    "References", "Library Info", "Signals", "Symbols",
                ],
                Markdown = true,
                Verbosity = Verbosity.Detailed,
            }));

        Assert.Equal(0, exit);

        // Non-vacuity: the hostile names must actually reach the rendered output.
        Assert.Contains("INJECTED", output, StringComparison.Ordinal);
        AssertNoHazard(output);
    }

    private static void AssertNoHazard(string output)
    {
        for (int i = 0; i < output.Length; i++)
        {
            char c = output[i];
            if (IsHazard(c))
            {
                Assert.Fail(
                    $"rendered library output carries U+{(int)c:X4} at index {i}: "
                    + output.Substring(Math.Max(0, i - 60), Math.Min(120, output.Length - Math.Max(0, i - 60))));
            }
        }
    }

    /// <summary>
    /// The harness spells the hazard set out rather than calling
    /// <c>CSharpIdentifier.IsRenderingHazard</c>, so that a wrong answer from
    /// the product cannot make this gate agree with it. Line feed and carriage
    /// return are legitimate structure in rendered Markdown, so only the
    /// remaining controls and the bidi set are hazards here.
    /// </summary>
    private static bool IsHazard(char c)
        => c is not '\t' and not '\n' and not '\r'
            && (char.IsControl(c)
                || c is '\u061C' or '\u200E' or '\u200F'
                    or >= '\u202A' and <= '\u202E'
                    or >= '\u2066' and <= '\u2069');

    private static void WriteHostileLibrary(string path)
    {
        var name = new AssemblyName("HostileLibrary") { Version = new Version(1, 0, 0, 0) };
        var ab = new PersistedAssemblyBuilder(name, typeof(object).Assembly);
        var module = ab.DefineDynamicModule("HostileLibrary");

        var asyncCtor = typeof(AsyncStateMachineAttribute).GetConstructor([typeof(Type)])!;
        var extensionCtor = typeof(ExtensionAttribute).GetConstructor(Type.EmptyTypes)!;

        // Async method: hostile method name, declaring type, and namespace.
        var asyncType = module.DefineType(
            $"Hostile{Hazard}INJECTED.Async{Hazard}INJECTED",
            TypeAttributes.Public | TypeAttributes.Class);
        var asyncMethod = asyncType.DefineMethod(
            $"DoWork{Hazard}INJECTED", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        asyncMethod.GetILGenerator().Emit(OpCodes.Ret);
        asyncMethod.SetCustomAttribute(new CustomAttributeBuilder(asyncCtor, [typeof(object)]));
        asyncType.CreateType();

        // Extension method: hostile method name and extension class.
        var extensionType = module.DefineType(
            $"Hostile{Hazard}INJECTED.Extensions{Hazard}INJECTED",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.Abstract);
        extensionType.SetCustomAttribute(new CustomAttributeBuilder(extensionCtor, []));
        var extensionMethod = extensionType.DefineMethod(
            $"Extend{Hazard}INJECTED",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(string)]);
        extensionMethod.DefineParameter(1, ParameterAttributes.None, $"value{Hazard}INJECTED");
        extensionMethod.GetILGenerator().Emit(OpCodes.Ret);
        extensionMethod.SetCustomAttribute(new CustomAttributeBuilder(extensionCtor, []));
        extensionType.CreateType();

        // P/Invoke: hostile method name and module name.
        var nativeType = module.DefineType(
            $"Hostile{Hazard}INJECTED.Native{Hazard}INJECTED",
            TypeAttributes.Public | TypeAttributes.Class);
        var pinvoke = nativeType.DefinePInvokeMethod(
            $"Call{Hazard}INJECTED",
            $"module{Hazard}INJECTED.dll",
            $"entry{Hazard}INJECTED",
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
    }

    private static void AssertNoHazard(string output)
    {
        for (int i = 0; i < output.Length; i++)
        {
            char c = output[i];
            if (IsHazard(c))
            {
                Assert.Fail($"rendered output carries U+{(int)c:X4} at index {i}");
            }
        }
    }

    /// <summary>
    /// Spelled out rather than calling the product's own hazard predicate, so a
    /// wrong answer from the product cannot make this gate agree with it.
    /// </summary>
    private static bool IsHazard(char c)
        => c is not '\t' and not '\n' and not '\r'
            && (char.IsControl(c)
                || c is '\u061C' or '\u200E' or '\u200F'
                    or >= '\u202A' and <= '\u202E'
                    or >= '\u2066' and <= '\u2069');

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
