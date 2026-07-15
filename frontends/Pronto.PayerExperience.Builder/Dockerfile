# syntax=docker/dockerfile:1
# Build-Job image: assembles the design brief, generates a bespoke skin, builds the PWA
# (tsc + vite), runs the deterministic Playwright gate, and publishes the built bundle to
# Blob Storage. Launched as a short-lived Kubernetes Job by the Deployment Worker.
#
# Based on the Playwright image so Chromium (matching @playwright/test) is preinstalled —
# the validation gate needs a browser and downloading one per Job would be slow/flaky.
# Build context is the repo root (needs both the builder and the PWA it stages).
FROM mcr.microsoft.com/playwright:v1.61.1-noble AS final

WORKDIR /app

# PWA toolchain (vite/tsc/react). The builder stages a copy of the PWA per biller and reuses
# these node_modules, so they must be present in the image.
COPY frontends/Pronto.BillerPayments.Pwa/package.json frontends/Pronto.BillerPayments.Pwa/package-lock.json ./frontends/Pronto.BillerPayments.Pwa/
RUN cd frontends/Pronto.BillerPayments.Pwa && npm ci
COPY frontends/Pronto.BillerPayments.Pwa/ ./frontends/Pronto.BillerPayments.Pwa/

# Builder toolchain: tsx runner, Azure Identity/Blob SDKs, Playwright test runner.
COPY frontends/Pronto.PayerExperience.Builder/package.json frontends/Pronto.PayerExperience.Builder/package-lock.json ./frontends/Pronto.PayerExperience.Builder/
RUN cd frontends/Pronto.PayerExperience.Builder && npm ci
COPY frontends/Pronto.PayerExperience.Builder/ ./frontends/Pronto.PayerExperience.Builder/

# /work is a writable scratch mount (emptyDir) for staged builds, artifacts, and the
# decoded definition; HOME points there too so no writes land outside the mount.
ENV PAYER_WORK=/work/.work \
    PAYER_ARTIFACTS=/work/.artifacts \
    PAYER_PWA_DIR=/app/frontends/Pronto.BillerPayments.Pwa \
    PAYER_CONTAINER=payer-experiences \
    PLAYWRIGHT_BROWSERS_PATH=/ms-playwright \
    HOME=/work
RUN mkdir -p /work && chown -R pwuser:pwuser /work /app

USER pwuser
WORKDIR /app/frontends/Pronto.PayerExperience.Builder
ENTRYPOINT ["./node_modules/.bin/tsx", "src/cli.ts"]
