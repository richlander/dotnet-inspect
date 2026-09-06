namespace ILInspector.Decompiler.Pipeline;

internal sealed partial class ClassicInverseAccountant
{
    readonly Dictionary<IrNode, DefaultValue> _rawDefaultInitializers =
        new(ReferenceEqualityComparer.Instance);
    readonly HashSet<IrNode> _rawDefaultTransfers =
        new(ReferenceEqualityComparer.Instance);
    readonly List<DefaultValue> _planningDefaults = [];

    ClassicInverseProtocolRule ClassifyRaw(IrNode node)
        => _interpolationStores.Contains(node)
            ? ClassicInverseProtocolRule.Frame("interpolation-handler-store", 0)
            : _interpolationAddresses.Contains(node)
                ? ClassicInverseProtocolRule.Owned("interpolation-handler-address")
            : _stackallocCountStores.Contains(node)
            ? ClassicInverseProtocolRule.Frame("stackalloc-count-store", 0)
            : _stackallocCountReads.Contains(node)
                ? ClassicInverseProtocolRule.Owned("stackalloc-count-read")
            : _rawDefaultTransfers.Contains(node)
            ? ClassicInverseProtocolRule.Owned("default-value-local-transfer")
            : ClassicInverseProtocol.Classify(node, _shell, _candidate);

    bool ProveDefaultInitializations()
    {
        if (_planningDefaults.Count == 0)
            return true;
        var initializers = new Dictionary<int, InitObject?>();
        var localUses = new Dictionary<int, List<IrNode>>();
        var positions = new Dictionary<IrNode, int>(ReferenceEqualityComparer.Instance);
        foreach (IrNode node in _rawExecutionPaths.Keys)
        {
            if (!_budget.Charge())
                return Exhausted();
            if (node is Block block)
            {
                for (int i = 0; i < block.Children.Count; i++)
                {
                    if (!_budget.Charge())
                        return Exhausted();
                    positions.Add(block.Children[i], i);
                }
            }
            if (node is InitObject init && !initializers.TryAdd(init.SourceOffset, init))
                initializers[init.SourceOffset] = null;
            int local = node switch
            {
                LoadLocal read => read.Index,
                LoadLocalAddress address => address.Index,
                StoreLocal store => store.Index,
                _ => -1,
            };
            if (local < 0)
                continue;
            if (!localUses.TryGetValue(local, out var uses))
                localUses.Add(local, uses = []);
            uses.Add(node);
        }
        foreach (DefaultValue value in _planningDefaults)
        {
            if (!_budget.Charge())
                return Exhausted();
            if (EnclosingClaimSource(value) is null)
                continue;
            if (value.SourceOffset < 0
                || !initializers.TryGetValue(value.SourceOffset, out InitObject? init)
                || init is not { Address: LoadLocalAddress address, Parent: Block block }
                || !Equals(init.Type, value.Type) || !Equals(address.Type, value.Type)
                || address.SourceOffset < 0 || address.SourceOffset >= init.SourceOffset
                || !localUses.TryGetValue(address.Index, out var uses) || uses.Count != 2
                || uses.SingleOrDefault(use => use is LoadLocal) is not LoadLocal read
                || !Equals(read.Type, value.Type) || read.SourceOffset <= init.SourceOffset
                || !Equals(ClassicInverseExpressionRules.SinkType(read, _budget), value.Type)
                || !Equals(ClassicInverseExpressionRules.SinkType(value, _budget), value.Type))
            {
                return Unproven();
            }

            IrNode? consumer = read.Parent switch
            {
                Call { Parent: ExpressionStatement statement } => statement,
                StoreField store when ReferenceEquals(store.Value, read) => store,
                _ => null,
            };
            if (consumer is null || !ReferenceEquals(consumer.Parent, block)
                || !positions.TryGetValue(init, out int initializedAt)
                || !positions.TryGetValue(consumer, out int consumedAt)
                || consumedAt != initializedAt + 1
                || !_rawDefaultInitializers.TryAdd(init, value))
                return Unproven();

            _rawDefaultTransfers.Add(address);
            _rawDefaultTransfers.Add(read);
            _foldedValueOffsets[value.SourceOffset] = [address.SourceOffset, read.SourceOffset];
        }
        return true;

        bool Unproven()
            => _budget.Exhausted ? Exhausted()
                : DeclineFalse(ClassicInverseDeclineReason.UnrealizedSemanticEffect,
                    "a default initializer has no exact private local initialization and adjacent typed use");

        bool Exhausted()
        {
            _terminal = Failure("default initialization correspondence exhausted the planning budget");
            return false;
        }
    }
}
