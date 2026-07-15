import { Component, type ErrorInfo, type ReactNode } from 'react';
import { logError } from './telemetry';

export class ErrorBoundary extends Component<{ children: ReactNode }, { error?: Error }> {
  state: { error?: Error } = {};
  static getDerivedStateFromError(error: Error) { return { error }; }
  componentDidCatch(error: Error, info: ErrorInfo) { logError('studio.render.failed', error, { component_stack: info.componentStack }); }
  render() {
    if (!this.state.error) return this.props.children;
    return <main className="fatal-error" role="alert"><section><h1>Studio hit an unexpected error</h1><p>Your saved biller experience has not been removed. Reload Studio to continue.</p><button onClick={() => window.location.reload()}>Reload Studio</button></section></main>;
  }
}
