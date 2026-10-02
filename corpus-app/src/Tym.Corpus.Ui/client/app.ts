/** API metadata is evidence about a model manifest, never a measured probability. */
interface CorpusModel {
  model_id: string; task?: string; language?: string; available: boolean;
  training_rows?: number; document_count?: number; labels?: string[];
  display_name?: string; input_hint?: string; provenance?: string;
  research_limit?: string; metadata_available?: boolean; source_groups?: string[];
}
interface PredictionResponse { predicted_label: string; research_limit?: string; [key: string]: unknown; }
interface Window { TYM_CONFIG?: { apiBaseUrl?: string; diagramUiUrl?: string; resultsBaseUrl?: string; }; }

(() => {
  'use strict';
  const config = window.TYM_CONFIG || {};
  const api = String(config.apiBaseUrl || 'http://127.0.0.1:8871').replace(/\/+$/, '');
  function byId<T extends HTMLElement = HTMLElement>(id: string): T {
    const element = document.getElementById(id);
    if (!element) throw new Error(`Missing UI element: ${id}`);
    return element as T;
  }
  const select = byId<HTMLSelectElement>('model-select'), input = byId<HTMLTextAreaElement>('input-text'), predict = byId<HTMLButtonElement>('predict-button');
  const result = byId('prediction-result'), status = byId('request-status');
  const number = (n: unknown) => typeof n === 'number' ? n.toLocaleString() : 'Not reported';
  const fallbackLimit = 'Exploratory output from provided annotations with unknown adjudication status. Independent accuracy has not been measured.';
  const modelInfo: Record<string, [string, string, string]> = {
    segment_type_en: ['Narrative segment · English', 'TYM', 'Classify a candidate segment as narration, memory, fiction, or general knowledge.'],
    segment_type_ro: ['Narrative segment · Romanian', 'TYM', 'Classify the narrative mode of a Romanian candidate segment.'],
    temporal_relation_en: ['Segment relation · English', 'TYM', 'Predict a directed temporal relation between two supplied segments.'],
    temporal_relation_ro: ['Segment relation · Romanian', 'TYM', 'Predict a directed temporal relation between two supplied Romanian segments.'],
    timebank_event_class_ro: ['Event class · Romanian', 'ISO-TimeML', 'Classify the semantic category of the marked event mention.'],
    timebank_event_tense_ro: ['Event tense · Romanian', 'ISO-TimeML', 'Predict the grammatical tense label of the marked event mention.'],
    timebank_timex_type_ro: ['Time expression · Romanian', 'ISO-TimeML', 'Classify a marked time expression as date, time, duration, or set.'],
    timebank_tlink_ro: ['Temporal link · Romanian', 'ISO-TimeML', 'Predict the temporal relation from the source mention to the target mention.'],
    timebank_slink_ro: ['Subordination link · Romanian', 'ISO-TimeML', 'Classify the subordinating relation between two marked event mentions.'],
    timebank_alink_ro: ['Aspectual link · Romanian', 'ISO-TimeML', 'Classify how the source event describes the target event’s phase.']
  };
  const examples: Record<string, string[]> = {
    segment_type_en: ['Years after leaving home, Mara remembered the blue room and the letter her brother had left.', 'Every spring, villagers gathered at the fountain and retold the same story.'],
    segment_type_ro: ['Mult timp după ce a plecat de acasă, Mara și-a amintit de camera albastră și de scrisoarea pe care o lăsase fratele ei.', 'În fiecare primăvară, sătenii se adunau la fântână și spuneau aceeași poveste.'],
    temporal_relation_en: ['Earlier segment: Mara left the village before dawn.\nRelation cue: years later.\nLater segment: She remembered the journey.', 'Earlier segment: The rain stopped.\nRelation cue: at the same time.\nLater segment: The ferry reached the harbor.'],
    temporal_relation_ro: ['Earlier segment: Mara a părăsit satul înainte de răsărit.\nRelation cue: ani mai târziu.\nLater segment: Ea și-a amintit călătoria.', 'Earlier segment: Ploaia s-a oprit.\nRelation cue: în același timp.\nLater segment: Feribotul a ajuns în port.'],
    timebank_event_class_ro: ['Context: Mara [TARGET] a aflat [/TARGET] vestea și a zâmbit.\nEvent: a aflat', 'Context: [TARGET] Cred [/TARGET] că drumul va fi lung.\nEvent: Cred'],
    timebank_event_tense_ro: ['Context: Mâine Mara [TARGET] va pleca [/TARGET] spre munte.\nEvent: va pleca', 'Context: Ieri Mara [TARGET] a terminat [/TARGET] cartea.\nEvent: a terminat'],
    timebank_timex_type_ro: ['Context: În [TARGET] primăvara trecută [/TARGET], Mara a sosit la Iași.\nTime expression: primăvara trecută', 'Context: A rămas acolo timp de [TARGET] trei zile [/TARGET].\nTime expression: trei zile'],
    timebank_tlink_ro: ['From: a plecat\nFrom context: Mara [TARGET] a plecat [/TARGET] din oraș înainte de răsărit.\nSignal: înainte\nTo: a ajuns\nTo context: Călătorul [TARGET] a ajuns [/TARGET] la munte în aceeași zi.', 'From: a părăsit\nFrom context: Mara [TARGET] a părăsit [/TARGET] satul.\nSignal: după\nTo: s-a întors\nTo context: Ea [TARGET] s-a întors [/TARGET] acasă două zile mai târziu.'],
    timebank_slink_ro: ['From: crede\nFrom context: Mara [TARGET] crede [/TARGET] că drumul va fi lung.\nSignal: că\nTo: va fi\nTo context: Mara crede că drumul [TARGET] va fi [/TARGET] lung.', 'From: știa\nFrom context: El [TARGET] știa [/TARGET] că trenul întârziase.\nSignal: că\nTo: întârziase\nTo context: El știa că trenul [TARGET] întârziase [/TARGET].'],
    timebank_alink_ro: ['From: a început\nFrom context: Ploaia [TARGET] a început [/TARGET] să cadă.\nSignal: să\nTo: cadă\nTo context: Ploaia a început să [TARGET] cadă [/TARGET].', 'From: a continuat\nFrom context: Ea [TARGET] a continuat [/TARGET] să citească.\nSignal: să\nTo: citească\nTo context: Ea a continuat să [TARGET] citească [/TARGET].']
  };
  const meanings: Record<string, string> = {
    NAR:'Narration', REM:'Remembered events', FIC:'Imagined or fictional events', GEN:'General knowledge', SUP:'Supporting material',
    OCCURRENCE:'Event occurrence', STATE:'State', I_STATE:'Intentional state', I_ACTION:'Intentional action', REPORTING:'Reporting', PERCEPTION:'Perception', ASPECTUAL:'Aspectual event',
    DATE:'Date expression', TIME:'Time expression', DURATION:'Duration expression', SET:'Recurring set of times',
    PAST:'Past tense', PRESENT:'Present tense', FUTURE:'Future tense', NONE:'No grammatical tense', INFINITIVE:'Infinitive', PASTPART:'Past participle', PRESPART:'Present participle',
    BEFORE:'Source precedes target', AFTER:'Source follows target', SIMULTANEOUS:'Source and target coincide',
    IMMEDIATELY_BEFORE:'Source immediately precedes target', IMMEDIATELY_AFTER:'Source immediately follows target',
    INITIATES:'Source initiates target', CONTINUES:'Source continues target', TERMINATES:'Source terminates target', REINITIATES:'Source reinitiates target', CULMINATES:'Source culminates target'
  };
  let models: CorpusModel[] = [], busy = false, selectedId = '', catalogReady = false;
  const drafts = new Map<string, string>();
  const baseModelId = (id: string) => id.replace(/_structured$/, '');
  function safeLink(elementId: string, url: string) {
    try { const u = new URL(url, location.origin); if (['http:', 'https:'].includes(u.protocol)) byId<HTMLAnchorElement>(elementId).href = u.href; } catch { /* Preserve the original link. */ }
  }
  safeLink('diagram-link', config.diagramUiUrl || byId<HTMLAnchorElement>('diagram-link').href);
  safeLink('pdf-link', '/results/Deep_Learning_Spatial_Temporal_Semantics_Updated_Results_20261002.pdf');
  safeLink('pptx-link', '/results/TYM_MLNET_Updated_Results_20261002.pptx');
  const currentModel = () => models.find(m => m.model_id === select.value);
  function requestStatus(message: string, tone='info') { status.textContent = message; status.dataset.tone = tone; }
  function controls() {
    const model = currentModel();
    predict.disabled = busy || !catalogReady || !model || !model.available || !input.value.trim();
    select.disabled = busy || !models.length;
    byId<HTMLButtonElement>('example-one').disabled = byId<HTMLButtonElement>('example-two').disabled = busy || !model;
    input.readOnly = busy;
    byId('prediction-form').setAttribute('aria-busy', String(busy));
  }
  function inputChanged() {
    byId('input-count').textContent = `${input.value.length.toLocaleString()} / 50,000 characters`;
    result.hidden = true;
    if (!busy) requestStatus('');
    controls();
  }
  function renderSelection() {
    if (selectedId) drafts.set(selectedId, input.value);
    const model = currentModel();
    if (!model) return;
    selectedId = model.model_id;
    const info = modelInfo[baseModelId(selectedId)] || [model.display_name || model.task || selectedId, 'Corpus classifier', 'Explore the selected classifier.'];
    byId('model-description').textContent = info[2];
    byId('model-language').textContent = model.language === 'ro' ? 'Romanian' : model.language === 'en' ? 'English' : model.language || 'Not reported';
    input.lang = model.language === 'ro' ? 'ro' : 'en';
    byId('model-rows').textContent = number(model.training_rows);
    byId('model-documents').textContent = number(model.document_count);
    byId('model-labels').textContent = (model.labels || []).join(', ') || 'Not reported';
    byId('model-provenance').textContent = typeof model.provenance === 'string' ? model.provenance : 'Provided annotations. Adjudication status unknown.';
    const hint = model.input_hint || (selectedId.startsWith('timebank_') ? 'Keep the context fields shown in the example.' : selectedId.startsWith('temporal_relation_') ? 'Use the two segment fields and relation cue shown in the example. The model predicts their directed relationship.' : 'Supply the prose of a candidate narrative segment. The model scores the entire supplied text.');
    byId('input-hint').textContent = hint + (selectedId.startsWith('timebank_') ? ' Mark the intended mention in each context with [TARGET] and [/TARGET].' : '');
    input.value = drafts.get(selectedId) ?? examples[baseModelId(selectedId)]?.[0] ?? '';
    input.placeholder = 'Enter your text or load an example.';
    inputChanged();
    if (catalogReady && !model.available) requestStatus('This model is not configured on the connected server. You can still edit its examples.', 'error');
  }
  function populateCatalog() {
    select.replaceChildren();
    for (const family of ['TYM','ISO-TimeML']) {
      const group = document.createElement('optgroup'); group.label = family === 'TYM' ? 'TYM narrative models' : 'ISO-TimeML models';
      for (const model of models.filter(m => (modelInfo[baseModelId(m.model_id)]?.[1] || 'TYM') === family)) {
        const option = document.createElement('option'); option.value = model.model_id;
        option.textContent = (model.display_name || modelInfo[baseModelId(model.model_id)]?.[0] || model.task || model.model_id) + (model.available ? '' : ' (unavailable)');
        group.append(option);
      }
      if (group.children.length) select.append(group);
    }
    select.value = models.some(m => m.model_id === selectedId) ? selectedId : (models.find(m => m.available) || models[0])?.model_id || '';
    renderSelection();
  }
  async function fetchJson<T>(url: string, options: RequestInit = {}): Promise<T> {
    const response = await fetch(url, {...options, signal:AbortSignal.timeout(45000)});
    const type = response.headers.get('content-type') || '';
    if (!type.includes('json')) throw new Error(response.ok ? 'The API returned an unexpected response.' : `The API returned HTTP ${response.status}.`);
    const data = await response.json();
    if (!response.ok) throw new Error(data.error || data.detail || `The API returned HTTP ${response.status}.`);
    return data as T;
  }
  async function loadCatalog() {
    byId('catalog-status').textContent = 'Connecting to the model catalog…';
    byId('reload-catalog').hidden = true;
    catalogReady = false; controls();
    try {
      const data = await fetchJson<{models: CorpusModel[]}>(api + '/v1/models');
      if (!Array.isArray(data.models) || !data.models.length) throw new Error('The API has no corpus classifiers in its catalog.');
      models = data.models; catalogReady = true; populateCatalog();
      const available = models.filter(m => m.available).length;
      byId('catalog-status').textContent = `${available} of ${models.length} models available`;
      byId('model-total').textContent = String(models.length);
      if (available < models.length) byId('reload-catalog').hidden = false;
    } catch (error) {
      models = Object.keys(modelInfo).map(model_id => ({model_id, language:model_id.endsWith('_ro')?'ro':'en', available:false, labels:[]}));
      populateCatalog();
      byId('catalog-status').textContent = 'API connection unavailable';
      byId('reload-catalog').hidden = false;
      requestStatus('Could not connect to the corpus API. Check its configured address or reconnect. Examples remain editable.', 'error');
    }
    controls();
  }
  select.addEventListener('change', renderSelection);
  input.addEventListener('input', inputChanged);
  function loadExample(index: number) { input.value = examples[baseModelId(select.value)]?.[index] || ''; inputChanged(); input.focus(); }
  byId('example-one').addEventListener('click', () => loadExample(0));
  byId('example-two').addEventListener('click', () => loadExample(1));
  byId('reload-catalog').addEventListener('click', loadCatalog);
  byId('prediction-form').addEventListener('submit', async event => {
    event.preventDefault();
    const model = currentModel();
    if (busy || !model?.available || !catalogReady) return;
    if (!input.value.trim()) { requestStatus('Enter some text before predicting.', 'error'); input.focus(); return; }
    busy = true; controls(); result.hidden = true; requestStatus('Predicting the label…');
    try {
      const data = await fetchJson<PredictionResponse>(api + '/v1/predictions', {method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({model_id:model.model_id,text:input.value})});
      if (typeof data.predicted_label !== 'string' || !data.predicted_label.trim()) throw new Error('The model returned no prediction label.');
      byId('predicted-label').textContent = data.predicted_label;
      byId('label-meaning').textContent = meanings[data.predicted_label] || 'Label from the selected model’s vocabulary';
      byId('prediction-limit').textContent = data.research_limit || model.research_limit || fallbackLimit;
      byId('response-json').textContent = JSON.stringify(data,null,2);
      result.hidden = false; requestStatus('Prediction complete.');
    } catch (error) {
      const message = error instanceof Error ? (error.name === 'TimeoutError' ? 'The API took too long to respond. Try again.' : error.name === 'TypeError' ? 'Could not reach the prediction API. Check the connection and try again.' : error.message) : 'Prediction failed. Try again.';
      requestStatus(message, 'error');
    } finally { busy = false; controls(); }
  });
  loadCatalog();
})();
