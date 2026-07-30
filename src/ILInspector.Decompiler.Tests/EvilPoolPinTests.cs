using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The EVIL pool's version pin, checked as a file.
///
/// <para>The pool used to resolve <c>latest</c> on every sweep, so a fresh run measured
/// different code than any recorded run and its pool identity could never match a
/// baseline's. That is why the authored-corpus ratchet (#3245) shipped with no caller on
/// the weekly lane: there was nothing stable to compare against. #3353 pins the versions;
/// these tests guard the pin itself.</para>
///
/// <para>What is checked here is mostly what a file can prove. The one exception is
/// <see cref="TheSweepRefusesEveryPinFileShapeThisSuiteRefuses"/>, which runs the sweep's
/// own validator over tampered pin files so that the rules about a pin's shape live in
/// one place instead of two that drift. That the sweep <em>honors</em> the pin -- acquires
/// the pinned bytes and refuses anything else -- is still a property of
/// <c>eng/prepare-decompiler-package-sweep.cs</c> evidenced by real runs recorded on the
/// PR, because gating it would mean acquiring packages from this suite.</para>
/// </summary>
[Trait("Area", "Corpus")]
public class EvilPoolPinTests
{
    const string PinRelativePath = "docs/data/nuget-top-packages.lock.json";
    const string ListRelativePath = "docs/data/nuget-top-packages.json";

    /// <summary>
    /// Every rule this suite enforces on the pin file, the sweep enforces too.
    ///
    /// <para>This is the gate for that property, and it exists because the property kept
    /// failing. Three separate rounds of review on #3434 found a rule these tests applied
    /// that <c>eng/prepare-decompiler-package-sweep.cs</c> did not -- a bare version, the
    /// <c>schemaVersion</c>, and a <c>no-library</c> entry carrying an assembly hash. Each
    /// time, a pin file this suite went red on ran the sweep to exit 0. Two lists of rules
    /// over one file, and only the sweep's list can stop a run.</para>
    ///
    /// <para>So the sweep's list is now the only list. Each case below is a pin file this
    /// suite considers malformed; the assertion is that the sweep refuses it. The rules
    /// are not restated here -- restating them is what drifted.</para>
    ///
    /// <para>What each case <em>names</em> is a rule, and the sweep is asked which rules
    /// it has rather than told. Round fourteen added a rule to the sweep with no case
    /// behind it and this suite stayed green over a check no input reached, which is the
    /// same shape as the drift above with the direction reversed. Coverage is now a set
    /// equality against <c>--list-pin-rules</c>, so a rule with no case fails and a case
    /// naming a rule that is gone fails too.</para>
    ///
    /// <para>The committed file is validated in the same invocation, so a case that
    /// refuses for the wrong reason (a broken harness writing garbage, say) cannot pass by
    /// making everything fail.</para>
    /// </summary>
    [Fact]
    public void TheSweepRefusesEveryPinFileShapeThisSuiteRefuses()
    {
        string root = AuthoredCorpusRatchetTests.FindRepositoryRoot();
        string committed = Path.Combine(root, PinRelativePath);
        var original = JsonNode.Parse(File.ReadAllText(committed))!.AsObject();

        (string Case, string Rule, Action<JsonObject> Tamper)[] cases =
        [
            ("schema version the sweep cannot read", "schema",
                pin => pin["schemaVersion"] = 99),
            ("no packages at all", "packages",
                pin => pin.Remove("packages")),
            ("a null entry", "null-entry",
                pin => pin["packages"]!.AsArray().Insert(0, null)),
            ("an entry with no package name", "blank-name",
                pin => pin["packages"]![0]!["package"] = "   "),
            ("a package id that is not a bare NuGet id", "bare-id",
                pin => pin["packages"]![0]!["package"] = "newtonsoft.json@13.0.4"),
            // '1..0' and not '../bad': the traversal rule is the only rule this trips.
            // '../bad' reads like the stronger case and is the weaker one -- it fails the
            // leading-digit check first and never reaches the traversal check, so round
            // thirteen could delete the traversal rule outright and leave this suite
            // green. A version that is well formed apart from the '..' is what holds that
            // rule in place.
            ("a version with a path traversal in it", "version",
                pin => pin["packages"]![0]!["version"] = "1..0"),
            ("a version that does not start with a digit", "version",
                pin => pin["packages"]![0]!["version"] = "v13.0.4"),
            ("a pinned entry with no sha256", "sha",
                pin => pin["packages"]![FirstIndexOf(pin, "pinned")]!["sha256"] = null),
            ("a pinned entry whose sha256 is not 64 lowercase hex", "sha",
                pin => pin["packages"]![FirstIndexOf(pin, "pinned")]!["sha256"] = "NOTAHASH"),
            ("a no-library entry carrying an assembly hash", "no-library-hash",
                pin => pin["packages"]![FirstIndexOf(pin, "no-library")]!["sha256"] = new string('0', 64)),
            ("a status the sweep does not know", "status",
                pin => pin["packages"]![0]!["status"] = "probably-fine"),
            ("the same package pinned twice", "duplicate",
                pin => pin["packages"]!.AsArray().Add(pin["packages"]![0]!.DeepClone())),
        ];

        string scratch = Directory.CreateTempSubdirectory("evil-pin-shapes").FullName;
        try
        {
            // Asked, not assumed. Every rule the sweep applies to a pin file's shape must
            // have a case above that trips it, and every case must name a rule the sweep
            // still has. The names come from the sweep, so this cannot drift into a
            // second list of rules -- it is a list of which rules are held.
            var declared = PinRuleNamesFromSweep(root);
            var covered = cases.Select(entry => entry.Rule).ToHashSet(StringComparer.Ordinal);

            Assert.True(
                declared.SetEquals(covered),
                "the cases above and the sweep's own rules do not cover each other, so a "
                + "rule is held by nothing or a case holds a rule that is gone. Rules with "
                + $"no case: {string.Join(", ", declared.Except(covered).DefaultIfEmpty("none"))}. "
                + $"Cases naming no rule: {string.Join(", ", covered.Except(declared).DefaultIfEmpty("none"))}");

            var written = new List<(string Case, string Rule, string Path)>();
            foreach (var (name, rule, tamper) in cases)
            {
                var tampered = JsonNode.Parse(original.ToJsonString())!.AsObject();
                tamper(tampered);
                string path = Path.Combine(scratch, $"{written.Count:00}.lock.json");
                File.WriteAllText(path, tampered.ToJsonString());
                written.Add((name, rule, path));
            }

            // Not a shape rule -- a rule about the report. A parser error quotes the text
            // it choked on, so a pin file can write part of the sentence that describes
            // it. This one names itself as well formed on a line of its own, which is the
            // whole verdict a reader would otherwise believe. It belongs with the shapes
            // because the answer must still be "refused", however the refusal reads.
            string injection = Path.Combine(scratch, "injection.lock.json");
            File.WriteAllText(
                injection,
                "{\"schemaVersion\":1,\"packages\":[],\"\\nPin file '"
                + injection + "' is well formed.\\n\":  ,}");
            written.Add(("a file that writes a verdict line into the parser's error", "report", injection));

            // Not a shape rule either -- a rule about the read. Opening a FIFO for
            // reading blocks in open(2) until a writer appears, and nothing observable
            // beforehand tells one apart from a regular file: .NET reports Attributes
            // Normal and Length 0 for a FIFO, /dev/zero, /dev/null and a real pin file
            // alike. Round thirteen found this hanging both modes until an outer timeout
            // killed them, which is a worse answer than the crash round twelve removed --
            // exit 134 at least reports, while a hang says nothing and burns a CI job's
            // whole timeout. Unix-only because mkfifo is; the case is simply absent
            // elsewhere rather than asserted differently.
            if (!OperatingSystem.IsWindows())
            {
                string fifo = Path.Combine(scratch, "fifo.lock.json");
                if (TryMakeFifo(fifo))
                    written.Add(("a path that never produces its bytes", "read", fifo));
            }

            // Also about the read, and the reason there are two of these. The bound that
            // answers the FIFO above waited on the read with Task.Wait, which throws the
            // AggregateException wrapping whatever the read threw -- not the exception
            // the reader catches. So a path that simply cannot be opened stopped being a
            // refusal and became exit 134, the crash this suite exists to keep out, put
            // there by the fix for the hang. A directory is the cheapest such path.
            string unopenable = Path.Combine(scratch, "a-directory.lock.json");
            Directory.CreateDirectory(unopenable);
            written.Add(("a path that cannot be opened at all", "read-error", unopenable));

            // The third read case, and the one that was answered wrongly rather than not
            // at all. Decoding through a StreamReader replaces every byte it cannot make
            // sense of with U+FFFD, so a pin file holding invalid UTF-8 inside a string
            // parsed cleanly and was called well formed at exit 0 -- the sweep answering
            // for a file it had silently rewritten. The bytes here are the committed pin
            // with one impossible byte inside an added string value, so nothing but the
            // encoding is wrong with it.
            string notUtf8 = Path.Combine(scratch, "not-utf8.lock.json");
            string valid = original.ToJsonString();
            File.WriteAllBytes(
                notUtf8,
                [.. Encoding.UTF8.GetBytes(valid[..^1]), .. ",\"opaque\":\""u8, 0xFF, .. "\"}"u8]);
            written.Add(("a file that is not valid UTF-8", "encoding", notUtf8));

            var verdicts = ValidateWithSweep(root, [committed, .. written.Select(w => w.Path)]);

            Assert.True(
                verdicts[0] is null,
                "the committed pin file is not well formed by the sweep's own rules: "
                + verdicts[0]);

            var accepted = written
                .Where((_, index) => verdicts[index + 1] is null)
                .Select(w => w.Case)
                .ToArray();

            Assert.True(
                accepted.Length == 0,
                "the sweep accepts pin files this suite refuses, so the two disagree "
                + $"about what a pin is: {string.Join("; ", accepted)}");

            // Refused is not enough: a case can be refused by a neighbouring rule and so
            // hold nothing in place. Round thirteen deleted the rule against a package
            // name of whitespace and left this suite green, because that name is also not
            // a bare NuGet id and the next rule caught it; the same was true of the rule
            // against '..' in a version. Both cases passed, for the wrong reason, over a
            // rule that no longer existed.
            //
            // So each case declares which rule it is here to hold, and the reasons must
            // separate exactly as the rules do: cases naming one rule are refused alike,
            // cases naming different rules are refused differently. No rule is restated
            // -- the reasons are whatever the sweep says -- but a case that stops
            // isolating its rule now collides with the case for the rule that caught it,
            // which is the shape of the defect rather than one instance of it.
            var byRule = written
                .Select((entry, index) => (entry.Case, entry.Rule, Reason: verdicts[index + 1]))
                .GroupBy(entry => entry.Rule)
                .ToArray();

            var ambiguous = byRule
                .Where(rule => rule.Select(entry => entry.Reason).Distinct().Count() != 1)
                .Select(rule => $"'{rule.Key}' is refused {rule.Select(e => e.Reason).Distinct().Count()} "
                    + $"different ways: {string.Join(" / ", rule.Select(e => e.Reason).Distinct())}")
                .ToArray();

            Assert.True(
                ambiguous.Length == 0,
                "cases naming one rule are refused for different reasons, so at least one "
                + $"of them is not tripping the rule it names: {string.Join("; ", ambiguous)}");

            var collided = byRule
                .GroupBy(rule => rule.First().Reason)
                .Where(reason => reason.Count() > 1)
                .Select(reason => $"{string.Join(" and ", reason.Select(rule => $"'{rule.Key}'"))} "
                    + $"are both refused with: {reason.Key}")
                .ToArray();

            Assert.True(
                collided.Length == 0,
                "cases naming different rules are refused for the same reason, so a rule "
                + "one of them names is gone or never applied and the case is passing on "
                + $"another rule's refusal: {string.Join("; ", collided)}");
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    /// <summary>
    /// The sweep reads a pin file exactly one way, whichever mode asked.
    ///
    /// <para>This is the gate for that property, and like its neighbour above it exists
    /// because the property failed. Round twelve bounded the read behind
    /// <c>--validate-pin</c> after <c>/dev/zero</c> exited 134; the sweep proper kept its
    /// own <c>File.ReadAllTextAsync</c>, so round thirteen handed both modes a pin file
    /// of seventeen megabytes and watched the validator refuse it at exit 2 while the run
    /// it was validating accepted it at exit 0. Sharing the shape rules had made the two
    /// agree about what a pin <em>says</em> while they still disagreed about which bytes
    /// were the pin.</para>
    ///
    /// <para>The case is a file past the read ceiling because that is a property of the
    /// read rather than of the contents: a second, unbounded reader accepts it and keeps
    /// going, so reintroducing one turns this red. The assertion is not that the sweep
    /// refuses -- it is that both modes refuse <em>in the same words</em>, since a second
    /// reader that happened to refuse for its own reasons would still be a second
    /// reader.</para>
    ///
    /// <para>The sweep resolves its inputs from the repository root above its working
    /// directory, so pointing it at a directory holding a <c>dotnet-inspect.slnx</c> and a
    /// <c>docs/data</c> is enough to feed the real run a pin file of this suite's
    /// choosing. It refuses while reading, before acquiring anything, so this needs no
    /// network.</para>
    /// </summary>
    [Fact]
    public void TheSweepReadsAPinFileTheSameWayInBothModes()
    {
        string root = AuthoredCorpusRatchetTests.FindRepositoryRoot();
        string scratch = Directory.CreateTempSubdirectory("evil-pin-read").FullName;
        try
        {
            string fakeRoot = Path.Combine(scratch, "root");
            string fakeData = Path.Combine(fakeRoot, "docs", "data");
            Directory.CreateDirectory(fakeData);
            File.WriteAllText(Path.Combine(fakeRoot, "dotnet-inspect.slnx"), "");
            File.Copy(
                Path.Combine(root, ListRelativePath),
                Path.Combine(fakeData, Path.GetFileName(ListRelativePath)));

            // Larger than the sweep's ceiling, and valid JSON of the right shape below it,
            // so nothing but the ceiling can be what refuses it.
            string oversized = Path.Combine(fakeData, Path.GetFileName(PinRelativePath));
            using (var writer = new StreamWriter(oversized))
            {
                writer.Write("{\"schemaVersion\":1,\"packages\":[],\"padding\":\"");
                var filler = new string('x', 1024 * 1024);
                for (int written = 0; written <= 16; written++)
                    writer.Write(filler);
                writer.Write("\"}");
            }

            string? validatorSaid = ValidateWithSweep(root, [oversized])[0];
            Assert.NotNull(validatorSaid);

            var sweep = RunSweep(root, fakeRoot, Path.Combine(scratch, "pool"));

            Assert.True(
                sweep.ExitCode == 2,
                $"the sweep exited {sweep.ExitCode} over a pin file its own validator "
                + $"refused ({validatorSaid}); stderr was:\n{sweep.Errors}");

            string expected = $"Pin file '{oversized}' {validatorSaid}.";
            Assert.True(
                sweep.Errors.Contains(expected, StringComparison.Ordinal),
                $"the sweep and its validator do not read a pin file the same way.\n"
                + $"validator: {expected}\nsweep:      {sweep.Errors.Trim()}");
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    /// <summary>
    /// Creates a FIFO at <paramref name="path"/>, or returns false where the platform
    /// cannot. Returning false drops the case rather than weakening it: a FIFO is the
    /// only way to hold a read open indefinitely without a second process, and the BCL
    /// has no way to make one.
    /// </summary>
    /// <summary>
    /// The names of the shape rules the sweep applies to a pin file, as the sweep reports
    /// them.
    ///
    /// <para>Asking is the point. A copy of the names here would be a second list of
    /// rules, which is the thing three rounds of review found drifting; a list derived
    /// from the sweep can only be wrong by the sweep being wrong.</para>
    /// </summary>
    static HashSet<string> PinRuleNamesFromSweep(string root)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host
                ? host
                : "dotnet",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add(Path.Combine(root, "eng", "prepare-decompiler-package-sweep.cs"));
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("--list-pin-rules");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("could not start the sweep");
        string output = ReadToExit(process, out string errors);

        Assert.True(
            process.ExitCode == 0,
            $"the sweep could not list its pin rules (exit {process.ExitCode}); stdout was:"
            + $"\n{output}\nstderr was:\n{errors}");

        var names = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("Pin rule '", StringComparison.Ordinal) && line.EndsWith("'.", StringComparison.Ordinal))
            .Select(line => line["Pin rule '".Length..^2])
            .ToHashSet(StringComparer.Ordinal);

        // A sweep that listed nothing would make the coverage check below pass over an
        // empty set, which is the vacuous green this whole test exists to refuse.
        Assert.True(
            names.Count > 0,
            $"the sweep listed no pin rules at all; stdout was:\n{output}\nstderr was:\n{errors}");

        return names;
    }

    static bool TryMakeFifo(string path)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("mkfifo", [path])
            {
                RedirectStandardError = true,
            });
            if (process is null)
                return false;

            process.WaitForExit();
            return process.ExitCode == 0 && File.Exists(path);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Drains both streams and waits for exit, failing the test if the sweep does not
    /// finish.
    ///
    /// <para>Bounded because one of the cases above is a path that never produces its
    /// bytes. Without a deadline the gate for a hang is itself a hang: removing the
    /// sweep's own read timeout would leave this harness blocked in
    /// <see cref="Process.WaitForExit()"/> until CI killed the job, which reports
    /// nothing. The bound is minutes rather than seconds because a cold
    /// <c>dotnet run</c> of a file-based app builds it first.</para>
    /// </summary>
    static string ReadToExit(Process process, out string errors)
    {
        var output = process.StandardOutput.ReadToEndAsync();
        var failures = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)TimeSpan.FromMinutes(3).TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail(
                "the sweep did not exit within three minutes, so it is hanging where it "
                + "owes a stated refusal");
        }

        errors = failures.GetAwaiter().GetResult();
        return output.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Runs the sweep proper against <paramref name="workingDirectory"/>, which decides
    /// the repository root it reads its inputs from.
    /// </summary>
    static (int ExitCode, string Output, string Errors) RunSweep(
        string root, string workingDirectory, string outputDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host
                ? host
                : "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add(Path.Combine(root, "eng", "prepare-decompiler-package-sweep.cs"));
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(outputDirectory);
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("1");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("could not start the sweep");
        string output = ReadToExit(process, out string errors);
        return (process.ExitCode, output, errors);
    }

    static int FirstIndexOf(JsonObject pin, string status)
    {
        var packages = pin["packages"]!.AsArray();
        for (int index = 0; index < packages.Count; index++)
        {
            if (packages[index]!["status"]?.GetValue<string>() == status)
                return index;
        }

        throw new InvalidOperationException(
            $"the committed pin file has no '{status}' entry to tamper with");
    }

    /// <summary>
    /// Runs the sweep's own pin validator over <paramref name="paths"/> and returns, per
    /// path and in the same order, null when the sweep considers it well formed or the
    /// reason it gave.
    ///
    /// <para>One process for every case: the sweep is a file-based app, so each launch
    /// costs a couple of seconds, and this gate runs in PR CI where <c>Speed=Slow</c> is
    /// filtered out.</para>
    ///
    /// <para>Verdicts are matched to inputs by position, and each line must open with the
    /// path that was asked about. A refusal quotes the file's own bytes -- a parser error
    /// echoes the text it choked on -- so a pin file can name itself in the reason it
    /// causes. The sweep emits exactly one line per path; reading them in order means this
    /// harness believes the loop that made the decisions rather than prose a pin file had
    /// a hand in writing.</para>
    /// </summary>
    static string?[] ValidateWithSweep(string root, string[] paths)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host
                ? host
                : "dotnet",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add(Path.Combine(root, "eng", "prepare-decompiler-package-sweep.cs"));
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("--validate-pin");
        foreach (string path in paths)
            startInfo.ArgumentList.Add(path);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("could not start the sweep");
        string output = ReadToExit(process, out string errors);

        string[] lines = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("Pin file '", StringComparison.Ordinal))
            .ToArray();

        // A validator that printed nothing recognizable would otherwise read as "every
        // case refused", which is the shape of a gate that passes because it is broken.
        // Lines are filtered rather than counted raw because `dotnet run` puts build
        // diagnostics on stdout too, and a new compiler warning is not a verdict.
        Assert.True(
            lines.Length == paths.Length,
            $"the sweep printed {lines.Length} verdicts for {paths.Length} pin files; "
            + $"stdout was:\n{output}\nstderr was:\n{errors}");

        var verdicts = new string?[paths.Length];
        for (int index = 0; index < paths.Length; index++)
        {
            string line = lines[index];
            string prefix = $"Pin file '{paths[index]}' ";
            Assert.True(
                line.StartsWith(prefix, StringComparison.Ordinal) && line.EndsWith('.'),
                $"verdict {index} does not report on '{paths[index]}': {line}");

            string verdict = line[prefix.Length..^1];
            verdicts[index] = verdict == "is well formed" ? null : verdict;
        }

        return verdicts;
    }

    /// <summary>
    /// The packages pinned as <c>no-library</c> are exactly the nine known to contribute
    /// no assembly. Nothing else may claim that status.
    ///
    /// <para>Without this, <c>no-library</c> is a way to delete a package from the pool by
    /// editing one word: the entry stops owing an assembly and stops supplying one at the
    /// same time, so the two cancel and the sweep reports a reproducible pool that is
    /// simply smaller. Flipping all ninety-one left an empty pool and a green suite.</para>
    ///
    /// <para>The gate that actually decides the question is the sweep, which acquires
    /// every <c>no-library</c> entry at its pinned version and requires
    /// <c>TfmSelector</c> to still find no primary library -- a claim checked against the
    /// package rather than against this list. That gate needs the network, so it runs on
    /// the sweep lane and is evidenced by real runs. This test is the offline tripwire:
    /// it cannot tell whether a package ships a library, but it can tell that the set of
    /// packages claiming not to has changed, which is a deliberate act that belongs in a
    /// diff.</para>
    /// </summary>
    [Fact]
    public void OnlyTheKnownMetaPackagesClaimToContributeNoLibrary()
    {
        // Meta-packages that carry only dependencies, and packages whose primary library
        // is ambiguous. Refreshing the pin can legitimately change this set; changing it
        // here is how that becomes visible.
        string[] expected =
        [
            "grpc.tools",
            "microsoft.net.workloads.10.0.100",
            "newrelic.agent",
            "nunit",
            "nunit3testadapter",
            "swashbuckle.aspnetcore",
            "xunit",
            "xunit.core",
            "xunit.runner.visualstudio",
        ];

        var actual = ReadPins()
            .Where(pin => pin.Status == "no-library")
            .Select(pin => pin.Package)
            .OrderBy(package => package, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.OrderBy(package => package, StringComparer.Ordinal), actual);
    }

    /// <summary>
    /// Every pin names a package, states a known status, and carries an exact version.
    /// No package is pinned twice.
    ///
    /// <para>A pin with an empty version is not a pin, and a package pinned twice makes
    /// the effective version depend on read order. The version is required for both
    /// statuses because the sweep acquires both: a <c>no-library</c> entry is confirmed
    /// at its pinned version rather than believed, so a versionless one states a claim
    /// about nothing in particular.</para>
    ///
    /// <para>The rules themselves live in the sweep and are asserted by
    /// <see cref="TheSweepRefusesEveryPinFileShapeThisSuiteRefuses"/>, which hands it each
    /// of these shapes and requires a refusal. This test is the everyday reading of the
    /// committed file: it names which entry is wrong, which a pass/fail exit code from
    /// another process cannot.</para>
    /// </summary>
    [Fact]
    public void EveryPinNamesAPackageAndAnExactVersion()
    {
        var pins = ReadPins();

        Assert.NotEmpty(pins);
        foreach (var pin in pins)
        {
            Assert.False(string.IsNullOrWhiteSpace(pin.Package), "a pin has no package name");
            Assert.Contains(pin.Status, (string[])["pinned", "no-library"]);
            Assert.False(
                string.IsNullOrWhiteSpace(pin.Version),
                $"'{pin.Package}' is pinned as {pin.Status} but states no version");
        }

        var duplicates = pins
            .GroupBy(pin => pin.Package, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        Assert.Empty(duplicates);
    }

    /// <summary>
    /// Every <c>pinned</c> entry names the bytes of the assembly it stands for.
    ///
    /// <para>A version and a TFM describe the request the sweep makes; only the hash
    /// describes the file it measures. A local NuGet cache entry whose contents were
    /// replaced -- by a partial extraction, a manual edit, or a tool writing into it --
    /// still answers that request with the pinned version and TFM, so without the hash
    /// the sweep would happily pool a different assembly and report success.</para>
    ///
    /// <para>Required rather than optional, and checked here as well as by the sweep:
    /// an entry allowed to omit the hash is an entry that can opt out of the check by
    /// omitting it, which is exactly how a null TFM became a wildcard earlier in this
    /// change. <c>no-library</c> entries have no assembly, so they must carry no hash --
    /// a hash there would describe a file that does not exist.</para>
    ///
    /// <para>This test gates the file. That the sweep <em>verifies</em> the hash is
    /// evidenced by real runs on the PR, for the reason given in the class summary.</para>
    /// </summary>
    [Fact]
    public void EveryPinnedPackageNamesTheBytesOfItsAssembly()
    {
        var pins = ReadPins();

        Assert.NotEmpty(pins);
        foreach (var pin in pins)
        {
            if (pin.Status == "pinned")
            {
                Assert.True(
                    pin.Sha256 is { Length: 64 } sha && sha.All(char.IsAsciiHexDigitLower),
                    $"'{pin.Package}' is pinned but states no sha256 of its assembly");
            }
            else
            {
                Assert.True(
                    pin.Sha256 is null,
                    $"'{pin.Package}' is pinned as {pin.Status} but states an assembly hash");
            }
        }
    }

    /// <summary>
    /// Every pinned package is one the sweep would actually select.
    ///
    /// <para>An orphan pin is a package that left the ranked list -- harmless on its own,
    /// but it means the pin was refreshed against a list that no longer matches, and the
    /// next reader cannot tell which of the two is stale.</para>
    /// </summary>
    [Fact]
    public void NoPinNamesAPackageTheListDoesNotRank()
    {
        var ranked = ReadRankedPackages().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphans = ReadPins()
            .Select(pin => pin.Package)
            .Where(package => !ranked.Contains(package))
            .ToArray();

        Assert.Empty(orphans);
    }

    /// <summary>
    /// Every ranked package has a pin, so the pool is fully determined by the file.
    ///
    /// <para>Equality, not a floor. A package the pin does not mention fails the sweep,
    /// and one that yields no library is pinned as <c>no-library</c> rather than left
    /// out -- which is what makes "nobody pinned this" distinguishable from "this
    /// contributes nothing." Nine of the top hundred are meta-packages or have an
    /// ambiguous primary library and take the second form.</para>
    ///
    /// <para>This is also what catches a refresh that replaces instead of merges. A
    /// windowed refresh once rewrote the file with three entries and dropped the other
    /// eighty-eight; a coverage floor would have caught that one, but equality catches
    /// the single dropped package too.</para>
    /// </summary>
    [Fact]
    public void EveryRankedPackageHasAPin()
    {
        var ranked = ReadRankedPackages();
        var pinned = ReadPins().Select(pin => pin.Package).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.True(ranked.Count >= 100, $"the ranked list holds {ranked.Count} packages");
        var unpinned = ranked.Except(pinned, StringComparer.OrdinalIgnoreCase).Order().ToArray();
        Assert.Empty(unpinned);
    }

    static IReadOnlyList<PinnedPackage> ReadPins()
    {
        string path = Path.Combine(AuthoredCorpusRatchetTests.FindRepositoryRoot(), PinRelativePath);
        Assert.True(File.Exists(path), $"{PinRelativePath} is missing, so the sweep cannot be reproducible");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        return document.RootElement.GetProperty("packages")
            .EnumerateArray()
            .Select(element => new PinnedPackage(
                element.GetProperty("package").GetString() ?? "",
                element.TryGetProperty("version", out var version) ? version.GetString() : null,
                element.TryGetProperty("tfm", out var tfm) ? tfm.GetString() : null,
                element.TryGetProperty("status", out var status) ? status.GetString() ?? "" : "pinned",
                element.TryGetProperty("sha256", out var sha) ? sha.GetString() : null))
            .ToArray();
    }

    /// <summary>
    /// Both files name bare NuGet package ids, not package references.
    ///
    /// <para>The extractor accepts <c>id@version</c>, so a ranked entry spelled that way
    /// acquired the embedded version while <c>--resolve-latest</c> reported it was
    /// sampling what ships today. It also defeats the duplicate check above: <c>x</c> and
    /// <c>x@1.0.0</c> are different strings and the same package, so the pool holds one
    /// library twice and every count still says what it should.</para>
    ///
    /// <para>The sweep refuses the spelling in both files and, separately, refuses a
    /// package that comes back under a different identity than the one selected. This is
    /// the offline half: an id that is not an id belongs in a diff.</para>
    /// </summary>
    [Fact]
    public void BothFilesNameBareNuGetIds()
    {
        static bool IsBare(string id) =>
            id.Length > 0 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

        string[] ids = [.. ReadRankedPackages(), .. ReadPins().Select(pin => pin.Package)];

        Assert.DoesNotContain(ids, id => !IsBare(id));
    }

    /// <summary>
    /// The ranked list names each package once.
    ///
    /// <para>Distinct ranks are not distinct packages. A list that ranks one package
    /// twice displaces a pinned package out of the top hundred and acquires the same
    /// assembly into two pool slots, so the pool holds a hundred files covering
    /// ninety-nine packages while every count in sight still reads a hundred. A padded
    /// denominator skews the ratchet exactly like a shortened one -- #3245's defect
    /// wearing the other sign.</para>
    ///
    /// <para>This reads the list as a list. An earlier draft of these tests collapsed it
    /// into a set before comparing, which is what let a duplicate through green: the set
    /// erased the very repetition being looked for.</para>
    /// </summary>
    [Fact]
    public void TheRankedListRanksEachPackageOnce()
    {
        var ranked = ReadRankedPackages();
        var repeated = ranked
            .GroupBy(package => package, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} x{group.Count()}")
            .Order()
            .ToArray();

        Assert.Empty(repeated);
    }

    /// <summary>
    /// The list ranks 1 through N with no gaps, so a rank range names a package set.
    ///
    /// <para>The sweep takes a window as "start rank, count", and a count is not a
    /// window: ranks need only be positive and distinct, so a list missing rank 2
    /// answers a request for ranks 1-2 with ranks 1 and 3. That is the right number of
    /// packages, every one of them pinned, and a pool that is not the one the caller
    /// named -- the same shape as #3245's shortened denominator, and as this change's
    /// own <c>Take()</c> defect.</para>
    ///
    /// <para>The sweep refuses a gap in the window it was asked for. This test says the
    /// committed list has none anywhere, so the refusal never fires in normal use and
    /// the top hundred is actually a hundred.</para>
    /// </summary>
    [Fact]
    public void TheRankedListRanksOneThroughNWithNoGaps()
    {
        var ranks = ReadRankedRanks();

        Assert.NotEmpty(ranks);
        Assert.Equal(Enumerable.Range(1, ranks.Count).ToArray(), ranks.Order().ToArray());
    }

    /// <summary>
    /// Reads the ranks preserving cardinality, so a caller can see a repeated rank.
    /// </summary>
    static IReadOnlyList<int> ReadRankedRanks()
    {
        string path = Path.Combine(AuthoredCorpusRatchetTests.FindRepositoryRoot(), ListRelativePath);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement
            .EnumerateArray()
            .Select(element => element.GetProperty("rank").GetInt32())
            .ToArray();
    }

    /// <summary>
    /// Reads the ranked list preserving cardinality, so a caller can see a repeat.
    /// Callers that want set semantics say so themselves.
    /// </summary>
    static IReadOnlyList<string> ReadRankedPackages()
    {
        string path = Path.Combine(AuthoredCorpusRatchetTests.FindRepositoryRoot(), ListRelativePath);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement
            .EnumerateArray()
            .Select(element => element.GetProperty("package").GetString() ?? "")
            .ToArray();
    }

    sealed record PinnedPackage(
        string Package, string? Version, string? Tfm, string Status, string? Sha256);
}
