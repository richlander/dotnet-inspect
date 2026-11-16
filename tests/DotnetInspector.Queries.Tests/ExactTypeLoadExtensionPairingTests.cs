using System.Text;
using DotnetInspector.Presentation;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotnetInspector.Queries.Tests;

/// <summary>
/// An exact Type load pairs with every extension the complete comparison pairs
/// a moved instance member with, whatever the extension's visibility, status,
/// or form (library-api-diff-presentation.md#exact-type-load).
/// </summary>
public sealed class ExactTypeLoadExtensionPairingTests
{
    // Each receiver's instance member M moves to an extension in the After
    // version: obsolete, editor-hidden, internal, plain, a C# 14 extension
    // block, and generic and nested receivers.
    const string Before = """
        namespace P;
        public class A { public void M(int x) {} }
        public class B { public void M(int x) {} }
        public class C { public void M(int x) {} }
        public class D { public void M(int x) {} }
        public class E { public void M(int x) {} }
        public static class Holder { }
        public class G<T> { public void M(int x) {} public class N { public void M(int x) {} } }
        public class Outer { public class Inner { public void M(int x) {} } }
        """;

    const string After = """
        using System;
        using System.ComponentModel;
        namespace P;
        public class A { }
        public class B { }
        public class C { }
        public class D { }
        public class E { }
        public static class Holder { }
        public static class AExt { [Obsolete("Moved.")] public static void M(this A a, int x) {} }
        internal static class BExt { public static void M(this B b, int x) {} }
        public static class CExt { [EditorBrowsable(EditorBrowsableState.Never)] public static void M(this C c, int x) {} }
        public static class DExt { public static void M(this D d, int x) {} }
        public static class EExt { extension(E e) { public void M(int x) {} } }
        public class G<T> { public class N { } }
        public class Outer { public class Inner { } }
        public static class GExt
        {
            public static void M<T>(this G<T> g, int x) {}
            public static void M<T>(this G<T>.N n, int x) {}
            public static void M(this Outer.Inner i, int x) {}
        }
        """;

    static ApiSurfaceProjectionLimits GenerousLimits { get; } =
        new(64, 1_000_000, 1_000_000, int.MaxValue, int.MaxValue, int.MaxValue);

    [Theory]
    [InlineData(ApiSurfaceScope.Public)]
    [InlineData(ApiSurfaceScope.IncludeAll)]
    [InlineData(ApiSurfaceScope.ApiAxis)]
    public async Task ExactLoad_MatchesCompleteComparisonForEveryType(ApiSurfaceScope scope)
    {
        string directory = Directory.CreateTempSubdirectory("exact-type-load-").FullName;
        try
        {
            string before = Emit(directory, "Before", Before);
            string after = Emit(directory, "After", After);
            AssemblyContextApiComparisonResult completeResult =
                await Compare(before, after, scope, typeSelection: null);
            LibraryApiDiffOutcome.Available complete =
                Assert.IsType<LibraryApiDiffOutcome.Available>(
                    LibraryApiDiffPresentationAdapter.Create(completeResult));
            string[] names =
            [
                .. completeResult.Before.Surface!.Types
                    .Concat(completeResult.After.Surface!.Types)
                    .Select(type => type.FullName)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal),
            ];
            Assert.Contains("P.A", names);

            foreach (string name in names)
            {
                ApiTypeSelection selection = ApiTypeSelection.ForFullNames([name]);
                LibraryApiDiffOutcome.Available selected =
                    Assert.IsType<LibraryApiDiffOutcome.Available>(
                        LibraryApiDiffPresentationAdapter.Create(
                            await Compare(before, after, scope, selection),
                            selection.FullNames));
                Assert.Equal(
                    complete.Document.Comparison.Subjects.Where(subject => subject.Display == name),
                    selected.Document.Comparison.Subjects);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // A predicate selection of several receivers, as a CLI -t pattern makes,
    // pairs with the union of their extensions in one walk of each image.
    [Theory]
    [InlineData(ApiSurfaceScope.Public)]
    [InlineData(ApiSurfaceScope.IncludeAll)]
    [InlineData(ApiSurfaceScope.ApiAxis)]
    public async Task PredicateLoadOfSeveralReceivers_MatchesCompleteComparison(ApiSurfaceScope scope)
    {
        string directory = Directory.CreateTempSubdirectory("exact-type-load-").FullName;
        try
        {
            string before = Emit(directory, "Before", Before);
            string after = Emit(directory, "After", After);
            LibraryApiDiffOutcome.Available complete =
                Assert.IsType<LibraryApiDiffOutcome.Available>(
                    LibraryApiDiffPresentationAdapter.Create(
                        await Compare(before, after, scope, typeSelection: null)));
            static bool Receiver(string name)
                => !name.EndsWith("Ext", StringComparison.Ordinal) && name != "P.Holder";

            AssemblyContextApiComparisonResult selected = await Compare(
                before, after, scope, new ApiTypeSelection(Receiver));
            LibraryApiDiffOutcome.Available document =
                Assert.IsType<LibraryApiDiffOutcome.Available>(
                    LibraryApiDiffPresentationAdapter.Create(selected));

            Assert.Equal(
                complete.Document.Comparison.Subjects.Where(subject => Receiver(subject.Display)),
                document.Document.Comparison.Subjects.Where(subject => Receiver(subject.Display)));
            Assert.DoesNotContain(
                "P.Holder",
                selected.After.Surface!.Types.Select(type => type.FullName));
            Assert.Contains(
                "P.GExt",
                selected.After.Surface!.Types.Select(type => type.FullName));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static string Emit(string directory, string name, string source)
    {
        string[] platform =
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator);
        // Both versions are one logical Library.
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ExactLibrary",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            platform.Select(static path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, deterministic: true));
        string path = Path.Combine(Directory.CreateDirectory(Path.Combine(directory, name)).FullName, "ExactLibrary.dll");
        using (FileStream stream = File.Create(path))
        {
            var result = compilation.Emit(stream);
            Assert.True(
                result.Success,
                string.Join(Environment.NewLine, result.Diagnostics));
        }
        return path;
    }

    static async Task<AssemblyContextApiComparisonResult> Compare(
        string beforePath,
        string afterPath,
        ApiSurfaceScope scope,
        ApiTypeSelection? typeSelection)
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup beforeGroup =
            workspace.CreateAssemblyContextGroup([Participant(beforePath, "Before")]);
        using AssemblyContextGroup afterGroup =
            workspace.CreateAssemblyContextGroup([Participant(afterPath, "After")]);
        return AssemblyContextApiComparisonQuery.Execute(
            beforeGroup,
            Assert.Single(beforeGroup.Participants),
            afterGroup,
            Assert.Single(afterGroup.Participants),
            scope,
            GenerousLimits,
            typeSelection);
    }

    static AssemblyContextParticipant Participant(string path, string label)
        => new(
            ResolvedAssemblyReference.CreateFromPath(path, AssemblyResolutionProvenance.Local(label)),
            NoResolverAssemblyBindingPolicy.Instance);
}
