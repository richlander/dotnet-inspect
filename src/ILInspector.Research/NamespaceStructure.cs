using System.Collections.Immutable;

namespace ILInspector.Research;

/// <summary>
/// Namespace cycles (strongly connected components of two or more namespaces)
/// and Lakos levels over the condensation of the internal namespace graph.
/// Iterative so that untrusted inputs with deep dependency chains cannot
/// exhaust the stack.
/// </summary>
static class NamespaceStructure
{
    internal static (
        ImmutableArray<LibraryDependencyNamespaceCycle> Cycles,
        Dictionary<string, int> CycleIndex,
        Dictionary<string, int> Levels) Derive(
            IReadOnlyList<string> namespaces,
            IEnumerable<LibraryDependencyNamespaceEdge> edges)
    {
        int count = namespaces.Count;
        var indexOf = new Dictionary<string, int>(count, StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
            indexOf[namespaces[i]] = i;

        var successors = new List<int>[count];
        for (int i = 0; i < count; i++)
            successors[i] = [];
        foreach (LibraryDependencyNamespaceEdge edge in edges)
            successors[indexOf[edge.SourceNamespace]].Add(indexOf[edge.TargetNamespace]);
        foreach (List<int> list in successors)
            list.Sort();

        int[] component = Tarjan(successors, out List<List<int>> components);

        // Tarjan emits components in reverse topological order: every
        // component a component depends on is emitted before it.
        int[] componentLevel = new int[components.Count];
        for (int c = 0; c < components.Count; c++)
        {
            int level = 0;
            foreach (int node in components[c])
            {
                foreach (int next in successors[node])
                {
                    int target = component[next];
                    if (target != c)
                        level = Math.Max(level, componentLevel[target] + 1);
                }
            }
            componentLevel[c] = level;
        }

        var levels = new Dictionary<string, int>(count, StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
            levels[namespaces[i]] = componentLevel[component[i]];

        ImmutableArray<LibraryDependencyNamespaceCycle> cycles =
        [
            .. components
                .Where(static members => members.Count >= 2)
                .Select(members => new LibraryDependencyNamespaceCycle(
                    [.. members.Select(node => namespaces[node]).Order(StringComparer.Ordinal)]))
                .OrderBy(static cycle => cycle.Namespaces[0], StringComparer.Ordinal),
        ];
        var cycleIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int c = 0; c < cycles.Length; c++)
        {
            foreach (string ns in cycles[c].Namespaces)
                cycleIndex[ns] = c;
        }

        return (cycles, cycleIndex, levels);
    }

    static int[] Tarjan(List<int>[] successors, out List<List<int>> components)
    {
        int count = successors.Length;
        int[] index = new int[count];
        int[] lowLink = new int[count];
        int[] component = new int[count];
        bool[] onStack = new bool[count];
        Array.Fill(index, -1);
        var stack = new Stack<int>();
        var work = new Stack<(int Node, int NextSuccessor)>();
        components = [];
        int nextIndex = 0;

        for (int root = 0; root < count; root++)
        {
            if (index[root] >= 0)
                continue;
            work.Push((root, 0));
            while (work.Count > 0)
            {
                (int node, int next) = work.Pop();
                if (next == 0 && index[node] < 0)
                {
                    index[node] = lowLink[node] = nextIndex++;
                    stack.Push(node);
                    onStack[node] = true;
                }

                if (next < successors[node].Count)
                {
                    work.Push((node, next + 1));
                    int successor = successors[node][next];
                    if (index[successor] < 0)
                    {
                        work.Push((successor, 0));
                    }
                    else if (onStack[successor])
                    {
                        lowLink[node] = Math.Min(lowLink[node], index[successor]);
                    }
                    continue;
                }

                // All successors visited: propagate to the parent frame.
                if (work.Count > 0)
                {
                    int parent = work.Peek().Node;
                    lowLink[parent] = Math.Min(lowLink[parent], lowLink[node]);
                }
                if (lowLink[node] == index[node])
                {
                    var members = new List<int>();
                    int member;
                    do
                    {
                        member = stack.Pop();
                        onStack[member] = false;
                        component[member] = components.Count;
                        members.Add(member);
                    }
                    while (member != node);
                    components.Add(members);
                }
            }
        }

        return component;
    }
}
