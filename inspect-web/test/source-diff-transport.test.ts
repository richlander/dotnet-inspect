import assert from "node:assert/strict";
import { describe, test } from "node:test";
import {
  sourceDiffPayloadDecoder,
  type BrowserSourceComparisonResult,
  type BrowserSourceComparisonEndpoint,
} from "../src/source-diff-transport.ts";

function endpoint(version: string): BrowserSourceComparisonEndpoint {
  return {
    packageId: "Example.Package",
    version,
    framework: "net10.0",
    assembly: "Example",
    assetPath: "lib/net10.0/Example.dll",
    moduleVersionId: "00000000-0000-0000-0000-000000000001",
    assemblyIdentity: "Example, Version=1.0.0.0",
    memberIdentity: "Example.Widget::Run()",
    metadataToken: 0x06000001,
    state: "Available",
    detail: null,
    text: null,
    browseUrl: "https://github.com/example/repo/blob/abc/Widget.cs",
    repositoryUrl: "https://github.com/example/repo",
    revision: "abc",
  };
}

function exactResult(): BrowserSourceComparisonResult {
  return {
    version: 1,
    kind: "Succeeded",
    value: {
      request: {
        packageId: "Example.Package",
        beforeVersion: "1.0.0",
        afterVersion: "2.0.0",
        framework: "net10.0",
        assembly: "Example",
        typeIdentity: "Example.Widget",
        memberName: "Run",
        selectorKey: "Run()",
        metadataToken: 0x06000001,
      },
      status: "Compared",
      isExact: true,
      before: endpoint("1.0.0"),
      after: endpoint("2.0.0"),
      diff: {
        version: 1,
        before: {
          label: "1.0.0",
          lines: ["void Run()"],
          finalLineTerminator: "Absent",
        },
        after: {
          label: "2.0.0",
          lines: ["void Run()"],
          finalLineTerminator: "Absent",
        },
        relations: [{
          kind: "Correspondence",
          beforeCoordinates: [0],
          afterCoordinates: [0],
          content: "Unchanged",
          placement: "Stable",
        }],
        statistics: {
          added: 0,
          removed: 0,
          changedBefore: 0,
          changedAfter: 0,
          movedBefore: 0,
          movedAfter: 0,
        },
        changes: [],
      },
      failure: null,
    },
    failureKind: null,
    error: null,
    diagnostic: null,
    reason: null,
    capacity: null,
  };
}

function encoded(value: unknown): string {
  return JSON.stringify(value);
}

function decode(value: unknown) {
  return sourceDiffPayloadDecoder.decode(value);
}

function assertRejected(
  value: unknown,
  reason: "invalid" | "oversized",
): void {
  const result = decode(value);
  assert.equal(result.kind, "rejected");
  if (result.kind === "rejected") assert.equal(result.reason, reason);
}

function set(
  value: unknown,
  path: readonly string[],
  replacement: unknown,
): void {
  function container(candidate: unknown): object {
    if (typeof candidate !== "object" || candidate === null)
      throw new Error("Test payload path does not identify an object.");
    return candidate;
  }
  let current = value;
  for (const segment of path.slice(0, -1)) {
    current = Reflect.get(container(current), segment);
  }
  Reflect.set(container(current), path.at(-1)!, replacement);
}

function changed(mutator: (value: unknown) => void): unknown {
  const value = structuredClone(exactResult());
  mutator(value);
  return value;
}

void describe("Source diff transport decoder", () => {
  test("decodes the closed typed payload", () => {
    const expected = exactResult();
    assert.deepEqual(decode(encoded(expected)), {
      kind: "decoded",
      value: expected,
    });
  });

  test("admits exact aggregate coordinate and mapped-change capacities", () => {
    const value = changed(candidate => {
      set(candidate, ["value", "diff", "before", "lines"],
        Array.from({ length: 1_024 }, () => "x"));
      set(candidate, ["value", "diff", "after", "lines"],
        Array.from({ length: 1_024 }, () => "x"));
      set(candidate, ["value", "diff", "relations"], [{
        kind: "Correspondence",
        beforeCoordinates: Array.from({ length: 1_024 }, (_, index) => index),
        afterCoordinates: Array.from({ length: 1_024 }, (_, index) => index),
        content: "Unchanged",
        placement: "Stable",
      }]);
      set(candidate, ["value", "diff", "changes"],
        Array.from({ length: 2_048 }, (_, index) => ({
        before: { start: index % 1_024, count: 1 },
        after: { start: 0, count: 0 },
        innerMappings: [],
        annotations: [],
        })));
    });
    assert.equal(decode(encoded(value)).kind, "decoded");
  });

  for (const [name, mutate] of [
    ["unknown result field", (value: unknown) => {
      set(value, ["unknown"], true);
    }],
    ["unknown result kind", (value: unknown) => {
      set(value, ["kind"], "Deferred");
    }],
    ["unknown diff version", (value: unknown) => {
      set(value, ["value", "diff", "version"], 2);
    }],
    ["unknown relation enum", (value: unknown) => {
      set(value, ["value", "diff", "relations", "0", "content"], "Similar");
    }],
    ["out-of-range coordinate", (value: unknown) => {
      set(value, ["value", "diff", "relations", "0", "beforeCoordinates"], [1]);
    }],
    ["out-of-range change", (value: unknown) => {
      set(value, ["value", "diff", "changes"], [{
        before: { start: 1, count: 1 },
        after: { start: 0, count: 0 },
        innerMappings: [],
        annotations: [],
      }]);
    }],
    ["empty mapped change", (value: unknown) => {
      set(value, ["value", "diff", "changes"], [{
        before: { start: 0, count: 0 },
        after: { start: 0, count: 0 },
        innerMappings: [],
        annotations: [],
      }]);
    }],
    ["empty inner mapping", (value: unknown) => {
      set(value, ["value", "diff", "changes"], [{
        before: { start: 0, count: 1 },
        after: { start: 0, count: 1 },
        innerMappings: [{
          before: { line: 0, start: 0, count: 0 },
          after: { line: 0, start: 0, count: 0 },
        }],
        annotations: [],
      }]);
    }],
    ["empty annotation text", (value: unknown) => {
      set(value, ["value", "diff", "changes"], [{
        before: { start: 0, count: 1 },
        after: { start: 0, count: 1 },
        innerMappings: [],
        annotations: [{
          text: "",
          severity: "Note",
          targetKind: "Change",
          side: null,
          line: null,
          span: null,
        }],
      }]);
    }],
    ["logical line with a newline", (value: unknown) => {
      set(value, ["value", "diff", "before", "lines"], ["void\nRun()"]);
    }],
    ["malformed UTF-16", (value: unknown) => {
      set(value, ["value", "diff", "before", "lines"], ["\uD800"]);
    }],
    ["span splitting a surrogate pair", (value: unknown) => {
      set(value, ["value", "diff", "before", "lines"], ["😀"]);
      set(value, ["value", "diff", "after", "lines"], ["😀"]);
      set(value, ["value", "diff", "changes"], [{
        before: { start: 0, count: 1 },
        after: { start: 0, count: 1 },
        innerMappings: [{
          before: { line: 0, start: 1, count: 1 },
          after: { line: 0, start: 0, count: 2 },
        }],
        annotations: [],
      }]);
    }],
    ["invalid relation case", (value: unknown) => {
      set(value, ["value", "diff", "relations", "0", "beforeCoordinates"], []);
    }],
    ["endpoint text retained with complete diff", (value: unknown) => {
      set(value, ["value", "before", "text"], "void Run()");
    }],
    ["capacity at or below limit", (value: unknown) => {
      set(value, ["kind"], "TooComplex");
      set(value, ["value"], null);
      set(value, ["capacity"], {
        dimension: "Relations",
        limit: 2_048,
        actual: 2_048,
      });
    }],
  ] as const) {
    test(`rejects ${name}`, () => {
      assertRejected(encoded(changed(mutate)), "invalid");
    });
  }

  test("rejects malformed and non-text payloads", () => {
    assertRejected("{", "invalid");
    assertRejected(exactResult(), "invalid");
  });

  for (const [name, mutate] of [
    ["endpoint bytes", (value: unknown) => {
      set(value, ["value", "status"], "Unavailable");
      set(value, ["value", "isExact"], false);
      set(value, ["value", "diff"], null);
      set(value, ["value", "before", "text"],
        "x".repeat(128 * 1_024 + 1));
    }],
    ["endpoint lines", (value: unknown) => {
      set(value, ["value", "status"], "Unavailable");
      set(value, ["value", "isExact"], false);
      set(value, ["value", "diff"], null);
      set(value, ["value", "before", "text"], "\n".repeat(1_024));
    }],
    ["canonical final terminator bytes", (value: unknown) => {
      set(value, ["value", "diff", "before", "lines"],
        ["x".repeat(128 * 1_024)]);
      set(value, ["value", "diff", "before", "finalLineTerminator"],
        "Present");
    }],
    ["relations", (value: unknown) => {
      set(value, ["value", "diff", "relations"],
        Array.from({ length: 2_049 }, () => ({
        kind: "Correspondence",
        beforeCoordinates: [0],
        afterCoordinates: [0],
        content: "Unchanged",
        placement: "Stable",
        })));
    }],
    ["coordinate occurrences", (value: unknown) => {
      set(value, ["value", "diff", "before", "lines"],
        Array.from({ length: 1_024 }, () => "x"));
      set(value, ["value", "diff", "after", "lines"],
        Array.from({ length: 1_024 }, () => "x"));
      set(value, ["value", "diff", "relations"], [
        {
          kind: "Correspondence",
          beforeCoordinates:
            Array.from({ length: 1_024 }, (_, index) => index),
          afterCoordinates:
            Array.from({ length: 1_024 }, (_, index) => index),
          content: "Unchanged",
          placement: "Stable",
        },
        {
          kind: "Addition",
          beforeCoordinates: [],
          afterCoordinates: [0],
          content: null,
          placement: null,
        },
      ]);
    }],
    ["mapped changes", (value: unknown) => {
      set(value, ["value", "diff", "changes"],
        Array.from({ length: 2_049 }, () => ({
        before: { start: 0, count: 1 },
        after: { start: 0, count: 0 },
        innerMappings: [],
        annotations: [],
        })));
    }],
    ["inner mappings", (value: unknown) => {
      set(value, ["value", "diff", "changes"], [{
        before: { start: 0, count: 1 },
        after: { start: 0, count: 1 },
        innerMappings: Array.from({ length: 1_025 }, () => ({
          before: { line: 0, start: 0, count: 1 },
          after: { line: 0, start: 0, count: 1 },
        })),
        annotations: [],
      }]);
    }],
    ["annotations", (value: unknown) => {
      set(value, ["value", "diff", "changes"], [{
        before: { start: 0, count: 1 },
        after: { start: 0, count: 1 },
        innerMappings: [],
        annotations: Array.from({ length: 513 }, () => ({
          text: "note",
          severity: "Note",
          targetKind: "Change",
          side: null,
          line: null,
          span: null,
        })),
      }]);
    }],
    ["annotation text", (value: unknown) => {
      set(value, ["value", "diff", "changes"], [{
        before: { start: 0, count: 1 },
        after: { start: 0, count: 1 },
        innerMappings: [],
        annotations: [{
          text: "x".repeat(64 * 1_024 + 1),
          severity: "Note",
          targetKind: "Change",
          side: null,
          line: null,
          span: null,
        }],
      }]);
    }],
    ["auxiliary text", (value: unknown) => {
      set(value, ["value", "request", "typeIdentity"],
        "x".repeat(16 * 1_024 + 1));
    }],
    ["failure diagnostic text", (value: unknown) => {
      set(value, ["kind"], "Failed");
      set(value, ["value"], null);
      set(value, ["failureKind"], "Expected");
      set(value, ["error"], "x".repeat(16 * 1_024 + 1));
    }],
  ] as const) {
    test(`rejects over-limit ${name}`, () => {
      assertRejected(encoded(changed(mutate)), "oversized");
    });
  }

  test("rejects UTF-8 encoding expansion beyond the result ceiling", () => {
    const padding = "😀".repeat(300_000);
    assert.ok(encoded(padding).length < 1_024 * 1_024);
    assertRejected(encoded(padding), "oversized");
  });
});
