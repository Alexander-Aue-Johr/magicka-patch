# WIP Windows and CLR 2 fixes

This file records the local commits that were combined into the transferable
WIP commit on 2026-08-28. Remove it when the final issue-based commits have
been reconstructed or reviewed.

- `16e95ce Fix Windows store delegate loading (#38)`
- `67eae50 Fix CLR compatibility in patched payload (#45)`
- `c5487ad Use CLR-2 collection lock calls (#46)`
- `64436f4 Report GC analyzer failure stage (#40)`
- `3540d5a Support CLR 2 in GC analyzer (#40)`

The intended final history keeps issues #38, #45, and #46 separate and combines
the two issue #40 analyzer commits into one final commit.

The ignored Grease retention-test executables and their patching work directory
under `tmp/` are deliberately not part of the product changes.
