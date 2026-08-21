namespace tsbindgen;

internal sealed class UnsupportedWireContractException(string location, string reason)
    : Exception($"{location}: {reason}.");
