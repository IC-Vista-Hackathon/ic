using Xunit;

// These tests observe process-global ActivitySource/ActivityListener state (e.g. publication.*
// spans). Running test classes in parallel lets one class's global listener capture activities
// started by another, so parallelization is disabled to keep telemetry assertions isolated.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
