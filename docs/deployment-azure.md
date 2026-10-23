# Azure Deployment

The existing deployment uses resource group `tym-llm-prod-rg`, registry `ca0e9fe87ad1acr`, API app `tym-api-serban`, UI app `tym-ui-serban`, and the static UI storage account `tymui20260707serban`.

**Cost shutdown policy (20 October 2026):** the owner approved taking all hosted
apps offline at EUR 20 combined monthly subscription cost (raised from EUR 10).
Raising the threshold does not automatically restart sites. Following explicit
restart approval and a below-budget cost check, the TYM API, container UI and
static website were restored on 20 October 2026. The four other Container Apps
and both App Service sites remain stopped. The EUR 20 budget/action-group/workflow
connection remains enabled. Review the [monthly cost guard](azure-cost-guard.md)
before further deployments or restarts. Fixed infrastructure charges can continue.

## Build and Verify

```powershell
dotnet build Tym.Api.sln -c Release
dotnet run --project tools/Tym.GenerationChecks -c Release
node --test tools/tests/chapter-splitter.test.cjs tools/tests/track-groups.test.cjs tools/tests/density-cache.test.cjs
```

Generation/POS checks require the local spaCy setup in [the POS guide](plans/pos-filtering.md). The root Dockerfile installs Python, spaCy and both official English/Romanian models, then verifies that each loads. The UI has its own Dockerfile under `ui/Tym.Ui`. Azure Container Registry Tasks can build both images without a local Docker daemon. Use unique tags and inspect build status before updating live apps.

For an API remote build, use the explicit root `.dockerignore` allowlist: public project/runtime files, root API modules (including `Accounts.cs`), `openapi.yaml`, authored samples, third-party notices and POS worker requirements/script. No books, local virtual environments, private settings, screenshots or test outputs are needed. Existing private book-library layers can be copied within the registry instead of uploading the sources again. The UI build context is `ui/Tym.Ui`, with build output ignored.

## Release

Update the API and container UI image tags through `az containerapp update`, preserving their ingress, identities, registry credentials and API URL settings. Set Production explicitly. Keep previous image/revision identifiers for rollback. The in-memory five-per-IP daily diagram quota requires a single API replica for process-wide enforcement; restarts reset counts. Distributed enforcement is a separate improvement.

For the static UI, copy only `ui/Tym.Ui/wwwroot` into a clean staging directory and generate a production `config.js` containing the public API URL and an empty `examples` array. Upload that directory to the existing `$web` container with Azure CLI, not the full repository or local publish folders. Use no-cache response metadata for HTML/JS/CSS so changes are revalidated. Local private examples are never deployed; the container UI's Production environment also refuses its Development/loopback-only text route.

Verify both public UI URLs, API health, actual English/Romanian POS lookup, a generated SVG/JSON response, chapter controls, theme/fonts, and absence of the private catalog. Cloud tests use authored text, not private book inputs. Record the deployed image tags/revisions and pushed Git commit together in release notes.

Rollback uses the previous known-good image with `az containerapp update --image <previous-image>`. Keep the static site's prior public files available in a local deployment backup if rollback is required. Do not expose private sources to make cloud/local catalogs identical.

## Latest Release: 23 October 2026

Both public UI hosts now include the bounded IndexedDB diagram/catalog cache,
offline application shell, self-hosted React and horizontally aligned Saved
diagrams / Cached TT density controls. The API image is tagged
`release-20261023-offline` (revision `r20261023offline`); the final UI image is
`release-20261023-offline2` (revision `r20261023offline2`).
The API cache implementation is deployed but production SQLite remains disabled:
no persistent cache volume exists and `TYM_DIAGRAM_CACHE_PATH` is unset. Do not
enable it on ephemeral container storage. No database, volume or paid service was
provisioned. See [cache behavior and limits](offline-caching.md) and
[release checks](verification/offline-20261023.md).

The new [improvements document](research/TYM_Improvements_20261023.md) is published
as a PDF under the static website's `research/` path. Previous research files,
the sixteen-book server library, public reading, disabled accounts and the
EUR 20 shutdown policy are preserved. Other hosted apps remain stopped.

## Release: 21 October 2026

The API and both UI hosts now serve selectable rule/logistic/perceptron
segmentation, marked source ranges and aligned language/account/theme controls.
The API remains on `release-20261021-segmentation`, revision `r20261021seg`.
The latest UI-only update uses `release-20261021-header`, revision
`r20261021header`, with bottom-aligned controls and status above the buttons.
See the [header release checks](verification/header-20261021.md).
The existing server-only library, resource limits, public reading,
disabled accounts and enabled EUR 20 cost guard are unchanged. See the
[release verification](verification/segmentation-20261021.md) for build IDs,
live checks and seed-model limitations. The dated releases below are historical.

## Release: 19 October 2026

- API image: `ca0e9fe87ad1acr.azurecr.io/tym-api-serban:release-20261019-density2`; revision `tym-api-serban--r20261019density2`.
- Container UI image: `ca0e9fe87ad1acr.azurecr.io/tym-ui-serban:release-20261019-density2`; revision `tym-ui-serban--r20261019density`.
- Static UI: public assets and empty-catalog configuration uploaded to the existing `$web` container with `no-cache` metadata. Asset URL version: `20261019-density`.
- Existing CPU/memory settings retained. API maximum replicas set to one for its in-memory daily quota. No other prototype applications were updated.
- Live checks passed on both public UIs at 390/1440 pixels: English/Romanian POS models, eleven cached density options, no requests when switching, rendered SVG, selected-density JSON export and self-hosted fonts. The Production private-text route returned 404; both configurations exposed zero private examples.
- Two authored diagram requests were used for live verification; no private book was uploaded. Local tests additionally verified all cached SVG/JSON/XML exports and the one-permit cache bundle.
- The OpenAPI file is explicitly included in publish output. Fresh image installation uses the tested Click/Typer pins as well as spaCy/model pins. Build runs for the final images: API `cah`, UI `cag`.

The public endpoints are [static UI](https://tymui20260707serban.z13.web.core.windows.net), [container UI](https://tym-ui-serban.livelyrock-2726c024.eastus.azurecontainerapps.io), and [API](https://tym-api-serban.livelyrock-2726c024.eastus.azurecontainerapps.io). Previous public static files are backed up locally under the ignored deployment staging directory. Verification details are processing/regression evidence, not NLP accuracy scores.

## Release: 20 October 2026

The API and both UI hosts now serve public full/chapter reading and numbered chapter labels. API/UI image tags are `release-20261020-publictext`; both ready revisions end in `r20261020publictext`. See the [release verification](verification/release-20261020.md) for exact builds and live tests. The older 19 October details above are historical.

Live accounts remain disabled: Azure rejected the approved free-limit SQL server in three regions. Do not enable accounts against container-local SQLite. The implemented SQL provider uses managed identity and persistent SQL token keys; initialize and test a real free-limit database plus email delivery before activation. No billed-overage database or paid mail provider was substituted. The local preview still uses its separate private SQLite and SMTP configuration.
