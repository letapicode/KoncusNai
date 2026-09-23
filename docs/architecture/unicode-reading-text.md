# Unicode reading text architecture

Reading Studio keeps text segmentation independent from WPF rendering and speech providers. `UnicodeReadingTextSegmenter` is the single boundary that converts logical source text into highlight tokens and paragraph spans.

## Invariants

- Token boundaries use .NET text elements, which implement Unicode extended grapheme clusters. A combining sequence or joined emoji is therefore never divided between highlight tokens.
- Every token stores an exact UTF-16 source start and length. Consumers that need narration or export text recover it from those spans instead of rebuilding source text from token strings.
- Han, Japanese kana, and Hangul retain the existing one-text-element-per-highlight behavior. Other scripts remain whitespace-delimited, with sentence punctuation that already had standalone reader behavior kept separate.
- Paragraph direction follows the first strong letter rule used by Unicode Bidirectional Algorithm rules P2 and P3 for the scripts the application supports. Each paragraph is resolved independently; WPF remains responsible for visual bidirectional layout.
- Segmentation is content-aware. Language metadata selects fonts, metrics, expected direction, OCR, and timing capabilities, but code does not branch on individual language names outside `ReaderLanguageRegistry`.

## Standards profile

The implementation follows [Unicode Text Segmentation (UAX #29)](https://www.unicode.org/reports/tr29/) for extended grapheme boundaries through `StringInfo.ParseCombiningCharacters`. It applies a deliberately small reader-token tailoring after grapheme segmentation: whitespace separates tokens, CJK text elements are individual tokens, and the reader's existing standalone sentence punctuation remains individual tokens.

Paragraph direction follows the first-strong portion of [Unicode Bidirectional Algorithm (UAX #9)](https://www.unicode.org/reports/tr9/). This component preserves logical source order and does not attempt visual reordering.

The live reader applies direction to one WPF inline span per paragraph. Video pagination ends a page before a direction change and passes the page direction to `FormattedText`, so logical word indices remain stable for highlighting in both LTR and RTL output.

Any future language-specific tailoring belongs in a named segmentation profile and must preserve the source-span and grapheme-boundary invariants above.
