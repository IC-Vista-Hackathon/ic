import { StrictMode } from 'react'; import { createRoot } from 'react-dom/client'; import { App } from './App'; import { ErrorBoundary } from './ErrorBoundary'; import { initBrowserTelemetry, trackEvent } from './insights'; import { logError } from './telemetry'; import './skin/theme.css';
if ('serviceWorker' in navigator) navigator.serviceWorker.register(`${import.meta.env.BASE_URL}sw.js`).catch(error => console.error(JSON.stringify({ level:'error', event:'pwa.service_worker.registration_failed', message:String(error) })));
window.addEventListener('error', event => logError('pwa.window.error', event.error ?? event.message, { source: event.filename, line: event.lineno, column: event.colno }));
window.addEventListener('unhandledrejection', event => logError('pwa.window.unhandled_rejection', event.reason));
trackEvent('pwa.session_started'); // queued until init resolves; dropped when telemetry is unconfigured
void initBrowserTelemetry();
createRoot(document.getElementById('root')!).render(<StrictMode><ErrorBoundary><App /></ErrorBoundary></StrictMode>);
