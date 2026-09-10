// Shared, test-controlled state for the generated-facade stand-ins the coordinator composes.
// One instance backs every scenario, so each scenario resets it before importing a fresh
// coordinator.

export interface FacadeRecording {
  events: string[];
  failing: Set<string>;
}

export const recording: FacadeRecording = {
  events: [],
  failing: new Set<string>(),
};

export function resetRecording(failing: readonly string[] = []): void {
  recording.events = [];
  recording.failing = new Set(failing);
}

export function record(event: string): void {
  recording.events.push(event);
}
