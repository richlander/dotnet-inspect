using System.IO.Compression;
using DotnetInspector.Inspectors;
using DotnetInspector.Output;

namespace DotnetInspector.Tests;

public class CallerScopeResolverTests
{
    [Fact]
    public async Task ResolveAsync_CallerPackageKeepsExtractedAssembliesUntilDisposed()
    {
        var packageDir = Directory.CreateTempSubdirectory("caller-scope-package-test").FullName;
        var packagePath = Path.Combine(packageDir, "CallerScope.1.0.0.nupkg");
        var sourceAssembly = typeof(CallerScopeResolverTests).Assembly.Location;

        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(sourceAssembly, "lib/net10.0/CallerScope.dll");
            }

            using var httpClient = new HttpClient();
            var assemblySet = await CallerScopeResolver.ResolveAsync(
                directories: [],
                projects: [],
                packages: [packagePath],
                tfm: "net10.0",
                ownAssemblyPath: null,
                httpClient,
                new VerboseLogger(enabled: false));

            var assemblyPath = Assert.Single(assemblySet.Assemblies);
            Assert.True(File.Exists(assemblyPath));

            assemblySet.Dispose();

            Assert.False(File.Exists(assemblyPath));
        }
        finally
        {
            Directory.Delete(packageDir, recursive: true);
        }
    }

    /// <summary>
    /// Two assemblies in a scope directory whose names differ only in case are two assemblies.
    ///
    /// <para>The resolver deduplicates by full path, which is right, but it compared those paths
    /// case-insensitively — so on a case-sensitive volume the second file was discarded before any
    /// caller analysis ran and its callers could never be found. This is the first path comparison
    /// in the chain that #3419's forwarded-caller work depends on; the layers above it cannot
    /// recover a candidate that never arrives. Found in review of <c>32951519</c>.</para>
    /// </summary>
    [Fact]
    public async Task ResolveAsync_KeepsTwoAssembliesWhosePathsDifferOnlyInCase()
    {
        var directory = Directory.CreateTempSubdirectory("caller-scope-case-test").FullName;
        try
        {
            Assert.SkipUnless(
                IsCaseSensitive(directory),
                "Needs a case-sensitive filesystem; CI runs one.");

            var source = typeof(CallerScopeResolverTests).Assembly.Location;
            File.Copy(source, Path.Combine(directory, "Alpha.dll"));
            File.Copy(source, Path.Combine(directory, "alpha.dll"));

            using var httpClient = new HttpClient();
            using var assemblySet = await CallerScopeResolver.ResolveAsync(
                directories: [directory],
                projects: [],
                packages: [],
                tfm: null,
                ownAssemblyPath: null,
                httpClient,
                new VerboseLogger(enabled: false));

            Assert.Equal(2, assemblySet.Assemblies.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// One physical file supplied under two spellings is one caller, even when the filesystem
    /// resolved both spellings to it.
    ///
    /// <para>Round 12 changed this dedup from <c>OrdinalIgnoreCase</c> to <c>Ordinal</c> so that
    /// two genuinely distinct files on a case-sensitive volume would both be scanned. That is
    /// right, but comparing the strings exactly is the wrong way to get it: on a case-insensitive
    /// volume <c>Out\Caller.dll</c> and <c>out\caller.dll</c> are two strings for one file, so the
    /// resolver returned both, the same image was opened twice, and every call site in it was
    /// reported twice. Two scope arguments differing only in case is enough to trigger it. Found in
    /// review of <c>37a4444b</c>.</para>
    ///
    /// <para>The fix asks the filesystem which spelling the entry actually has rather than
    /// assuming a platform rule, so this test is meaningful on either kind of volume: where the two
    /// directory spellings name one directory there is one caller, and the companion test above
    /// keeps genuinely distinct files apart where they can exist.</para>
    /// </summary>
    [Fact]
    public async Task ResolveAsync_CountsOnePhysicalFileOnceAcrossTwoSpellingsOfItsDirectory()
    {
        var directory = Directory.CreateTempSubdirectory("caller-scope-spelling-test").FullName;
        try
        {
            var source = typeof(CallerScopeResolverTests).Assembly.Location;
            File.Copy(source, Path.Combine(directory, "Caller.dll"));

            string swapped = SwapLeafCase(directory);
            Assert.SkipUnless(
                Directory.Exists(swapped),
                "Needs a volume that resolves the directory under both spellings.");

            using var httpClient = new HttpClient();
            using var assemblySet = await CallerScopeResolver.ResolveAsync(
                directories: [directory, swapped],
                projects: [],
                packages: [],
                tfm: null,
                ownAssemblyPath: null,
                httpClient,
                new VerboseLogger(enabled: false));

            Assert.Single(assemblySet.Assemblies);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The gate for the wildcard rule, staged so that exactly one candidate can be returned: the
    /// decoy is on disk and the wildcard-named file is not, so the enumeration's unspecified
    /// ordering cannot decide the outcome. Canonicalizing <c>ab&lt;.dll</c> onto <c>abc.dll</c>
    /// here would collapse two distinct assemblies to one scope entry and lose the second's
    /// callers.
    ///
    /// <para>This asks the product function directly because the end-to-end test below cannot gate
    /// the rule: staging it needs the wildcard-named file to exist, and a wildcard character also
    /// matches itself, so that file is always a second candidate and the pre-fix behavior depends
    /// on which one <c>Directory.GetFiles</c> yields first (reported in review of
    /// <c>e7c04f92</c>). Nothing here needs a permissive filesystem — the wildcard name is only
    /// ever a string — so unlike that test, this one runs everywhere.</para>
    /// </summary>
    [Fact]
    public void MatchedEntry_DoesNotResolveAWildcardNameOntoADifferentFile()
    {
        var directory = Directory.CreateTempSubdirectory("caller-scope-wildcard-unit").FullName;
        try
        {
            File.Copy(typeof(CallerScopeResolverTests).Assembly.Location, Path.Combine(directory, "abc.dll"));

            // The positive control, and it has to be chosen per volume. Case is the one difference
            // canonicalization exists to absorb, so on a case-insensitive volume folding `ABC.DLL`
            // onto the spelling on disk is proof the rule is not simply declining everything. On a
            // case-sensitive volume there is nothing to fold — `ABC.DLL` names a file that does not
            // exist, and returning it unchanged is the right answer — so the control there is that
            // two spellings which really are two files stay two.
            if (IsCaseSensitive(directory))
            {
                File.Copy(typeof(CallerScopeResolverTests).Assembly.Location, Path.Combine(directory, "ABC.DLL"));
                Assert.Equal("abc.dll", CallerScopeResolver.MatchedEntry(directory, "abc.dll", files: true));
                Assert.Equal("ABC.DLL", CallerScopeResolver.MatchedEntry(directory, "ABC.DLL", files: true));
            }
            else
            {
                Assert.Equal("abc.dll", CallerScopeResolver.MatchedEntry(directory, "ABC.DLL", files: true));
            }

            Assert.Equal("ab<.dll", CallerScopeResolver.MatchedEntry(directory, "ab<.dll", files: true));
            Assert.Equal("ab?.dll", CallerScopeResolver.MatchedEntry(directory, "ab?.dll", files: true));
            Assert.Equal("ab*.dll", CallerScopeResolver.MatchedEntry(directory, "ab*.dll", files: true));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A file whose own name contains a character the enumeration API treats as a wildcard must
    /// not be canonicalized onto a *different* file that the wildcard happens to match. Doing so
    /// would collapse two distinct assemblies to one scope entry and lose the second's callers.
    ///
    /// <para>Windows forbids every Win32 wildcard character in a file name, so this can only be
    /// staged where the filesystem permits one — which is where CI runs.</para>
    ///
    /// <para>This is the wiring proof, not the gate: with both files present the outcome before the
    /// fix depended on enumeration order, which is unspecified. The gate is
    /// <see cref="MatchedEntry_DoesNotResolveAWildcardNameOntoADifferentFile"/>.</para>
    /// </summary>
    [Fact]
    public async Task ResolveAsync_DoesNotCanonicalizeAWildcardNamedFileOntoADifferentFile()
    {
        var directory = Directory.CreateTempSubdirectory("caller-scope-wildcard-test").FullName;
        try
        {
            var source = typeof(CallerScopeResolverTests).Assembly.Location;
            var decoy = Path.Combine(directory, "abc.dll");
            var wildcard = Path.Combine(directory, "ab<.dll");

            File.Copy(source, decoy);
            try
            {
                File.Copy(source, wildcard);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
            {
                Assert.Skip("Needs a filesystem that permits a Win32 wildcard character in a file name.");
            }

            using var httpClient = new HttpClient();
            using var assemblySet = await CallerScopeResolver.ResolveAsync(
                directories: [directory],
                projects: [],
                packages: [],
                tfm: null,
                ownAssemblyPath: null,
                httpClient,
                new VerboseLogger(enabled: false));

            Assert.Equal(2, assemblySet.Assemblies.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A scope that names one assembly both directly and through a symbolic link must scan it
    /// once. Left unresolved, the link and its target are two different paths under any comparer,
    /// so every call site in the file is reported twice.
    ///
    /// <para>Creating a symbolic link needs Developer Mode or elevation on Windows, so this skips
    /// where the OS refuses.</para>
    /// </summary>
    [Fact]
    public async Task ResolveAsync_CountsOnePhysicalFileOnceWhenAlsoReachedThroughASymbolicLink()
    {
        var directory = Directory.CreateTempSubdirectory("caller-scope-link-test").FullName;
        try
        {
            var source = typeof(CallerScopeResolverTests).Assembly.Location;
            var target = Path.Combine(directory, "Caller.dll");
            File.Copy(source, target);

            try
            {
                File.CreateSymbolicLink(Path.Combine(directory, "Link.dll"), target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Assert.Skip("Needs permission to create a symbolic link.");
            }

            using var httpClient = new HttpClient();
            using var assemblySet = await CallerScopeResolver.ResolveAsync(
                directories: [directory],
                projects: [],
                packages: [],
                tfm: null,
                ownAssemblyPath: null,
                httpClient,
                new VerboseLogger(enabled: false));

            Assert.Single(assemblySet.Assemblies);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A scope naming one assembly both directly and through a linked *directory* must scan it
    /// once. A directory symbolic link or junction is a reparse point on the directory, so
    /// resolving the file inside it reports "not a link" — the link has to be followed at the
    /// segment that carries it, which is the common shape (a symlinked framework or SDK directory).
    /// </summary>
    [Fact]
    public async Task ResolveAsync_CountsOnePhysicalFileOnceWhenAlsoReachedThroughALinkedDirectory()
    {
        var root = Directory.CreateTempSubdirectory("caller-scope-dirlink-test").FullName;
        try
        {
            var real = Path.Combine(root, "real");
            Directory.CreateDirectory(real);
            File.Copy(typeof(CallerScopeResolverTests).Assembly.Location, Path.Combine(real, "Caller.dll"));

            var link = Path.Combine(root, "link");
            try
            {
                Directory.CreateSymbolicLink(link, real);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Assert.Skip("Needs permission to create a directory symbolic link.");
            }

            using var httpClient = new HttpClient();
            using var assemblySet = await CallerScopeResolver.ResolveAsync(
                directories: [real, link],
                projects: [],
                packages: [],
                tfm: null,
                ownAssemblyPath: null,
                httpClient,
                new VerboseLogger(enabled: false));

            Assert.Single(assemblySet.Assemblies);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The same path with the case of its last segment inverted.</summary>
    static string SwapLeafCase(string path)
    {
        string leaf = Path.GetFileName(path);
        string swapped = new([.. leaf.Select(c => char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c))]);
        return Path.Combine(Path.GetDirectoryName(path)!, swapped);
    }

    /// <summary>
    /// Whether <paramref name="directory"/> distinguishes names that differ only in case, asked of
    /// the filesystem rather than of the operating system: Windows carries per-directory case
    /// sensitivity, so the answer is a property of the path and not of the platform.
    /// </summary>
    static bool IsCaseSensitive(string directory)
    {
        string probe = Path.Combine(directory, "case-probe.tmp");
        File.WriteAllText(probe, "");
        try
        {
            return !File.Exists(Path.Combine(directory, "CASE-PROBE.TMP"));
        }
        finally
        {
            File.Delete(probe);
        }
    }
}
