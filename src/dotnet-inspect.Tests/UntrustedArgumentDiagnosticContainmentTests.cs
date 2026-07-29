using System.Diagnostics;
using System.Reflection;

namespace DotnetInspector.Tests;

/// <summary>
/// Gate for the diagnostics the CLI composes out of the user's own command
/// line (issue #3319).
/// </summary>
/// <remarks>
/// Argv looks like trusted input and is not. An agent builds these option
/// values out of names it just read from a package or an assembly, so a type
/// name carrying <c>U+202E</c> or an ANSI escape arrives as <c>-S</c>,
/// <c>--where</c>, or a bare argument and is quoted straight back. The line
/// that results is indistinguishable from a genuine diagnostic, which is the
/// whole attack.
///
/// A hostile-argv sweep over these options found every case below leaking, and
/// two of them show why the earlier gates missed the family:
///
/// <list type="bullet">
/// <item><description>
/// <c>SelectOutput</c> wrote <c>$"{prefix}: Select value '{value}' not
/// found."</c>, choosing <c>"Error"</c> or <c>"Warning"</c> at the call site.
/// It never spells the literal <c>Error:</c>, so it escaped both
/// <c>CommandError</c> and the source scan in
/// <see cref="CommandErrorOwnershipTests"/>. Severity now belongs to the
/// writer.
/// </description></item>
/// <item><description>
/// Parse errors are formatted in <c>Program.cs</c>, above the root command, so
/// no in-process gate that invokes the parsed command can reach them. These
/// cases therefore run the real executable.
/// </description></item>
/// </list>
/// </remarks>
public class UntrustedArgumentDiagnosticContainmentTests
{
    private const string Bidi = "\u202E";
    private const string VerticalTab = "\u000B";
    private const string Escape = "\u001B";
    private const string LineSeparator = "\u2028";
    private const string ParagraphSeparator = "\u2029";

    public static TheoryData<string, string[]> HostileArgumentChannels()
    {
        var data = new TheoryData<string, string[]>();
        string library = ProductAssemblyPath();

        foreach (string hazard in new[] { Bidi, VerticalTab, Escape, LineSeparator, ParagraphSeparator })
        {
            string hostile = $"HOSTILE{hazard}INJECTEDARG";

            // Parse-time failures, formatted above the root command.
            data.Add("parse-integer", ["type", "Object", "-n", hostile]);
            data.Add("parse-unrecognized", ["library", library, "--rows", hostile]);
            data.Add("parse-row", ["library", library, "--row", hostile]);

            // Command-time failures that quote the offending argument.
            data.Add("select-miss", ["library", library, "-S", hostile]);
            data.Add("il-offset", ["library", library, "--il-offset", hostile]);
            data.Add("order-by", ["library", library, "--order-by", hostile]);
            data.Add("where", ["library", library, "--where", hostile]);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(HostileArgumentChannels))]
    public void HostileArgument_IsContainedInDiagnostics(string channel, string[] args)
    {
        var (output, error) = RunCli(args);
        string combined = output + error;

        // Non-vacuity: the diagnostic must actually have quoted the argument.
        // Without this a command that silently succeeded, or failed for an
        // unrelated reason, would pass the hazard scan below having proved
        // nothing about this channel.
        HostileOutputAssert.MarkersRendered(combined, channel, "INJECTEDARG");
        HostileOutputAssert.NoRenderingHazard(combined, channel);
        HostileOutputAssert.NoLineSplit(combined, "INJECTEDARG");
    }

    private static (string Output, string Error) RunCli(string[] args)
    {
        string executable = Path.Combine(
            Path.GetDirectoryName(ProductAssemblyPath())!,
            OperatingSystem.IsWindows() ? "dotnet-inspect.exe" : "dotnet-inspect");
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        // Out-of-process, so the offline switch has to travel as environment
        // rather than as the process-wide static the in-process helper sets.
        psi.Environment["DOTNET_INSPECT_OFFLINE"] = "1";

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not start {executable}.");

        // Drain both pipes before waiting; a synchronous read of one blocks
        // until EOF and lets the child deadlock filling the other.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException($"{executable} did not exit.");
        }

        Task.WaitAll([stdout, stderr], 10_000);
        return (stdout.Result, stderr.Result);
    }

    private static string ProductAssemblyPath()
    {
        // Deliberately the product copy in *this* test project's output, not
        // the one under artifacts/bin/dotnet-inspect. Anyone checking that
        // these cases are non-vacuous by breaking a product line must rebuild
        // the test project, not just the product: building the product alone
        // leaves this copy stale and the tamper appears to change nothing.
        string path = Path.Combine(AppContext.BaseDirectory, "dotnet-inspect.dll");
        if (File.Exists(path))
        {
            return path;
        }

        // The test assembly copies the product next to itself; fall back to the
        // loaded location so a layout change fails loudly here rather than
        // turning every case above into a vacuous pass.
        var located = Assembly.Load("dotnet-inspect").Location;
        return string.IsNullOrEmpty(located)
            ? throw new FileNotFoundException("Could not locate the dotnet-inspect product assembly.")
            : located;
    }
}
