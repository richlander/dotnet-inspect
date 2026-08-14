using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class MemberCallsSectionTests
{
    [Fact]
    public async Task CallsSection_RendersOneRowPerCallSite()
    {
        var result = await RunMemberCallsAsync(nameof(MemberCallsFixture.CallsWriteLineTwice));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Calls", result.Output);
        Assert.Contains("| IL Offset | Opcode | Call Kind | Callee | Operand Token | Return Address |", result.Output);
        Assert.Equal(2, CountOccurrences(result.Output, "`System.Console.WriteLine(string)`"));
        Assert.Contains("`IL_", result.Output);
        Assert.Contains("`0x0A", result.Output);
    }

    [Fact]
    public async Task CallsSection_KeepsInterfaceCallvirtAsDeclaredTarget()
    {
        var result = await RunMemberCallsAsync(nameof(MemberCallsFixture.CallsInterfaceItem));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("`System.Collections.Generic.IList<int>.get_Item(int)`", result.Output);
        Assert.Contains("| callvirt | virtual |", result.Output);
    }

    [Fact]
    public async Task CallsSection_SelectedSecondOverload_UsesSelectedMethod()
    {
        var result = await RunMemberCallsAsync(nameof(MemberCallsFixture.Overloaded), overloadIndex: 2);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Calls", result.Output);
        Assert.Contains("`System.Console.WriteLine(string)`", result.Output);
    }

    [Fact]
    public async Task CallsSection_TsvUsesPlainNormalizedValues()
    {
        var result = await RunMemberCallsAsync(nameof(MemberCallsFixture.CallsWriteLineTwice), tsv: true);

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("il_offset\topcode\tcall_kind\tcallee\toperand_token\treturn_address", result.Output);
        Assert.DoesNotContain('`', result.Output);
        Assert.Equal(2, CountOccurrences(result.Output, "System.Console.WriteLine(string)"));
    }

    [Fact]
    public async Task EffectiveDiscovery_ListsCallsForSelectedMember()
    {
        var result = await RunMemberCallsAsync(nameof(MemberCallsFixture.CallsWriteLineTwice), discover: true);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Calls\tsection", result.Output);
    }

    [Fact]
    public void EmptyImplicitCallerTokenScope_DoesNotResolveSolePropertyAccessor()
    {
        var type = SolePropertyType();
        var options = ImplicitCallerOptions();

        var methods = ApiOutputFormatter.ResolveBodyMethods(
            type, new HashSet<string> { SectionNames.Callers }, options);

        Assert.Empty(methods);
    }

    [Fact]
    public void EmptyImplicitCallerTokenScope_DoesNotMakeCallersEffective()
    {
        var type = SolePropertyType();
        var options = ImplicitCallerOptions();

        Assert.False(ApiMemberSectionPipelines.ShouldAggregateImplicitCallers(type, options));
    }

    static ApiType SolePropertyType()
        => new()
        {
            Namespace = "Samples",
            Name = "Properties",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Solo",
                    Kind = "property",
                    ReturnType = "int",
                    GetterToken = 0x06000001
                }
            ]
        };

    static MemberOptions ImplicitCallerOptions()
        => new()
        {
            CallerScopeSectionImplicitlySelected = true,
            IncludeSections = [SectionNames.Callers],
            ImplicitCallerMemberTokens = new HashSet<int>()
        };

    static Task<(int ExitCode, string Output, string Error)> RunMemberCallsAsync(string memberName, bool tsv = false, bool discover = false, int? overloadIndex = null)
        => ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallsFixture).FullName,
            AssemblyPath = typeof(MemberCallsFixture).Assembly.Location,
            MemberFilter = [memberName],
            OverloadIndex = overloadIndex,
            IncludeSections = [SectionNames.Calls],
            TipLevel = TipLevel.Quiet,
            Discover = discover ? [] : null,
            Verbosity = Verbosity.Minimal,
            Tabular = tsv || discover,
            Tsv = tsv || discover,
            TabularExplicitlySet = tsv || discover,
            FormatExplicitlySet = true,
        }));

    static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}

public static class MemberCallsFixture
{
    public static void CallsWriteLineTwice()
    {
        Console.WriteLine("first");
        Console.WriteLine("second");
    }

    public static int CallsInterfaceItem(IList<int> values) => values[0];

    public static void Overloaded(int value)
    {
    }

    public static void Overloaded(string value)
    {
        Console.WriteLine(value);
    }

    public static void CallsOverloaded()
    {
        Overloaded(1);
        Overloaded("value");
    }

    public static void UnusedOverloaded(int value)
    {
    }

    public static void UnusedOverloaded(string value)
    {
    }

    // Non-public member: only selectable under --all. Regression coverage for #1323,
    // where the body-load path counted overloads public-only and so reported "no IL body"
    // for a method that the Calls/IL index reads fine.
    internal static int InternalHelper(int value)
    {
        Console.WriteLine(value);
        return value + 1;
    }
}

public sealed class MemberPropertyCallsFixture
{
    public int this[int index] => index;
    public int this[string key] => key.Length;

    public static void CallsIndexers()
    {
        var fixture = new MemberPropertyCallsFixture();
        _ = fixture[1];
        _ = fixture["one"];
    }
}

public abstract class MemberAbstractCallsFixture
{
    public abstract void Mixed(int value);
    public void Mixed(string value) { }

    public static void CallsMixed(MemberAbstractCallsFixture fixture)
    {
        fixture.Mixed(1);
        fixture.Mixed("one");
    }
}

public abstract class MemberAbstractPropertyCallsFixture
{
    public abstract int this[int index] { get; }
    public abstract int this[string key] { get; }

    public static void CallsIndexers(MemberAbstractPropertyCallsFixture fixture)
    {
        _ = fixture[1];
        _ = fixture["one"];
    }
}

public static class MemberOnlyPropertyCallsFixture
{
    public static int Solo => 1;
}

public static class MemberOnlyPropertyCallerFixture
{
    public static int CallsSolo() => MemberOnlyPropertyCallsFixture.Solo;
}
