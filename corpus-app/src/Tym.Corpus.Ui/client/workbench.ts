/** Document review operates on unchanged UTF-16 source offsets. All DOM writes use textContent. */
interface FieldEvidence { method: string; evidence: string; model_id?: string | null; }
type EvidenceMap = Record<string, FieldEvidence>;
interface SpanAnnotation { id: string; start: number; end: number; text: string; review_status: string; provenance: EvidenceMap; }
interface TimeSegment extends SpanAnnotation { label: string | null; }
interface SourceMention extends SpanAnnotation { kind: string; normalized_value: string | null; }
interface DirectedRelation { id: string; family: string; from_id: string; to_id: string; label: string; review_status: string; provenance: EvidenceMap; }
interface CorpusDocument {
  schema_version: number; document_id: string; text: string; text_sha256: string; offset_encoding: string;
  language: string; document_date: string | null; review_status: string;
  segments: TimeSegment[]; mentions: SourceMention[]; relations: DirectedRelation[];
}
interface AnnotationDiagnostic { severity: string; code: string; message: string; annotation_id?: string | null; }
interface RelationInventories { tym: string[]; tlink: string[]; slink: string[]; alink: string[]; }
interface DocumentAnalysis { document: CorpusDocument; segment_model_available: boolean; relation_inventories: RelationInventories; diagnostics: AnnotationDiagnostic[]; research_limit: string; }
interface PrecedenceEdge { from_id: string; to_id: string; family: string; relation_id: string; }
interface GraphValidation { valid: boolean; diagnostics: AnnotationDiagnostic[]; story_order: { status: string; precedence_edges: PrecedenceEdge[]; simultaneous_groups: string[][]; scope: string; }; relation_inventories: RelationInventories; research_limit: string; }

(() => {
  const api = String(window.TYM_CONFIG?.apiBaseUrl || 'http://127.0.0.1:8871').replace(/\/+$/, '');
  function element<T extends HTMLElement = HTMLElement>(id: string): T {
    const found = document.getElementById(id);
    if (!found) throw new Error(`Missing workbench element: ${id}`);
    return found as T;
  }
  const sourceInput = element<HTMLTextAreaElement>('document-text');
  const preserved = element<HTMLTextAreaElement>('preserved-source');
  const language = element<HTMLSelectElement>('document-language');
  const date = element<HTMLInputElement>('document-date');
  const status = element('document-status');
  let draft: CorpusDocument | null = null;
  let inventories: RelationInventories = { tym: [], tlink: [], slink: [], alink: [] };
  let busy = false;
  let sequence = 0;
  let revision = 0;
  let exportReady = false;
  const originalExamples: Record<string, string> = {
    en: 'Yesterday, Mara arrived in Iași. She remembered the room where her brother had waited. Today she leaves for the mountains.',
    ro: 'Ieri, Mara a ajuns la Iași. Și-a amintit de camera în care fratele ei așteptase. Astăzi pleacă spre munte.'
  };
  function message(text: string, error = false): void { status.textContent = text; status.dataset.tone = error ? 'error' : 'info'; }
  function errorText(error: unknown): string { return error instanceof Error ? error.message : 'The operation failed. Try again.'; }
  function evidence(method: string, reason: string): FieldEvidence { return { method, evidence: reason }; }
  function controls(): void {
    element<HTMLButtonElement>('analyze-button').disabled = busy || !sourceInput.value.trim();
    element<HTMLButtonElement>('document-example').disabled = busy;
    sourceInput.readOnly = busy; language.disabled = busy; date.disabled = busy;
    element('document-form').setAttribute('aria-busy', String(busy));
    for (const id of ['validate-document', 'add-segment', 'add-mention', 'add-relation']) element<HTMLButtonElement>(id).disabled = busy || !draft;
    for (const field of element('document-results').querySelectorAll<HTMLInputElement | HTMLSelectElement | HTMLButtonElement>('input,select,button')) field.disabled = busy;
    element<HTMLButtonElement>('export-document').disabled = busy || !exportReady;
  }
  async function request<T>(path: string, body: unknown): Promise<T> {
    const response = await fetch(api + path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body), signal: AbortSignal.timeout(60000) });
    const contentType = response.headers.get('content-type') || '';
    if (!contentType.includes('json')) throw new Error(`The API returned HTTP ${response.status} without a JSON response.`);
    const result = await response.json() as { error?: string; detail?: string };
    if (!response.ok) throw new Error(result.error || result.detail || `The API returned HTTP ${response.status}.`);
    return result as T;
  }
  /** Editing never promotes an annotation to reviewed. A separate user action does that. */
  function dirty(): void {
    if (!draft) return;
    draft.review_status = 'draft';
    revision++;
    exportReady = false;
    element<HTMLButtonElement>('export-document').disabled = true;
    element('validation-state').textContent = 'Draft changed. Validate again to enable export.';
    element('document-review-state').textContent = 'Draft annotations';
    element('temporal-graph').replaceChildren();
    element('simultaneous-info').textContent = '';
    element('validation-diagnostics').replaceChildren();
    element('graph-scope').textContent = 'Validate to see the partial order from reviewed temporal relations. Disconnected annotations retain unknown order.';
  }
  function uniqueId(prefix: string): string {
    const ids = new Set([...(draft?.segments || []), ...(draft?.mentions || []), ...(draft?.relations || [])].map(item => item.id));
    let id: string;
    do { id = `${prefix}${++sequence}`; } while (ids.has(id));
    return id;
  }
  function textNode<K extends keyof HTMLElementTagNameMap>(tag: K, text: string, className?: string): HTMLElementTagNameMap[K] {
    const node = document.createElement(tag); node.textContent = text;
    if (className) node.className = className;
    return node;
  }
  function cell(row: HTMLTableRowElement): HTMLTableCellElement { const td = document.createElement('td'); row.append(td); return td; }
  function selectControl(values: string[], selected: string | null, label: string, changed: (value: string) => void, includeBlank = false): HTMLSelectElement {
    const select = document.createElement('select'); select.setAttribute('aria-label', label);
    for (const value of includeBlank ? ['', ...values] : values) {
      const option = document.createElement('option'); option.value = value; option.textContent = value || 'Unassigned'; select.append(option);
    }
    if (selected && !values.includes(selected)) { const option = document.createElement('option'); option.value = selected; option.textContent = selected; select.append(option); }
    select.value = selected || '';
    select.addEventListener('change', () => changed(select.value));
    return select;
  }
  function actionButton(text: string, label: string, action: () => void): HTMLButtonElement {
    const button = textNode('button', text, 'text-button'); button.type = 'button'; button.setAttribute('aria-label', label); button.addEventListener('click', () => { if (!busy) action(); }); return button;
  }
  function inspect(annotation: SpanAnnotation | DirectedRelation): void {
    if (!draft) return;
    element('annotation-evidence').textContent = JSON.stringify({ id: annotation.id, review_status: annotation.review_status, provenance: annotation.provenance }, null, 2);
    const preview = element('source-highlight'); preview.replaceChildren();
    if ('start' in annotation) {
      preview.append(document.createTextNode(draft.text.slice(0, annotation.start)));
      preview.append(textNode('mark', draft.text.slice(annotation.start, annotation.end)));
      preview.append(document.createTextNode(draft.text.slice(annotation.end)));
      preserved.setSelectionRange(annotation.start, annotation.end); updateSelection();
    } else preview.append(textNode('p', `${annotation.family}: ${annotation.from_id} → ${annotation.to_id} (${annotation.label})`));
  }
  function reviewCell(row: HTMLTableRowElement, annotation: SpanAnnotation | DirectedRelation): void {
    const td = cell(row);
    td.append(actionButton(annotation.review_status === 'reviewed' ? 'Reviewed ✓' : 'Mark reviewed', `Review ${annotation.id}`, () => {
      annotation.review_status = annotation.review_status === 'reviewed' ? 'draft' : 'reviewed';
      annotation.provenance['review_status'] = evidence('user_supplied', 'Explicit reviewer decision; not independently adjudicated.');
      dirty(); renderAnnotations(); inspect(annotation);
    }));
  }
  function markCorrected(annotation: SpanAnnotation | DirectedRelation, field: string): void {
    annotation.review_status = 'draft'; annotation.provenance[field] = evidence('human_corrected', 'Edited in the document review workspace.');
    annotation.provenance['review_status'] = evidence('human_corrected', 'Editing returned this annotation to draft; a new review is required.'); dirty();
  }
  function spanCells(row: HTMLTableRowElement, annotation: SpanAnnotation): void {
    cell(row).append(actionButton(`${annotation.id} [${annotation.start}, ${annotation.end})`, `Inspect ${annotation.id}`, () => inspect(annotation)));
    cell(row).append(textNode('span', annotation.text, 'annotation-text'));
  }
  function renderAnnotations(): void {
    if (!draft) return;
    const segments = element('segment-rows'); segments.replaceChildren();
    for (const segment of draft.segments) {
      const row = document.createElement('tr'); spanCells(row, segment);
      cell(row).append(selectControl(['NAR','REM','SUP','GEN','FIC'], segment.label, `Narrative mode for ${segment.id}`, value => { segment.label = value || null; markCorrected(segment, 'label'); renderAnnotations(); }, true));
      reviewCell(row, segment);
      cell(row).append(actionButton('Remove', `Remove ${segment.id}`, () => { if (draft) draft.segments = draft.segments.filter(item => item.id !== segment.id); dirty(); renderAnnotations(); })); segments.append(row);
    }
    if (!draft.segments.length) appendEmpty(segments, 5, 'No time segments. Select a source span to add one.');
    const mentions = element('mention-rows'); mentions.replaceChildren();
    for (const mention of draft.mentions) {
      const row = document.createElement('tr'); spanCells(row, mention);
      cell(row).append(selectControl(['TIME','ACTOR','LOCATION','EVENT'], mention.kind, `Kind for ${mention.id}`, value => { mention.kind = value; markCorrected(mention, 'kind'); renderAnnotations(); }));
      const normalized = document.createElement('input'); normalized.type = 'text'; normalized.value = mention.normalized_value || ''; normalized.setAttribute('aria-label', `Normalized value for ${mention.id}`);
      normalized.addEventListener('change', () => { mention.normalized_value = normalized.value.trim() || null; markCorrected(mention, 'normalized_value'); renderAnnotations(); }); cell(row).append(normalized);
      reviewCell(row, mention);
      cell(row).append(actionButton('Remove', `Remove ${mention.id}`, () => { if (draft) draft.mentions = draft.mentions.filter(item => item.id !== mention.id); dirty(); renderAnnotations(); })); mentions.append(row);
    }
    if (!draft.mentions.length) appendEmpty(mentions, 6, 'No candidate mentions. Select a source span to add one.');
    const relations = element('relation-rows'); relations.replaceChildren();
    for (const relation of draft.relations) {
      const row = document.createElement('tr'); cell(row).append(actionButton(`${relation.id} · ${relation.family}`, `Inspect ${relation.id}`, () => inspect(relation)));
      cell(row).textContent = `${relation.from_id} → ${relation.to_id}`;
      cell(row).append(selectControl(relationLabels(relation.family), relation.label, `Label for ${relation.id}`, value => { relation.label = value; markCorrected(relation, 'label'); renderAnnotations(); }));
      reviewCell(row, relation);
      cell(row).append(actionButton('Remove', `Remove ${relation.id}`, () => { if (draft) draft.relations = draft.relations.filter(item => item.id !== relation.id); dirty(); renderAnnotations(); })); relations.append(row);
    }
    if (!draft.relations.length) appendEmpty(relations, 5, 'No relations are inferred automatically. Add a directed draft relation after inspecting its endpoints.');
    relationOptions();
  }
  function appendEmpty(body: HTMLElement, columns: number, text: string): void { const row = document.createElement('tr'); const td = cell(row); td.colSpan = columns; td.textContent = text; body.append(row); }
  function relationLabels(family: string): string[] { return inventories[family.toLowerCase() as keyof RelationInventories] || []; }
  function relationEndpoints(family: string): SpanAnnotation[] {
    if (!draft) return [];
    return family === 'TYM' ? draft.segments : draft.mentions.filter(mention => family === 'TLINK' ? ['EVENT','TIME'].includes(mention.kind) : mention.kind === 'EVENT');
  }
  function relationOptions(): void {
    const family = element<HTMLSelectElement>('relation-family').value;
    const endpoints = relationEndpoints(family);
    for (const id of ['relation-from', 'relation-to']) {
      const select = element<HTMLSelectElement>(id), selected = select.value; select.replaceChildren();
      for (const endpoint of endpoints) { const option = document.createElement('option'); option.value = endpoint.id; option.textContent = `${endpoint.id} · ${endpoint.text.slice(0, 48)}`; select.append(option); }
      if (endpoints.some(item => item.id === selected)) select.value = selected;
    }
    const label = element<HTMLSelectElement>('relation-label'), selected = label.value; label.replaceChildren();
    for (const value of relationLabels(family)) { const option = document.createElement('option'); option.value = value; option.textContent = value; label.append(option); }
    if (relationLabels(family).includes(selected)) label.value = selected;
    element<HTMLButtonElement>('add-relation').disabled = busy || endpoints.length < 2 || !label.value;
  }
  function updateSelection(): void { element('selection-info').textContent = preserved.selectionStart === preserved.selectionEnd ? 'Select a source span.' : `Selected [${preserved.selectionStart}, ${preserved.selectionEnd})`; }
  function selectedSpan(): { start: number; end: number; text: string } | null {
    if (!draft) return null;
    const start = preserved.selectionStart, end = preserved.selectionEnd;
    if (end <= start || !draft.text.slice(start, end).trim()) { message('Select a nonempty span in the preserved source first.', true); return null; }
    return { start, end, text: draft.text.slice(start, end) };
  }
  function showDiagnostics(diagnostics: AnnotationDiagnostic[]): void {
    const container = element('validation-diagnostics'); container.replaceChildren();
    if (!diagnostics.length) { container.append(textNode('p', 'No validation diagnostics. This checks consistency, not annotation accuracy.')); return; }
    const list = document.createElement('ul');
    for (const diagnostic of diagnostics) { const item = textNode('li', `${diagnostic.severity}: ${diagnostic.code}${diagnostic.annotation_id ? ` (${diagnostic.annotation_id})` : ''} — ${diagnostic.message}`); item.dataset.severity = diagnostic.severity; list.append(item); }
    container.append(list);
  }
  /** Graph layout conveys explicit edges only; geometric placement is not a reconstructed timeline. */
  function renderGraph(validation: GraphValidation): void {
    const graph = element('temporal-graph'); graph.replaceChildren();
    const order = validation.story_order;
    element('graph-scope').textContent = `${order.status.toUpperCase()} order. ${order.scope} Layout positions do not imply chronology.`;
    element('simultaneous-info').textContent = order.simultaneous_groups.length ? `Reviewed simultaneous groups: ${order.simultaneous_groups.map(group => group.join(', ')).join('; ')}` : 'No reviewed simultaneous groups.';
    if (!order.precedence_edges.length) { graph.append(textNode('p', 'No reviewed precedence edges. Story order remains unknown for unlinked annotations.')); return; }
    const ids = Array.from(new Set(order.precedence_edges.flatMap(edge => [edge.from_id, edge.to_id])));
    const shownIds = ids.slice(0, 40);
    const ns = 'http://www.w3.org/2000/svg';
    const svg = document.createElementNS(ns, 'svg'); svg.setAttribute('viewBox', '0 0 760 460'); svg.setAttribute('role', 'img'); svg.setAttribute('aria-label', `Reviewed partial temporal graph with ${ids.length} connected annotations. See edge list below.`);
    const title = document.createElementNS(ns, 'title'); title.textContent = 'Reviewed partial temporal relation graph'; svg.append(title);
    const defs = document.createElementNS(ns, 'defs'), marker = document.createElementNS(ns, 'marker'); marker.id = 'temporal-arrow'; marker.setAttribute('markerWidth','8'); marker.setAttribute('markerHeight','8'); marker.setAttribute('refX','7'); marker.setAttribute('refY','4'); marker.setAttribute('orient','auto');
    const arrow = document.createElementNS(ns, 'path'); arrow.setAttribute('d','M0,0 L8,4 L0,8 Z'); arrow.setAttribute('fill',order.status === 'inconsistent' ? '#9b342b' : '#147a7d'); marker.append(arrow); defs.append(marker); svg.append(defs);
    const positions = new Map<string, { x: number; y: number }>();
    shownIds.forEach((id, index) => { const angle = 2 * Math.PI * index / shownIds.length - Math.PI / 2; positions.set(id, { x: 380 + 285 * Math.cos(angle), y: 230 + 175 * Math.sin(angle) }); });
    for (const edge of order.precedence_edges) {
      const from = positions.get(edge.from_id), to = positions.get(edge.to_id); if (!from || !to) continue;
      const dx = to.x - from.x, dy = to.y - from.y, length = Math.hypot(dx,dy) || 1;
      const line = document.createElementNS(ns,'line'); line.setAttribute('x1',String(from.x + 35*dx/length)); line.setAttribute('y1',String(from.y + 20*dy/length)); line.setAttribute('x2',String(to.x - 39*dx/length)); line.setAttribute('y2',String(to.y - 24*dy/length)); line.setAttribute('stroke',order.status === 'inconsistent' ? '#9b342b' : '#147a7d'); line.setAttribute('stroke-width','2'); line.setAttribute('marker-end','url(#temporal-arrow)'); svg.append(line);
    }
    for (const [id, position] of positions) {
      const rect = document.createElementNS(ns,'rect'); rect.setAttribute('x',String(position.x-42)); rect.setAttribute('y',String(position.y-17)); rect.setAttribute('width','84'); rect.setAttribute('height','34'); rect.setAttribute('rx','4'); rect.setAttribute('fill','#edf5f5'); rect.setAttribute('stroke','#147a7d'); svg.append(rect);
      const text = document.createElementNS(ns,'text'); text.setAttribute('x',String(position.x)); text.setAttribute('y',String(position.y+5)); text.setAttribute('text-anchor','middle'); text.setAttribute('fill','#172f43'); text.setAttribute('font-size','13'); text.textContent = id.slice(0,15); svg.append(text);
    }
    graph.append(svg);
    if (ids.length > shownIds.length) graph.append(textNode('p', 'Diagram displays the first 40 connected annotations. The complete edge list is below.'));
    const list = document.createElement('ul'); list.className = 'graph-edge-list';
    for (const edge of order.precedence_edges) list.append(textNode('li', `${edge.from_id} → ${edge.to_id} · ${edge.family} · ${edge.relation_id}`));
    graph.append(list);
    if (draft) { const disconnected = [...draft.segments, ...draft.mentions].filter(item => !ids.includes(item.id)); graph.append(textNode('p', `${disconnected.length} annotations are disconnected from precedence edges; their chronological order is unknown.`)); }
  }
  sourceInput.value = originalExamples['en'];
  sourceInput.addEventListener('input', controls);
  language.addEventListener('change', () => { sourceInput.lang = language.value; });
  element('document-example').addEventListener('click', () => { sourceInput.value = originalExamples[language.value] || originalExamples['en']; sourceInput.lang = language.value; controls(); });
  preserved.addEventListener('select', updateSelection); preserved.addEventListener('keyup', updateSelection); preserved.addEventListener('mouseup', updateSelection);
  element('relation-family').addEventListener('change', relationOptions);
  element('document-form').addEventListener('submit', async event => {
    event.preventDefault(); if (busy || !sourceInput.value.trim()) return;
    busy = true; controls(); message('Analyzing candidate annotations…');
    try {
      const analysis = await request<DocumentAnalysis>('/v1/documents/analyze', { text: sourceInput.value, language: language.value, document_date: date.value || null });
      if (analysis.document.text !== sourceInput.value || analysis.document.offset_encoding !== 'utf-16') throw new Error('The API did not preserve the source text and UTF-16 offset contract.');
      draft = analysis.document; inventories = analysis.relation_inventories; sequence = 0;
      preserved.value = draft.text; preserved.lang = draft.language;
      element('document-id').textContent = draft.document_id;
      element('document-offset-info').textContent = `${draft.text.length.toLocaleString()} UTF-16 code units · [start, end)`;
      element('document-limit').textContent = analysis.research_limit;
      element('document-results').hidden = false; element('source-highlight').textContent = draft.text;
      element('annotation-evidence').textContent = 'Select an annotation to inspect its field provenance.';
      dirty(); renderAnnotations(); showDiagnostics(analysis.diagnostics);
      message(analysis.segment_model_available ? 'Candidate annotations ready. Inspect and review each field.' : 'Rule candidates ready. The segment model is unavailable; narrative labels remain unassigned.');
    } catch (error) { message(errorText(error), true); }
    finally { busy = false; controls(); relationOptions(); }
  });
  element('add-segment').addEventListener('click', () => {
    const span = selectedSpan(); if (!span || !draft) return;
    const annotation: TimeSegment = { id: uniqueId('ts'), ...span, label: null, review_status: 'draft', provenance: { start: evidence('user_supplied','Inclusive UTF-16 start selected by the reviewer.'), end: evidence('user_supplied','Exclusive UTF-16 end selected by the reviewer.'), text: evidence('source_slice','Exact substring of preserved source.'), label: evidence('user_supplied','Narrative mode not yet assigned.'), review_status: evidence('user_supplied','New annotation starts in draft; no review has been performed.') } };
    draft.segments.push(annotation); dirty(); renderAnnotations(); inspect(annotation);
  });
  element('add-mention').addEventListener('click', () => {
    const span = selectedSpan(); if (!span || !draft) return;
    const annotation: SourceMention = { id: uniqueId('m'), ...span, kind: element<HTMLSelectElement>('new-mention-kind').value, normalized_value: null, review_status: 'draft', provenance: { start: evidence('user_supplied','Inclusive UTF-16 start selected by the reviewer.'), end: evidence('user_supplied','Exclusive UTF-16 end selected by the reviewer.'), text: evidence('source_slice','Exact substring of preserved source.'), kind: evidence('user_supplied','Mention kind selected by the reviewer.'), normalized_value: evidence('user_supplied','No normalized value has been assigned.'), review_status: evidence('user_supplied','New annotation starts in draft; no review has been performed.') } };
    draft.mentions.push(annotation); dirty(); renderAnnotations(); inspect(annotation);
  });
  element('relation-form').addEventListener('submit', event => {
    event.preventDefault(); if (!draft || busy) return;
    const family = element<HTMLSelectElement>('relation-family').value, from_id = element<HTMLSelectElement>('relation-from').value, to_id = element<HTMLSelectElement>('relation-to').value, label = element<HTMLSelectElement>('relation-label').value;
    if (!from_id || !to_id || from_id === to_id || !label) { message('Choose two distinct endpoints and a relation label.', true); return; }
    draft.relations.push({ id: uniqueId('r'), family, from_id, to_id, label, review_status:'draft', provenance: { family: evidence('user_supplied','Relation schema selected by the reviewer.'), from_id: evidence('user_supplied','Source endpoint selected by the reviewer.'), to_id: evidence('user_supplied','Target endpoint selected by the reviewer.'), label: evidence('user_supplied','Label selected from the family inventory.'), review_status: evidence('user_supplied','New relation starts in draft; no review has been performed.') } });
    dirty(); renderAnnotations(); message('Draft relation added. Review it explicitly before using it in the temporal graph.');
  });
  element('validate-document').addEventListener('click', async () => {
    if (!draft || busy) return;
    const validatingRevision = revision;
    busy = true; exportReady = false; controls(); message('Validating annotation consistency…');
    try {
      const validation = await request<GraphValidation>('/v1/documents/validate', draft);
      if (revision !== validatingRevision) { message('Draft changed during validation. Validate the current draft again.', true); return; }
      inventories = validation.relation_inventories; showDiagnostics(validation.diagnostics); renderGraph(validation);
      element('validation-state').textContent = validation.valid ? 'Consistent annotation structure. Export is available; this is not an accuracy score.' : 'Validation failed. Correct the diagnostics before exporting.';
      exportReady = validation.valid;
      element<HTMLButtonElement>('export-document').disabled = !exportReady;
      message(validation.valid ? 'Validation complete.' : 'Validation found inconsistent or invalid annotations.', !validation.valid);
    } catch (error) { message(errorText(error), true); }
    finally { busy = false; controls(); relationOptions(); }
  });
  element('export-document').addEventListener('click', () => {
    if (!draft || element<HTMLButtonElement>('export-document').disabled) return;
    const blob = new Blob([JSON.stringify(draft, null, 2)], { type:'application/json' });
    const url = URL.createObjectURL(blob), link = document.createElement('a'); link.href = url; link.download = `${draft.document_id.replace(/[^a-zA-Z0-9_-]/g,'_')}-annotations.json`; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
  });
  controls();
})();
