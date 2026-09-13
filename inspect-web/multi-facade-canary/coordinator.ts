import {
  createRuntime,
  initializeRuntime as initializeAlpha,
} from "./facades/alpha.js";
import {
  initializeRuntime as initializeBeta,
} from "./facades/beta.js";

let initialization: Promise<void> | undefined;

async function initializeCore(): Promise<void> {
  const runtime = createRuntime();
  await initializeAlpha(runtime);
  await initializeBeta(runtime);
}

export function initializeFacades(): Promise<void> {
  initialization ??= initializeCore();
  return initialization;
}
