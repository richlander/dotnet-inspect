import {
  KeybindingRegistry,
  type KeybindingConflict,
} from "./keybinding-registry.ts";

export const WORKBENCH_KEYBINDING_PRIORITY = {
  workspace: 100,
  popover: 150,
  element: 200,
  spotlight: 300,
  documentViewer: 310,
  graphSource: 320,
  annotatedSource: 325,
  methodBodyDiff: 327,
  unavailableWorkspace: 330,
  settings: 340,
  metadataExplorer: 350,
} as const;

function reportConflict(conflict: KeybindingConflict): void {
  const ids = conflict.bindings.map(binding => binding.id).join(", ");
  console.error(`Ambiguous keybinding '${conflict.event.key}': ${ids}`);
}

export function createWorkbenchKeybindings(): KeybindingRegistry {
  return new KeybindingRegistry({ onConflict: reportConflict });
}
