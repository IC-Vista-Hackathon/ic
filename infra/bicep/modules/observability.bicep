// Observability alerts for the hackathon sandbox: an action group plus three log-search alert
// rules over the workspace-based Application Insights resource (appi-ic-hack). Queries follow the
// repo's snake_case customEvents conventions (see frontends/Pronto.BillerPayments.Pwa/README.md
// "Browser observability") and the worker's ic.biller.publication.* metrics. Grafana dashboards
// are checked in as JSON under infra/grafana (Managed Grafana has no ARM resource for dashboard
// content); this module owns only the alerting that Grafana can't express.
//
// The rules are scoped to the Application Insights component, so they use the classic App Insights
// table names (customEvents, requests, customMetrics) rather than the Log Analytics App* names.
param location string
param appInsightsId string

@description('Optional email address to notify. Leave empty to create the action group with no receivers (add them later).')
param alertEmailAddress string = ''

@description('pwa.payment_failed count over the 15m window above which the payment-failure spike alert fires.')
param paymentFailedThreshold int = 5

@description('Lookback window, in hours, for the telemetry-silence alert.')
param telemetrySilenceLookbackHours int = 1

@description('Minimum non-health request count over the lookback window before the telemetry-silence alert can fire. Guards against firing on health-probe-only traffic during genuine off-hours.')
param telemetrySilenceMinRequests int = 20

var emailReceivers = empty(alertEmailAddress)
  ? []
  : [
      {
        name: 'primaryEmail'
        emailAddress: alertEmailAddress
        useCommonAlertSchema: true
      }
    ]

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'ag-ic-hack-observability'
  location: 'global'
  properties: {
    groupShortName: 'ic-obsv'
    enabled: true
    emailReceivers: emailReceivers
  }
}

// pwa.payment_failed spike: too many payer payment failures in the evaluation window.
resource paymentFailedSpike 'Microsoft.Insights/scheduledQueryRules@2023-12-01' = {
  name: 'ic-hack-pwa-payment-failed-spike'
  location: location
  properties: {
    displayName: 'PWA payment_failed spike'
    description: 'Fires when pwa.payment_failed customEvents exceed the configured threshold over 15 minutes.'
    severity: 2
    enabled: true
    scopes: [appInsightsId]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    autoMitigate: true
    criteria: {
      allOf: [
        {
          query: 'customEvents\n| where name == "pwa.payment_failed"'
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: paymentFailedThreshold
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [actionGroup.id]
    }
  }
}

// Publish failures > 0 in 15m: any worker publication that ended with outcome "failed".
resource publishFailures 'Microsoft.Insights/scheduledQueryRules@2023-12-01' = {
  name: 'ic-hack-publish-failures'
  location: location
  properties: {
    displayName: 'Publish pipeline failures'
    description: 'Fires when the publication worker records any ic.biller.publication.results with outcome "failed" in 15 minutes.'
    severity: 1
    enabled: true
    scopes: [appInsightsId]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    autoMitigate: true
    criteria: {
      allOf: [
        {
          query: 'customMetrics\n| where name == "ic.biller.publication.results"\n| where tostring(customDimensions.outcome) == "failed"'
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [actionGroup.id]
    }
  }
}

// Server-side payment failure > 0 in 15m: the Payment API recorded a finalized payment with
// lifecycle "failed". This is the authoritative money-movement signal (the PWA payment_failed
// event above is browser-side and can miss failures the client never observes).
resource paymentFinalizedFailures 'Microsoft.Insights/scheduledQueryRules@2023-12-01' = {
  name: 'ic-hack-payment-finalized-failures'
  location: location
  properties: {
    displayName: 'Payment finalized failures (server-side)'
    description: 'Fires when the Payment API records any ic.payment.finalized with lifecycle "failed" in 15 minutes.'
    severity: 1
    enabled: true
    scopes: [appInsightsId]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    autoMitigate: true
    criteria: {
      allOf: [
        {
          query: 'customMetrics\n| where name == "ic.payment.finalized"\n| where tostring(customDimensions.lifecycle) == "failed"'
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [actionGroup.id]
    }
  }
}

// Telemetry silence: server requests are flowing but no PWA customEvents have arrived for an hour,
// which usually means the browser telemetry pipeline (config fetch, SDK bootstrap, or ingestion)
// is broken rather than genuine idleness.
resource telemetrySilence 'Microsoft.Insights/scheduledQueryRules@2023-12-01' = {
  name: 'ic-hack-telemetry-silence'
  location: location
  properties: {
    displayName: 'PWA telemetry silence'
    description: 'Fires when requests are flowing but zero customEvents have arrived over the lookback window (browser telemetry pipeline likely broken).'
    severity: 1
    enabled: true
    scopes: [appInsightsId]
    evaluationFrequency: 'PT15M'
    windowSize: 'PT${telemetrySilenceLookbackHours}H'
    autoMitigate: true
    criteria: {
      allOf: [
        {
          query: 'let lookback = ${telemetrySilenceLookbackHours}h;\nlet event_count = toscalar(customEvents | where timestamp > ago(lookback) | count);\nlet request_count = toscalar(requests | where timestamp > ago(lookback) | where url !has "/health/" and name !has "health" | count);\nprint silent = iff(request_count >= ${telemetrySilenceMinRequests} and event_count == 0, 1, 0)\n| where silent == 1'
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [actionGroup.id]
    }
  }
}

output actionGroupId string = actionGroup.id
output paymentFailedSpikeRuleId string = paymentFailedSpike.id
output publishFailuresRuleId string = publishFailures.id
output paymentFinalizedFailuresRuleId string = paymentFinalizedFailures.id
output telemetrySilenceRuleId string = telemetrySilence.id
