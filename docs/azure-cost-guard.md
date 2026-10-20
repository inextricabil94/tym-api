# Azure Monthly Cost Guard

## Policy

The approved policy is a combined **EUR 20 per calendar month** across the Azure
subscription, not EUR 20 per app and not bandwidth-only. The existing budget
`all-hosted-apps-monthly-eur10` includes all services without filters. Its email
alerts are at EUR 10, EUR 16, EUR 20 actual cost, and EUR 20 forecast cost.
The budget resource name retains `eur10` for continuity; the configured amount
and workflow threshold are EUR 20. Do not create a duplicate budget based on its
legacy name.

Only the **100% actual-cost** alert is linked to the
`all-hosted-apps-budget-stop` Azure Monitor action group and consumption Logic
App. No Codex scheduler, local process, mail credentials, or LLM is required.

The workflow:

1. Accepts only the matching budget resource ID, actual-cost alert type, and EUR
   currency in Azure Monitor's common alert schema.
2. Queries Azure Cost Management for actual, pre-tax, current-calendar-month
   usage. It validates the response columns, currency, and aggregate row before
   comparing the amount with EUR 20. It does not trust a supplied spending amount.
3. At or above the threshold, discovers Container Apps and App Service sites
   across the subscription and stops those not already stopped.
4. Discovers storage accounts and disables enabled Blob static websites through
   a partial `StaticWebsite` service-property update. Other storage settings and
   blob contents are not deleted.

Inventory requests use pagination up to 100,000 results. Operations use bounded
retries and sequential per-platform resource loops; the three resource types can
be processed independently. Failed requests remain visible as failed Azure
workflow runs. A delayed alert in a later month cannot use the previous month's
spend to trigger a shutdown. Unrelated and forecast alerts make no changes.

**There is no automatic restart.** Apps remain stopped until explicitly started
again. The workflow is alert-triggered, not a continuous lock: a manual restart
or new deployment after shutdown is not guaranteed to trigger another budget
notification in the same month. Obtain approval and review costs before bringing
sites back online.

## Limitations and Continuing Charges

This is a best-effort shutdown, **not a hard EUR 20 payment limit**. Azure's cost
reporting and budget evaluation are delayed; reported costs can overshoot before
the alert is delivered. Taxes and final invoice adjustments are not part of the
pre-tax comparison. At initial setup on 20 October 2026, reported month-to-date
pre-tax usage already exceeded the original EUR 10 limit, so shutdown applied
immediately after approval. Raising the limit to EUR 20 does not restart apps.

Stopping apps does not delete or downgrade their paid infrastructure. In
particular, the existing Basic B1 App Service plan, both Basic container
registries, storage, and retained logs can continue billing. The App Service plan
name includes `free`, but its actual SKU was B1 at setup. Removing these fixed
charges requires a separate approved downgrade or cleanup; this workflow has no
deletion permission. It does not manage VMs, SQL, CDN, or other hosting platforms
that were absent from the verified inventory.

The consumption Logic App has no dedicated hosting plan. Executions, run-history
retention, and storage API transactions follow Azure usage pricing; do not assume
an unconditional zero charge. The workflow is used only for budget events and
explicit tests, not frequent polling.

## Deployment and Verification

The generator is [azure-cost-guard.mjs](../deploy/azure-cost-guard.mjs). The
deployment tool is
[configure-azure-cost-guard.ps1](../tools/configure-azure-cost-guard.ps1), which
requires PowerShell 7, Node.js, Azure CLI, an existing EUR 20 monthly subscription
budget, and permission to configure resources and assign the custom role:

```powershell
./tools/configure-azure-cost-guard.ps1 `
  -SubscriptionId '<subscription-id>' `
  -NodePath '<absolute-path-to-node.exe>' `
  -Test
```

The script creates the workflow disabled, assigns its system-managed identity a
custom role, enables the workflow, creates the action group, and links the actual
100% notification while retaining other alerts and recipients. It uses stable
role identifiers and the budget eTag. No Owner/Contributor role is assigned to
the workflow. Permissions are limited to cost queries, app discovery/stopping,
storage discovery, and Blob service-property read/write. There are no start,
delete, key-listing, blob-content, or role-management grants.

Signed workflow callback URLs stay in memory and the Azure action group. Never
print them, add them to source control, or paste workflow run input/output URLs
into public documentation. `-Test` submits a matching notification, waits for the
final run status, and still queries real costs; it cannot override the spending
threshold. Verify resource states as well, not just the HTTP 202 response.

Run the local configuration checks with:

```powershell
node --test tools/tests/azure-cost-guard.test.mjs
```

These are configuration checks, not simulations of Azure billing delivery. A
live workflow test and a saved-budget read-back are separate verification steps.

### Original EUR 10 Setup: 20 October 2026

- Six local configuration tests passed, including unique nested action names.
- Azure accepted and enabled the workflow and its restricted managed identity.
- The saved actual 100% budget notification points to the action group; forecast
  and lower thresholds retain email-only behavior.
- A live matching notification succeeded, queried real current-month EUR cost,
  took the over-limit branch, and completed all three resource inventory loops.
- Existing apps and static websites had already been shut down with owner
  approval. The live workflow correctly skipped their redundant stop/disable
  operations; this test did not start any app to exercise a shutdown.
- A separate rejection test covers forecast, wrong-currency, and unrelated-budget
  notifications. See Azure run history for each final status.
- All six Container Apps and both App Service sites were confirmed stopped. The
  TYM static site, container UI, and API health URLs returned HTTP 404. Static
  hosting was disabled and the preserved `$web` container remained private.

Azure's eventual delivery of a real billing notification is not simulated or
guaranteed by these tests. Alerts can be delayed, and future permissions, hosting
types, subscription changes, or resource policies can affect execution.

### EUR 20 Update: 20 October 2026

- The owner raised the monthly policy from EUR 10 to EUR 20. Both the saved Azure
  budget amount and the enabled workflow's parameter/default were read back as
  EUR 20. The managed identity and existing resource IDs were preserved.
- Existing percentage thresholds now notify at EUR 10, EUR 16, and EUR 20 actual
  cost, plus EUR 20 forecast cost. Recipients were retained; only the EUR 20
  actual-cost notification remains connected to automatic shutdown.
- The live test queried real current-month cost, succeeded on the below-limit
  branch, and skipped shutdown actions. No artificial cost override was used.
- Six local configuration tests and the deployment script's syntax check passed.
- All six Container Apps and both App Service sites were still stopped. This
  threshold update did not start apps or re-enable the static website.

### Approved TYM Restart: 20 October 2026

After the owner's explicit restart approval, reported current-month EUR cost was
confirmed below EUR 20. Only `tym-api-serban`, `tym-ui-serban` and the
`tymui20260707serban` static website were restored. The existing published images
and website files were reused; no image build, new resource, or paid plan was
needed. The four other Container Apps and both App Service sites remain stopped.

- Static UI, container UI and API health returned HTTP 200.
- Published index, application script, styles, translations and both flag SVGs
  matched the local release by SHA-256. API CORS allowed the website and the
  catalog contained 16 books, without reading or exporting their prose.
- An authored sample generated a nonempty SVG with six segments and six tracks.
  Two diagram requests were used in verification: the initial response check
  incorrectly expected a top-level SVG field, then the corrected check used the
  documented `render.svg` field. No application-code change was needed.
- The EUR 20 budget, enabled workflow, enabled action group and actual-cost
  notification link were verified again after restart. They were not disabled
  or bypassed to restore service.

## Recovery

Do not delete and recreate applications. Their stopped resources retain their
images, identity, settings, and stored files. Following explicit approval, use
the Azure portal's Start action for the intended Container Apps or App Service
sites. For `tymui20260707serban`, re-enable the static website with
`index.html` as both index and error document; disabling the feature clears those
service-property labels but preserves all files in the private `$web` container.

To disable automatic shutdown while preserving email alerts, first remove this
action group from the budget's actual 100% notification, then disable the Logic
App. Do not remove the entire budget or modify unrelated alerts. To keep the
policy, leave the action group and workflow enabled even while application sites
are stopped. Review costs before any restart, particularly within an already
over-budget month.

## References

- [Azure spending limits: no custom hard spending limit](https://learn.microsoft.com/en-us/azure/cost-management-billing/manage/spending-limit)
- [Budget notifications and evaluation delays](https://learn.microsoft.com/en-us/azure/cost-management-billing/costs/manage-automation)
- [Budget-triggered action groups](https://learn.microsoft.com/en-us/azure/cost-management-billing/manage/cost-management-budget-scenario)
- [Common-schema budget alert payload](https://learn.microsoft.com/en-us/azure/azure-monitor/alerts/alerts-payload-samples)
- [Stopping Azure Container Apps](https://learn.microsoft.com/en-us/rest/api/resource-manager/containerapps/container-apps/stop?view=rest-resource-manager-containerapps-2025-07-01)
- [Blob service-property updates and managed identity permissions](https://learn.microsoft.com/en-us/rest/api/storageservices/set-blob-service-properties)
- [App Service billing](https://learn.microsoft.com/en-us/azure/app-service/overview-manage-costs)
- [Logic Apps usage pricing](https://azure.microsoft.com/en-us/pricing/details/logic-apps/)
