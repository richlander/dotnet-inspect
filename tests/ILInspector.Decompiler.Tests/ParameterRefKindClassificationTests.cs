using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Call-site ref kinds come from the shared Metadata classifier
/// (<c>ParameterByRefKinds</c>), so the decompiler agrees with declaration spelling
/// and with Roslyn's metadata reader.
/// </summary>
public sealed class ParameterRefKindClassificationTests
{
    const string Source = """
        using System.Runtime.InteropServices;

        public static class Sample
        {
            public static void Passing(
                ref int plain,
                [In] ref int interopIn,
                [In, Out] ref int interopInOut,
                out int result,
                in int readOnly,
                ref readonly int location,
                int byValue) => result = 0;
        }
        """;

    [Fact]
    public void ReadParameterRefKinds_SpellsCompilerEncodedPassingModes()
    {
        byte[] image = Compile();
        using var pe = new PEReader(new MemoryStream(image));
        var result = Read(pe.GetMetadataReader(), "Sample", "Passing");

        Assert.Equal(ParameterRefKindFacts.Known, result.State);
        Assert.Equal(
            [
                ArgumentRefKind.Ref,
                ArgumentRefKind.Ref,
                ArgumentRefKind.Ref,
                ArgumentRefKind.Out,
                ArgumentRefKind.In,
                ArgumentRefKind.In,
                ArgumentRefKind.Value,
            ],
            result.Kinds);
        Assert.True(result.HasRefReadOnlyParameters);
    }

    /// <remarks>
    /// Roslyn reads <c>Out</c> without <c>In</c> as <c>out</c> before consulting
    /// <c>IsReadOnlyAttribute</c> or <c>RequiresLocationAttribute</c>. The compiler
    /// never emits that combination, so the <c>in</c> and <c>ref readonly</c> rows
    /// are patched to carry only the <c>Out</c> flag.
    /// </remarks>
    [Fact]
    public void ReadParameterRefKinds_SpellsOutOnlyFlagAsOutBeforeCompilerAttributes()
    {
        byte[] image = Compile();
        using (var pe = new PEReader(new MemoryStream(image)))
        {
            var reader = pe.GetMetadataReader();
            var method = FindMethod(reader, "Sample", "Passing");
            int table = pe.PEHeaders.MetadataStartOffset + reader.GetTableMetadataOffset(TableIndex.Param);
            int rowSize = reader.GetTableRowSize(TableIndex.Param);
            foreach (var handle in method.GetParameters())
            {
                if (reader.GetParameter(handle).SequenceNumber is not (5 or 6))
                    continue;
                int row = MetadataTokens.GetRowNumber(handle);
                BinaryPrimitives.WriteUInt16LittleEndian(
                    image.AsSpan(table + (row - 1) * rowSize),
                    (ushort)ParameterAttributes.Out);
            }
        }

        using var patched = new PEReader(new MemoryStream(image));
        var result = Read(patched.GetMetadataReader(), "Sample", "Passing");

        Assert.Equal(ArgumentRefKind.Out, result.Kinds[4]);
        Assert.Equal(ArgumentRefKind.Out, result.Kinds[5]);
        Assert.Equal(ArgumentRefKind.Ref, result.Kinds[1]);
        Assert.False(result.HasRefReadOnlyParameters);
    }

    /// <remarks>
    /// The motivating real asset (#3537): the runtime declares
    /// <c>FindConnectionPoint([In] ref Guid riid, out IConnectionPoint ppCP)</c>.
    /// </remarks>
    [Fact]
    public void ReadParameterRefKinds_SpellsCoreLibInteropInRefAsRef()
    {
        using var pe = new PEReader(File.OpenRead(
            typeof(System.Runtime.InteropServices.ComTypes.IConnectionPointContainer).Assembly.Location));
        var reader = pe.GetMetadataReader();

        var result = Read(
            reader,
            "IConnectionPointContainer",
            "FindConnectionPoint",
            "System.Runtime.InteropServices.ComTypes");

        Assert.Equal([ArgumentRefKind.Ref, ArgumentRefKind.Out], result.Kinds);
        Assert.False(result.HasRefReadOnlyParameters);
    }

    static ParameterRefKindResult Read(MetadataReader reader, string typeName, string methodName, string @namespace = "")
    {
        var method = FindMethod(reader, typeName, methodName, @namespace);
        var signature = GuardedDecode.MethodSignature(reader, method, GenericScope.Empty);
        return MethodDefinitionFacts.ReadParameterRefKinds(reader, method, signature.ParameterTypes);
    }

    static MethodDefinition FindMethod(MetadataReader reader, string typeName, string methodName, string @namespace = "")
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(type.Name) != typeName || reader.GetString(type.Namespace) != @namespace)
                continue;
            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) == methodName)
                    return method;
            }
        }

        throw new InvalidOperationException($"Method '{typeName}.{methodName}' was not found.");
    }

    static byte[] Compile()
    {
        var compilation = CSharpCompilation.Create(
            "parameter-ref-kind-classification",
            [CSharpSyntaxTree.ParseText(Source, new CSharpParseOptions(LanguageVersion.Preview))],
            RoslynTestReferences.TrustedPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        using var output = new MemoryStream();
        var emit = compilation.Emit(output);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return output.ToArray();
    }
}
