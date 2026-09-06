import type { MemberFacts } from "../src/member-detail-inspection.ts";

export function memberFactsFixture(
  mode: "populated" | "zero" | "long" = "populated",
): MemberFacts {
  const zero = mode === "zero";
  const long = mode === "long";
  return {
    metadataToken: 0x06000125,
    signals: {
      allocations: zero ? 0 : 1,
      copies: long ? 2147483647 : 0,
      reflection: 0,
      throws: zero ? 0 : long ? 2147483647 : 1,
      catches: long ? 2147483647 : 0,
      finallys: long ? 2147483647 : 0,
      unsafe: false,
      allocatesInLoop: false,
      evidenceOffsets: [],
      exceptionTypes: [],
    },
    allocations: zero ? [] : [{
      kind: "newobj",
      type: "System.Text.Json.JsonException",
      offset: "IL_0020",
      countedAsHeap: true,
      frequency: "conditional",
      multiplicity: "once",
      path: "conditional",
      escape: "escapes",
      inLoop: false,
      estimatedSizeBytes: null,
      detail: null,
    }],
    calls: zero ? [] : [
      {
        callee: "JsonTypeInfo.EnsureConfigured()",
        offset: long ? "IL_12345678" : "IL_0008",
        opcode: "call",
        kind: "direct",
        multiplicity: "once",
        inLoop: false,
      },
      {
        callee: "JsonConverter.ReadCore()",
        offset: long ? "IL_23456789" : "IL_0014",
        opcode: "callvirt",
        kind: "virtual",
        multiplicity: "once",
        inLoop: false,
      },
      {
        callee: "JsonException..ctor()",
        offset: "IL_0020",
        opcode: "newobj",
        kind: "constructor",
        multiplicity: "once",
        inLoop: false,
      },
    ],
    safety: [],
    exceptionRegions: [],
    performanceOpportunities: [],
    diagnostics: [],
  };
}

export function allocationFactsFixture(
  mode: "populated" | "long" = "populated",
): MemberFacts {
  const facts = memberFactsFixture();
  const long = mode === "long";
  return {
    ...facts,
    signals: { ...facts.signals, allocations: 2, allocatesInLoop: true },
    allocations: [
      {
        kind: "Object",
        type: long
          ? "Example.Serialization.BufferedDocumentReader<System.Collections.Generic.Dictionary<System.String, System.Collections.Generic.List<System.Text.Json.JsonElement>>>"
          : "System.Text.Json.JsonException",
        offset: long ? "IL_12345678" : "IL_0020",
        countedAsHeap: true,
        frequency: "Always",
        multiplicity: "Conditional",
        path: "ErrorPath",
        escape: "ThrowPath",
        inLoop: false,
        estimatedSizeBytes: null,
        detail: null,
      },
      {
        kind: "Array",
        type: "System.Byte[]",
        offset: "IL_0048",
        countedAsHeap: true,
        frequency: "PerIteration",
        multiplicity: "Loop",
        path: "LoopBody",
        escape: "LocalOnly",
        inLoop: true,
        estimatedSizeBytes: long ? 2147483647 : 280,
        detail: null,
      },
      {
        kind: "Enumerator",
        type: long ? null
          : "System.Collections.Generic.Dictionary<System.String, System.Text.Json.JsonElement>.Enumerator",
        offset: "IL_009C",
        countedAsHeap: false,
        frequency: "Always",
        multiplicity: "Once",
        path: "StraightLine",
        escape: "Unknown",
        inLoop: false,
        estimatedSizeBytes: null,
        detail: null,
      },
    ],
  };
}
