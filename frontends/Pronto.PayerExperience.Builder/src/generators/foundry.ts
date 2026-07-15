import { DefaultAzureCredential } from '@azure/identity';
import type { DesignBrief, GeneratedSkin } from '../types';
import type { SkinGenerator } from './index';

// Real bespoke-authoring path: Claude Opus hosted on Azure AI Foundry, called through
// the unified model-inference chat/completions API so the same endpoint that serves the
// OpenAI config models also serves Anthropic. Auth is workload identity / az login by
// default (no key in pods), with an optional key for local runs.
export interface FoundryOptions {
  endpoint?: string;
  deployment?: string;
  chatUrl?: string;
  apiVersion?: string;
  apiKey?: string;
  maxTokens?: number;
}

const COGNITIVE_SERVICES_SCOPE = 'https://cognitiveservices.azure.com/.default';

export class FoundryOpusSkinGenerator implements SkinGenerator {
  readonly name = 'opus';
  private readonly options: FoundryOptions;

  constructor(options: FoundryOptions = {}) {
    this.options = {
      endpoint: options.endpoint ?? process.env.FOUNDRY_ENDPOINT,
      deployment: options.deployment ?? process.env.FOUNDRY_OPUS_DEPLOYMENT ?? 'claude-opus-4-1',
      chatUrl: options.chatUrl ?? process.env.FOUNDRY_CHAT_URL,
      apiVersion: options.apiVersion ?? process.env.FOUNDRY_API_VERSION ?? '2024-05-01-preview',
      apiKey: options.apiKey ?? process.env.FOUNDRY_API_KEY,
      maxTokens: options.maxTokens ?? 8000,
    };
  }

  async generate(brief: DesignBrief): Promise<GeneratedSkin> {
    const url = this.resolveUrl();
    const body = {
      model: this.options.deployment,
      max_tokens: this.options.maxTokens,
      temperature: 0.4,
      messages: [
        { role: 'system', content: SYSTEM_PROMPT },
        { role: 'user', content: JSON.stringify({ design_brief: brief, editable_files: EDITABLE_FILES, contract: CONTRACT_TS }) },
      ],
      response_format: { type: 'json_object' },
    };

    const response = await fetch(url, {
      method: 'POST',
      headers: { 'content-type': 'application/json', ...(await this.authHeaders()) },
      body: JSON.stringify(body),
    });
    if (!response.ok) {
      throw new Error(`Foundry request failed (${response.status}): ${await response.text().catch(() => '')}`);
    }
    const payload = (await response.json()) as { choices?: Array<{ message?: { content?: string } }> };
    const content = payload.choices?.[0]?.message?.content;
    if (!content) throw new Error('Foundry returned no content.');
    return parseSkin(content);
  }

  private resolveUrl(): string {
    if (this.options.chatUrl) return this.options.chatUrl;
    if (!this.options.endpoint) {
      throw new Error('Set FOUNDRY_ENDPOINT (or FOUNDRY_CHAT_URL) to call the Opus generator.');
    }
    const base = this.options.endpoint.replace(/\/$/, '');
    return `${base}/models/chat/completions?api-version=${this.options.apiVersion}`;
  }

  private async authHeaders(): Promise<Record<string, string>> {
    if (this.options.apiKey) return { 'api-key': this.options.apiKey };
    const token = await new DefaultAzureCredential().getToken(COGNITIVE_SERVICES_SCOPE);
    if (!token) throw new Error('Unable to acquire an Azure AD token for Foundry.');
    return { authorization: `Bearer ${token.token}` };
  }
}

function parseSkin(content: string): GeneratedSkin {
  const json = stripFence(content);
  const parsed = JSON.parse(json) as Partial<GeneratedSkin>;
  if (!parsed.themeCss || !parsed.chromeTsx) {
    throw new Error('Opus response missing themeCss/chromeTsx.');
  }
  return { themeCss: parsed.themeCss, chromeTsx: parsed.chromeTsx, notes: parsed.notes };
}

function stripFence(text: string): string {
  const trimmed = text.trim();
  const fence = trimmed.match(/^```(?:json)?\s*([\s\S]*?)\s*```$/);
  return fence ? fence[1] : trimmed;
}

const EDITABLE_FILES = ['src/skin/theme.css', 'src/skin/chrome.tsx'];

const CONTRACT_TS = `export interface HeaderProps { brand: { display_name: string; primary_color: string; secondary_color: string; font_family: string | null } }
export interface IntroProps { eyebrow: string; heading: string; subheading: string }
export interface FooterProps { brand: HeaderProps['brand']; content: { support_text: string; privacy_policy_url: string; terms_of_service_url: string } }
// chrome.tsx must export: Header(HeaderProps), Intro(IntroProps), Footer(FooterProps)`;

const SYSTEM_PROMPT = `You are a senior React/CSS designer producing a bespoke skin for one biller's payment PWA.
Return ONLY a JSON object: {"themeCss": string, "chromeTsx": string, "notes": string}.

Hard rules — the build and a scripted Playwright gate will reject violations:
- Edit ONLY the two editable files. Never touch payment flow, service calls, or money logic.
- chrome.tsx is PRESENTATIONAL ONLY: no fetch, no network, no state with side effects, no payment logic, no new npm imports. Import types only from './contract'. Export exactly Header, Intro, Footer with the given signatures.
- Preserve every CSS class name the core renders (app, mark, account-nav, intro, card, card-copy, bill, choices, method-chips, check, notice, consent, actions, success, success-icon, pill, history-row, preference-summary, alert, center). Restyle them freely; do not delete them or the data-testid controls will lose styling.
- No <script>, no external URLs in CSS except fonts you declare, no @import of untrusted origins, no inline event handlers.
- Keep strong color contrast (WCAG AA) and respect prefers-reduced-motion.
- Use the brand colors and voice/tone from the brief. Make it look genuinely different from a generic template.`;
