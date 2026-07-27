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
}
