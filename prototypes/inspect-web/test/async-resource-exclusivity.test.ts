import assert from "node:assert/strict";
import test from "node:test";

import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

import { idleAsyncResource, type AsyncResource } from "../src/data.ts";

const sourceRoot = join(dirname(fileURLToPath(import.meta.url)), "..", "src");

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

// `idle` is the variant the renderer short-circuits to the placeholder, so a widened `idle`
// is precisely the shape that could smuggle a previous scan's payload or failure past that
// short-circuit. Both combinations were missing from this roster, and adding them to
// `AsyncResource` left the suite green.
const idleWithData: AsyncResource<Payload> = {
  status: "idle",
  // @ts-expect-error -- `idle` must not carry `data`
  data: { value: 1 },
};

const idleWithError: AsyncResource<Payload> = {
  status: "idle",
  // @ts-expect-error -- `idle` must not carry `error`
  error: "boom",
};

// Reading each binding keeps `noUnusedLocals` from removing the assertions above, and
// keeps the file honest about being compiled rather than merely parsed.
const forbidden = [
  loadingWithData,
  loadingWithError,
  readyWithError,
  failedWithData,
  idleWithKey,
  idleWithData,
  idleWithError,
];

// The declaration drives the enforcement set. `forbidden.length === 5` was a pinned literal:
// adding a sixth variant to `AsyncResource`, or a fifth field, required no change here and
// produced no failure, so the roster restated one moment's truth. Reading the union instead
// means a new variant or field is unenforced *until* its combinations are enumerated.
function requiredCombinations(): Set<string> {
  const source = readFileSync(join(sourceRoot, "data.ts"), "utf8");
  const declaration =
    /export type AsyncResource<T> =([\s\S]*?);\n/.exec(source)?.[1] ?? "";
  const variants = [...declaration.matchAll(/\{([^}]*)\}/g)].map(match => {
    const body = match[1] ?? "";
    const fields = [...body.matchAll(/(\w+)\s*:/g)]
      .map(field => field[1])
      .filter((field): field is string => field !== undefined && field !== "status");
    const status = /status\s*:\s*"([^"]+)"/.exec(body)?.[1] ?? "";
    return { status, fields: new Set(fields) };
  });

  assert.ok(
    variants.length >= 4 && variants.every(variant => variant.status !== ""),
    "the AsyncResource anchor stopped matching, so this gate derives nothing");

  const everyField = new Set(variants.flatMap(variant => [...variant.fields]));
  const required = new Set<string>();
  for (const variant of variants) {
    for (const field of everyField) {
      if (!variant.fields.has(field)) required.add(`${variant.status}.${field}`);
    }
  }
  return required;
}

// Each directive names its combination in prose, which is also how a reader checks it, so
// the enumeration is read from the same place rather than maintained beside it.
function enumeratedCombinations(): Set<string> {
  const source = readFileSync(fileURLToPath(import.meta.url), "utf8");
  return new Set(
    [...source.matchAll(/@ts-expect-error -- `(\w+)` must not carry `(\w+)`/g)]
      .map(match => `${match[1]}.${match[2]}`));
}

test("the forbidden AsyncResource combinations are rejected by the compiler", () => {
  // The compile-time directives above are the actual gate; `npm test` typechecks this
  // file through `test/tsconfig.json` before it runs. This asserts the file was not
  // quietly excluded from that typecheck -- if it were, the directives would prove
  // nothing and this suite would still be green.
  const enumerated = enumeratedCombinations();
  assert.equal(forbidden.length, enumerated.size);

  assert.deepEqual(
    [...enumerated].sort((a, b) => a.localeCompare(b)),
    [...requiredCombinations()].sort((a, b) => a.localeCompare(b)),
    "the exclusivity roster no longer matches the AsyncResource union. A missing entry is a "
    + "variant/field combination nothing rejects; an extra one no longer exists.");

  assert.deepEqual(idleAsyncResource<Payload>(), { status: "idle" });
});
