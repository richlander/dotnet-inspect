using System.CommandLine;
using System.Reflection.PortableExecutable;
using ILInspector.Analysis;
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

            if (!File.Exists(assemblyPath))
            {
                stderr.WriteLine($"tsbindgen: assembly not found: {assemblyPath}");
                return 1;
            }

            ApiSurface apiSurface;
            try
            {
                using FileStream stream = File.OpenRead(assemblyPath);
                using var peReader = new PEReader(stream);

                // includeAll: true, not false. The [JSExport] wire boundary is not "public API" in
                // the documentation sense: a consuming assembly commonly keeps its
                // JsonSerializerContext (and the DTOs it roots) internal, since nothing outside the
                // assembly ever touches them in C# — the wasm/JS boundary is their only external
                // consumer. JsExportSurfaceBuilder's record/enum discovery walks surface.Types
                // looking for a JsonSerializerContext-derived type; extracting public-only silently
                // drops that type (and therefore every DTO it roots) whenever it's internal, which
                // collapses every JSON-shaped return/parameter to "unknown" instead of a real
                // interface.
                apiSurface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
            }
            catch (Exception ex) when (
                ex is BadImageFormatException or IOException or UnauthorizedAccessException)
            {
                stderr.WriteLine($"tsbindgen: could not read '{assemblyPath}' as a .NET assembly: {ex.Message}");
                return 1;
            }

            global::ILInspector.JsExportSurface.JsExportSurface jsExportSurface;
            try
            {
                // Only DirectCalls (MethodEvidence) is needed for wire-contract resolution;
                // allocation/optimization-opportunity analysis is unrelated work this command
                // doesn't consume, so it's explicitly left out rather than paid for by default.
                LibraryBodyIndex bodyIndex = LibraryBodyIndex.Open(
                    assemblyPath, LibraryBodyAnalysisFeatures.MethodEvidence);
                jsExportSurface = JsExportSurfaceBuilder.Build(apiSurface, bodyIndex);
            }
            catch (Exception ex) when (
                ex is BadImageFormatException or IOException or UnauthorizedAccessException)
            {
                stderr.WriteLine(
                    $"tsbindgen: could not read IL bodies from '{assemblyPath}' for wire-contract "
                        + $"resolution: {ex.Message}");
                return 1;
            }

            var diagnostics = new TsBindGenDiagnostics();
            string generated = DtsEmitter.Emit(jsExportSurface, diagnostics);
            int exitCode = diagnostics.HasUnmappedTypes ? 1 : 0;

            foreach (TsBindGenDiagnostic diagnostic in diagnostics.UnmappedTypes)
            {
                stderr.WriteLine(
                    $"tsbindgen: {diagnostic.Location}: {diagnostic.CSharpType} has no TypeScript mapping.");
            }

            if (emitJsPath is not null)
            {
                string generatedJs = JsEmitter.Emit(jsExportSurface);
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

            stdout.WriteLine($"tsbindgen: no drift detected against {diffAgainst}.");
            return exitCode;
        });

        return rootCommand;
    }
}
