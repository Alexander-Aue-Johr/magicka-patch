# Dialog and tutorial line breaks

The original localization tables encode structured text in two different ways.
The Simplified Chinese tables contain literal line feeds. The original shipped
language tables instead place spaces between the logical sections and rely on
automatic font wrapping. This can merge multiple dialogue beats or tutorial
properties onto one line.

`Magicka.GameLogic.UI.Message.Initialize` now passes the resolved dialogue
through `DialogLayoutCompatibility.RestoreDialogListBreaks` before calling
`BitmapFont.Wrap`. The helper inserts a line feed only when a pause tag such as
`[P=0.5]` is followed by optional horizontal whitespace and a single list dash.
Pause tags used inside normal prose, including Magicka's `[P=...] --` dramatic
asides, are unchanged.

`Magicka.Levels.Triggers.Actions.SetDialogHint.Initialize` now passes the raw
localized hint through `RestoreElementHintBreaks` before resolving localization
references. The helper replaces double-space separators only when the text
contains all three element-hint markers: `#TYPE;`, `#PROP;`, and `#OPP;`. It
restores a blank line before each of those section headings and one line break
between entries within a section. Other tutorial hints and prose remain
unchanged.

Existing line feeds remain intact. In particular, the Simplified Chinese
element and dialogue layouts do not receive duplicate breaks.
