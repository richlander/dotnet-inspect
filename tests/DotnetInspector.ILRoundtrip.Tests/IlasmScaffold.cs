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
    /// Renders a compilation unit containing the whole module's type skeleton —
    /// every type (with nesting), field, and method signature — so any
    /// same-assembly operand resolves. The target method carries its real
    /// disassembled body; every other method is a <c>ldnull/throw</c> stub.
    /// </summary>
    public static string BuildCompilationUnit(PEReader peReader, MetadataReader reader, MethodDefinition method)
    {
        // One extern per assembly ref in the source module so canonical
        // [asm]-qualified operands resolve regardless of which facade the
        // compiler bound against.
        var sb = new StringBuilder();
        foreach (var arh in reader.AssemblyReferences)
            sb.AppendLine($".assembly extern {CanonicalIL.QuoteDottedName(reader.GetString(reader.GetAssemblyReference(arh).Name))} {{ }}");
        sb.AppendLine(".assembly roundtrip { }");
        sb.AppendLine(".module roundtrip.dll");
        sb.AppendLine();

        // Pass 1: empty type shells. ILAssembler resolves type references
        // eagerly (no forward references), so every typedef must exist before
        // any method body mentions it; ilasm class re-opening makes this legal.
        foreach (var tdh in TopLevelTypes(reader))
            EmitTypeShell(reader, tdh, sb, indent: 0);
        sb.AppendLine();

        // Pass 2: full definitions (fields, method stubs, the real target body).
        foreach (var tdh in TopLevelTypes(reader))
            EmitType(peReader, reader, tdh, method, sb, indent: 0);

        return sb.ToString();
    }

    static IEnumerable<TypeDefinitionHandle> TopLevelTypes(MetadataReader reader)
    {
        foreach (var tdh in reader.TypeDefinitions)
        {
            var td = reader.GetTypeDefinition(tdh);
            if (!td.GetDeclaringType().IsNil)
                continue; // emitted by its enclosing type
            if (reader.GetString(td.Name) == "<Module>")
                continue;
            yield return tdh;
        }
    }

    static void EmitTypeShell(MetadataReader reader, TypeDefinitionHandle tdh, StringBuilder sb, int indent)
    {
        string pad = new(' ', indent);
        sb.AppendLine($"{pad}{TypeHeader(reader, tdh, indent, includeGenericParams: true, includeBase: false)}");
        sb.AppendLine($"{pad}{{");
        foreach (var nested in reader.GetTypeDefinition(tdh).GetNestedTypes())
            EmitTypeShell(reader, nested, sb, indent + 2);
        sb.AppendLine($"{pad}}}");
    }

    static string TypeHeader(MetadataReader reader, TypeDefinitionHandle tdh, int indent, bool includeGenericParams, bool includeBase = true)
    {
        var td = reader.GetTypeDefinition(tdh);
        var attrs = td.Attributes;
        bool isInterface = (attrs & System.Reflection.TypeAttributes.Interface) != 0;

        string ns = CanonicalIL.QuoteDottedName(reader.GetString(td.Namespace));
        string nm = CanonicalIL.QuoteName(reader.GetString(td.Name));
        string declName = indent == 0 && ns.Length > 0 ? $"{ns}.{nm}" : nm;

        // Generic parameters are declared by the shell pass only; redeclaring
        // them when the class is re-opened registers duplicate GenericParam
        // rows and the metadata builder rejects the unsorted table.
        var typeGenParams = td.GetGenericParameters();
        string genDecl = includeGenericParams && typeGenParams.Count > 0
            ? "<" + string.Join(", ", typeGenParams.Select(g => CanonicalIL.QuoteName(reader.GetString(reader.GetGenericParameter(g).Name)))) + ">"
            : "";

        // Base types resolve whether cross-assembly (TypeReference) or
        // same-assembly (TypeDefinition) — but only once every shell exists, so
        // the shell pass omits extends (a same-module base may be declared later).
        string extends = !includeBase || td.BaseType.IsNil
            ? ""
            : $" extends {CanonicalIL.ResolveTypeHandle(reader, td.BaseType)}";

        return ".class "
            + (isInterface ? "interface " : "")
            + (indent > 0 ? "nested public " : "public ")
            + "auto ansi "
            + ((attrs & System.Reflection.TypeAttributes.Abstract) != 0 ? "abstract " : "")
            + ((attrs & System.Reflection.TypeAttributes.Sealed) != 0 ? "sealed " : "")
            + "beforefieldinit "
            + declName + genDecl + extends;
    }

    static void EmitType(PEReader peReader, MetadataReader reader, TypeDefinitionHandle tdh, MethodDefinition target, StringBuilder sb, int indent)
    {
        var td = reader.GetTypeDefinition(tdh);
        string pad = new(' ', indent);

        sb.AppendLine($"{pad}{TypeHeader(reader, tdh, indent, includeGenericParams: false)}");
        sb.AppendLine($"{pad}{{");

        foreach (var fh in td.GetFields())
        {
            var field = reader.GetFieldDefinition(fh);
            string fieldType = field.DecodeSignature(ILSignatureTypeProvider.Instance, genericContext: null);
            bool fieldStatic = (field.Attributes & System.Reflection.FieldAttributes.Static) != 0;
            sb.AppendLine($"{pad}  .field public {(fieldStatic ? "static " : "")}{fieldType} {CanonicalIL.QuoteName(reader.GetString(field.Name))}");
        }

        foreach (var mh in td.GetMethods())
        {
            var m = reader.GetMethodDefinition(mh);
            bool isTarget = m.RelativeVirtualAddress != 0
                && m.RelativeVirtualAddress == target.RelativeVirtualAddress
                && m.Name == target.Name;
            EmitMethod(peReader, reader, m, isTarget, sb, indent + 2);
        }

        foreach (var nested in td.GetNestedTypes())
            EmitType(peReader, reader, nested, target, sb, indent + 2);

        sb.AppendLine($"{pad}}}");
    }

    static void EmitMethod(PEReader peReader, MetadataReader reader, MethodDefinition method, bool isTarget, StringBuilder sb, int indent)
    {
        string pad = new(' ', indent);
        string name = reader.GetString(method.Name);
        var sig = method.DecodeSignature(ILSignatureTypeProvider.Instance, genericContext: null);

        var paramNames = method.GetParameters()
            .Select(ph => reader.GetString(reader.GetParameter(ph).Name))
            .Where(n => n.Length > 0)
            .ToArray();
        string paramList = string.Join(", ",
            sig.ParameterTypes.Select((t, i) => $"{t} {(i < paramNames.Length ? $"'{paramNames[i]}'" : $"'p{i}'")}"));

        var genParams = method.GetGenericParameters();
        string genDecl = genParams.Count > 0
            ? "<" + string.Join(", ", genParams.Select(g => CanonicalIL.QuoteName(reader.GetString(reader.GetGenericParameter(g).Name)))) + ">"
            : "";

        bool isStatic = (method.Attributes & System.Reflection.MethodAttributes.Static) != 0;
        bool isCtor = name is ".ctor" or ".cctor";
        string flags = (isStatic ? "static " : "")
            + (isCtor ? "specialname rtspecialname " : "");
        string methodName = isCtor ? name : CanonicalIL.QuoteName(name);

        sb.AppendLine($"{pad}.method public hidebysig {flags}{sig.ReturnType} {methodName}{genDecl}({paramList}) cil managed");
        sb.AppendLine($"{pad}{{");

        if (isTarget)
        {
            var instructions = ILDisassembler.Disassemble(peReader, reader, method, ILSyntax.Canonical)
                ?? throw new InvalidOperationException($"No IL body for {name}");
            var body = peReader.GetMethodBody(method.RelativeVirtualAddress);

            sb.AppendLine($"{pad}  .maxstack {body.MaxStack}");
            if (!body.LocalSignature.IsNil)
            {
                var localTypes = reader.GetStandaloneSignature(body.LocalSignature)
                    .DecodeLocalSignature(ILSignatureTypeProvider.Instance, genericContext: null);
                sb.AppendLine($"{pad}  .locals init ({string.Join(", ", localTypes.Select((t, i) => $"{t} V_{i}"))})");
            }
            foreach (var instr in instructions)
                sb.AppendLine($"{pad}  {instr}");
            foreach (var region in peReader.GetMethodBody(method.RelativeVirtualAddress).ExceptionRegions)
            {
                string clause = region.Kind switch
                {
                    ExceptionRegionKind.Catch => $"catch {CanonicalIL.ResolveTypeHandle(reader, region.CatchType)}",
                    ExceptionRegionKind.Finally => "finally",
                    ExceptionRegionKind.Filter => $"filter IL_{region.FilterOffset:X4}",
                    ExceptionRegionKind.Fault => "fault",
                    _ => throw new InvalidOperationException($"Unknown region kind {region.Kind}")
                };
                sb.AppendLine($"{pad}  .try IL_{region.TryOffset:X4} to IL_{region.TryOffset + region.TryLength:X4} {clause} handler IL_{region.HandlerOffset:X4} to IL_{region.HandlerOffset + region.HandlerLength:X4}");
            }
        }
        else if (method.RelativeVirtualAddress != 0)
        {
            // Stub body: any signature can throw.
            sb.AppendLine($"{pad}  .maxstack 8");
            sb.AppendLine($"{pad}  ldnull");
            sb.AppendLine($"{pad}  throw");
        }
        // Abstract/extern methods (RVA 0): empty body block → no RVA emitted.

        sb.AppendLine($"{pad}}}");
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
            string? crash = null;
            try
            {
                (diagnostics, result) = new DocumentCompiler().Compile(
                    new SourceText(il, "roundtrip.il"),
                    _ => throw new InvalidOperationException("no includes expected"),
                    _ => throw new InvalidOperationException("no resources expected"),
                    new Options());
            }
            catch (Exception ex)
            {
                // ILAssembler can throw (rather than diagnose) on inputs its error
                // recovery doesn't handle; surface as a failure, not a test crash.
                crash = $"ILAssembler threw {ex.GetType().Name}: {ex.Message}";
                result = null;
            }
            finally
            {
                Console.SetError(realErr);
            }

            string parserErrors = stderr.ToString().ReplaceLineEndings(" / ").Trim();
            PEReader? image = null;
            if (result is not null)
            {
                try
                {
                    var blob = new BlobBuilder();
                    result.Serialize(blob);
                    image = new PEReader(blob.ToImmutableArray());
                }
                catch (Exception ex)
                {
                    crash = $"PE serialization threw {ex.GetType().Name}: {ex.Message}";
                }
            }
            if (crash is not null)
                parserErrors = parserErrors.Length > 0 ? $"{crash} / {parserErrors}" : crash;

            return new AssembleResult(diagnostics, parserErrors, image);
        }
    }

    /// <summary>Nesting path of a type, e.g. <c>Outer/&lt;&gt;c</c> (no namespace).</summary>
    public static string TypePath(MetadataReader reader, TypeDefinitionHandle tdh)
    {
        var td = reader.GetTypeDefinition(tdh);
        string name = reader.GetString(td.Name);
        var declaring = td.GetDeclaringType();
        return declaring.IsNil ? name : $"{TypePath(reader, declaring)}/{name}";
    }

    /// <summary>
    /// Rendered signature (generic arity + return + parameter types) for overload
    /// disambiguation. Conversion operators overload by return type alone, and
    /// generic overloads can render identical !!N parameter lists at different
    /// arities (JsonMetadataServices.CreateIListInfo&lt;T&gt; vs &lt;TC, TE&gt;).
    /// </summary>
    public static string ParamTypes(MethodDefinition method)
    {
        var sig = method.DecodeSignature(ILSignatureTypeProvider.Instance, genericContext: null);
        return $"`{sig.GenericParameterCount} {sig.ReturnType}({string.Join(",", sig.ParameterTypes)})";
    }

    /// <summary>
    /// Finds a method by name in an image and disassembles it. Optional
    /// declaring-type path (see <see cref="TypePath"/>) and signature string
    /// (see <see cref="ParamTypes"/>) disambiguate within the full module
    /// skeleton (stubs share names with their originals elsewhere).
    /// </summary>
    public static List<ILInstruction>? DisassembleByName(PEReader peReader, string methodName, string? typePath = null, string? paramTypes = null)
    {
        var reader = peReader.GetMetadataReader();
        foreach (var tdh in reader.TypeDefinitions)
        {
            var td = reader.GetTypeDefinition(tdh);
            if (typePath is not null && TypePath(reader, tdh) != typePath)
                continue;
            foreach (var mh in td.GetMethods())
            {
                var method = reader.GetMethodDefinition(mh);
                if (reader.GetString(method.Name) != methodName)
                    continue;
                if (paramTypes is not null && ParamTypes(method) != paramTypes)
                    continue;
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
