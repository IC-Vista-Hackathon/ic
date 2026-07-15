import type { DesignBrief, GeneratedSkin } from '../types';
import { DeterministicSkinGenerator } from './deterministic';
import { FoundryOpusSkinGenerator } from './foundry';

export interface SkinGenerator {
  readonly name: string;
  generate(brief: DesignBrief): Promise<GeneratedSkin>;
}

export type GeneratorMode = 'deterministic' | 'opus';

// Mirrors the app's existing Deterministic-vs-Azure split: the deterministic
// generator makes the pipeline runnable and testable with no external calls,
// while the Opus generator is the real bespoke-authoring path on Azure AI Foundry.
export function createGenerator(mode: GeneratorMode): SkinGenerator {
  switch (mode) {
    case 'opus':
      return new FoundryOpusSkinGenerator();
    case 'deterministic':
      return new DeterministicSkinGenerator();
    default:
      throw new Error(`Unknown generator mode: ${mode satisfies never}`);
  }
}

export { DeterministicSkinGenerator, FoundryOpusSkinGenerator };
