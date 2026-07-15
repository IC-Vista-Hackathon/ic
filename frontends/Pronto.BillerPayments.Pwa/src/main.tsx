import { StrictMode } from 'react'; import { createRoot } from 'react-dom/client'; import { App } from './App'; import { initBrowserTelemetry, trackEvent } from './insights'; import './styles.css';
if ('serviceWorker' in navigator) navigator.serviceWorker.register(`${import.meta.env.BASE_URL}sw.js`).catch(error => console.error(JSON.stringify({ level:'error', event:'pwa.service_worker.registration_failed', message:String(error) })));
trackEvent('pwa.session_started'); // queued until init resolves; dropped when telemetry is unconfigured
void initBrowserTelemetry();
createRoot(document.getElementById('root')!).render(<StrictMode><App /></StrictMode>);
