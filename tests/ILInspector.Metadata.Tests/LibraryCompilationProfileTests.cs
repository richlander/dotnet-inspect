using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;

using ReleaseFixtures =
    ILInspector.Metadata.StateMachineFixtures.StateMachineFixtures;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Compilation profile gate for
/// docs/design/method-classification-analyzers.md#compilation-profile-facts,
/// adopting docs/design/notable-findings.md#library-explained-facts and
/// #expectation: the Debug fact reads the Debuggable attribute's arguments,
/// and the async facts count every method with a body at every accessibility.
/// </summary>
public sealed class LibraryCompilationProfileTests
{
    [Fact]
    public void ReleaseBuild_IsOptimized_AndHasNoRuntimeAsync()
    {
        using var pe = Open(typeof(ReleaseFixtures).Assembly.Location);
        MetadataReader reader = pe.GetMetadataReader();

        LibraryCompilationProfile profile = LibraryCompilationProfileScanner.Scan(reader);

        Assert.False(profile.IsDebug);
        Assert.False(profile.HasRuntimeAsync);
        Assert.True(profile.HasStateMachineAsync);
        Assert.Equal(1, profile.AsyncIteratorMethods);
        Assert.Equal(2, profile.IteratorMethods);
        Assert.Equal(0, profile.UnexpectedStateMachineAsyncMethods);
        Assert.False(profile.ExplainsStateMachine(StateMachineClaimKind.ClassicAsync));
    }

    [Fact]
    public void DebugBuild_ExplainsClassicAsync_AndNothingElse()
    {
        using var pe = Open(FixtureCatalog.AssemblyPath(FixtureIds.MetadataStateMachinesDebug));
        MetadataReader reader = pe.GetMetadataReader();

        LibraryCompilationProfile profile = LibraryCompilationProfileScanner.Scan(reader);

        Assert.True(profile.IsDebug);
        Assert.True(profile.ExplainsStateMachine(StateMachineClaimKind.ClassicAsync));
        Assert.False(profile.ExplainsStateMachine(StateMachineClaimKind.AsyncIterator));
        Assert.False(profile.ExplainsStateMachine(StateMachineClaimKind.Iterator));
        Assert.False(profile.ExplainsStateMachine(null));
    }

    [Fact]
    public void DebugAndReleaseTwins_AgreeOnEveryClaim_AndDifferInTheDebugFact()
    {
        using var release = Open(typeof(ReleaseFixtures).Assembly.Location);
        using var debug = Open(FixtureCatalog.AssemblyPath(FixtureIds.MetadataStateMachinesDebug));

        LibraryCompilationProfile releaseProfile = LibraryCompilationProfileScanner.Scan(release.GetMetadataReader());
        LibraryCompilationProfile debugProfile = LibraryCompilationProfileScanner.Scan(debug.GetMetadataReader());

        // Debug lowering emits more bodies (uncached lambdas, display-class
        // members), so the body count differs; the claimed state machines do not.
        Assert.False(releaseProfile.IsDebug);
        Assert.True(debugProfile.IsDebug);
        Assert.Equal(Claims(releaseProfile), Claims(debugProfile));
        Assert.True(debugProfile.MethodsWithBody >= releaseProfile.MethodsWithBody);
    }

    static (int RuntimeAsync, int StateMachineAsync, int AsyncIterators, int Iterators) Claims(LibraryCompilationProfile profile) =>
        (profile.RuntimeAsyncMethods, profile.StateMachineAsyncMethods, profile.AsyncIteratorMethods, profile.IteratorMethods);

    [Fact]
    public void RuntimeAsyncLibrary_CountsItsInternalRuntimeAsync_AndTheStateMachinesLeftBehind()
    {
        string path = FixtureCatalog.AssemblyPath(FixtureIds.MetadataCompilationProfileRuntimeAsync);
        using var pe = Open(path);
        MetadataReader reader = pe.GetMetadataReader();

        LibraryCompilationProfile profile = LibraryCompilationProfileScanner.Scan(reader);

        Assert.False(profile.IsDebug);
        Assert.Equal(1, profile.RuntimeAsyncMethods);
        Assert.Equal(2, profile.StateMachineAsyncMethods);
        Assert.Equal(1, profile.AsyncIteratorMethods);
        Assert.Equal(1, profile.IteratorMethods);
        Assert.Equal(2, profile.UnexpectedStateMachineAsyncMethods);

        // The public presence flags summarize public non-accessor methods only,
        // so they miss the internal runtime-async method the profile reads.
        PresenceFlags flags = AssemblyDetailScanner.ScanPresenceFlags(pe);
        Assert.False(flags.HasRuntimeAsync);
        Assert.True(flags.HasStateMachineAsync);
    }

    [Theory]
    [InlineData(nameof(ReleaseFixtures.ClassicAsync), StateMachineClaimKind.ClassicAsync)]
    [InlineData(nameof(ReleaseFixtures.ExplicitAsync), StateMachineClaimKind.ClassicAsync)]
    [InlineData(nameof(ReleaseFixtures.AsyncIterator), StateMachineClaimKind.AsyncIterator)]
    [InlineData(nameof(ReleaseFixtures.Iterator), StateMachineClaimKind.Iterator)]
    [InlineData(nameof(ReleaseFixtures.Synchronous), null)]
    [InlineData(nameof(ReleaseFixtures.AsyncLocalFunction), null)]
    public void ClassifyStateMachine_ReadsTheMethodsOwnClaim(string name, StateMachineClaimKind? expected)
    {
        using var pe = Open(typeof(ReleaseFixtures).Assembly.Location);
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinition method = reader.GetMethodDefinition(FindMethod(reader, name));

        Assert.Equal(expected, MethodClassificationScanner.ClassifyStateMachine(reader, method));
    }

    [Theory]
    [InlineData(FixtureIds.MetadataStateMachinesDebug)]
    [InlineData(FixtureIds.MetadataCompilationProfileRuntimeAsync)]
    [InlineData(FixtureIds.MetadataEnablements)]
    public void Profile_EqualsTheMaterializedClassificationOverEveryBody(string fixtureId)
    {
        using var pe = Open(FixtureCatalog.AssemblyPath(fixtureId));
        MetadataReader reader = pe.GetMetadataReader();

        LibraryCompilationProfile profile = LibraryCompilationProfileScanner.Scan(reader);

        Assert.Equal(Oracle(reader) with { IsDebug = profile.IsDebug }, profile);
    }

    [Fact]
    public void EnablementFixture_ReadsItsClosureLambdaAsRuntimeAsync()
    {
        using var pe = Open(FixtureCatalog.AssemblyPath(FixtureIds.MetadataEnablements));

        LibraryCompilationProfile profile = LibraryCompilationProfileScanner.Scan(pe.GetMetadataReader());

        Assert.True(profile.HasRuntimeAsync);
        Assert.False(AssemblyDetailScanner.ScanPresenceFlags(pe).HasRuntimeAsync);
    }

    /// <summary>
    /// The materialized classification, which the legacy oracle also uses,
    /// walked over every method with a body.
    /// </summary>
    static LibraryCompilationProfile Oracle(MetadataReader reader)
    {
        int bodies = 0, runtimeAsync = 0, stateMachineAsync = 0, asyncIterators = 0, iterators = 0;
        foreach (MethodDefinitionHandle handle in reader.MethodDefinitions)
        {
            MethodDefinition method = reader.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0)
                continue;
            bodies++;
            switch (MethodClassificationScanner.ClassifyAsyncMethod(reader, method))
            {
                case MethodClassification.RuntimeAsync:
                    runtimeAsync++;
                    break;
                case MethodClassification.StateMachineAsync:
                    stateMachineAsync++;
                    if (AttributeReader.HasAttribute(reader, method.GetCustomAttributes(), KnownAttributeNames.AsyncIteratorStateMachineAttribute))
                        asyncIterators++;
                    break;
                default:
                    if (AttributeReader.HasAttribute(reader, method.GetCustomAttributes(), KnownAttributeNames.IteratorStateMachineAttribute))
                        iterators++;
                    break;
            }
        }

        return new LibraryCompilationProfile(false, bodies, runtimeAsync, stateMachineAsync, asyncIterators, iterators);
    }

    static PEReader Open(string path) =>
        new(File.OpenRead(path));

    static MethodDefinitionHandle FindMethod(MetadataReader reader, string name)
    {
        foreach (MethodDefinitionHandle handle in reader.MethodDefinitions)
        {
            if (reader.StringComparer.Equals(reader.GetMethodDefinition(handle).Name, name))
                return handle;
        }

        throw new InvalidOperationException($"Method '{name}' not found.");
    }
}
