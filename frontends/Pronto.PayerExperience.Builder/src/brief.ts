import type { BrandAsset, DesignBrief, ExperienceDefinition } from './types';

// Assemble the Opus-facing design brief from the published definition plus a few
// biller facts. This is where the "under-specified config" gap is closed: we derive
// voice/tone, visual keywords and asset references rather than handing the model the
// enum'd ui block. Callers may override any derived field with explicit values.
export function assembleBrief(
  definition: ExperienceDefinition,
  slug: string,
  overrides: Partial<DesignBrief> = {},
): DesignBrief {
  const { brand, content } = definition;
  const assets: BrandAsset[] = [];
  if (brand.logo_asset_id) assets.push({ kind: 'logo', url: brand.logo_asset_id, description: `${brand.display_name} logo` });
  if (definition.pwa.icon_asset_id) assets.push({ kind: 'icon', url: definition.pwa.icon_asset_id });

  const billType = overrides.bill_type ?? inferBillType(content.heading);

  return {
    biller_slug: slug,
    display_name: brand.display_name,
    bill_type: billType,
    primary_color: brand.primary_color,
    secondary_color: brand.secondary_color,
    font_family: brand.font_family || 'Inter',
    voice_and_tone: overrides.voice_and_tone ?? 'Reassuring, plain-language, and efficient. Confident without jargon.',
    visual_style: overrides.visual_style ?? 'Modern civic: generous whitespace, calm surfaces, clear hierarchy, accessible contrast.',
    brand_keywords: overrides.brand_keywords ?? deriveKeywords(brand.display_name, billType),
    reference_url: overrides.reference_url,
    assets: overrides.assets ?? assets,
    layout_intent: overrides.layout_intent,
    enabled_payment_capabilities: definition.enabled_payment_capabilities,
    content,
  };
}

function inferBillType(heading: string): string | undefined {
  const match = heading.toLowerCase().match(/pay your (.+?) bill/);
  return match?.[1];
}

function deriveKeywords(name: string, billType?: string): string[] {
  const keywords = ['trustworthy', 'secure', 'straightforward'];
  if (billType) keywords.push(billType);
  if (/city|county|municipal|utility|water|tax/i.test(`${name} ${billType ?? ''}`)) keywords.push('civic', 'community');
  return keywords;
}
