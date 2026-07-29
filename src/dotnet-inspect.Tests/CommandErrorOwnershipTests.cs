using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ILInspector.Instructions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotnetInspector.Tests;

/// <summary>
/// Pins <see cref="DotnetInspector.Output.CommandError"/> as the sole writer of
/// the CLI's severity-prefixed stderr lines (<c>Error:</c>, <c>Warning:</c>,
/// <c>Note:</c>).
/// </summary>
/// <remarks>
/// This is the gate for the containment property that
/// <c>UntrustedErrorChannelContainmentTests</c> demonstrates end to end. That
/// test proves one <c>Error:</c> line is contained; it cannot prove the next
/// call site someone adds will be, and there are ~200 of them. The rule this
/// issue keeps rediscovering is that a containment obligation restated at every
/// call site disagrees with itself as soon as one more is added, so the
/// enforcement has to be structural rather than per-site.
///
/// The check is a source scan because that is where the property lives: it is a
/// statement about which code may spell the prefix, not about any one runtime
/// value. A message composed from nothing but literals is still in scope --
/// exempting it would put the author back in the business of judging, per site,
/// whether an interpolated fragment is trusted, which is the judgement that has
/// been wrong repeatedly here.
///
/// The scan reads whole file text rather than lines. Its first version matched
/// per line and so was blind to the wrapped form, where
/// <c>Console.Error.WriteLine(</c> and the string sit on different lines --
/// fourteen real call sites in this repository. A gate that a reformatter can
/// switch off is not a gate, and it reads green while doing it.
/// </remarks>
public class CommandErrorOwnershipTests
{
    /// <summary>
    /// Matches a severity-prefixed string literal anywhere outside the owner --
    /// no receiver, no method name, no call shape.
    /// </summary>
    /// <remarks>
    /// Every narrower spelling of this rule was defeated by the next call site.
    /// Naming <c>Console.Error</c> missed three helpers that took a
    /// <c>TextWriter error</c> parameter every caller filled with
    /// <c>Console.Error</c>; one listed undecodable signatures from the
    /// inspected assembly straight to stderr. Naming a write method then missed
    /// forty sites that spelled <c>logger.Log($"Warning: ...")</c>, where the
    /// prefix travels as an argument to something that is not a writer at all
    /// and the untrusted path or exception text reached stderr raw under
    /// <c>--verbose</c>.
    ///
    /// So the rule is now the smallest one that covers all of them: outside
    /// <c>CommandError</c>, no source line may spell a severity prefix. That is
    /// checkable without understanding the call, and it cannot be sidestepped
    /// by choosing a different sink. It is also newline-immune by construction
    /// -- the literal lives on one line however the call is wrapped -- which the
    /// receiver-based version was not.
    ///
    /// The match is case-insensitive because one site wrote a lowercase
    /// <c>"warning: "</c>, which is the same forgeable line to a reader and was
    /// invisible to a case-sensitive scan.
    /// </remarks>
    private static readonly Regex SeverityLiteral =
        new(@"""(Error|Warning|Note): ", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Catches a diagnostic whose severity is interpolated rather than spelled,
    /// such as <c>$"{prefix}: Select value '{value}' not found."</c>.
    /// </summary>
    /// <remarks>
    /// This is not a hypothetical bypass. <c>SelectOutput</c> chose
    /// <c>"Error"</c> or <c>"Warning"</c> into a local and interpolated it, so
    /// it emitted a real <c>Error:</c> line that neither
    /// <see cref="ErrorWrite"/> nor <c>CommandError</c> ever saw, and the
    /// argument it quoted reached stderr uncontained. Severity now belongs to
    /// the writer, and a call site that reaches for the old shape fails here.
    /// </remarks>
    /// <summary>
    /// Matches stderr reaching code as anything other than a direct write --
    /// aliased into a local, passed as an argument, or taken as a raw handle.
    /// </summary>
    /// <remarks>
    /// The first version matched only the argument position,
    /// <c>[(,]\s*Console\s*\.\s*Error\s*[,)]</c>, and a reviewer defeated it in
    /// one line: <c>var sink = Console.Error;</c> followed by
    /// <c>Serialize(view, sink, ...)</c> added a fifth uncontained sink and the
    /// test stayed green, because the count it asserts never moved.
    /// <c>writer: Console.Error</c>, <c>Console.OpenStandardError()</c> and
    /// <c>Console.SetError</c> were invisible the same way.
    ///
    /// So the rule is the complement of the one next to it: every mention of
    /// the stream that is not <c>Console.Error.Write</c> hands it to something
    /// else, and that is precisely the shape
    /// <see cref="CommandError_IsTheOnlyWriterOfStderr"/> cannot judge.
    /// </remarks>
    private static readonly Regex StderrSink =
        new(@"Console\s*\.\s*(Error\b(?!\s*\.\s*\w+\s*\()|OpenStandardError|SetError)", RegexOptions.Compiled);

    /// <summary>
    /// A call on the stderr stream -- any member, not a named few.
    /// </summary>
    /// <remarks>
    /// Spelling the two method names was the same mistake this file keeps
    /// making at a smaller scale: a reviewer wrote
    /// <c>Console.Error.WriteAsync(value).GetAwaiter().GetResult()</c> and
    /// every test here stayed green, because the rule matched
    /// <c>Write</c> and <c>WriteLine</c> and <c>TextWriter</c> has neither of
    /// those exclusively. <c>WriteLineAsync</c>, <c>Flush</c>, and
    /// <c>Write(char[])</c> were open the same way. The member name is not the
    /// property; reaching the stream is.
    /// </remarks>
    private static readonly Regex StderrWrite =
        new(@"Console\s*\.\s*Error\s*\.\s*\w+\s*\(", RegexOptions.Compiled);

    private static readonly Regex ComposedPrefixWrite =
        new(@"\.\s*(WriteLine|Write)\s*\(\s*\$@?""\{[^}""]+\}\s*:\s", RegexOptions.Compiled);

    /// <summary>
    /// The values MSBuild evaluates <paramref name="item"/> to for
    /// <paramref name="projectPath"/>, in <paramref name="configuration"/>.
    /// </summary>
    /// <remarks>
    /// The alternative -- and what every earlier version of this class did --
    /// is to read the <c>.csproj</c> as XML and re-derive what it means. That
    /// answers "what does this project file say", which is not the question:
    /// the question is what the compiler was handed. A reviewer separated the
    /// two by declaring
    /// <c>&lt;Compile Include="$(MSBuildThisFileDirectory)eng\Diagnostics.cs"/&gt;</c>
    /// in <c>Directory.Build.targets</c>. The file compiles into the CLI, sits
    /// outside every project directory so the glob misses it, and is named in
    /// no <c>.csproj</c>, so the XML scan misses it too. Imports, implicit
    /// build files above the clone, and a package's <c>buildTransitive</c>
    /// targets all reach the compilation the same way, and none of them is a
    /// file this repository can enumerate.
    ///
    /// MSBuild already answers this exactly, so it is asked. That is the same
    /// move as reading the generated <c>GlobalUsings.g.cs</c> instead of the
    /// <c>&lt;Using&gt;</c> elements that produce it, applied to the other two
    /// item types this class depends on.
    ///
    /// A failed evaluation throws. Falling back to the XML reading would be a
    /// harness compensating for an unavailable observation, and it is exactly
    /// when evaluation stops working that the difference matters.
    /// </remarks>
    /// <summary>
    /// The item types this class asks for. Requested together because the cost
    /// is the process, not the question.
    /// </summary>
    private static readonly string[] Items = ["Compile", "ProjectReference", "Using"];

    private static IReadOnlyList<Dictionary<string, string>> EvaluatedItems(
        string projectPath,
        string item,
        BuildFlavor flavor) =>
        Evaluations.GetOrAdd((projectPath, flavor), static key => Evaluate(key.Project, key.Flavor))
            .GetValueOrDefault(item, []);

    private static readonly ConcurrentDictionary<(string Project, BuildFlavor Flavor),
        Dictionary<string, IReadOnlyList<Dictionary<string, string>>>> Evaluations = new();

    private static Dictionary<string, IReadOnlyList<Dictionary<string, string>>> Evaluate(
        string projectPath,
        BuildFlavor flavor)
    {
        string item = string.Join(',', Items);

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                ArgumentList = { "msbuild", projectPath, $"-getItem:{item}" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };

        foreach (string property in flavor.Properties)
        {
            process.StartInfo.ArgumentList.Add($"-p:{property}");
        }

        // Items a target adds do not exist at evaluation time. A reviewer put
        // <Compile Include="eng/InjectedStderr.cs"/> inside a target with
        // BeforeTargets="CoreCompile": the file compiles into the CLI and the
        // evaluated item list does not mention it. Asking for the items after
        // the target has run does, and costs nothing on an up-to-date build.
        if (flavor.AfterTargets)
        {
            process.StartInfo.ArgumentList.Add("-t:CoreCompile");
        }

        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Could not evaluate {item} for {projectPath} ({flavor.Name}). This class reads the set of files "
                + $"the compiler was handed rather than deriving it from project XML, so an evaluation it cannot "
                + $"run is an observation it does not have.{Environment.NewLine}{output}{Environment.NewLine}{error}");
        }

        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement items = document.RootElement.GetProperty("Items");
        Dictionary<string, IReadOnlyList<Dictionary<string, string>>> result = new(StringComparer.Ordinal);

        foreach (string name in Items)
        {
            result[name] = items.TryGetProperty(name, out JsonElement values)
                ?
                [
                    .. values.EnumerateArray().Select(v => v.EnumerateObject()
                        .ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.Ordinal))
                ]
                : [];
        }

        return result;
    }

    /// <summary>
    /// One way the CLI is built: the properties that select it, and whether the
    /// items are read after the targets that contribute to them have run.
    /// </summary>
    private readonly record struct BuildFlavor(string Name, string[] Properties, bool AfterTargets);

    /// <summary>
    /// Every way the CLI is built that this class reads.
    /// </summary>
    /// <remarks>
    /// More than one, because a <c>Condition</c> is part of what a build file
    /// can say and the answer changes with it. Reading only the configuration
    /// the tests happen to run in would let
    /// <c>Condition="'$(Configuration)' == 'Debug'"</c> put a file into a
    /// compilation this class never reads, and a reviewer pointed out that
    /// <c>OfficialBuild</c>, <c>OfficialAotBuild</c>, and <c>PublishAot</c> are
    /// the same hole one property further out: <c>release.yml</c> packs with
    /// them set and nothing else here ever does.
    ///
    /// Only the Release flavour is read after target execution. Running
    /// <c>CoreCompile</c> in a configuration the repository does not build
    /// would compile the whole closure to answer a question about item lists,
    /// and Release is the configuration whose assemblies
    /// <see cref="CompiledIl_ReachesStderrOnlyWhereAccountedFor"/> reads. A
    /// target-injected file in another flavour is named in the PR's declared
    /// residual rather than silently covered.
    /// </remarks>
    private static readonly BuildFlavor[] Configurations =
    [
        new("Release", ["Configuration=Release"], AfterTargets: true),
        new("Debug", ["Configuration=Debug"], AfterTargets: false),
        new("Official", ["Configuration=Release", "OfficialBuild=true"], AfterTargets: false),
        new("OfficialAot", ["Configuration=Release", "OfficialAotBuild=true"], AfterTargets: false),
        new("NoAot", ["Configuration=Release", "PublishAot=false"], AfterTargets: false),
    ];

    /// <summary>

    /// </summary>
    /// <summary>
    /// The <c>Include</c> of every <c>ProjectReference</c> in a project file.
    /// </summary>
    /// <remarks>
    /// Read with an XML parser rather than a regex. The regex this replaces was
    /// <c>&lt;ProjectReference\s+Include="..."</c>, which requires
    /// <c>Include</c> to be the first attribute; a reviewer wrote
    /// <c>&lt;ProjectReference Condition="'1' == '1'" Include="..." /&gt;</c>
    /// and the referenced project -- still compiled into the CLI -- vanished
    /// from a closure this class calls exact, taking a raw stderr write with
    /// it. Attribute order, element case, and whitespace are XML's problem, and
    /// XML is what this is reading.
    ///
    /// <c>Condition</c> is deliberately ignored rather than evaluated. A
    /// conditional reference is still a reference under some configuration, and
    /// the safe reading of a condition this class cannot evaluate is to include
    /// the project, not to drop it.
    /// </remarks>
    private static IEnumerable<string> ProjectReferences(string projectPath)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(projectPath);
        }
        catch (XmlException)
        {
            yield break;
        }

        foreach (var element in document.Descendants())
        {
            if (!string.Equals(element.Name.LocalName, "ProjectReference", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? include = element.Attributes()
                .FirstOrDefault(a => string.Equals(a.Name.LocalName, "Include", StringComparison.OrdinalIgnoreCase))
                ?.Value;

            if (!string.IsNullOrWhiteSpace(include))
            {
                yield return include;
            }
        }
    }

    /// <summary>
    /// Every <c>&lt;Using&gt;</c> element in a build file.
    /// </summary>
    /// <remarks>
    /// Read as XML for the reason the <c>ProjectReference</c> scan next to it
    /// is: a regex over markup is a guess about markup. This one guessed that
    /// an attribute list contains no <c>&gt;</c> -- both alternatives crossed it
    /// with <c>[^&gt;]*?</c> -- and <c>&gt;</c> is legal, unescaped, inside an
    /// XML attribute value, which MSBuild conditions use it as. A reviewer wrote
    /// <c>&lt;Using Condition="'1'&gt;'0'" Include="System.Console" Alias="C"/&gt;</c>
    /// and the element was never reported.
    /// </remarks>
    private static IEnumerable<XElement> UsingElements(string path)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(path, LoadOptions.SetLineInfo);
        }
        catch (XmlException)
        {
            yield break;
        }

        foreach (XElement element in document.Descendants())
        {
            if (string.Equals(element.Name.LocalName, "Using", StringComparison.OrdinalIgnoreCase))
            {
                yield return element;
            }
        }
    }

    /// <summary>
    /// Whether a <c>&lt;Using&gt;</c> element makes <c>Console</c>'s members
    /// nameable without a receiver.
    /// </summary>
    /// <remarks>
    /// An <c>Include</c> this class cannot resolve counts. MSBuild evaluates
    /// properties in that attribute, so
    /// <c>&lt;Using Include="$(SomeProperty)" Static="true"/&gt;</c> imports
    /// whatever the property holds while naming nothing. This is not an MSBuild
    /// evaluator and should not become one, so an unresolvable value is reported
    /// rather than guessed at -- an import this class cannot read is one it
    /// cannot vouch for.
    /// </remarks>
    private static bool DeclaresConsoleImport(XElement element)
    {
        string include = Attribute(element, "Include") ?? string.Empty;
        bool bindsWithoutReceiver =
            string.Equals(Attribute(element, "Static"), "true", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(Attribute(element, "Alias"));

        return string.Equals(include, "Console", StringComparison.OrdinalIgnoreCase)
            || string.Equals(include, "System.Console", StringComparison.OrdinalIgnoreCase)
            || (bindsWithoutReceiver && include.Contains("$(", StringComparison.Ordinal));
    }

    private static string? Attribute(XElement element, string name) =>
        element.Attributes()
            .FirstOrDefault(a => string.Equals(a.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
            ?.Value;

    /// <summary>
    /// Every project reachable from <paramref name="projectPath"/> through
    /// ProjectReference, including itself.
    /// </summary>
    private static HashSet<string> ProjectClosure(string projectPath)
    {
        return Closures.GetOrAdd(Path.GetFullPath(projectPath), Walk);

        static HashSet<string> Walk(string start)
        {
            HashSet<string> seen = new(StringComparer.Ordinal);
            List<string> frontier = [start];

            while (frontier.Count > 0)
            {
                List<string> current = [.. frontier.Where(p => seen.Add(p) && File.Exists(p))];
                ConcurrentBag<string> found = [];

                // A level at a time, because each project costs two MSBuild
                // evaluations and they do not depend on each other.
                Parallel.ForEach(current, project =>
                {
                    string directory = Path.GetDirectoryName(project)!;

                    foreach (string relative in ProjectReferences(project))
                    {
                        found.Add(Path.GetFullPath(
                            Path.Combine(directory, relative.Replace('\\', Path.DirectorySeparatorChar))));
                    }

                    // ... and the references the build actually resolves, which
                    // is a different set: one added by an imported build file
                    // appears here and in no project XML.
                    foreach (BuildFlavor configuration in Configurations)
                    {
                        foreach (var reference in EvaluatedItems(project, "ProjectReference", configuration))
                        {
                            if (reference.TryGetValue("FullPath", out string? full) && !string.IsNullOrEmpty(full))
                            {
                                found.Add(Path.GetFullPath(full));
                            }
                        }
                    }
                });

                frontier = [.. found.Where(p => !seen.Contains(p)).Distinct(StringComparer.Ordinal)];
            }

            return seen;
        }
    }

    private static readonly ConcurrentDictionary<string, HashSet<string>> Closures = new(StringComparer.Ordinal);

    /// <summary>
    /// Every C# file belonging to code that runs inside the CLI process.
    /// </summary>
    /// <remarks>
    /// Derived from the CLI's transitive ProjectReference closure rather than a
    /// directory. Scoping the stream rule to <c>src/dotnet-inspect</c> left its
    /// sibling <c>CommandError_IsTheOnlyWriterOfTheErrorPrefix</c> correct and
    /// this one blind: a reviewer added <c>Console.Error.WriteLine(untrusted)</c>
    /// to <c>DotnetInspector.Services</c> -- in-process, on a hostile-nuspec
    /// path -- and the suite stayed green. The closure is the exact set, and it
    /// found a real uncontained sink in <c>DotnetInspector.Core</c> the moment
    /// it was applied.
    /// </remarks>
    private static IEnumerable<string> CliSourceFiles(string root)
    {
        HashSet<string> files = new(StringComparer.Ordinal);

        foreach (string project in ProjectClosure(
            Path.Combine(root, "src", "dotnet-inspect", "dotnet-inspect.csproj")))
        {
            string directory = Path.GetDirectoryName(project)!;

            // The SDK's implicit glob.
            foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                files.Add(file);
            }

            // ... and everything the project compiles that the glob does not
            // reach: another extension, or a file linked in from outside the
            // project directory.
            foreach (string included in CompileIncludes(project))
            {
                files.Add(included);
            }

            // ... and, finally, the set the compiler is actually handed, which
            // is the only one of the three that sees a Compile item contributed
            // by a build file rather than by the project.
            foreach (BuildFlavor configuration in Configurations)
            {
                foreach (var compile in EvaluatedItems(project, "Compile", configuration))
                {
                    if (compile.TryGetValue("FullPath", out string? full)
                        && !string.IsNullOrEmpty(full)
                        && File.Exists(full))
                    {
                        files.Add(Path.GetFullPath(full));
                    }
                }
            }
        }

        return files;
    }

    /// <summary>
    /// Every build file that can put a <c>Using</c> into a CLI compilation.
    /// </summary>
    /// <remarks>
    /// This scan used to read the closure's <c>.csproj</c> files and nothing
    /// else, which mistakes "the project" for "the project's build". A reviewer
    /// put <c>&lt;Using Include="System.Console" Static="true"/&gt;</c> in
    /// <c>Directory.Build.props</c> -- imported implicitly into every project
    /// beneath it -- wrote a bare <c>Error.WriteLine(args[1])</c>, and all five
    /// tests stayed green while the CLI forged a diagnostic line.
    ///
    /// Rather than reproduce MSBuild's implicit-import walk and its explicit
    /// <c>Import</c> graph -- which would need property evaluation, and would be
    /// a second implementation of the thing being trusted -- this reads every
    /// props/targets file in the repository. Over-reading is free here: the rule
    /// only fires on an import of <c>System.Console</c> or on an unevaluable
    /// <c>Include</c>, and no build file has a legitimate reason to carry
    /// either, wherever it sits. Deriving the exact set would be more precise
    /// and strictly more fragile.
    /// </remarks>
    private static IEnumerable<string> MsBuildFiles(string root)
    {
        HashSet<string> files = new(
            ProjectClosure(Path.Combine(root, "src", "dotnet-inspect", "dotnet-inspect.csproj")),
            StringComparer.Ordinal);

        foreach (string pattern in new[] { "*.props", "*.targets" })
        {
            foreach (string file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                files.Add(file);
            }
        }

        return files;
    }

    /// <summary>
    /// The global usings the compiler was handed, as the build wrote them down,
    /// for every project in the CLI's closure.
    /// </summary>
    /// <remarks>
    /// Reading the repository's build files answers "what does this repository
    /// declare", which is not the question. A reviewer imported a props file
    /// from outside the repository -- <c>&lt;Import Project="/tmp/evil.props"/&gt;</c>,
    /// though a machine-wide <c>Directory.Build.props</c> above the clone or a
    /// NuGet package's <c>buildTransitive</c> targets need no <c>Import</c> at
    /// all -- declared <c>&lt;Using Include="System.Console" Static="true"/&gt;</c>
    /// there, wrote a bare <c>Error.WriteLine</c>, and every test stayed green.
    ///
    /// Widening the file scan cannot fix that, because the set of files MSBuild
    /// reads is not a property of this repository. So this stops reading
    /// declarations and reads the result: the SDK writes every effective
    /// <c>Using</c> into a generated <c>GlobalUsings.g.cs</c>, and that file is
    /// a compiler input, not a prediction of one. Wherever the import came from
    /// -- this repository, a parent directory, a package, an environment
    /// variable -- it appears there or it did not happen.
    ///
    /// A missing file throws. It means the closure was not built, so the
    /// observation is unavailable, and an unavailable answer must not read as a
    /// clean one.
    /// </remarks>
    private static IEnumerable<string> GeneratedSources(string root)
    {
        string artifacts = Path.Combine(root, "artifacts", "obj");

        foreach (string project in ProjectClosure(
            Path.Combine(root, "src", "dotnet-inspect", "dotnet-inspect.csproj")))
        {
            string name = Path.GetFileNameWithoutExtension(project);
            string directory = Path.Combine(artifacts, name);
            string[] generated = Directory.Exists(directory)
                ? Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
                : [];

            if (!generated.Any(f => f.EndsWith(".GlobalUsings.g.cs", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"{name}: no generated GlobalUsings.g.cs under {directory}. This rule reads what the compiler "
                    + "was handed rather than what this repository declares, which is the only way to see code or "
                    + "an import that arrives from outside it. Build the CLI closure first; a missing observation "
                    + "is not a clean one.");
            }

            foreach (string file in generated)
            {
                yield return file;
            }
        }
    }

    /// <summary>
    /// Files named by an explicit <c>&lt;Compile Include="..."/&gt;</c> in
    /// <paramref name="projectPath"/>.
    /// </summary>
    /// <remarks>
    /// The scanned set used to be "<c>*.cs</c> under each project directory",
    /// which is the SDK's default glob mistaken for the set of files the
    /// compiler reads. They are not the same set, and a reviewer showed the
    /// difference: a raw stderr write in <c>Hack.txt</c> plus
    /// <c>&lt;Compile Include="Hack.txt"/&gt;</c> compiles into the CLI and
    /// this class never opened the file. A linked file from outside the project
    /// directory is the same hole with a plainer motive.
    ///
    /// So the set is the union of the glob and what the project says, and an
    /// <c>Include</c> naming nothing on disk throws rather than resolving to
    /// zero files -- an unreadable answer must not read as an empty one. That is
    /// the same rule the MSBuild <c>Using</c> scan already follows.
    ///
    /// Wildcards are expanded; <c>Remove</c> and <c>Exclude</c> are ignored,
    /// because over-reporting a file that is not compiled costs a reworded line
    /// and under-reporting one that is costs the guarantee.
    /// </remarks>
    private static IEnumerable<string> CompileIncludes(string projectPath)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(projectPath);
        }
        catch (XmlException)
        {
            yield break;
        }

        string directory = Path.GetDirectoryName(projectPath)!;

        foreach (var element in document.Descendants())
        {
            if (!string.Equals(element.Name.LocalName, "Compile", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? include = element.Attributes()
                .FirstOrDefault(a => string.Equals(a.Name.LocalName, "Include", StringComparison.OrdinalIgnoreCase))
                ?.Value;

            if (string.IsNullOrWhiteSpace(include) || include.Contains("$(", StringComparison.Ordinal))
            {
                // A property this class cannot evaluate. Refusing to guess is
                // the same answer the MSBuild Using scan gives.
                if (!string.IsNullOrWhiteSpace(include))
                {
                    throw new InvalidOperationException(
                        $"{projectPath}: <Compile Include=\"{include}\"/> is not statically resolvable, so the "
                        + "set of files compiled into the CLI cannot be determined. Resolve it or teach this scan "
                        + "to expand it; do not let it read as zero files.");
                }

                continue;
            }

            string pattern = include.Replace('\\', Path.DirectorySeparatorChar);
            string searchDirectory = Path.GetFullPath(
                Path.Combine(directory, Path.GetDirectoryName(pattern) is { Length: > 0 } d ? d : "."));
            string name = Path.GetFileName(pattern);

            string[] matches = Directory.Exists(searchDirectory)
                ? Directory.GetFiles(searchDirectory, name, SearchOption.TopDirectoryOnly)
                : [];

            if (matches.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{projectPath}: <Compile Include=\"{include}\"/> matched no file on disk. This scan claims "
                    + "to cover every file compiled into the CLI, so an Include it cannot resolve is a gap in "
                    + "that claim rather than an empty result.");
            }

            foreach (string match in matches)
            {
                yield return match;
            }
        }
    }

    /// <summary>
    /// Every reading of <paramref name="source"/> the rules must be checked
    /// against: the compiler's own token stream, once per configuration the CLI
    /// is built in. A match in any reading is a match.
    /// </summary>
    /// <remarks>
    /// Every rule in this file is a claim about the program the compiler sees,
    /// so the text they match has to be that program. Six rounds of review were
    /// spent discovering that a hand-written approximation of the C# lexer is
    /// not that program, each round by the same route -- a construction the
    /// approximation read differently than the compiler:
    ///
    /// <list type="bullet">
    /// <item>a line-anchored comment test beaten by a leading comment;</item>
    /// <item><c>//</c> inside a string literal, blanking the write after it;</item>
    /// <item><c>System.\u0043onsole</c>, an identifier the scan never decoded;</item>
    /// <item><c>Console.@Error</c>, and then <c>@\u0045rror</c>, where the
    /// <c>@</c>-drop was decided against the undecoded next character;</item>
    /// <item>an interpolation hole, which is literal content and code at once;</item>
    /// <item>and finally <c>$" { "} /* " } ";</c>, where an unescaped quote
    /// inside a hole ended the literal early, so the <c>/*</c> the compiler
    /// reads as string content opened a comment that blanked the real write
    /// after it -- in <i>both</i> of the two readings that replaced the single
    /// one, because a desynchronized literal scan and a literal-blind scan
    /// agree about exactly this.</item>
    /// </list>
    ///
    /// The pattern is not that the approximation had six bugs. It is that a
    /// second implementation of a lexer is a place for bugs to be, and here a
    /// bug is silence rather than a false report. So this stops approximating
    /// and tokenizes with the compiler's own lexer, which the repository already
    /// takes as a test-only dependency. Comments arrive as trivia, an
    /// interpolation hole arrives as ordinary tokens, a literal arrives as one
    /// token however it is quoted, and <c>ValueText</c> gives an identifier the
    /// name it binds to with escapes decoded and the <c>@</c> gone. All four
    /// helpers this replaced were approximations of those four facts.
    ///
    /// Token text is written back at its own offset, so line numbers stay the
    /// source's. Literal <i>contents</i> are still kept: a write inside a hole
    /// is a separate token and is seen on its own, so blanking is not needed to
    /// find it, and quoting <c>Console.Error.WriteLine(</c> in a string remains
    /// a false report by design -- loud, and fixed by rewording.
    ///
    /// Excluded code is the other half. Code inside a <c>#if</c> whose symbol
    /// is undefined is disabled text -- no tokens, so no rule can see it -- and
    /// the DEBUG-only sink in <c>HttpClientFactory.cs</c> is a live example of
    /// code that reaches the stream in one configuration only. Reading each
    /// file once would make <c>#if</c> a way to hide a write.
    ///
    /// The symbols are taken from the file rather than listed here, because a
    /// list is the same mistake as the lexer: a first draft of this defined
    /// <c>DEBUG</c> and nothing else, and the CLI turned out to test
    /// <c>NET11_0_OR_GREATER</c> too, so a write under that condition would
    /// have been invisible in every reading.
    ///
    /// Every assignment of those symbols is read -- not a chosen few, because
    /// choosing needs an argument about which configurations matter and the
    /// second draft's argument was also wrong. It read three configurations
    /// (none, all, all-but-<c>DEBUG</c>) and asserted every condition tested one
    /// optionally-negated symbol, which sounds sufficient and is not: those
    /// three only ever assign a pair of symbols <c>(off,off)</c> or
    /// <c>(on,on)</c>, while an <c>#elif B</c> after <c>#if A</c> is live under
    /// <c>!A &amp;&amp; B</c>. A reviewer put a live
    /// <c>Console.Error.WriteLine(args[0])</c> in exactly that branch; it
    /// compiled, it ran, it forged an <c>Error:</c> line from argv, and all five
    /// tests here stayed green. Per-condition simplicity was never the property
    /// that mattered -- reachability of a branch is a conjunction across the
    /// chain, not a fact about one directive.
    ///
    /// Enumerating retires that argument and the shape restriction with it:
    /// compound conditions, nested chains and <c>#else</c> all follow from each
    /// symbol being tried both ways. It is affordable because the exponent is
    /// tiny -- measured over the CLI's sources and its generated files, 1978
    /// name no symbol at all and 5 name exactly one -- and where it would not
    /// be, <see cref="MaxConditionalSymbols"/> throws rather than quietly
    /// reading fewer configurations than the file has.
    /// </remarks>
    private static string Code(string source) => CodeText(source, []).Text;

    /// <summary>
    /// One configuration of a file: the tokens the compiler produces for it,
    /// written back at their own offsets, and the tree they came from.
    /// </summary>
    /// <remarks>
    /// The tree travels with the text because two questions here are not
    /// answerable by matching it. Which imports a file makes is one -- a
    /// using directive has a grammar, and every regex spelling of it has lost
    /// to a legal one. Where a match sits is the other: a syntax tree turns an
    /// offset into the statement it belongs to, which is what makes an
    /// accounted sink identifiable as itself.
    /// </remarks>
    private readonly record struct Reading(string Text, SyntaxTree Tree);

    /// <summary>
    /// The most distinct conditional symbols one file may name.
    /// </summary>
    /// <remarks>
    /// The bound is on the enumeration, not on the code. Exceeding it means
    /// this class can no longer read every configuration of that file, which
    /// has to be a failure rather than a silently smaller set of readings.
    /// </remarks>
    private const int MaxConditionalSymbols = 8;

    private static IEnumerable<Reading> CodeReadings(string source)
    {
        string[] symbols =
        [
            .. ConditionalSymbols(source).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
        ];

        if (symbols.Length > MaxConditionalSymbols)
        {
            throw new InvalidOperationException(
                $"A file names {symbols.Length} conditional symbols ({string.Join(", ", symbols)}), more than the "
                + $"{MaxConditionalSymbols} configurations of which this scan enumerates. Reading only some of them "
                + "leaves the rest unread, which is the hole the enumeration exists to close.");
        }

        for (int assignment = 0; assignment < 1 << symbols.Length; assignment++)
        {
            int defined = assignment;
            yield return CodeText(source, [.. symbols.Where((_, i) => (defined & (1 << i)) != 0)]);
        }
    }

    /// <summary>
    /// <paramref name="source"/> reduced to the tokens the compiler produces
    /// for it under <paramref name="symbols"/>, each written back at its own
    /// offset with identifiers spelled the way they bind.
    /// </summary>
    private static Reading CodeText(string source, string[] symbols)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview, preprocessorSymbols: symbols));

        // Everything not covered by a token -- comments, disabled regions,
        // whitespace -- becomes a space, with line breaks kept so that a
        // reported line number is the one in the file.
        char[] result = new char[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            result[i] = source[i] is '\n' or '\r' ? source[i] : ' ';
        }

        foreach (SyntaxToken token in tree.GetRoot().DescendantTokens())
        {
            // ValueText decodes \uXXXX and drops the @ of a verbatim
            // identifier, which is what "the name it binds to" means. It is
            // never longer than the text it replaces, so writing it at the
            // token's own offset cannot disturb a later token.
            string text = token.IsKind(SyntaxKind.IdentifierToken) ? token.ValueText : token.Text;
            Assert.True(text.Length <= token.Span.Length);

            for (int i = 0; i < text.Length; i++)
            {
                result[token.Span.Start + i] = text[i];
            }
        }

        return new Reading(new string(result), tree);
    }

    /// <summary>
    /// The preprocessor symbols <paramref name="source"/> tests, whether or not
    /// they are defined.
    /// </summary>
    private static IEnumerable<string> ConditionalSymbols(string source)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));

        foreach (SyntaxTrivia trivia in tree.GetRoot().DescendantTrivia())
        {
            if (trivia.GetStructure() is ConditionalDirectiveTriviaSyntax directive)
            {
                foreach (SyntaxToken token in directive.Condition.DescendantTokens())
                {
                    if (token.IsKind(SyntaxKind.IdentifierToken))
                    {
                        yield return token.ValueText;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Every using directive in <paramref name="tree"/> that makes
    /// <c>Console</c>'s members nameable without writing <c>Console</c>.
    /// </summary>
    /// <remarks>
    /// <c>using static System.Console;</c> followed by a bare
    /// <c>Error.WriteLine(untrusted)</c> defeats every other rule in this file
    /// at once, because each of them requires the literal receiver. An alias,
    /// <c>using C = System.Console;</c>, does the same thing. So the import is
    /// forbidden rather than its consequences enumerated: after it, the only
    /// way to name the type is <c>Console</c> or <c>System.Console</c>, and
    /// that is a set rather than a list of spellings.
    ///
    /// Which is only true if the import itself is recognised, and the regex
    /// this replaces recognised a grammar it had guessed. Four rounds of
    /// reviewers spent their findings on that guess: <c>@C</c>, a leading
    /// comment where the pattern wanted a line start, <c>global::</c>,
    /// <c>\u0043onsole</c>. The last two arrived together --
    /// <c>using Ω = System.Console;</c>, because the alias character class was
    /// <c>[A-Za-z_]</c> and a C# identifier is any Unicode letter, and
    /// <c>using static sys::System.Console;</c>, because the only alias
    /// qualifier the pattern knew was <c>global</c>. Both compile; neither
    /// matched.
    ///
    /// A using directive is not a string with a shape, so this asks the parser
    /// instead. <see cref="UsingDirectiveSyntax"/> tells us whether the
    /// directive is static or aliased without any spelling being involved, and
    /// walking the name to its last identifier is indifferent to how the
    /// qualifier in front of it is written or which alphabet the alias is in.
    /// </remarks>
    private static IEnumerable<UsingDirectiveSyntax> ConsoleImports(SyntaxTree tree)
    {
        foreach (UsingDirectiveSyntax directive in tree.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            bool bindsWithoutReceiver = directive.Alias is not null
                || directive.StaticKeyword.IsKind(SyntaxKind.StaticKeyword);

            if (bindsWithoutReceiver
                && string.Equals(LastIdentifier(directive.NamespaceOrType), "Console", StringComparison.Ordinal))
            {
                yield return directive;
            }
        }
    }

    /// <summary>
    /// The last identifier of a possibly-qualified name -- the type or
    /// namespace being named, with every qualifier in front of it discarded.
    /// </summary>
    /// <remarks>
    /// The qualifier is discarded rather than checked because it cannot make
    /// the import safe. <c>global::System.Console</c>, <c>sys::System.Console</c>
    /// and <c>System.Console</c> import the same type; an extern alias root is
    /// declared elsewhere in the file, so reading it here would be one more
    /// spelling to get right. Matching on the last identifier over-reports a
    /// hypothetical unrelated <c>Console</c>, which costs a rename.
    /// </remarks>
    private static string? LastIdentifier(TypeSyntax? name) => name switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        QualifiedNameSyntax qualified => LastIdentifier(qualified.Right),
        AliasQualifiedNameSyntax aliased => LastIdentifier(aliased.Name),
        _ => null,
    };

    /// <summary>
    /// The smallest statement or member containing <paramref name="offset"/>,
    /// as its own tokens with the whitespace, comments, and line breaks between
    /// them removed.
    /// </summary>
    /// <remarks>
    /// This is how an accounted stderr sink is named. A per-file count is not
    /// an identity: a reviewer pointed out that removing one accounted sink and
    /// adding an unaccounted one in the same file leaves the tally where it was,
    /// so the one file with two accounted sinks could carry a substitution that
    /// this class calls accounted. Naming the statement makes the substitution
    /// a change to the pinned set, which is the point of pinning it.
    ///
    /// Normalising to tokens keeps reformatting, rewrapping, and comments from
    /// churning the pin, so the pin moves when the code moves and not when the
    /// file does.
    /// </remarks>
    private static string EnclosingStatement(SyntaxTree tree, int offset)
    {
        SyntaxNode? node = tree.GetRoot().FindToken(offset).Parent;

        while (node is not null and not StatementSyntax and not MemberDeclarationSyntax)
        {
            node = node.Parent;
        }

        return node is null
            ? "<unattached>"
            : string.Concat(node.DescendantTokens().Select(t => t.Text));
    }


    /// <summary>
    /// The value MSBuild evaluates <paramref name="property"/> to for
    /// <paramref name="projectPath"/>.
    /// </summary>
    private static string EvaluatedProperty(string projectPath, string property, string configuration)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                ArgumentList = { "msbuild", projectPath, $"-getProperty:{property}", $"-p:Configuration={configuration}" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };

        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0
            ? output.Trim()
            : throw new InvalidOperationException($"Could not evaluate {property} for {projectPath}.{Environment.NewLine}{output}");
    }

    /// <summary>
    /// Every member of <c>System.Console</c> that reaches stderr, named as the
    /// compiler emits it.
    /// </summary>
    private static readonly HashSet<string> StderrMembers = new(StringComparer.Ordinal)
    {
        "get_Error",
        "OpenStandardError",
        "SetError",
    };

    /// <summary>
    /// Every method in <paramref name="assemblyPath"/> whose IL references a
    /// member of <see cref="StderrMembers"/>.
    /// </summary>
    /// <remarks>
    /// Read as IL rather than as source because IL is what runs. Every finding
    /// from round 20 to round 26 of this review was a way of writing C# that
    /// the source scan read differently than the compiler did -- an escape, a
    /// verbatim identifier, an interpolation hole, an alias in an alphabet the
    /// pattern did not cover, a preprocessor branch it did not realise, a file
    /// it never opened because a build file rather than a project named it.
    /// None of those is expressible here: by the time a call is in a method
    /// body it has one spelling, and it is this one.
    ///
    /// The two rules are kept together because they fail differently and are
    /// worth different things. The source rule names a file and a line, which
    /// is what a contributor needs, and it reads code that is compiled in
    /// configurations this one is not built in. This rule cannot be evaded by
    /// how the code is written at all. A leak has to get past both.
    /// </remarks>
    private static List<string> ConsoleErrorReferences(string assemblyPath)
    {
        List<string> references = [];
        using FileStream stream = File.OpenRead(assemblyPath);
        using PEReader pe = new(stream);
        MetadataReader reader = pe.GetMetadataReader();
        string assembly = Path.GetFileNameWithoutExtension(assemblyPath);

        foreach (MethodDefinitionHandle handle in reader.MethodDefinitions)
        {
            MethodDefinition method = reader.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0)
            {
                continue;
            }

            MethodBodyBlock body = pe.GetMethodBody(method.RelativeVirtualAddress);
            ILReader il = new(body.GetILBytes() ?? []);

            while (il.HasNext)
            {
                ILOpCode opCode = il.ReadILOpcode();

                if (opCode is ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj
                    or ILOpCode.Ldftn or ILOpCode.Ldvirtftn or ILOpCode.Jmp or ILOpCode.Ldtoken)
                {
                    if (NamesStderrMember(reader, MetadataTokens.EntityHandle(il.ReadILToken())))
                    {
                        TypeDefinition declaring = reader.GetTypeDefinition(method.GetDeclaringType());
                        string type = reader.GetString(declaring.Namespace) is { Length: > 0 } ns
                            ? $"{ns}.{reader.GetString(declaring.Name)}"
                            : reader.GetString(declaring.Name);

                        references.Add($"{assembly}!{type}.{reader.GetString(method.Name)}");
                    }
                }
                else if (!il.TrySkip(opCode))
                {
                    throw new InvalidOperationException(
                        $"{assembly}: could not decode {opCode} in {reader.GetString(method.Name)}. A body this rule "
                        + "cannot read is a body it cannot vouch for.");
                }
            }
        }

        return references;
    }

    /// <summary>
    /// Whether <paramref name="handle"/> names a <c>System.Console</c> member
    /// that reaches stderr.
    /// </summary>
    private static bool NamesStderrMember(MetadataReader reader, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.MethodSpecification:
                return NamesStderrMember(reader, reader.GetMethodSpecification((MethodSpecificationHandle)handle).Method);

            case HandleKind.MemberReference:
                MemberReference member = reader.GetMemberReference((MemberReferenceHandle)handle);
                if (!StderrMembers.Contains(reader.GetString(member.Name))
                    || member.Parent.Kind != HandleKind.TypeReference)
                {
                    return false;
                }

                TypeReference parent = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
                return reader.GetString(parent.Namespace) == "System"
                    && reader.GetString(parent.Name) == "Console";

            default:
                return false;
        }
    }

    /// <summary>
    /// Pins the rule that actually closes this class of defect: outside the
    /// owner, no code in the CLI process writes text to stderr.
    /// </summary>
    /// <remarks>
    /// The severity-literal scan below is a spelling rule, and a spelling rule
    /// is evadable by construction -- <c>"Error" + ": "</c>,
    /// <c>string.Format("Error: {0}", m)</c>, or <c>$"{severity}: {m}"</c> all
    /// produce the same forged line without ever spelling it. Worse, it only
    /// describes lines that carry a severity, and stderr also carries
    /// suggestion lists, TFM lists, and progress text. Thirty-four such sites
    /// wrote untrusted text raw; <c>depends</c> printed a hostile package's
    /// <c>targetFramework</c> attribute unindented, forging a diagnostic with
    /// no severity literal anywhere in the source.
    ///
    /// Owning the stream subsumes all of it: if every line comes from the
    /// writer, every line is contained, whatever the caller composed and
    /// however it spelled it. That is a property of the code that runs, not of
    /// the text that appears in it.
    ///
    /// Handing <c>Console.Error</c> to a renderer as a sink stays allowed,
    /// because this scan cannot tell a containing renderer from a
    /// non-containing one. That allowance is the rule's known blind spot and it
    /// has already been exploited once: <c>--trace-mermaid</c> passed the
    /// stream to a bespoke writer that escaped only the two Mermaid
    /// metacharacters, so a line terminator in a package name forged an
    /// unindented stderr line without a single <c>Console.Error.Write</c> in
    /// the source. Two reviewers found it independently.
    ///
    /// Each sink is therefore accounted for by name rather than by category,
    /// and a new one is a change this test cannot catch:
    /// <list type="bullet">
    /// <item><c>Output/Hints.cs</c> x2 -- Markout views whose untrusted field
    /// is contained when the row is built.</item>
    /// <item><c>Program.cs</c> --info -- a Markout view of counts, durations,
    /// and the readme path from inside the .nupkg.</item>
    /// <item><c>Program.cs</c> --trace-mermaid -- contained at composition,
    /// with containment a required parameter so no caller can omit it, and
    /// gated end to end by the trace-mermaid channel.</item>
    /// <item><c>DotnetInspector.Core/HttpClientFactory.cs</c> -- the DEBUG-only
    /// network traffic log, whose URL carries the package id from argv;
    /// contained through a required constructor parameter.</item>
    /// </list>
    /// <see cref="StderrSinks_AreStillTheOnesAccountedFor"/> fails when this
    /// list goes stale.
    /// </remarks>
    [Fact]
    public void CommandError_IsTheOnlyWriterOfStderr()
    {
        string root = RepositoryRoot();
        string owner = Path.Combine(root, "src", "dotnet-inspect", "Output", "CommandError.cs");

        List<string> offenders = [];

        // The checked-in sources plus the build's own record of what the
        // compiler was additionally handed. The second set is the only reading
        // that sees an import arriving from outside the repository, and the
        // only one that sees generated code at all -- the CLI compiles ~490
        // generated files, including the Markout serializers for the very view
        // rows that carry untrusted text.
        foreach (string path in CliSourceFiles(root).Concat(GeneratedSources(root)))
        {
            if (string.Equals(path, owner, StringComparison.Ordinal))
            {
                continue;
            }

            string source = File.ReadAllText(path);
            foreach (Reading reading in CodeReadings(source))
            {
                // Token text is written back at its own offset, so a line
                // number is the source's in every reading -- which is also what
                // makes the offender string a sound key for the overlap between
                // configurations that compile the same code.
                foreach (Match match in StderrWrite.Matches(reading.Text))
                {
                    Report(match.Index, match.Value.Trim());
                }

                foreach (UsingDirectiveSyntax import in ConsoleImports(reading.Tree))
                {
                    Report(import.Span.Start, import.ToString().Trim());
                }

                void Report(int offset, string what)
                {
                    int line = reading.Text.Take(offset).Count(c => c == '\n') + 1;
                    string offender = $"{Path.GetRelativePath(root, path)}:{line}: {what}";
                    if (!offenders.Contains(offender))
                    {
                        offenders.Add(offender);
                    }
                }
            }
        }


        // The declaring side of the same import. It reports a narrower set than
        // the generated files above and is kept for the message: it names the
        // build file a contributor has to edit, which the generated file cannot.
        foreach (string project in MsBuildFiles(root))
        {
            foreach (XElement element in UsingElements(project))
            {
                if (!DeclaresConsoleImport(element))
                {
                    continue;
                }

                int line = ((IXmlLineInfo)element).HasLineInfo() ? ((IXmlLineInfo)element).LineNumber : 0;
                string why = (Attribute(element, "Include") ?? string.Empty).Contains("$(", StringComparison.Ordinal)
                    ? " (Include is a property this rule cannot evaluate; spell the type literally)"
                    : string.Empty;
                offenders.Add($"{Path.GetRelativePath(root, project)}:{line}: {element}{why}");
            }
        }

        // ... and the resolved side, which is what the projects in the closure
        // were actually built with. A property-valued Include is a name only
        // here; a Using contributed by a build file this repository does not
        // contain appears only here.
        foreach (string project in ProjectClosure(
            Path.Combine(root, "src", "dotnet-inspect", "dotnet-inspect.csproj")))
        {
            foreach (BuildFlavor configuration in Configurations)
            {
                foreach (var import in EvaluatedItems(project, "Using", configuration))
                {
                    import.TryGetValue("Identity", out string? identity);
                    bool bindsWithoutReceiver = string.Equals(
                            import.GetValueOrDefault("Static"), "true", StringComparison.OrdinalIgnoreCase)
                        || !string.IsNullOrEmpty(import.GetValueOrDefault("Alias"));

                    if (bindsWithoutReceiver
                        && (string.Equals(identity, "Console", StringComparison.Ordinal)
                            || string.Equals(identity, "System.Console", StringComparison.Ordinal)))
                    {
                        offenders.Add(
                            $"{Path.GetRelativePath(root, project)} ({configuration.Name}): <Using Include=\"{identity}\"/>");
                    }
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Only CommandError may write text to stderr, so that every line on the stream is "
                + $"contained. Use CommandError.Write/WriteWarning/WriteNote/WriteLine/WriteDetail. "
                + $"A `using static System.Console` is reported too: it makes `Error.WriteLine` "
                + $"reachable without the receiver this rule keys on.{Environment.NewLine}"
                + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Pins the set of places that hand stderr to something other than a direct
    /// write, which is the one shape
    /// <see cref="CommandError_IsTheOnlyWriterOfStderr"/> cannot check. A new
    /// sink is a real risk -- two of these five were live forgeries -- so adding
    /// one must fail here and force a decision about how its text is contained.
    /// </summary>
    /// <remarks>
    /// The assertion is the set of sites, not their number. Asserting
    /// <c>sinks.Count == 4</c> made the test blind in exactly the direction it
    /// exists to watch: a reviewer aliased <c>Console.Error</c> into a local,
    /// added a fifth uncontained sink alongside the four real ones, and the
    /// count-based version passed. A per-file tally moves with any addition,
    /// including one that replaces a site it also removes.
    /// </remarks>
    [Fact]
    public void StderrSinks_AreStillTheOnesAccountedFor()
    {
        string root = RepositoryRoot();
        Dictionary<string, int> sinks = new(StringComparer.Ordinal);

        foreach (string path in CliSourceFiles(root))
        {
            // Read in every configuration -- a sink compiled in only one of
            // them, as the DEBUG-only one below is, is still a sink -- and
            // deduplicated by offset, because token text is written back at its
            // own position, so one site seen in two configurations is one
            // offset in both.
            string source = File.ReadAllText(path);
            string file = Path.GetRelativePath(root, path).Replace('\\', '/');
            Dictionary<int, string> sites = [];

            foreach (Reading reading in CodeReadings(source))
            {
                foreach (Match match in StderrSink.Matches(reading.Text))
                {
                    sites[match.Index] = EnclosingStatement(reading.Tree, match.Index);
                }
            }

            // Counted per statement rather than per file. Two sites that are
            // the same statement are indistinguishable and are pinned as a
            // count; two that are not are separate entries, which is what makes
            // a substitution within one file visible.
            foreach (var site in sites.Values.GroupBy(v => v, StringComparer.Ordinal))
            {
                sinks[$"{file}: {site.Key}"] = site.Count();
            }
        }

        Dictionary<string, int> accounted = new(StringComparer.Ordinal)
        {
            // Markout views of the tips and the legend, written through one
            // call site; every field of both rows is contained where the row is
            // built.
            ["src/dotnet-inspect/Output/Hints.cs: MarkoutSerializer.Serialize(view,Console.Error,newPlainTextFormatter(),TipsViewContext.Default);"] = 2,

            // --info: a view of counts and durations, plus the readme path from
            // inside the .nupkg, contained at row construction.
            ["src/dotnet-inspect/Program.cs: MarkoutSerializer.Serialize(view,Console.Error,InfoViewContext.Default);"] = 1,

            // --trace-mermaid: contained at composition, with containment a
            // required parameter so no caller can omit it.
            ["src/dotnet-inspect/Program.cs: traceMermaid.WriteTo(Console.Error,CSharpIdentifier.ContainRenderedText);"] = 1,

            // The network traffic log. Its call site is behind #if DEBUG, but
            // the method itself is public API and ships, as
            // CompiledIl_ReachesStderrOnlyWhereAccountedFor shows. The logged
            // URL carries the package id from argv, so its consumer takes
            // containment as a required constructor parameter.
            ["src/DotnetInspector.Core/HttpClientFactory.cs: return_networkTrafficLoggingSubscription??=NetworkTelemetry.Subscribe(newNetworkTrafficLogConsumer(Console.Error,contain));"] = 1,
        };

        Assert.Equal(accounted, sinks);
    }

    /// <summary>
    /// Guards the stream rule against becoming vacuous if the pattern or the
    /// scanned root stops matching real code.
    /// </summary>
    [Fact]
    public void StderrScan_MatchesTheShapeItIsMeantToCatch()
    {
        Assert.Matches(StderrWrite, "        Console.Error.WriteLine($\"{a}\");");
        Assert.Matches(StderrWrite, "Console.Error.Write(x);");
        Assert.DoesNotMatch(StderrWrite, "MarkoutSerializer.Serialize(view, Console.Error, ctx);");

        // The sink scan has to see the stream however it is handed over, not
        // only in argument position: each of these was green under the earlier
        // pattern, and the first is the one a reviewer used to smuggle a fifth
        // sink past a passing test.
        Assert.Matches(StderrSink, "var sink = Console.Error;");
        Assert.Matches(StderrSink, "Serialize(view, writer: Console.Error, ctx);");
        Assert.Matches(StderrSink, "using var s = Console.OpenStandardError();");
        Assert.Matches(StderrSink, "Console.SetError(w);");
        Assert.DoesNotMatch(StderrSink, "Console.Error.WriteLine(x);");

        // Naming a method rather than the stream left every other member of
        // TextWriter open; each of these reaches stderr and none is a Write or
        // a WriteLine.
        Assert.Matches(StderrWrite, "Console.Error.WriteAsync(value).GetAwaiter().GetResult();");
        Assert.Matches(StderrWrite, "await Console.Error.WriteLineAsync(value);");
        Assert.Matches(StderrWrite, "Console.Error.Flush();");
        Assert.Matches(StderrWrite, "System.Console.Error.WriteLine(x);");

        // An import of the type makes `Error` nameable with no receiver, which
        // is invisible to every other rule in this file.
        Assert.True(ImportsConsole("using static System.Console;"));
        Assert.True(ImportsConsole("using static Console;"));
        Assert.True(ImportsConsole("global using static System.Console;"));
        Assert.True(ImportsConsole("using C = System.Console;"));
        Assert.True(ImportsConsole("using C = global::System.Console;"));

        // A directive is legal after anything on its line, and the regex this
        // replaces was anchored to the line start.
        Assert.True(ImportsConsole("/* bypass */ using static System.Console;"));
        Assert.True(ImportsConsole("\t  using   static   System . Console ;"));

        // An alias is an identifier, not `[A-Za-z_]\w*`, and the qualifier in
        // front of the name is not always `global`. Both of these compiled
        // against the pattern this replaces and matched nothing.
        Assert.True(ImportsConsole("using \u03a9 = System.Console;"));
        Assert.True(ImportsConsole("extern alias sys;\nusing static sys::System.Console;"));
        Assert.True(ImportsConsole("using \u00c9rr = global::System.Console;"));

        // Naming the type is not importing it.
        Assert.False(ImportsConsole("using System;"));
        Assert.False(ImportsConsole("using static System.Math;"));
        Assert.False(ImportsConsole("using DotnetInspector.Output.ConsoleTheme;"));
        Assert.False(ImportsConsole("var x = Console.Out;"));

        // The MSBuild spelling of the same import, including one whose value
        // this class cannot resolve, and one whose attribute list carries a
        // raw `>` -- legal in XML, and the character the retired regex used to
        // find the end of the element.
        Assert.True(DeclaresConsoleImport("<Using Include=\"System.Console\" Static=\"true\" />"));
        Assert.True(DeclaresConsoleImport("<Using Static=\"true\" Include=\"$(Reviewer)\" />"));
        Assert.True(DeclaresConsoleImport("<Using Condition=\"'1'>'0'\" Include=\"System.Console\" Alias=\"C\" />"));
        Assert.False(DeclaresConsoleImport("<Using Include=\"System.Linq\" />"));

        // Every construction that defeated an earlier version of this scan.
        // The rules now run over the compiler's token stream, so each of these
        // is a regression case for a specific way a hand-written lexer read the
        // file differently than the compiler did.
        //
        // Prose is exempt; code that merely follows prose is not.
        Assert.DoesNotMatch(StderrSink, Code("// see Console.Error for why\n"));
        Assert.Matches(StderrSink, Code("// prose\nvar sink = Console.Error;\n"));
        Assert.Matches(StderrWrite, Code("// it's fine\nConsole.Error.WriteLine(x);"));

        // `//` inside a literal is not a comment, in any spelling of literal.
        Assert.Matches(StderrWrite, Code("_ = \"https://\"; Console.Error.WriteLine(x);"));
        Assert.Matches(StderrWrite, Code("_ = @\"c://p\"; Console.Error.WriteLine(x);"));
        Assert.Matches(StderrWrite, Code("_ = \"\"\"a//b\"\"\"; Console.Error.WriteLine(x);"));
        Assert.Matches(StderrWrite, Code("_ = \"a\\\"//b\"; Console.Error.WriteLine(x);"));

        // ...including one that ends a literal early only for a lexer that does
        // not track interpolation holes. Both readings of the previous scan
        // blanked the write after this as a comment.
        Assert.Matches(
            StderrWrite,
            Code("string s = $\" { \"} /* \" } \";\nConsole.Error.WriteLine(x);\n/* */"));

        // A Unicode-escaped identifier binds to the same type...
        Assert.Matches(StderrWrite, Code("System.\\u0043onsole.Error.WriteLine(x);"));
        Assert.Matches(StderrWrite, Code("System.\\U00000043onsole.Error.WriteLine(x);"));
        Assert.True(ImportsConsole("using static System.\\u0043onsole;"));

        // ...including inside an interpolation hole, which is code however the
        // string around it is quoted.
        Assert.Matches(
            StderrWrite,
            Code("_ = $\"\"\"{System.\\u0043onsole.Error.WriteLineAsync(args[0])}\"\"\";"));
        Assert.Matches(StderrWrite, Code("_ = $\"{System.Console./*x*/Error.WriteLineAsync(y)}\";"));

        // A verbatim identifier binds to the same member, in every position.
        Assert.Matches(StderrWrite, Code("System.Console.@Error.WriteLine(x);"));
        Assert.Matches(StderrWrite, Code("System.@Console.@Error.@WriteLine(x);"));
        Assert.Matches(StderrSink, Code("var sink = Console.@Error;"));
        Assert.True(ImportsConsole("using static System.@Console;"));
        Assert.True(ImportsConsole("using @C = System.Console;"));

        // A `@` before an escape is still an identifier prefix. Deciding that
        // against the raw next character kept the `@`, decoded to `@Error`, and
        // matched nothing.
        Assert.Matches(StderrWrite, Code("Console.@\\u0045rror.WriteLine(x);"));
        Assert.True(ImportsConsole("using static System.@\\u0043onsole;"));

        // A literal is kept as a literal, so an escape inside one stays part of
        // its value and cannot forge syntax. Quoting the call in a string is
        // still a false report by design -- loud, and fixed by rewording.
        Assert.DoesNotContain("\"", Code("var x = a\\u0022b;"), StringComparison.Ordinal);
        Assert.Matches(StderrWrite, Code("_ = \"Console.Error.WriteLine(\";"));

        // Code excluded by the configuration being parsed contributes no
        // tokens, which is why the CLI is read once per configuration it is
        // built in.
        const string Conditional = "#if DEBUG\nConsole.Error.WriteLine(x);\n#endif\n";
        Assert.DoesNotMatch(StderrWrite, CodeText(Conditional, []).Text);
        Assert.Matches(StderrWrite, CodeText(Conditional, ["DEBUG"]).Text);
        Assert.Contains(CodeReadings(Conditional), r => StderrWrite.IsMatch(r.Text));

        // A symbol nobody thought to list is read out of the file, and both
        // polarities of it are covered.
        const string Unlisted = "#if NET11_0_OR_GREATER\nConsole.Error.WriteLine(x);\n#endif\n";
        Assert.Contains(CodeReadings(Unlisted), r => StderrWrite.IsMatch(r.Text));
        const string Negated = "#if !DEBUG\nConsole.Error.WriteLine(x);\n#endif\n";
        Assert.Contains(CodeReadings(Negated), r => StderrWrite.IsMatch(r.Text));

        // A branch's reachability is a conjunction across its chain, not a
        // property of one directive. These three are live under an assignment
        // that no fixed handful of readings produces: the first needs
        // `!A && B`, the second needs an outer symbol on and an inner one off,
        // and the third is a condition no per-directive shape rule admits. A
        // reviewer put a live write in the first and every test here stayed
        // green.
        const string Elif = "#if A\n#elif B\nConsole.Error.WriteLine(x);\n#endif\n";
        Assert.Contains(CodeReadings(Elif), r => StderrWrite.IsMatch(r.Text));
        const string Nested = "#if A\n#if !B\nConsole.Error.WriteLine(x);\n#endif\n#endif\n";
        Assert.Contains(CodeReadings(Nested), r => StderrWrite.IsMatch(r.Text));
        const string Compound = "#if A && !B\nConsole.Error.WriteLine(x);\n#endif\n";
        Assert.Contains(CodeReadings(Compound), r => StderrWrite.IsMatch(r.Text));

        // Enumeration is only exhaustive while it is affordable, so the bound
        // on it fails rather than silently reading fewer configurations.
        Assert.Throws<InvalidOperationException>(
            () => CodeReadings(string.Concat(
                Enumerable.Range(0, MaxConditionalSymbols + 1).Select(i => $"#if S{i}\n#endif\n"))).ToList());

        // The symbols really are read from the file: the CLI tests more than
        // DEBUG, which is what retired the hard-coded list.
        string[] symbols = CliSourceFiles(RepositoryRoot())
            .SelectMany(f => ConditionalSymbols(File.ReadAllText(f)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.Contains("DEBUG", symbols);
        Assert.True(symbols.Length > 1, string.Join(", ", symbols));

        // The owner still writes, so the rule is about who, not about whether.
        string owner = Path.Combine(RepositoryRoot(), "src", "dotnet-inspect", "Output", "CommandError.cs");
        Assert.Matches(StderrWrite, File.ReadAllText(owner));

        // The generated sources are read as the compiler's input, so the
        // spelling the SDK emits has to match.
        Assert.True(ImportsConsole("global using static global::System.Console;"));

        // Source-generator output is only observable because the build is asked
        // to write it down. If EmitCompilerGeneratedFiles is removed from
        // Directory.Build.props the files vanish silently and the scan above
        // reads clean, so the property is checked by its effect rather than
        // trusted.
        string[] generated = [.. GeneratedSources(RepositoryRoot())];
        Assert.Contains(generated, f => f.EndsWith(".GlobalUsings.g.cs", StringComparison.Ordinal));
        Assert.Contains(
            generated,
            f => f.Replace('\\', '/') is var p
                && p.Contains("/obj/dotnet-inspect/", StringComparison.Ordinal)
                && p.Contains("/generated/", StringComparison.Ordinal));
        Assert.All(generated, f => Assert.True(File.Exists(f)));

        // The MSBuild half reaches the implicitly imported build files, not just
        // the projects. A `Using` in Directory.Build.props applies to every
        // project beneath it and named no .cs file, so a scan that missed these
        // reported nothing while the CLI compiled the import.
        string[] buildFiles = [.. MsBuildFiles(RepositoryRoot())];
        Assert.Contains(
            buildFiles,
            f => string.Equals(Path.GetFileName(f), "Directory.Build.props", StringComparison.Ordinal));
        Assert.Contains(
            buildFiles,
            f => string.Equals(Path.GetFileName(f), "dotnet-inspect.csproj", StringComparison.Ordinal));

        // ... and the source half reaches files the SDK glob does not name.
        Assert.Contains(
            CliSourceFiles(RepositoryRoot()),
            f => string.Equals(Path.GetFileName(f), "CommandError.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void CommandError_IsTheOnlyWriterOfTheErrorPrefix()
    {
        string root = RepositoryRoot();
        string owner = Path.Combine(root, "src", "dotnet-inspect", "Output", "CommandError.cs");

        Assert.True(File.Exists(owner), $"Expected the owning writer at {owner}.");

        // Every project whose code runs inside this CLI process, derived from
        // the CLI's own transitive ProjectReference closure rather than a
        // hand-kept list. Scoping this to src/dotnet-inspect let
        // ILInspector.Metadata keep composing its own "Error: " into a returned
        // message, which the CLI then prefixed again: "member String -m
        // ToString --index 99" printed "Error: Error: ...". Naming the
        // exclusions instead went the other way and flagged sibling tools
        // (runfaster, mdi, ILInspector.Analysis.App) that have their own entry
        // points and cannot reach this writer at all. The closure is the exact
        // set: a project newly referenced by the CLI is covered automatically,
        // and a separate tool never is.
        //
        // Excluding mdi is mechanically right and substantively a gap: it reads
        // the same untrusted metadata and renders it uncontained. That is
        // tracked as its own issue (#3444), not silently inherited from here.
        string[] products = [.. ProjectClosure(Path.Combine(root, "src", "dotnet-inspect", "dotnet-inspect.csproj"))
            .Select(Path.GetDirectoryName)
            .OfType<string>()];

        // A broken closure would shrink to the CLI alone and pass vacuously,
        // which is exactly the failure this rule already had once.
        string[] names = [.. products.Select(Path.GetFileName).OfType<string>()];
        Assert.Contains("dotnet-inspect", names);
        Assert.Contains("ILInspector.Metadata", names);
        Assert.Contains("DotnetInspector.Services", names);
        Assert.DoesNotContain("mdi", names);
        Assert.DoesNotContain("runfaster", names);

        // The same set of files its sibling reads, for the same reason: a
        // directory glob is the SDK's default, not the compiler's input, and
        // this rule used the glob while the stream rule had already stopped.
        // src/UnionPolyfill.cs is compiled into the CLI through a Compile item
        // today and was scanned for stderr writes but never for a prefix.
        List<string> offenders = [];
        foreach (string path in CliSourceFiles(root))
        {
            if (string.Equals(path, owner, StringComparison.Ordinal))
            {
                continue;
            }

            // Read as the compiler reads it rather than as lines of text. The
            // line version skipped any line whose first non-space characters
            // were `//`, so a prefix after a block comment, or under a `#if`,
            // was outside the rule; blanking comments and disabled regions
            // makes that a property of the code rather than of the layout.
            string source = File.ReadAllText(path);
            foreach (Reading reading in CodeReadings(source))
            {
                string[] lines = reading.Text.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    if (SeverityLiteral.IsMatch(lines[i]))
                    {
                        Add($"{Path.GetRelativePath(root, path)}:{i + 1}: {lines[i].Trim()}");
                    }
                }

                foreach (Match match in ComposedPrefixWrite.Matches(reading.Text))
                {
                    int line = reading.Text.Take(match.Index).Count(c => c == '\n') + 1;
                    Add($"{Path.GetRelativePath(root, path)}:{line}: {match.Value.Replace('\n', ' ').Trim()}");
                }
            }
        }

        void Add(string offender)
        {
            if (!offenders.Contains(offender))
            {
                offenders.Add(offender);
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A severity prefix must only be written by CommandError, which contains the message. "
                + $"Replace these with CommandError.Write/WriteWarning/WriteNote:{Environment.NewLine}"
                + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Guards the scan itself: a regex that matched nothing anywhere would let
    /// the test above pass vacuously forever.
    /// </summary>
    [Fact]
    public void Scan_MatchesTheShapeItIsMeantToCatch()
    {
        Assert.Matches(SeverityLiteral, "            Console.Error.WriteLine(\"Error: plain.\");");
        Assert.Matches(SeverityLiteral, "Console.Error.WriteLine($\"Error: {value} interpolated.\");");

        // The wrapped call: the literal is still on one line.
        Assert.Matches(SeverityLiteral, "                    $\"Error: wrapped {value}.\");");

        // The aliased writer, and the sink that is not a writer at all.
        Assert.Matches(SeverityLiteral, "            error.WriteLine($\"Warning: {count} signatures.\");");
        Assert.Matches(SeverityLiteral, "                logger.Log($\"Warning: Could not read {skippedPath}\");");

        // A returned message, not a write, and a lowercase prefix: both real.
        Assert.Matches(SeverityLiteral, "                $\"Error: No members matched selector '{text}'.\",");
        Assert.Matches(SeverityLiteral, "                var msg = $\"warning: {kind} '{name}' not found\";");

        Assert.DoesNotMatch(SeverityLiteral, "Console.Error.WriteLine(\"Errors: not a prefix.\");");
        Assert.DoesNotMatch(SeverityLiteral, "CommandError.Write($\"{message}\");");

        Assert.Matches(ComposedPrefixWrite, "Console.Error.WriteLine($\"{prefix}: Select value '{v}' not found.\");");
        Assert.DoesNotMatch(ComposedPrefixWrite, "Console.Error.WriteLine($\"  {suggestion}\");");
        Assert.DoesNotMatch(ComposedPrefixWrite, "Console.Error.WriteLine($\"{count} rows.\");");
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find repository root containing dotnet-inspect.slnx.");
    }

    /// <summary>
    /// Whether <paramref name="source"/>, read in every configuration, imports
    /// <c>System.Console</c>.
    /// </summary>
    private static bool ImportsConsole(string source) =>
        CodeReadings(source).Any(r => ConsoleImports(r.Tree).Any());

    /// <summary>
    /// Whether <paramref name="xml"/>, a single MSBuild element, declares one.
    /// </summary>
    private static bool DeclaresConsoleImport(string xml) => DeclaresConsoleImport(XElement.Parse(xml));


    /// <summary>
    /// The same rule as <see cref="CommandError_IsTheOnlyWriterOfStderr"/>, read
    /// from the IL that ships rather than from the source that produced it.
    /// </summary>
    /// <remarks>
    /// A source scan can only be as good as its reading of C#, and seven
    /// consecutive rounds of this review were spent on the gap between that
    /// reading and the compiler's. This rule has no such gap: an alias, an
    /// escape, a verbatim identifier, an interpolation hole, a preprocessor
    /// branch, a source generator, and a file contributed by a build file all
    /// produce the same <c>call System.Console::get_Error</c>, and that is what
    /// is matched.
    ///
    /// It is scoped to the assemblies built from the CLI's own project closure,
    /// which is the scope its sibling has. A third-party dependency writing to
    /// stderr is a different question with a different answer, and pretending
    /// this rule covers it would be worse than saying it does not.
    ///
    /// The accounted set is the same five sites the source rule accounts for,
    /// plus the owner. Both are pinned because either could go stale alone.
    /// </remarks>
    [Fact]
    public void CompiledIl_ReachesStderrOnlyWhereAccountedFor()
    {
        string root = RepositoryRoot();
        List<string> found = [];

        foreach (string project in ProjectClosure(
            Path.Combine(root, "src", "dotnet-inspect", "dotnet-inspect.csproj")))
        {
            string target = EvaluatedProperty(project, "TargetPath", "Release");

            // A missing assembly is an unavailable observation, not a clean one.
            Assert.True(
                File.Exists(target),
                $"{Path.GetRelativePath(root, project)} is in the CLI's closure but {target} does not exist. "
                    + "Build the solution in Release before running this rule; it reads the IL that ships.");

            found.AddRange(ConsoleErrorReferences(target));
        }

        // Counted, not listed. A method is not a fine enough identity on its
        // own: `Program.<Main>$` is every top-level statement in the CLI and is
        // accounted for, so a set-valued pin called a new raw write in it
        // accounted too. Five tampers proved that -- a plain write, a method
        // group, OpenStandardError, and SetError all landed in an accounted
        // method and the rule stayed green.
        Dictionary<string, int> accounted = new(StringComparer.Ordinal)
        {
            // The owner, which contains every string before it writes it. Four
            // methods rather than one, because the stream is fetched at each.
            ["dotnet-inspect!DotnetInspector.Output.CommandError.WriteDiagnostic"] = 1,
            ["dotnet-inspect!DotnetInspector.Output.CommandError.WriteDetail"] = 1,
            ["dotnet-inspect!DotnetInspector.Output.CommandError.WriteLine"] = 1,
            ["dotnet-inspect!DotnetInspector.Output.CommandError.WriteBlankLine"] = 1,

            // Markout views of the tips and the legend; every field of both
            // rows is contained where the row is built.
            ["dotnet-inspect!DotnetInspector.Output.Hints.WriteTips"] = 1,
            ["dotnet-inspect!DotnetInspector.Output.Hints.WriteLegend"] = 1,

            // --info and --trace-mermaid, both in top-level code, so IL sees one
            // method where the source rule sees two statements. The source rule
            // is the one that tells them apart; this one is why they are here.
            ["dotnet-inspect!Program.<Main>$"] = 2,

            // The network traffic log. Its caller is behind #if DEBUG, but this
            // method is not: it is public API and the reference to the stream is
            // in the shipped assembly, which is a thing only this rule can say.
            // Its consumer takes containment as a required constructor
            // parameter, and the logged URL carries the package id from argv.
            ["DotnetInspector.Core!DotnetInspector.Core.HttpClientFactory.EnableNetworkTrafficLogging"] = 1,
        };

        Assert.Equal(
            accounted,
            found.GroupBy(f => f, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal));
    }

}
