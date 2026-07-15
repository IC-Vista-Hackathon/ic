import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { ErrorBoundary } from './ErrorBoundary';
import { initBrowserTelemetry, trackEvent } from './insights';
import { logError } from './telemetry';
import './design-tokens.css';
import './theme.css';

window.addEventListener('error', event => logError('studio.window.error', event.error ?? event.message, { source: event.filename, line: event.lineno, column: event.colno }));
window.addEventListener('unhandledrejection', event => logError('studio.window.unhandled_rejection', event.reason));
trackEvent('studio.session_started'); // queued until init resolves; dropped when telemetry is unconfigured
void initBrowserTelemetry();
createRoot(document.getElementById('root')!).render(<StrictMode><ErrorBoundary><App /></ErrorBoundary></StrictMode>);
