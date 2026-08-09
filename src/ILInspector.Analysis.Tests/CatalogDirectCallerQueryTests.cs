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
            GroupPolicy(target, source));

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
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
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
            UnavailablePolicy.Instance);

        Assert.DoesNotContain(
            callers,
            caller => caller.Call.Caller.Name == "Run");
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
        var policy = new SourceRelativeAssemblyGroupBindingPolicy(
            [
                (Descriptor(target), (IAssemblyBindingPolicy)new FrameworkPolicy()),
                (Descriptor(source), (IAssemblyBindingPolicy)new FrameworkPolicy()),
            ]);

        return Find(target, create.MetadataToken, source, policy);
    }

    static ImmutableArray<CatalogDirectCaller> Find(
        LibraryBodyIndex target,
        int targetMethodToken,
        LibraryBodyIndex source,
        IAssemblyBindingPolicy policy)
    {
        var targetAssembly = Descriptor(target);
        var sourceAssembly = Descriptor(source);
        return CatalogDirectCallerQuery.Find(
            policy,
            new CatalogCallGraphParticipant(target, targetAssembly),
            targetMethodToken,
            [new CatalogCallGraphParticipant(source, sourceAssembly)]);
    }

    static IAssemblyBindingPolicy GroupPolicy(
        LibraryBodyIndex target,
        LibraryBodyIndex source)
    {
        ResolvedAssemblyReference targetAssembly = Descriptor(target);
        ResolvedAssemblyReference sourceAssembly = Descriptor(source);
        return new SourceRelativeAssemblyGroupBindingPolicy(
            [
                (
                    targetAssembly,
                    (IAssemblyBindingPolicy)new AssemblyDependencyResolver(
                        new AssemblyDependencyResolutionOptions(target.Path))),
                (
                    sourceAssembly,
                    (IAssemblyBindingPolicy)new AssemblyDependencyResolver(
                        new AssemblyDependencyResolutionOptions(source.Path))),
            ]);
    }

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
