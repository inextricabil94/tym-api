# Lucide icons

The embedded diagram symbols use Lucide's `play`, `rewind`, `circle-question-mark`, `repeat-2`, `sparkles`, `merge`, and `split` SVG paths. The UI also uses `chevron-left`, `chevron-right`, `sun`, `moon`, and `rotate-cw`. Source: https://github.com/lucide-icons/lucide/tree/main/icons (retrieved 19 October 2026).

## ISC License

Copyright (c) 2026 Lucide Icons and Contributors

Permission to use, copy, modify, and/or distribute this software for any purpose with or without fee is hereby granted, provided that the above copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.

## Feather-Derived Icons: MIT License

Copyright (c) 2013-present Cole Bemis

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

# Bundled Fonts

The UI reuses the self-hosted Space Grotesk, Inter, and IBM Plex Mono font files supplied in the user's DataSpace project. Fonts are distributed under the SIL Open Font License 1.1. Unmodified copyright and license notices accompany the binaries in `ui/Tym.Ui/wwwroot/assets/fonts/OFL-spacegrotesk.txt`, `OFL-inter.txt`, and `OFL-ibmplexmono.txt`. Both Latin and Latin Extended subsets are included for Romanian text.

# Optional Local POS Runtime and Models

The entity highlight filter uses [spaCy](https://github.com/explosion/spaCy) (MIT), with official [en_core_web_sm 3.8.0](https://github.com/explosion/spacy-models/releases/tag/en_core_web_sm-3.8.0) (MIT) and [ro_core_news_sm 3.8.0](https://github.com/explosion/spacy-models/releases/tag/ro_core_news_sm-3.8.0) (CC BY-SA 4.0) models from Explosion and the contributors/data sources named in each model's metadata. The Romanian model's metadata identifies [UD Romanian RRT v2.8](https://github.com/UniversalDependencies/UD_Romanian-RRT) (CC BY-SA 4.0; Barbu Mititelu, Verginica; Irimia, Elena; Perez, Cenel-Augusto; Ion, Radu; Simionescu, Radu; Popel, Martin) and [RONEC](https://github.com/dumitrescustefan/ronec) (MIT; Dumitrescu, Stefan Daniel; Avram, Andrei-Marius; Morogan, Luciana; Toma; Stefan) as sources. CC BY-SA 4.0 terms: https://creativecommons.org/licenses/by-sa/4.0/ .

These packages/weights are downloaded separately, are unmodified, and are not bundled in this repository or its .NET publish output. The API Dockerfile also installs them in its runtime image. Their original `LICENSE`, `LICENSES_SOURCES` (where supplied) and `meta.json` files remain in the installed packages. Keep those notices and review the applicable model/source license obligations before redistributing weights or adapting and distributing a model.
