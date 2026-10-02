import { type Page, expect } from 'playwright/test';

export const baselineIds = ['segment_type_en','segment_type_ro','temporal_relation_en','temporal_relation_ro','timebank_event_class_ro','timebank_event_tense_ro','timebank_timex_type_ro','timebank_tlink_ro','timebank_slink_ro','timebank_alink_ro'];
export const modelIds = [...baselineIds, ...baselineIds.filter(id => id.startsWith('timebank_')).map(id => id + '_structured')];
export const originalText = 'Mara arrived yesterday. Today she left for Iași.';
const field = { method: 'user_supplied', evidence: 'Synthetic original test annotation.' };
type SyntheticRelation = {id:string;family:string;from_id:string;to_id:string;label:string;review_status:string;provenance:Record<string,typeof field>};
const segmentEvidence = {start:field,end:field,text:field,label:field,review_status:field};
const mentionEvidence = {start:field,end:field,text:field,kind:field,normalized_value:field,review_status:field};
export function syntheticDocument() {
  return { schema_version:1, document_id:'synthetic-ui-example', text:originalText, text_sha256:'synthetic-hash', offset_encoding:'utf-16', language:'en', document_date:null, review_status:'draft',
    segments:[{id:'ts1',start:0,end:23,text:originalText.slice(0,23),label:'NAR',review_status:'draft',provenance:{...segmentEvidence}},{id:'ts2',start:24,end:originalText.length,text:originalText.slice(24),label:'NAR',review_status:'draft',provenance:{...segmentEvidence}}],
    mentions:[{id:'e1',kind:'EVENT',start:5,end:12,text:'arrived',normalized_value:null,review_status:'draft',provenance:{...mentionEvidence}},{id:'e2',kind:'EVENT',start:34,end:38,text:'left',normalized_value:null,review_status:'draft',provenance:{...mentionEvidence}}],relations:[] as SyntheticRelation[] };
}
export const inventories = { tym:['BEFORE','AFTER','SIMULTANEOUS'],tlink:['BEFORE','AFTER','SIMULTANEOUS'],slink:['MODAL','FACTIVE'],alink:['INITIATES','CONTINUES'] };
export async function mockCatalog(page: Page) {
  await page.route('**/v1/models', route => route.fulfill({ json:{ models:modelIds.map(model_id => ({model_id,language:model_id.includes('_ro')?'ro':'en',available:true,training_rows:123,document_count:2,labels:['NAR','REM'],display_name:model_id,provenance:'Synthetic UI test manifest',input_hint:'Synthetic test guidance.'})) } }));
  await page.route('**/v1/predictions', route => route.fulfill({ json:{model_id:route.request().postDataJSON().model_id,predicted_label:'NAR',research_limit:'Synthetic response; no measured accuracy.'} }));
}
export async function mockWorkbench(page: Page) {
  await page.route('**/v1/documents/analyze', route => route.fulfill({ json:{document:syntheticDocument(),segment_model_available:true,relation_inventories:inventories,diagnostics:[],research_limit:'Synthetic candidate annotations.'} }));
  await page.route('**/v1/documents/validate', route => {
    const doc = route.request().postDataJSON() as ReturnType<typeof syntheticDocument>;
    const reviewed = doc.relations.filter(r => r.review_status === 'reviewed' && r.label === 'BEFORE');
    const cyclic = reviewed.some(a => reviewed.some(b => a.from_id === b.to_id && a.to_id === b.from_id));
    const missing = doc.segments.some(s=>['start','end','text','label','review_status'].some(key=>!(key in s.provenance))) || doc.mentions.some(m=>['start','end','text','kind','normalized_value','review_status'].some(key=>!(key in m.provenance))) || doc.relations.some(r=>['from_id','to_id','label','review_status'].some(key=>!(key in r.provenance)));
    return route.fulfill({ json:{valid:!cyclic&&!missing,diagnostics:cyclic?[{severity:'error',code:'temporal_cycle',message:'Reviewed temporal relations form a cycle.'}]:missing?[{severity:'error',code:'missing_field_provenance',message:'Required field evidence missing.'}]:[],story_order:{status:cyclic?'inconsistent':reviewed.length?'partial':'unknown',precedence_edges:reviewed.map(r=>({from_id:r.from_id,to_id:r.to_id,family:r.family,relation_id:r.id})),simultaneous_groups:[],scope:'Synthetic reviewed relations only.'},relation_inventories:inventories,research_limit:'Synthetic validation.'} });
  });
}
export async function analyzeSynthetic(page: Page) {
  await page.goto('/');
  await page.getByLabel('Source passage',{exact:true}).fill(originalText);
  await page.getByRole('button',{name:'Analyze passage',exact:true}).click();
  await expect(page.locator('#document-results')).toBeVisible();
}
