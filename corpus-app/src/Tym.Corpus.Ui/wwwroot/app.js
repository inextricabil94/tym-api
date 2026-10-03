"use strict";
(() => {
    'use strict';
    const config = window.TYM_CONFIG || {};
    const api = String(config.apiBaseUrl || 'http://127.0.0.1:8871').replace(/\/+$/, '');
    function byId(id) {
        const element = document.getElementById(id);
        if (!element)
            throw new Error(`Missing UI element: ${id}`);
        return element;
    }
    const select = byId('model-select'), input = byId('input-text'), predict = byId('predict-button');
    const result = byId('prediction-result'), status = byId('request-status');
    const number = (n) => typeof n === 'number' ? n.toLocaleString() : 'Not reported';
    const fallbackLimit = 'Exploratory output from provided annotations with unknown adjudication status. Independent accuracy has not been measured.';
    const modelInfo = {
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
    const examples = {
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
    const meanings = {
        NAR: 'Narration', REM: 'Remembered events', FIC: 'Imagined or fictional events', GEN: 'General knowledge', SUP: 'Supporting material',
        OCCURRENCE: 'Event occurrence', STATE: 'State', I_STATE: 'Intentional state', I_ACTION: 'Intentional action', REPORTING: 'Reporting', PERCEPTION: 'Perception', ASPECTUAL: 'Aspectual event',
        DATE: 'Date expression', TIME: 'Time expression', DURATION: 'Duration expression', SET: 'Recurring set of times',
        PAST: 'Past tense', PRESENT: 'Present tense', FUTURE: 'Future tense', NONE: 'No grammatical tense', INFINITIVE: 'Infinitive', PASTPART: 'Past participle', PRESPART: 'Present participle',
        BEFORE: 'Source precedes target', AFTER: 'Source follows target', SIMULTANEOUS: 'Source and target coincide',
        IMMEDIATELY_BEFORE: 'Source immediately precedes target', IMMEDIATELY_AFTER: 'Source immediately follows target',
        INITIATES: 'Source initiates target', CONTINUES: 'Source continues target', TERMINATES: 'Source terminates target', REINITIATES: 'Source reinitiates target', CULMINATES: 'Source culminates target'
    };
    let models = [], busy = false, selectedId = '', catalogReady = false;
    const drafts = new Map();
    const baseModelId = (id) => id.replace(/_structured$/, '');
    function safeLink(elementId, url) {
        try {
            const u = new URL(url, location.origin);
            if (['http:', 'https:'].includes(u.protocol))
                byId(elementId).href = u.href;
        }
        catch { /* Preserve the original link. */ }
    }
    safeLink('diagram-link', config.diagramUiUrl || byId('diagram-link').href);
    safeLink('pdf-link', '/results/Deep_Learning_Spatial_Temporal_Semantics_Updated_Results_20261002.pdf');
    safeLink('pptx-link', '/results/TYM_MLNET_Updated_Results_20261002.pptx');
    const currentModel = () => models.find(m => m.model_id === select.value);
    function requestStatus(message, tone = 'info') { status.textContent = message; status.dataset.tone = tone; }
    function controls() {
        const model = currentModel();
        predict.disabled = busy || !catalogReady || !model || !model.available || !input.value.trim();
        select.disabled = busy || !models.length;
        byId('example-one').disabled = byId('example-two').disabled = busy || !model;
        input.readOnly = busy;
        byId('prediction-form').setAttribute('aria-busy', String(busy));
    }
    function inputChanged() {
        byId('input-count').textContent = `${input.value.length.toLocaleString()} / 50,000 characters`;
        result.hidden = true;
        if (!busy)
            requestStatus('');
        controls();
    }
    function renderSelection() {
        if (selectedId)
            drafts.set(selectedId, input.value);
        const model = currentModel();
        if (!model)
            return;
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
        if (catalogReady && !model.available)
            requestStatus('This model is not configured on the connected server. You can still edit its examples.', 'error');
    }
    function populateCatalog() {
        select.replaceChildren();
        for (const family of ['TYM', 'ISO-TimeML']) {
            const group = document.createElement('optgroup');
            group.label = family === 'TYM' ? 'TYM narrative models' : 'ISO-TimeML models';
            for (const model of models.filter(m => (modelInfo[baseModelId(m.model_id)]?.[1] || 'TYM') === family)) {
                const option = document.createElement('option');
                option.value = model.model_id;
                option.textContent = (model.display_name || modelInfo[baseModelId(model.model_id)]?.[0] || model.task || model.model_id) + (model.available ? '' : ' (unavailable)');
                group.append(option);
            }
            if (group.children.length)
                select.append(group);
        }
        select.value = models.some(m => m.model_id === selectedId) ? selectedId : (models.find(m => m.available) || models[0])?.model_id || '';
        renderSelection();
    }
    async function fetchJson(url, options = {}) {
        const response = await fetch(url, { ...options, signal: AbortSignal.timeout(45000) });
        const type = response.headers.get('content-type') || '';
        if (!type.includes('json'))
            throw new Error(response.ok ? 'The API returned an unexpected response.' : `The API returned HTTP ${response.status}.`);
        const data = await response.json();
        if (!response.ok)
            throw new Error(data.error || data.detail || `The API returned HTTP ${response.status}.`);
        return data;
    }
    async function loadCatalog() {
        byId('catalog-status').textContent = 'Connecting to the model catalog…';
        byId('reload-catalog').hidden = true;
        catalogReady = false;
        controls();
        try {
            const data = await fetchJson(api + '/v1/models');
            if (!Array.isArray(data.models) || !data.models.length)
                throw new Error('The API has no corpus classifiers in its catalog.');
            models = data.models;
            catalogReady = true;
            populateCatalog();
            const available = models.filter(m => m.available).length;
            byId('catalog-status').textContent = `${available} of ${models.length} models available`;
            byId('model-total').textContent = String(models.length);
            if (available < models.length)
                byId('reload-catalog').hidden = false;
        }
        catch (error) {
            models = Object.keys(modelInfo).map(model_id => ({ model_id, language: model_id.endsWith('_ro') ? 'ro' : 'en', available: false, labels: [] }));
            populateCatalog();
            byId('catalog-status').textContent = 'API connection unavailable';
            byId('reload-catalog').hidden = false;
            requestStatus('Could not connect to the corpus API. Check its configured address or reconnect. Examples remain editable.', 'error');
        }
        controls();
    }
    select.addEventListener('change', renderSelection);
    input.addEventListener('input', inputChanged);
    function loadExample(index) { input.value = examples[baseModelId(select.value)]?.[index] || ''; inputChanged(); input.focus(); }
    byId('example-one').addEventListener('click', () => loadExample(0));
    byId('example-two').addEventListener('click', () => loadExample(1));
    byId('reload-catalog').addEventListener('click', loadCatalog);
    byId('prediction-form').addEventListener('submit', async (event) => {
        event.preventDefault();
        const model = currentModel();
        if (busy || !model?.available || !catalogReady)
            return;
        if (!input.value.trim()) {
            requestStatus('Enter some text before predicting.', 'error');
            input.focus();
            return;
        }
        busy = true;
        controls();
        result.hidden = true;
        requestStatus('Predicting the label…');
        try {
            const data = await fetchJson(api + '/v1/predictions', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ model_id: model.model_id, text: input.value }) });
            if (typeof data.predicted_label !== 'string' || !data.predicted_label.trim())
                throw new Error('The model returned no prediction label.');
            byId('predicted-label').textContent = data.predicted_label;
            byId('label-meaning').textContent = meanings[data.predicted_label] || 'Label from the selected model’s vocabulary';
            byId('prediction-limit').textContent = data.research_limit || model.research_limit || fallbackLimit;
            byId('response-json').textContent = JSON.stringify(data, null, 2);
            result.hidden = false;
            requestStatus('Prediction complete.');
        }
        catch (error) {
            const message = error instanceof Error ? (error.name === 'TimeoutError' ? 'The API took too long to respond. Try again.' : error.name === 'TypeError' ? 'Could not reach the prediction API. Check the connection and try again.' : error.message) : 'Prediction failed. Try again.';
            requestStatus(message, 'error');
        }
        finally {
            busy = false;
            controls();
        }
    });
    loadCatalog();
})();
(() => {
    const api = String(window.TYM_CONFIG?.apiBaseUrl || 'http://127.0.0.1:8871').replace(/\/+$/, '');
    function element(id) {
        const found = document.getElementById(id);
        if (!found)
            throw new Error(`Missing workbench element: ${id}`);
        return found;
    }
    const sourceInput = element('document-text');
    const preserved = element('preserved-source');
    const language = element('document-language');
    const date = element('document-date');
    const status = element('document-status');
    let draft = null;
    let inventories = { tym: [], tlink: [], slink: [], alink: [] };
    let busy = false;
    let sequence = 0;
    let revision = 0;
    let exportReady = false;
    const originalExamples = {
        en: 'Yesterday, Mara arrived in Iași. She remembered the room where her brother had waited. Today she leaves for the mountains.',
        ro: 'Ieri, Mara a ajuns la Iași. Și-a amintit de camera în care fratele ei așteptase. Astăzi pleacă spre munte.'
    };
    function message(text, error = false) { status.textContent = text; status.dataset.tone = error ? 'error' : 'info'; }
    function errorText(error) { return error instanceof Error ? error.message : 'The operation failed. Try again.'; }
    function evidence(method, reason) { return { method, evidence: reason }; }
    function controls() {
        element('analyze-button').disabled = busy || !sourceInput.value.trim();
        element('document-example').disabled = busy;
        sourceInput.readOnly = busy;
        language.disabled = busy;
        date.disabled = busy;
        element('document-form').setAttribute('aria-busy', String(busy));
        for (const id of ['validate-document', 'add-segment', 'add-mention', 'add-relation'])
            element(id).disabled = busy || !draft;
        for (const field of element('document-results').querySelectorAll('input,select,button'))
            field.disabled = busy;
        element('export-document').disabled = busy || !exportReady;
    }
    async function request(path, body) {
        const response = await fetch(api + path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body), signal: AbortSignal.timeout(60000) });
        const contentType = response.headers.get('content-type') || '';
        if (!contentType.includes('json'))
            throw new Error(`The API returned HTTP ${response.status} without a JSON response.`);
        const result = await response.json();
        if (!response.ok)
            throw new Error(result.error || result.detail || `The API returned HTTP ${response.status}.`);
        return result;
    }
    /** Editing never promotes an annotation to reviewed. A separate user action does that. */
    function dirty() {
        if (!draft)
            return;
        draft.review_status = 'draft';
        revision++;
        exportReady = false;
        element('export-document').disabled = true;
        element('validation-state').textContent = 'Draft changed. Validate again to enable export.';
        element('document-review-state').textContent = 'Draft annotations';
        element('temporal-graph').replaceChildren();
        element('simultaneous-info').textContent = '';
        element('validation-diagnostics').replaceChildren();
        element('graph-scope').textContent = 'Validate to see the partial order from reviewed temporal relations. Disconnected annotations retain unknown order.';
    }
    function uniqueId(prefix) {
        const ids = new Set([...(draft?.segments || []), ...(draft?.mentions || []), ...(draft?.relations || [])].map(item => item.id));
        let id;
        do {
            id = `${prefix}${++sequence}`;
        } while (ids.has(id));
        return id;
    }
    function textNode(tag, text, className) {
        const node = document.createElement(tag);
        node.textContent = text;
        if (className)
            node.className = className;
        return node;
    }
    function cell(row) { const td = document.createElement('td'); row.append(td); return td; }
    function selectControl(values, selected, label, changed, includeBlank = false) {
        const select = document.createElement('select');
        select.setAttribute('aria-label', label);
        for (const value of includeBlank ? ['', ...values] : values) {
            const option = document.createElement('option');
            option.value = value;
            option.textContent = value || 'Unassigned';
            select.append(option);
        }
        if (selected && !values.includes(selected)) {
            const option = document.createElement('option');
            option.value = selected;
            option.textContent = selected;
            select.append(option);
        }
        select.value = selected || '';
        select.addEventListener('change', () => changed(select.value));
        return select;
    }
    function actionButton(text, label, action) {
        const button = textNode('button', text, 'text-button');
        button.type = 'button';
        button.setAttribute('aria-label', label);
        button.addEventListener('click', () => { if (!busy)
            action(); });
        return button;
    }
    function inspect(annotation) {
        if (!draft)
            return;
        element('annotation-evidence').textContent = JSON.stringify({ id: annotation.id, review_status: annotation.review_status, provenance: annotation.provenance }, null, 2);
        const preview = element('source-highlight');
        preview.replaceChildren();
        if ('start' in annotation) {
            preview.append(document.createTextNode(draft.text.slice(0, annotation.start)));
            preview.append(textNode('mark', draft.text.slice(annotation.start, annotation.end)));
            preview.append(document.createTextNode(draft.text.slice(annotation.end)));
            preserved.setSelectionRange(annotation.start, annotation.end);
            updateSelection();
        }
        else
            preview.append(textNode('p', `${annotation.family}: ${annotation.from_id} → ${annotation.to_id} (${annotation.label})`));
    }
    function reviewCell(row, annotation) {
        const td = cell(row);
        td.append(actionButton(annotation.review_status === 'reviewed' ? 'Reviewed ✓' : 'Mark reviewed', `Review ${annotation.id}`, () => {
            annotation.review_status = annotation.review_status === 'reviewed' ? 'draft' : 'reviewed';
            annotation.provenance['review_status'] = evidence('user_supplied', 'Explicit reviewer decision; not independently adjudicated.');
            dirty();
            renderAnnotations();
            inspect(annotation);
        }));
    }
    function markCorrected(annotation, field) {
        annotation.review_status = 'draft';
        annotation.provenance[field] = evidence('human_corrected', 'Edited in the document review workspace.');
        annotation.provenance['review_status'] = evidence('human_corrected', 'Editing returned this annotation to draft; a new review is required.');
        dirty();
    }
    function spanCells(row, annotation) {
        cell(row).append(actionButton(`${annotation.id} [${annotation.start}, ${annotation.end})`, `Inspect ${annotation.id}`, () => inspect(annotation)));
        cell(row).append(textNode('span', annotation.text, 'annotation-text'));
    }
    function renderAnnotations() {
        if (!draft)
            return;
        const segments = element('segment-rows');
        segments.replaceChildren();
        for (const segment of draft.segments) {
            const row = document.createElement('tr');
            spanCells(row, segment);
            cell(row).append(selectControl(['NAR', 'REM', 'SUP', 'GEN', 'FIC'], segment.label, `Narrative mode for ${segment.id}`, value => { segment.label = value || null; markCorrected(segment, 'label'); renderAnnotations(); }, true));
            reviewCell(row, segment);
            cell(row).append(actionButton('Remove', `Remove ${segment.id}`, () => { if (draft)
                draft.segments = draft.segments.filter(item => item.id !== segment.id); dirty(); renderAnnotations(); }));
            segments.append(row);
        }
        if (!draft.segments.length)
            appendEmpty(segments, 5, 'No time segments. Select a source span to add one.');
        const mentions = element('mention-rows');
        mentions.replaceChildren();
        for (const mention of draft.mentions) {
            const row = document.createElement('tr');
            spanCells(row, mention);
            cell(row).append(selectControl(['TIME', 'ACTOR', 'LOCATION', 'EVENT'], mention.kind, `Kind for ${mention.id}`, value => { mention.kind = value; markCorrected(mention, 'kind'); renderAnnotations(); }));
            const normalized = document.createElement('input');
            normalized.type = 'text';
            normalized.value = mention.normalized_value || '';
            normalized.setAttribute('aria-label', `Normalized value for ${mention.id}`);
            normalized.addEventListener('change', () => { mention.normalized_value = normalized.value.trim() || null; markCorrected(mention, 'normalized_value'); renderAnnotations(); });
            cell(row).append(normalized);
            reviewCell(row, mention);
            cell(row).append(actionButton('Remove', `Remove ${mention.id}`, () => { if (draft)
                draft.mentions = draft.mentions.filter(item => item.id !== mention.id); dirty(); renderAnnotations(); }));
            mentions.append(row);
        }
        if (!draft.mentions.length)
            appendEmpty(mentions, 6, 'No candidate mentions. Select a source span to add one.');
        const relations = element('relation-rows');
        relations.replaceChildren();
        for (const relation of draft.relations) {
            const row = document.createElement('tr');
            cell(row).append(actionButton(`${relation.id} · ${relation.family}`, `Inspect ${relation.id}`, () => inspect(relation)));
            cell(row).textContent = `${relation.from_id} → ${relation.to_id}`;
            cell(row).append(selectControl(relationLabels(relation.family), relation.label, `Label for ${relation.id}`, value => { relation.label = value; markCorrected(relation, 'label'); renderAnnotations(); }));
            reviewCell(row, relation);
            cell(row).append(actionButton('Remove', `Remove ${relation.id}`, () => { if (draft)
                draft.relations = draft.relations.filter(item => item.id !== relation.id); dirty(); renderAnnotations(); }));
            relations.append(row);
        }
        if (!draft.relations.length)
            appendEmpty(relations, 5, 'No relations are inferred automatically. Add a directed draft relation after inspecting its endpoints.');
        relationOptions();
    }
    function appendEmpty(body, columns, text) { const row = document.createElement('tr'); const td = cell(row); td.colSpan = columns; td.textContent = text; body.append(row); }
    function relationLabels(family) { return inventories[family.toLowerCase()] || []; }
    function relationEndpoints(family) {
        if (!draft)
            return [];
        return family === 'TYM' ? draft.segments : draft.mentions.filter(mention => family === 'TLINK' ? ['EVENT', 'TIME'].includes(mention.kind) : mention.kind === 'EVENT');
    }
    function relationOptions() {
        const family = element('relation-family').value;
        const endpoints = relationEndpoints(family);
        for (const id of ['relation-from', 'relation-to']) {
            const select = element(id), selected = select.value;
            select.replaceChildren();
            for (const endpoint of endpoints) {
                const option = document.createElement('option');
                option.value = endpoint.id;
                option.textContent = `${endpoint.id} · ${endpoint.text.slice(0, 48)}`;
                select.append(option);
            }
            if (endpoints.some(item => item.id === selected))
                select.value = selected;
        }
        const label = element('relation-label'), selected = label.value;
        label.replaceChildren();
        for (const value of relationLabels(family)) {
            const option = document.createElement('option');
            option.value = value;
            option.textContent = value;
            label.append(option);
        }
        if (relationLabels(family).includes(selected))
            label.value = selected;
        element('add-relation').disabled = busy || endpoints.length < 2 || !label.value;
    }
    function updateSelection() { element('selection-info').textContent = preserved.selectionStart === preserved.selectionEnd ? 'Select a source span.' : `Selected [${preserved.selectionStart}, ${preserved.selectionEnd})`; }
    function selectedSpan() {
        if (!draft)
            return null;
        const start = preserved.selectionStart, end = preserved.selectionEnd;
        if (end <= start || !draft.text.slice(start, end).trim()) {
            message('Select a nonempty span in the preserved source first.', true);
            return null;
        }
        return { start, end, text: draft.text.slice(start, end) };
    }
    function showDiagnostics(diagnostics) {
        const container = element('validation-diagnostics');
        container.replaceChildren();
        if (!diagnostics.length) {
            container.append(textNode('p', 'No validation diagnostics. This checks consistency, not annotation accuracy.'));
            return;
        }
        const list = document.createElement('ul');
        for (const diagnostic of diagnostics) {
            const item = textNode('li', `${diagnostic.severity}: ${diagnostic.code}${diagnostic.annotation_id ? ` (${diagnostic.annotation_id})` : ''} — ${diagnostic.message}`);
            item.dataset.severity = diagnostic.severity;
            list.append(item);
        }
        container.append(list);
    }
    /** Graph layout conveys explicit edges only; geometric placement is not a reconstructed timeline. */
    function renderGraph(validation) {
        const graph = element('temporal-graph');
        graph.replaceChildren();
        const order = validation.story_order;
        element('graph-scope').textContent = `${order.status.toUpperCase()} order. ${order.scope} Layout positions do not imply chronology.`;
        element('simultaneous-info').textContent = order.simultaneous_groups.length ? `Reviewed simultaneous groups: ${order.simultaneous_groups.map(group => group.join(', ')).join('; ')}` : 'No reviewed simultaneous groups.';
        if (!order.precedence_edges.length) {
            graph.append(textNode('p', 'No reviewed precedence edges. Story order remains unknown for unlinked annotations.'));
            return;
        }
        const ids = Array.from(new Set(order.precedence_edges.flatMap(edge => [edge.from_id, edge.to_id])));
        const shownIds = ids.slice(0, 40);
        const ns = 'http://www.w3.org/2000/svg';
        const svg = document.createElementNS(ns, 'svg');
        svg.setAttribute('viewBox', '0 0 760 460');
        svg.setAttribute('role', 'img');
        svg.setAttribute('aria-label', `Reviewed partial temporal graph with ${ids.length} connected annotations. See edge list below.`);
        const title = document.createElementNS(ns, 'title');
        title.textContent = 'Reviewed partial temporal relation graph';
        svg.append(title);
        const defs = document.createElementNS(ns, 'defs'), marker = document.createElementNS(ns, 'marker');
        marker.id = 'temporal-arrow';
        marker.setAttribute('markerWidth', '8');
        marker.setAttribute('markerHeight', '8');
        marker.setAttribute('refX', '7');
        marker.setAttribute('refY', '4');
        marker.setAttribute('orient', 'auto');
        const arrow = document.createElementNS(ns, 'path');
        arrow.setAttribute('d', 'M0,0 L8,4 L0,8 Z');
        arrow.setAttribute('fill', order.status === 'inconsistent' ? '#9b342b' : '#147a7d');
        marker.append(arrow);
        defs.append(marker);
        svg.append(defs);
        const positions = new Map();
        shownIds.forEach((id, index) => { const angle = 2 * Math.PI * index / shownIds.length - Math.PI / 2; positions.set(id, { x: 380 + 285 * Math.cos(angle), y: 230 + 175 * Math.sin(angle) }); });
        for (const edge of order.precedence_edges) {
            const from = positions.get(edge.from_id), to = positions.get(edge.to_id);
            if (!from || !to)
                continue;
            const dx = to.x - from.x, dy = to.y - from.y, length = Math.hypot(dx, dy) || 1;
            const line = document.createElementNS(ns, 'line');
            line.setAttribute('x1', String(from.x + 35 * dx / length));
            line.setAttribute('y1', String(from.y + 20 * dy / length));
            line.setAttribute('x2', String(to.x - 39 * dx / length));
            line.setAttribute('y2', String(to.y - 24 * dy / length));
            line.setAttribute('stroke', order.status === 'inconsistent' ? '#9b342b' : '#147a7d');
            line.setAttribute('stroke-width', '2');
            line.setAttribute('marker-end', 'url(#temporal-arrow)');
            svg.append(line);
        }
        for (const [id, position] of positions) {
            const rect = document.createElementNS(ns, 'rect');
            rect.setAttribute('x', String(position.x - 42));
            rect.setAttribute('y', String(position.y - 17));
            rect.setAttribute('width', '84');
            rect.setAttribute('height', '34');
            rect.setAttribute('rx', '4');
            rect.setAttribute('fill', '#edf5f5');
            rect.setAttribute('stroke', '#147a7d');
            svg.append(rect);
            const text = document.createElementNS(ns, 'text');
            text.setAttribute('x', String(position.x));
            text.setAttribute('y', String(position.y + 5));
            text.setAttribute('text-anchor', 'middle');
            text.setAttribute('fill', '#172f43');
            text.setAttribute('font-size', '13');
            text.textContent = id.slice(0, 15);
            svg.append(text);
        }
        graph.append(svg);
        if (ids.length > shownIds.length)
            graph.append(textNode('p', 'Diagram displays the first 40 connected annotations. The complete edge list is below.'));
        const list = document.createElement('ul');
        list.className = 'graph-edge-list';
        for (const edge of order.precedence_edges)
            list.append(textNode('li', `${edge.from_id} → ${edge.to_id} · ${edge.family} · ${edge.relation_id}`));
        graph.append(list);
        if (draft) {
            const disconnected = [...draft.segments, ...draft.mentions].filter(item => !ids.includes(item.id));
            graph.append(textNode('p', `${disconnected.length} annotations are disconnected from precedence edges; their chronological order is unknown.`));
        }
    }
    sourceInput.value = originalExamples['en'];
    sourceInput.addEventListener('input', controls);
    language.addEventListener('change', () => { sourceInput.lang = language.value; });
    element('document-example').addEventListener('click', () => { sourceInput.value = originalExamples[language.value] || originalExamples['en']; sourceInput.lang = language.value; controls(); });
    preserved.addEventListener('select', updateSelection);
    preserved.addEventListener('keyup', updateSelection);
    preserved.addEventListener('mouseup', updateSelection);
    element('relation-family').addEventListener('change', relationOptions);
    element('document-form').addEventListener('submit', async (event) => {
        event.preventDefault();
        if (busy || !sourceInput.value.trim())
            return;
        busy = true;
        controls();
        message('Analyzing candidate annotations…');
        try {
            const analysis = await request('/v1/documents/analyze', { text: sourceInput.value, language: language.value, document_date: date.value || null });
            if (analysis.document.text !== sourceInput.value || analysis.document.offset_encoding !== 'utf-16')
                throw new Error('The API did not preserve the source text and UTF-16 offset contract.');
            draft = analysis.document;
            inventories = analysis.relation_inventories;
            sequence = 0;
            preserved.value = draft.text;
            preserved.lang = draft.language;
            element('document-id').textContent = draft.document_id;
            element('document-offset-info').textContent = `${draft.text.length.toLocaleString()} UTF-16 code units · [start, end)`;
            element('document-limit').textContent = analysis.research_limit;
            element('document-results').hidden = false;
            element('source-highlight').textContent = draft.text;
            element('annotation-evidence').textContent = 'Select an annotation to inspect its field provenance.';
            dirty();
            renderAnnotations();
            showDiagnostics(analysis.diagnostics);
            message(analysis.segment_model_available ? 'Candidate annotations ready. Inspect and review each field.' : 'Rule candidates ready. The segment model is unavailable; narrative labels remain unassigned.');
        }
        catch (error) {
            message(errorText(error), true);
        }
        finally {
            busy = false;
            controls();
            relationOptions();
        }
    });
    element('add-segment').addEventListener('click', () => {
        const span = selectedSpan();
        if (!span || !draft)
            return;
        const annotation = { id: uniqueId('ts'), ...span, label: null, review_status: 'draft', provenance: { start: evidence('user_supplied', 'Inclusive UTF-16 start selected by the reviewer.'), end: evidence('user_supplied', 'Exclusive UTF-16 end selected by the reviewer.'), text: evidence('source_slice', 'Exact substring of preserved source.'), label: evidence('user_supplied', 'Narrative mode not yet assigned.'), review_status: evidence('user_supplied', 'New annotation starts in draft; no review has been performed.') } };
        draft.segments.push(annotation);
        dirty();
        renderAnnotations();
        inspect(annotation);
    });
    element('add-mention').addEventListener('click', () => {
        const span = selectedSpan();
        if (!span || !draft)
            return;
        const annotation = { id: uniqueId('m'), ...span, kind: element('new-mention-kind').value, normalized_value: null, review_status: 'draft', provenance: { start: evidence('user_supplied', 'Inclusive UTF-16 start selected by the reviewer.'), end: evidence('user_supplied', 'Exclusive UTF-16 end selected by the reviewer.'), text: evidence('source_slice', 'Exact substring of preserved source.'), kind: evidence('user_supplied', 'Mention kind selected by the reviewer.'), normalized_value: evidence('user_supplied', 'No normalized value has been assigned.'), review_status: evidence('user_supplied', 'New annotation starts in draft; no review has been performed.') } };
        draft.mentions.push(annotation);
        dirty();
        renderAnnotations();
        inspect(annotation);
    });
    element('relation-form').addEventListener('submit', event => {
        event.preventDefault();
        if (!draft || busy)
            return;
        const family = element('relation-family').value, from_id = element('relation-from').value, to_id = element('relation-to').value, label = element('relation-label').value;
        if (!from_id || !to_id || from_id === to_id || !label) {
            message('Choose two distinct endpoints and a relation label.', true);
            return;
        }
        draft.relations.push({ id: uniqueId('r'), family, from_id, to_id, label, review_status: 'draft', provenance: { family: evidence('user_supplied', 'Relation schema selected by the reviewer.'), from_id: evidence('user_supplied', 'Source endpoint selected by the reviewer.'), to_id: evidence('user_supplied', 'Target endpoint selected by the reviewer.'), label: evidence('user_supplied', 'Label selected from the family inventory.'), review_status: evidence('user_supplied', 'New relation starts in draft; no review has been performed.') } });
        dirty();
        renderAnnotations();
        message('Draft relation added. Review it explicitly before using it in the temporal graph.');
    });
    element('validate-document').addEventListener('click', async () => {
        if (!draft || busy)
            return;
        const validatingRevision = revision;
        busy = true;
        exportReady = false;
        controls();
        message('Validating annotation consistency…');
        try {
            const validation = await request('/v1/documents/validate', draft);
            if (revision !== validatingRevision) {
                message('Draft changed during validation. Validate the current draft again.', true);
                return;
            }
            inventories = validation.relation_inventories;
            showDiagnostics(validation.diagnostics);
            renderGraph(validation);
            element('validation-state').textContent = validation.valid ? 'Consistent annotation structure. Export is available; this is not an accuracy score.' : 'Validation failed. Correct the diagnostics before exporting.';
            exportReady = validation.valid;
            element('export-document').disabled = !exportReady;
            message(validation.valid ? 'Validation complete.' : 'Validation found inconsistent or invalid annotations.', !validation.valid);
        }
        catch (error) {
            message(errorText(error), true);
        }
        finally {
            busy = false;
            controls();
            relationOptions();
        }
    });
    element('export-document').addEventListener('click', () => {
        if (!draft || element('export-document').disabled)
            return;
        const blob = new Blob([JSON.stringify(draft, null, 2)], { type: 'application/json' });
        const url = URL.createObjectURL(blob), link = document.createElement('a');
        link.href = url;
        link.download = `${draft.document_id.replace(/[^a-zA-Z0-9_-]/g, '_')}-annotations.json`;
        link.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    });
    controls();
})();
/** Loads aggregate research evidence independently of the prediction/document workbenches.
 * All report labels, scopes, metric keys and notes are rendered as text, never HTML. */
(() => {
    if (!document.getElementById('algorithm-comparison'))
        return;
    const familyOrder = ['linear_regression', 'logistic_regression', 'decision_tree', 'random_forest',
        'gradient_boosting', 'svm', 'knn', 'naive_bayes', 'kmeans', 'hierarchical_clustering', 'pca',
        'mlp', 'cnn', 'rnn', 'transformer', 'autoencoder', 'dbscan'];
    const bookFamilies = new Set(['kmeans', 'hierarchical_clustering', 'pca', 'autoencoder', 'dbscan']);
    class ReportError extends Error {
    }
    const invalid = () => { throw new ReportError('The comparison summary is incomplete or has an invalid format. Reload it after a complete report is published.'); };
    function object(value) {
        if (value === null || typeof value !== 'object' || Array.isArray(value))
            return invalid();
        return value;
    }
    function text(value, maximum = 250) {
        if (typeof value !== 'string' || !value.trim() || value.length > maximum)
            return invalid();
        return value;
    }
    function nonnegative(value) {
        if (typeof value !== 'number' || !Number.isFinite(value) || value < 0)
            return invalid();
        return value;
    }
    function fraction(value) {
        const result = nonnegative(value);
        return result <= 1 ? result : invalid();
    }
    function notes(value) {
        if (!Array.isArray(value) || value.length > 50)
            return invalid();
        return value.map(note => text(note, 6000));
    }
    function approach(value) {
        const row = object(value), family = text(row.family);
        if (!familyOrder.includes(family) || row.language !== 'en' && row.language !== 'ro')
            return invalid();
        return { family: family, family_label: text(row.family_label), algorithm: text(row.algorithm),
            algorithm_label: text(row.algorithm_label), language: row.language, notes: notes(row.notes) };
    }
    function classification(value) {
        const row = object(value), base = approach(row), count = nonnegative(row.rows);
        if (bookFamilies.has(base.family) || count < 1 || !Number.isSafeInteger(count))
            return invalid();
        return { ...base, task: text(row.task), accuracy_fraction: fraction(row.accuracy_fraction),
            macro_f1_fraction: fraction(row.macro_f1_fraction), total_training_seconds: nonnegative(row.total_training_seconds),
            total_prediction_seconds: nonnegative(row.total_prediction_seconds), prediction_ms_per_row: nonnegative(row.prediction_ms_per_row),
            batched_rows_per_second: nonnegative(row.batched_rows_per_second), rows: count };
    }
    function book(value) {
        const row = object(value), base = approach(row), sourceMetrics = object(row.metrics);
        if (!bookFamilies.has(base.family) || row.semantic_accuracy !== null || Object.keys(sourceMetrics).length > 60)
            return invalid();
        const metrics = Object.create(null);
        for (const [key, value] of Object.entries(sourceMetrics)) {
            text(key, 150);
            if (value !== null && (typeof value !== 'number' || !Number.isFinite(value)))
                return invalid();
            metrics[key] = value;
        }
        return { ...base, training_seconds: nonnegative(row.training_seconds), assessment_seconds: nonnegative(row.assessment_seconds),
            semantic_accuracy: null, metrics, scope: text(row.scope, 6000) };
    }
    function parseSummary(value) {
        const data = object(value), coverage = object(data.coverage);
        if (data.status !== 'completed_algorithm_comparison' || coverage.families !== 17
            || data.schema_version !== undefined && data.schema_version !== 1
            || !Array.isArray(data.classification_rows) || data.classification_rows.length < 1 || data.classification_rows.length > 5000
            || !Array.isArray(data.books_rows) || data.books_rows.length < 1 || data.books_rows.length > 500)
            return invalid();
        const classificationRows = data.classification_rows.map(classification), bookRows = data.books_rows.map(book);
        const families = new Set([...classificationRows, ...bookRows].map(row => row.family));
        const primary = text(data.classification_primary_task);
        if (families.size !== 17 || familyOrder.some(family => !families.has(family))
            || !classificationRows.some(row => row.task === primary))
            return invalid();
        const identities = new Set();
        for (const row of classificationRows) {
            const key = JSON.stringify([row.task, row.language, row.algorithm]);
            if (identities.has(key))
                return invalid();
            identities.add(key);
        }
        identities.clear();
        for (const row of bookRows) {
            const key = JSON.stringify([row.language, row.algorithm]);
            if (identities.has(key))
                return invalid();
            identities.add(key);
        }
        return { classification_primary_task: primary, classification_rows: classificationRows, books_rows: bookRows };
    }
    function element(id) {
        const result = document.getElementById(id);
        if (!result)
            throw new Error('Missing comparison element: ' + id);
        return result;
    }
    const section = element('algorithm-comparison'), status = element('comparison-status');
    const results = element('comparison-results'), retry = element('comparison-retry');
    const task = element('comparison-task'), language = element('comparison-language');
    let summary = null, loading = false;
    const taskNames = {
        timebank_event_class: 'Event class', timebank_event_tense: 'Event tense', timebank_timex_type: 'Time-expression type',
        timebank_tlink: 'TLINK · temporal relations', timebank_slink: 'SLINK · subordinate relations', timebank_alink: 'ALINK · aspectual relations'
    };
    const languageName = (value) => value === 'ro' ? 'Romanian' : value === 'en' ? 'English' : value;
    const seconds = (value) => value.toLocaleString(undefined, { minimumFractionDigits: value < 1 ? 3 : 2, maximumFractionDigits: value < 1 ? 3 : 2 });
    const percent = (value) => (value * 100).toFixed(2) + '%';
    const decimal = (value) => value.toLocaleString(undefined, { maximumFractionDigits: 3 });
    const metricName = (key) => key.replace(/_/g, ' ').replace(/^./, letter => letter.toUpperCase());
    const metricValue = (value) => value === null ? 'N/A' : value !== 0 && Math.abs(value) < 0.0001
        ? value.toExponential(3) : value.toLocaleString(undefined, { maximumFractionDigits: 5 });
    function cell(row, value, className = '') {
        const result = document.createElement('td');
        result.textContent = value;
        result.className = className;
        row.append(result);
        return result;
    }
    function nameCell(row, approach) {
        const result = cell(row, ''), family = document.createElement('strong'), algorithm = document.createElement('span');
        family.className = 'comparison-family';
        family.textContent = approach.family_label;
        algorithm.className = 'comparison-approach';
        algorithm.textContent = approach.algorithm_label;
        result.append(family, algorithm);
    }
    function renderNotes(parent, values) {
        if (!values.length)
            return;
        const details = document.createElement('details'), heading = document.createElement('summary');
        heading.textContent = 'Interpretation notes';
        details.append(heading);
        for (const note of values) {
            const paragraph = document.createElement('p');
            paragraph.textContent = note;
            details.append(paragraph);
        }
        parent.append(details);
    }
    function emptyRow(body, columns, message) {
        const row = document.createElement('tr'), messageCell = cell(row, message, 'comparison-empty');
        messageCell.colSpan = columns;
        body.append(row);
    }
    function order(rows) {
        return rows.sort((left, right) => familyOrder.indexOf(left.family) - familyOrder.indexOf(right.family)
            || left.algorithm_label.localeCompare(right.algorithm_label) || left.language.localeCompare(right.language));
    }
    function render() {
        if (!summary)
            return;
        const selectedLanguage = language.value;
        const classificationRows = order(summary.classification_rows.filter(row => row.task === task.value && (selectedLanguage === 'all' || row.language === selectedLanguage)));
        const bookRows = order(summary.books_rows.filter(row => selectedLanguage === 'all' || row.language === selectedLanguage));
        const classBody = element('comparison-classification-rows'), bookBody = element('comparison-book-rows');
        classBody.replaceChildren();
        bookBody.replaceChildren();
        for (const result of classificationRows) {
            const row = document.createElement('tr');
            nameCell(row, result);
            cell(row, languageName(result.language));
            cell(row, percent(result.accuracy_fraction), 'comparison-number');
            cell(row, percent(result.macro_f1_fraction), 'comparison-number');
            cell(row, seconds(result.total_training_seconds), 'comparison-number');
            cell(row, seconds(result.total_prediction_seconds), 'comparison-number');
            cell(row, decimal(result.prediction_ms_per_row), 'comparison-number');
            const noteCell = cell(row, '', 'comparison-note'), count = document.createElement('p');
            count.textContent = result.rows.toLocaleString() + ' validation rows · ' + decimal(result.batched_rows_per_second) + ' rows/s in batch';
            noteCell.append(count);
            renderNotes(noteCell, result.notes);
            classBody.append(row);
        }
        if (!classificationRows.length)
            emptyRow(classBody, 8, 'No published classification results for this task and language.');
        for (const result of bookRows) {
            const row = document.createElement('tr');
            nameCell(row, result);
            cell(row, languageName(result.language));
            cell(row, seconds(result.training_seconds), 'comparison-number');
            cell(row, seconds(result.assessment_seconds), 'comparison-number');
            cell(row, 'N/A · unlabeled passages');
            const diagnostic = cell(row, '', 'comparison-note'), scope = document.createElement('p');
            scope.textContent = result.scope;
            diagnostic.append(scope);
            const metrics = document.createElement('dl');
            metrics.className = 'comparison-metrics';
            for (const [key, value] of Object.entries(result.metrics)) {
                const entry = document.createElement('div'), name = document.createElement('dt'), number = document.createElement('dd');
                name.textContent = metricName(key);
                number.textContent = metricValue(value);
                entry.append(name, number);
                metrics.append(entry);
            }
            diagnostic.append(metrics);
            renderNotes(diagnostic, result.notes);
            bookBody.append(row);
        }
        if (!bookRows.length)
            emptyRow(bookBody, 6, 'No published book exploration results for this language.');
        element('comparison-classification-summary').textContent = `${classificationRows.length} approaches shown for ${taskNames[task.value] || task.value}. Each score is conditional on the supplied annotations.`;
        element('comparison-books-summary').textContent = `${bookRows.length} exploration results shown. The task filter applies to classification only; the language filter applies to both tables.`;
    }
    function populate(data) {
        const tasks = [...new Set(data.classification_rows.map(row => row.task))].sort();
        task.replaceChildren();
        for (const value of tasks) {
            const option = document.createElement('option');
            option.value = value;
            option.textContent = taskNames[value] || value;
            task.append(option);
        }
        task.value = data.classification_primary_task;
        language.replaceChildren();
        const all = document.createElement('option');
        all.value = 'all';
        all.textContent = 'All languages';
        language.append(all);
        for (const value of [...new Set([...data.classification_rows, ...data.books_rows].map(row => row.language))].sort()) {
            const option = document.createElement('option');
            option.value = value;
            option.textContent = languageName(value);
            language.append(option);
        }
        language.value = 'all';
        const classificationVariants = new Set(data.classification_rows.map(row => row.algorithm)).size;
        const bookVariants = new Set(data.books_rows.map(row => row.algorithm)).size;
        element('comparison-coverage').textContent = `17 algorithm families · ${classificationVariants} classification approaches across ${tasks.length} tasks · ${bookVariants} book approaches. Expanded algorithm selection is exploratory; no independent selection holdout was used.`;
    }
    async function load() {
        if (loading)
            return;
        loading = true;
        section.setAttribute('aria-busy', 'true');
        results.hidden = true;
        retry.hidden = true;
        task.disabled = language.disabled = true;
        status.dataset.tone = 'info';
        status.textContent = 'Loading the comparison summary…';
        try {
            const response = await fetch('/results/algorithm-comparison.json', { mode: 'same-origin', credentials: 'same-origin', signal: AbortSignal.timeout(30000) });
            if (!response.ok)
                throw new ReportError(response.status === 404 ? 'The comparison has not been published yet. Try reloading later.' : 'The comparison summary could not be loaded. Try reloading it.');
            if (!(response.headers.get('content-type') || '').toLowerCase().includes('json'))
                invalid();
            const length = response.headers.get('content-length');
            if (length && (!Number.isFinite(Number(length)) || Number(length) < 0 || Number(length) > 2000000))
                invalid();
            const body = await response.text();
            if (body.length > 2000000)
                invalid();
            summary = parseSummary(JSON.parse(body));
            populate(summary);
            render();
            results.hidden = false;
            task.disabled = language.disabled = false;
            status.textContent = 'Published comparison loaded.';
        }
        catch (error) {
            summary = null;
            status.dataset.tone = 'error';
            retry.hidden = false;
            status.textContent = error instanceof ReportError ? error.message : error instanceof Error && (error.name === 'TimeoutError' || error.name === 'AbortError')
                ? 'The comparison took too long to load. Try reloading it.' : 'The comparison summary is unavailable or has an invalid format. Try reloading later.';
        }
        finally {
            loading = false;
            section.setAttribute('aria-busy', 'false');
        }
    }
    task.addEventListener('change', render);
    language.addEventListener('change', render);
    retry.addEventListener('click', () => { void load(); });
    void load();
})();
