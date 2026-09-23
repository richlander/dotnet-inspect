using DotnetInspect.Cli.Sections;
using System.Globalization;
using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Type_DecompiledSource_RendersWholeTypeListing(
        bool includeAll)
    {
        List<string> arguments =
        [
            "type",
            "System.Collections.Generic.Stack",
            "--platform",
            "System.Collections",
            "-S",
            "Decompiled Source",
            "--tips",
            "q",
        ];
        if (includeAll)
            arguments.Add("--all");

        var (exit, output, error) = await RunAppAsync(
            [.. arguments]);

        Assert.True(exit == 0, error);
        Assert.Empty(error);
        Assert.Contains("namespace System.Collections.Generic;", output);
        Assert.Contains("public class Stack<T>", output);
        Assert.Contains("private T[] _array;", output);
        Assert.Contains("public void Push(T item)", output);
        Assert.Contains("public bool TryPop([MaybeNullWhen(false)] out T result)", output);
        // Using hoisting: qualified names shorten against the metadata
        // namespace tables; the directives appear at the top.
        Assert.Contains("using System.Runtime.CompilerServices;", output);
        Assert.Contains(
            ": IEnumerable<T>, System.Collections.IEnumerable, "
            + "System.Collections.ICollection, IReadOnlyCollection<T>",
            output);
        Assert.Contains("RuntimeHelpers.IsReferenceOrContainsReferences", output);
        Assert.Contains(
            "IEnumerator<T> System.Collections.Generic.IEnumerable<T>.GetEnumerator()",
            output);
        // Explicit interface property implementations render exactly once
        // as properties with their selected accessor bodies.
        Assert.Equal(
            1,
            output.Split(
                "bool System.Collections.ICollection.IsSynchronized",
                StringSplitOptions.None).Length - 1);
        Assert.Equal(
            1,
            output.Split(
                "object System.Collections.ICollection.SyncRoot",
                StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("private virtual bool ICollection.IsSynchronized", output);
        Assert.DoesNotContain("private virtual object ICollection.SyncRoot", output);
        Assert.DoesNotContain("get_IsSynchronized", output);
        Assert.DoesNotContain("get_SyncRoot", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task
        Type_DecompiledSource_PreservesExplicitPropertyDeclarationAttributes(
            bool includeAll)
    {
        List<string> arguments =
        [
            "type",
            typeof(AttributedExplicitValuesFixture).FullName!,
            "--library",
            TestAssemblyPath,
            "-S",
            "Decompiled Source",
            "--tips",
            "q",
        ];
        if (includeAll)
            arguments.Add("--all");

        var (exit, output, error) = await RunAppAsync([.. arguments]);

        Assert.Equal(0, exit);
        Assert.Empty(error);
        string normalized = output.ReplaceLineEndings("\n");
        const string values =
            "        [System.Runtime.Serialization.DataMember(Name = \"values\")]\n"
            + "        List<int> CommandExecutionTests.IAttributedExplicitValuesFixture.Values\n"
            + "        {\n"
            + "            get => _values;\n"
            + "        }";
        const string otherValues =
            "        [System.Runtime.Serialization.DataMember(Name = \"other-values\")]\n"
            + "        List<int> CommandExecutionTests.IAttributedExplicitValuesFixture.OtherValues\n"
            + "        {\n"
            + "            get => _otherValues;\n"
            + "        }";
        Assert.Equal(
            1,
            normalized.Split(values, StringSplitOptions.None).Length - 1);
        Assert.Equal(
            1,
            normalized.Split(otherValues, StringSplitOptions.None).Length - 1);
        Assert.Equal(
            1,
            normalized.Split(
                "[System.Runtime.Serialization.DataMember(Name = \"values\")]",
                StringSplitOptions.None).Length - 1);
        Assert.Equal(
            1,
            normalized.Split(
                "[System.Runtime.Serialization.DataMember(Name = \"other-values\")]",
                StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain(
            "[System.Runtime.Serialization.DataMember(Name = \"values\")]\n"
            + "        List<int> CommandExecutionTests.IAttributedExplicitValuesFixture.OtherValues",
            normalized,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "[System.Runtime.Serialization.DataMember(Name = \"other-values\")]\n"
            + "        List<int> CommandExecutionTests.IAttributedExplicitValuesFixture.Values",
            normalized,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "private virtual List<int> CommandExecutionTests.IAttributedExplicitValuesFixture.",
            normalized,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        Type_DecompiledSource_RequiresCompletedTypeDocumentInspection()
    {
        ApiSurface surface =
            AssemblyReader.ExtractApiSurface(
                TestAssemblyPath)!;
        ApiType type = Assert.Single(
            surface.Types,
            candidate =>
                candidate.FullName
                    == typeof(MemberCallsFixture).FullName);
        var options =
            new TypeOptions
            {
                DllPath = TestAssemblyPath,
                IncludeSections =
                    [SectionNames.DecompiledSource],
                Select =
                    [SectionNames.DecompiledSource],
                DocsExplicitlySet = true,
                TipLevel = TipLevel.Quiet,
                Verbosity = Verbosity.Minimal,
            };

        var (exit, output, error) =
            await ConsoleCapture.RunAsync(
                () => ApiCommand.WriteTypeOutputAsync(
                    type,
                    foundIn: null,
                    packageName: null,
                    packageVersion: null,
                    apiSource: null,
                    selectedTfm: null,
                    options));

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("DEC0001", error);
        Assert.Contains(
            "completed Type document inspection is unavailable",
            error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(IAbstractExplicitValueFixture), false)]
    [InlineData(typeof(IAbstractExplicitValueFixture), true)]
    [InlineData(typeof(ExternExplicitValueFixture), false)]
    [InlineData(typeof(ExternExplicitValueFixture), true)]
    public async Task Type_DecompiledSource_BodylessExplicitPropertyRetainsDeclarationAttributes(
        Type fixtureType,
        bool includeAll)
    {
        var (exit, output, error) = await RunAppAsync(
            [
                "type", fixtureType.FullName!,
                "--library", TestAssemblyPath,
                "-S", "Decompiled Source", "--tips", "q",
                .. includeAll ? new[] { "--all" } : [],
            ]);

        Assert.Equal(0, exit);
        Assert.Empty(error);
        string[] lines = output.ReplaceLineEndings("\n").Split('\n');
        const string attribute =
            "[System.Obsolete(\"Use Value2 instead\")]";
        Assert.Single(lines, line => line.Trim() == attribute);
        int attributeLine = Array.FindIndex(lines, line => line.Trim() == attribute);
        Assert.InRange(attributeLine, 0, lines.Length - 2);
        Assert.Contains(
            $"{nameof(IBodylessExplicitValueFixture)}.Value",
            lines[attributeLine + 1],
            StringComparison.Ordinal);
        Assert.DoesNotContain("get_Value", lines[attributeLine + 1]);
    }

    [Fact]
    public async Task
        Type_DecompiledSource_EmptyType_RemainsAbsent()
    {
        var (exit, output, error) =
            await RunAppAsync(
                "type",
                typeof(IEmptyStyleFixture).FullName!,
                "--library",
                TestAssemblyPath,
                "-S",
                "Decompiled Source",
                "--tips",
                "q");

        Assert.Equal(0, exit);
        Assert.Contains(
            typeof(IEmptyStyleFixture).FullName!,
            output);
        Assert.DoesNotContain(
            "public interface IEmptyStyleFixture",
            output);
        Assert.Contains(
            "section 'Decompiled Source' has no data",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Error:",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        Type_DecompiledSource_DefaultAndAllRenderSameCompleteType()
    {
        var (defaultExit, defaultOutput, defaultError) =
            await RunAppAsync(
                "type",
                typeof(FullTypeDecompilationFixture).FullName!,
                "--library",
                TestAssemblyPath,
                "-S",
                "Decompiled Source",
                "--tips",
                "q");
        var (allExit, allOutput, allError) =
            await RunAppAsync(
                "type",
                typeof(FullTypeDecompilationFixture).FullName!,
                "--library",
                TestAssemblyPath,
                "-S",
                "Decompiled Source",
                "--all",
                "--tips",
                "q");

        Assert.Equal(0, defaultExit);
        Assert.Empty(defaultError);
        Assert.Contains(
            "public abstract string ConvertName(string name);",
            defaultOutput,
            StringComparison.Ordinal);
        Assert.Contains(
            "public static int Visible",
            defaultOutput,
            StringComparison.Ordinal);
        Assert.Contains(
            "protected FullTypeDecompilationFixture()",
            defaultOutput,
            StringComparison.Ordinal);
        Assert.Contains(
            "static FullTypeDecompilationFixture()",
            defaultOutput,
            StringComparison.Ordinal);
        Assert.Contains(
            "private static int ConcealedCore()",
            defaultOutput,
            StringComparison.Ordinal);

        Assert.Equal(0, allExit);
        Assert.Empty(allError);
        Assert.Equal(defaultOutput, allOutput);
    }

    [Fact]
    public async Task
        Type_MemberListing_DefaultAndAllRetainAccessibilityBoundary()
    {
        var (defaultExit, defaultOutput, defaultError) =
            await RunAppAsync(
                "type",
                typeof(FullTypeDecompilationFixture).FullName!,
                "--library",
                TestAssemblyPath,
                "-S",
                "Methods",
                "--table",
                "--tips",
                "q");
        var (allExit, allOutput, allError) =
            await RunAppAsync(
                "type",
                typeof(FullTypeDecompilationFixture).FullName!,
                "--library",
                TestAssemblyPath,
                "-S",
                "Methods",
                "--table",
                "--all",
                "--tips",
                "q");

        Assert.Equal(0, defaultExit);
        Assert.Empty(defaultError);
        Assert.Contains(
            "InvokePrivateCore",
            defaultOutput,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ConcealedCore",
            defaultOutput,
            StringComparison.Ordinal);

        Assert.Equal(0, allExit);
        Assert.Empty(allError);
        Assert.Contains(
            "InvokePrivateCore",
            allOutput,
            StringComparison.Ordinal);
        Assert.Contains(
            "ConcealedCore",
            allOutput,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_GenericInstantiation_PreservesNestedTypeSuffix()
    {
        // #1154: an instantiated nested type (Dictionary`2.Enumerator) must keep
        // its nested segment instead of collapsing to Dictionary<TKey, TValue>.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Collections.Generic.Dictionary`2", "--platform", "System.Private.CoreLib",
            "-S", "Methods");

        Assert.Equal(0, exit);
        Assert.Contains("Dictionary<TKey, TValue>.Enumerator GetEnumerator()", output);
        Assert.DoesNotContain("Dictionary<TKey, TValue> GetEnumerator()", output);
    }

    [Fact]
    public async Task Type_DecompiledSource_UsesStructuredBodySpelling()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath,
            "-S", "Decompiled Source");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains(
            "    public static int CallsInterfaceItem(IList<int> values)\n"
            + "    {\n"
            + "        return values[0];\n"
            + "    }",
            output.ReplaceLineEndings("\n"));
        Assert.Contains("    public static void CallsWriteLineTwice()\n    {", output.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task TypeListing_NestedTypes_ShowDeclaringTypeContext()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "--platform", "System.Collections", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("System.Collections.Generic.SortedDictionary<TKey, TValue>.KeyCollection", output);
        Assert.Contains("System.Collections.Generic.SortedDictionary<TKey, TValue>.ValueCollection", output);
        Assert.Contains("System.Collections.Generic.Stack<T>.Enumerator", output);
        Assert.DoesNotContain("class   KeyCollection", output);
        Assert.DoesNotContain("struct  Enumerator", output);
    }

    [Fact]
    public async Task TypeListing_NestedDelegate_ShowsFullDeclaringTypeContext()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text.Json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("`System.Text.Json.Serialization.Metadata.FSharpCoreReflectionProxy.StructGetter<TStruct, TResult>`", output);
        Assert.DoesNotContain("| `StructGetter<TStruct, TResult>` |", output);
    }

    [Fact]
    public async Task Type_DecompiledSource_Enum_RendersValuesListing()
    {
        // Enums have no method bodies; the listing renders the declaration
        // and values — following the ref assembly's type forwarder to the
        // defining assembly.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.DayOfWeek", "--platform", "System.Runtime",
            "-S", "Decompiled Source");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("public enum DayOfWeek", output);
        Assert.Contains("Sunday = 0,", output);
        Assert.Contains("Saturday = 6,", output);
    }

    [Fact]
    public async Task Type_DecompiledSource_Default_EmitsNativeListing()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Collections.Generic.Stack", "--platform", "System.Collections",
            "-S", "Decompiled Source");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        // Native C#: no markdown heading, section title, code fence, or tips.
        Assert.StartsWith("using ", output);
        Assert.Contains("using System.Runtime.CompilerServices;", output);
        Assert.Contains("namespace System.Collections.Generic;", output);
        Assert.DoesNotContain("# ", output);
        Assert.DoesNotContain("```", output);
        Assert.DoesNotContain("Tips:", output);
    }

    [Fact]
    public async Task Type_DecompiledSource_ExplicitMarkdown_RestoresDocumentFraming()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Collections.Generic.Stack", "--platform", "System.Collections",
            "-S", "Decompiled Source", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("# System.Collections.Generic.Stack", output);
        Assert.Contains("## Decompiled Source", output);
        Assert.Contains("```csharp", output);
    }

    [Fact]
    public async Task Type_MultipleSelectedSections_RemainMarkdownDocument()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Collections.Generic.Stack", "--platform", "System.Collections",
            "-S", "Decompiled Source,Member Index", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("# System.Collections.Generic.Stack", output);
        Assert.Contains("## Member Index", output);
        Assert.Contains("## Decompiled Source", output);
        Assert.Contains("```csharp", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Type_DecompiledSource_EnvironmentMarkdown_RestoresDocumentFraming(bool print)
    {
        string? originalFormat =
            Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", "markdown");

            var (exit, output, error) = await RunAppAsync(
            [
                "type", "System.Collections.Generic.Stack", "--platform", "System.Collections",
                "-S", "Decompiled Source", "--tips", "q",
                .. print ? new[] { "--print" } : [],
            ]);

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.StartsWith(
                print ? "# Decompiled Source" : "# System.Collections.Generic.Stack",
                output);
            Assert.Contains("```csharp", output);
            if (!print)
                Assert.Contains("## Decompiled Source", output);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                originalFormat);
        }
    }

    [Fact]
    public async Task Type_DecompiledSource_WithEmptySibling_RemainsMarkdownDocument()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonNamingPolicy", "--platform", "System.Text.Json",
            "-S", "Decompiled Source,Fields", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Note: section 'Fields' has no data", error);
        Assert.StartsWith("# System.Text.Json.JsonNamingPolicy", output);
        Assert.DoesNotContain("## Fields", output);
        Assert.Contains("## Decompiled Source", output);
        Assert.Contains("```csharp", output);
    }

    [Fact]
    public async Task Type_Bare_IsRetired()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "String", "--platform", "System.Private.CoreLib", "--bare", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Unrecognized option '--bare'", error);
    }

    [Fact]
    public async Task Type_Help_DoesNotAdvertiseBare()
    {
        var (exit, output, error) = await RunAppAsync("type", "--help");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("--bare", output);
    }

    [Fact]
    public async Task Type_ExactType_DefaultAndTreeOutputAreEquivalent()
    {
        var defaultResult = await RunAppAsync(
            "type", "System.Math", "--tips", "q");
        var treeResult = await RunAppAsync(
            "type", "System.Math", "--tree", "--tips", "q");

        Assert.Equal(defaultResult, treeResult);
        Assert.Equal(0, defaultResult.Exit);
    }

    [Fact]
    public async Task Type_ExactType_TreeOverridesEnvironmentTable()
    {
        string? originalFormat =
            Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        try
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                "table");

            var (exit, output, error) = await RunAppAsync(
                "type", "System.Math", "--tree", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.StartsWith(
                "static class System.Math",
                output,
                StringComparison.Ordinal);
            Assert.Contains("─ Methods", output, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "Kind    Name    Return Type",
                output,
                StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                originalFormat);
        }
    }

    public abstract class FullTypeDecompilationFixture
    {
        static FullTypeDecompilationFixture()
        {
            Visible = 42;
        }

        protected FullTypeDecompilationFixture()
        {
        }

        public abstract string ConvertName(string name);

        public static int Visible { get; }

        public int InvokePrivateCore() =>
            ConcealedCore();

        private static int ConcealedCore() => 42;
    }

    public interface IAttributedExplicitValuesFixture
    {
        List<int> Values { get; }

        List<int> OtherValues { get; }
    }

    public interface IBodylessExplicitValueFixture
    {
        int Value { get; }
    }

    public interface IAbstractExplicitValueFixture : IBodylessExplicitValueFixture
    {
        [Obsolete("Use Value2 instead", true)]
        abstract int IBodylessExplicitValueFixture.Value { get; }
    }

    public sealed class ExternExplicitValueFixture : IBodylessExplicitValueFixture
    {
        [Obsolete("Use Value2 instead", true)]
        extern int IBodylessExplicitValueFixture.Value
        {
            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.InternalCall)]
            get;
        }
    }

    [System.Runtime.Serialization.DataContract]
    public sealed class AttributedExplicitValuesFixture :
        IAttributedExplicitValuesFixture
    {
        readonly List<int> _values = [];
        readonly List<int> _otherValues = [];

        [System.Runtime.Serialization.DataMember(Name = "values")]
        List<int> IAttributedExplicitValuesFixture.Values => _values;

        [System.Runtime.Serialization.DataMember(Name = "other-values")]
        List<int> IAttributedExplicitValuesFixture.OtherValues =>
            _otherValues;
    }

    [Fact]
    public async Task Type_StringShape_RendersLearnMemberOrder()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "String", "--platform", "System.Private.CoreLib", "--tree");

        Assert.Equal(0, exit);
        Assert.Empty(error);

        string[] headings =
        [
            "Constructors",
            "Fields",
            "Properties",
            "Methods",
            "Operators",
            "Explicit Interface Implementations",
            "Extension Methods"
        ];

        var previous = -1;
        foreach (var heading in headings)
        {
            var current = output.IndexOf($"─ {heading}", StringComparison.Ordinal);
            Assert.True(current > previous, $"{heading} was not after the previous heading.");
            previous = current;
        }
    }

    [Fact]
    public async Task Type_StaticClass_RendersStaticClassModifierOnly()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Math", "--tree", "--tips", "q", "-n", "1", "--lines");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("static class System.Math", output, StringComparison.Ordinal);
        Assert.DoesNotContain("static abstract sealed class", output);
    }

    [Fact]
    public async Task Type_BareStringAlias_RendersCoreLibString()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "string", "--tree", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("System.String", output);
        Assert.Contains("─ Methods", output);
    }

    [Theory]
    [InlineData("Dictionary<TKey,TValue>")]
    [InlineData("Dictionary`2")]
    public async Task Type_BareDictionaryGeneric_RendersCoreLibDictionary(string typeName)
    {
        var (exit, output, error) = await RunAppAsync(
            "type", typeName, "--tree", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("System.Collections.Generic.Dictionary<TKey, TValue>", output);
        Assert.Contains("void Add(TKey key, TValue value)", output);
    }

    [Fact]
    public async Task Type_SelectWithUnknownColumn_ReturnsError()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Properties"],
            Columns = ["Bogus"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Contains("column 'Bogus' not found in section 'Properties'", error);
        Assert.Contains("No columns matched projection: Bogus", error);
    }
}
