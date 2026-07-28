using System.Reflection;
using System.Reflection.Emit;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Research;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// End-to-end gates for issue #3319: a metadata name is attacker-controlled, and
/// the C# printer renders it into a fenced C# block. A name
/// carrying a line terminator must not be able to close that fence or inject
/// text that reads as decompiler output.
/// </summary>
/// <remarks>
/// The sibling <c>UntrustedIlPresentationTests</c> covers the IL comment channel
/// on annotated lines (issue #3257). This file covers the C# spelling channel,
/// which that fix deliberately left open.
/// </remarks>
public sealed class UntrustedIdentifierPresentationTests
{
    const string Marker = "public int Injected() => 42; //";

    /// <summary>
    /// The line terminators, plus the rendering hazards that are not line
    /// terminators but still break the rendering: vertical tab moves a terminal
    /// cursor down a row, and an ANSI escape erases or recolors what is already
    /// there.
    /// </summary>
    public static TheoryData<string> LineTerminators => new()
    {
        "\n", "\r\n", "\u2028", "\v", "\u001b[2J",
    };

    [Theory]
    [MemberData(nameof(LineTerminators))]
    public void RenderedCSharp_HostileParameterNameStaysOnItsLine(string terminator)
        => AssertContained(
            terminator,
            "Echo",
            (module, type, hostile) =>
            {
                var method = type.DefineMethod(
                    "Echo",
                    MethodAttributes.Public | MethodAttributes.Static,
                    typeof(int),
                    [typeof(int)]);
                method.DefineParameter(1, ParameterAttributes.None, hostile);
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ret);
            });

    [Theory]
    [MemberData(nameof(LineTerminators))]
    public void RenderedCSharp_HostileGenericParameterNameStaysOnItsLine(string terminator)
        => AssertContained(
            terminator,
            "Generic",
            (module, type, hostile) =>
            {
                var method = type.DefineMethod(
                    "Generic",
                    MethodAttributes.Public | MethodAttributes.Static,
                    typeof(Type),
                    Type.EmptyTypes);
                var parameters = method.DefineGenericParameters(hostile);

                // The body must mention the generic parameter: the annotated
                // projection renders the body, and a bare `ret` would never carry
                // the name into emitted C# at all.
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldtoken, parameters[0]);
                il.Emit(
                    OpCodes.Call,
                    typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle), [typeof(RuntimeTypeHandle)])!);
                il.Emit(OpCodes.Ret);
            });

    /// <summary>
    /// A property read renders the property's metadata name into the method body,
    /// so a hostile property name reaches emitted C# through a caller that never
    /// mentions it in its own signature.
    /// </summary>
    [Theory]
    [MemberData(nameof(LineTerminators))]
    public void RenderedCSharp_HostilePropertyNameStaysOnItsLine(string terminator)
        => AssertContained(
            terminator,
            "Caller",
            (module, type, hostile) =>
            {
                var property = type.DefineProperty(hostile, PropertyAttributes.None, typeof(int), null);
                var getter = type.DefineMethod(
                    "get_" + hostile,
                    MethodAttributes.Public | MethodAttributes.SpecialName,
                    typeof(int),
                    Type.EmptyTypes);
                var getterIl = getter.GetILGenerator();
                getterIl.Emit(OpCodes.Ldc_I4_7);
                getterIl.Emit(OpCodes.Ret);
                property.SetGetMethod(getter);

                var caller = type.DefineMethod("Caller", MethodAttributes.Public, typeof(int), Type.EmptyTypes);
                var il = caller.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, getter);
                il.Emit(OpCodes.Ret);
            });

    static void AssertContained(
        string terminator,
        string memberName,
        Action<ModuleBuilder, TypeBuilder, string> build)
    {
        // Two renderings of the same member, differing only in the name: a benign
        // "n" and a hostile "n<terminator>    <payload>". Containment is a claim
        // about line structure, so the benign rendering is the oracle for it.
        string benignOutput = Render(memberName, "n", build);
        string hostileOutput = Render(memberName, $"n{terminator}    {Marker}", build);

        // Non-vacuity: the name reaches the output and shapes it. Without this a
        // producer that dropped the name entirely would satisfy the line-count
        // claim trivially.
        Assert.NotEqual(benignOutput, hostileOutput);

        // Containment: a hostile name may be folded (the IL comment channel) or
        // sanitized (the C# channel), but it must not add a line. A payload that
        // starts its own line is exactly what reads as decompiler output rather
        // than as a member name.
        Assert.Equal(
            benignOutput.ReplaceLineEndings("\n").Split('\n').Length,
            hostileOutput.ReplaceLineEndings("\n").Split('\n').Length);

        // And no line consists solely of the payload.
        foreach (var line in hostileOutput.ReplaceLineEndings("\n").Split('\n'))
            Assert.NotEqual(Marker, line.Trim());

        // Line count cannot see a vertical tab or an ANSI escape -- neither adds a
        // line to the string, they only move or rewrite the terminal cursor once
        // the string is rendered. So assert the hazard set directly as well.
        // The output's own line structure is legitimate, so exempt CR and LF; every
        // other hazard character in the rendering came from the name.
        //
        // The predicate is spelled out here rather than calling
        // CSharpIdentifier.IsRenderingHazard: a gate that asks the product what
        // counts as dangerous goes vacuous the moment the product's answer is
        // wrong, which is the one case it exists to catch. Tampering
        // IsRenderingHazard to `false` left this test green until it owned its own
        // oracle.
        Assert.DoesNotContain(
            hostileOutput,
            c => c is not ('\n' or '\r' or '\t')
                && (char.IsControl(c) || IsBidiControl(c)));

        static bool IsBidiControl(char ch)
            => ch is '\u061C' or '\u200E' or '\u200F'
                or >= '\u202A' and <= '\u202E'
                or >= '\u2066' and <= '\u2069';
    }

    static string Render(
        string memberName,
        string name,
        Action<ModuleBuilder, TypeBuilder, string> build)
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-hostile-identifier-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var assemblyName = new AssemblyName("HostileIdentifier");
            var assemblyBuilder = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
            var moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);
            var typeBuilder = moduleBuilder.DefineType("Hostile.Target", TypeAttributes.Public | TypeAttributes.Class);
            build(moduleBuilder, typeBuilder, name);
            typeBuilder.CreateType();

            var dllPath = Path.Combine(tempDir, "HostileIdentifier.dll");
            assemblyBuilder.Save(dllPath);

            using var source = MetadataSource.Open(dllPath);
            var projection = ResearchViews.ProjectMember(
                new ResearchViews.MemberProjectionRequest(
                    source,
                    "Hostile.Target",
                    memberName,
                    AnnotatedSource: true));
            return Assert.IsType<string>(projection.AnnotatedSource?.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
