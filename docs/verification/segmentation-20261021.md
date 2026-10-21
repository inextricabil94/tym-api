# Segmentation and Header Release: 21 October 2026

## Published Changes

- Selectable article-guided rules, ML.NET SDCA logistic regression and averaged
  perceptron boundary algorithms for English/Romanian event-to-TS segmentation.
  Rules remain the default. The learned models use 48 authored feature-pattern
  rows, not an annotated narrative corpus; probabilities are uncalibrated and
  perceptron margins are not probabilities. No LLM or paid model endpoint is used.
- A Segmentation result tab marks original UTF-16 source ranges with selectable
  TS IDs, a type-color legend, evidence, score and TT navigation.
  Twenty TS spans per page preserve the complete source across pages. Density
  changes retain segmentation; algorithm changes invalidate incompatible caches.
- Language flags, account and theme controls align vertically at a stable
  44-pixel height. Mobile flags keep accessible names; status has its own row.
  Text/NLP language remains separate from interface language.

Implementation and research limits: [segmentation classifiers](../segmentation-classifiers.md).

## Verification

- Release API/checker and UI builds passed without warnings/errors. Six bilingual
  algorithm fixtures cover event/TS correspondence, UTF-16 spans, score semantics,
  continuity, density invariance, option validation and concurrent prediction.
- Existing trained EN/RO NER, three POS, nineteen reference fixtures and 202
  density cases passed again. Forty-five Node tests passed.
- The 36-case segmentation browser suite passed locally and on the published
  static assets: three algorithms, two languages, 320/390/1440 widths and both
  themes. Additional checks cover paging, chapter-local offsets, cache
  invalidation, legacy API rejection and restricted-source handling.
- The bilingual browser suite passed locally and live, including twenty header
  alignment cases: guest/signed-in, English/Romanian and five widths from 320 to
  1920 pixels. It checks equal button tops/heights, no overlap, locale persistence,
  account messages and source/cache preservation. Account fixtures send no mail.
- Real Azure requests executed English rules (five events, three TS), English
  logistic (five events, four TS) and Romanian perceptron (five events, four TS).
  The last ran through the container UI, preserved exact marked source, warmed
  all ten cached densities and switched density without a second generation.
  An unknown algorithm returned HTTP 400. Browser errors: zero.
- These fixture counts are reproducible engineering outputs, not NLP accuracy
  statistics. Three valid generations and one invalid request used the daily
  quota for the test IP. Fixture-only browser suites use no live generation quota.
- Both live UIs matched local SHA-256 hashes for eleven key assets, including
  the new segmentation script and flag files/license. Their configurations expose
  zero private local examples and the expected API URL. The existing research PDF
  checksum is unchanged.
- All sixteen server-only books retain their counts, source checksums and section
  counts. Catalog summary SHA-256 before/after:
  `9F26499CA89B5986DF9E6970061E84B96180319B83473F593A4699A918B5A81C`.
  Existing public reading and disabled live accounts are unchanged. Trained EN/RO
  NER remains active. No books, credentials, mail or databases entered Git/static
  assets; the library was copied inside the private registry.

## Deployment

- API base build: `ca14`; private-library final build: `ca15`.
- UI build: `ca13`.
- Final API/UI image tag: `release-20261021-segmentation` in the existing registry.
- Ready revisions: `tym-api-serban--r20261021seg` and
  `tym-ui-serban--r20261021seg`. Both report successful provisioning; the API
  revision is healthy with 100% traffic, 0.5 CPU / 1 GiB and max one replica.
- Static website: 31 public staged files uploaded with no-cache metadata and
  versioned script/style URLs. Existing research and other blobs were not deleted.
- Reported pre-release monthly cost was EUR 13.86. The EUR 20 subscription budget
  and shutdown workflow remained enabled after publication. No other stopped
  prototype was restarted. Cost reporting delays still prevent a strict hard cap.

[Live workbench](https://tymui20260707serban.z13.web.core.windows.net/),
[container UI](https://tym-ui-serban.livelyrock-2726c024.eastus.azurecontainerapps.io/),
[API](https://tym-api-serban.livelyrock-2726c024.eastus.azurecontainerapps.io/).
