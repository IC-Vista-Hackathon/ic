import type { FooterProps, HeaderProps, IntroProps } from './contract';

// AI-EDITABLE SKIN CHROME.
// Presentational only. No hooks with side effects, no fetch, no payment logic.
// Keep the exported component names and their prop contracts (contract.ts) intact;
// the stable core composes these and the build/typecheck gate enforces the signatures.

export function Header({ brand }: HeaderProps) {
  return (
    <header data-testid="app-header" style={{ background: brand.primary_color || 'var(--brand)' }}>
      <div className="mark">{initials(brand.display_name)}</div>
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
