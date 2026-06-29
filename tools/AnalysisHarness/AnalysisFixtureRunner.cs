using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ILInspector.Analysis;

namespace ILInspector.AnalysisHarness;

/// <summary>
/// Materializes analysis fixtures into temporary assemblies and grades them with the real
/// analyzer (<see cref="LibraryBodyIndex"/>). Each fixture builds in isolation: a consumer
/// assembly (the inspected one) plus, when the fixture is cross-assembly, a referenced external
/// assembly. Isolation keeps same-fully-qualified-name collision fixtures and extern aliases
/// from clashing across catalogue entries.
/// </summary>
public static class AnalysisFixtureRunner
{
    static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AnalysisFixtureRunResult Run(
        IReadOnlyList<AnalysisFixtureDefinition> fixtures,
        AnalysisFixtureRunOptions? options = null)
    {
        if (fixtures.Count == 0)
            throw new ArgumentException("At least one analysis fixture is required.", nameof(fixtures));

        options ??= AnalysisFixtureRunOptions.Default;
        string tfm = options.TargetFramework ?? CurrentTargetFramework();
        var root = Path.Combine(Path.GetTempPath(), "dotnet-inspect-analysis-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var results = new List<AnalysisFixtureResult>();
            foreach (var fixture in fixtures)
                results.AddRange(RunFixture(fixture, Path.Combine(root, SafeFileName(fixture.Id)), tfm));
            return new AnalysisFixtureRunResult(root, results);
        }
        finally
        {
            if (!options.KeepArtifacts)
                TryDelete(root);
        }
    }

    static IEnumerable<AnalysisFixtureResult> RunFixture(AnalysisFixtureDefinition fixture, string dir, string tfm)
    {
        Directory.CreateDirectory(dir);
        bool hasExternal = fixture.ExternalSource is not null;

        if (hasExternal)
        {
            string externalDir = Path.Combine(dir, "External");
            Directory.CreateDirectory(externalDir);
            File.WriteAllText(Path.Combine(externalDir, "External.csproj"), LibraryProjectFile(tfm, "External"));
            File.WriteAllText(Path.Combine(externalDir, "External.cs"), fixture.ExternalSource!);
        }

        string consumerDir = Path.Combine(dir, "Consumer");
        Directory.CreateDirectory(consumerDir);
        File.WriteAllText(
            Path.Combine(consumerDir, "Consumer.csproj"),
            ConsumerProjectFile(tfm, hasExternal, fixture.ExternalNeedsAlias, fixture.ConsumerFrameworkReferences));
        File.WriteAllText(Path.Combine(consumerDir, "Consumer.cs"), fixture.ConsumerSource);

        Build(consumerDir);
        string consumerDll = Path.Combine(consumerDir, "bin", "Release", tfm, "Consumer.dll");
        if (!File.Exists(consumerDll))
            throw new InvalidOperationException($"Fixture '{fixture.Id}' did not produce {consumerDll}.");

        var index = LibraryBodyIndex.Open(consumerDll);
        var signalsByToken = index.GetMethodSignals();

        foreach (var target in fixture.Targets)
            yield return Grade(fixture, target, index, signalsByToken);
    }

    static AnalysisFixtureResult Grade(
        AnalysisFixtureDefinition fixture,
        AnalysisFixtureTarget target,
        LibraryBodyIndex index,
        IReadOnlyDictionary<int, MethodSignals> signalsByToken)
    {
        var matches = index.Methods.Where(m => m.Name == target.Method).ToList();
        if (matches.Count != 1)
        {
            // Grade only an unambiguous target: 0 matches is a missing method, >1 is an
            // ambiguous name (an overload or a same-named method on another type) that could
            // otherwise silently grade the wrong method's signals.
            string why = matches.Count == 0
                ? "target-method-not-found"
                : $"target-method-ambiguous ({matches.Count} methods named '{target.Method}')";
            return new AnalysisFixtureResult(
                fixture.Id, target.Method, 0, [], [], false, target.Boundary, target.BlockedOn,
                Describe(target.Expect), why, why, target.Note);
        }

        var method = matches[0];
        var signals = signalsByToken.GetValueOrDefault(method.MetadataToken, MethodSignals.None);
        var exceptionTypes = signals.ExceptionTypes;
        var opportunityShapes = index.OptimizationOpportunities
            .Where(o => o.Method.MetadataToken == method.MetadataToken)
            .Select(o => o.Shape)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(shape => shape, StringComparer.Ordinal)
            .ToArray();

        string? failure = Evaluate(target.Expect, signals, exceptionTypes, opportunityShapes);
        string actual =
            $"allocations={signals.Allocations}, exceptions=[{string.Join(",", exceptionTypes)}], opportunities=[{string.Join(",", opportunityShapes)}]";

        return new AnalysisFixtureResult(
            fixture.Id,
            target.Method,
            signals.Allocations,
            exceptionTypes,
            opportunityShapes,
            failure is null,
            target.Boundary,
            target.BlockedOn,
            Describe(target.Expect),
            actual,
            failure,
            target.Note);
    }

    static string? Evaluate(
        AnalysisExpectation expect,
        MethodSignals signals,
        IReadOnlyList<string> exceptionTypes,
        IReadOnlyList<string> opportunityShapes)
    {
        if (expect.AllocationsExactly is { } exact && signals.Allocations != exact)
            return $"expected allocations={exact} but was {signals.Allocations}";
        if (expect.AllocationsAtLeast is { } atLeast && signals.Allocations < atLeast)
            return $"expected allocations>={atLeast} but was {signals.Allocations}";
        if (expect.ExceptionTypePresent is { } present && !exceptionTypes.Contains(present))
            return $"expected exception type '{present}' present but exceptions were [{string.Join(",", exceptionTypes)}]";
        if (expect.ExceptionTypeAbsent is { } absent && exceptionTypes.Contains(absent))
            return $"expected exception type '{absent}' absent but it was present";
        if (expect.OpportunityShapePresent is { } shape && !opportunityShapes.Contains(shape))
            return $"expected opportunity shape '{shape}' but shapes were [{string.Join(",", opportunityShapes)}]";
        if (expect.OpportunityShapeAbsent is { } absentShape && opportunityShapes.Contains(absentShape))
            return $"expected opportunity shape '{absentShape}' absent but it was present";
        return null;
    }

    static string Describe(AnalysisExpectation expect)
    {
        var parts = new List<string>(3);
        if (expect.AllocationsExactly is { } e) parts.Add($"allocations={e}");
        if (expect.AllocationsAtLeast is { } a) parts.Add($"allocations>={a}");
        if (expect.ExceptionTypePresent is { } p) parts.Add($"exception+'{p}'");
        if (expect.ExceptionTypeAbsent is { } ab) parts.Add($"exception-'{ab}'");
        if (expect.OpportunityShapePresent is { } s) parts.Add($"opportunity+'{s}'");
        if (expect.OpportunityShapeAbsent is { } sa) parts.Add($"opportunity-'{sa}'");
        return parts.Count == 0 ? "(no expectation)" : string.Join(", ", parts);
    }

    public static string FormatReport(AnalysisFixtureRunResult run)
    {
        var sb = new StringBuilder();
        int fixtureCount = run.Results.Select(r => r.FixtureId).Distinct(StringComparer.Ordinal).Count();
        sb.AppendLine($"ANALYSIS FIXTURE LEDGER over {fixtureCount} fixture(s), {run.Results.Count} target method(s)");
        foreach (var result in run.Results
            .OrderBy(r => r.FixtureId, StringComparer.Ordinal)
            .ThenBy(r => r.Method, StringComparer.Ordinal))
        {
            string status = result.Passed ? "PASS" : "FAIL";
            string boundary = BoundaryTag(result.Boundary, result.BlockedOn);
            sb.AppendLine($"  {status}{boundary}  {result.FixtureId}  {result.Method}");
            sb.AppendLine($"      expected: {result.Expected}");
            sb.AppendLine($"      actual:   {result.Actual}");
            if (result.Failure is not null)
                sb.AppendLine($"      failure:  {result.Failure}");
            if (!string.IsNullOrWhiteSpace(result.Note))
                sb.AppendLine($"      note:     {result.Note}");
        }
        return sb.ToString();
    }

    public static string FormatJson(AnalysisFixtureRunResult run)
        => JsonSerializer.Serialize(new { run.Results, run.Passed }, s_jsonOptions);

    public static string FormatList(IReadOnlyList<AnalysisFixtureDefinition> fixtures)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"ANALYSIS FIXTURE CATALOG ({fixtures.Count} fixture(s))");
        foreach (var fixture in fixtures.OrderBy(fixture => fixture.Id, StringComparer.Ordinal))
        {
            sb.AppendLine($"  {fixture.Id}  [{string.Join(", ", fixture.Tags)}]");
            foreach (var target in fixture.Targets)
            {
                string boundary = BoundaryTag(target.Boundary, target.BlockedOn);
                sb.AppendLine($"      {target.Method}  expected={Describe(target.Expect)}{boundary}");
            }
        }
        return sb.ToString();
    }

    static string BoundaryTag(OwnedBoundary boundary, string? blockedOn) => boundary switch
    {
        OwnedBoundary.FalsePositive => " [owned-FP]",
        OwnedBoundary.FalseNegative => blockedOn is null ? " [owned-FN]" : $" [owned-FN blocked-on {blockedOn}]",
        _ => "",
    };

    public static string FormatListJson(IReadOnlyList<AnalysisFixtureDefinition> fixtures)
    {
        var items = fixtures
            .OrderBy(fixture => fixture.Id, StringComparer.Ordinal)
            .Select(fixture => new
            {
                fixture.Id,
                fixture.Tags,
                Targets = fixture.Targets.Select(target => new
                {
                    target.Method,
                    Expected = Describe(target.Expect),
                    Boundary = target.Boundary.ToString(),
                    target.BlockedOn,
                    target.Note,
                }),
            });
        return JsonSerializer.Serialize(items, s_jsonOptions);
    }

    static string LibraryProjectFile(string tfm, string assemblyName) =>
        $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>{{tfm}}</TargetFramework>
            <ImplicitUsings>disable</ImplicitUsings>
            <Nullable>disable</Nullable>
            <LangVersion>preview</LangVersion>
            <IsPackable>false</IsPackable>
            <IsAotCompatible>false</IsAotCompatible>
            <AssemblyName>{{assemblyName}}</AssemblyName>
          </PropertyGroup>
        </Project>
        """;

    static string ConsumerProjectFile(string tfm, bool hasExternal, bool needsAlias, IReadOnlyList<string> frameworkReferences)
    {
        string reference = hasExternal
            ? needsAlias
                ? $"""
                  <ItemGroup>
                    <ProjectReference Include="..\External\External.csproj">
                      <Aliases>{AnalysisFixtureCatalog.ExternalAlias}</Aliases>
                    </ProjectReference>
                  </ItemGroup>
                """
                : """
                  <ItemGroup>
                    <ProjectReference Include="..\External\External.csproj" />
                  </ItemGroup>
                """
            : "";
        string frameworks = frameworkReferences.Count == 0
            ? ""
            : "  <ItemGroup>\n"
              + string.Concat(frameworkReferences.Select(framework => $"    <FrameworkReference Include=\"{framework}\" />\n"))
              + "  </ItemGroup>\n";
        return $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{{tfm}}</TargetFramework>
                <ImplicitUsings>disable</ImplicitUsings>
                <Nullable>disable</Nullable>
                <LangVersion>preview</LangVersion>
                <IsPackable>false</IsPackable>
                <IsAotCompatible>false</IsAotCompatible>
                <AssemblyName>Consumer</AssemblyName>
              </PropertyGroup>
            {{reference}}
            {{frameworks}}
            </Project>
            """;
    }

    static string CurrentTargetFramework()
    {
        var frameworkName = typeof(AnalysisFixtureRunner).Assembly
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
        => new([.. id.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')]);

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
            throw new InvalidOperationException("Could not start dotnet build for analysis fixtures.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(milliseconds: 120_000))
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            throw new TimeoutException("Analysis fixture build timed out.");
        }

        string stdout = stdoutTask.GetAwaiter().GetResult();
        string stderr = stderrTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Analysis fixture build failed with exit code {process.ExitCode}.\n{stdout}\n{stderr}");
    }

    static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
