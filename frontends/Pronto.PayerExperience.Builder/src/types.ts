// Wire shape of the published experience definition (snake_case), mirroring
// frontends/Pronto.BillerPayments.Pwa/src/types.ts and the BillerExperienceDefinition contract.
export interface ExperienceDefinition {
  schema_version: string;
  biller_id: string;
  brand: {
    display_name: string;
    primary_color: string;
    secondary_color: string;
    logo_asset_id?: string | null;
    font_family?: string | null;
  };
  content: {
    heading: string;
    introduction: string;
    support_text: string;
    privacy_policy_url: string;
    terms_of_service_url: string;
  };
  pwa: {
    name: string;
    short_name: string;
    theme_color: string;
    background_color: string;
    icon_asset_id?: string | null;
  };
  enabled_payment_capabilities: string[];
  ui?: unknown;
  preferences?: unknown;
}

export interface BrandAsset {
  kind: 'logo' | 'icon' | 'hero' | 'background';
  url: string;
  description?: string;
}

// The bounded creative input an AI generator is allowed to author against.
// Assembled from the definition + biller facts; deliberately separate from the
// functional contract (payment capabilities, preferences) the generated code must honor.
export interface DesignBrief {
  biller_slug: string;
  display_name: string;
  bill_type?: string;
  primary_color: string;
  secondary_color: string;
  font_family: string;
  voice_and_tone: string;
  visual_style: string;
  brand_keywords: string[];
  reference_url?: string;
  assets: BrandAsset[];
  layout_intent?: string;
  // Functional guardrails the skin must respect but never change.
  enabled_payment_capabilities: string[];
  content: ExperienceDefinition['content'];
}

// A generated skin is exactly the set of files the core marks editable
// (frontends/Pronto.BillerPayments.Pwa/src/skin/index.ts -> SKIN_EDITABLE_FILES).
export interface GeneratedSkin {
  themeCss: string;
  chromeTsx: string;
  notes?: string;
}
