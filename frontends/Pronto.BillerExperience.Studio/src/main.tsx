import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import './styles.css';
import './agent-activity.css';
import './handoff-theme.css';

createRoot(document.getElementById('root')!).render(<StrictMode><App /></StrictMode>);
