using System.Text;
using ILInspector.JsExportSurface;

namespace tsbindgen;

/// <summary>
/// Projects a <see cref="JsExportSurface"/> into a runtime <c>.js</c> module: the wasm bootstrap
/// (<c>dotnet.create()</c> / <c>getAssemblyExports()</c>) plus one typed wrapper function per
/// <c>[JSExport]</c> export. This replaces a hand-maintained bridge, which
/// duplicated this exact bootstrap-and-JSON.parse boilerplate by hand and could silently drift
/// from the assembly's real exports. Every JSON-string envelope this module parses corresponds
/// exactly to the DTO type <see cref="DtsEmitter"/> already put in the sibling <c>.d.ts</c> via
/// <see cref="JsExportFunction.ReturnWireType"/> — the same wire-contract resolution drives both,
/// so the two files can't independently drift from each other.
/// </summary>
/// <remarks>
/// <c>initializeEngine</c> returns the raw <c>getAssemblyExports()</c> object so a caller that
/// needs an export this module hasn't been asked to wrap can reuse the same wasm runtime instance
/// instead of calling <c>dotnet.create()</c> a second time, which would load a second full runtime
/// in the browser.
/// </remarks>
static class JsEmitter
{
    public static string Emit(ILInspector.JsExportSurface.JsExportSurface surface)
    {
        var sb = new StringBuilder();
        sb.Append("import { dotnet } from \"./_framework/dotnet.js\";\n\n");

        JsExportFunction[] functions = [.. surface.Functions.OrderBy(f => f.Name, StringComparer.Ordinal)];

        foreach (JsExportFunction function in functions)
        {
            sb.Append("let ").Append(CamelCase.FromPascalCase(function.Name)).Append("Export;\n");
        }

        sb.Append("\nexport async function initializeEngine(onStatus = () => {}) {\n");
        sb.Append("  onStatus(\"Loading .NET WebAssembly…\");\n");
        sb.Append("  const runtime = await dotnet.create();\n");
        sb.Append("  const config = runtime.getConfig();\n");
        sb.Append("  const exports = await runtime.getAssemblyExports(config.mainAssemblyName);\n");

        foreach (JsExportFunction function in functions)
        {
            string tsName = CamelCase.FromPascalCase(function.Name);
            sb.Append("  ").Append(tsName).Append("Export = exports.")
              .Append(function.DeclaringType).Append('.').Append(function.Name).Append(";\n");
        }

        // ConfigureHost is a bootstrap step, not an on-demand export: the browser host must know
        // its own origin (for MSDL-proxied source requests) before any other export is used, so
        // this generator calls it here rather than leaving every caller to remember to.
        JsExportFunction? configureHost = functions.FirstOrDefault(
            f => string.Equals(f.Name, "ConfigureHost", StringComparison.Ordinal));
        if (configureHost is not null)
        {
            sb.Append("  ").Append(CamelCase.FromPascalCase(configureHost.Name))
              .Append("Export(window.location.origin);\n");
        }

        sb.Append("  await runtime.runMain();\n");
        sb.Append("  return exports;\n");
        sb.Append("}\n");

        foreach (JsExportFunction function in functions)
        {
            EmitFunction(sb, function);
        }

        return sb.ToString();
    }

    static void EmitFunction(StringBuilder sb, JsExportFunction function)
    {
        string tsName = CamelCase.FromPascalCase(function.Name);
        bool isAsync = TsTypeMapper.IsAsyncReturnType(function.ReturnType);
        bool parsesJson = function.ReturnWireType is not null
            && TsTypeMapper.IsJsonEnvelopeReturnType(function.ReturnType);
        var parameters = function.Parameters.Select(p => CamelCase.FromPascalCase(p.Name)).ToArray();
        string parameterList = string.Join(", ", parameters);

        sb.Append('\n');
        sb.Append("export ").Append(isAsync ? "async " : "").Append("function ")
          .Append(tsName).Append('(').Append(parameterList).Append(") {\n");
        sb.Append("  if (!").Append(tsName).Append("Export) throw new Error(\"The browser inspection engine is not initialized.\");\n");

        string call = $"{tsName}Export({parameterList})";
        if (isAsync)
        {
            call = $"await {call}";
        }

        if (parsesJson)
        {
            sb.Append("  const result = ").Append(call).Append(";\n");
            sb.Append("  return JSON.parse(result);\n");
        }
        else
        {
            sb.Append("  return ").Append(call).Append(";\n");
        }

        sb.Append("}\n");
    }
}
