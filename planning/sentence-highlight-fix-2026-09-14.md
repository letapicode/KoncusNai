# Continuous sentence highlighting

Implemented for Reading Studio's Sentence on Page and Focused Sentence views.

The old renderer painted padded word controls and text-run spaces independently. Their different bounds produced disconnected rectangles. The new ReaderHighlightTextBlock draws the sentence behind the arranged inline controls, grouping physical lines and consecutive highlighted words in visual order. Unhighlighted words interrupt a band, including in bidirectional text. Rounded bands share one fill geometry so overlapping line bounds do not accumulate translucent color.

Word controls remain available for click-to-seek. Highlighting does not change font metrics, padding, or line breaks. Layout updates refresh the drawing after resizing, font changes, and sentence changes. Word mode retains its existing treatment; sentence outlines also use line geometry. Export rendering was not changed.

Validation: 12 targeted tests pass, including actual ReaderDocumentView rendering at 29 and 42 point sizes in both modes, sentence transitions, stable word positions, wrapped/centered/right-to-left text, resizing, reset behavior, and pixel coverage across spaces. Release solution build has zero warnings and errors. Rendered fixtures were visually inspected under artifacts/sentence-highlights. Full solution acceptance results are recorded in artifacts/sentence-highlights-acceptance.log.

Full solution acceptance: 1235 passed, 0 failed, 5 skipped.
