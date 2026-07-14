// Azure Monitor workspace: the metrics backend for Azure Monitor managed Prometheus.
// No self-hosted Prometheus/Grafana in-cluster — this plus managed Grafana replaces that
// footprint (PVs, storage, upgrades) with managed equivalents, appropriate for a hackathon
// cluster this small.
param name string
param location string

resource workspace 'Microsoft.Monitor/accounts@2023-04-03' = {
  name: name
  location: location
}

output id string = workspace.id
output name string = workspace.name
