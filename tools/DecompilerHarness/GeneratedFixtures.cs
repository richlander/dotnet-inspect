using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ILInspector.Decompiler.Pipeline;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Addressable generated C# fixtures for progressive compile-back checks. The
/// catalogue names the source shape, expected target methods, and expected
/// compile-back outcome; the runner materializes those entries as a temporary
/// class library and grades them with <see cref="FidelityCheck.Evaluate(string)"/>.
/// </summary>
internal static class GeneratedFixtureCatalog
{
    public static readonly GeneratedFixtureDefinition MinimalPropertyLiteral = new(
        "minimal.property.literal",
        """
        namespace GeneratedFixtures.MinimalPropertyLiteral;

        public class Class1
        {
            public string Method1 => "Hello World";
        }
        """,
        [
            new("GeneratedFixtures.MinimalPropertyLiteral.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalPropertyLiteral.Class1", "get_Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "property", "literal"]);

    public static readonly GeneratedFixtureDefinition MinimalPrimaryCtorFieldInit = new(
        "minimal.primary-ctor.field-init",
        """
        namespace GeneratedFixtures.MinimalPrimaryCtorFieldInit;

        public class Class1(string message)
        {
            private readonly string _message = message;

            public string Method1 => _message;
        }
        """,
        [
            new(
                "GeneratedFixtures.MinimalPrimaryCtorFieldInit.Class1",
                ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalPrimaryCtorFieldInit.Class1", "get_Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "primary-constructor", "field-initializer"]);

    public static readonly GeneratedFixtureDefinition MinimalCtorFieldGetter = new(
        "minimal.ctor-field.getter",
        """
        namespace GeneratedFixtures.MinimalCtorFieldGetter;

        public class Class1
        {
            private readonly string _message;

            public Class1(string message)
            {
                _message = message;
            }

            public string Method1 => _message;
        }
        """,
        [
            new("GeneratedFixtures.MinimalCtorFieldGetter.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalCtorFieldGetter.Class1", "get_Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "constructor", "field", "property"]);

    public static readonly GeneratedFixtureDefinition MinimalAutoPropertyGetter = new(
        "minimal.auto-property.getter",
        """
        namespace GeneratedFixtures.MinimalAutoPropertyGetter;

        public class Class1
        {
            public Class1(string message)
            {
                Method1 = message;
            }

            public string Method1 { get; }
        }
        """,
        [
            new("GeneratedFixtures.MinimalAutoPropertyGetter.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalAutoPropertyGetter.Class1", "get_Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "constructor", "auto-property"]);

    public static readonly GeneratedFixtureDefinition MinimalMethodCallSameType = new(
        "minimal.method-call.same-type",
        """
        namespace GeneratedFixtures.MinimalMethodCallSameType;

        public class Class1
        {
            public string Method1() => Method2();

            private string Method2() => "Hello World";
        }
        """,
        [
            new("GeneratedFixtures.MinimalMethodCallSameType.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalMethodCallSameType.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalMethodCallSameType.Class1", "Method2",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "method-call"]);

    public static readonly GeneratedFixtureDefinition MinimalStaticMethodCall = new(
        "minimal.static-method-call",
        """
        namespace GeneratedFixtures.MinimalStaticMethodCall;

        public class Class1
        {
            public static string Method1() => Helper();

            private static string Helper() => "Hello World";
        }
        """,
        [
            new("GeneratedFixtures.MinimalStaticMethodCall.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalStaticMethodCall.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalStaticMethodCall.Class1", "Helper",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "static", "method-call"]);

    public static readonly GeneratedFixtureDefinition MinimalIfElse = new(
        "minimal.if-else",
        """
        namespace GeneratedFixtures.MinimalIfElse;

        public class Class1
        {
            public int Method1(int value)
            {
                if (value < 0)
                    return -1;
                return 1;
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalIfElse.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalIfElse.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "branch", "if-else"]);

    public static readonly GeneratedFixtureDefinition MinimalNullCoalesce = new(
        "minimal.null-coalesce",
        """
        namespace GeneratedFixtures.MinimalNullCoalesce;

        public class Class1
        {
            public string Method1(string value) => value ?? "fallback";
        }
        """,
        [
            new("GeneratedFixtures.MinimalNullCoalesce.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalNullCoalesce.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "null-coalesce"]);

    public static readonly GeneratedFixtureDefinition MinimalTryFinally = new(
        "minimal.try-finally",
        """
        namespace GeneratedFixtures.MinimalTryFinally;

        public class Class1
        {
            private int _count;

            public int Method1(int value)
            {
                try
                {
                    return value + 1;
                }
                finally
                {
                    _count++;
                }
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalTryFinally.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalTryFinally.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "try-finally", "lifetime"]);

    public static readonly GeneratedFixtureDefinition MinimalUsingDispose = new(
        "minimal.using-dispose",
        """
        namespace GeneratedFixtures.MinimalUsingDispose;

        public class Class1
        {
            public long Method1()
            {
                using var stream = new System.IO.MemoryStream();
                return stream.Length;
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalUsingDispose.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalUsingDispose.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "using", "dispose", "lifetime"]);

    public static readonly GeneratedFixtureDefinition MinimalForeachArray = new(
        "minimal.foreach-array",
        """
        namespace GeneratedFixtures.MinimalForeachArray;

        public class Class1
        {
            public int Method1(int[] values)
            {
                var sum = 0;
                foreach (var value in values)
                    sum += value;
                return sum;
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalForeachArray.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalForeachArray.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "foreach", "array", "loop"]);

    public static readonly GeneratedFixtureDefinition MinimalForLoop = new(
        "minimal.for-loop",
        """
        namespace GeneratedFixtures.MinimalForLoop;

        public class Class1
        {
            public int Method1(int count)
            {
                var sum = 0;
                for (var i = 0; i < count; i++)
                    sum += i;
                return sum;
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalForLoop.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalForLoop.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "for", "loop"]);

    public static readonly GeneratedFixtureDefinition MinimalWhileLoop = new(
        "minimal.while-loop",
        """
        namespace GeneratedFixtures.MinimalWhileLoop;

        public class Class1
        {
            public int Method1(int count)
            {
                var sum = 0;
                while (count > 0)
                {
                    sum += count;
                    count--;
                }
                return sum;
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalWhileLoop.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalWhileLoop.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "while", "loop"]);

    public static readonly GeneratedFixtureDefinition MinimalDoWhileLoop = new(
        "minimal.do-while",
        """
        namespace GeneratedFixtures.MinimalDoWhileLoop;

        public class Class1
        {
            public int Method1(int count)
            {
                var sum = 0;
                do
                {
                    sum += count;
                    count--;
                }
                while (count > 0);
                return sum;
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalDoWhileLoop.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalDoWhileLoop.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "do-while", "loop"]);

    public static readonly GeneratedFixtureDefinition MinimalSwitchInt = new(
        "minimal.switch-int",
        """
        namespace GeneratedFixtures.MinimalSwitchInt;

        public class Class1
        {
            public string Method1(int value)
            {
                switch (value)
                {
                    case 0:
                        return "zero";
                    case 1:
                        return "one";
                    case 2:
                        return "two";
                    case 3:
                        return "three";
                    default:
                        return "many";
                }
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalSwitchInt.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new("GeneratedFixtures.MinimalSwitchInt.Class1", "Method1",
                FidelityCheck.CompileBackStatus.Exact),
        ],
        ["minimal", "switch", "int", "branch"]);

    public static readonly GeneratedFixtureDefinition MinimalSwitchTwoCaseLowersIf = new(
        "minimal.switch-two-case-lowers-if",
        """
        namespace GeneratedFixtures.MinimalSwitchTwoCaseLowersIf;

        public class Class1
        {
            public string Method1(int value)
            {
                switch (value)
                {
                    case 0:
                        return "zero";
                    case 1:
                        return "one";
                    default:
                        return "many";
                }
            }
        }
        """,
        [
            new("GeneratedFixtures.MinimalSwitchTwoCaseLowersIf.Class1", ".ctor",
                FidelityCheck.CompileBackStatus.Exact),
            new(
                "GeneratedFixtures.MinimalSwitchTwoCaseLowersIf.Class1",
                "Method1",
                FidelityCheck.CompileBackStatus.OpcodeDiff,
                IsFrontier: true,
                Note: "Current SDK lowers this two-case source switch to if/else; dense minimal.switch-int is the stable switch-statement rung."),
        ],
        ["minimal", "switch", "int", "branch", "frontier", "compiler-lowering"]);

    public static IReadOnlyList<GeneratedFixtureDefinition> All { get; } =
    [
        MinimalPropertyLiteral,
        MinimalPrimaryCtorFieldInit,
        MinimalCtorFieldGetter,
        MinimalAutoPropertyGetter,
        MinimalMethodCallSameType,
        MinimalStaticMethodCall,
        MinimalIfElse,
        MinimalNullCoalesce,
        MinimalTryFinally,
        MinimalUsingDispose,
        MinimalForeachArray,
        MinimalForLoop,
        MinimalWhileLoop,
        MinimalDoWhileLoop,
        MinimalSwitchInt,
    ];

    public static IReadOnlyList<GeneratedFixtureDefinition> Frontiers { get; } =
    [
        MinimalSwitchTwoCaseLowersIf,
    ];

    public static IReadOnlyList<GeneratedFixtureDefinition> Catalog { get; } =
    [
        .. All,
        .. Frontiers,
    ];

    public static IReadOnlyList<GeneratedFixtureDefinition> MinimalCompileBackRungs => All;

    public static IReadOnlyList<GeneratedFixtureDefinition> Select(string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
            return All;

        return Catalog
            .Where(fixture => fixture.Id.Equals(selector, StringComparison.Ordinal)
                || fixture.Id.StartsWith(selector, StringComparison.Ordinal))
            .ToArray();
    }
}

internal sealed record GeneratedFixtureDefinition(
    string Id,
    string Source,
    IReadOnlyList<GeneratedFixtureTarget> Targets,
    IReadOnlyList<string> Tags);

internal sealed record GeneratedFixtureTarget(
    string Type,
    string Method,
    FidelityCheck.CompileBackStatus ExpectedStatus,
    int Overload = 0,
    bool IsFrontier = false,
    string? Note = null)
{
    public string DisplayMember => $"{Type}::{Method}#{Overload}";
}

internal sealed record GeneratedFixtureRunOptions(
    string? TargetFramework = null,
    bool KeepArtifacts = false)
{
    public static GeneratedFixtureRunOptions Default { get; } = new();
}

internal sealed record GeneratedFixtureRunResult(
    string ProjectDirectory,
    string AssemblyPath,
    IReadOnlyList<GeneratedFixtureResult> Results)
{
    public bool Passed => Results.All(result => result.Passed);
}

internal sealed record GeneratedFixtureResult(
    string FixtureId,
    string Type,
    string Method,
    int Overload,
    string DecompilerFidelity,
    FidelityCheck.CompileBackStatus? ActualStatus,
    FidelityCheck.CompileBackStatus ExpectedStatus,
    bool IsFrontier,
    string? Detail,
    string? Note)
{
    public bool Passed => ActualStatus == ExpectedStatus;
    public string DisplayMember => $"{Type}::{Method}#{Overload}";
}

internal static class GeneratedFixtureRunner
{
    static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static GeneratedFixtureRunResult Run(
        IReadOnlyList<GeneratedFixtureDefinition> fixtures,
        GeneratedFixtureRunOptions? options = null)
    {
        if (fixtures.Count == 0)
            throw new ArgumentException("At least one generated fixture is required.", nameof(fixtures));

        options ??= GeneratedFixtureRunOptions.Default;
        var root = Path.Combine(Path.GetTempPath(), "dotnet-inspect-generated-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string projectPath = Path.Combine(root, "GeneratedDecompilerFixtures.csproj");
            File.WriteAllText(projectPath, ProjectFile(options.TargetFramework ?? CurrentTargetFramework()));

            for (int i = 0; i < fixtures.Count; i++)
            {
                string sourcePath = Path.Combine(root, $"{i:000}_{SafeFileName(fixtures[i].Id)}.cs");
                File.WriteAllText(sourcePath, fixtures[i].Source);
            }

            Build(root);
            string assemblyPath = Path.Combine(root, "bin", "Release",
                options.TargetFramework ?? CurrentTargetFramework(), "GeneratedDecompilerFixtures.dll");
            if (!File.Exists(assemblyPath))
                throw new InvalidOperationException($"Generated fixture assembly was not produced: {assemblyPath}");

            var compileBack = FidelityCheck.Evaluate(assemblyPath)
                .ToDictionary(result => Key(result.Type, result.Method, result.Overload), StringComparer.Ordinal);
            var decompilerFidelity = DecompilerFidelity(assemblyPath, fixtures);
            var results = new List<GeneratedFixtureResult>();
            foreach (var fixture in fixtures)
            {
                foreach (var target in fixture.Targets)
                {
                    compileBack.TryGetValue(Key(target.Type, target.Method, target.Overload), out var actual);
                    results.Add(new GeneratedFixtureResult(
                        fixture.Id,
                        target.Type,
                        target.Method,
                        target.Overload,
                        decompilerFidelity.GetValueOrDefault(Key(target.Type, target.Method, target.Overload), "Unknown"),
                        actual?.Status,
                        target.ExpectedStatus,
                        target.IsFrontier,
                        actual?.Detail ?? (actual is null ? "target-method-not-found" : null),
                        target.Note));
                }
            }

            return new GeneratedFixtureRunResult(root, assemblyPath, results);
        }
        finally
        {
            if (!options.KeepArtifacts)
                TryDelete(root);
        }
    }

    public static string FormatReport(GeneratedFixtureRunResult run)
    {
        var sb = new StringBuilder();
        int fixtureCount = run.Results.Select(r => r.FixtureId).Distinct(StringComparer.Ordinal).Count();
        sb.AppendLine(
            $"GENERATED FIXTURE COMPILE-BACK over {fixtureCount} fixture(s), {run.Results.Count} target method(s)");
        foreach (var result in run.Results.OrderBy(r => r.FixtureId, StringComparer.Ordinal).ThenBy(r => r.DisplayMember, StringComparer.Ordinal))
        {
            string actual = result.ActualStatus?.ToString() ?? "Missing";
            string frontier = result.IsFrontier ? " frontier" : "";
            string status = result.Passed ? "PASS" : "FAIL";
            sb.AppendLine(
                $"  {status}{frontier}  {result.FixtureId}  {result.DisplayMember}  " +
                $"decompiler={result.DecompilerFidelity}  compile-back={actual}  expected={result.ExpectedStatus}");
            if (!string.IsNullOrWhiteSpace(result.Detail))
                sb.AppendLine($"      detail: {result.Detail}");
            if (!string.IsNullOrWhiteSpace(result.Note))
                sb.AppendLine($"      note: {result.Note}");
        }
        return sb.ToString();
    }

    public static string FormatJson(GeneratedFixtureRunResult run)
    {
        var payload = new
        {
            ProjectDirectory = Directory.Exists(run.ProjectDirectory) ? run.ProjectDirectory : null,
            AssemblyPath = File.Exists(run.AssemblyPath) ? run.AssemblyPath : null,
            run.Results,
            run.Passed,
        };
        return JsonSerializer.Serialize(payload, s_jsonOptions);
    }

    public static string FormatList(IReadOnlyList<GeneratedFixtureDefinition> fixtures)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"GENERATED FIXTURE CATALOG ({fixtures.Count} fixture(s))");
        foreach (var fixture in fixtures.OrderBy(fixture => fixture.Id, StringComparer.Ordinal))
        {
            sb.AppendLine($"  {fixture.Id}  [{string.Join(", ", fixture.Tags)}]");
            foreach (var target in fixture.Targets)
            {
                string frontier = target.IsFrontier ? " frontier" : "";
                sb.AppendLine(
                    $"      {target.DisplayMember}  expected={target.ExpectedStatus}{frontier}");
            }
        }
        return sb.ToString();
    }

    public static string FormatListJson(IReadOnlyList<GeneratedFixtureDefinition> fixtures)
    {
        var items = fixtures
            .OrderBy(fixture => fixture.Id, StringComparer.Ordinal)
            .Select(fixture => new
            {
                fixture.Id,
                fixture.Tags,
                Targets = fixture.Targets.Select(target => new
                {
                    target.Type,
                    target.Method,
                    target.Overload,
                    ExpectedStatus = target.ExpectedStatus.ToString(),
                    target.IsFrontier,
                    target.Note,
                }),
            });
        return JsonSerializer.Serialize(items, s_jsonOptions);
    }

    static string Key(string type, string method, int overload) => $"{type}::{method}#{overload}";

    static string ProjectFile(string targetFramework) =>
        $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>{{targetFramework}}</TargetFramework>
            <ImplicitUsings>disable</ImplicitUsings>
            <Nullable>disable</Nullable>
            <LangVersion>preview</LangVersion>
            <IsPackable>false</IsPackable>
            <IsAotCompatible>false</IsAotCompatible>
            <AssemblyName>GeneratedDecompilerFixtures</AssemblyName>
          </PropertyGroup>
        </Project>
        """;

    static string CurrentTargetFramework()
    {
        var frameworkName = typeof(GeneratedFixtureRunner).Assembly
            .GetCustomAttributes(typeof(TargetFrameworkAttribute), inherit: false)
            .OfType<TargetFrameworkAttribute>()
            .FirstOrDefault()
            ?.FrameworkName;
        if (frameworkName is null)
            return "net11.0";

        const string prefix = ".NETCoreApp,Version=v";
        return frameworkName.StartsWith(prefix, StringComparison.Ordinal)
            ? "net" + frameworkName[prefix.Length..]
            : "net11.0";
    }

    static string SafeFileName(string id)
    {
        var chars = id.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        return new string(chars);
    }

    static Dictionary<string, string> DecompilerFidelity(
        string assemblyPath,
        IReadOnlyList<GeneratedFixtureDefinition> fixtures)
    {
        var fidelities = new Dictionary<string, string>(StringComparer.Ordinal);
        using var source = MetadataSource.Open(assemblyPath);
        foreach (var target in fixtures.SelectMany(fixture => fixture.Targets))
        {
            var function = IrImporter.Import(source, target.Type, target.Method, target.Overload);
            if (function is null)
                continue;

            _ = CSharpPrinter.PrintRaised(function, method => IrImporter.Import(source, method));
            fidelities[Key(target.Type, target.Method, target.Overload)] = function.Fidelity.ToString();
        }

        return fidelities;
    }

    static void Build(string workingDirectory)
    {
        using var process = new Process();
        process.StartInfo.FileName = "dotnet";
        process.StartInfo.WorkingDirectory = workingDirectory;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.ArgumentList.Add("build");
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add("Release");
        process.StartInfo.ArgumentList.Add("--nologo");
        process.StartInfo.ArgumentList.Add("--verbosity");
        process.StartInfo.ArgumentList.Add("quiet");
        if (!process.Start())
            throw new InvalidOperationException("Could not start dotnet build for generated fixtures.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(milliseconds: 120_000))
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            throw new TimeoutException("Generated fixture build timed out.");
        }

        string stdout = stdoutTask.GetAwaiter().GetResult();
        string stderr = stderrTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Generated fixture build failed with exit code {process.ExitCode}.\n{stdout}\n{stderr}");
    }

    static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
