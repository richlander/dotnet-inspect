using System.CommandLine;
using ILInspector.Metadata;
using ILInspector.TypeScriptGeneration;

namespace TsJsExport;

public static class TsJsExportCommand
{
    public static int Invoke(
        string[] args,
        TextWriter? output = null,
        TextWriter? error = null) =>
        CreateRootCommand(output, error).Parse(args).Invoke();

    public static RootCommand CreateRootCommand(
        TextWriter? output = null,
        TextWriter? error = null)
    {
        TextWriter stdout = output ?? Console.Out;
        TextWriter stderr = error ?? Console.Error;

        var assemblyArgument = new Argument<string>("assembly")
        {
            Description =
                "Path to a .NET assembly exposing supported static [JSExport] methods.",
        };
        var runtimeModuleOption = new Option<string?>("--runtime-module")
        {
            Description =
                "Module specifier for the SDK-owned dotnet.js imported by the generated facade.",
        };
        var outputOption = new Option<string?>("--output")
        {
            Description =
                "Path to publish the generated TypeScript module. Writes to stdout when omitted.",
        };

        var rootCommand = new RootCommand(
            "Generates one typed TypeScript facade from an assembly's authenticated "
                + "[JSExport] surface.")
        {
            assemblyArgument,
            runtimeModuleOption,
            outputOption,
        };

        rootCommand.SetAction(parseResult =>
        {
            string assemblyPath = parseResult.GetValue(assemblyArgument)!;
            string? runtimeModule =
                parseResult.GetValue(runtimeModuleOption);
            string? outputPath = parseResult.GetValue(outputOption);
            if (string.IsNullOrWhiteSpace(runtimeModule))
            {
                stderr.WriteLine(
                    "ts-jsexport: --runtime-module requires a non-empty module specifier.");
                return 1;
            }
            global::ILInspector.JsExportSurface.JsExportSurface? surface;
            try
            {
                if (!JsExportSurfaceLoader.TryLoad(
                        assemblyPath,
                        "ts-jsexport",
                        stderr,
                        out surface))
                {
                    return 1;
                }
            }
            catch (UnsupportedMetadataFormatException ex)
            {
                stderr.WriteLine($"ts-jsexport: {ex.Message}");
                return 1;
            }
            catch (MalformedMetadataRootException ex)
            {
                stderr.WriteLine($"ts-jsexport: {ex.Message}");
                return 1;
            }

            var diagnostics = new TypeScriptGenerationDiagnostics();
            string generated;
            try
            {
                generated = TypeScriptFacadeEmitter.Emit(
                    surface!,
                    runtimeModule,
                    diagnostics);
            }
            catch (UnsupportedWireContractException ex)
            {
                stderr.WriteLine($"ts-jsexport: {ex.Message}");
                return 1;
            }

            foreach (TypeScriptGenerationDiagnostic diagnostic in diagnostics.UnmappedTypes)
            {
                stderr.WriteLine(
                    $"ts-jsexport: {diagnostic.Location}: "
                        + $"{diagnostic.CSharpType} has no TypeScript mapping.");
            }
            if (diagnostics.HasUnmappedTypes)
                return 1;

            if (outputPath is null)
            {
                stdout.Write(generated);
                return 0;
            }

            try
            {
                PublishAtomically(outputPath, generated);
                return 0;
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException
                    or ArgumentException or NotSupportedException)
            {
                stderr.WriteLine(
                    $"ts-jsexport: could not write TypeScript module to "
                        + $"'{outputPath}': {ex.Message}");
                return 1;
            }
        });

        return rootCommand;
    }

    static void PublishAtomically(string outputPath, string content)
    {
        string fullPath = Path.GetFullPath(outputPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory)
            || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                $"Output directory does not exist: {directory}");
        }

        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, content);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
