using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Sections;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class MemberCallGraphSectionTests
{
    [Fact]
    public async Task CallGraphSection_RendersBoundedTree_WhenExplicitlySelected()
    {
        var result = await RunCallGraphAsync(
            typeof(MemberCallGraphFixture).FullName!, nameof(MemberCallGraphFixture.RootCall));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Call Graph", result.Output);
        Assert.Contains(nameof(MemberCallGraphFixture.RootCall), result.Output);
        Assert.Contains(nameof(MemberCallGraphFixture.Mid), result.Output);
        Assert.Contains(nameof(MemberCallGraphFixture.Inner), result.Output);
        // External callees (outside this assembly) are recorded as bounded leaves.
        Assert.Contains("(external)", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_LowersToAnEdgeTable_UnderTabularOutput()
    {
        // The section is a graph, not a fixed rendering: each sink picks its own lowering, so the
        // same model that draws a tree in Markdown yields one row per edge under --tsv.
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.RootCall)],
            IncludeSections = [SectionNames.CallGraph],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
            Tabular = true,
            Tsv = true,
            TabularExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("From\tFrom Group\tTo\tTo Group", result.Output);
        // Grouping carries the external boundary the tree spells in the label.
        Assert.Contains("\tExternal", result.Output);
        Assert.Contains($"{nameof(MemberCallGraphFixture.RootCall)}", result.Output);
        Assert.Contains($"{nameof(MemberCallGraphFixture.Mid)}", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_RendersPerfCuesForFanoutDepthAndLoopingCalls()
    {
        var result = await RunCallGraphAsync(
            typeof(MemberCallGraphFixture).FullName!, nameof(MemberCallGraphFixture.LoopHeavyCall));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Call Graph", result.Output);
        Assert.Contains("fanout", result.Output);
        Assert.Contains("depth", result.Output);
        Assert.Contains("loop", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_ProjectsAllocationAndCopySignals()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.AllocCall)],
            IncludeSections = [SectionNames.CallGraph],
            Fields = ["Alloc", "Copy"],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Call Graph", result.Output);
        Assert.Contains("alloc 1", result.Output);
        Assert.Contains("copy 1", result.Output);
        // Signals are opt-in: unrequested cues must not appear.
        Assert.DoesNotContain("fanout", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_ProjectsExceptionSignals()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.RiskyCall)],
            IncludeSections = [SectionNames.CallGraph],
            Fields = ["Throw", "Catch", "Finally", "Exceptions", "EvidenceIL"],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Call Graph", result.Output);
        Assert.Contains("throw 1", result.Output);
        Assert.Contains("catch 1", result.Output);
        Assert.Contains("finally 1", result.Output);
        // The constructed exception type is a distinct field from the throw-site count (#1277).
        Assert.Contains("exceptions InvalidOperationException", result.Output);
        Assert.Contains("il IL_", result.Output);
        // Unrequested cost cues stay hidden.
        Assert.DoesNotContain("copy", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_UsesRequestedFieldsWhenRenderingNodeLabels()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.LoopHeavyCall)],
            IncludeSections = [SectionNames.CallGraph],
            Fields = ["Depth", "Loop"],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Call Graph", result.Output);
        Assert.Contains("depth 4", result.Output);
        Assert.Contains("loop", result.Output);
        Assert.DoesNotContain("fanout", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_RendersEmptyStateNote_WhenNoCallersOrCallees()
    {
        var result = await RunCallGraphAsync(
            typeof(MemberCallGraphFixture).FullName!, nameof(MemberCallGraphFixture.NoCalls));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Call Graph", result.Output);
        Assert.Contains("No inbound callers or outbound calls found for this method.", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_IncludesInboundCallers_NotJustOutboundCallees()
    {
        // The section is bidirectional: one graph centred on the selected member carries both
        // halves, so a member with no callees still shows who reaches it.
        var result = await RunCallGraphAsync(
            typeof(MemberCallGraphFixture).FullName!, nameof(MemberCallGraphFixture.Inner));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Call Graph", result.Output);
        Assert.Contains(nameof(MemberCallGraphFixture.Inner), result.Output);
        Assert.Contains(nameof(MemberCallGraphFixture.Mid), result.Output);
        Assert.Contains(nameof(MemberCallGraphFixture.RootCall), result.Output);
        Assert.Contains("fanin", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_StaysSilent_WhenNotExplicitlySelected()
    {
        // Call Graph is opt-in (ExplicitOnly): a broad view must never auto-include it.
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.RootCall)],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Detailed,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("## Call Graph", result.Output);
    }

    [Fact]
    public async Task EffectiveDiscovery_ListsCallGraphAsOptIn()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.RootCall)],
            OverloadIndex = 1,
            TipLevel = TipLevel.Quiet,
            Discover = [],
            Verbosity = Verbosity.Normal,
            Tabular = true,
            Tsv = true,
            TabularExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Call Graph\tsection", result.Output);
        Assert.DoesNotContain("(opt-in)", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_ResolvesPropertyGetterAccessor()
    {
        // A property has no body of its own; the default accessor ordinal addresses the
        // getter, and the graph roots at the getter's metadata name (#3265).
        var result = await RunCallGraphAsync(
            typeof(MemberCallGraphFixture).FullName!, nameof(MemberCallGraphFixture.Descriptor));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Call Graph", result.Output);
        Assert.Contains("get_Descriptor", result.Output);
        Assert.Contains(nameof(MemberCallGraphFixture.Inner), result.Output);
    }

    [Fact]
    public async Task CallGraphSection_ResolvesPropertySetterAccessorByOrdinal()
    {
        // Accessor ordinal 2 addresses the setter: its callee, distinct from the getter's.
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [nameof(MemberCallGraphFixture.Descriptor)],
            OverloadIndex = 2,
            IncludeSections = [SectionNames.CallGraph],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Call Graph", result.Output);
        Assert.Contains("set_Descriptor", result.Output);
        Assert.Contains("Consume", result.Output);
        Assert.DoesNotContain("Describe", result.Output);
    }

    [Fact]
    public async Task CallGraphSection_ResolvesEventAdderAccessor()
    {
        // An event target resolves to its adder accessor, whose field-like body combines
        // delegates (#3265).
        var result = await RunCallGraphAsync(
            typeof(MemberCallGraphFixture).FullName!, nameof(MemberCallGraphFixture.Triggered));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Call Graph", result.Output);
        Assert.Contains("add_Triggered", result.Output);
        Assert.Contains("Combine", result.Output);
    }

    [Fact]
    public async Task DecompiledSource_PropertyGetterRendersAccessorDeclaration()
    {
        // The getter renders a real method header (not the property's bare return type)
        // with the setter's body kept off it (#3265).
        var result = await RunDecompiledAsync(
            typeof(MemberCallGraphFixture).FullName!, nameof(MemberCallGraphFixture.Descriptor), overloadIndex: null);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Decompiled Source", result.Output);
        Assert.Contains("get_Descriptor(", result.Output);
        Assert.DoesNotContain("set_Descriptor(", result.Output);
    }

    [Fact]
    public async Task DecompiledSource_PropertySetterRendersVoidAccessorDeclaration()
    {
        // Accessor ordinal 2 renders the setter: void return, a trailing `value` parameter.
        var result = await RunDecompiledAsync(
            typeof(MemberCallGraphFixture).FullName!, nameof(MemberCallGraphFixture.Descriptor), overloadIndex: 2);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Decompiled Source", result.Output);
        Assert.Contains("void set_Descriptor(", result.Output);
        Assert.Contains("value", result.Output);
        Assert.DoesNotContain("get_Descriptor(", result.Output);
    }

    [Fact]
    public async Task DecompiledSource_EventAdderRendersVoidAccessorDeclaration()
    {
        // The adder renders as a real void method taking the delegate value, not the
        // event's bare delegate type as a headless declaration (#3265).
        var result = await RunDecompiledAsync(
            typeof(MemberCallGraphFixture).FullName!, nameof(MemberCallGraphFixture.Triggered), overloadIndex: null);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Decompiled Source", result.Output);
        Assert.Contains("void add_Triggered(", result.Output);
    }

    [Fact]
    public async Task DecompiledSource_VirtualPropertyGetterKeepsVirtualModifier()
    {
        // The accessor shares the property's slot, so a virtual getter renders `virtual`
        // rather than the owner's bare accessibility (#3265).
        var result = await RunDecompiledAsync(
            typeof(MemberAccessorModifierFixture).FullName!,
            nameof(MemberAccessorModifierFixture.Label), overloadIndex: null);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Decompiled Source", result.Output);
        Assert.Contains("public virtual string get_Label()", result.Output);
    }

    [Fact]
    public async Task DecompiledSource_OverridePropertyGetterKeepsOverrideModifier()
    {
        var result = await RunDecompiledAsync(
            typeof(DerivedAccessorModifierFixture).FullName!,
            nameof(DerivedAccessorModifierFixture.Label), overloadIndex: null);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Decompiled Source", result.Output);
        Assert.Contains("public override string get_Label()", result.Output);
    }

    [Fact]
    public async Task DecompiledSource_PrivateSetterKeepsAccessorAccessibility()
    {
        // A `private set` must render `private`, not the property's public accessibility (#3265).
        var result = await RunDecompiledAsync(
            typeof(MemberAccessorModifierFixture).FullName!,
            nameof(MemberAccessorModifierFixture.State), overloadIndex: 2);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Decompiled Source", result.Output);
        Assert.Contains("private void set_State(bool value)", result.Output);
        Assert.DoesNotContain("public void set_State(", result.Output);
    }

    static Task<(int ExitCode, string Output, string Error)> RunDecompiledAsync(
        string typeName, string memberName, int? overloadIndex)
        => ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeName,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [memberName],
            OverloadIndex = overloadIndex,
            IncludeSections = [SectionNames.DecompiledSource],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));

    static Task<(int ExitCode, string Output, string Error)> RunCallGraphAsync(
        string typeName, string memberName)
        => ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeName,
            AssemblyPath = typeof(MemberCallGraphFixture).Assembly.Location,
            MemberFilter = [memberName],
            IncludeSections = [SectionNames.CallGraph],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Normal,
        }));
}

public static class MemberCallGraphFixture
{
    public static void RootCall() => Mid();

    public static void Mid() => Inner();

    public static void Inner() => Console.WriteLine("leaf");

    // Property whose accessors have distinct, non-trivial bodies so accessor addressing
    // (Descriptor:1 = getter, Descriptor:2 = setter) resolves to different call trees (#3265).
    public static string Descriptor
    {
        get => Describe();
        set => Consume(value);
    }

    static string Describe()
    {
        Inner();
        return "descriptor";
    }

    static void Consume(string value) => Console.WriteLine(value);

    // Field-like event: the compiler generates add/remove accessor bodies whose call graph
    // an event target resolves to via its adder/remover accessor (#3265).
    public static event Action? Triggered;

    public static void Raise() => Triggered?.Invoke();

    public static void LoopHeavyCall()
    {
        for (int i = 0; i < 2; i++)
            RootCall();
    }

    public static void NoCalls()
    {
    }

    // new List<int> -> alloc; ToArray -> copy.
    public static int AllocCall(int[] data)
    {
        var list = new System.Collections.Generic.List<int>(data);
        return list.Count + System.Linq.Enumerable.ToArray(data).Length;
    }

    // throw + try/catch/finally -> throw/catch/finally signals.
    public static int RiskyCall(int x)
    {
        try
        {
            if (x < 0)
                throw new System.InvalidOperationException("negative");
            return 100 / x;
        }
        catch (System.DivideByZeroException)
        {
            return -1;
        }
        finally
        {
            System.GC.KeepAlive(x);
        }
    }
}

// Instance fixtures whose accessors carry non-default modifiers, so the synthesized
// accessor declarations must reflect virtual/override (from the owning slot) and the
// per-accessor accessibility of a `private set` rather than the owner's aggregate (#3265).
public class MemberAccessorModifierFixture
{
    public virtual string Label { get; } = "base";

    public bool State { get; private set; }
}

public class DerivedAccessorModifierFixture : MemberAccessorModifierFixture
{
    public override string Label => "derived";
}
