import type { FooterProps, HeaderProps, IntroProps } from './contract';

// AI-EDITABLE SKIN CHROME.
// Presentational only. No hooks with side effects, no fetch, no payment logic.
// Keep the exported component names and their prop contracts (contract.ts) intact;
// the stable core composes these and the build/typecheck gate enforces the signatures.

export function Header({ brand }: HeaderProps) {
  const logo = brand.logo_asset_id?.trim();
  return (
    <header data-testid="app-header" style={{ background: brand.primary_color || 'var(--brand)' }}>
      <div className="mark">
        {logo
          ? <>
            <span aria-hidden="true" style={{ gridArea: '1 / 1' }}>{initials(brand.display_name)}</span>
            <img
              src={logo}
              alt={`${brand.display_name} logo`}
              style={{ display: 'block', width: '100%', height: '100%', objectFit: 'contain', borderRadius: 'inherit', gridArea: '1 / 1' }}
              onError={event => { event.currentTarget.style.display = 'none'; }}
            />
          </>
          : initials(brand.display_name)}
      </div>
      <strong>{brand.display_name}</strong>
      <span>Secure account services</span>
    </header>
  );
}

export function Intro({ eyebrow, heading, subheading }: IntroProps) {
  return (
    <div className="intro">
      <p>{eyebrow}</p>
      <h1>{heading}</h1>
      <span>{subheading}</span>
    </div>
  );
}

export function Footer({ brand, content }: FooterProps) {
  return (
    <footer>
      <span>{content.support_text}</span>
      <nav>
        <a href={content.privacy_policy_url}>Privacy</a>
        <a href={content.terms_of_service_url}>Terms</a>
      </nav>
    </footer>
  );
}

function initials(name: string) {
  return name
    .split(' ')
    .map(word => word[0])
    .slice(0, 2)
    .join('');
}
