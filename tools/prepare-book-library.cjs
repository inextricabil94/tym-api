const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { cleanBookText, bookSections } = require('./book-text.cjs');

const source = path.resolve(process.argv[2] || 'private-examples');
const destination = path.resolve(process.argv[3] || 'build_publish/book-library/library');
if (fs.existsSync(destination)) throw new Error('Choose a new output directory; existing libraries are never overwritten.');
const examples = JSON.parse(fs.readFileSync(path.join(source, 'examples.json'), 'utf8'));
const books = examples.filter(item => item.is_private).map(item => {
  if (!/^[a-z0-9-]+$/.test(item.id)) throw new Error('Invalid book ID.');
  const original = fs.readFileSync(path.join(source, 'full-text', `${item.id}.txt`), 'utf8');
  const hash = crypto.createHash('sha256').update(original, 'utf8').digest('hex');
  if (original.length !== item.full_text_characters || hash !== item.full_text_sha256_utf8)
    throw new Error(`Original integrity check failed: ${item.id}`);
  const text = cleanBookText(original);
  return { metadata: { id: item.id, label: item.label, language: item.language,
    source: item.full_source, character_count: text.length, original_character_count: original.length,
    sha256_utf8: crypto.createHash('sha256').update(text, 'utf8').digest('hex'),
    sections: bookSections(text) }, text };
});
if (!books.length) throw new Error('No complete books found.');
fs.mkdirSync(destination, { recursive: true });
for (const book of books) fs.writeFileSync(path.join(destination, `${book.metadata.id}.txt`), book.text, 'utf8');
fs.writeFileSync(path.join(destination, 'catalog.json'), `${JSON.stringify(books.map(book => book.metadata), null, 2)}\n`, 'utf8');
console.log(JSON.stringify({ books: books.length, characters: books.reduce((sum, book) => sum + book.text.length, 0),
  sections: books.reduce((sum, book) => sum + book.metadata.sections.length, 0), output: destination }));
