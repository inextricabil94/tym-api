# English and Romanian Interface

The header's **Interface language / Limba interfeței** menu switches between English and Romanian. The preference is stored in `tym-ui-locale`; on first visit, the first supported browser language is used, with English as the fallback. Storage restrictions do not prevent changing language in memory. Changes synchronize between open tabs.

This is independent of **Text language / Limba textului**, which controls NLP. Interface changes preserve text, selected chapter, entity selection and cached diagrams. They neither repeat analysis nor refetch the current chapter. Source prose retains its own `lang` attribute, spelling, UTF-16 offsets and original headings. Numeric chapter labels follow the interface language; original headings remain available in option titles.

Translated presentation includes workbench controls, analysis tables, standard diagnostics, statuses, account forms/messages, image descriptions, accessibility labels, document title and SVG legend/tooltips. Downloaded SVG legends follow the current interface language; narrative excerpts and IDs remain unchanged. Raw JSON/XML/TimeML, model identifiers, technical annotation codes, proper names, book titles, sources and license identifiers are not translated. Previously unknown API diagnostic messages retain their original text as a fallback. This does not translate server-sent emails or change account-provider configuration.

## Maintenance

`ui/Tym.Ui/wwwroot/i18n.js` is a small shared catalog using existing React and browser Intl APIs; no new runtime dependency is required. English source strings are stable message keys. Add their Romanian values in the catalog and wrap presentation text with `t()`. For asynchronous messages use `I18n.message(key, params)` or `I18n.error(key, params)` so a subsequent language switch still translates them. Do not interpolate user prose into a translation key or pass source text through the translator.

Plural forms use `Intl.PluralRules`, including Romanian one/few/other categories, and numeric counts use locale formatting. Keep machine fields and API payloads independent of interface locale. Only presentation-owned SVG legend text and icon tooltips are localized; density cache responses are never mutated. New locale files must remain in the UI build/static publishing assets and load before `accounts.js` and `app.js`.

## Verification on 20 October 2026

- Release solution build: zero warnings and errors after restarting the local preview to release locked assemblies.
- Forty Node tests pass, including catalog/placeholder consistency, Romanian plural forms, locale fallback, blocked storage and chapter-label compatibility.
- The new browser suite passes both languages at 320, 390, 768, 1440 and 1920 pixels. It checks saved/browser locale, cross-tab synchronization, translated account errors, source/analysis independence, unchanged caches and no additional NLP calls on language changes. It uses authored API fixtures and sends no real email.
- Existing real API account suites pass in public and restricted reading modes. The full book/density browser suite also passes.
- A separate local smoke used two actual API-generated authored diagrams at 1440/390 pixels: ten density presets, Romanian SVG downloads, unchanged source text, and no clipped/overlapping legend labels or browser errors. Screenshots were visually reviewed.

Run unit tests with `node --test tools/tests/*.test.cjs`. With the UI preview running, run `node tools/tests/i18n.browser.mjs`; set `TYM_UI_URL`, `TYM_PLAYWRIGHT_MODULE` and `TYM_CHROMIUM` if required by the local runtime. Full-page screenshots can temporarily resize the browser, invalidating viewport-keyed diagrams, so localization screenshots use a stable viewport and scroll instead.

These changes were published to the static website and container UI on 20 October 2026, alongside the trained entity picker. Live asset hashes match this checkout; both UI hosts passed actual API/browser smoke checks and the static site passed the ten-case bilingual responsive fixture suite. See [release verification](verification/i18n-ner-20261020.md). The local preview remains at `http://127.0.0.1:8780/`.
