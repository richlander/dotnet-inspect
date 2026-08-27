using System.Text;
using System.Text.Encodings.Web;
using ILInspector.JsExportSurface;
using ILInspector.Metadata;

namespace tsbindgen;

/// <summary>
/// Projects a <see cref="JsExportSurface"/> into a runtime <c>.js</c> module: the wasm bootstrap
/// (<c>dotnet.create()</c> / <c>getAssemblyExports()</c>) plus one typed wrapper function per
/// <c>[JSExport]</c> export. This replaces a hand-maintained bridge, which
/// duplicated this exact bootstrap-and-JSON.parse boilerplate by hand and could silently drift
/// from the assembly's real exports. Every JSON-string envelope this module parses corresponds
/// exactly to the DTO type <see cref="DtsEmitter"/> already put in the sibling <c>.d.ts</c> via
/// <see cref="JsExportFunction.ReturnWireType"/> — the same wire-contract resolution drives both,
/// and both emitters consume the same mapped function-signature model.
/// </summary>
/// <remarks>
/// <c>initializeEngine</c> returns the raw <c>getAssemblyExports()</c> object so a caller that
/// needs an export this module hasn't been asked to wrap can reuse the same wasm runtime instance
/// instead of calling <c>dotnet.create()</c> a second time, which would load a second full runtime
/// in the browser.
/// </remarks>
static class JsEmitter
{
    public static string Emit(
        ILInspector.JsExportSurface.JsExportSurface surface,
        string declarationModuleSpecifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(declarationModuleSpecifier);

        string assemblyName = surface.AssemblyIdentity?.Name
            ?? throw new InvalidOperationException(
                "Runtime wrapper emission requires an assembly identity.");
        var sb = new StringBuilder();
        sb.Append("import { dotnet } from \"./_framework/dotnet.js\";\n\n");

        ApiType[] declarationTypes = DtsEmitter.DeclarationTypes(surface);
        TypeScriptFunctionSignature[] functions =
            DtsEmitter.MapFunctionSignatures(surface);
        string managedExportsTypeName =
            AllocateManagedExportsTypeName(declarationTypes);
        // JavaScriptEncoder leaves '*'; encode it so a module specifier cannot close the JSDoc.
        string encodedDeclarationModuleSpecifier =
            JavaScriptEncoder.Default.Encode(declarationModuleSpecifier)
                .Replace("*", "\\u002A", StringComparison.Ordinal);

        foreach (ApiType declarationType in declarationTypes
            .OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            sb.Append("/** @typedef {import(\"")
              .Append(encodedDeclarationModuleSpecifier).Append("\").")
              .Append(declarationType.Name).Append("} ")
              .Append(declarationType.Name).Append(" */\n");
        }
        if (declarationTypes.Length > 0)
            sb.Append('\n');

        EmitManagedExportsType(sb, functions, managedExportsTypeName);
        sb.Append("/** @type {").Append(managedExportsTypeName)
          .Append(" | undefined} */\n");
        sb.Append("let $managedExports;\n\n");
        sb.Append("/** @returns {").Append(managedExportsTypeName)
          .Append("} */\n");
        sb.Append("function $requireManagedExports() {\n");
        sb.Append("  if (!$managedExports) throw new Error(\"The browser inspection engine is not initialized.\");\n");
        sb.Append("  return $managedExports;\n");
        sb.Append("}\n\n");

        sb.Append("/**\n");
        sb.Append(" * @param {string} value\n");
        sb.Append(" * @returns {unknown}\n");
        sb.Append(" */\n");
        sb.Append("function $parseJson(value) {\n");
        sb.Append("  return JSON.parse(value);\n");
        sb.Append("}\n\n");

        sb.Append("/**\n");
        sb.Append(" * @param {(status: string) => void} [onStatus]\n");
        sb.Append(" * @returns {Promise<unknown>}\n");
        sb.Append(" */\n");
        sb.Append("export async function initializeEngine(onStatus = () => {}) {\n");
        sb.Append("  onStatus(\"Loading .NET WebAssembly…\");\n");
        sb.Append("  const runtime = await dotnet.create();\n");
        sb.Append("  // This assertion is the one untyped .NET runtime boundary. ")
          .Append(managedExportsTypeName).Append(" is\n");
        sb.Append("  // generated from the same authenticated surface as the wrappers below.\n");
        sb.Append("  // oxlint-disable-next-line typescript/no-unsafe-type-assertion\n");
        sb.Append("  const exports = /** @type {").Append(managedExportsTypeName)
          .Append("} */ (await runtime.getAssemblyExports(\"")
          .Append(JavaScriptEncoder.Default.Encode(assemblyName))
          .Append("\"));\n");

        // ConfigureHost is a bootstrap step, not an on-demand export: the browser host must know
        // its own origin (for MSDL-proxied source requests) before any other export is used, so
        // this generator calls it here rather than leaving every caller to remember to.
        TypeScriptFunctionSignature? configureHost = functions.FirstOrDefault(
            function => DtsEmitter.IsConfigureHostBootstrap(
                function.Function));
        if (configureHost is not null)
        {
            sb.Append("  exports.").Append(configureHost.Function.DeclaringType)
              .Append('.').Append(configureHost.Function.Name)
              .Append("(window.location.origin);\n");
        }

        sb.Append("  await runtime.runMain();\n");
        sb.Append("  $managedExports = exports;\n");
        sb.Append("  return exports;\n");
        sb.Append("}\n");

        foreach (TypeScriptFunctionSignature function in functions)
        {
            EmitFunction(sb, function);
        }

        return sb.ToString();
    }

    static void EmitManagedExportsType(
        StringBuilder sb,
        IReadOnlyList<TypeScriptFunctionSignature> functions,
        string managedExportsTypeName)
    {
        var root = new ExportTypeNode();
        foreach (TypeScriptFunctionSignature function in functions)
        {
            ExportTypeNode node = root;
            foreach (string segment in function.Function.DeclaringType.Split('.'))
            {
                if (!node.Children.TryGetValue(segment, out ExportTypeNode? child))
                {
                    child = new ExportTypeNode();
                    node.Children.Add(segment, child);
                }
                node = child;
            }
            node.Functions.Add(function);
        }

        sb.Append("/**\n");
        sb.Append(" * @typedef {{\n");
        EmitManagedExportsProperties(sb, root, indent: 2);
        sb.Append(" * }} ").Append(managedExportsTypeName).Append('\n');
        sb.Append(" */\n");
    }

    static string AllocateManagedExportsTypeName(
        IReadOnlyList<ApiType> declarationTypes)
    {
        var declarationNames = new HashSet<string>(
            declarationTypes.Select(type => type.Name),
            StringComparer.Ordinal);
        string name = "$ManagedExports";
        while (declarationNames.Contains(name))
            name += '$';
        return name;
    }

    static void EmitManagedExportsProperties(
        StringBuilder sb,
        ExportTypeNode node,
        int indent)
    {
        foreach ((string name, ExportTypeNode child) in node.Children)
        {
            sb.Append(" * ").Append(' ', indent).Append(name).Append(": {\n");
            EmitManagedExportsProperties(sb, child, indent + 2);
            sb.Append(" * ").Append(' ', indent).Append("},\n");
        }

        foreach (TypeScriptFunctionSignature function in node.Functions
            .OrderBy(function => function.Function.Name, StringComparer.Ordinal))
        {
            sb.Append(" * ").Append(' ', indent)
              .Append(function.Function.Name).Append(": (")
              .Append(string.Join(
                  ", ",
                  function.Parameters.Select(parameter =>
                      $"{parameter.Name}: {parameter.Type}")))
              .Append(") => ").Append(function.InteropReturnType).Append(",\n");
        }
    }

    static void EmitFunction(
        StringBuilder sb,
        TypeScriptFunctionSignature function)
    {
        bool isAsync = TsTypeMapper.IsAsyncReturnType(function.Function.ReturnType);
        bool parsesJson = function.Function.ReturnWireType is not null
            && TsTypeMapper.IsJsonEnvelopeReturnType(function.Function.ReturnType);
        string parameterList = string.Join(
            ", ",
            function.Parameters.Select(parameter => parameter.Name));

        sb.Append('\n');
        sb.Append("/**\n");
        foreach (TypeScriptParameterSignature parameter in function.Parameters)
        {
            sb.Append(" * @param {").Append(parameter.Type).Append("} ")
              .Append(parameter.Name).Append('\n');
        }
        sb.Append(" * @returns {").Append(function.ReturnType).Append("}\n");
        sb.Append(" */\n");
        sb.Append("export ").Append(isAsync ? "async " : "").Append("function ")
          .Append(function.Name).Append('(').Append(parameterList).Append(") {\n");
        sb.Append("  const $exports = $requireManagedExports();\n");

        string call =
            $"$exports.{function.Function.DeclaringType}.{function.Function.Name}({parameterList})";
        if (isAsync)
        {
            call = $"await {call}";
        }

        if (parsesJson)
        {
            sb.Append("  const $result = ").Append(call).Append(";\n");
            sb.Append("  const $parsed = $parseJson($result);\n");
            sb.Append("  // The authenticated serializer contract fixes this function's exact wire type.\n");
            sb.Append("  // oxlint-disable-next-line typescript/no-unsafe-type-assertion\n");
            sb.Append("  return /** @type {").Append(function.WireReturnType)
              .Append("} */ ($parsed);\n");
        }
        else
        {
            sb.Append("  return ").Append(call).Append(";\n");
        }

        sb.Append("}\n");
    }

    sealed class ExportTypeNode
    {
        public SortedDictionary<string, ExportTypeNode> Children { get; } =
            new(StringComparer.Ordinal);

        public List<TypeScriptFunctionSignature> Functions { get; } = [];
    }
}
