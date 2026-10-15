import assert from "node:assert/strict";
import test from "node:test";

import {
  browserDocumentPresentationLeaves,
  completeDocumentLeafContainment,
  prepareDocumentLeafContainment,
} from "../src/contained-document.ts";
import type {
  BrowserDocumentLeafContainmentFailureKind,
  BrowserDocumentLeafContainmentOutcome,
  InertString,
} from "../src/facades/inspect-web-package.d.ts";

function isInertString(value: unknown): value is InertString {
  return typeof value === "string";
}

function inertString(value: string): InertString {
  const wire: unknown = JSON.parse(JSON.stringify(value));
  if (!isInertString(wire)) {
    throw new TypeError("The inert-string fixture must remain a JSON string.");
  }
  return wire;
}

function contained(
  generation = "document-4",
): BrowserDocumentLeafContainmentOutcome {
  return {
    outcome: "contained",
    generation,
    leaves: [
      { identity: 1, policy: "Field", text: inertString("field\\^Jvalue") },
      { identity: 2, policy: "Prose", text: inertString("line\\u202Eone") },
    ],
  };
}

function rejected(
  kind: BrowserDocumentLeafContainmentFailureKind,
): BrowserDocumentLeafContainmentOutcome {
  return {
    outcome: "rejected",
    generation: "document-4",
    failure: {
      kind,
      identity: null,
      actual: 20,
      limit: 10,
    },
  };
}

function prepared() {
  return prepareDocumentLeafContainment(
    "document-4",
    [
      { identity: 2, policy: "Prose", text: "line\u202Eone" },
      { identity: 1, policy: "Field", text: "field\nvalue" },
    ],
  );
}

function rejectionReason(
  completion: ReturnType<typeof completeDocumentLeafContainment>,
) {
  assert.equal(completion.kind, "rejected");
  if (completion.kind !== "rejected") {
    throw new Error("Expected leaf completion to reject.");
  }
  return completion.reason;
}

test("leaf completion restores request order after exact correspondence", () => {
  const request = prepared();
  assert.deepEqual(
    browserDocumentPresentationLeaves(request),
    [
      { identity: 2, policy: "Prose", text: "line\u202Eone" },
      { identity: 1, policy: "Field", text: "field\nvalue" },
    ],
  );

  const completion = completeDocumentLeafContainment(
    request,
    "document-4",
    contained(),
  );

  assert.equal(completion.kind, "completed");
  if (completion.kind !== "completed") return;
  assert.equal(completion.population.generation, "document-4");
  assert.deepEqual(
    completion.population.leaves,
    [
      { identity: 2, policy: "Prose", text: "line\\u202Eone" },
      { identity: 1, policy: "Field", text: "field\\^Jvalue" },
    ],
  );
});

test("preparation rejects invalid or duplicate identities", () => {
  assert.throws(
    () => prepareDocumentLeafContainment(
      "document-4",
      [{ identity: -1, policy: "Field", text: "value" }],
    ),
    /non-negative safe integer/,
  );
  assert.throws(
    () => prepareDocumentLeafContainment(
      "document-4",
      [
        { identity: 1, policy: "Field", text: "first" },
        { identity: 1, policy: "Prose", text: "second" },
      ],
    ),
    /duplicated/,
  );
});

test("preparation rejects an invalid generation or policy", () => {
  assert.throws(
    () => prepareDocumentLeafContainment(" ", []),
    /must not be empty/,
  );
  assert.throws(
    () => prepareDocumentLeafContainment(
      "document-4",
      [
        {
          identity: 1,
          // @ts-expect-error Runtime validation closes the generated numeric enum.
          policy: "Code",
          text: "value",
        },
      ],
    ),
    /unsupported text policy/,
  );
});

test("completion rejects a closed or replaced generation", () => {
  assert.deepEqual(
    completeDocumentLeafContainment(prepared(), null, contained()),
    {
      kind: "rejected",
      reason: "stale-generation",
      identity: null,
      failure: null,
    },
  );
  assert.deepEqual(
    completeDocumentLeafContainment(
      prepared(),
      "document-5",
      contained(),
    ),
    {
      kind: "rejected",
      reason: "stale-generation",
      identity: null,
      failure: null,
    },
  );
});

test("completion rejects a result from another generation", () => {
  assert.deepEqual(
    completeDocumentLeafContainment(
      prepared(),
      "document-4",
      contained("document-3"),
    ),
    {
      kind: "rejected",
      reason: "wrong-generation",
      identity: null,
      failure: null,
    },
  );
});

test("completion preserves a managed rejection", () => {
  const outcome = rejected("OutputTextTooLarge");
  if (outcome.outcome !== "rejected") return;
  const completion = completeDocumentLeafContainment(
    prepared(),
    "document-4",
    outcome,
  );

  assert.equal(completion.kind, "rejected");
  if (completion.kind !== "rejected") return;
  assert.equal(completion.reason, "managed-rejection");
  assert.deepEqual(completion.failure, outcome.failure);
});

test("completion rejects duplicate, surplus, and missing leaves", () => {
  const duplicate = contained();
  if (duplicate.outcome !== "contained") return;
  assert.equal(
    rejectionReason(completeDocumentLeafContainment(
      prepared(),
      "document-4",
      { ...duplicate, leaves: [duplicate.leaves[0]!, duplicate.leaves[0]!] },
    )),
    "duplicate-leaf",
  );

  assert.equal(
    rejectionReason(completeDocumentLeafContainment(
      prepared(),
      "document-4",
      {
        ...duplicate,
        leaves: [
          ...duplicate.leaves,
          { identity: 3, policy: "Field", text: inertString("surplus") },
        ],
      },
    )),
    "surplus-leaf",
  );

  assert.equal(
    rejectionReason(completeDocumentLeafContainment(
      prepared(),
      "document-4",
      { ...duplicate, leaves: [duplicate.leaves[0]!] },
    )),
    "missing-leaf",
  );
});

test("completion rejects a policy mismatch", () => {
  const outcome = contained();
  if (outcome.outcome !== "contained") return;

  assert.deepEqual(
    completeDocumentLeafContainment(
      prepared(),
      "document-4",
      {
        ...outcome,
        leaves: [
          { ...outcome.leaves[0]!, policy: "Prose" },
          outcome.leaves[1]!,
        ],
      },
    ),
    {
      kind: "rejected",
      reason: "policy-mismatch",
      identity: 1,
      failure: null,
    },
  );
});
