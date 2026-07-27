using System.Reflection;
using System.Reflection.Emit;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Research;

namespace ILInspector.Decompiler.Tests;

public sealed class UntrustedIlPresentationTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    [InlineData("\f")]
    [InlineData("\u0085")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public void AnnotatedSource_IlOperandCannotEscapeItsComment(string terminator)
    {
        const string marker = "public int Injected() => 42; //";
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-hostile-il-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var assemblyName = new AssemblyName("HostileAnnotatedSource");
            var assemblyBuilder = new PersistedAssemblyBuilder(
                assemblyName, typeof(object).Assembly);
            var moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);
            var typeBuilder = moduleBuilder.DefineType(
                "Hostile.Target",
                TypeAttributes.Public | TypeAttributes.Class);
            var field = typeBuilder.DefineField(
                $"field{terminator}    {marker}",
                typeof(int),
                FieldAttributes.Public);
            var method = typeBuilder.DefineMethod(
                "GetCount",
                MethodAttributes.Public,
                typeof(int),
                Type.EmptyTypes);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Ret);
            typeBuilder.CreateType();

            var dllPath = Path.Combine(tempDir, "HostileAnnotatedSource.dll");
            assemblyBuilder.Save(dllPath);

            using var source = MetadataSource.Open(dllPath);
            var projection = ResearchViews.ProjectMember(
                new ResearchViews.MemberProjectionRequest(
                    source,
                    "Hostile.Target",
                    "GetCount",
                    AnnotatedSource: true));
            var output = Assert.IsType<string>(projection.AnnotatedSource?.Output);

            int opcode = output.IndexOf(": ldfld", StringComparison.Ordinal);
            int injected = output.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(opcode >= 0 && injected > opcode);
            var operandPrefix = output[opcode..injected];
            Assert.DoesNotContain('\r', operandPrefix);
            Assert.DoesNotContain('\n', operandPrefix);
            Assert.DoesNotContain('\f', operandPrefix);
            Assert.DoesNotContain('\u0085', operandPrefix);
            Assert.DoesNotContain('\u2028', operandPrefix);
            Assert.DoesNotContain('\u2029', operandPrefix);
            Assert.Contains("// IL_", output[..injected], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// The operand is not the only untrusted text on an IL comment line. A
    /// parameter name is attacker-controlled metadata too, and it is appended as
    /// an <c>arg:</c> annotation after the instruction — so it needs the same
    /// fold. Regression gate for the channel adversarial review found still open
    /// after the operand-only fix.
    /// </summary>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\u2028")]
    public void AnnotatedSource_HostileParameterNameCannotEscapeItsIlComment(string terminator)
    {
        const string marker = "public int Injected() => 42; //";
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-hostile-param-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var assemblyName = new AssemblyName("HostileParameterName");
            var assemblyBuilder = new PersistedAssemblyBuilder(
                assemblyName, typeof(object).Assembly);
            var moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);
            var typeBuilder = moduleBuilder.DefineType(
                "Hostile.Target",
                TypeAttributes.Public | TypeAttributes.Class);
            var method = typeBuilder.DefineMethod(
                "Echo",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(int),
                [typeof(int)]);
            method.DefineParameter(1, ParameterAttributes.None, $"p{terminator}    {marker}");
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ret);
            typeBuilder.CreateType();

            var dllPath = Path.Combine(tempDir, "HostileParameterName.dll");
            assemblyBuilder.Save(dllPath);

            using var source = MetadataSource.Open(dllPath);
            var projection = ResearchViews.ProjectMember(
                new ResearchViews.MemberProjectionRequest(
                    source,
                    "Hostile.Target",
                    "Echo",
                    AnnotatedSource: true));
            var output = Assert.IsType<string>(projection.AnnotatedSource?.Output);

            var argumentLine = Assert.Single(
                output.ReplaceLineEndings("\n").Split('\n'),
                line => line.Contains(": ldarg", StringComparison.Ordinal));

            // The annotation is folded onto the instruction's own comment line,
            // so the payload never reaches column zero as active C#.
            Assert.StartsWith("//", argumentLine.TrimStart(), StringComparison.Ordinal);
            Assert.Contains("arg: p", argumentLine, StringComparison.Ordinal);
            Assert.Contains(marker, argumentLine, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// A <c>ldstr</c> operand is rendered from raw <c>#US</c> content, and a
    /// multi-line string literal is ordinary C# — so this fires on benign
    /// assemblies, not just hostile ones. The operand must be escaped rather than
    /// folded: escaping keeps the line intact *and* keeps the string's content
    /// recoverable, matching what the <c>IL</c> section already does.
    /// </summary>
    [Theory]
    [InlineData("\n", "\\n")]
    [InlineData("\r", "\\r")]
    [InlineData("\u0085", "\\u0085")]
    [InlineData("\u2028", "\\u2028")]
    [InlineData("\u2029", "\\u2029")]
    public void AnnotatedSource_UserStringIsEscapedLosslessly(string terminator, string escaped)
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-userstring-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var assemblyName = new AssemblyName("MultiLineUserString");
            var assemblyBuilder = new PersistedAssemblyBuilder(
                assemblyName, typeof(object).Assembly);
            var moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);
            var typeBuilder = moduleBuilder.DefineType(
                "Probe.Banner",
                TypeAttributes.Public | TypeAttributes.Class);
            var method = typeBuilder.DefineMethod(
                "Text",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(string),
                Type.EmptyTypes);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldstr, $"first{terminator}second");
            il.Emit(OpCodes.Ret);
            typeBuilder.CreateType();

            var dllPath = Path.Combine(tempDir, "MultiLineUserString.dll");
            assemblyBuilder.Save(dllPath);

            using var source = MetadataSource.Open(dllPath);
            var projection = ResearchViews.ProjectMember(
                new ResearchViews.MemberProjectionRequest(
                    source,
                    "Probe.Banner",
                    "Text",
                    AnnotatedSource: true));
            var output = Assert.IsType<string>(projection.AnnotatedSource?.Output);

            var stringLine = Assert.Single(
                output.ReplaceLineEndings("\n").Split('\n'),
                line => line.Contains(": ldstr", StringComparison.Ordinal));

            // Escaped, not folded: the terminator is still identifiable, and the
            // whole literal stayed on the instruction's comment line.
            Assert.Contains($"\"first{escaped}second\"", stringLine, StringComparison.Ordinal);
            Assert.StartsWith("//", stringLine.TrimStart(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
