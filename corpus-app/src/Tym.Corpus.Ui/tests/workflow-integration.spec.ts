import { test, expect, type APIRequestContext, type Page } from 'playwright/test';
import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve, join } from 'node:path';

/** Real browser -> HTTP -> immutable private ML.NET bundle checks.
 * All inputs below were authored for the tests; none are copied from Books.zip.
 * Labels are observations, not adjudicated correctness or an accuracy score.
 */
interface Workflow {
  id: string; title: string; model: string; task: string; language: 'en' | 'ro';
  text: string; explanation: string; scope: string; structured?: string;
}
interface ReadyModel {
  model_id: string; ready: boolean; integrity_status: string;
  identity_status: string; readiness_status: string;
}
interface Readiness {
  ready: boolean; all_integrity_verified: boolean; ready_models: number;
  total_models: number; models: ReadyModel[];
}
interface CatalogModel {
  model_id: string; task: string; language: string; available: boolean;
  metadata_available: boolean; installed: boolean; loaded: boolean; scorable: boolean;
  labels: string[]; training_rows: number; document_count: number; model_sha256: string;
  integrity_status: string; identity_status: string; readiness_status: string;
  input_format: string | null;
}
interface Prediction {
  model_id: string; task: string; language: string; predicted_label: string;
  labels: string[]; metadata_available: boolean; training_rows: number; document_count: number;
  integrity_status: string; identity_status: string; readiness_status: string;
  research_limit: string; input_format: string | null;
}
interface VariantEvidence {
  model_id: string; input_format: string | null; predicted_label: string; ui_label: string;
  labels: string[]; training_rows: number; document_count: number; model_sha256: string;
  identity_status: string; integrity_status: string; readiness_status: string;
  http_status: number; ui_http_status: number; ui_api_agreement: boolean;
  direct_api_elapsed_ms: number; ui_round_trip_elapsed_ms: number;
}

const enabled = process.env.TYM_RUN_WORKFLOW_INTEGRATION === '1';
const api = (process.env.TYM_UI_TEST_API_URL || 'http://127.0.0.1:8871').replace(/\/+$/, '');
const evidenceRoot = process.env.TYM_WORKFLOW_EVIDENCE_DIR;

// One case per user workflow. Six TimeBank cases exercise both representations
// of that same task, so ten tests cover all sixteen installed model identities.
const workflows: Workflow[] = [
  {
    id: 'prediction-segment-en', title: 'English narrative segment', model: 'segment_type_en',
    task: 'segment_type', language: 'en',
    text: 'Years after leaving home, Mara remembered the blue room and the letter her brother had left.',
    explanation: 'The English TYM classifier scores the narrative mode of one supplied segment.',
    scope: 'Candidate segment supplied by the user; this does not evaluate document segmentation or independent narrative accuracy.'
  },
  {
    id: 'prediction-segment-ro', title: 'Romanian narrative segment', model: 'segment_type_ro',
    task: 'segment_type', language: 'ro',
    text: 'Mult timp după ce a plecat de acasă, Mara și-a amintit de camera albastră și de scrisoarea pe care o lăsase fratele ei.',
    explanation: 'The Romanian TYM classifier scores the narrative mode of one supplied segment, preserving Romanian diacritics.',
    scope: 'Candidate segment supplied by the user; the parallel English and Romanian training story is one source work.'
  },
  {
    id: 'prediction-relation-en', title: 'English directed segment relation', model: 'temporal_relation_en',
    task: 'temporal_relation', language: 'en',
    text: 'Earlier segment: Mara left the village before dawn.\nRelation cue: years later.\nLater segment: She remembered the journey.',
    explanation: 'The English TYM relation classifier receives two ordered segments and a relation cue.',
    scope: 'Ordered endpoints and cue are supplied; the predicted relationship is not an automatically extracted or adjudicated timeline.'
  },
  {
    id: 'prediction-relation-ro', title: 'Romanian directed segment relation', model: 'temporal_relation_ro',
    task: 'temporal_relation', language: 'ro',
    text: 'Earlier segment: Mara a părăsit satul înainte de răsărit.\nRelation cue: ani mai târziu.\nLater segment: Ea și-a amintit călătoria.',
    explanation: 'The Romanian TYM relation classifier receives two ordered segments and a relation cue.',
    scope: 'Ordered endpoints and cue are supplied; endpoint detection, chronology reconstruction and independent semantic accuracy are outside this check.'
  },
  {
    id: 'prediction-event-class', title: 'Romanian TimeBank event class', model: 'timebank_event_class_ro',
    structured: 'timebank_event_class_ro_structured', task: 'timebank_event_class', language: 'ro',
    text: 'Context: Mara [TARGET] a aflat [/TARGET] vestea și a zâmbit.\nEvent: a aflat',
    explanation: 'The lexical and structured event-class models classify the same marked event mention.',
    scope: 'Conditional event classification: the event span is supplied, not detected by the test.'
  },
  {
    id: 'prediction-event-tense', title: 'Romanian TimeBank event tense', model: 'timebank_event_tense_ro',
    structured: 'timebank_event_tense_ro_structured', task: 'timebank_event_tense', language: 'ro',
    text: 'Context: Mâine Mara [TARGET] va pleca [/TARGET] spre munte.\nEvent: va pleca',
    explanation: 'The lexical and structured tense models classify the tense label of the same supplied event.',
    scope: 'Conditional tense classification of a marked event, not calendar date prediction or end-to-end event extraction.'
  },
  {
    id: 'prediction-timex-type', title: 'Romanian TimeBank time-expression type', model: 'timebank_timex_type_ro',
    structured: 'timebank_timex_type_ro_structured', task: 'timebank_timex_type', language: 'ro',
    text: 'Context: În [TARGET] primăvara trecută [/TARGET], Mara a sosit la Iași.\nTime expression: primăvara trecută',
    explanation: 'The lexical and structured TIMEX models classify a supplied temporal expression by type.',
    scope: 'The time-expression span is provided; type classification does not establish a normalized date or measure span detection.'
  },
  {
    id: 'prediction-tlink', title: 'Romanian TimeBank temporal link', model: 'timebank_tlink_ro',
    structured: 'timebank_tlink_ro_structured', task: 'timebank_tlink', language: 'ro',
    text: 'From: a plecat\nFrom context: Mara [TARGET] a plecat [/TARGET] din oraș înainte de răsărit.\nSignal: înainte\nTo: a ajuns\nTo context: Călătorul [TARGET] a ajuns [/TARGET] la munte în aceeași zi.',
    explanation: 'The lexical and structured TLINK models predict a temporal label from the supplied source to target mention.',
    scope: 'Conditional relation classification with supplied ordered endpoints and signal; this does not assess graph completion or document timeline accuracy.'
  },
  {
    id: 'prediction-slink', title: 'Romanian TimeBank subordination link', model: 'timebank_slink_ro',
    structured: 'timebank_slink_ro_structured', task: 'timebank_slink', language: 'ro',
    text: 'From: crede\nFrom context: Mara [TARGET] crede [/TARGET] că drumul va fi lung.\nSignal: că\nTo: va fi\nTo context: Mara crede că drumul [TARGET] va fi [/TARGET] lung.',
    explanation: 'The lexical and structured SLINK models classify a directed subordination relationship between supplied events.',
    scope: 'Subordination labels describe the supplied events; they do not by themselves establish temporal precedence.'
  },
  {
    id: 'prediction-alink', title: 'Romanian TimeBank aspectual link', model: 'timebank_alink_ro',
    structured: 'timebank_alink_ro_structured', task: 'timebank_alink', language: 'ro',
    text: 'From: a început\nFrom context: Ploaia [TARGET] a început [/TARGET] să cadă.\nSignal: să\nTo: cadă\nTo context: Ploaia a început să [TARGET] cadă [/TARGET].',
    explanation: 'The lexical and structured ALINK models classify the source event’s phase relationship to a supplied target event.',
    scope: 'Aspectual labels refer to supplied ordered event mentions, not automatically extracted causal or temporal edges.'
  }
];

/** Reject remote hosts before any browser navigation or direct HTTP request. */
function localUrl(value: string | undefined, purpose: string): URL {
  if (!value) throw new Error(`${purpose} requires an explicit localhost URL.`);
  const url = new URL(value);
  if (!['http:', 'https:'].includes(url.protocol) || !['127.0.0.1', 'localhost', '[::1]'].includes(url.hostname)
    || url.username || url.password) throw new Error(`${purpose} must use localhost without URL credentials.`);
  return url;
}

async function jsonResponse<T>(request: APIRequestContext, path: string): Promise<T> {
  // Direct request contexts bypass page routing. Reject redirects so an API
  // response cannot forward authored inputs or metadata checks to another host.
  const response = await request.get(api + path, { maxRedirects: 0 });
  expect(response.status(), `Live ${path} returns HTTP 200`).toBe(200);
  expect(response.headers()['content-type']).toContain('application/json');
  const data = await response.json() as T;
  await response.dispose();
  return data;
}

function verifiedModel(model: CatalogModel | undefined, workflow: Workflow, modelId: string): CatalogModel {
  expect(model, `Installed catalog entry exists for ${modelId}`).toBeDefined();
  const found = model!;
  expect(found.model_id).toBe(modelId);
  expect(found.task).toBe(workflow.task);
  expect(found.language).toBe(workflow.language);
  for (const field of ['available', 'metadata_available', 'installed', 'loaded', 'scorable'] as const) expect(found[field]).toBe(true);
  expect(found.integrity_status).toBe('verified');
  expect(found.identity_status).toBe('verified');
  expect(found.readiness_status).toBe('ready_verified');
  expect(found.model_sha256).toMatch(/^[a-f0-9]{64}$/i);
  expect(Array.isArray(found.labels) && found.labels.length > 0).toBe(true);
  expect(found.labels.every(label => typeof label === 'string' && label.trim().length > 0)).toBe(true);
  expect(new Set(found.labels).size).toBe(found.labels.length);
  expect(Number.isSafeInteger(found.training_rows) && found.training_rows > 0).toBe(true);
  expect(Number.isSafeInteger(found.document_count) && found.document_count > 0).toBe(true);
  expect(found.input_format).toBe(modelId.endsWith('_structured') ? 'structured_target_v1' : null);
  return found;
}

async function scoreThroughApiAndUi(page: Page, workflow: Workflow, model: CatalogModel): Promise<VariantEvidence> {
  const request = { model_id: model.model_id, text: workflow.text };
  const directStarted = performance.now();
  const direct = await page.request.post(api + '/v1/predictions', { data: request, maxRedirects: 0 });
  const directElapsed = performance.now() - directStarted;
  expect(direct.status()).toBe(200);
  expect(direct.headers()['content-type']).toContain('application/json');
  const prediction = await direct.json() as Prediction;
  const directStatus = direct.status();
  await direct.dispose();
  expect(prediction.model_id).toBe(model.model_id);
  expect(prediction.task).toBe(workflow.task);
  expect(prediction.language).toBe(workflow.language);
  expect(model.labels).toContain(prediction.predicted_label);
  expect(prediction.labels.slice().sort()).toEqual(model.labels.slice().sort());
  expect(prediction.metadata_available).toBe(true);
  expect(prediction.training_rows).toBe(model.training_rows);
  expect(prediction.document_count).toBe(model.document_count);
  expect(prediction.integrity_status).toBe('verified');
  expect(prediction.identity_status).toBe('verified');
  expect(prediction.readiness_status).toBe('ready_verified');
  expect(prediction.input_format).toBe(model.input_format);
  expect(prediction.research_limit).toContain('Independent accuracy has not been measured');

  await page.locator('#model-select').selectOption(model.model_id);
  await page.locator('#input-text').fill(workflow.text);
  await expect(page.locator('#input-text')).toHaveValue(workflow.text);
  const pendingHttp = page.waitForResponse(response => response.url() === api + '/v1/predictions'
    && response.request().method() === 'POST');
  const uiStarted = performance.now();
  await page.getByRole('button', { name: 'Predict label', exact: true }).click();
  const uiHttp = await pendingHttp;
  expect(uiHttp.status()).toBe(200);
  expect(uiHttp.request().postDataJSON()).toEqual(request);
  const uiPrediction = await uiHttp.json() as Prediction;
  await expect(page.locator('#request-status')).toHaveText('Prediction complete.');
  await expect(page.locator('#prediction-result')).toBeVisible();
  await expect(page.locator('#predicted-label')).toHaveText(prediction.predicted_label);
  await expect(page.locator('#prediction-limit')).toContainText('Independent accuracy has not been measured');
  // The response <pre> is inside a closed <details>. innerText can therefore
  // be empty although app.ts has populated it before showing the result.
  // Read DOM textContent, retry until valid JSON, and compare the entire object.
  const responseJson = page.locator('#response-json');
  await expect(responseJson).not.toHaveText('');
  await expect.poll(async () => {
    const text = await responseJson.textContent();
    if (!text?.trim()) return null;
    try { return JSON.parse(text) as Prediction; } catch { return null; }
  }, { message: 'The UI response JSON equals the real direct API result.' }).toEqual(prediction);
  const rendered = JSON.parse((await responseJson.textContent())!) as Prediction;
  expect(uiPrediction).toEqual(prediction);
  expect(rendered).toEqual(uiPrediction);
  const uiLabel = await page.locator('#predicted-label').innerText();
  const uiElapsed = performance.now() - uiStarted;
  expect(Number.isFinite(directElapsed) && directElapsed >= 0).toBe(true);
  expect(Number.isFinite(uiElapsed) && uiElapsed >= 0).toBe(true);
  return {
    model_id: model.model_id, input_format: prediction.input_format,
    predicted_label: prediction.predicted_label, ui_label: uiLabel, labels: model.labels,
    training_rows: model.training_rows, document_count: model.document_count,
    model_sha256: model.model_sha256, identity_status: prediction.identity_status,
    integrity_status: prediction.integrity_status, readiness_status: prediction.readiness_status,
    http_status: directStatus, ui_http_status: uiHttp.status(), ui_api_agreement: true,
    direct_api_elapsed_ms: directElapsed, ui_round_trip_elapsed_ms: uiElapsed
  };
}

test.describe('ten real localhost prediction workflows', () => {
  test.skip(!enabled, 'Set TYM_RUN_WORKFLOW_INTEGRATION=1 to score real private ML.NET bundles through the local UI and API.');
  for (const workflow of workflows) {
    test(`${workflow.id}: ${workflow.title}`, async ({ page }, testInfo) => {
      const apiUrl = localUrl(api, 'Integration API');
      const uiUrl = localUrl(testInfo.project.use.baseURL, 'Integration UI');
      const allowedOrigins = new Set([apiUrl.origin, uiUrl.origin]);
      // This firewall only continues real requests; it never fulfills or mocks one.
      await page.route('**/*', route => {
        try {
          return allowedOrigins.has(localUrl(route.request().url(), 'Browser request').origin)
            ? route.continue() : route.abort('blockedbyclient');
        } catch { return route.abort('blockedbyclient'); }
      });
      const readiness = await jsonResponse<Readiness>(page.request, '/ready');
      expect(readiness.ready).toBe(true);
      expect(readiness.all_integrity_verified).toBe(true);
      expect(readiness.total_models).toBe(16);
      expect(readiness.ready_models).toBe(16);
      expect(readiness.models).toHaveLength(16);
      const catalog = await jsonResponse<{ models: CatalogModel[] }>(page.request, '/v1/models');
      expect(catalog.models).toHaveLength(16);
      await page.setViewportSize({ width: 1280, height: 1100 });
      await page.goto('/#workbench');
      expect(localUrl(page.url(), 'Navigated UI').origin).toBe(uiUrl.origin);
      await expect(page.locator('#catalog-status')).toHaveText('16 of 16 models available');
      const configuredApi = await page.evaluate(() =>
        (window as Window & { TYM_CONFIG?: { apiBaseUrl: string } }).TYM_CONFIG?.apiBaseUrl || '');
      expect(localUrl(configuredApi, 'Configured UI API').origin).toBe(apiUrl.origin);

      const variants: VariantEvidence[] = [];
      for (const modelId of [workflow.model, ...(workflow.structured ? [workflow.structured] : [])]) {
        const ready = readiness.models.find(model => model.model_id === modelId);
        expect(ready?.ready).toBe(true);
        expect(ready?.integrity_status).toBe('verified');
        expect(ready?.identity_status).toBe('verified');
        expect(ready?.readiness_status).toBe('ready_verified');
        const model = verifiedModel(catalog.models.find(item => item.model_id === modelId), workflow, modelId);
        variants.push(await scoreThroughApiAndUi(page, workflow, model));
      }

      // Evidence is generated only after all assertions pass. It contains selected
      // metadata and authored prose, never private paths or training corpus text.
      if (evidenceRoot) {
        const directory = join(resolve(evidenceRoot), 'predictions');
        mkdirSync(directory, { recursive: true });
        await page.locator('#workbench').screenshot({ path: join(directory, workflow.id + '.png') });
        writeFileSync(join(directory, workflow.id + '.json'), JSON.stringify({
          schema_version: 1, suite: 'predictions', workflow_id: workflow.id, title: workflow.title,
          status: 'passed', fixture_origin: 'original_authored_non_corpus', input_text: workflow.text,
          language: workflow.language, task: workflow.task, explanation: workflow.explanation, scope: workflow.scope,
          observed_at_utc: new Date().toISOString(), ui_api_agreement: true, variants,
          screenshot: 'predictions/' + workflow.id + '.png', screenshot_model_id: variants.at(-1)!.model_id,
          assertions: [
            'Localhost UI and API only; live HTTP requests, no mocked response.',
            'All sixteen installed ML.NET bundles are ready with verified integrity.',
            'Each exercised task/language identity and SHA-256 manifest is verified.',
            'Returned label belongs to that model’s nonempty manifest inventory.',
            'The UI sends the exact authored input and selected model identity.',
            'Direct API, browser HTTP response and displayed label/JSON agree.',
            'The UI preserves the independent-accuracy limitation.'
          ],
          scores_exposed: false, score_count: null,
          score_limit: 'The prediction HTTP contract exposes a label and metadata, not class scores or probabilities; finite score assertions are unavailable.',
          accuracy: null,
          accuracy_limit: 'Integration checks record program behavior on authored examples, not adjudicated semantic correctness.',
          timing_limit: 'Single local HTTP and browser round trips include service/browser overhead; these are not benchmark averages or isolated inference latency.'
        }, null, 2) + '\n', 'utf8');
      }
    });
  }
});
