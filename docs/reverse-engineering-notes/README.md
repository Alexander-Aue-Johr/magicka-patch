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

- [Paradox store price render-thread hang](paradox-store-price-render-thread-hang.md)
  documents the reproducible 100-second intro-to-loading pause, the staged
  render-thread diagnosis, the obsolete HTTP endpoint, and the asynchronous
  price-refresh guard.
- [In-game UI render scaling](in-game-ui-render-scaling.md) explains the
  virtual gameplay-GUI render target, render-thread-only size accessors,
  pre-rendered position conversion, contextual notifier alignment, and menu
  mouse-coordinate handling.
- [Missing animation clip compatibility guard](animation-clip-missing-key-guard.md)
- [Startup, controller, version-text, and supporter dialog guards](startup-controller-and-supporter-dialog-guards.md)
  covers absolute startup file resolution, launch-option bounds checks, the
  rare modified-installation footer overflow, graceful missing-DirectInput
  behavior, and the clickable supporter list.
- [Borderless fullscreen loading stability](borderless-fullscreen-loading.md)
  explains how logical fullscreen is retained while non-exclusive Direct3D 9
  presentation prevents focus-related device loss during asset loading and
  how mouse coordinates stay aligned at non-native render resolutions.
- [Menu content unload guard](menu-content-unload-guard.md) explains how the
  character-selection texture lifetime follows the `Tome` draw window and its
  cached render channels instead of only `Tome.CurrentMenu`.
- [Runtime null and boss ordering guards](runtime-null-and-boss-ordering-guards.md)
  covers Avatar cache misses, detached player controllers, optional gameplay
  telemetry, SpawnNPC WorldSync validation, network template-cache validation,
  and deferred client boss setup.
- [Avatar cache expansion CLR compatibility](avatar-cache-expansion-clr-compatibility.md)
  documents the short-branch overflow in the expanded Avatar cache fallback
  and the CLR-2-compatible long branch.
- [Windows CLR and Wine Mono compatibility audit](windows-mono-runtime-compatibility-audit.md)
  separates Linux installer work from game-runtime changes in 0.0.42 through
  0.0.45 and documents the release-time compatibility checks.
- [Collection growth CLR compatibility](static-list-growth-clr-compatibility.md)
  documents the CLR-4 lock overload introduced while adding dynamic capacity
  and the equivalent CLR-2 lock sequence.
- [GC retention root analysis](gc-retention-root-analysis.md) documents the
  CLR-2-compatible external analyzer, candidate resolution, bounded root-path
  findings, and release-package layout.

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
