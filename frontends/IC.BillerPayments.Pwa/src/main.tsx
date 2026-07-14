import { StrictMode } from 'react'; import { createRoot } from 'react-dom/client'; import { App } from './App'; import './styles.css';
if ('serviceWorker' in navigator) navigator.serviceWorker.register(`${import.meta.env.BASE_URL}sw.js`).catch(error => console.error(JSON.stringify({ level:'error', event:'pwa.service_worker.registration_failed', message:String(error) })));
createRoot(document.getElementById('root')!).render(<StrictMode><App /></StrictMode>);
