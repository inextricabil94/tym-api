/** Builds a corpus-free illustrated report from completed real integration evidence.
 * Training remains C#/ML.NET/TorchSharp. This Node script only draws/assembles documents.
 * Requires the bundled pdf-lib and sharp document dependencies. */
import fs from 'node:fs';
import path from 'node:path';
import {createRequire} from 'node:module';

const options = Object.fromEntries(process.argv.slice(2).reduce((pairs, arg, index, args) => {
  if (arg.startsWith('--')) pairs.push([arg.slice(2), args[index + 1]]);
  return pairs;
}, []));
for (const key of ['evidence', 'out', 'pdf', 'runtime']) if (!options[key]) throw new Error('Required: --' + key);
const root = path.resolve(options.evidence), out = path.resolve(options.out), pdfPath = path.resolve(options.pdf);
const require = createRequire(path.join(path.resolve(options.runtime), 'package.json'));
const {PDFDocument, StandardFonts, rgb} = require('pdf-lib');
const sharp = require('sharp');
const escape = text => String(text ?? '').replace(/[&<>"']/g, char => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[char]));
const svgText = escape;
const jsons = folder => fs.readdirSync(path.join(root, folder)).filter(name => name.endsWith('.json')).sort()
  .map(name => JSON.parse(fs.readFileSync(path.join(root, folder, name), 'utf8')));
const predictions = jsons('predictions'), books = jsons('books');
if (predictions.length !== 10 || books.length !== 10) throw new Error('Exactly 10 prediction and 10 book evidence files are required.');
const records = [...predictions, ...books];
if (records.some(row => row.status !== 'passed' || !Array.isArray(row.assertions) || row.assertions.length < 1)) {
  throw new Error('Only completed passing evidence with explicit assertions can produce this report.');
}
const identity = row => row.workflow_id ?? row.id;
if (records.some(row => !/^[a-z0-9_-]+$/i.test(identity(row) ?? '')) || new Set(records.map(identity)).size !== 20) {
  throw new Error('Evidence identities must be distinct safe filenames.');
}
const algorithmNames={mlnet_kmeans:'KMeans',mlnet_pca_kmeans:'PCA plus KMeans',
  csharp_average_linkage_hierarchical:'Average-linkage hierarchy',csharp_dbscan:'DBSCAN',
  csharp_torchsharp_autoencoder:'Neural autoencoder'};
const titleFor=row=>row.title ?? row.workflow_title ??
  (row.algorithm ? `${row.language==='ro'?'Romanian':'English'} books: ${algorithmNames[row.algorithm] ?? row.algorithm}` : identity(row));
fs.mkdirSync(path.join(out, 'images'), {recursive:true});
fs.mkdirSync(path.dirname(pdfPath), {recursive:true});
const C = {navy:'#152C43', teal:'#007F78', pale:'#E8F4F1', blue:'#EEF3F8', ink:'#20364A', muted:'#53697D'};
function wrappedWords(text, width=48) {
  const lines=[]; let line='';
  for(const word of String(text).split(/\s+/)) { if(line && (line+' '+word).length>width){lines.push(line); line='';} line+=(line?' ':'')+word; }
  if(line) lines.push(line); return lines;
}
function diagram(row, isBook) {
  const algorithm=row.algorithm ?? '';
  const representation = algorithm==='mlnet_kmeans' ? 'Train-only text + L2' : 'Train-only text + PCA 32';
  const method = ({mlnet_kmeans:'KMeans: 8 clusters',mlnet_pca_kmeans:'PCA + KMeans: 8 clusters',
    csharp_average_linkage_hierarchical:'Average-linkage hierarchy',csharp_dbscan:'DBSCAN: ε .45, minPts 5',
    csharp_torchsharp_autoencoder:'Autoencoder: 32 → 16 → 32'})[algorithm] ?? 'ML.NET classifier';
  const output = row.variants?.map(v => v.predicted_label).join(' / ') ??
    (algorithm==='csharp_torchsharp_autoencoder' ? 'Heldout numeric reconstruction' : algorithm.includes('dbscan') || algorithm.includes('hierarchical') ? 'Training-sample clusters / noise' : 'Heldout lexical cluster geometry');
  const steps=isBook ? ['Private book passages',representation,method,output] : ['Original authored input','Real HTTP / TypeScript UI','Verified ML.NET model',output];
  const boxes=steps.map((label,i)=>{
    const lines=wrappedWords(label,23), x=24+i*237;
    return `<rect x="${x}" y="84" width="210" height="136" rx="12" fill="${i===3?C.pale:C.blue}" stroke="#BDCEDB"/><text x="${x+18}" y="110" font-family="Segoe UI,Arial" font-size="13" fill="${C.muted}">STEP ${i+1}</text>`+
      lines.slice(0,4).map((line,j)=>`<text x="${x+18}" y="${145+j*23}" font-family="Segoe UI,Arial" font-size="17" font-weight="600" fill="${C.ink}">${svgText(line)}</text>`).join('')+
      (i<3?`<path d="M ${x+214} 154 h 17 m -7 -6 l 7 6 -7 6" fill="none" stroke="${C.teal}" stroke-width="3"/>`:'');
  }).join('');
  const scope=isBook ? 'Semantic accuracy: unavailable (unlabeled input)' : 'Label agreement verifies software behavior, not semantic correctness';
  return `<svg xmlns="http://www.w3.org/2000/svg" width="980" height="282" viewBox="0 0 980 282"><rect width="980" height="282" rx="16" fill="white"/><text x="24" y="40" font-family="Segoe UI,Arial" font-size="20" font-weight="600" fill="${C.navy}">${svgText(row.title ?? row.workflow_title ?? identity(row))}</text>${boxes}<text x="24" y="257" font-family="Segoe UI,Arial" font-size="17" fill="${C.muted}">${svgText(scope)}</text></svg>`;
}
const definitions={
  mlnet_kmeans:'Fits vocabulary and eight centroids on training passages, reloads the private pipeline, and scores an original authored query. Heldout distances describe lexical geometry.',
  mlnet_pca_kmeans:'Fits centered PCA on training text vectors, reduces represented width to 32, then fits eight centroids. The saved pipeline is reloaded; query text features must match an independently fitted training vocabulary.',
  csharp_average_linkage_hierarchical:'Projects a deterministic sample of at most 256 training passages into PCA space, merges clusters by average linkage, and checks the saved sample vectors and assignments. It provides no unseen-document classifier.',
  csharp_dbscan:'Runs density clustering on the same bounded training-only PCA sample. Noise is a valid result at the fixed epsilon and minimum-points settings, rather than a failed prediction.',
  csharp_torchsharp_autoencoder:'Trains a small C# neural encoder/decoder on training-fitted PCA coordinates. It checks heldout reconstruction and constant baselines, a 16-coordinate bottleneck, and the absence of exported neural weights.'
};
function explanation(row, isBook) {
  if(isBook) return definitions[row.algorithm] ?? 'Checks the complete real book preparation, training, assessment and artifact contracts.';
  const conditional=String(row.task ?? '').startsWith('timebank') || String(identity(row)).includes('timebank');
  return 'Sends original authored text through the real localhost TypeScript workbench and API, then compares the UI label with a direct HTTP prediction from the same verified ML.NET model.' +
    (row.variants?.length>1 ? ' The same test also exercises the structured variant for this task.' : '')+
    (conditional ? ' Supplied mentions or ordered endpoints make this a conditional classification workflow.' : ' The output belongs to the task-specific label vocabulary.');
}
function normalizeMetrics(row, isBook) {
  const fmt=value=>value===null||value===undefined?'N/A':typeof value==='number'?Number(value.toPrecision(6)):String(value);
  if(!isBook) return [
    ...(row.variants ?? []).map(v=>[v.input_format?'Structured observed label':'Lexical observed label',v.predicted_label]),
    ['Verified models',(row.variants ?? []).map(v=>v.model_id).join(' / ')],
    ['UI/API agreement',(row.variants ?? []).every(v=>v.ui_label===v.predicted_label)?'Matched for every variant':'Mismatch'],
    ['Artifact integrity',(row.variants ?? []).map(v=>v.integrity_status).join(' / ')],
    ['Direct HTTP time, ms',(row.variants ?? []).map(v=>fmt(v.direct_api_elapsed_ms)).join(' / ')],
    ['UI round trip, ms',(row.variants ?? []).map(v=>fmt(v.ui_round_trip_elapsed_ms)).join(' / ')]
  ];
  const m=row.measurement ?? {},space=m.space ?? {}, pairs=[
    ['Passages: train / holdout',`${row.training_rows} / ${row.heldout_rows}`],
    ['Source documents',row.documents],['Feature dimensions',m.feature_dimensions],
    [m.fit_seconds_including_shared_featurizer!==undefined?'Fit with shared text, seconds':m.fit_seconds_including_shared_representation!==undefined?'Fit with representation, seconds':'Algorithm-only fit, seconds',fmt(m.fit_seconds_including_shared_featurizer ?? m.fit_seconds_including_shared_representation ?? m.fit_seconds_algorithm_only)]
  ];
  if(m.heldout_metrics) pairs.push(['Heldout squared distance',fmt(m.heldout_metrics.average_squared_centroid_distance)],['Heldout Davies-Bouldin',fmt(m.heldout_metrics.davies_bouldin_index)]);
  if(m.sample_metrics) pairs.push(['Sample clusters / noise',`${m.sample_metrics.clusters} / ${m.sample_metrics.noise_rows} of ${m.sample_metrics.rows}`],['Sample silhouette',fmt(m.sample_metrics.silhouette_excluding_noise)]);
  if(m.heldout_reconstruction_mean_squared_error!==undefined) pairs.push(['Heldout reconstruction MSE',fmt(m.heldout_reconstruction_mean_squared_error)],['Bottleneck / parameters',`${m.bottleneck_dimensions} / ${m.model_parameters}`]);
  pairs.push(['Private artifact bytes',fmt(row.artifact_bytes)],['Shared working set, MiB',fmt(space.process_working_set_mb)],['Semantic accuracy','N/A - unlabeled']);
  return pairs;
}

const pdf=await PDFDocument.create();
const regular=await pdf.embedFont(StandardFonts.Helvetica);
const bold=await pdf.embedFont(StandardFonts.HelveticaBold);
const W=595.276,H=841.89, M=42;
const color=hex=>{const v=hex.replace('#','');return rgb(parseInt(v.slice(0,2),16)/255,parseInt(v.slice(2,4),16)/255,parseInt(v.slice(4,6),16)/255);};
function text(page, value, x,y,size=11,font=regular,fill=C.ink) { page.drawText(String(value),{x,y,size,font,color:color(fill)}); }
function paragraph(page,value,x,y,width,size=11,lineHeight=16,font=regular,fill=C.ink,maxLines=30){
  const lines=[];let line='';for(const word of String(value).split(/\s+/)){if(line&&font.widthOfTextAtSize(line+' '+word,size)>width){lines.push(line);line='';}line+=(line?' ':'')+word;}if(line)lines.push(line);
  if(lines.length>maxLines) throw new Error('Report paragraph exceeds available layout.');
  lines.forEach((line,i)=>text(page,line,x,y-i*lineHeight,size,font,fill));return y-lines.length*lineHeight;
}
function basePage(index, kicker) {
  const page=pdf.addPage([W,H]);page.drawRectangle({x:0,y:H-9,width:W,height:9,color:color(C.teal)});
  text(page,'TYM / INTEGRATION EVIDENCE',M,H-38,9,bold,C.muted);text(page,kicker,M,H-60,10,regular,C.muted);
  text(page,'5 October 2026  |  Original inputs or aggregate book evidence only',M,23,8,regular,C.muted);
  text(page,String(index),W-M-15,23,9,regular,C.muted);return page;
}
const cover=basePage(1,'20 real integration workflows');
text(cover,'Prediction and book',M,727,29,bold,C.navy);text(cover,'workflow integration',M,690,29,bold,C.navy);
let cy=paragraph(cover,'Ten prediction workflows plus ten book experiments. This report connects each workflow to a real passing test, its picture, observed output and explicit interpretation limits.',M,646,W-2*M,13,20);
cover.drawRectangle({x:M,y:cy-102,width:W-2*M,height:85,color:color(C.pale)});
text(cover,'20 / 20 integration tests passed',M+18,cy-46,21,bold,C.teal);
text(cover,'Real localhost UI/API • Fresh C# book fits • No request mocks',M+18,cy-76,10,regular,C.ink);
cy-=145;
text(cover,'WHAT THE TESTS ESTABLISH',M,cy,11,bold,C.navy);cy-=27;
cy=paragraph(cover,'Prediction checks cover real HTTP requests, model readiness and integrity, task inventories, browser results and six structured TimeBank variants. Book checks independently reproduce the split and text vocabulary, reload artifacts, and check finite metrics. PCA and neural fitting boundaries rely on the implementation path and metadata.',M,cy,W-2*M,12,18);
cy-=28;text(cover,'HOW TO READ THE PICTURES',M,cy,11,bold,C.navy);cy-=27;
cy=paragraph(cover,'Prediction pages show screenshots from the actual tested interface with original authored prose. Book pages show diagrams generated from passing aggregate evidence; they are explanatory illustrations, not screenshots of a book-classification interface.',M,cy,W-2*M,12,18);
cy-=28;text(cover,'SCIENTIFIC LIMITS',M,cy,11,bold,C.navy);cy-=27;
paragraph(cover,'A passing integration test is a software-contract result. Predicted labels are not human gold. Book passages have no semantic labels, so accuracy and F1 are unavailable. Fresh fitting costs in this report describe this integration execution and must not be substituted for the published algorithm benchmark. Private prose and weights remain outside this document.',M,cy,W-2*M,12,18);

const cards=[];
for(let i=0;i<records.length;i++){
  const row=records[i],isBook=i>=10,id=identity(row),title=titleFor(row);
  const svg=diagram({...row,title},isBook), svgPath=path.join(out,'images',id+'-flow.svg'), pngPath=path.join(out,'images',id+'-flow.png');
  fs.writeFileSync(svgPath,svg);await sharp(Buffer.from(svg)).resize(1960,564).png().toFile(pngPath);
  let screenshotFile=null;
  if(!isBook){if(!row.screenshot)throw new Error('Prediction evidence requires actual screenshot.'); const source=path.resolve(root,row.screenshot);if(!source.startsWith(root+path.sep)||!fs.existsSync(source))throw new Error('Missing or unsafe screenshot path.');screenshotFile=id+'-ui.png';fs.copyFileSync(source,path.join(out,'images',screenshotFile));}
  const imageFile=screenshotFile ?? id+'-flow.png', imageBytes=fs.readFileSync(path.join(out,'images',imageFile));
  const page=basePage(i+2,(isBook?'BOOK PIPELINE':'PREDICTION WORKFLOW')+' / '+String(row.language??'').toUpperCase());
  let y=paragraph(page,title,M,H-90,W-2*M,22,28,bold,C.navy,3)-15;
  const intro=explanation(row,isBook);y=paragraph(page,intro,M,y,W-2*M,11,16,regular,C.ink,6)-15;
  if(!isBook){
    text(page,'ORIGINAL AUTHORED INPUT',M,y,10,bold,C.navy);y-=18;
    // Draw the Unicode input as a code-native vector panel so Romanian spelling
    // is preserved even when the PDF runtime only supplies standard fonts.
    const lines=String(row.input_text ?? '').split('\n').flatMap(line=>wrappedWords(line,92));
    if(lines.length>8)throw new Error('Authored input exceeds the readable panel: '+id);
    const panelHeight=lines.length*28+8;
    const panel=`<svg xmlns="http://www.w3.org/2000/svg" width="1022" height="${panelHeight}"><rect width="1022" height="${panelHeight}" fill="white"/>${lines.map((line,n)=>`<text x="0" y="${21+n*28}" font-family="Segoe UI,Arial" font-size="20" fill="${C.ink}">${svgText(line)}</text>`).join('')}</svg>`;
    const panelPath=path.join(out,'images',id+'-input.png');
    await sharp(Buffer.from(panel)).png().toFile(panelPath);
    const inputPng=await pdf.embedPng(fs.readFileSync(panelPath));
    const displayHeight=panelHeight*(W-2*M)/1022;
    page.drawImage(inputPng,{x:M,y:y-displayHeight+10,width:W-2*M,height:displayHeight});
    y-=displayHeight+12;
  }
  const png=await pdf.embedPng(imageBytes);const width=W-2*M;const height=Math.min(isBook?155:150,width*png.height/png.width);
  const imageWidth=height*png.width/png.height;page.drawImage(png,{x:M+(width-imageWidth)/2,y:y-height,width:imageWidth,height});y-=height+17;
  y=paragraph(page,isBook?'Diagram: the pipeline exercised by this real book integration case.':'Actual browser screenshot for visual context. Read the input above and observed labels below; the HTML report links the full-resolution image.',M,y,width,9,13,regular,C.muted)-17;
  text(page,'OBSERVED OUTPUT / CONTRACT',M,y,10,bold,C.navy);y-=19;
  const metrics=normalizeMetrics(row,isBook);
  for(const [key,value] of metrics.slice(0,isBook?7:row.variants?.length>1?6:4)){
    y=paragraph(page,key+': '+String(value),M,y,width,10,14,regular,C.ink,3)-2;
  }
  y-=12;text(page,'ASSERTIONS THAT PASSED',M,y,10,bold,C.navy);y-=20;
  const conciseAssertions=[...row.assertions.slice(0,2),...row.assertions.slice(-2)];
  for(const assertion of conciseAssertions)y=paragraph(page,'• '+assertion,M,y,width,10,14,regular,C.ink,3)-4;
  const limit=isBook?'Semantic accuracy: N/A. Geometry, noise and numeric reconstruction are not adjudicated semantic correctness.':'Output labels are software observations on authored examples. No independent accuracy or probability estimate follows from these checks.';
  if(y<90)throw new Error('Workflow page content exceeds safe footer area: '+id);
  paragraph(page,limit,M,Math.min(y-12,94),width,9,13,regular,C.muted,3);
  cards.push({row,id,title,isBook,intro,metrics,imageFile,diagramFile:id+'-flow.png'});
}
const pdfBytes=await pdf.save();fs.writeFileSync(pdfPath,pdfBytes);
const html=`<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>TYM Integration Workflows</title><style>
body{margin:0;background:#F1F5F8;color:${C.ink};font:16px/1.6 'Segoe UI',Arial,sans-serif}header,main{max-width:1120px;margin:auto;padding:38px 24px}header{background:${C.navy};color:white;max-width:none}header div{max-width:1120px;margin:auto}h1{font-size:42px;line-height:1.15}h2{font-size:25px;line-height:1.25}h3{font-size:15px;color:${C.teal};letter-spacing:.06em}nav{display:flex;flex-wrap:wrap;gap:8px}nav a{color:white;background:#29445D;padding:8px 14px;border-radius:5px;text-decoration:none}.count{font-size:24px;color:#93DACB}.limits{background:#E8F4F1;border-left:4px solid ${C.teal};padding:18px 22px}.card{background:white;margin:24px 0;padding:28px;border:1px solid #CCD9E3;border-radius:12px}.badge{background:${C.pale};color:${C.teal};padding:4px 10px;border-radius:4px;font-size:13px;font-weight:600}.figure{width:100%;height:auto;border:1px solid #D8E1E8;border-radius:6px}.caption{font-size:13px;color:${C.muted}}.columns{display:grid;grid-template-columns:1fr 1fr;gap:25px}dl{margin:0}dl div{display:grid;grid-template-columns:1fr 1fr;gap:12px;border-bottom:1px solid #EEF3F8;padding:7px 0}dt{color:${C.muted}}dd{margin:0;overflow-wrap:anywhere}li{margin:8px 0}a{color:${C.teal}}footer{padding:18px;color:${C.muted};text-align:center}@media(max-width:720px){h1{font-size:30px}.columns{display:block}.card{padding:18px}header,main{padding:24px 16px}}
</style><header><div><p>TYM / REAL INTEGRATION EVIDENCE / 5 OCTOBER 2026</p><h1>Prediction and book workflows</h1><p class="count">20 / 20 integration tests passed</p><p>Ten prediction workflows and ten book experiments, illustrated with real UI screenshots and evidence-derived pipeline diagrams.</p><nav><a href="#prediction-workflows">Prediction workflows</a><a href="#book-workflows">Book experiments</a></nav></div></header><main><div class="limits">Passing checks establish software integration. Authored prediction labels are observations; private books remain unlabeled. Book diagrams illustrate the tested pipeline. Fresh fit costs are integration evidence, not replacement benchmark measurements.</div>`+
cards.map((card,index)=>`${index===0?'<h2 id="prediction-workflows">Ten prediction workflows</h2>':index===10?'<h2 id="book-workflows">Ten book experiments</h2>':''}<article class="card" id="${escape(card.id)}"><span class="badge">PASSED / ${card.isBook?'BOOK PIPELINE':'REAL UI + API'}</span><h2>${index+1}. ${escape(card.title)}</h2><p>${escape(card.intro)}</p><a href="images/${escape(card.imageFile)}"><img class="figure" src="images/${escape(card.imageFile)}" alt="${escape(card.isBook?'Pipeline diagram for ':'Real integration screenshot for ')}${escape(card.title)}"></a><p class="caption">${card.isBook?'Diagram generated from passing aggregate evidence. No private book prose is shown.':'Screenshot captured from the actual tested local interface; text is original authored test input. Click to view full resolution.'}</p>${card.isBook?'':`<details><summary>Workflow diagram</summary><img class="figure" src="images/${escape(card.diagramFile)}" alt="Workflow diagram"></details>`}<div class="columns"><div><h3>OBSERVED OUTPUT / CONTRACT</h3><dl>${card.metrics.map(([key,value])=>`<div><dt>${escape(key)}</dt><dd>${escape(value)}</dd></div>`).join('')}</dl></div><div><h3>ASSERTIONS THAT PASSED</h3><ul>${card.row.assertions.map(a=>`<li>${escape(a)}</li>`).join('')}</ul></div></div><p class="caption">${escape(card.row.scope ?? (card.isBook?'Semantic accuracy unavailable: unlabeled input.':'Conditional software behavior; no independent semantic correctness claim.'))}</p></article>`).join('')+
`</main><footer>Created from 20 passing integration evidence records. ML.NET / C# / TorchSharp training; TypeScript interface. Private weights and corpus prose are excluded.</footer></html>`;
fs.writeFileSync(path.join(out,'index.html'),html);
fs.writeFileSync(path.join(out,'workflow-evidence.json'),JSON.stringify({schema_version:1,status:'completed_integration_evidence',date:'2026-10-05',prediction_workflows:10,book_workflows:10,passed:20,records},null,2)+'\n');
console.log(JSON.stringify({html:path.join(out,'index.html'),pdf:pdfPath,pages:pdf.getPageCount(),workflow_count:20}));
