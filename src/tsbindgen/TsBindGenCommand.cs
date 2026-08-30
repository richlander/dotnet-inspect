using System.CommandLine;
using ILInspector.JsExportSurface;
using ILInspector.Metadata;

namespace tsbindgen;

/// <summary>
/// The <c>tsbindgen</c> command: generate TypeScript <c>.d.ts</c> declarations from an assembly's
/// <c>[JSExport]</c> wasm/JS interop surface, and optionally diff that output against a
/// hand-written TypeScript file to detect drift (for CI gating).
/// </summary>
public static class TsBindGenCommand
{
    public static int Invoke(string[] args, TextWriter? output = null, TextWriter? error = null)
        => CreateRootCommand(output, error).Parse(args).Invoke();

    public static RootCommand CreateRootCommand(TextWriter? output = null, TextWriter? error = null)
    {
        TextWriter stdout = output ?? Console.Out;
        TextWriter stderr = error ?? Console.Error;

        var assemblyArgument = new Argument<string>("assembly")
        {
            Description = "Path to a .NET assembly (.dll) exposing [JSExport] static members.",
        };

        var diffOption = new Option<string?>("--diff-against")
        {
            Description = "Path to a hand-written .d.ts/.ts file. Instead of printing generated output, compares it and exits non-zero on drift.",
        };

        var emitJsOption = new Option<string?>("--emit-js")
        {
            Description = "Path to write a generated runtime .js wrapper module (the wasm bootstrap "
                + "plus one typed function per [JSExport] export, replacing a hand-maintained shim "
                + "with a generated bridge module) alongside the printed/diffed .d.ts output.",
        };

        var rootCommand = new RootCommand(
            "Generates TypeScript declarations from an assembly's [JSExport] surface.")
        {
            assemblyArgument,
            diffOption,
            emitJsOption,
        };

        rootCommand.SetAction(parseResult =>
        {
            string assemblyPath = parseResult.GetValue(assemblyArgument)!;
            string? diffAgainst = parseResult.GetValue(diffOption);
            string? emitJsPath = parseResult.GetValue(emitJsOption);

            global::ILInspector.JsExportSurface.JsExportSurface?
                jsExportSurface;
            try
            {
                if (!JsExportSurfaceLoader.TryLoad(
                        assemblyPath,
                        "tsbindgen",
                        stderr,
                        out jsExportSurface))
                {
                    return 1;
                }
            }
            catch (UnsupportedMetadataFormatException ex)
            {
                stderr.WriteLine($"tsbindgen: {ex.Message}");
                return 1;
            }
            catch (MalformedMetadataRootException ex)
            {
                stderr.WriteLine($"tsbindgen: {ex.Message}");
                return 1;
            }
            if (emitJsPath is not null
                && jsExportSurface!.AssemblyIdentity is null)
            {
                stderr.WriteLine(
                    "tsbindgen: --emit-js requires an assembly manifest identity.");
                return 1;
            }

            var diagnostics = new TsBindGenDiagnostics();
            string generated;
            try
            {
                generated = DtsEmitter.Emit(jsExportSurface!, diagnostics);
            }
            catch (UnsupportedWireContractException ex)
            {
                stderr.WriteLine($"tsbindgen: {ex.Message}");
                return 1;
            }

            int exitCode = diagnostics.HasUnmappedTypes ? 1 : 0;

            foreach (TsBindGenDiagnostic diagnostic in diagnostics.UnmappedTypes)
            {
                stderr.WriteLine(
                    $"tsbindgen: {diagnostic.Location}: {diagnostic.CSharpType} has no TypeScript mapping.");
            }

            if (diffAgainst is not null)
            {
                if (!File.Exists(diffAgainst))
                {
                    stderr.WriteLine($"tsbindgen: --diff-against file not found: {diffAgainst}");
                    return 1;
                }

                string handWritten = File.ReadAllText(diffAgainst);
                if (!DriftDetector.IsCovered(generated, handWritten))
                {
                    stderr.WriteLine($"tsbindgen: drift detected against {diffAgainst}.");
                    stderr.WriteLine();
                    stderr.WriteLine("--- generated ---");
                    stderr.WriteLine(generated);
                    return 1;
                }
            }

            if (emitJsPath is not null
                && !diagnostics.HasUnmappedTypes)
            {
                string generatedJs = JsEmitter.Emit(jsExportSurface!);
                try
                {
                    File.WriteAllText(emitJsPath, generatedJs);
                }
                catch (Exception ex) when (
                    ex is IOException or UnauthorizedAccessException
                        or ArgumentException or NotSupportedException)
                {
                    stderr.WriteLine(
                        $"tsbindgen: could not write JavaScript module to '{emitJsPath}': {ex.Message}");
                    return 1;
                }
            }

            if (diffAgainst is null)
            {
                stdout.Write(generated);
                return exitCode;
            }

            stdout.WriteLine($"tsbindgen: no drift detected against {diffAgainst}.");
            return exitCode;
        });

        return rootCommand;
    }
}
