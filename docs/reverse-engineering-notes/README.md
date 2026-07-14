# Reverse Engineering Notes

This directory documents changes made inside existing Magicka classes without
publishing decompiled source for those original classes.

Use this area for patch-site notes: class and method names, observed failure
modes, changed behavior, telemetry reason codes, and high-level pseudocode that
explains the patch without reproducing the original implementation.

Do not add full decompiled game classes here. Newly authored classes that belong
to the community patch itself are documented separately under
`docs/injected-source/`.

This is a transparency compromise: players can inspect what the patch changes
and what data it may report, while the repository avoids redistributing large
parts of a closed-source game.

Patch-site notes:

- [Borderless fullscreen loading stability](borderless-fullscreen-loading.md)
  explains how logical fullscreen is retained while non-exclusive Direct3D 9
  presentation prevents focus-related device loss during asset loading.

Legal note: this repository is not legal advice. EU Directive 2009/24/EC treats
computer programs as copyright-protected works and gives rightholders exclusive
rights over reproduction, translation, adaptation, alteration, and distribution,
subject to limited exceptions. GitHub also operates a DMCA notice-and-takedown
process for allegedly infringing repository content. For that reason, patch
notes in this directory should describe modifications rather than copy the
original decompiled source.

References:

- EU Directive 2009/24/EC on the legal protection of computer programs: https://eur-lex.europa.eu/legal-content/EN/TXT/HTML/?uri=CELEX:32009L0024
- GitHub DMCA Takedown Policy: https://docs.github.com/en/site-policy/content-removal-policies/dmca-takedown-policy
