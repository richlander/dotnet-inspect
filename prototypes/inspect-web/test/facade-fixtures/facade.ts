// One stand-in for a generated facade module. The resolve hook in
// `engine-facades.test.ts` maps every `/inspect-web-*.js` specifier here and names the facade
// through the query string, so the seven modules the coordinator composes are seven distinct
// module instances that share one recording.

import { record, recording } from "./facade-state.ts";

const facade = new URL(import.meta.url).searchParams.get("facade") ?? "unknown";

interface RuntimeFixture {
  readonly owner: string;
}

export function createRuntime(): Promise<RuntimeFixture> {
  record(`createRuntime:${facade}`);
  return Promise.resolve({ owner: facade });
}

export async function initializeRuntime(
  runtime?: RuntimeFixture | PromiseLike<RuntimeFixture>,
): Promise<void> {
  const sharedRuntime = await runtime;
  if (sharedRuntime?.owner !== "inspect-web-host") {
    throw new Error(`${facade} did not receive the host-owned runtime`);
  }
  record(`begin:${facade}`);
  // A real generated module awaits the SDK before it resolves; the turn here is what makes
  // an overlapping initialization observable as interleaved begin/end events.
  await Promise.resolve();
  if (recording.failing.has(facade)) {
    record(`fail:${facade}`);
    throw new Error(`${facade} could not acquire its managed export assembly`);
  }
  record(`end:${facade}`);
}

export function configureHost(origin: string): void {
  record(`configureHost:${origin}`);
}

export function runEntryPoint(): Promise<number> {
  record(`runEntryPoint:${facade}`);
  return Promise.resolve(0);
}
