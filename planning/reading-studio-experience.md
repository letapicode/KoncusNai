# Reading Studio experience

> **Historical record:** This product proposal preserves Reading Studio design
> rationale; it is not a current product contract or status owner. Use
> [`docs/documentation-map.md`](../docs/documentation-map.md) for current owners.

## Purpose

Reading Studio is the long-form, local-only reading surface for books and documents. It replaces the old one-shot document narration action with a dedicated window that remains useful while audio is being prepared.

## Flow

1. The user chooses the book/document icon in the Notype composer.
2. The file is extracted locally and opened in Reading Studio. EPUB, PDF, DOCX, TXT, Markdown, RTF, HTML, CSV, JSON, and XML are supported.
3. Text is normalized into paragraph-aware sections sized for responsive Kokoro synthesis.
4. The first section has an explicit preparation overlay. The reader then begins on demand, while the next section is generated in the background.
5. The reader shows an estimated whole-document progress and keeps the current section readable even when audio is paused.

## Controls

- Play/pause and previous/next section navigation.
- 0.75x–2.00x playback speed.
- 18–42 point text size.
- Book serif, focus sans, and hand-drawn typography choices.
- Optional current-word highlighting.

## Highlighting

Kokoro provides audio rather than word timestamps. Reading Studio therefore creates a stable proportional timing map from each section's WAV duration and its normalized word sequence. The current word is a helpful reading cue, with a visible toggle for users who prefer an uninterrupted page.

## Privacy and performance

Book text stays on the device. Sections are synthesized locally and the following section is prefetched during playback. This reduces long-form latency without requiring a cloud service or asking the user to prepare an entire book before the first page can be read.
