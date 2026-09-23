# Reading Studio multilingual acceptance

## Purpose

This playbook captures the visual and synchronization evidence that automated Unicode and WPF shaping tests cannot prove. Run it on the Windows 11 release candidate after changes to reader languages, fonts, line metrics, highlighting, timing, OCR, or video rendering.

## Coverage model

Use two passes:

1. **Script presentation pass:** test one registry language for every `ReaderScriptFamily`. This validates font selection, shaping, direction, line spacing, focus modes, and video layout shared by that script.
2. **Language narration pass:** test every provider-qualified row visible in the Language menu. This validates the selected voice, pronunciation, timing behavior, capability summary, and OCR limitation independently of shared typography.

Do not treat one language as evidence for a different provider row with the same language code.

## Script presentation pass

For each declared script family, paste the corpus sample from `UnicodeReadingTextSegmenterTests.ScriptCorpus` and verify:

- the default typeface is compatible and no missing-glyph boxes appear;
- combining marks, joined letters, and emoji remain intact;
- page, focused sentence, and focused word modes do not clip ascenders, descenders, vowel marks, or underlines;
- paragraph direction and alignment are correct, including a mixed LTR/RTL two-paragraph sample;
- word and sentence highlighting follow the same logical token in normal playback;
- landscape and vertical video frames match the live reader's font, direction, wrapping, and highlight target.
- all seven highlight styles remain recognizable in word and sentence modes; complex scripts use weight instead of underline where declared;
- changing the active word in landscape or vertical export does not move, reshape, brighten, or dim text outside the active highlight region.

Capture the script family, language/provider row, Windows build, selected font ID, reader mode, video format, outcome, and screenshot or frame path.

## Language narration pass

For every visible language/provider row:

- generate the local preview and confirm it is for the selected language and voice;
- prepare a short native-language paragraph with punctuation;
- confirm the capability summary accurately reports native timing with deterministic fallback or local forced alignment, plus OCR availability;
- confirm the ready status reports the observed source as native, locally aligned, or estimated timing;
- verify that spoken-word highlighting remains monotonic and reaches the final token;
- for experimental rows, confirm the experimental label remains visible;
- for OCR-unavailable rows, confirm searchable text imports work and scanned-image import fails with the explicit OCR message;
- export one short clip for any row whose behavior differs from the script presentation pass.

## Required evidence

Record `PASS`, `FAIL`, or `BLOCKED` for each row. A `BLOCKED` result must name the missing runtime, model, hardware, or test artifact; it is not a pass. Attach screenshots for clipping, missing glyphs, wrong direction, or highlight drift. Do not update this document with claimed results unless the checks were actually performed.

Release acceptance requires:

- every script-presentation row passing;
- every non-experimental language narration row passing;
- every experimental failure documented as a known limitation;
- the automated Release build and full test suite passing after the manual run.
