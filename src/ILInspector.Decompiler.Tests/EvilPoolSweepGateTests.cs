using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotnetInspector.Packages;
using Xunit;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The two properties the EVIL pool's version pin exists to provide, run rather than read.
///
/// <para><see cref="EvilPoolPinTests"/> gates the pin as a <em>file</em> -- its shape, its
/// encoding, the bounds on reading it, the words the sweep refuses it in. None of that
/// runs a sweep, so neither of the properties the pinning work was done for was watched by
/// anything (#3560):</para>
///
/// <list type="bullet">
/// <item>the sweep pools the exact bytes <c>nuget-top-packages.lock.json</c> names, and
/// fails rather than pool anything else;</item>
/// <item>the assembly copies put those bytes at the right path inside the output
/// directory, without writing through whatever is already there, and every package whose
/// copy fails is counted as failed rather than dropping out of the pool and out of the
/// total owed together.</item>
/// </list>
///
/// <para>Both were evidenced only by probes run by hand and recorded on #3434, which no
/// future change re-runs. That mattered: the two most serious defects sixteen rounds of
/// review found were both here. A pin that described the <em>request</em> rather than the
/// bytes let a poisoned cache entry satisfy it, and <c>File.Copy</c> wrote an assembly
/// through a symlink planted at the destination while the sweep exited 0. Every case below
/// is one of those probes, kept.</para>
///
/// <para>These run a real sweep, and what they isolate is the sweep's own acquisition.
/// It resolves its inputs from the repository root above its working directory, so a
/// directory holding a <c>dotnet-inspect.slnx</c> and a <c>docs/data</c> feeds a real run
/// a one-package list and pin of this suite's choosing -- no product path override is
/// involved. It is pointed at a scratch cache holding one synthetic package and told to
/// stay offline, so <em>it</em> acquires over no network and reads no shared cache, and
/// nothing it writes lands outside the scratch directory. Offline is what keeps that
/// honest: a case that stopped being served from the seeded cache would reach for the
/// network and fail here rather than quietly passing on whatever the machine happened to
/// have.</para>
///
/// <para>The claim stops there, deliberately. Each case launches the sweep with
/// <c>dotnet run</c>, and a file-based app is restored and built before it runs, which
/// reads the ordinary NuGet package cache and may go to the network to fill it. That is
/// true of every build in this repository and of the sweep runs in
/// <see cref="EvilPoolPinTests"/>; it is not something these cases control, so they do
/// not claim a process that opens no socket. What is gated is narrower and is the part
/// that matters: no package <em>the sweep pools</em> can come from anywhere but the
/// fixture seeded below.</para>
/// </summary>
[Trait("Area", "Corpus")]
public class EvilPoolSweepGateTests
{
    const string FixturePackage = "sweep.fixture";
    const string FixtureVersion = "1.0.0";
    const string FixtureTfm = "net8.0";
    const string FixtureAssembly = "Sweep.Fixture.dll";

    /// <summary>
    /// A sweep whose pin matches the cache pools those bytes, at the path the manifest
    /// says, and leaves nothing behind.
    ///
    /// <para>This is the case every other one below is a deviation from. It is also what
    /// stops those cases passing for the wrong reason: a harness that could not produce a
    /// successful sweep at all would report every refusal as correct.</para>
    /// </summary>
    [Fact]
    public void ASweepPoolsTheBytesThePinNames()
    {
        using var world = SweepWorld.Create();

        var sweep = world.Run();

        Assert.True(sweep.ExitCode == 0, world.Explain(sweep, "a pin naming the cached bytes"));

        string pooled = Path.Combine(
            world.OutputDirectory, "packages", $"001-{FixturePackage}", FixtureVersion, FixtureAssembly);
        Assert.True(File.Exists(pooled), $"the sweep exited 0 without pooling '{pooled}'");
        Assert.Equal(world.FixtureSha256, Sha256Of(pooled));

        // The pool is only reproducible if what the sweep reports is what it wrote.
        Assert.Equal(pooled, File.ReadAllText(world.PooledListPath).Trim());
        var entry = world.ReportedEntry(sweep);
        Assert.Equal("selected", entry["Status"]!.GetValue<string>());
        Assert.Equal(world.FixtureSha256, entry["Sha256"]!.GetValue<string>());

        world.AssertNoTemporaryLeftBehind();
    }

    /// <summary>
    /// Bytes the pin does not name are refused, and are not left in the pool.
    ///
    /// <para>The defect this replays: the pin recorded a version and a TFM, which describe
    /// what the sweep <em>asks</em> the cache for, and the cache is a directory anything
    /// can write to. Answering with a different assembly satisfied both. Swapping the
    /// cached file for other bytes here is exactly that -- the request is untouched and
    /// only the answer changes -- so only a check over the bytes themselves can refuse
    /// it.</para>
    ///
    /// <para>Refusing is half of it. A rejected assembly left in the output directory
    /// would be pooled by everything downstream, which reads the directory.</para>
    /// </summary>
    [Fact]
    public void ASweepRefusesBytesThePinDoesNotName()
    {
        using var world = SweepWorld.Create();
        byte[] impostor = [.. world.FixtureBytes, 0];
        File.WriteAllBytes(world.CachedAssemblyPath, impostor);

        var sweep = world.Run();

        Assert.True(sweep.ExitCode == 1, world.Explain(sweep, "a cache answering with other bytes"));
        Assert.Contains(world.FixtureSha256, sweep.Output + sweep.Errors, StringComparison.Ordinal);
        Assert.Contains(Sha256Of(impostor), sweep.Output + sweep.Errors, StringComparison.Ordinal);

        Assert.Empty(PooledAssemblies(world.OutputDirectory));
        world.AssertNoTemporaryLeftBehind();
    }

    /// <summary>
    /// A pin naming a TFM the package does not carry is refused.
    ///
    /// <para>Gated because this check was silently off for a year of the pin's life: the
    /// comparison was guarded by <c>pin.Tfm is not null</c>, so a null TFM matched
    /// anything. A pin that binds the version but not the framework pools a different
    /// assembly out of the same package.</para>
    /// </summary>
    [Fact]
    public void ASweepRefusesATfmThePinDoesNotName()
    {
        using var world = SweepWorld.Create(tfm: "net10.0");

        var sweep = world.Run();

        Assert.True(sweep.ExitCode == 1, world.Explain(sweep, "a pin naming a TFM the package lacks"));
        Assert.Contains("net10.0", sweep.Output + sweep.Errors, StringComparison.Ordinal);
        Assert.Empty(PooledAssemblies(world.OutputDirectory));
        world.AssertNoTemporaryLeftBehind();
    }

    /// <summary>
    /// A pin naming a version that is not there is refused rather than served by whatever
    /// is.
    ///
    /// <para>The version in the pin is the coordinate the sweep acquires at, so this is
    /// the property that a pinned pool is a pinned pool: asked for a version the cache
    /// does not hold, a sweep must come up short rather than fall back to the version it
    /// does hold. Falling back is how the pool drifted before it was pinned.</para>
    ///
    /// <para>Enforced at the <em>request</em>, which is what the status below pins down.
    /// The sweep also compares the returned version against the pin afterwards, and this
    /// case does not reach that comparison -- measured: disabling it alone leaves this
    /// case green, because acquisition has already refused. That comparison is a backstop
    /// against an extractor that answers with something other than what it was asked for,
    /// and nothing here can arrange for it to; it is deliberately left ungated rather than
    /// left looking gated.</para>
    /// </summary>
    [Fact]
    public void ASweepRefusesAVersionThePinDoesNotName()
    {
        using var world = SweepWorld.Create(version: "9.9.9");

        var sweep = world.Run();

        Assert.True(sweep.ExitCode == 1, world.Explain(sweep, "a pin naming an absent version"));

        // Never acquired, rather than acquired and then caught: the version bound the
        // request. A fallback to the version the cache does hold would read as selected.
        Assert.Equal("acquisition-failed", world.ReportedStatus(sweep));

        Assert.Empty(PooledAssemblies(world.OutputDirectory));
        world.AssertNoTemporaryLeftBehind();
    }

    /// <summary>
    /// Something planted at a destination is replaced, not written through.
    ///
    /// <para>This is the round-sixteen defect, kept. The three metadata writes had been
    /// hardened and the ninety-one assembly copies had not, so a bare
    /// <c>File.Copy(overwrite: true)</c> opened the destination, followed a symlink at it,
    /// and turned a file outside the output directory into a copy of a .NET assembly at
    /// exit 0. Nothing downstream could notice: the hash is taken over the destination,
    /// and reading back through the same link returns the bytes that were written.</para>
    ///
    /// <para>So the assertion is not about the sweep's exit code -- the sweep is entitled
    /// to succeed here, and does. It is that the file outside the output directory still
    /// holds its own bytes, and that what the sweep pooled is a real file rather than a
    /// link to somewhere else.</para>
    /// </summary>
    [Fact]
    public void ASweepReplacesWhatIsPlantedAtItsDestinationRatherThanWritingThroughIt()
    {
        using var world = SweepWorld.Create();

        string outsider = Path.Combine(world.Scratch, "outside.txt");
        const string Sentinel = "bytes that belong to someone else";
        File.WriteAllText(outsider, Sentinel);

        string destination = Path.Combine(
            world.OutputDirectory, "packages", $"001-{FixturePackage}", FixtureVersion, FixtureAssembly);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        PlantSymbolicLink(destination, outsider);

        var sweep = world.Run();

        Assert.Equal(Sentinel, File.ReadAllText(outsider));
        Assert.True(sweep.ExitCode == 0, world.Explain(sweep, "a symlink planted at the destination"));

        // Replaced, so the pooled path is the assembly itself rather than a way back out
        // to the file above -- which would read as correct while pooling nothing.
        Assert.Null(new FileInfo(destination).LinkTarget);
        Assert.Equal(world.FixtureSha256, Sha256Of(destination));
        world.AssertNoTemporaryLeftBehind();
    }

    /// <summary>
    /// A package whose copy fails is counted as failed.
    ///
    /// <para>The family that produced findings in four consecutive review rounds was a
    /// package dropping out of the pool and out of the total owed at the same time, so the
    /// two cancelled and a short pool exited 0. A copy that cannot be written is the last
    /// place that can happen, and the one furthest from the pin, which is why an
    /// unwritable destination is the case here.</para>
    ///
    /// <para>The temporary matters as much as the exit code. Writes go to a fresh sibling
    /// and are renamed onto the destination, so a failure that leaves the sibling behind
    /// leaves a partial assembly in the output directory for a later run to hash.</para>
    /// </summary>
    [Fact]
    public void ASweepCountsAPackageWhoseCopyFailedAsFailed()
    {
        using var world = SweepWorld.Create();

        string destinationDirectory = Path.Combine(
            world.OutputDirectory, "packages", $"001-{FixturePackage}", FixtureVersion);
        Directory.CreateDirectory(destinationDirectory);
        MakeUnwritable(destinationDirectory);

        try
        {
            var sweep = world.Run();

            Assert.True(sweep.ExitCode == 1, world.Explain(sweep, "a destination that cannot be written"));
            Assert.Empty(PooledAssemblies(world.OutputDirectory));

            // Reported as this package failing, not as a pool that was simply smaller.
            Assert.Equal(0, world.ReportedManifest(sweep)["SelectedPackageCount"]!.GetValue<int>());
            Assert.NotEqual("selected", world.ReportedStatus(sweep));

            // The other half of the delta's own claim: this failure happens before the
            // temporary exists, so the sweep must say so rather than report a cleanup it
            // never performed. Without this, a sweep whose "did I create one" flag was
            // stuck true reported "removed" here -- File.Delete swallows a missing file
            // even in a directory it could not have written -- and nothing in the class
            // read the value that would have said otherwise.
            Assert.Equal("none", world.ReportedWriteTemporary(sweep));
        }
        finally
        {
            RestoreWritable(destinationDirectory);
        }

        world.AssertNoTemporaryLeftBehind();
    }

    /// <summary>
    /// A write that fails after its temporary exists leaves no temporary.
    ///
    /// <para>The other failing case here cannot prove this. An unwritable directory fails
    /// at the moment the temporary is created, so there is nothing to clean up and the
    /// cleanup is never reached -- a case that asserted the absence of a temporary there
    /// would be asserting that a file which was never created does not exist. A directory
    /// standing at the destination is the failure that happens <em>after</em>: the
    /// temporary is created beside it and written, and only the rename onto the name
    /// fails.</para>
    ///
    /// <para>What is left behind matters because the temporary is a sibling of the
    /// destination, inside the pool. A partial assembly left in <c>packages/</c> is a file
    /// a later run finds and hashes, and the pin disagreeing with it would be reported as
    /// the pin failing rather than as the run that abandoned it.</para>
    ///
    /// <para>Which is why the reported outcome is asserted and not just the exit code.
    /// Absence is satisfied by a sweep that never got far enough to create anything, so a
    /// case asserting only "no temporary" passes on any early exit at all -- measured: a
    /// sweep that exits before acquisition, with the cleanup deleted, left this case
    /// green, and now fails on the manifest it never wrote. Requiring the run to have
    /// reached the copy is what makes the absence afterwards mean the cleanup happened.
    /// </para>
    ///
    /// <para>And the creation itself is asserted, because reaching the copy is not the
    /// same as having created the temporary: a failure raised inside <c>ReplaceOrReport</c>
    /// before the file exists reports the same <c>copy-failed</c>, and a case stopping at
    /// the status passed with the cleanup deleted. Two reviewers closed that gap the same
    /// way, with a <c>FileSystemWatcher</c>, disproving a claim made here that the
    /// randomized name put it out of reach. They were right that it was reachable and the
    /// watcher was the wrong instrument: on a machine whose inotify watch limit is
    /// exhausted -- which a developer box with enough worktrees and editors on it reaches
    /// -- arming fails through the <c>Error</c> event and the case reports a sweep that
    /// did create its temporary as one that never did. Measured here, reproducibly.</para>
    ///
    /// <para>So the sweep says what became of it instead, in the manifest, beside the
    /// staging-directory cleanup it already reported. <c>removed</c> against <c>none</c>
    /// separates a cleanup that ran from one that was never needed, and both halves are
    /// gated: this case requires <c>removed</c>, and
    /// <see cref="ASweepCountsAPackageWhoseCopyFailedAsFailed"/> -- whose write fails
    /// before the temporary exists -- requires <c>none</c>. Deterministic, on every
    /// platform, and read for the same reason the rest of this file reads statuses rather
    /// than prose.</para>
    ///
    /// <para><c>left-behind</c> is the third value and is <em>not</em> gated, deliberately
    /// and not silently. It needs <c>File.Delete</c> to throw on a file <c>CreateNew</c>
    /// had just made, and on Linux the two need the same directory permission, so no
    /// black-box case can arrange it. It is worth reporting anyway -- it is the answer an
    /// operator wants when a failed sweep may have dropped a partial assembly in the pool
    /// -- but nothing here proves the sweep would say it, and that is stated rather than
    /// left to read as covered. The set difference in
    /// <see cref="SweepWorld.AssertNoTemporaryLeftBehind"/> is what actually catches the
    /// leak; <c>left-behind</c> is how it would be explained.</para>
    ///
    /// <para>The value is derived from creation downward -- <c>created ? "left-behind" :
    /// "none"</c> -- so a cleanup that stopped being called reports a leak rather than
    /// reporting that no temporary was ever made. That direction is a defensive choice for
    /// the operator reading the manifest, not a property this gate enforces: reverting it
    /// leaves every case green, because the two derivations differ only on the
    /// <c>left-behind</c> path that nothing can reach. Measured, and recorded here so the
    /// next reader does not mistake the argument for a covered claim.</para>
    /// </summary>
    [Fact]
    public void ASweepLeavesNoTemporaryWhenAWriteFailsAfterCreatingOne()
    {
        using var world = SweepWorld.Create();

        string destination = Path.Combine(
            world.OutputDirectory, "packages", $"001-{FixturePackage}", FixtureVersion, FixtureAssembly);
        Directory.CreateDirectory(destination);

        var sweep = world.Run();

        Assert.True(sweep.ExitCode == 1, world.Explain(sweep, "a directory standing at the destination"));

        // Reached the copy and failed there, rather than exiting earlier.
        Assert.Equal("copy-failed", world.ReportedStatus(sweep));

        // Created, then removed -- not "none", which is the failure this case is not about.
        Assert.Equal("removed", world.ReportedWriteTemporary(sweep));

        world.AssertNoTemporaryLeftBehind();
    }

    /// <summary>
    /// A package the seeded cache does not hold is unreachable, rather than fetched.
    ///
    /// <para>This is the case that gates the other seven. They all pool a synthetic package
    /// that only the scratch cache holds, so they stay green whatever the isolation does:
    /// deleting both knobs from the sweep -- <c>HttpClientFactory.Initialize(false)</c> and
    /// <c>skipNuGetCache: false</c> -- leaves all seven passing, because the fixture is
    /// still found where it was seeded. The suite would quietly become able to reach the
    /// developer's NuGet cache and the network, and <see cref="SweepWorld.Run"/> would go
    /// on claiming otherwise. Naming a real package is what makes that regression visible:
    /// it is reachable both ways, so only genuine isolation can fail to reach it.</para>
    ///
    /// <para>The outcome is asserted, not just the exit code. A sweep that found this
    /// package would still exit 1 -- on the pin, which names bytes no real package has --
    /// so an exit-code-only case would pass equally well with the isolation removed, which
    /// is the failure it exists to catch. <c>acquisition-failed</c> and
    /// <c>pin-mismatch</c> are the sweep's own recorded statuses, and they separate the two
    /// without depending on how any layer words its message.</para>
    ///
    /// <para>Half of its power is ambient, and the case runs anyway rather than skipping.
    /// The network half holds anywhere: a sweep that regressed <c>Initialize(false)</c>
    /// downloads this package from nuget.org and reaches <c>pin-mismatch</c> on any
    /// machine. The shared-NuGet-cache half only bites where that cache actually holds the
    /// package; somewhere it does not, a sweep with <c>skipNuGetCache</c> regressed misses
    /// the cache, falls through to a severed network, and fails exactly as it should have.
    /// Skipping the whole case over that used to throw away the network half with it --
    /// measured: on a machine whose cache lacks the package, bypassing the old skip left
    /// the case discriminating correctly (<c>acquisition-failed</c> against the regressed
    /// sweep's <c>pin-mismatch</c>). So the assertion is unconditional, and the ambient
    /// half it may be missing is reported by
    /// <see cref="TheSharedNuGetCacheHoldsWhatTheIsolationCaseNeeds"/> instead.</para>
    /// </summary>
    [Fact]
    public void ASweepCannotReachAPackageTheSeededCacheDoesNotHold()
    {
        using var world = SweepWorld.Create();
        world.PinInstead(IsolationProbePackage, IsolationProbeVersion, "net6.0");

        var sweep = world.Run();

        Assert.True(sweep.ExitCode == 1, world.Explain(sweep, "a package the scratch cache does not hold"));

        // Never reached, rather than reached and rejected. Isolation regressed, this reads
        // pin-mismatch instead, because the package was found and then failed the pin.
        Assert.Equal("acquisition-failed", world.ReportedStatus(sweep));

        Assert.Empty(PooledAssemblies(world.OutputDirectory));
    }

    /// <summary>
    /// Present in the shared NuGet cache of any machine that has built this repository,
    /// and on nuget.org: reachable by either isolation knob's absence, and by neither's
    /// presence. Which is what makes it the probe
    /// <see cref="ASweepCannotReachAPackageTheSeededCacheDoesNotHold"/> uses.
    /// </summary>
    const string IsolationProbePackage = "newtonsoft.json";

    const string IsolationProbeVersion = "13.0.4";

    /// <summary>
    /// The shared NuGet cache holds the package the isolation case probes with.
    ///
    /// <para>Not a property of the product: a precondition of one case's coverage, asserted
    /// separately so that losing it is a visible skip rather than a silent narrowing.
    /// <see cref="ASweepCannotReachAPackageTheSeededCacheDoesNotHold"/> keeps gating the
    /// network knob without this; what it loses is the cache knob, because a sweep that
    /// regressed <c>skipNuGetCache</c> can only be caught reaching a cache that has
    /// something to give it.</para>
    ///
    /// <para>Asked of the product, and asked as the product asks it. Whether a directory
    /// exists is not whether the package is there to be served: an empty
    /// <c>newtonsoft.json/13.0.4/</c> satisfies <c>Directory.Exists</c> and is rejected by
    /// the cache lookup, so a precondition spelled that way declares power the case does
    /// not have -- measured, and it left the isolation case green over a regression it
    /// could no longer see. These are the same two calls
    /// <see cref="NuGetCache.TryGetCachedPackage"/> makes on its NuGet-cache branch, so
    /// this and the sweep cannot disagree about what is cached.</para>
    ///
    /// <para>Read-only, deliberately. The directory is shared with every other agent and
    /// with the developer, and the one thing a test may never do to it is write -- seeding
    /// it to make this precondition true would be a test repairing its own coverage by
    /// damaging the machine.</para>
    /// </summary>
    [Fact]
    public void TheSharedNuGetCacheHoldsWhatTheIsolationCaseNeeds()
    {
        string cached = Path.Combine(
            NuGetCache.GetNuGetCachePath(), IsolationProbePackage, IsolationProbeVersion);

        if (!Directory.Exists(cached) || !NuGetCache.IsCachedPackageValid(cached, IsolationProbePackage))
        {
            Assert.Skip(
                $"The shared NuGet cache cannot serve {IsolationProbePackage} {IsolationProbeVersion}, so " +
                $"{nameof(ASweepCannotReachAPackageTheSeededCacheDoesNotHold)} gates the network knob but " +
                "not the cache knob on this machine.");
        }
    }

    static IReadOnlyList<string> PooledAssemblies(string outputDirectory) =>
        Directory.Exists(Path.Combine(outputDirectory, "packages"))
            ? Directory.GetFiles(Path.Combine(outputDirectory, "packages"), "*.dll", SearchOption.AllDirectories)
            : [];

    static string Sha256Of(string path) => Sha256Of(File.ReadAllBytes(path));

    static string Sha256Of(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>
    /// Plants a symlink at <paramref name="path"/>.
    ///
    /// <para>Only Windows is allowed to duck this, and it does so as a <em>skip</em>. An
    /// unprivileged Windows process cannot create a symlink, but a case that returned
    /// early and let the test pass would report the sweep's most serious historical defect
    /// as gated on a platform where nothing had run -- a green result covering an empty
    /// one. Everywhere else a failure to plant the link is a real failure and is thrown,
    /// because on those platforms this always works, and the reason it stopped working is
    /// something the run should say out loud rather than swallow.</para>
    /// </summary>
    static void PlantSymbolicLink(string path, string target)
    {
        if (OperatingSystem.IsWindows())
            Assert.Skip("planting a symlink needs privileges an unelevated Windows process lacks.");

        File.CreateSymbolicLink(path, target);
    }

    /// <summary>
    /// Makes <paramref name="directory"/> refuse new files, skipping visibly where that
    /// cannot be arranged.
    ///
    /// <para>Unix mode bits are the mechanism, so Windows skips. So does running as root,
    /// which ignores them: the check is not that the bits were set but that they now
    /// <em>bite</em>, because a case that believed the chmod and ran anyway would watch
    /// the copy succeed and report a failing copy as being accounted for.</para>
    /// </summary>
    static void MakeUnwritable(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("making a directory refuse writes here needs Unix mode bits.");
            return;
        }

        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        string probe = Path.Combine(directory, "probe");
        try
        {
            File.WriteAllText(probe, "");
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        File.Delete(probe);
        RestoreWritable(directory);
        Assert.Skip("this process can write to a directory it just made read-only (running as root?).");
    }

    /// <summary>
    /// Undoes <see cref="TryMakeUnwritable"/>, so the scratch directory can be deleted.
    /// </summary>
    static void RestoreWritable(string directory)
    {
        if (OperatingSystem.IsWindows())
            return;

        File.SetUnixFileMode(
            directory,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>
    /// A repository root, a package list, a pin, a cache holding one package, and an output
    /// directory -- everything one sweep reads and writes, all of it scratch.
    ///
    /// <para>The cache is seeded through <see cref="NuGetCache.CommitPackage"/> rather than
    /// by writing the directories directly. The layout of a committed package, including
    /// the marker that makes it count as committed, belongs to the product; a harness that
    /// laid it out itself would be a second implementation of it, and would keep passing
    /// after the product's own layout moved.</para>
    /// </summary>
    sealed class SweepWorld : IDisposable
    {
        string? _cachedAssemblyPath;
        string _requestedPackage = FixturePackage;

        SweepWorld(string scratch, string cacheDirectory, byte[] fixtureBytes)
        {
            Scratch = scratch;
            CacheDirectory = cacheDirectory;
            FixtureBytes = fixtureBytes;
            FixtureSha256 = Sha256Of(fixtureBytes);
        }

        public string Scratch { get; }

        public string CacheDirectory { get; }

        public byte[] FixtureBytes { get; }

        /// <summary>The hash of the assembly the pin names, and the only correct pool.</summary>
        public string FixtureSha256 { get; }

        public string FakeRoot => Path.Combine(Scratch, "root");

        public string OutputDirectory => Path.Combine(Scratch, "pool");

        public string ManifestPath => Path.Combine(OutputDirectory, "manifest.json");

        public string PooledListPath => Path.Combine(OutputDirectory, "assemblies.txt");

        /// <summary>
        /// The fixture assembly as the cache holds it. Overwriting this is how a case
        /// makes the cache answer with bytes the pin does not name.
        ///
        /// <para>Recorded by <see cref="SeedCache"/> from the path the product itself
        /// names, not composed here: the directory a committed package lands in is the
        /// product's to decide, down to the casing it applies to the name and version.</para>
        /// </summary>
        public string CachedAssemblyPath => _cachedAssemblyPath
            ?? throw new InvalidOperationException("The cache has not been seeded yet.");

        /// <summary>
        /// Builds the world. <paramref name="version"/> and <paramref name="tfm"/> are what
        /// the <em>pin</em> claims; the package in the cache is always the real one, so a
        /// case that changes them is changing the pin alone.
        /// </summary>
        public static SweepWorld Create(string? version = null, string? tfm = null)
        {
            string scratch = Directory.CreateTempSubdirectory("evil-sweep-gate").FullName;
            try
            {
                // An assembly the build already produced, so the fixture is a real PE file
                // rather than something shaped like one.
                byte[] bytes = File.ReadAllBytes(typeof(EvilPoolSweepGateTests).Assembly.Location);
                string cacheDirectory = Path.Combine(scratch, "cache");
                var world = new SweepWorld(scratch, cacheDirectory, bytes);

                world.SeedCache();
                world.WriteInputs(version ?? FixtureVersion, tfm ?? FixtureTfm);
                return world;
            }
            catch
            {
                Directory.Delete(scratch, recursive: true);
                throw;
            }
        }

        void SeedCache()
        {
            string staged = Path.Combine(Scratch, "staged");
            Directory.CreateDirectory(Path.Combine(staged, "lib", FixtureTfm));
            File.WriteAllBytes(Path.Combine(staged, "lib", FixtureTfm, FixtureAssembly), FixtureBytes);
            File.WriteAllText(
                Path.Combine(staged, $"{FixturePackage}.nuspec"),
                $"""
                <?xml version="1.0" encoding="utf-8"?>
                <package><metadata>
                  <id>{FixturePackage}</id><version>{FixtureVersion}</version>
                  <authors>{nameof(EvilPoolSweepGateTests)}</authors>
                  <description>A synthetic package that exists only to be pooled.</description>
                </metadata></package>
                """);

            // Points this process's cache at the scratch directory only so that the commit
            // below lands there. The sweep is told the same directory by environment, and
            // resolves it with this same code, so the two cannot disagree about where it is.
            //
            // This is process-global state with no reset, and it is left pointing at a
            // directory Dispose removes. That is deliberate: no other type in this test
            // assembly references CoreCache or NuGetCache, and if one ever does, resolving
            // against a removed directory fails loudly, where leaving a usable scratch
            // cache behind would quietly serve it this fixture instead.
            NuGetCache.Initialize("dotnet-inspect", CacheDirectory, skipNuGetCache: true);
            NuGetCache.CommitPackage(staged, null, FixturePackage, FixtureVersion);

            // Where the product put it, plus the layout of the package this test authored.
            // A product that moved its cache layout fails here, naming the path it did not
            // write, rather than downstream as a sweep that mysteriously ignored a tamper.
            _cachedAssemblyPath = Path.Combine(
                NuGetCache.GetPackageCachePath(FixturePackage, FixtureVersion),
                "lib",
                FixtureTfm,
                FixtureAssembly);

            Assert.True(
                File.Exists(_cachedAssemblyPath),
                $"The committed package does not hold the fixture assembly at {_cachedAssemblyPath}.");
        }

        void WriteInputs(string pinnedVersion, string pinnedTfm)
        {
            string data = Path.Combine(FakeRoot, "docs", "data");
            Directory.CreateDirectory(data);

            // What makes this directory a repository root, and so what makes the sweep read
            // the two files beside it instead of the committed ones.
            File.WriteAllText(Path.Combine(FakeRoot, "dotnet-inspect.slnx"), "");

            var list = new JsonArray(
                new JsonObject
                {
                    ["rank"] = 1,
                    ["package"] = FixturePackage,
                    ["downloads"] = 1,
                });
            var pin = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["packages"] = new JsonArray(
                    new JsonObject
                    {
                        ["package"] = FixturePackage,
                        ["version"] = pinnedVersion,
                        ["tfm"] = pinnedTfm,
                        ["status"] = "pinned",
                        ["detail"] = null,
                        ["sha256"] = Sha256Of(FixtureBytes),
                    }),
            };

            var indented = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(Path.Combine(data, "nuget-top-packages.json"), list.ToJsonString(indented));
            File.WriteAllText(Path.Combine(data, "nuget-top-packages.lock.json"), pin.ToJsonString(indented));
        }

        /// <summary>
        /// The status the sweep recorded for the one package it was given.
        ///
        /// <para>The sweep's own vocabulary -- <c>acquisition-failed</c>,
        /// <c>pin-mismatch</c>, <c>copy-failed</c>, <c>selected</c> -- rather than the
        /// prose any layer happens to render. A case asserting on message text fails when
        /// wording changes and behavior does not, which trains the next reader to edit the
        /// assertion rather than read it.</para>
        ///
        /// <para>The entry has to be about the package that was asked for. A status is a
        /// value, not a subject, so a manifest whose one row reports the right outcome for
        /// some other package satisfies every caller of this that reads only the status --
        /// measured: rewriting every recorded package name left all eight cases green.
        /// Asking the row who it is about is what makes the status an answer to the
        /// question the case put.</para>
        /// </summary>
        public string ReportedStatus((int ExitCode, string Output, string Errors) sweep) =>
            ReportedEntry(sweep)["Status"]!.GetValue<string>();

        /// <summary>
        /// The manifest's one row, having established that it is about the package this
        /// world asked for.
        ///
        /// <para>Every read of the manifest goes through here, which is the point. A status
        /// is a value, not a subject, so a row reporting the right outcome for some other
        /// package satisfies any reader that takes the value and never asks whose it is --
        /// measured: rewriting every recorded package name left all nine cases green, and
        /// after the check was added to the status reader alone it still left two of them
        /// green, because those two indexed the array themselves. One door, so a case
        /// cannot get the value without the question having been put.</para>
        /// </summary>
        public JsonNode ReportedEntry((int ExitCode, string Output, string Errors) sweep)
        {
            Assert.True(File.Exists(ManifestPath), Explain(sweep, "a run that wrote no manifest"));
            var manifested = JsonNode.Parse(File.ReadAllText(ManifestPath))!;
            var packages = manifested["Packages"]!.AsArray();
            Assert.True(packages.Count == 1, Explain(sweep, $"a manifest holding {packages.Count} packages"));

            var recorded = packages[0]!["RequestedPackage"]?.GetValue<string>();
            Assert.True(
                recorded == _requestedPackage,
                Explain(sweep, $"a manifest reporting on '{recorded}' when '{_requestedPackage}' was asked for"));

            return packages[0]!;
        }

        /// <summary>The manifest as a whole, for the fields that are not the one row.</summary>
        public JsonNode ReportedManifest((int ExitCode, string Output, string Errors) sweep)
        {
            Assert.True(File.Exists(ManifestPath), Explain(sweep, "a run that wrote no manifest"));
            return JsonNode.Parse(File.ReadAllText(ManifestPath))!;
        }

        /// <summary>
        /// What the sweep recorded became of the temporary a failing write went through:
        /// <c>removed</c>, <c>left-behind</c>, or <c>none</c> -- and <c>null</c> on any row
        /// that is not a <c>copy-failed</c>, which is the only status the sweep records it
        /// on. <c>moved</c> is a value the write returns and the manifest never carries,
        /// because a write that landed has no temporary to account for.
        /// </summary>
        public string? ReportedWriteTemporary((int ExitCode, string Output, string Errors) sweep) =>
            ReportedEntry(sweep)["WriteTemporary"]?.GetValue<string>();

        /// <summary>
        /// Replaces the inputs so the sweep is asked for some other package, pinned to
        /// bytes nothing has. Used to ask for something the scratch cache cannot answer.
        /// </summary>
        public void PinInstead(string package, string version, string tfm)
        {
            _requestedPackage = package;
            string data = Path.Combine(FakeRoot, "docs", "data");
            var list = new JsonArray(
                new JsonObject
                {
                    ["rank"] = 1,
                    ["package"] = package,
                    ["downloads"] = 1,
                });
            var pin = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["packages"] = new JsonArray(
                    new JsonObject
                    {
                        ["package"] = package,
                        ["version"] = version,
                        ["tfm"] = tfm,
                        ["status"] = "pinned",
                        ["detail"] = null,
                        ["sha256"] = Sha256Of(FixtureBytes),
                    }),
            };

            var indented = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(Path.Combine(data, "nuget-top-packages.json"), list.ToJsonString(indented));
            File.WriteAllText(Path.Combine(data, "nuget-top-packages.lock.json"), pin.ToJsonString(indented));
        }

        /// <summary>
        /// Runs the sweep over this world's inputs, offline and against this world's cache.
        ///
        /// <para>Offline is not decoration. It is what makes a green result mean the seeded
        /// cache answered: without it, a case that stopped reaching the fixture would go to
        /// nuget.org and could pass on a real package, and the suite would be gating the
        /// network instead of the pin. That claim is itself gated, by
        /// <see cref="ASweepCannotReachAPackageTheSeededCacheDoesNotHold"/> -- without it,
        /// both knobs could be deleted from the sweep with all other cases still green.</para>
        /// </summary>
        public (int ExitCode, string Output, string Errors) Run()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host
                    ? host
                    : "dotnet",
                WorkingDirectory = FakeRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add(Path.Combine(
                AuthoredCorpusRatchetTests.FindRepositoryRoot(),
                "eng",
                "prepare-decompiler-package-sweep.cs"));
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add(OutputDirectory);
            startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add("1");

            startInfo.Environment["DOTNET_INSPECT_OFFLINE"] = "1";
            startInfo.Environment["DOTNET_INSPECT_ISOLATED"] = "evil-sweep-gate";
            startInfo.Environment["DOTNET_INSPECT_CACHE_DIR"] = CacheDirectory;

            // NUGET_PACKAGES is deliberately left alone, and cannot be used for isolation.
            // The sweep is a file-based app, so this subprocess restores itself before it
            // runs anything, and it restores from wherever that variable points. Aiming it
            // somewhere without this repository's own package graph fails the restore
            // (NU1102) and the sweep never starts -- every case here goes red naming a
            // package it has nothing to do with. That is a broken environment reporting
            // itself, not a false green, and Explain prints the restore error that says so.
            // Isolation from the shared NuGet cache is DOTNET_INSPECT_ISOLATED's job, which
            // the sweep applies to its own lookups and not to its restore.

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("could not start the sweep");

            var output = process.StandardOutput.ReadToEndAsync();
            var failures = process.StandardError.ReadToEndAsync();

            // Bounded, because a hang here would otherwise be reported by CI killing the
            // job, which says nothing about which case hung. Minutes rather than seconds
            // because a cold `dotnet run` of a file-based app builds it first.
            if (!process.WaitForExit((int)TimeSpan.FromMinutes(5).TotalMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("the sweep did not exit within five minutes");
            }

            return (process.ExitCode, output.GetAwaiter().GetResult(), failures.GetAwaiter().GetResult());
        }

        /// <summary>
        /// Reports a sweep in full when its exit code was not the expected one. A bare
        /// "expected 1, got 0" over a subprocess sends the reader back to reproduce it by
        /// hand, which is what these cases exist to stop.
        /// </summary>
        public string Explain((int ExitCode, string Output, string Errors) sweep, string what) =>
            $"the sweep exited {sweep.ExitCode} over {what}.\n"
            + $"stdout:\n{sweep.Output}\nstderr:\n{sweep.Errors}";

        /// <summary>
        /// The pool holds exactly what the sweep recorded pooling, and nothing else.
        ///
        /// <para>Which is the sweep's own claim, made at the point it deletes an assembly
        /// that failed its pin: <em>"assemblies.txt is written from the in-memory list so
        /// a stray file cannot enter the pool"</em>. A leftover write temporary is one way
        /// to break it and a rejected assembly left in place is another, and both matter
        /// for the same reason -- a file in <c>packages/</c> that nothing recorded is one a
        /// later sweep finds, hashes, and disagrees with the pin about, reported as the pin
        /// failing rather than as the run that made it.</para>
        ///
        /// <para>Asked as a set difference rather than by name. This used to glob
        /// <c>*.tmp</c>, which is the product's naming convention restated in the harness:
        /// a reviewer changed the sweep's temporary suffix to <c>.temp</c>, disabled the
        /// cleanup, and watched the case stay green over a temporary genuinely left behind.
        /// Nothing here now knows what a temporary is called, so nothing here goes stale
        /// when that changes -- what it knows is that the pool and the record must agree,
        /// and the record is the product's.</para>
        ///
        /// <para>Over the whole output directory, not <c>packages/</c>. Narrowed to the
        /// subtree the pool lives in, a cleanup that moved its temporary one level up
        /// instead of deleting it left the file sitting beside <c>packages/</c> with all
        /// nine cases green -- measured. The sweep's output directory has exactly three
        /// kinds of occupant, and naming all three is what makes anything else a failure:
        /// the manifest, the record, and the assemblies the record lists.</para>
        ///
        /// <para>The record has to exist, rather than being read as empty when it is
        /// absent. Mapping a missing file to no entries makes "the sweep wrote no record"
        /// indistinguishable from "the sweep recorded pooling nothing", and the first of
        /// those is a sweep that did not finish -- measured: suppressing the write whenever
        /// the pool came out empty left every case green.</para>
        /// </summary>
        public void AssertNoTemporaryLeftBehind()
        {
            if (!Directory.Exists(OutputDirectory))
                return;

            Assert.True(
                File.Exists(PooledListPath),
                $"the sweep left an output directory behind without writing '{PooledListPath}', "
                + "so there is no record to hold the pool against.");

            string[] present =
                [.. Directory.GetFiles(OutputDirectory, "*", SearchOption.AllDirectories)
                    .Order(StringComparer.Ordinal)];
            string[] expected =
                [.. File.ReadAllLines(PooledListPath)
                    .Where(line => line.Length > 0)
                    .Append(PooledListPath)
                    .Append(ManifestPath)
                    .Order(StringComparer.Ordinal)];

            Assert.Equal(expected, present);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Scratch, recursive: true);
            }
            catch (IOException)
            {
                // A scratch directory that outlives the run is not a result.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
