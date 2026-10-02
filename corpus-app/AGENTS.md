# Corpus project conventions

- This project exposes an exploratory ML.NET lexical baseline. Keep TYM labels and ISO-TimeML labels in separate tasks.
- Provided annotations have unknown adjudication status. Prediction snapshots check program behavior and are not NLP accuracy scores.
- Preserve document and translation grouping. The parallel Tash Aw XML pair is one source work.
- Keep corpus text, converted passages, trained model weights, and private build contexts outside Git.
- Raw books contain no supervised labels. Use them for explicitly unlabeled clustering and coverage inspection.
- The reported historical CNN scores in the supplied paper have not been reproduced by this project.
- Keep the frontend independent of external script/CDN dependencies. Include provenance and clear task-specific input guidance.
