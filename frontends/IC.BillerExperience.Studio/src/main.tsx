import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { ErrorBoundary } from './ErrorBoundary';
import { logError } from './telemetry';
import './styles.css';
import './agent-activity.css';
import './handoff-theme.css';

window.addEventListener('error', event => logError('studio.window.error', event.error ?? event.message, { source: event.filename, line: event.lineno, column: event.colno }));
window.addEventListener('unhandledrejection', event => logError('studio.window.unhandled_rejection', event.reason));
createRoot(document.getElementById('root')!).render(<StrictMode><ErrorBoundary><App /></ErrorBoundary></StrictMode>);
