import { Component, type ErrorInfo, type ReactNode } from 'react';
import { logError } from './telemetry';
export class ErrorBoundary extends Component<{ children: ReactNode }, { error?: Error }> {
  state: { error?: Error } = {}; static getDerivedStateFromError(error: Error) { return { error }; }
  componentDidCatch(error: Error, info: ErrorInfo) { logError('pwa.render.failed', error, { component_stack: info.componentStack }); }
  render() { return this.state.error ? <main className="center fatal-error" role="alert"><section className="card"><h1>We couldn’t display this payment page</h1><p>Your payment has not been submitted. Reload the page to try again.</p><button onClick={() => window.location.reload()}>Reload payment page</button></section></main> : this.props.children; }
}
