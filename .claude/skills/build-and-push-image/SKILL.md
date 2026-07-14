---
name: build-and-push-image
description: Build a container image for an Pronto service/frontend and push it to the hackathon ACR (acrichackjk4zmntatjem4.azurecr.io). Use when asked to build, containerize, or push an image for one of this repo's services, or to get an image into the registry the AKS cluster pulls from.
---

# Build and push image

Builds a container image and pushes it to the hackathon sandbox's ACR: registry
`acrichackjk4zmntatjem4.azurecr.io`, resource group `rg-ic-hack`, subscription
`poc-vista-hackathon` (`ca64adec-b195-49fd-a782-15553708c07c`).

## Prerequisite check — do this first

**This repo does not have a `Dockerfile` for any service yet** (checked
`services/Pronto.BillerExperience.Api`, `services/Pronto.BillerExperience.Worker`,
`frontends/Pronto.BillerExperience.Studio`, `frontends/Pronto.BillerPayments.Pwa` as of this writing —
none exist). Before attempting a build:

```sh
find <service-or-frontend-dir> -iname Dockerfile
```

If none exists, **stop and say so explicitly** rather than silently failing partway through a
build, or improvising a Dockerfile without checking with the user first — there's no established
base-image/build convention in this repo yet to follow.

## Path A — local Docker available

```sh
docker build -t <image-name>:<tag> -f <path-to-Dockerfile> <build-context>

az account set --subscription ca64adec-b195-49fd-a782-15553708c07c
az acr login --name acrichackjk4zmntatjem4

docker tag <image-name>:<tag> acrichackjk4zmntatjem4.azurecr.io/<image-name>:<tag>
docker push acrichackjk4zmntatjem4.azurecr.io/<image-name>:<tag>
```

## Path B — no local Docker (ACR Tasks remote build)

Builds happen server-side in Azure, no local Docker daemon needed:

```sh
az account set --subscription ca64adec-b195-49fd-a782-15553708c07c
az acr build --registry acrichackjk4zmntatjem4 \
  --image <image-name>:<tag> \
  <build-context>
```

`<build-context>` is a local path or a git URL; `az acr build` uploads the context (or clones
the repo) and runs the build in ACR Tasks, streaming logs back. Prefer this path in any sandboxed
environment where Docker isn't installed or the daemon isn't reachable.

## Notes

- AKS (`aks-ic-hack`) already has `AcrPull` on this registry (granted in
  `infra/bicep/modules/aks.bicep`) — no extra pull-secret plumbing needed once an image is
  pushed.
- Tag with something traceable (git SHA or version), not just `latest` — the AKS publication
  model documented in the root `README.md` calls for immutable image digests, not floating tags.
- Image/tag naming isn't established yet either; a reasonable default is the service's runtime
  name from the root `README.md`'s component table (e.g. `ic-biller-experience-api`,
  `ic-biller-experience-worker`) — confirm with the user if this matters for their use case.
