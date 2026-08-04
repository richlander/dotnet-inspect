using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
/// a package list and pin of this suite's choosing -- no product path override is
/// involved. It is pointed at a scratch cache holding two synthetic packages and told to
/// stay offline, so <em>it</em> acquires over no network and reads no shared cache, and
/// nothing it pools or acquires comes from outside the scratch directory. Offline is what
/// keeps that honest: a case that stopped being served from the seeded cache would reach
/// for the network and fail here rather than quietly passing on whatever the machine
/// happened to have.</para>
///
/// <para>The claim stops there, deliberately. Each case launches the sweep with
/// <c>dotnet run</c>, and a file-based app is restored and built before it runs, which
/// reads the ordinary NuGet package cache, writes to it and to the SDK's runfile
/// directories, and may go to the network to fill them. That is
/// true of every build in this repository and of the sweep runs in
/// <see cref="EvilPoolPinTests"/>; it is not something these cases control, so they do
/// not claim a process that opens no socket. What is gated is narrower and is the part
/// that matters: no package <em>the sweep pools</em> can come from anywhere but the
/// fixture seeded below.</para>
/// </summary>
[Trait("Area", "Corpus")]
public class EvilPoolSweepGateTests
{
    /// <summary>
    /// Identity of the source these fixtures speak for. Cached content is scoped
    /// to the source that committed it, so a test that seeds the cache by hand
    /// must use the same source the code under test resolves — otherwise the
    /// seeded entry is correctly invisible.
    /// </summary>
    private static readonly string TestSourceKey =
        NuGetCache.GetSourceKey("https://api.nuget.org/v3/index.json");

    const string FixturePackage = "sweep.fixture";
    const string FixtureVersion = "1.0.0";
    const string FixtureTfm = "net8.0";
    const string FixtureAssembly = "Sweep.Fixture.dll";

    // A second package, ranked ahead of the one each case is about, which every sweep here
    // pools successfully before reaching the subject. See SweepWorld for why the subject is
    // never first.
    const string LeadPackage = "sweep.lead";
    const string LeadAssembly = "Sweep.Lead.dll";

    // A third package, committed by every world and pooled by none of them, which ships a
    // nuspec and no library at all. It is what the !IsSelected arm needs: the two packages
    // above both ship an assembly, so that whole arm -- library-unavailable, and the
    // no-library pin it confirms -- was unreachable, and deleting the arm outright left all
    // eleven cases green.
    const string EmptyPackage = "sweep.empty";

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

        string pooled = world.SubjectDestination;
        Assert.True(File.Exists(pooled), $"the sweep exited 0 without pooling '{pooled}'");
        Assert.Equal(world.FixtureSha256, Sha256Of(pooled));

        // This list crosses from .NET into shell tooling, so its exact bytes are the
        // protocol: BOM-less UTF-8, LF separators, and a terminating LF. Reading lines
        // would accept UTF-16 and a missing terminator and leave that contract ungated.
        byte[] expectedList = Encoding.UTF8.GetBytes(
            $"{world.LeadDestination}\n{pooled}\n");
        Assert.Equal(expectedList, File.ReadAllBytes(world.PooledListPath));

        // And the count agrees with the record. Nothing read this aggregate before, so a
        // successful two-package pool could report zero selected -- measured.
        Assert.Equal(2, world.ReportedManifest(sweep)["SelectedPackageCount"]!.GetValue<int>());

        var entry = world.ReportedEntry(sweep);
        Assert.Equal("selected", entry["Status"]!.GetValue<string>());
        Assert.Equal(world.FixtureSha256, entry["Sha256"]!.GetValue<string>());

        // And that it came from the cache rather than the network, which is the manifest's
        // own record of the isolation every other case rests on -- measured: hardcoding it
        // false left every case green. One direction only, and worth saying: a constant
        // true passes this too, because no case here can produce a row that legitimately
        // reports false. The gate on the isolation itself is
        // ASweepCannotReachAPackageTheSeededCacheDoesNotHold.
        Assert.True(entry["FromCache"]!.GetValue<bool>(), world.Explain(sweep, "a pool the cache did not serve"));

        // Where it landed, too. The manifest is how an operator finds the pooled file
        // without walking the tree, and the path is the one recorded field nothing else
        // here reads -- measured: replacing it with a literal left all nine cases green.
        Assert.Equal(
            Path.Combine("packages", $"002-{FixturePackage}", FixtureVersion, FixtureAssembly),
            entry["AssemblyPath"]!.GetValue<string>());

        world.AssertNoTemporaryLeftBehind();
    }

    /// <summary>
    /// A sweep that cannot write its assembly record refuses the output directory.
    ///
    /// <para>Exit 1 means the sweep ran and the corpus is short a package. This failure
    /// is different: both packages were pooled, but the operator-supplied output path
    /// prevented the record that makes them consumable from being written. Exit 2 keeps
    /// that bad-output refusal distinct from a package failure.</para>
    /// </summary>
    [Fact]
    public void ASweepThatCannotWriteItsAssemblyRecordRefusesTheOutputDirectory()
    {
        using var world = SweepWorld.Create();
        Directory.CreateDirectory(world.PooledListPath);

        var sweep = world.Run();

        Assert.True(
            sweep.ExitCode == 2,
            world.Explain(sweep, "a directory standing at the assembly record path"));
        Assert.Contains(
            $"Could not write '{world.PooledListPath}'",
            sweep.Errors,
            StringComparison.Ordinal);

        // The run reached the record write after pooling both packages; this is not an
        // earlier refusal that happened to return the same exit code.
        Assert.True(File.Exists(world.LeadDestination), "the lead was not pooled.");
        Assert.True(File.Exists(world.SubjectDestination), "the subject was not pooled.");
        Assert.False(File.Exists(world.ManifestPath), "the sweep wrote a manifest without its assembly record.");
    }

    /// <summary>
    /// A sweep that cannot invalidate its prior manifest refuses the output directory.
    ///
    /// <para>Manifest invalidation precedes the first pool or assembly-record mutation.
    /// The stale file makes that boundary observable: an invalidation failure must leave
    /// both the prior pool and the absence of a new record untouched.</para>
    /// </summary>
    [Fact]
    public void ASweepThatCannotInvalidateItsManifestRefusesTheOutputDirectory()
    {
        using var world = SweepWorld.Create();
        Directory.CreateDirectory(world.ManifestPath);

        string stale = Path.Combine(
            world.OutputDirectory, "packages", "999-sweep.stale", "1.0.0", "Sweep.Stale.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        File.WriteAllBytes(stale, world.FixtureBytes);

        var sweep = world.Run();

        Assert.True(
            sweep.ExitCode == 2,
            world.Explain(sweep, "a directory standing at the manifest path"));
        Assert.Contains(
            $"Could not invalidate prior manifest '{world.ManifestPath}'",
            sweep.Errors,
            StringComparison.Ordinal);

        Assert.False(File.Exists(world.PooledListPath), "the sweep wrote an assembly record after invalidation failed.");
        Assert.False(File.Exists(world.LeadDestination), "the sweep pooled the lead after invalidation failed.");
        Assert.False(File.Exists(world.SubjectDestination), "the sweep pooled the subject after invalidation failed.");
        Assert.True(File.Exists(stale), "the sweep reconciled the prior pool after invalidation failed.");
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

        // Which refusal, not merely that it refused. The label is what --refresh-pin acts
        // on: pin-mismatch records nothing and leaves the existing pin standing, while
        // library-unavailable rewrites the pin to no-library. Mislabelled, the tampered
        // package this case plants is pinned out of the pool permanently and the evidence
        // of the tamper goes with it -- measured: relabelling both pin-mismatch sites left
        // all nine cases green, because exit code, prose and an empty pool are unchanged.
        Assert.Equal("pin-mismatch", world.ReportedStatus(sweep));

        world.AssertOnlyTheLeadWasPooled(sweep);
        world.AssertNoTemporaryLeftBehind();
    }

    /// <summary>
    /// A package pinned as shipping no library, which now ships one, is refused.
    ///
    /// <para>The other direction of the pin, and the one that is easy to miss. Nine of the
    /// top hundred packages genuinely carry no primary library -- meta-packages, and
    /// packages whose primary is ambiguous -- so the pin records <c>no-library</c> for them
    /// and the sweep neither acquires nor fails over them. That is an assertion about the
    /// package, not a way of ignoring it: a version that starts shipping an assembly starts
    /// contributing to the pool, and a pool that absorbed it quietly would have grown
    /// without anyone approving the bytes.</para>
    ///
    /// <para>Gated because nothing reached this behavior at all before: no case pinned a
    /// package as <c>no-library</c>, so the whole arm was unexercised.</para>
    ///
    /// <para>What the case proves is the refusal, not the arm. Replacing the arm's
    /// condition with <c>false</c> leaves this green, and that is correct rather than a
    /// gap: a <c>no-library</c> pin carries no hash -- there is no assembly for one to
    /// describe, which is the invariant <c>EvilPoolPinTests</c> holds the committed file to
    /// -- so the assembly that arrived is compared against no hash and refused a few lines
    /// later anyway. The arm is a better-diagnosed refusal in front of a backstop that
    /// would catch it regardless, which is worth having and is not what keeps the pool
    /// pinned. A reviewer read the green as the pool silently drifting; measured, it does
    /// not -- the pool still holds the lead alone, which is what this case asserts. Gating
    /// the arm itself would need a pin that is <c>no-library</c> and carries a matching
    /// hash, a combination the real file cannot hold, so it is left ungated and said so.
    /// </para>
    /// </summary>
    [Fact]
    public void ASweepRefusesAPackagePinnedAsShippingNoLibraryThatShipsOne()
    {
        using var world = SweepWorld.Create(status: "no-library");

        var sweep = world.Run();

        Assert.True(sweep.ExitCode == 1, world.Explain(sweep, "a no-library pin over a package with one"));

        // The same word as any other pin failure: what arrived is not what was named.
        Assert.Equal("pin-mismatch", world.ReportedStatus(sweep));

        world.AssertOnlyTheLeadWasPooled(sweep);
        world.AssertNoTemporaryLeftBehind();
    }

    /// <summary>
    /// The pin binds the bytes at the first rank as well as the second, and one package's
    /// refusal is not the run's.
    ///
    /// <para>The companion to <see cref="ASweepRefusesBytesThePinDoesNotName"/>, which
    /// tampers with the subject at rank 2. Moving the subject off the front is what made
    /// the first iteration observable at all, and it left the first iteration itself
    /// unwatched: gating the hash comparison on <c>entry.Rank &gt; 1</c> left all ten cases
    /// green while a poisoned rank-1 package entered the pool -- measured. So this case
    /// tampers with the lead instead, and the two together hold both ends of the window
    /// the sweep is run over.</para>
    ///
    /// <para>It asserts the other half too, which nothing else could: the subject behind it
    /// is still pooled. A refusal has to stop the package it is about, and a sweep that
    /// abandoned the rest of the list on the first bad hash would pass every other case
    /// here, because in all of them the refused package is the last one.</para>
    /// </summary>
    [Fact]
    public void ASweepRefusesBytesThePinDoesNotNameAtTheFirstRankToo()
    {
        using var world = SweepWorld.Create();
        File.WriteAllBytes(world.LeadCachedAssemblyPath, [.. world.LeadBytes, 0]);

        var sweep = world.Run();

        Assert.True(sweep.ExitCode == 1, world.Explain(sweep, "a cache answering rank 1 with other bytes"));
        Assert.Equal("pin-mismatch", world.ReportedStatus(sweep, LeadPackage));

        // The run carried on to rank 2 and pooled it, and the pool is that alone.
        Assert.Equal("selected", world.ReportedStatus(sweep));
        Assert.Equal([world.SubjectDestination], PooledAssemblies(world.OutputDirectory));
        Assert.Equal(world.FixtureSha256, Sha256Of(world.SubjectDestination));
        Assert.Equal(1, world.ReportedManifest(sweep)["SelectedPackageCount"]!.GetValue<int>());

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

        // The sweep's own word for a package that arrived and was refused, and the other
        // of the two sites that produce it. See ASweepRefusesBytesThePinDoesNotName for
        // what a wrong label costs.
        Assert.Equal("pin-mismatch", world.ReportedStatus(sweep));

        world.AssertOnlyTheLeadWasPooled(sweep);
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

        world.AssertOnlyTheLeadWasPooled(sweep);
        world.AssertNoTemporaryLeftBehind();
    }

    /// <summary>
    /// What a case plants at the destination before the sweep runs: the three shapes a
    /// path can take that are not an ordinary file the sweep may simply overwrite.
    /// </summary>
    public enum Plant
    {
        /// <summary>A symlink to an existing file outside the output directory.</summary>
        SymbolicLink,

        /// <summary>A symlink whose target does not exist.</summary>
        DanglingSymbolicLink,

        /// <summary>A second name for an existing file outside the output directory.</summary>
        HardLink,
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
    /// to succeed here, and does. It is that the path outside the output directory is
    /// exactly as it was, and that what the sweep pooled is a real file rather than a link
    /// to somewhere else.</para>
    ///
    /// <para>All three shapes, because one of them was the whole coverage and the other two
    /// are the ways around it. Gated on a live symlink alone, a sweep that protected links
    /// whose target exists and wrote through the rest kept every case green while creating
    /// an arbitrary external file through a dangling one; and a sweep that asked only
    /// whether <c>LinkTarget</c> was set -- which a hard link leaves null -- wrote straight
    /// through a hard-linked destination onto the file it shares an inode with. Both were
    /// measured green against the single-shape case. The property was never about symlinks;
    /// it is that the sweep replaces what it finds instead of opening it.</para>
    /// </summary>
    [Theory]
    [InlineData(Plant.SymbolicLink)]
    [InlineData(Plant.DanglingSymbolicLink)]
    [InlineData(Plant.HardLink)]
    public void ASweepReplacesWhatIsPlantedAtItsDestinationRatherThanWritingThroughIt(Plant plant)
    {
        using var world = SweepWorld.Create();

        string outsider = Path.Combine(world.Scratch, "outside.txt");
        const string Sentinel = "bytes that belong to someone else";
        if (plant != Plant.DanglingSymbolicLink)
            File.WriteAllText(outsider, Sentinel);

        string destination = world.SubjectDestination;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        PlantAt(destination, outsider, plant);

        var sweep = world.Run();

        // The outside path first, before anything about the sweep: whatever it exited, this
        // is the fact the case is about.
        if (plant == Plant.DanglingSymbolicLink)
        {
            Assert.False(
                File.Exists(outsider),
                $"the sweep created '{outsider}' by writing through a dangling link at its destination.");
        }
        else
        {
            Assert.Equal(Sentinel, File.ReadAllText(outsider));
        }

        Assert.True(sweep.ExitCode == 0, world.Explain(sweep, $"a {plant} planted at the destination"));

        // Replaced, so the pooled path is the assembly itself rather than a way back out
        // to the path above -- which would read as correct while pooling nothing.
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

        string destinationDirectory = Path.GetDirectoryName(world.SubjectDestination)!;
        Directory.CreateDirectory(destinationDirectory);
        MakeUnwritable(destinationDirectory);

        try
        {
            var sweep = world.Run();

            Assert.True(sweep.ExitCode == 1, world.Explain(sweep, "a destination that cannot be written"));
            world.AssertOnlyTheLeadWasPooled(sweep);

            // Reported as this package failing, not as a pool that was simply smaller, and
            // as the failure it was: labelling an unwritable destination acquisition-failed
            // sends an operator to the network for a fault on their own disk -- measured,
            // green until this asserted the label rather than merely "not selected".
            Assert.Equal("copy-failed", world.ReportedStatus(sweep));

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

        string destination = world.SubjectDestination;
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
    /// A second sweep into the same output directory pools what that sweep recorded, and
    /// not what the one before it left there.
    ///
    /// <para>Reuse is the normal way this sweep is run, not an edge: the corpus script
    /// passes the same <c>&lt;outdir&gt;/sweep</c> every time, deliberately, so that the
    /// paths in an earlier <c>assemblies.txt</c> stay valid. So the pool accumulates
    /// across runs while each run's record describes only itself, and the two disagree
    /// the first time a package stops being pooled -- which is exactly the state
    /// <see cref="SweepWorld.AssertNoTemporaryLeftBehind"/> already says is the dangerous
    /// one: <em>a file in <c>packages/</c> that nothing recorded is one a later sweep
    /// finds, hashes, and disagrees with the pin about, reported as the pin failing
    /// rather than as the run that made it.</em> That was prose about a single run; this
    /// is the case that makes it true across runs.</para>
    ///
    /// <para>The second run refuses on a version the scratch cache cannot serve, which is
    /// the cheapest way to make a package that <em>was</em> pooled stop being pooled. Any
    /// refusal would do -- the point is the leftover, not the reason for it.</para>
    ///
    /// <para>Three leftovers rather than one, in two directories and not all of them
    /// assemblies, because one leftover cannot tell a reconciliation that removes
    /// everything from one that removes something. Measured: with a single leftover, a
    /// walk that stopped after its first removal and a walk that considered only
    /// <c>*.dll</c> were both green.</para>
    /// </summary>
    [Fact]
    public void ASweepIntoAReusedDirectoryPoolsOnlyWhatItRecorded()
    {
        using var world = SweepWorld.Create();

        var first = world.Run();
        Assert.True(first.ExitCode == 0, world.Explain(first, "a first sweep with a matching pin"));
        Assert.Equal("selected", world.ReportedStatus(first));

        // Same package, a version nothing seeded: pooled by the run above, refused by this
        // one, so anything of its still in packages/ afterwards is the previous run's.
        world.PinInstead(FixturePackage, "9.9.9", FixtureTfm);

        // Two more leftovers of the shape a partial or abandoned run leaves: one beside the
        // subject's, one under the lead's own directory, and one that is not an assembly.
        string sibling = Path.Combine(
            Path.GetDirectoryName(world.SubjectDestination)!, "Sweep.Fixture.tmp");
        string underLead = Path.Combine(
            Path.GetDirectoryName(world.LeadDestination)!, "Sweep.Lead.Stale.dll");
        File.WriteAllBytes(sibling, world.FixtureBytes);
        File.WriteAllBytes(underLead, world.FixtureBytes);

        // And one whose name begins with a dot, which is what NuGet itself leaves in a
        // package directory -- .nupkg.metadata and .signature.p7s are both real. Nothing
        // in this file had a dotfile in the pool, and the walk enumerating them is not
        // something a reader can take on faith: filtering them out of the enumeration is
        // a plausible edit, it is what several directory-walking APIs do by default on
        // other platforms, and measured before this leftover existed it left all 23 cases
        // green while an unrecorded file stayed in the pool over an exit code of 0.
        string dotted = Path.Combine(
            Path.GetDirectoryName(world.SubjectDestination)!, ".nupkg.metadata");
        File.WriteAllBytes(dotted, world.FixtureBytes);

        var second = world.Run();
        Assert.True(second.ExitCode == 1, world.Explain(second, "a rerun whose subject cannot be acquired"));
        Assert.Equal("acquisition-failed", world.ReportedStatus(second));

        world.AssertOnlyTheLeadWasPooled(second);

        // Named, not merely gone. The sweep says which files it removed, so a run that had
        // to remove one is visible to whoever reads the manifest -- and so a leak this
        // step would otherwise quietly tidy away cannot pass for a clean pool.
        world.AssertPoolMatchesRecord([world.SubjectDestination, sibling, underLead, dotted]);
    }

    /// <summary>
    /// A sweep does not reconcile the pool until its new record is durable.
    ///
    /// <para>The first run leaves a complete pool and the record that names it. The second
    /// run would record only the lead, but its <c>assemblies.txt</c> replacement is made to
    /// fail after the replacement temporary has been written. That failure exits 2 before
    /// reconciliation, so the subject named by the first run's record must remain.
    /// Manifest invalidation is the separate contract held by
    /// <see cref="AFailedSweepLeavesNoManifestForThePoolItChanged"/>.</para>
    ///
    /// <para>A directory planted at the record path is the deterministic cross-platform
    /// way to make the atomic move fail. The fixture moves the first record aside and moves
    /// that same file back after the failed run; it does not reconstruct the expectation.
    /// Moving reconciliation ahead of the failed write makes this case fail on the missing
    /// subject.</para>
    /// </summary>
    [Fact]
    public void ASweepDoesNotReconcileBeforeItsRecordIsDurable()
    {
        using var world = SweepWorld.Create();

        var first = world.Run();
        Assert.True(first.ExitCode == 0, world.Explain(first, "a first sweep with a matching pin"));
        world.AssertPoolMatchesRecord(removals: []);

        world.PinInstead(FixturePackage, "9.9.9", FixtureTfm);

        string savedRecord = Path.Combine(world.Scratch, "assemblies.first.txt");
        File.Move(world.PooledListPath, savedRecord);
        Directory.CreateDirectory(world.PooledListPath);

        var second = world.Run();

        Directory.Delete(world.PooledListPath);
        File.Move(savedRecord, world.PooledListPath);

        Assert.True(second.ExitCode == 2, world.Explain(second, "an assemblies.txt that cannot be replaced"));
        Assert.True(
            File.Exists(world.SubjectDestination),
            "the sweep reconciled the subject before its new record was durable.");
        Assert.Equal(world.FixtureSha256, Sha256Of(world.SubjectDestination));
    }

    /// <summary>
    /// A failed sweep leaves no previous manifest claiming that changed pool bytes still
    /// hold.
    ///
    /// <para>The first run publishes a manifest naming the fixture hash. The second run
    /// legitimately pools different pinned bytes, then fails while replacing
    /// <c>assemblies.txt</c>, before it can publish its own manifest. The changed bytes
    /// prove the run crossed the pool-mutation boundary; absence of the old manifest is
    /// the only truthful artifact state at that point.</para>
    /// </summary>
    [Fact]
    public void AFailedSweepLeavesNoManifestForThePoolItChanged()
    {
        using var world = SweepWorld.Create();

        var first = world.Run();
        Assert.True(first.ExitCode == 0, world.Explain(first, "a first sweep with a matching pin"));
        Assert.Equal(world.FixtureSha256, world.ReportedEntry(first)["Sha256"]!.GetValue<string>());

        byte[] replacement = [.. world.FixtureBytes, 0];
        string replacementSha = Sha256Of(replacement);
        world.ReplaceSubjectBytesAndPin(replacement);

        string savedRecord = Path.Combine(world.Scratch, "assemblies.first.txt");
        File.Move(world.PooledListPath, savedRecord);
        Directory.CreateDirectory(world.PooledListPath);

        var second = world.Run();

        Directory.Delete(world.PooledListPath);
        File.Move(savedRecord, world.PooledListPath);

        Assert.True(second.ExitCode == 2, world.Explain(second, "a changed pool whose record cannot be replaced"));
        Assert.Equal(replacementSha, Sha256Of(world.SubjectDestination));
        Assert.False(
            File.Exists(world.ManifestPath),
            "the manifest survived after the pool bytes it described changed.");
    }

    /// <summary>
    /// A sweep removes a link planted in the pool as a link, rather than deleting what is
    /// on the far side of it.
    ///
    /// <para>The reconciliation deletes what the run did not record, so what it is willing
    /// to walk into decides what it is willing to delete. Enumerating the pool with
    /// <c>SearchOption.AllDirectories</c> -- the obvious spelling, and the one this started
    /// as -- descends through a directory symlink and yields the paths behind it, so a link
    /// under <c>packages/</c> aims the deletion at a directory the sweep does not own.
    /// Measured against that spelling: the file outside the pool was deleted and the run
    /// exited 0.</para>
    ///
    /// <para>Removing the link itself keeps the property the reconciliation is for -- the
    /// pool holds only what the record names -- with a deletion that cannot reach past the
    /// pool, because removing a symlink never touches its target.</para>
    ///
    /// <para>This is not a claim that something plants links in the pool. It is that a
    /// step which deletes recursively should be unable to delete outside the directory it
    /// owns, whatever is in it.</para>
    /// </summary>
    [Fact]
    public void ASweepRemovesALinkPlantedInThePoolWithoutDeletingWhatItPointsAt()
    {
        using var world = SweepWorld.Create();

        // Somewhere the sweep has no business touching, holding a file worth keeping.
        string outsider = Path.Combine(world.Scratch, "outside-the-pool");
        Directory.CreateDirectory(outsider);
        string bystander = Path.Combine(outsider, "keep.dll");
        File.WriteAllBytes(bystander, world.FixtureBytes);

        string link = Path.Combine(world.OutputDirectory, "packages", "linked");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        Directory.CreateSymbolicLink(link, outsider);

        var sweep = world.Run();

        Assert.True(sweep.ExitCode == 0, world.Explain(sweep, "a link planted in the pool"));

        // The whole point: the deletion stopped at the pool boundary.
        Assert.True(File.Exists(bystander), "the sweep deleted a file outside the pool.");
        Assert.True(Directory.Exists(outsider), "the sweep removed a directory outside the pool.");

        // And the pool still ends up holding only what the record names.
        Assert.False(Path.Exists(link), "the link is still in the pool.");
        world.AssertPoolMatchesRecord([link]);
    }

    /// <summary>
    /// A package that ships no primary library is refused as <c>library-unavailable</c>,
    /// rather than pooled or crashed over.
    ///
    /// <para>Nothing reached this arm before: the two packages every other case uses both
    /// ship an assembly, so <c>!selection.IsSelected</c> was never true and the whole arm
    /// below it -- this refusal, and the <c>no-library</c> pin it confirms -- could be
    /// deleted outright with every case still green. It is not a quiet corner. A run that
    /// records <c>library-unavailable</c> is what <c>--refresh-pin</c> turns into a
    /// permanent <c>no-library</c> pin, so this is the status that decides whether a
    /// package is in the pool at all from then on.</para>
    /// </summary>
    [Fact]
    public void ASweepRefusesAPackageThatShipsNoLibrary()
    {
        using var world = SweepWorld.Create();
        world.PinEmptyInstead();

        var sweep = world.Run();

        Assert.True(sweep.ExitCode == 1, world.Explain(sweep, "a package that ships no library"));
        Assert.Equal("library-unavailable", world.ReportedStatus(sweep));

        // The description the pin is later confirmed against, so the two cases below are
        // about the same fact this one records.
        Assert.Equal("NoAssemblies", world.ReportedDetail(sweep));

        world.AssertOnlyTheLeadWasPooled(sweep);
        world.AssertNoTemporaryLeftBehind();
    }

    /// <summary>
    /// A package pinned as shipping no library, that ships none, is confirmed -- accounted
    /// for rather than counted as a failure.
    ///
    /// <para>The other half of
    /// <see cref="ASweepRefusesAPackagePinnedAsShippingNoLibraryThatShipsOne"/>, and the
    /// half that makes <c>no-library</c> a claim about a package rather than a way to
    /// delete one from the pool: nine of the real top hundred are meta-packages, and this
    /// is the arm that lets the sweep account for them without pretending they contributed
    /// an assembly.</para>
    /// </summary>
    [Fact]
    public void ASweepConfirmsAPackagePinnedAsShippingNoLibraryThatShipsNone()
    {
        using var world = SweepWorld.Create();
        world.PinEmptyInstead("no-library", "NoAssemblies");

        var sweep = world.Run();

        Assert.True(sweep.ExitCode == 0, world.Explain(sweep, "a confirmed no-library pin"));
        Assert.Equal("no-library-confirmed", world.ReportedStatus(sweep));

        world.AssertOnlyTheLeadWasPooled(sweep);
        world.AssertNoTemporaryLeftBehind();
    }

    /// <summary>
    /// A <c>no-library</c> pin is confirmed against the detail it was recorded over, so a
    /// package that has since lost its candidates no longer confirms.
    ///
    /// <para>This is the check the sweep describes as its defence against a wiped or
    /// truncated cache entry: an emptied extraction reports exactly what a genuine
    /// meta-package reports, so without comparing the recorded detail the sweep would
    /// confirm a package it no longer has. Dropping the detail comparison leaves the case
    /// above green, because there the two agree; only a disagreement can tell whether it
    /// was consulted.</para>
    /// </summary>
    [Fact]
    public void ASweepDoesNotConfirmANoLibraryPinRecordedOverSomethingElse()
    {
        using var world = SweepWorld.Create();
        world.PinEmptyInstead("no-library", "NoAssemblies: Some.Candidate.dll");

        var sweep = world.Run();

        Assert.True(sweep.ExitCode == 1, world.Explain(sweep, "a no-library pin whose detail no longer holds"));

        // Refused, not confirmed: the pin describes a package that had candidates, and this
        // one has none.
        Assert.Equal("library-unavailable", world.ReportedStatus(sweep));

        world.AssertOnlyTheLeadWasPooled(sweep);
        world.AssertNoTemporaryLeftBehind();
    }

    /// <summary>
    /// A sweep that cannot reconcile the pool with its record says so, and fails.
    ///
    /// <para>The other side of <see cref="ASweepIntoAReusedDirectoryPoolsOnlyWhatItRecorded"/>:
    /// there the leftover is removed, here it cannot be. Exiting 0 would hand a consumer a
    /// pool holding an assembly the manifest does not name, which is the state the
    /// reconciliation exists to prevent -- so the failure to prevent it has to be as loud
    /// as the thing it was preventing.</para>
    ///
    /// <para>The stray sits in a directory of its own, and every package the sweep is
    /// about is acquired, copied, and recorded normally. That is deliberate and is what
    /// makes the case about the reconciliation: an earlier shape here made the
    /// <em>subject's own</em> directory unwritable, which fails the copy, and a copy
    /// failure exits 1 too -- so the case passed while the exit code under test was
    /// mutated to 0. Nothing else in this run can fail, so the exit code can only have
    /// come from the reconciliation. Measured after the rework: setting that exit code to
    /// 0 turns this case, and only this case, red.</para>
    /// </summary>
    [Fact]
    public void ASweepThatCannotReconcileThePoolSaysSoAndFails()
    {
        using var world = SweepWorld.Create();

        // A file the sweep will not record, in a directory that will not let it go. Given
        // a rank no window here reaches, so it is a leftover rather than a destination.
        string stallDirectory = Path.Combine(
            world.OutputDirectory, "packages", "999-sweep.stale", "1.0.0");
        Directory.CreateDirectory(stallDirectory);
        string stale = Path.Combine(stallDirectory, "Sweep.Stale.dll");
        File.WriteAllBytes(stale, world.FixtureBytes);
        MakeUnwritable(stallDirectory);

        // A leftover the run *can* remove, named to sort ahead of the one it cannot, so
        // the walk reaches it first. What the run managed before it failed is the part of
        // its report that a failure is most likely to lose.
        string removable = Path.Combine(
            world.OutputDirectory, "packages", "000-removable.tmp");
        File.WriteAllBytes(removable, world.FixtureBytes);

        try
        {
            var sweep = world.Run();

            Assert.True(sweep.ExitCode == 1, world.Explain(sweep, "a pool that cannot be reconciled"));

            // Still there -- which is the point, and why the run had to fail.
            Assert.True(File.Exists(stale), "the leftover was removed after all.");

            // The reason, in the manifest rather than only on stderr: a consumer reading
            // the pool reads this file, and a pool that could not be reconciled is one its
            // record misdescribes. Non-empty, not merely non-null -- measured, reporting
            // an empty string satisfied a null check and told the consumer nothing.
            Assert.NotEmpty(world.ReportedManifest(sweep)["Unreconciled"]!.GetValue<string>());

            // Exactly what left the pool, and nothing else. Both halves matter and each
            // fails on its own: a run that discards the removals it managed before the
            // failure deletes evidence off disk and omits it from the record, and a run
            // that records a removal before performing it names a file that is still
            // sitting in the pool. Measured, both left every case green -- the first by
            // clearing the list in the catch, the second by recording ahead of the delete.
            Assert.False(File.Exists(removable), "the removable leftover was not removed.");
            Assert.Equal<IEnumerable<string>>(
                [removable], world.ReportedRemovals(sweep));

            // Both packages were pooled and recorded: the run did its work, and the
            // failure is about the pool it could not clean rather than about them.
            Assert.Equal("selected", world.ReportedStatus(sweep));
            Assert.True(File.Exists(world.LeadDestination), "the lead was not pooled.");
            Assert.True(File.Exists(world.SubjectDestination), "the subject was not pooled.");
        }
        finally
        {
            RestoreWritable(stallDirectory);
        }
    }

    /// <summary>
    /// A sweep refuses a pool directory that is a link out of the output directory.
    ///
    /// <para><see cref="ASweepRemovesALinkPlantedInThePoolWithoutDeletingWhatItPointsAt"/>
    /// keeps the reconciliation from descending through a link it finds <em>in</em> the
    /// pool. That argument bounds the deletion to the pool, and is only as good as the
    /// boundary it starts from -- <c>Directory.CreateDirectory</c> is satisfied by an
    /// existing symlink, so a linked pool root put every copy and every deletion outside
    /// the output directory while the run exited 0. Measured against the code before this:
    /// the sweep wrote its assemblies into the link's target, deleted a file there, and
    /// reported success.</para>
    ///
    /// <para>Refused rather than replaced, and refused before anything is written: removing
    /// the link would be this step deleting something the caller put on a path the caller
    /// named, which is a larger liberty than declining to run.</para>
    /// </summary>
    [Fact]
    public void ASweepRefusesAPoolDirectoryThatIsALinkOutOfTheOutputDirectory()
    {
        using var world = SweepWorld.Create();

        if (OperatingSystem.IsWindows())
            Assert.Skip("planting a link needs privileges an unelevated Windows process lacks.");

        string elsewhere = Path.Combine(world.Scratch, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        string bystander = Path.Combine(elsewhere, "keep.dll");
        File.WriteAllBytes(bystander, world.FixtureBytes);

        Directory.CreateDirectory(world.OutputDirectory);
        Directory.CreateSymbolicLink(Path.Combine(world.OutputDirectory, "packages"), elsewhere);

        var sweep = world.Run();

        // The argument refusal, not a copy failure: nothing should have been attempted.
        Assert.True(sweep.ExitCode == 2, world.Explain(sweep, "a pool directory that is a link"));

        Assert.True(File.Exists(bystander), "the sweep deleted a file outside the output directory.");
        Assert.Equal(world.FixtureBytes, File.ReadAllBytes(bystander));

        // Held to the whole directory rather than to a path composed here. Naming the
        // assembly's expected location gets that location wrong -- measured: an assertion
        // on `elsewhere/Sweep.Lead.dll` passed while the sweep, with the refusal moved
        // after the copies, wrote `elsewhere/001-sweep.lead/1.0.0/Sweep.Lead.dll`. A path
        // this case builds itself is a second copy of the sweep's layout, and a wrong copy
        // asserts nothing. What is actually meant is that nothing over there changed.
        Assert.Equal<IEnumerable<string>>(
            [bystander],
            [.. Directory.GetFiles(elsewhere, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)]);
    }

    /// <summary>
    /// A sweep leaves what the caller put beside the pool alone.
    ///
    /// <para>The reconciliation is scoped to <c>packages/</c> on purpose, because the
    /// output directory is shared: <c>deep-inspect.yml</c> writes its acquisition log into
    /// the output directory, beside <c>packages/</c> rather than inside it. The scope is
    /// what keeps a step that deletes from reaching that file, and the scope is one
    /// argument -- measured, widening the walk from <c>packages/</c> to the output
    /// directory deleted a planted sibling and left all nineteen cases green.</para>
    /// </summary>
    [Fact]
    public void ASweepLeavesWhatTheCallerPutBesideThePoolAlone()
    {
        using var world = SweepWorld.Create();

        Directory.CreateDirectory(world.OutputDirectory);
        string sibling = Path.Combine(world.OutputDirectory, "acquisition.log");
        File.WriteAllBytes(sibling, world.FixtureBytes);

        var sweep = world.Run();

        Assert.True(sweep.ExitCode == 0, world.Explain(sweep, "a file beside the pool"));

        Assert.True(File.Exists(sibling), "the sweep removed a file beside the pool.");
        Assert.Equal(world.FixtureBytes, File.ReadAllBytes(sibling));

        // Nor claimed as one, which is the half a run could get wrong while leaving the
        // file alone.
        Assert.DoesNotContain(sibling, world.ReportedRemovals(sweep));
    }

    /// <summary>
    /// Removing a leftover from the pool frees the name, and not the bytes another name
    /// still refers to.
    ///
    /// <para>The reconciliation treats a hard link as the file it is indistinguishable
    /// from, which is right, and rests on unlinking a name being the only thing that
    /// happens. Nothing held it to that: a walk that truncated each leftover before
    /// unlinking it emptied a consumer's file through a shared inode and left all nineteen
    /// cases green, because from inside the pool the outcome looks identical.</para>
    ///
    /// <para>The plant here is a leftover to be reconciled away, which is a different path
    /// from
    /// <see cref="ASweepReplacesWhatIsPlantedAtItsDestinationRatherThanWritingThroughIt"/>:
    /// there the hard link stands at a destination and is replaced by a write, here it is
    /// unrecorded and is removed by the walk.</para>
    /// </summary>
    [Fact]
    public void ASweepRemovingALeftoverFreesTheNameAndNotTheBytesBehindIt()
    {
        using var world = SweepWorld.Create();

        string outside = Path.Combine(world.Scratch, "consumer-owned.bin");
        byte[] owned = [.. world.FixtureBytes.Take(64)];
        File.WriteAllBytes(outside, owned);

        string leftoverDirectory = Path.Combine(
            world.OutputDirectory, "packages", "999-sweep.stale", "1.0.0");
        Directory.CreateDirectory(leftoverDirectory);
        string leftover = Path.Combine(leftoverDirectory, "Sweep.Stale.dll");
        PlantAt(leftover, outside, Plant.HardLink);

        var sweep = world.Run();

        Assert.True(sweep.ExitCode == 0, world.Explain(sweep, "a leftover hard-linked outside the pool"));

        Assert.False(File.Exists(leftover), "the leftover was not removed.");
        Assert.Contains(leftover, world.ReportedRemovals(sweep));

        // The bytes the other name still refers to, untouched: unlinking is all that
        // happened.
        Assert.True(File.Exists(outside), "the sweep deleted a file outside the pool.");
        Assert.Equal(owned, File.ReadAllBytes(outside));
    }

    /// <summary>
    /// A leftover whose name differs from a recorded one only in case is a leftover.
    ///
    /// <para>The recorded set is compared ordinally. Comparing it any other way lets an
    /// unrecorded file pass for the recorded one it resembles and stay in the pool over an
    /// exit code of 0 -- measured, a case-insensitive comparer left all nineteen cases
    /// green while the planted file survived.</para>
    ///
    /// <para>Skipped where the filesystem cannot tell the two names apart, because there
    /// the planted file is the recorded file and the case would be asserting nothing. An
    /// earlier version of this claim said the assertion would be vacuous everywhere, which
    /// was simply wrong: where the two names coexist, only one of them is recorded.</para>
    /// </summary>
    [Fact]
    public void ASweepRemovesALeftoverThatDiffersFromARecordedNameOnlyInCase()
    {
        using var world = SweepWorld.Create();

        var first = world.Run();
        Assert.True(first.ExitCode == 0, world.Explain(first, "a first sweep with a matching pin"));

        // Beside the subject's own pooled assembly, differing from it only in case.
        string pooled = world.SubjectDestination;
        string variant = Path.Combine(
            Path.GetDirectoryName(pooled)!,
            Path.GetFileName(pooled).ToUpperInvariant());

        if (string.Equals(variant, pooled, StringComparison.Ordinal) || File.Exists(variant))
            Assert.Skip("this filesystem cannot hold both spellings, so the plant is the recorded file.");

        File.WriteAllBytes(variant, world.FixtureBytes);

        var second = world.Run();

        Assert.True(second.ExitCode == 0, world.Explain(second, "a leftover differing only in case"));

        Assert.False(File.Exists(variant), "the case-variant leftover stayed in the pool.");
        Assert.True(File.Exists(pooled), "the recorded assembly was removed instead.");
        world.AssertPoolMatchesRecord([variant]);
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

        world.AssertOnlyTheLeadWasPooled(sweep);
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
    /// <summary>
    /// Plants one of the three shapes at <paramref name="path"/>, skipping visibly where
    /// the platform will not allow it.
    /// </summary>
    static void PlantAt(string path, string target, Plant plant)
    {
        if (OperatingSystem.IsWindows())
            Assert.Skip("planting a link needs privileges an unelevated Windows process lacks.");

        if (plant != Plant.HardLink)
        {
            File.CreateSymbolicLink(path, target);
            return;
        }

        // No BCL API makes a hard link, so this is the platform's. Reported rather than
        // skipped when it fails: the file it names was just written, so there is no
        // ordinary reason for `ln` not to succeed, and a case that skipped here would
        // quietly stop covering the shape it exists for.
        using var link = Process.Start(new ProcessStartInfo("ln")
        {
            ArgumentList = { target, path },
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("could not start ln");

        string errors = link.StandardError.ReadToEnd();
        link.WaitForExit();
        Assert.True(link.ExitCode == 0, $"could not hard-link '{path}' to '{target}': {errors}");
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

        // The directory may be gone: reconciliation removes an empty pool entry, and a
        // directory made unwritable to fail a copy is exactly one that ends the run empty.
        // Removing it needs write permission on its parent, not on itself, so the mode this
        // undoes does not prevent it. Teardown, not a result -- a case that cares whether
        // the directory survived says so itself.
        if (!Directory.Exists(directory))
            return;

        File.SetUnixFileMode(
            directory,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>
    /// A repository root, a package list, a pin, a cache holding two packages, and an output
    /// directory -- everything one sweep reads and writes, all of it scratch.
    ///
    /// <para>Two packages, and the one each case is about is normally the second. A real
    /// sweep runs this loop ninety-odd times; a world holding a single package can only ever
    /// exercise the first iteration, and every guarantee here would then hold for the first
    /// package alone. Measured, on a one-package world: restricting the hash comparison, the
    /// replace-don't-write-through copy, the failure accounting, and the manifest's package
    /// identity to <c>accountedFor == 0</c> each left all cases green, while a real
    /// hundred-package sweep would pool poisoned bytes, write through a planted symlink,
    /// exit 0 over a failed copy, and refresh the wrong pin key.</para>
    ///
    /// <para>What that buys, exactly: the sweep is run over a window of two, and both ends
    /// of that window are subjects. <see cref="ASweepRefusesBytesThePinDoesNotName"/>
    /// tampers with rank 2 and
    /// <see cref="ASweepRefusesBytesThePinDoesNotNameAtTheFirstRankToo"/> with rank 1, so a
    /// mutation that spares either end is caught -- and a two-package window has no interior
    /// to hide in.</para>
    ///
    /// <para>It does <em>not</em> make the guarantee loop-wide, and this file should not be
    /// read as though it does. A mutation keyed on the index -- <c>accountedFor &lt;= 1</c>,
    /// say -- enforces the property for exactly the ranks this fixture occupies and drops it
    /// for every rank beyond, with all cases green; measured, on the byte pin, the safe copy,
    /// and the failure accounting alike. No finite fixture closes that, because the threshold
    /// simply moves. What is gated is the loop <em>body</em>, at both ends of the window it
    /// is run over; that the body is reached identically on the eighty-eight iterations no
    /// fixture here reaches is unverified, and is named here rather than left to read as
    /// covered.</para>
    ///
    /// <para>The lead is pooled successfully by every case that does not tamper with it,
    /// including the ones whose subject is refused, which is the other half of what it buys:
    /// a refusal must stop the package it is about and nothing else, and a sweep that
    /// abandoned the run would take the lead with it.</para>
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
        string? _leadCachedAssemblyPath;
        string _requestedPackage = FixturePackage;

        SweepWorld(string scratch, string cacheDirectory, byte[] fixtureBytes)
        {
            Scratch = scratch;
            CacheDirectory = cacheDirectory;
            FixtureBytes = fixtureBytes;
            FixtureSha256 = Sha256Of(fixtureBytes);

            // Distinct bytes, so a sweep that pooled one package's assembly for the other
            // fails the hash rather than passing on a coincidence.
            LeadBytes = [.. fixtureBytes, (byte)'l', (byte)'e', (byte)'a', (byte)'d'];
            LeadSha256 = Sha256Of(LeadBytes);
        }

        public string Scratch { get; }

        public string CacheDirectory { get; }

        public byte[] FixtureBytes { get; }

        /// <summary>The lead package's assembly, pooled ahead of the subject in every case.</summary>
        public byte[] LeadBytes { get; }

        public string LeadSha256 { get; }

        /// <summary>The hash of the assembly the pin names, and the only correct pool.</summary>
        public string FixtureSha256 { get; }

        /// <summary>
        /// Where the subject's assembly belongs, at rank 2. Composed here rather than in
        /// each case so the rank the subject sits at is stated once.
        /// </summary>
        public string SubjectDestination => Path.Combine(
            OutputDirectory, "packages", $"002-{FixturePackage}", FixtureVersion, FixtureAssembly);

        /// <summary>Where the lead's assembly belongs, at rank 1.</summary>
        public string LeadDestination => Path.Combine(
            OutputDirectory, "packages", $"001-{LeadPackage}", FixtureVersion, LeadAssembly);

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

        /// <summary>The same, for the lead -- how a case makes rank 1 the one being refused.</summary>
        public string LeadCachedAssemblyPath => _leadCachedAssemblyPath
            ?? throw new InvalidOperationException("The cache has not been seeded yet.");

        /// <summary>
        /// Builds the world. <paramref name="version"/> and <paramref name="tfm"/> are what
        /// the <em>pin</em> claims; the package in the cache is always the real one, so a
        /// case that changes them is changing the pin alone.
        /// </summary>
        public static SweepWorld Create(string? version = null, string? tfm = null, string? status = null)
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
                world.WriteInputs(version ?? FixtureVersion, tfm ?? FixtureTfm, status ?? "pinned");
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
            // Points this process's cache at the scratch directory only so that the commits
            // below land there. The sweep is told the same directory by environment, and
            // resolves it with this same code, so the two cannot disagree about where it is.
            //
            // This is process-global state with no reset, and it is left pointing at a
            // directory Dispose removes. That is deliberate: no other type in this test
            // assembly references CoreCache or NuGetCache, and if one ever does, resolving
            // against a removed directory fails loudly, where leaving a usable scratch
            // cache behind would quietly serve it this fixture instead.
            NuGetCache.Initialize("dotnet-inspect", CacheDirectory, skipNuGetCache: true);

            _leadCachedAssemblyPath = Commit(LeadPackage, LeadAssembly, LeadBytes);
            _cachedAssemblyPath = Commit(FixturePackage, FixtureAssembly, FixtureBytes);
            CommitWithoutLibrary(EmptyPackage);
        }

        /// <summary>
        /// Stages the part of a synthetic package that is not its library -- the directory
        /// and the nuspec -- and answers with the staged path for a caller to fill in.
        /// </summary>
        string Stage(string package)
        {
            string staged = Path.Combine(Scratch, "staged", package);
            Directory.CreateDirectory(staged);
            File.WriteAllText(
                Path.Combine(staged, $"{package}.nuspec"),
                $"""
                <?xml version="1.0" encoding="utf-8"?>
                <package><metadata>
                  <id>{package}</id><version>{FixtureVersion}</version>
                  <authors>{nameof(EvilPoolSweepGateTests)}</authors>
                  <description>A synthetic package that exists only to be pooled.</description>
                </metadata></package>
                """);

            return staged;
        }

        /// <summary>
        /// Commits a package that ships a nuspec and no library, so that the sweep's
        /// <c>!IsSelected</c> arm has something to be about.
        /// </summary>
        void CommitWithoutLibrary(string package) =>
            NuGetCache.CommitPackage(Stage(package), null, package, FixtureVersion, TestSourceKey);

        /// <summary>
        /// Stages one synthetic package and commits it, answering with the path the product
        /// put its assembly at.
        ///
        /// <para>The path is the product's, not one composed here: the directory a committed
        /// package lands in is the product's to decide, down to the casing it applies to the
        /// name and version. A product that moved its cache layout fails here, naming the
        /// path it did not write, rather than downstream as a sweep that mysteriously
        /// ignored a tamper.</para>
        /// </summary>
        string Commit(string package, string assembly, byte[] bytes)
        {
            string staged = Stage(package);
            Directory.CreateDirectory(Path.Combine(staged, "lib", FixtureTfm));
            File.WriteAllBytes(Path.Combine(staged, "lib", FixtureTfm, assembly), bytes);

            NuGetCache.CommitPackage(staged, null, package, FixtureVersion, TestSourceKey);

            string committed = Path.Combine(
                NuGetCache.GetPackageCachePath(package, FixtureVersion, TestSourceKey),
                "lib",
                FixtureTfm,
                assembly);

            Assert.True(
                File.Exists(committed),
                $"The committed package '{package}' does not hold its assembly at {committed}.");

            return committed;
        }

        void WriteInputs(string pinnedVersion, string pinnedTfm, string pinnedStatus)
        {
            string data = Path.Combine(FakeRoot, "docs", "data");
            Directory.CreateDirectory(data);

            // What makes this directory a repository root, and so what makes the sweep read
            // the two files beside it instead of the committed ones.
            File.WriteAllText(Path.Combine(FakeRoot, "dotnet-inspect.slnx"), "");

            WriteListAndPin(data, FixturePackage, pinnedVersion, pinnedTfm, FixtureSha256, pinnedStatus);
        }

        /// <summary>
        /// Writes the list and the pin the sweep reads: the lead at rank 1, always correct,
        /// and the subject at rank 2 with whatever this case is asking for.
        ///
        /// <para>A <c>no-library</c> pin carries no hash, because there is no assembly for
        /// one to describe -- which is what <c>EvilPoolPinTests</c> holds the committed pin
        /// file to, so writing one here would be a fixture the real file could not be.</para>
        /// </summary>
        void WriteListAndPin(
            string data,
            string package,
            string version,
            string? tfm,
            string sha256,
            string status = "pinned",
            string? detail = null)
        {
            var list = new JsonArray(
                new JsonObject
                {
                    ["rank"] = 1,
                    ["package"] = LeadPackage,
                    ["downloads"] = 2,
                },
                new JsonObject
                {
                    ["rank"] = 2,
                    ["package"] = package,
                    ["downloads"] = 1,
                });
            var pin = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["packages"] = new JsonArray(
                    new JsonObject
                    {
                        ["package"] = LeadPackage,
                        ["version"] = FixtureVersion,
                        ["tfm"] = FixtureTfm,
                        ["status"] = "pinned",
                        ["detail"] = null,
                        ["sha256"] = LeadSha256,
                    },
                    new JsonObject
                    {
                        ["package"] = package,
                        ["version"] = version,
                        ["tfm"] = tfm,
                        ["status"] = status,
                        ["detail"] = detail ?? (status == "no-library" ? "ships no primary library" : null),
                        ["sha256"] = status == "no-library" ? null : sha256,
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
        public string ReportedStatus(
            (int ExitCode, string Output, string Errors) sweep, string? package = null) =>
            ReportedEntry(sweep, package)["Status"]!.GetValue<string>();

        /// <summary>
        /// The manifest row about the package this world asked for, having established that it
        /// is about that package and not the lead beside it.
        ///
        /// <para>Every read of a package row goes through here, which is the point. A status
        /// is a value, not a subject, so a row reporting the right outcome for some other
        /// package satisfies any reader that takes the value and never asks whose it is --
        /// measured: rewriting every recorded package name left all nine cases green, and
        /// after the check was added to the status reader alone it still left two of them
        /// green, because those two indexed the array themselves. One door, so a case
        /// cannot get the value without the question having been put. <see cref="ReportedManifest"/>
        /// is the parse this is built on and reaches no row; a reader wanting one comes
        /// here.</para>
        /// </summary>
        public JsonNode ReportedEntry(
            (int ExitCode, string Output, string Errors) sweep, string? package = null)
        {
            string subject = package ?? _requestedPackage;
            var packages = ReportedManifest(sweep)["Packages"]!.AsArray();
            Assert.True(packages.Count == 2, Explain(sweep, $"a manifest holding {packages.Count} packages"));

            var matching = packages
                .Where(row => row!["RequestedPackage"]?.GetValue<string>() == subject)
                .ToArray();
            Assert.True(
                matching.Length == 1,
                Explain(
                    sweep,
                    $"a manifest with {matching.Length} rows about '{subject}', reporting on "
                    + string.Join(
                        ", ",
                        packages.Select(row => $"'{row!["RequestedPackage"]?.GetValue<string>()}'"))));

            return matching[0]!;
        }

        /// <summary>
        /// The pool holds the lead's assembly and nothing else -- what a case asserts when
        /// its subject was refused.
        ///
        /// <para>Stronger than an empty pool, and the reason the lead is there: a refusal
        /// has to stop the package it is about and leave the rest of the run alone. A sweep
        /// that abandoned everything on the first refusal, or one that pooled the subject
        /// anyway under the lead's name, both satisfy "the subject's assembly is absent".</para>
        /// </summary>
        public void AssertOnlyTheLeadWasPooled((int ExitCode, string Output, string Errors) sweep)
        {
            Assert.Equal([LeadDestination], PooledAssemblies(OutputDirectory));
            Assert.Equal(LeadSha256, Sha256Of(LeadDestination));
            Assert.Equal(1, ReportedManifest(sweep)["SelectedPackageCount"]!.GetValue<int>());
        }

        /// <summary>
        /// The manifest as a whole, for the fields that are not a package row -- the
        /// aggregates, which have no subject to ask about. Read a row through
        /// <see cref="ReportedEntry"/> instead.
        /// </summary>
        public JsonNode ReportedManifest((int ExitCode, string Output, string Errors) sweep)
        {
            Assert.True(File.Exists(ManifestPath), Explain(sweep, "a run that wrote no manifest"));
            return JsonNode.Parse(File.ReadAllText(ManifestPath))!;
        }

        /// <summary>
        /// The paths the sweep recorded removing from the pool, in the order it recorded
        /// them. The manifest is the gate on those removals; the stderr line beside each
        /// one is an operator convenience and is not asserted anywhere.
        /// </summary>
        public string[] ReportedRemovals((int ExitCode, string Output, string Errors) sweep) =>
            [.. (ReportedManifest(sweep)["RemovedFromPool"]?.AsArray() ?? [])
                .Select(removed => removed!.GetValue<string>())];

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
        /// The detail the sweep recorded beside the status -- for the <c>!IsSelected</c>
        /// arm, its description of what it found instead of a library, which a
        /// <c>no-library</c> pin is confirmed against.
        /// </summary>
        public string? ReportedDetail((int ExitCode, string Output, string Errors) sweep) =>
            ReportedEntry(sweep)["Detail"]?.GetValue<string>();

        /// <summary>
        /// Replaces the inputs so the sweep is asked for some other package, pinned to
        /// bytes nothing has. Used to ask for something the scratch cache cannot answer.
        /// </summary>
        public void PinInstead(string package, string version, string tfm)
        {
            _requestedPackage = package;
            WriteListAndPin(
                Path.Combine(FakeRoot, "docs", "data"), package, version, tfm, FixtureSha256);
        }

        /// <summary>
        /// Replaces the subject's cached assembly and updates its pin to name those bytes,
        /// so the next sweep legitimately changes an existing pool destination.
        /// </summary>
        public void ReplaceSubjectBytesAndPin(byte[] bytes)
        {
            File.WriteAllBytes(CachedAssemblyPath, bytes);
            WriteListAndPin(
                Path.Combine(FakeRoot, "docs", "data"), FixturePackage, FixtureVersion, FixtureTfm,
                Sha256Of(bytes));
        }

        /// <summary>
        /// Replaces the inputs so the sweep is asked for the package that ships no library,
        /// pinned however this case needs.
        /// </summary>
        public void PinEmptyInstead(string status = "pinned", string? detail = null)
        {
            _requestedPackage = EmptyPackage;
            WriteListAndPin(
                Path.Combine(FakeRoot, "docs", "data"), EmptyPackage, FixtureVersion, FixtureTfm,
                FixtureSha256, status, detail);
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

            // Ranks 1 through 2: the lead and then the subject. The window has to cover
            // both, and the sweep refuses a window its list does not rank, so a world that
            // stopped writing the lead fails here rather than quietly narrowing back to one
            // package and taking the coverage of every later iteration with it.
            startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add("2");

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
        public void AssertNoTemporaryLeftBehind() => AssertPoolMatchesRecord(removals: []);

        /// <summary>
        /// The pool holds exactly what the sweep recorded, and the sweep says which files
        /// it had to remove to make that true.
        ///
        /// <para>The removals are the half a set difference over the disk cannot see. The
        /// sweep deletes unrecorded files under <c>packages/</c>, and a write temporary it
        /// leaked is an unrecorded file under <c>packages/</c> -- so once it started
        /// reconciling, a sweep that skipped its cleanup and still reported the temporary
        /// as <c>removed</c> had the evidence swept up behind it, and the disk check that
        /// used to catch exactly that passed. Measured: with the removals unreported, that
        /// mutation left all seventeen cases green, and reverting the reconciliation turned
        /// it red again. Asking what the sweep removed puts the fact back, and takes it
        /// from the sweep's own record rather than from a name this harness would have to
        /// know.</para>
        /// </summary>
        public void AssertPoolMatchesRecord(string[] removals)
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

            // Directories, not only files. This assertion read the pool with
            // `Directory.GetFiles` alone for its whole life, so it could not see a pool
            // entry that holds nothing -- and reconciliation, which descended into a
            // directory but never removed one, left a `<rank>-<id>/<version>/` skeleton
            // standing wherever a package was refused, failed to copy, or was pooled by an
            // earlier run and not this one. To anything enumerating the pool by directory
            // that skeleton is a package that shipped nothing, which is the state a
            // recorded-only pool is supposed to make unrepresentable; the central check
            // that the pool equals its record was structurally unable to say so.
            //
            // The expectation is derived from the record rather than composed here: the
            // directories a pool may contain are exactly the ancestors of the files the
            // sweep says it pooled. A test that instead named the directories it expected
            // would be agreeing with itself, which is how three separate cases in this
            // file came to gate nothing.
            string[] presentDirectories =
                [.. Directory.GetDirectories(OutputDirectory, "*", SearchOption.AllDirectories)
                    .Order(StringComparer.Ordinal)];
            var expectedDirectories = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string pooled in expected)
            {
                for (string? at = Path.GetDirectoryName(pooled);
                    at is not null && at.Length > OutputDirectory.Length;
                    at = Path.GetDirectoryName(at))
                {
                    expectedDirectories.Add(at);
                }
            }

            Assert.Equal<IEnumerable<string>>(expectedDirectories, presentDirectories);

            var manifest = JsonNode.Parse(File.ReadAllText(ManifestPath))!;
            Assert.Null(manifest["Unreconciled"]?.GetValue<string>());
            string[] recordedRemovals =
                [.. (manifest["RemovedFromPool"]?.AsArray() ?? [])
                    .Select(removed => removed!.GetValue<string>())];
            Assert.Equal<IEnumerable<string>>(
                [.. removals.Order(StringComparer.Ordinal)], recordedRemovals);
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
