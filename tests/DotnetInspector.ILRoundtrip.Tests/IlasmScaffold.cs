using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

using DotnetInspector.Metadata;

using ILAssembler;

namespace DotnetInspector.ILRoundtrip.Tests;

/// <summary>Result of an in-memory assembly attempt.</summary>
public sealed record AssembleResult(
    ImmutableArray<Diagnostic> Diagnostics,
    string ParserErrors,
    PEReader? Image)
{
    public bool Succeeded => Image is not null
        && ParserErrors.Length == 0
        && !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    public string Describe()
    {
        var sb = new StringBuilder();
        foreach (var d in Diagnostics)
            sb.AppendLine($"{d.Severity} {d.Id}: {d.Message}");
        if (ParserErrors.Length > 0)
            sb.AppendLine($"parser: {ParserErrors}");
        return sb.ToString();
    }
}

/// <summary>
/// Wraps dotnet-inspect disassembly in ilasm scaffolding and assembles it
/// in-memory with the vendored managed ILAssembler.
/// </summary>
public static class IlasmScaffold
{
    // ILAssembler's ANTLR parser reports syntax errors only on Console.Error
    // (they are not surfaced as diagnostics); capturing requires swapping the
    // global writer, so assembly calls are serialized.
    static readonly Lock s_consoleLock = new();

    /// <summary>
    /// Renders a full single-method compilation unit for <paramref name="method"/>:
    /// assembly externs, a wrapper class, the method header, .maxstack/.locals,
    /// the instruction stream, and exception regions in label form.
    /// </summary>
    public static string BuildCompilationUnit(PEReader peReader, MetadataReader reader, MethodDefinition method)
    {
        string name = reader.GetString(method.Name);
        var instructions = ILDisassembler.Disassemble(peReader, reader, method)
            ?? throw new InvalidOperationException($"No IL body for {name}");
        var body = peReader.GetMethodBody(method.RelativeVirtualAddress);
        var sigProvider = new ILSyntaxProvider(reader);

        var sig = method.DecodeSignature(sigProvider, genericContext: null);
        var paramNames = method.GetParameters()
            .Select(ph => reader.GetString(reader.GetParameter(ph).Name))
            .Where(n => n.Length > 0)
            .ToArray();
        string paramList = string.Join(", ",
            sig.ParameterTypes.Select((t, i) => $"{t} {(i < paramNames.Length ? $"'{paramNames[i]}'" : $"'p{i}'")}"));

        var genParams = method.GetGenericParameters();
        string genDecl = genParams.Count > 0
            ? "<" + string.Join(", ", genParams.Select(g => reader.GetString(reader.GetGenericParameter(g).Name))) + ">"
            : "";

        string localsDirective = "";
        if (!body.LocalSignature.IsNil)
        {
            var localSig = reader.GetStandaloneSignature(body.LocalSignature);
            var localTypes = localSig.DecodeLocalSignature(sigProvider, genericContext: null);
            localsDirective = $"    .locals init ({string.Join(", ", localTypes.Select((t, i) => $"{t} V_{i}"))})\n";
        }

        var ehDirectives = new StringBuilder();
        foreach (var region in body.ExceptionRegions)
        {
            string tryRange = $".try IL_{region.TryOffset:X4} to IL_{region.TryOffset + region.TryLength:X4}";
            string handlerRange = $"handler IL_{region.HandlerOffset:X4} to IL_{region.HandlerOffset + region.HandlerLength:X4}";
            string clause = region.Kind switch
            {
                ExceptionRegionKind.Catch => $"catch {sigProvider.RenderTypeHandle(region.CatchType)}",
                ExceptionRegionKind.Finally => "finally",
                ExceptionRegionKind.Filter => $"filter IL_{region.FilterOffset:X4}",
                ExceptionRegionKind.Fault => "fault",
                _ => throw new InvalidOperationException($"Unknown region kind {region.Kind}")
            };
            ehDirectives.AppendLine($"    {tryRange} {clause} {handlerRange}");
        }

        var bodyText = new StringBuilder();
        foreach (var instr in instructions)
            bodyText.AppendLine($"    {instr}");

        // Wrapper class name is intentionally un-namespaced: ILAssembler does not
        // yet resolve member refs to dotted typedef names (upstream gap).
        return $$"""
.assembly extern System.Runtime { }
.assembly roundtrip { }
.module roundtrip.dll

.class public auto ansi beforefieldinit RoundtripProbe extends [System.Runtime]System.Object
{
  .method public hidebysig static {{sig.ReturnType}} {{name}}{{genDecl}}({{paramList}}) cil managed
  {
    .maxstack {{body.MaxStack}}
{{localsDirective}}{{bodyText}}{{ehDirectives}}  }
}
""";
    }

    /// <summary>Assembles IL text fully in-memory, capturing parser stderr.</summary>
    public static AssembleResult Assemble(string il)
    {
        lock (s_consoleLock)
        {
            var stderr = new StringWriter();
            var realErr = Console.Error;
            Console.SetError(stderr);

            ImmutableArray<Diagnostic> diagnostics = [];
            PEBuilder? result = null;
            try
            {
                (diagnostics, result) = new DocumentCompiler().Compile(
                    new SourceText(il, "roundtrip.il"),
                    _ => throw new InvalidOperationException("no includes expected"),
                    _ => throw new InvalidOperationException("no resources expected"),
                    new Options());
            }
            finally
            {
                Console.SetError(realErr);
            }

            string parserErrors = stderr.ToString().ReplaceLineEndings(" / ").Trim();
            PEReader? image = null;
            if (result is not null)
            {
                var blob = new BlobBuilder();
                result.Serialize(blob);
                image = new PEReader(blob.ToImmutableArray());
            }

            return new AssembleResult(diagnostics, parserErrors, image);
        }
    }

    /// <summary>Finds a method by name in an image and disassembles it.</summary>
    public static List<ILInstruction>? DisassembleByName(PEReader peReader, string methodName)
    {
        var reader = peReader.GetMetadataReader();
        foreach (var tdh in reader.TypeDefinitions)
        {
            var td = reader.GetTypeDefinition(tdh);
            foreach (var mh in td.GetMethods())
            {
                var method = reader.GetMethodDefinition(mh);
                if (reader.GetString(method.Name) == methodName)
                    return ILDisassembler.Disassemble(peReader, reader, method);
            }
        }
        return null;
    }

    /// <summary>Finds a method definition by name in the fixture assembly.</summary>
    public static MethodDefinition FindMethod(MetadataReader reader, string typeName, string methodName)
    {
        foreach (var tdh in reader.TypeDefinitions)
        {
            var td = reader.GetTypeDefinition(tdh);
            if (reader.GetString(td.Name) != typeName)
                continue;
            foreach (var mh in td.GetMethods())
            {
                var method = reader.GetMethodDefinition(mh);
                if (reader.GetString(method.Name) == methodName)
                    return method;
            }
        }
        throw new InvalidOperationException($"{typeName}.{methodName} not found");
    }

    /// <summary>
    /// Normalizes opcode names so equivalent encodings compare equal
    /// (short/long forms, macro forms like ldarg.0 vs ldarg.s).
    /// </summary>
    public static string CanonicalOpcode(string op)
    {
        // Strip a ".s" short-form suffix; branch kind (brtrue vs brfalse vs br)
        // is preserved because re-encoding only changes short/long form.
        string trimmed = op.EndsWith(".s", StringComparison.Ordinal) ? op[..^2] : op;
        if (trimmed.StartsWith("ldarg", StringComparison.Ordinal)) return "ldarg";
        if (trimmed.StartsWith("ldloc", StringComparison.Ordinal)) return "ldloc";
        if (trimmed.StartsWith("stloc", StringComparison.Ordinal)) return "stloc";
        if (trimmed.StartsWith("ldc.i4", StringComparison.Ordinal)) return "ldc.i4";
        return trimmed;
    }
}
