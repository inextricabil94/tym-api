# Google and Email Accounts

TYM uses ASP.NET Core Identity for email/password accounts and Google Identity Services for Google sign-in. Firebase is not used. At the user's latest request, chapter and full-text reading is **public for all user types by default**, including guests and deployments with accounts disabled. Set `TYM_REQUIRE_LOGIN_FOR_TEXT=true` to restore confirmed-account-only source reading. Omit `sectionId` on `/v1/books/{id}/source` to read the complete selected book. In restricted mode, missing, expired, forged and revoked sessions cannot read source text. The account endpoints remain protected independently of this setting.

## Required Configuration

Accounts are disabled by default. Set these API environment variables before enabling them:

| Variable | Purpose |
| --- | --- |
| `TYM_AUTH_ENABLED` | `true` to enable accounts. |
| `TYM_REQUIRE_LOGIN_FOR_TEXT` | `false` by default: guest and signed-in reading. `true` restores confirmed-account-only reading. |
| `TYM_AUTH_DATA_PATH` | Persistent private directory for local SQLite `accounts.db` and `keys/`. Required for production SQLite; not used by SQL deployments. |
| `TYM_AUTH_SQL_CONNECTION` | Optional Azure SQL connection string. Production requires Active Directory Managed Identity authentication, encryption and certificate validation. Identity records and Data Protection keys use the same private database. |
| `TYM_PUBLIC_API_URL` | Canonical HTTPS API origin, used in confirmation emails. |
| `TYM_PUBLIC_UI_URL` | Exact HTTPS UI origin; also the account/source CORS allowlist and password-reset destination. |
| `TYM_AUTH_ALLOWED_ORIGINS` | Optional comma-separated additional HTTPS UI origins, for example the container UI hostname. No wildcards. |
| `TYM_GOOGLE_CLIENT_ID` | Google OAuth **Web application** client ID. Optional; enables Google registration/sign-in. Not a client secret. |
| `TYM_SMTP_HOST` / `TYM_SMTP_PORT` | SMTP host and port (587 with mandatory STARTTLS by default; 465 uses TLS). |
| `TYM_SMTP_FROM` | Verified sender address accepted by the mail provider. |
| `TYM_SMTP_USER` / `TYM_SMTP_PASSWORD` | SMTP credentials, stored as Azure secrets, never in Git or frontend configuration. |

The frontend obtains non-secret availability/client-ID settings, `text_requires_login` and `email_delivery` from public `/v1/account/config`; no rebuild-time auth secret is needed. Delivery is `local_test`, `smtp` or `unavailable`. The same public API URL must be used by the static and container UI.

In Development only, `TYM_MAIL_PICKUP_PATH` can point to a private folder for `.eml` test messages instead of a real mail sender. It is ignored in production. **No message is sent to the user's inbox in this mode.** Registration/recovery notices explicitly identify local test delivery. Preview accounts are real local Identity accounts, not mocked authentication; confirmation links must still be followed. Do not expose the pickup directory through static hosting.

## Provider Setup

1. In the existing Google Cloud project, configure the OAuth consent screen and a Web application client. Add the exact live UI origin as an authorized JavaScript origin. Add `http://127.0.0.1:8780` separately for local testing. Publish/verify the consent screen as required by Google. TYM uses popup ID-token sign-in, not Gmail inbox scopes.
2. Set `TYM_GOOGLE_CLIENT_ID` on the API. No Google client secret is required for this ID-token flow. The server uses Google's official .NET verifier, checks the client audience and a single-use, five-minute nonce, and associates users by Google subject. Only verified Gmail/Workspace accounts use this path; other addresses can register by email.
3. Configure the SMTP sender for confirmation and recovery. Email passwords require at least 12 characters plus Identity's uppercase, lowercase, number and symbol rules. Accounts cannot sign in until email confirmation succeeds. Five failed logins lock the account for 15 minutes.
4. Use a managed database in Azure, or mount a persistent **private** data directory for a single-host SQLite deployment. The database and Data Protection keys must survive revisions/restarts. Do not put either in the container image, GitHub, `$web` storage, or a public blob. Restrict access and encrypt/back up the store. Do not share SQLite over a network filesystem for multi-replica writes. SQL setup is described below; Google nonce replay protection and IP quotas still require the existing single-replica deployment or a distributed store before scaling.
5. Verify Google sign-in and delivery to a real email inbox using the real configured project/sender before claiming the live feature is enabled. These cannot be fully tested without the user's provider configuration.

## Security and Reading Behavior

- Identity bearer access tokens expire after 15 minutes; refresh tokens after eight hours. Tokens stay in browser memory, not localStorage, cookies or URLs. Reloading the page requires sign-in again.
- Sign-out updates the account's security stamp (all sessions). `/me` and source reads in restricted mode validate the current stamp against the database. Password reset also invalidates prior sessions. Public-mode reading remains available after sign-out, as it is for every guest.
- Google accounts are never silently merged with existing email accounts. An email collision instructs the user to sign in with the existing method; account linking is not implemented.
- Account routes allow 20 requests per IP per five minutes; source reads allow 60 per minute. Diagram generation retains the existing shared five-per-day quota.
- Source responses and auth responses are `no-store`. Raw source is never put in the catalog, density cache configuration or frontend static assets. Public mode exposes it through the anonymous source endpoint; restricted mode requires authorization there. The reader clears when changing sources, or losing authorization in restricted mode; stale chapter responses are aborted/ignored.
- No source download/copy control is provided. Any text displayed in a browser can still be copied using browser tools; login is access control, not DRM. Analysis outputs can contain excerpts, so public diagram analysis is not a confidentiality boundary for books.
- Do not log passwords, credentials, access tokens, confirmation/reset tokens or source bodies. Review proxy/access-log query handling for the confirmation route and use short retention.

## Local Verification

`tools/tests/accounts.browser.mjs` creates an isolated authored book library, SQLite store and mail pickup folder. Its default verifies restricted reading; set `TYM_TEST_PUBLIC_READING=true` to verify guest/signed-in/post-logout reading in public mode. Both test account verification/revocation separately. `book-library.browser.mjs` also verifies public reading with accounts disabled. No real Google account or SMTP delivery is claimed by those tests.

References: [Microsoft Identity API endpoints](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-9.0), [Google server-side verification](https://developers.google.com/identity/gsi/web/guides/verify-google-id-token), [Google client setup](https://developers.google.com/identity/gsi/web/guides/get-google-api-clientid).

## Azure SMTP Provider Decision (19 October 2026)

The user chose a longer-term SMTP provider instead of Azure Communication Services Email after reviewing Microsoft's announcement that ACS Email retires on 30 September 2028, with onboarding restrictions beginning 23 October 2026. No ACS email resource, domain, app registration, SMTP credential or role assignment was created. Only the subscription's Communication resource-provider namespace was registered during capability checks; that is not an email sender.

SendGrid is available through Azure Marketplace, but requires provider onboarding and a verified sender/domain in addition to Azure billing credentials. There is no existing SendGrid/SaaS provider resource in the inspected subscription. The Romanian catalog's smallest listed plan was Essentials 50K at EUR 18.6318/month before taxes, auto-renewing, with EUR 0.00124/email overage above 50,000/month and no Azure-plan free trial. Checkout/billing-market pricing must be reconfirmed before purchase. The user explicitly declined purchasing it. No Marketplace subscription or paid plan was created. A proposed verification contact is retained only in ignored private planning metadata; it is not a verified sending identity or a configured SMTP account.

The generic TLS SMTP integration above is ready for the chosen provider. Email remains inactive in production until provider onboarding, sender verification and credentials are complete. Use Azure Container App secrets for its SMTP password/API key, not frontend configuration, Git or an Azure user's login password. A Gmail contact address does not establish ownership of the `gmail.com` domain for domain authentication; use provider-supported sender verification and prefer an owned sending domain for production.

References: [ACS retirement notice](https://learn.microsoft.com/en-us/azure/communication-services/acs-retirement-and-breaking-changes-guide), [SendGrid Azure onboarding](https://www.twilio.com/docs/sendgrid/for-developers/partners/microsoft-azure), [SendGrid sender authentication](https://www.twilio.com/docs/sendgrid/ui/sending-email/sender-verification).

## Direct Gmail SMTP for the Prototype

The existing Gmail mailbox can send prototype registration/confirmation mail directly, without buying a SendGrid subscription. Address verification alone is not an SMTP credential. Google recommends OAuth; where App Passwords are available, a dedicated App Password requires 2-Step Verification. Some protected/managed accounts cannot create them. Never use the normal Google account password or put an App Password in chat.

1. Enable 2-Step Verification and create a dedicated TYM App Password in Google Account settings.
2. Run `./tools/configure-gmail-smtp.ps1` in PowerShell. It uses the sender contact from ignored private metadata, or asks for the Gmail address, and prompts privately for the App Password. Copied spaces, tabs, line breaks, common invisible formatting marks and one pair of surrounding straight quotes are removed. Invalid input can be retried up to three times without saving settings; diagnostics report only length, never the input. Use Google's generated 16-character App Password, not a regular password or six-digit verification code. It configures `smtp.gmail.com:587` with mandatory STARTTLS, with the authenticated mailbox as the sender.
3. The helper saves settings via stdin into .NET User Secrets outside the repository. This is a development-only store and is **not encrypted**. It does not purchase anything, send mail, activate production or expose credentials in process arguments.
4. Restart the Development API without a `TYM_MAIL_PICKUP_PATH` environment override. Existing accounts need not be recreated: use Resend confirmation. Verify `email_delivery: smtp` and a real inbox delivery before calling SMTP operational. Local test-mail mode always wins when explicitly configured.
5. For Azure, put the App Password in a Container App secret/Key Vault and use a secret reference for `TYM_SMTP_PASSWORD`. Do not deploy the development secret store. Use a dedicated transactional provider or OAuth-based integration as production scale/security requirements grow.

References: [Google App Password requirements](https://support.google.com/accounts/answer/185833?hl=en), [Gmail SMTP settings](https://developers.google.com/workspace/gmail/imap/imap-smtp), [Microsoft development-secret guidance](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-9.0).

## Azure SQL Free-Limit Account Store

The user approved the free-limit database on 20 October 2026. Azure rejected logical-server creation with `RegionDoesNotAllowProvisioning` in East US, East US 2 and West Europe. A subsequent server listing confirmed that no SQL server/database was created. **Live accounts must remain disabled until region access, schema initialization and actual email delivery are verified.** No paid database fallback or SMTP plan was purchased. Microsoft.Sql was registered and a system-assigned managed identity was added to the API; neither provides a database by itself.

After Azure grants SQL region access, create one General Purpose Gen5 serverless database with `--use-free-limit true --free-limit-exhaustion-behavior AutoPause`, capacity 2, minimum 0.5, 60-minute idle auto-pause, 32 GB maximum, local backup redundancy and no zone redundancy. Verify the returned `useFreeLimit` and `freeLimitExhaustionBehavior` before enabling accounts. Never substitute `BillOverUsage`. At the monthly limit, sign-in/registration/recovery may be unavailable until the next month; public reading and diagrams do not depend on SQL startup.

Use Entra-only server administration and the API's managed identity, not a database password. The deployment administrator initializes the fresh database using `tools/Tym.AccountSetup`, passing a short-lived SQL access token on stdin. Its `initialize` command creates the Identity and `DataProtectionKeys` tables and a contained runtime principal with only SELECT/INSERT/UPDATE/DELETE on `dbo`. It does not migrate local preview users or SMTP credentials. Subsequent schema changes require a reviewed migration; the running API never creates SQL schema or receives DDL permissions.

```text
TYM_AUTH_SQL_CONNECTION=Server=tcp:<server>.database.windows.net,1433;Database=tym-accounts;Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;Pooling=False
```

The app enforces managed identity, TLS and certificate validation in production, and disables idle connection pooling to permit auto-pause. The standard EF Core Data Protection provider stores token keys in SQL so confirmation links and sessions survive container replacement. SQL encryption at rest and least-privilege database access protect this store; XML key material is not separately wrapped with Key Vault in this prototype. Database administrators can access it. Add independent key wrapping and a rotation/recovery procedure before treating this as a hardened account platform.

Restrict network access to the API's actual egress and temporary deployment IPs where possible. Do not enable all-internet SQL access. If Azure-services firewall access is necessary for the consumption environment, document that broader network scope and retain Entra-only, database-scoped authorization. Validate a real registration/confirmation, restart persistence and password recovery before announcing live email accounts. Keep SMTP passwords exclusively in Container App secrets, transferred without logging their values.

Run `dotnet run --project tools/Tym.AccountSetup -c Release -- --self-test` to verify the SQL schema, offline startup and production transport/identity/pooling guards without contacting Azure.

References: [Azure SQL free offer](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer?view=azuresql), [SQL capacity/region-access errors](https://learn.microsoft.com/azure/azure-sql/capacity-errors-troubleshoot), [EF Core Data Protection key storage](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-storage-providers?view=aspnetcore-9.0#entity-framework-core).
