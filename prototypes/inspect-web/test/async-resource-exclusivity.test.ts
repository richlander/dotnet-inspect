import assert from "node:assert/strict";
import test from "node:test";

import { idleAsyncResource, type AsyncResource } from "../src/data.ts";

// `AsyncResource` claims that a result, an error, and a request key exist only on the
// variants entitled to them -- that is the whole reason for replacing the four parallel
// fields (`data`, `loading`, `error`, `key`) with a union. Nothing enforced that claim.
//
// Adversarial review demonstrated it by widening each variant independently -- adding
// `data?` and `error?` to `loading`, `error?` to `ready`, `data?` to `failed` -- and
// running the full suite. All 500 tests passed for each. Every contradictory combination
// the slice says it eliminated could come back with the suite still green, because a
// runtime test can only observe states the code actually constructs, and a weakened type
// is a statement about states it *permits*.
//
// So the gate has to be the compiler. Each `@ts-expect-error` below asserts that the
// combination on the next line is rejected. TypeScript reports an unused
// `@ts-expect-error` directive as an error in its own right, so weakening a variant to
// admit one of these does not silently satisfy the assertion -- it fails typechecking
// from the other direction. That is what makes these two-way.

type Payload = { value: number };

// A loading resource has no result yet. Carrying one is the state that lets a stale render
// show data while a newer request is in flight.
const loadingWithData: AsyncResource<Payload> = {
  status: "loading",
  key: "k",
  // @ts-expect-error -- `loading` must not carry `data`
  data: { value: 1 },
};

// A loading resource has not failed. Carrying an error is what lets a spinner and a failure
// message describe the same request.
const loadingWithError: AsyncResource<Payload> = {
  status: "loading",
  key: "k",
  // @ts-expect-error -- `loading` must not carry `error`
  error: "boom",
};

// A ready resource succeeded. An error beside the result is the combination that lets a
// consumer suppress a failure by preferring the data, or blank a good result by preferring
// the error.
const readyWithError: AsyncResource<Payload> = {
  status: "ready",
  key: "k",
  data: { value: 1 },
  // @ts-expect-error -- `ready` must not carry `error`
  error: "boom",
};

// A failed resource has no result. Carrying one is how a failure renders as success-shaped
// output, which this repository's rules forbid outright.
const failedWithData: AsyncResource<Payload> = {
  status: "failed",
  key: "k",
  error: "boom",
  // @ts-expect-error -- `failed` must not carry `data`
  data: { value: 1 },
};

// An idle resource has never been requested, so it owns no request key: a key on `idle` is
// what would let a stale-scope check believe a scan had been requested for that scope.
const idleWithKey: AsyncResource<Payload> = {
  status: "idle",
  // @ts-expect-error -- `idle` must not carry `key`
  key: "k",
};

// Reading each binding keeps `noUnusedLocals` from removing the assertions above, and
// keeps the file honest about being compiled rather than merely parsed.
const forbidden = [
  loadingWithData,
  loadingWithError,
  readyWithError,
  failedWithData,
  idleWithKey,
];

test("the forbidden AsyncResource combinations are rejected by the compiler", () => {
  // The compile-time directives above are the actual gate; `npm test` typechecks this
  // file through `test/tsconfig.json` before it runs. This asserts the file was not
  // quietly excluded from that typecheck -- if it were, the directives would prove
  // nothing and this suite would still be green.
  assert.equal(forbidden.length, 5);
  assert.deepEqual(idleAsyncResource<Payload>(), { status: "idle" });
});
