(function (root) {
  'use strict';
  const names = { en: 'English', ro: 'Română' };
  const ro = {
    'Offline': 'Offline', 'Saved diagrams': 'Diagrame salvate', 'Select saved diagram': 'Selectați o diagramă salvată',
    'Clear offline cache': 'Ștergeți memoria cache offline', 'Regenerate diagram': 'Regenerați diagrama',
    'Custom text': 'Text personalizat', 'Saved offline': 'Salvată offline', 'Session cache only': 'Memorată doar în această sesiune',
    'Loaded from offline cache': 'Încărcată din memoria cache offline', 'Offline cache cleared': 'Memoria cache offline a fost ștearsă',
    'Offline storage unavailable': 'Stocarea offline nu este disponibilă',
    'Diagram exceeds the offline storage limit.': 'Diagrama depășește limita de stocare offline.',
    'No saved book library is available offline.': 'Nu există o bibliotecă salvată disponibilă offline.',
    'No saved diagram matches these settings. Connect to generate it.': 'Nu există o diagramă salvată pentru aceste setări. Conectați-vă pentru a o genera.',
    'Segmentation algorithm': 'Algoritm de segmentare', 'Segmentation': 'Segmentare',
    'Article-guided rules': 'Reguli bazate pe articol',
    'ML.NET logistic regression (seed)': 'ML.NET regresie logistică (model inițial)',
    'ML.NET averaged perceptron (seed)': 'ML.NET perceptron mediat (model inițial)',
    'Inspect segments': 'Inspectați segmentele', 'Marked segmentation': 'Segmentare marcată',
    'Marked source text': 'Text sursă marcat', 'Selected segment': 'Segment selectat',
    'Seed examples: {count} | Not corpus validated': 'Exemple inițiale: {count} | Nevalidat pe corpus',
    'Rule-based boundaries': 'Limite stabilite prin reguli',
    'Segments {start}-{end} of {total}': 'Segmentele {start}-{end} din {total}',
    'Previous segments': 'Segmentele anterioare', 'Next segments': 'Segmentele următoare',
    'Show time track': 'Afișați pista temporală', 'Inspect {id}': 'Inspectați {id}',
    'Decision margin': 'Marjă de decizie', 'Model probability (uncalibrated)': 'Probabilitate a modelului (necalibrată)',
    'Boundary score': 'Scor al limitei', 'Boundary evidence': 'Indicii ale limitei',
    'Source text is unavailable or its offsets do not match.': 'Textul sursă este indisponibil sau pozițiile nu corespund.',
    'The API did not apply the selected segmentation algorithm.': 'API-ul nu a aplicat algoritmul de segmentare selectat.',
    'document_start': 'început de document', 'actor_change': 'schimbare de actori',
    'temporal_category_change': 'schimbare de categorie temporală', 'explicit_temporal_cue': 'indiciu temporal explicit',
    'conflicting_time_anchor': 'ancore temporale diferite', 'location_change': 'schimbare de loc',
    'paragraph_break': 'paragraf nou', 'missing_actors': 'actori neidentificați',
    'same_sentence': 'aceeași propoziție', 'continuity_features': 'indicii de continuitate',
    'TYM Diagram UI': 'TYM - Diagrame temporale',
    ' Diagram UI': ' - Diagrame temporale',
    'Natural-language text to temporal-segment diagram': 'Din text în limbaj natural în diagramă temporală',
    'Interface language': 'Limba interfeței', 'English': 'Engleză', 'Romanian': 'Română',
    'Input': 'Date de intrare', 'Diagram': 'Diagramă', 'Examples': 'Exemple',
    'Time segments (TS)': 'Segmente temporale (TS)', 'Time tracks (TT)': 'Piste temporale (TT)',
    'Book library': 'Bibliotecă', 'Loading library...': 'Se încarcă biblioteca...',
    'Select a book': 'Selectați o carte', 'Refresh book library': 'Reîncărcați biblioteca',
    'Loading...': 'Se încarcă...', 'Book example': 'Exemplu dintr-o carte',
    'Text scope': 'Domeniul textului', 'Full text': 'Text integral', 'Full Text': 'Text integral',
    'Chapters': 'Capitole', 'Excerpt': 'Fragment', 'Chapter / Section ({count})': 'Capitol / Secțiune ({count})',
    'Chapter ({count} detected)': 'Capitole identificate: {count}',
    'Previous chapter': 'Capitolul anterior', 'Next chapter': 'Capitolul următor',
    'Section {index} of {total} | UTF-16 [{start}, {end})': 'Secțiunea {index} din {total} | UTF-16 [{start}, {end})',
    'Chapter detection': 'Identificarea capitolelor', 'Auto': 'Automat',
    'Prefixed headings only': 'Doar titluri cu prefix', 'Include short numbered sections': 'Include secțiuni numerotate scurte',
    'API URL': 'Adresa API', 'Text language': 'Limba textului', 'Highlight entity': 'Evidențiați o entitate',
    'No highlight': 'Fără evidențiere', 'People': 'Persoane', 'Places': 'Locuri',
    'Unresolved references': 'Referințe nerezolvate', 'Organizations': 'Organizații', 'Refresh entities': 'Reidentificați entitățile',
    'Recognizing entities...': 'Se identifică entitățile...', 'Not requested for this section': 'Neidentificate pentru această secțiune',
    'Diagram width': 'Lățimea diagramei', 'Window {width} x {height}': 'Fereastră {width} x {height}',
    'TT density {density}': 'Densitate TT {density}', '10 - Overview': '10 - Ansamblu',
    '100 - Detailed': '100 - Detaliat', 'Text': 'Text', 'Excerpt diagram preview': 'Previzualizarea diagramei fragmentului',
    'Generated TYM diagram for {title}': 'Diagramă TYM generată pentru {title}',
    'Generate diagram': 'Generați diagrama', 'Generating...': 'Se generează...',
    'Cached TT density': 'Densitate TT memorată', '{density} - Pending': '{density} - În așteptare',
    'No cached diagrams': 'Nu există diagrame memorate', 'Result views': 'Vizualizări ale rezultatului',
    'Analysis': 'Analiză', 'Fit diagram to viewport width': 'Adaptați diagrama la lățimea panoului',
    'Fit': 'Încadrare', 'Zoom out': 'Micșorați', 'Zoom in': 'Măriți', 'Actual size (100%)': 'Dimensiune reală (100%)',
    'Actors': 'Actori', 'Events': 'Evenimente', 'Segments': 'Segmente', 'Tracks': 'Piste temporale',
    'Relations': 'Relații', 'TLINKs': 'Relații TLINK', 'Characters': 'Caractere',
    'Avg event confidence': 'Încredere medie în evenimente', 'Avg TS confidence': 'Încredere medie în TS',
    'TimeML events': 'Evenimente TimeML', 'Segment types': 'Tipuri de segmente',
    'Temporal categories': 'Categorii temporale', 'Relation types': 'Tipuri de relații', 'Entity labels': 'Clase de entități',
    'Extraction sources': 'Surse de extragere', 'No sources.': 'Nu există surse.', 'No values.': 'Nu există valori.',
    'Analysis issues': 'Probleme de analiză', 'No analysis issues were reported.': 'Nu au fost raportate probleme de analiză.',
    'Warnings': 'Avertismente', 'No analysis yet': 'Nu există încă o analiză',
    'Generate a diagram to inspect extractor statistics.': 'Generați o diagramă pentru statisticile extragerii.',
    'Event': 'Eveniment', 'Temporal': 'Temporal', 'Rel prev': 'Relație anterioară', 'Cue': 'Indiciu',
    'Confidence': 'Încredere', 'Type': 'Tip', 'Track': 'Pistă temporală', 'Anchor': 'Ancoră',
    'No events detected.': 'Nu au fost identificate evenimente.', 'No segments detected.': 'Nu au fost identificate segmente.',
    'No JSON result yet.': 'Nu există încă un rezultat JSON.', 'No XML result yet.': 'Nu există încă un rezultat XML.',
    'No TimeML result yet.': 'Nu există încă un rezultat TimeML.', 'Filter time tracks': 'Filtrați pistele temporale',
    'Track ID or character': 'ID-ul pistei sau personajul', 'Display groups': 'Grupuri de afișare',
    '0 - Paged (12 tracks)': '0 - Paginat (12 piste)', '{groups} - {size} tracks/group': '{groups} - {size} piste/grup',
    'Group': 'Grup', 'No matching groups': 'Nu există grupuri corespunzătoare',
    'Group {index} of {total}': 'Grupul {index} din {total}',
    'Tracks {start}-{end} of {total}': 'Pistele {start}-{end} din {total}',
    '{count} matching': '{count} rezultate', 'Previous tracks': 'Pistele anterioare', 'Next tracks': 'Pistele următoare',
    'No matching tracks': 'Nu există piste corespunzătoare', 'No diagram yet': 'Nu există încă o diagramă',
    'TYM time tracks {ids}': 'Piste temporale TYM: {ids}', 'TYM time-yard diagram': 'Diagramă temporală TYM',
    'Loading text': 'Se încarcă textul', 'Generating': 'Se generează', 'Needs attention': 'Necesită atenție',
    'Ready': 'Pregătit', 'Idle': 'În așteptare', 'Switch to light theme': 'Activați tema luminoasă',
    'Switch to dark theme': 'Activați tema întunecată', 'Caching density {density}...': 'Se memorează densitatea {density}...',
    '10 densities cached': '10 densități memorate',
    'Background cache paused. Current diagram remains available.': 'Memorarea în fundal este suspendată. Diagrama curentă rămâne disponibilă.',
    'Book catalog unavailable (HTTP {status}).': 'Catalogul de cărți nu este disponibil (HTTP {status}).',
    'Invalid book catalog.': 'Catalog de cărți invalid.',
    'Entity lookup limit reached. Retry in a minute.': 'Limita identificărilor de entități a fost atinsă. Reîncercați într-un minut.',
    'Entity lookup failed (HTTP {status}).': 'Identificarea entităților a eșuat (HTTP {status}).',
    'Entity lookup failed.': 'Identificarea entităților a eșuat.',
    'The API returned an invalid entity list.': 'API-ul a returnat o listă de entități invalidă.',
    'The API did not return model-backed POS filtering.': 'API-ul nu a returnat filtrarea morfosintactică bazată pe model.',
    'Full text could not be loaded (HTTP {status}).': 'Textul integral nu a putut fi încărcat (HTTP {status}).',
    'Full text length differs from the imported source. Prepare the local examples again.': 'Lungimea textului integral diferă de sursa importată. Pregătiți din nou exemplele locale.',
    'Provide an API URL and input text.': 'Introduceți adresa API și textul de analizat.',
    'Daily limit reached. Try again later.': 'Limita zilnică a fost atinsă. Reîncercați mai târziu.',
    'API returned HTTP {status}': 'API-ul a returnat HTTP {status}',
    'The API returned JSON but no SVG rendering.': 'API-ul a returnat JSON, dar nu și o diagramă SVG.',
    'Diagram generation failed.': 'Generarea diagramei a eșuat.',
    'Request failed (HTTP {status}).': 'Cererea a eșuat (HTTP {status}).',
    'Network request failed. Check the connection and API URL.': 'Cererea de rețea a eșuat. Verificați conexiunea și adresa API.',
    'Sign In': 'Autentificare', 'Sign Out': 'Deconectare', 'Create Account': 'Creare cont',
    'Reset Password': 'Resetare parolă', 'Set New Password': 'Stabiliți o parolă nouă', 'Confirm Email': 'Confirmare e-mail',
    'Passwords do not match.': 'Parolele nu coincid.',
    'Confirmation saved to local test mail. No email was sent.': 'Confirmarea a fost salvată local pentru testare. Nu a fost trimis niciun e-mail.',
    'Check your email to confirm your account, then sign in.': 'Verificați e-mailul pentru a confirma contul, apoi autentificați-vă.',
    'Password updated. You can sign in.': 'Parola a fost actualizată. Vă puteți autentifica.',
    'If a message was needed, it was saved to local test mail. No email was sent.': 'Dacă era necesar un mesaj, acesta a fost salvat local pentru testare. Nu a fost trimis niciun e-mail.',
    'If that account exists, a password-reset email has been sent.': 'Dacă există acest cont, a fost trimis un e-mail pentru resetarea parolei.',
    'If confirmation is needed, an email has been sent.': 'Dacă este necesară confirmarea, a fost trimis un e-mail.',
    'Close': 'Închideți', 'Close sign-in': 'Închideți autentificarea', 'Loading sign-in...': 'Se încarcă autentificarea...',
    'Sign-in is not configured yet.': 'Autentificarea nu este încă configurată.',
    'Sign-in is not configured.': 'Autentificarea nu este configurată.',
    'Email registration is not available yet.': 'Înregistrarea prin e-mail nu este încă disponibilă.',
    'Local test mail only. Messages are saved on this computer, not sent to an inbox.': 'E-mail doar pentru testare locală. Mesajele sunt salvate pe acest calculator, nu sunt trimise într-o căsuță poștală.',
    'Email': 'E-mail', 'Password': 'Parolă', 'Confirm Password': 'Confirmați parola', 'Please wait...': 'Așteptați...',
    'Sign In With Email': 'Autentificare prin e-mail', 'Send Reset Email': 'Trimiteți e-mailul de resetare',
    'Save Password': 'Salvați parola', 'Resend Confirmation': 'Retrimiteți confirmarea',
    'Account options': 'Opțiuni pentru cont', 'Create account': 'Creați un cont', 'Forgot password': 'Ați uitat parola?',
    'Resend confirmation': 'Retrimiteți confirmarea', 'Back to sign in': 'Înapoi la autentificare',
    'Selected text': 'Textul selectat', 'Loading text access...': 'Se verifică accesul la text...',
    'Sign in to read text': 'Autentificați-vă pentru a citi textul', 'Loading text...': 'Se încarcă textul...',
    'Google sign-in could not load.': 'Autentificarea Google nu a putut fi încărcată.',
    'Too many sign-in requests. Please retry later.': 'Prea multe cereri de autentificare. Reîncercați mai târziu.',
    'Sign-in failed. Check your credentials and email confirmation.': 'Autentificarea a eșuat. Verificați datele de acces și confirmarea e-mailului.',
    'Invalid sign-in response.': 'Răspuns de autentificare invalid.', 'Sign in to read text.': 'Autentificați-vă pentru a citi textul.',
    'Sign-in session changed.': 'Sesiunea de autentificare s-a schimbat.', 'Session expired. Sign in again.': 'Sesiunea a expirat. Autentificați-vă din nou.',
    'Signed out here. Server sign-out failed; other sessions may remain active.': 'Ați fost deconectat local. Deconectarea pe server a eșuat; alte sesiuni pot rămâne active.',
    'Email delivery is temporarily unavailable. Retry confirmation or recovery later.': 'Trimiterea e-mailurilor este temporar indisponibilă. Reîncercați confirmarea sau recuperarea mai târziu.',
    'Account storage is temporarily unavailable. Retry in a minute.': 'Baza de date a conturilor este temporar indisponibilă. Reîncercați într-un minut.',
    'Email registration and recovery are unavailable until a mail sender is configured.': 'Înregistrarea și recuperarea prin e-mail nu sunt disponibile până la configurarea trimiterii mesajelor.',
    'Only bearer-token sign-in is supported.': 'Este acceptată doar autentificarea cu token bearer.',
    'Server sign-out failed. Please retry.': 'Deconectarea pe server a eșuat. Reîncercați.',
    'Google sign-in is not configured.': 'Autentificarea Google nu este configurată.',
    'Google verification is temporarily unavailable.': 'Verificarea Google este temporar indisponibilă.',
    'Use a verified Gmail or Google Workspace account, or register with email.': 'Folosiți un cont Gmail sau Google Workspace verificat ori înregistrați-vă prin e-mail.',
    'This email already has an account. Sign in with its existing method.': 'Există deja un cont cu această adresă. Folosiți metoda sa de autentificare.',
    'Unable to create this account. Use email sign-in.': 'Contul nu a putut fi creat. Folosiți autentificarea prin e-mail.',
    'Unable to link Google sign-in.': 'Autentificarea Google nu a putut fi asociată.',
    'Account request too large.': 'Cererea pentru cont este prea mare.',
    'POS tagging is unavailable. Configure the local spaCy Python runtime and English/Romanian models, then retry.': 'Analiza morfosintactică nu este disponibilă. Configurați mediul Python spaCy și modelele pentru engleză și română, apoi reîncercați.',
    'Book or section not found.': 'Cartea sau secțiunea nu a fost găsită.',
    'No text segments were detected.': 'Nu au fost identificate segmente de text.',
    'No event-like clauses were extracted.': 'Nu au fost extrase propoziții care descriu evenimente.',
    'Events without an explicit or carried actor: {count}.': 'Evenimente fără actor explicit sau preluat din context: {count}.',
    'Segments without an explicit temporal anchor: {count}.': 'Segmente fără ancoră temporală explicită: {count}.',
    'Multiple segments were produced without temporal relations.': 'Au fost produse mai multe segmente fără relații temporale.',
    'Average event temporal-category confidence is below 0.55.': 'Încrederea medie în categoria temporală a evenimentelor este sub 0,55.',
    'Average segment type confidence is below 0.60.': 'Încrederea medie în tipul segmentelor este sub 0,60.',
    'The input did not match the configured verb and sentence patterns.': 'Textul nu corespunde tiparelor verbale și propoziționale configurate.',
    'Actor extraction uses named-entity candidates and previous-actor carry-forward.': 'Extragerea actorilor folosește entități numite candidate și preluarea actorului anterior.',
    'Temporal anchors are optional in TYM; use TIMEX3 annotations when exact time evidence is needed.': 'Ancorele temporale sunt opționale în TYM; folosiți adnotări TIMEX3 pentru indicii temporale exacte.',
    'The relation layer depends on adjacency and lexical cue detection.': 'Extragerea relațiilor depinde de adiacență și de identificarea indiciilor lexicale.',
    'Use more temporal cues or train the ML.NET classifier on an annotated TYM/TimeML corpus.': 'Folosiți mai multe indicii temporale sau antrenați clasificatorul ML.NET pe un corpus adnotat TYM/TimeML.',
    'Segment labels fall back to rules when the seed classifier is uncertain.': 'Etichetele segmentelor sunt stabilite prin reguli când clasificatorul inițial este nesigur.',
    'Time segment types follow the paper labels: NAR, REM, SUP, GEN, and FIC.': 'Tipurile de segmente temporale folosesc etichetele din articol: NAR, REM, SUP, GEN și FIC.',
    'The XML output follows the paper notation: TT-SECTION, TIME-SECTION, EP-SECTION, and inline TS tags.': 'Rezultatul XML folosește notația din articol: TT-SECTION, TIME-SECTION, EP-SECTION și etichete TS în text.',
    'The diagram includes a TimeML-style layer with EVENT, TIMEX3, SIGNAL, MAKEINSTANCE, and TLINK annotations.': 'Diagrama include un strat de tip TimeML cu adnotări EVENT, TIMEX3, SIGNAL, MAKEINSTANCE și TLINK.',
    'TT titles are short extractive event summaries, not LLM-generated or validated reference summaries.': 'Titlurile TT sunt rezumate extractive scurte ale evenimentelor, nu rezumate generate de un LLM sau validate ca referință.',
    'Romanian extraction uses non-LLM rule profiles for Romanian entities, temporal cues, tense, and TimeML-like annotations.': 'Extragerea în română folosește reguli fără LLM pentru entități, indicii temporale, timpuri verbale și adnotări de tip TimeML.',
    'English extraction uses non-LLM rules plus ML.NET seed classifiers for event temporal category and TS type.': 'Extragerea în engleză folosește reguli fără LLM și clasificatoare ML.NET inițiale pentru categoria temporală a evenimentelor și tipul TS.',
    'Romanian mode intentionally avoids the English ML.NET seed classifiers; train Romanian annotated TYM/TimeML data for production use.': 'Modul românesc nu folosește clasificatoarele ML.NET inițiale pentru engleză; utilizarea în producție necesită antrenare pe date românești adnotate TYM/TimeML.',
    'ML.NET calculates event temporal category and TS type from bundled paper-guided seed examples; replace these with an annotated corpus for production use.': 'ML.NET calculează categoria temporală a evenimentelor și tipul TS din exemplele inițiale bazate pe articol; pentru producție, înlocuiți-le cu un corpus adnotat.',
    'n/a': 'n/d',
    'An account already exists with this email.': 'Există deja un cont cu această adresă de e-mail.',
    'Use a valid email address.': 'Folosiți o adresă de e-mail validă.',
    'Use at least 12 characters, uppercase and lowercase letters, a number and a symbol.': 'Folosiți cel puțin 12 caractere, litere mari și mici, o cifră și un simbol.',
    'The confirmation or reset code is invalid or expired.': 'Codul de confirmare sau resetare este invalid ori a expirat.',
    'Jakarta skyline photographed in 2017': 'Panoramă a orașului Jakarta, fotografiată în 2017',
    'Frontispiece of the 1871 Hetzel edition of Twenty Thousand Leagues Under the Seas': 'Frontispiciul ediției Hetzel din 1871 a romanului Douăzeci de mii de leghe sub mări',
    'Twenty Thousand Leagues, 1871': 'Douăzeci de mii de leghe, 1871', 'Portrait of Jules Verne in 1892': 'Portretul lui Jules Verne din 1892',
    'Cover of the 1843 edition of A Christmas Carol': 'Coperta ediției din 1843 a romanului Colind de Crăciun',
    'A Christmas Carol, 1843': 'Colind de Crăciun, 1843', 'Public domain': 'Domeniu public',
    'Legend': 'Legendă', 'Solid line: narrative continuity': 'Linie continuă: continuitate narativă',
    'Dotted line: REM/SUP/GEN/FIC segment': 'Linie punctată: segment REM/SUP/GEN/FIC',
    'Triangle: track start/stop': 'Triunghi: început/sfârșit de pistă', 'Blue box: TS marker': 'Casetă albastră: marcaj TS',
    'Vertical tick: segment boundary': 'Marcaj vertical: limita segmentului', 'Endpoint text: JOIN/SPLIT': 'Text la capete: JOIN/SPLIT',
    'Narration': 'Narațiune', 'Memory / flashback': 'Amintire / retrospectivă', 'Supposition': 'Presupunere',
    'Generalization': 'Generalizare', 'Fiction / imagined event': 'Ficțiune / eveniment imaginat',
    'Tracks meet': 'Pistele se unesc', 'Tracks separate': 'Pistele se separă',
    'NAR: Narration': 'NAR: Narațiune', 'REM: Memory / flashback': 'REM: Amintire / retrospectivă',
    'SUP: Supposition': 'SUP: Presupunere', 'GEN: Generalization': 'GEN: Generalizare',
    'FIC: Fiction / imagined event': 'FIC: Ficțiune / eveniment imaginat', 'JOIN: Tracks meet': 'JOIN: Pistele se unesc',
    'SPLIT: Tracks separate': 'SPLIT: Pistele se separă',
    'Text order overview; large diagrams show evenly spaced segment IDs.': 'Ordinea din text; diagramele mari afișează ID-urile segmentelor la distanțe egale.',
    'Present': 'Prezent', 'Past': 'Trecut', 'Future': 'Viitor', 'Unknown': 'Necunoscut',
    'Before': 'Înainte', 'After': 'După', 'Simultaneous': 'Simultan', 'PERSON': 'Persoană', 'LOCATION': 'Loc', 'REFERENCE': 'Referință',
    '{count} characters': { one: '{count} caracter', few: '{count} caractere', other: '{count} de caractere' },
    '{count} complete books': { one: '{count} carte integrală', few: '{count} cărți integrale', other: '{count} de cărți integrale' },
    '{count} entities detected': { one: '{count} entitate identificată', few: '{count} entități identificate', other: '{count} de entități identificate' },
    '{count} tracks': { one: '{count} pistă', few: '{count} piste', other: '{count} de piste' },
    'Daily limit reached. Try again in about {count} hours.': { one: 'Limita zilnică a fost atinsă. Reîncercați peste aproximativ {count} oră.', few: 'Limita zilnică a fost atinsă. Reîncercați peste aproximativ {count} ore.', other: 'Limita zilnică a fost atinsă. Reîncercați peste aproximativ {count} de ore.' },
    '{selected} / {total} characters': '{selected} / {total} caractere'
  };
  const en = Object.fromEntries(Object.keys(ro).map(key => [key, key]));
  Object.assign(en, {
    '{count} characters': { one: '{count} character', other: '{count} characters' },
    '{count} complete books': { one: '{count} complete book', other: '{count} complete books' },
    '{count} entities detected': { one: '{count} entity detected', other: '{count} entities detected' },
    '{count} tracks': { one: '{count} track', other: '{count} tracks' },
    'Daily limit reached. Try again in about {count} hours.': { one: 'Daily limit reached. Try again in about {count} hour.', other: 'Daily limit reached. Try again in about {count} hours.' }
  });
  const catalogs = { en, ro };
  function supported(value) { const language = String(value || '').toLowerCase().split(/[-_]/)[0]; return Object.hasOwn(names, language) ? language : null; }
  function resolveLocale(saved, preferred = []) { return supported(saved) || preferred.map(supported).find(Boolean) || 'en'; }
  const listeners = new Set();
  let saved;
  try { saved = root.localStorage?.getItem('tym-ui-locale'); } catch {}
  let locale = resolveLocale(saved, root.navigator?.languages || [root.navigator?.language]);
  function format(language, message, params = {}) {
    if (!message) return '';
    if (typeof message === 'object') { params = message.params || {}; message = message.key; }
    const lang = supported(language) || 'en';
    message = String(message || '');
    let template = Object.hasOwn(catalogs[lang], message) ? catalogs[lang][message]
      : Object.hasOwn(catalogs.en, message) ? catalogs.en[message] : message;
    if (typeof template === 'object') template = template[new Intl.PluralRules(lang).select(Number(params.count))] || template.other;
    return template.replace(/\{(\w+)\}/g, (match, name) => !Object.hasOwn(params, name) ? match
      : name === 'count' && typeof params[name] === 'number' ? new Intl.NumberFormat(lang).format(params[name]) : String(params[name]));
  }
  const t = (message, params) => format(locale, message, params);
  function metadata() {
    if (!root.document) return;
    root.document.documentElement.lang = locale;
    root.document.title = t('TYM Diagram UI');
  }
  function setLocale(value, persist = true) {
    const next = supported(value) || 'en';
    if (persist) { try { root.localStorage?.setItem('tym-ui-locale', next); } catch {} }
    if (locale === next) return;
    locale = next; metadata(); for (const listener of listeners) listener();
  }
  const message = (key, params = {}) => ({ key, params });
  function error(key, params) { const value = new Error(format('en', key, params)); value.i18n = message(key, params); return value; }
  function describe(value) {
    if (value?.i18n) return value.i18n;
    if (value instanceof TypeError) return message('Network request failed. Check the connection and API URL.');
    return value?.message || value;
  }
  function apiError(data, status) {
    if (data?.error) return error(data.error);
    const codes = Object.keys(data?.errors || {});
    if (codes.some(code => code.startsWith('Password'))) return error('Use at least 12 characters, uppercase and lowercase letters, a number and a symbol.');
    if (codes.some(code => ['DuplicateEmail', 'DuplicateUserName'].includes(code))) return error('An account already exists with this email.');
    if (codes.includes('InvalidEmail')) return error('Use a valid email address.');
    if (codes.includes('InvalidToken')) return error('The confirmation or reset code is invalid or expired.');
    if (status === 401) return error('Sign-in failed. Check your credentials and email confirmation.');
    return error('Request failed (HTTP {status}).', { status });
  }
  function localizeSvg(svg, language = locale) {
    if (!svg || !root.DOMParser) return svg;
    const doc = new root.DOMParser().parseFromString(svg, 'image/svg+xml');
    if (doc.querySelector('parsererror')) return svg;
    const element = doc.documentElement;
    // Only presentation-owned legend/tooltips are translated; narrative excerpts and IDs stay verbatim.
    for (const text of element.querySelectorAll('[data-section="legend"] text, [data-tag-icon] title, [data-section="text-order"] > title'))
      text.textContent = format(language, text.textContent);
    element.querySelector('[data-section="legend"]')?.setAttribute('lang', language);
    if (element.hasAttribute('aria-label')) element.setAttribute('aria-label', format(language, element.getAttribute('aria-label')));
    return new root.XMLSerializer().serializeToString(element);
  }
  metadata();
  root.addEventListener?.('storage', event => { if (event.key === 'tym-ui-locale') setLocale(resolveLocale(event.newValue, root.navigator?.languages || []), false); });
  const api = { names, catalogs, resolveLocale, format, t, message, error, describe, apiError, localizeSvg, setLocale,
    getLocale: () => locale, number: value => new Intl.NumberFormat(locale).format(value),
    subscribe(listener) { listeners.add(listener); return () => listeners.delete(listener); },
    useLocale() { const language = root.React.useSyncExternalStore(api.subscribe, api.getLocale); return { locale: language, t, setLocale }; }
  };
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.TymI18n = api;
})(typeof window === 'object' ? window : globalThis);
