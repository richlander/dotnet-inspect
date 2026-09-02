using System.Collections.Immutable;
using System.CommandLine;
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
                "Path to a .NET export assembly, or to the assembly defining --context.",
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
        var contextTypeOption = new Option<string?>("--context")
        {
            Description =
                "Exact context type carrying JsExportRoot declarations.",
        };
        var assemblySearchPathOption =
            new Option<string[]>("--assembly-search-path")
            {
                Description =
                    "Additional file or directory used to resolve rooted assemblies.",
                AllowMultipleArgumentsPerToken = false,
            };

        var rootCommand = new RootCommand(
            "Generates typed TypeScript facades from authenticated [JSExport] surfaces.")
        {
            assemblyArgument,
            runtimeModuleOption,
            outputOption,
            contextTypeOption,
            assemblySearchPathOption,
        };

        rootCommand.SetAction(parseResult =>
        {
            string assemblyPath = parseResult.GetValue(assemblyArgument)!;
            string? runtimeModule =
                parseResult.GetValue(runtimeModuleOption);
            string? outputPath = parseResult.GetValue(outputOption);
            string? contextType = parseResult.GetValue(contextTypeOption);
            string[] searchPaths =
                parseResult.GetValue(assemblySearchPathOption) ?? [];
            if (string.IsNullOrWhiteSpace(runtimeModule))
            {
                stderr.WriteLine(
                    "ts-jsexport: --runtime-module requires a non-empty module specifier.");
                return 1;
            }

            if (contextType is not null)
            {
                if (string.IsNullOrWhiteSpace(contextType))
                {
                    stderr.WriteLine(
                        "ts-jsexport: --context requires a non-empty exact type name.");
                    return 1;
                }
                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    stderr.WriteLine(
                        "ts-jsexport: --output is required with --context.");
                    return 1;
                }
                if (searchPaths.Length == 0)
                {
                    stderr.WriteLine(
                        "ts-jsexport: at least one --assembly-search-path is "
                            + "required with --context.");
                    return 1;
                }
                if (Path.Exists(outputPath))
                {
                    stderr.WriteLine(
                        $"ts-jsexport: context output path already exists: "
                            + outputPath);
                    return 1;
                }
                if (!JsExportContextGenerator.TryGenerate(
                        assemblyPath,
                        contextType,
                        searchPaths,
                        runtimeModule,
                        "ts-jsexport",
                        stderr,
                        out ImmutableArray<GeneratedJsExportFacade> facades))
                {
                    return 1;
                }

                try
                {
                    PublishContextDirectory(outputPath, facades);
                    return 0;
                }
                catch (Exception ex) when (
                    ex is IOException or UnauthorizedAccessException
                        or ArgumentException or NotSupportedException)
                {
                    stderr.WriteLine(
                        $"ts-jsexport: could not write TypeScript facade set to "
                            + $"'{outputPath}': {ex.Message}");
                    return 1;
                }
            }
            if (searchPaths.Length > 0)
            {
                stderr.WriteLine(
                    "ts-jsexport: --assembly-search-path requires --context.");
                return 1;
            }

            if (!JsExportSurfaceLoader.TryLoad(
                    assemblyPath,
                    "ts-jsexport",
                    stderr,
                    out global::ILInspector.JsExportSurface.JsExportSurface?
                        surface))
            {
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

    static void PublishContextDirectory(
        string outputDirectory,
        ImmutableArray<GeneratedJsExportFacade> facades)
    {
        string fullPath = Path.GetFullPath(outputDirectory);
        if (Path.Exists(fullPath))
        {
            throw new IOException(
                $"Context output path already exists: {fullPath}");
        }

        Directory.CreateDirectory(fullPath);
        foreach (GeneratedJsExportFacade facade in facades)
        {
            File.WriteAllText(
                Path.Combine(fullPath, facade.Root.ArtifactName),
                facade.Source);
        }
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
