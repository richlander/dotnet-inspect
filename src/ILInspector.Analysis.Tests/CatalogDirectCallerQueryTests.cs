using System.Collections.Immutable;
using System.Xml;
using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public class CatalogDirectCallerQueryTests
{
    static XmlReader WrapThroughFacade(XmlReader reader) =>
        XmlReader.Create(reader, new XmlReaderSettings());

    [Fact]
    public void ForwardedParameterTypesJoinCompleteMemberSignatures()
    {
        string? targetPath = PrivateXmlPath();
        Assert.SkipWhen(
            targetPath is null,
            "System.Private.Xml not in the runtime directory.");

        ImmutableArray<CatalogDirectCaller> callers = FindFrameworkCallers(
            targetPath!,
            "XmlReader",
            "XmlReaderSettings");

        Assert.Contains(
            callers,
            caller => caller.Call.Caller.Name
                == nameof(WrapThroughFacade));
    }

    [Fact]
    public void ForwardedParameterTypesDoNotJoinCloseOverloads()
    {
        string? targetPath = PrivateXmlPath();
        Assert.SkipWhen(
            targetPath is null,
            "System.Private.Xml not in the runtime directory.");

        ImmutableArray<CatalogDirectCaller> callers = FindFrameworkCallers(
            targetPath!,
            "Stream",
            "XmlReaderSettings");

        Assert.DoesNotContain(
            callers,
            caller => caller.Call.Caller.Name
                == nameof(WrapThroughFacade));
    }

    [Fact]
    public void ConstructedGenericCallJoinsOpenDefinition()
    {
        LibraryBodyIndex target = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        LibraryBodyIndex source = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        MethodIdentity store = target.DeclaredMethods.Single(method =>
            method.DeclaringType.Name == "Box`1"
            && method.Name == "Store"
            && method.ParameterTypes is [{ Kind: TypeRefKind.GenericParameter }]);

        ImmutableArray<CatalogDirectCaller> callers = Find(
            target,
            store.MetadataToken,
            source,
            new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(target.Path)),
            new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(source.Path)));

        Assert.Contains(
            callers,
            caller => caller.Call.Caller.Name == "UseBox");
        Assert.DoesNotContain(
            callers,
            caller => caller.Call.Caller.Name == "UseBoxList");
    }

    [Fact]
    public void UnavailableCorrespondenceDoesNotFabricateCaller()
    {
        LibraryBodyIndex target = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath());
        LibraryBodyIndex source = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        MethodIdentity ping = target.DeclaredMethods.Single(method =>
            method.DeclaringType.Name == "Api"
            && method.Name == "Ping"
            && method.ParameterTypes.IsEmpty);

        ImmutableArray<CatalogDirectCaller> callers = Find(
            target,
            ping.MetadataToken,
            source,
            UnavailablePolicy.Instance,
            UnavailablePolicy.Instance);

        Assert.DoesNotContain(
            callers,
            caller => caller.Call.Caller.Name == "Run");
    }

    [Fact]
    public void MatchingUnresolvedParameterContractsRetainCaller()
    {
        LibraryBodyIndex target = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        LibraryBodyIndex source = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        MethodIdentity ping = StringPing(target);

        ImmutableArray<CatalogDirectCaller> callers = Find(
            target,
            ping.MetadataToken,
            source,
            UnavailablePolicy.Instance,
            UnavailablePolicy.Instance);

        Assert.Contains(
            callers,
            caller => caller.Call.Caller.Name == "RunString");
        Assert.DoesNotContain(
            callers,
            caller => caller.Call.Caller.Name == "RunInt");
    }

    [Fact]
    public void ResolvedAndUnresolvedMatchingParameterContractsRetainCaller()
    {
        LibraryBodyIndex target = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        LibraryBodyIndex source = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        MethodIdentity ping = StringPing(target);
        var sourcePolicy = new CountingPolicy(new FrameworkPolicy());

        ImmutableArray<CatalogDirectCaller> callers = Find(
            target,
            ping.MetadataToken,
            source,
            UnavailablePolicy.Instance,
            sourcePolicy);

        Assert.Contains(
            callers,
            caller => caller.Call.Caller.Name == "RunString");
        Assert.DoesNotContain(
            callers,
            caller => caller.Call.Caller.Name == "RunInt");
        Assert.True(sourcePolicy.SelectedCount > 0);
    }

    static ImmutableArray<CatalogDirectCaller> FindFrameworkCallers(
        string targetPath,
        params string[] parameterTypes)
    {
        LibraryBodyIndex target = LibraryBodyIndex.Open(targetPath);
        LibraryBodyIndex source = LibraryBodyIndex.Open(
            typeof(CatalogDirectCallerQueryTests).Assembly.Location);
        MethodIdentity create = target.DeclaredMethods.Single(method =>
            method.DeclaringType.Name == "XmlReader"
            && method.Name == "Create"
            && method.ParameterTypes
                .Select(parameter => parameter.Name)
                .SequenceEqual(parameterTypes));
        return Find(
            target,
            create.MetadataToken,
            source,
            new FrameworkPolicy(),
            new FrameworkPolicy());
    }

    static ImmutableArray<CatalogDirectCaller> Find(
        LibraryBodyIndex target,
        int targetMethodToken,
        LibraryBodyIndex source,
        IAssemblyBindingPolicy targetPolicy,
        IAssemblyBindingPolicy sourcePolicy)
    {
        var targetAssembly = Descriptor(target);
        var sourceAssembly = Descriptor(source);
        var policy = new SourceRelativeAssemblyGroupBindingPolicy(
            [
                (targetAssembly, targetPolicy),
                (sourceAssembly, sourcePolicy),
            ]);
        return CatalogDirectCallerQuery.Find(
            policy,
            new CatalogCallGraphParticipant(target, targetAssembly),
            targetMethodToken,
            [new CatalogCallGraphParticipant(source, sourceAssembly)]);
    }

    static MethodIdentity StringPing(LibraryBodyIndex target) =>
        target.DeclaredMethods.Single(method =>
            method.DeclaringType.Name == "Api"
            && method.Name == "Ping"
            && method.ParameterTypes is [{ Name: "String" }]);

    static ResolvedAssemblyReference Descriptor(LibraryBodyIndex index) =>
        ResolvedAssemblyReference.CreateFromPath(
            index.Path,
            AssemblyResolutionProvenance.Local(
                "catalog direct-caller test"));

    static string? PrivateXmlPath()
    {
        string path = Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!,
            "System.Private.Xml.dll");
        return File.Exists(path) ? path : null;
    }

    sealed class FrameworkPolicy : IAssemblyBindingPolicy
    {
        readonly string _frameworkDirectory =
            Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request)
        {
            if (request.Target
                is not AssemblyBindingTarget.AssemblyReference reference)
            {
                return AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.CandidateUnavailable));
            }

            string path = Path.Combine(
                _frameworkDirectory,
                reference.Identity.Name + ".dll");
            if (!File.Exists(path))
            {
                return AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.CandidateUnavailable));
            }

            ResolvedAssemblyReference assembly =
                ResolvedAssemblyReference.CreateFromPath(
                    path,
                    AssemblyResolutionProvenance.Local(
                        "framework direct-caller test"));
            return assembly.Identity == reference.Identity
                ? AssemblyBindingSelection.Found(assembly)
                : AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.IdentityPolicyRequired));
        }
    }

    sealed class CountingPolicy(IAssemblyBindingPolicy inner)
        : IAssemblyBindingPolicy
    {
        internal int SelectedCount { get; private set; }

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request)
        {
            AssemblyBindingSelection selection = inner.Select(request);
            if (selection is AssemblyBindingSelection.Selected)
                SelectedCount++;
            return selection;
        }
    }

    sealed class UnavailablePolicy : IAssemblyBindingPolicy
    {
        internal static UnavailablePolicy Instance { get; } = new();

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request) =>
            AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.CandidateUnavailable));
    }
}
