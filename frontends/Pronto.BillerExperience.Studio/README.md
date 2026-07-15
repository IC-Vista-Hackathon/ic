# Pronto Biller Experience Studio

Chat-first biller onboarding, review, preview, approval, and publication UI.

```powershell
npm install
npm run dev
```

Set `VITE_API_URL` to override the default `http://localhost:5000` API.

## Deployment image flow

The Studio is deployed strictly source → CI image → SHA-pinned rollout, identical to the
PWA and the rest of the platform — no image is ever built or pushed by hand. On a PR (nonprod)
and on merge to `main` (prod), `.github/workflows/build-images.yml` server-side builds this
folder's `Dockerfile` with `az acr build` and pushes it to
`acrichackjk4zmntatjem4.azurecr.io/ic-biller-experience-studio` tagged with the immutable git
SHA (`github.event.pull_request.head.sha` for nonprod, `github.sha` for prod). The deploy jobs
then `sed` the `newTag: latest` placeholder in the environment's `kustomization.yaml` to that
same SHA before `kubectl apply -k`, so the running Deployment always references
`ic-biller-experience-studio:<merged-sha>` — the `:latest` in `overlays/*/*.yaml` is only a
kustomize placeholder and is never deployed. The result is verifiable: the SHA tag on the
running pod resolves to the ACR manifest digest built from that commit, and the static assets
served at `/studio/` (Vite emits content-hashed filenames like `assets/index-<hash>.js`) are
byte-identical to `npm run build` output for the same source.

The original bundled prototype remains in `design/ui.html` as a visual reference. This application
uses its Pronto design tokens while correcting forced enrollment, compliance claims, and the
wizard-first interaction model.
