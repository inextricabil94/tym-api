const assert = require('node:assert/strict');
const test = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const i18n = require('../../ui/Tym.Ui/wwwroot/i18n.js');
const chapters = require('../../ui/Tym.Ui/wwwroot/chapter-splitter.js');

test('locale resolution prefers a supported saved choice, then browser preferences', () => {
  assert.equal(i18n.resolveLocale('ro', ['en-US']), 'ro');
  assert.equal(i18n.resolveLocale('xx', ['fr-FR', 'ro-RO', 'en']), 'ro');
  assert.equal(i18n.resolveLocale(null, ['en-GB', 'ro']), 'en');
  assert.equal(i18n.resolveLocale(null, ['fr']), 'en');
});
test('every catalog entry has Romanian and English text with matching placeholders', () => {
  assert.deepEqual(Object.keys(i18n.catalogs.en).sort(), Object.keys(i18n.catalogs.ro).sort());
  const placeholders = text => [...new Set([...text.matchAll(/\{(\w+)\}/g)].map(match => match[1]))].sort();
  for (const key of Object.keys(i18n.catalogs.en)) {
    for (const language of ['en', 'ro']) {
      const entry = i18n.catalogs[language][key];
      for (const text of typeof entry === 'string' ? [entry] : Object.values(entry)) {
        assert.ok(text.trim(), `${language}: ${key}`);
        assert.deepEqual(placeholders(text), placeholders(key), `${language}: ${key}`);
      }
    }
  }
});
test('Romanian plurals distinguish one, few and many; English handles one and many', () => {
  for (const [count, expected] of [[0, '0 piste'], [1, '1 pistă'], [2, '2 piste'], [19, '19 piste'], [20, '20 de piste'], [100, '100 de piste']])
    assert.equal(i18n.format('ro', '{count} tracks', { count }), expected);
  assert.equal(i18n.format('en', '{count} tracks', { count: 1 }), '1 track');
  assert.equal(i18n.format('en', '{count} tracks', { count: 2 }), '2 tracks');
  assert.equal(i18n.format('ro', '{count} characters', { count: 12345 }), '12.345 de caractere');
});
test('privacy restrictions on localStorage do not prevent locale changes', () => {
  const window = { localStorage: { getItem() { throw new Error('blocked'); }, setItem() { throw new Error('blocked'); } }, navigator: { languages: ['ro-RO'] } };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../ui/Tym.Ui/wwwroot/i18n.js'), 'utf8'), { window });
  assert.equal(window.TymI18n.getLocale(), 'ro');
  window.TymI18n.setLocale('en');
  assert.equal(window.TymI18n.getLocale(), 'en');
});
test('structured messages and API validation errors translate after a locale change', () => {
  const value = i18n.message('Caching density {density}...', { density: 70 });
  assert.equal(i18n.format('en', value), 'Caching density 70...');
  assert.equal(i18n.format('ro', value), 'Se memorează densitatea 70...');
  const error = i18n.apiError({ errors: { DuplicateUserName: ["Username 'private@example.test' is taken."] } }, 400);
  assert.equal(i18n.format('ro', i18n.describe(error)), 'Există deja un cont cu această adresă de e-mail.');
  assert.ok(!error.message.includes('private@example.test'));
  assert.equal(i18n.format('ro', i18n.describe(new TypeError('Failed to fetch'))), 'Cererea de rețea a eșuat. Verificați conexiunea și adresa API.');
});
test('locale subscriptions, number formatting and unknown technical values are safe', () => {
  let changes = 0;
  const unsubscribe = i18n.subscribe(() => changes++);
  i18n.setLocale('en'); changes = 0;
  i18n.setLocale('ro'); i18n.setLocale('ro');
  assert.equal(changes, 1);
  assert.equal(i18n.number(12345), '12.345');
  assert.equal(i18n.t('TLINK_UNKNOWN_ID'), 'TLINK_UNKNOWN_ID');
  assert.equal(i18n.t('constructor'), 'constructor'); assert.equal(i18n.t('__proto__'), '__proto__');
  assert.equal(i18n.t(''), ''); assert.equal(i18n.t(null), '');
  unsubscribe(); i18n.setLocale('en'); assert.equal(changes, 1);
});
test('explicit interface language changes chapter labels, never source metadata', () => {
  const section = { kind: 'chapter', label: 'CAPITOLUL DOUĂZECI ȘI UNU / Part 2', start: 0, end: 50 };
  const original = JSON.stringify(section);
  assert.equal(chapters.sectionLabel(section, 0, 'en', { useHeadingLanguage: false }), 'Chapter 21 / Section 2');
  assert.equal(chapters.sectionLabel(section, 0, 'ro', { useHeadingLanguage: false }), 'Capitolul 21 / Secțiunea 2');
  assert.equal(chapters.sectionLabel(section, 0, 'en'), 'Capitolul 21 / Secțiunea 2');
  assert.equal(JSON.stringify(section), original);
});
test('literal UI translation calls have catalog entries', () => {
  for (const name of ['app.js', 'accounts.js']) {
    const source = fs.readFileSync(path.join(__dirname, '../../ui/Tym.Ui/wwwroot', name), 'utf8');
    for (const match of source.matchAll(/(?:\bt|I18n\.(?:message|error))\('([^']+)'/g)) {
      // DOM element names are not translation calls.
      assert.ok(Object.hasOwn(i18n.catalogs.en, match[1]), `${name}: ${match[1]}`);
    }
  }
});
