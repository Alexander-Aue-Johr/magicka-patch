# Runtime-Patcher-Bericht

Stand: 6. September 2026

## Zweck und Verifikationsgrenze

Der Runtime-Patcher übernimmt die semantischen Änderungen der manuell
bearbeiteten Community-Patch-Assembly schrittweise. Für jede Änderung wird die
kürzeste gut lesbare Harmony-Technik gewählt:

- Prefix oder Postfix für Verhalten vor oder nach einer Methode;
- ein boolescher Prefix, wenn die Originalmethode bedingt übersprungen wird;
- ein Transpiler nur für kleine Änderungen innerhalb einer großen Methode,
  wenn Prefix oder Postfix mehr oder unklareren Code erzeugen würden.

Die Verifikation vergleicht beobachtbares Verhalten. Derselbe Test muss den
Fehler im Original erkennen, in der manuellen Patch-Assembly bestehen und im
Original mit Runtime-Patch ebenfalls bestehen. Kontrollfälle müssen in allen
drei Profilen bestehen. Das beweist die getesteten Szenarien, aber keine
vollständige mathematische Gleichheit beliebiger Programmzustände.

## Schnell einarbeiten

Diese Dateien in dieser Reihenfolge öffnen:

1. `src/RuntimePatch/RuntimePatchPlan.cs` zeigt auf einer Bildschirmseite,
   welche Patchgruppen beim Start angewendet werden.
2. `src/RuntimePatch/AvatarFindInteractablePatch.cs` zeigt einen Prefix mit
   typgenauem Null-Ergebnis für eine abgelöste Szenenkette.
3. `src/RuntimePatch/AIStateAttackOnExecutePatch.cs` zeigt einen Prefix, der
   einen abgelösten Zielkörper vor dem ursprünglichen Update behandelt.
4. `src/RuntimePatch/EntityManagerClosestDamageablePatch.cs` zeigt einen
   Transpiler, der genau einen vorhandenen Schleifen-Ausstieg wiederverwendet.
5. `src/RuntimePatch/HelperArrayEqualsPatch.cs` zeigt den vollständigen Ersatz
   einer kleinen reinen Funktion durch einen Prefix.
6. `src/RuntimePatch/InventoryBoxDrawPatch.cs` zeigt den einfachsten
   gewöhnlichen Prefix.
7. `src/RuntimePatch/HUDManagerInitialisePatch.cs` zeigt einen Postfix;
   `HUDManagerPatchPlan.cs` behandelt Versionen ohne diese HUD-Implementierung.
8. `src/RuntimePatch/MachineNetworkInitializePatch.cs` zeigt einen eng
   begrenzten Transpiler innerhalb einer bestehenden Methode.
9. `src/RuntimePatch/PlayStatePatchPlan.cs` und
   `src/RuntimePatch/PlayStateAddWorldSyncMessagePatch.cs` zeigen eine
   versionsabhängige Patchgruppe und einen booleschen Prefix.
10. `src/RuntimePatch/RuntimePatchSession.cs` zeigt den gemeinsamen
   Harmony-Ablauf: Ziel suchen, Patch registrieren und Registrierung prüfen.
11. `src/RuntimePatch/RuntimePatchDefinition.cs` ist der kleine Vertrag
   zwischen Patchplan und Session.
12. `src/RuntimePatch/Bootstrap.cs` ist der Einstieg aus Magicka.
13. `src/BehaviorProbe/BehaviorSuite.cs` und die zwölf `*Scenarios.cs`-Dateien
   enthalten die realen Szenarien und ihre minimalen Reflection-Harnesses.
   `Program.cs` lädt nur die gewünschte echte Assembly.
14. `build.ps1` liest sich als vollständiger Build- und Prüfablauf.
15. `reference/verified-assemblies.txt` ist der maschinenlesbare Versionsvertrag
   für Original, manuelle Patch-Assembly und Kompatibilitätsversionen.
16. `src/AssemblyPatching/RuntimeLoaderInjection.cs` ist nur nötig, wenn die
   kleine Änderung an `Magicka.Program.Main` verstanden werden soll.

Der normale Kontrollfluss ist:

```text
Magicka.Program.Main
  -> Bootstrap.Apply
  -> RuntimePatchPlan.ApplyTo
  -> RuntimePatchSession.Apply
  -> Harmony Prefix oder Postfix
  -> ursprüngliche Magicka-Methode
```

Die Struktur folgt Single Layer of Abstraction: Der Plan nennt Patches, die
Session beschreibt Harmony-Operationen, die Patchklasse enthält fachliche
Entscheidungen, und die Reflection-Helfer enthalten die Laufzeitdetails.

## Verifizierte Referenz

| Rolle | Version | SHA-256 |
|---|---|---|
| Original | Magicka 1.10.4.2 | `A896E05A3CFF65CF9BAB4E67E13AE72CB428D99AA93098CF6A8DD8CBC3112EE7` |
| Manuelle Patch-Assembly | Community Patch 0.0.60 auf Magicka 1.10.4.2 | `F9457611B5407F40A21548C979C7856D8BC4EF43C9FDDCF5C4570494029E347D` |
| Kompatibilität | Magicka 1.4.16.0 | `BA15F8F61E172D2D103268587AB92C1DD25842EBC966E1A4D3418FCE27C93BBB` |
| Kompatibilität | Magicka 1.5.1.0 | `1F3C803F0C33DDB202D9A85D9AA07FD6F67A7304CDF2357FF58F64873327BFE8` |

Ändert sich der Hash der manuellen Patch-Assembly, gilt diese Verifikation nicht
automatisch für den neuen Stand. Dann müssen Quellvergleich, Checkliste und
Drei-Wege-Matrix erneut erzeugt und geprüft werden.

## Implementierte Runtime-Patches

- [x] `avatar-find-interactable`
  - Ziel: `Avatar.FindInteractable(bool)`
  - Technik: boolescher Prefix mit typgenauem Null-Ergebnis
  - Fehlerfälle: fehlender `PlayState`, `Level`, `CurrentScene` oder
    `Triggers`
  - Verhalten: während des Abbaus wird keine Interaktion gefunden
  - Kontrollfall: vorhandene Szene mit leerer Triggerliste durchläuft die
    Originalmethode
  - Original 1.10.4.2: alle vier Fehlerfälle enden in einer NullReferenceException
  - Manuelle Patch-Assembly 0.0.60: Fehler- und Kontrollfälle bestehen
  - Original plus Runtime-Patch: Fehler- und Kontrollfälle bestehen
  - Magicka 1.4.16.0 und 1.5.1.0: Prefix wird angewendet und alle fünf
    Szenarien bestehen
- [x] `ai-attack-detached-target`
  - Ziel: `AIStateAttack.OnExecute(IAI, float)`
  - Technik: boolescher Prefix
  - Fehlerfall: der aktuelle Angriffsziel-Eintrag lebt noch, sein Physikkörper
    wurde aber bereits abgelöst
  - Verhalten: Angriffszustand und Ziel werden wie beim bereits vorhandenen
    Null-Ziel-Pfad freigegeben
  - Kontrollfälle: fehlendes Ziel sowie der bestehende Fehler für einen
    ungültigen Nicht-Agent-Besitzer
  - Original 1.10.4.2: der körperlose Zielzustand endet in einer NullReferenceException
  - Manuelle Patch-Assembly 0.0.60: Fehler- und Kontrollfälle bestehen
  - Original plus Runtime-Patch: Fehler- und Kontrollfälle bestehen
  - Magicka 1.4.16.0 und 1.5.1.0: Prefix wird angewendet und alle drei
    Szenarien bestehen
- [x] `entity-manager-closest-damageable`
  - Ziel: `EntityManager.GetClosestIDamageable(...)`
  - Technik: Transpiler; fügt unmittelbar vor dem ersten `Position`-Zugriff
    den `Body == null`-Ausstieg zum nächsten QuadGrid-Eintrag ein
  - Fehlerfall: ein noch gelisteter, nicht toter Kandidat hat keinen Body mehr
  - Kontrollfälle: ein Null-Eintrag und ein leeres QuadGrid
  - Original 1.10.4.2: der körperlose Kandidat endet in einer
    NullReferenceException
  - Manuelle Patch-Assembly 0.0.60: Fehler- und Kontrollfälle bestehen
  - Original plus Runtime-Patch: Fehler- und Kontrollfälle bestehen
  - Magicka 1.4.16.0 und 1.5.1.0: Transpiler wird angewendet und alle drei
    Szenarien bestehen
- [x] `entity-manager-get-entities`
  - Ziel: vierparametriges `EntityManager.GetEntities(...)`
  - Technik: Transpiler; ergänzt Null- und Body-Prüfung vor dem ersten
    `Entity.Position`-Zugriff
  - Fehlerfälle: Null-Eintrag und körperloser Eintrag im QuadGrid
  - Kontrollfall: leeres QuadGrid
  - Original 1.10.4.2: beide Fehlerfälle enden in einer NullReferenceException
  - Manuelle Patch-Assembly 0.0.60: Fehler- und Kontrollfälle bestehen
  - Original plus Runtime-Patch: Fehler- und Kontrollfälle bestehen
  - Magicka 1.4.16.0 und 1.5.1.0: Transpiler wird angewendet und alle drei
    Szenarien bestehen
- [x] `entity-manager-clear-and-store`
  - Ziel: `EntityManager.ClearAndStore(List<Entity>)`
  - Technik: Postfix; ruft nach dem ursprünglichen Abbau `UpdateQuadGrid()` auf
  - Fehlerfall: eine stale Grid-Zelle bleibt trotz leerer Entity-Liste belegt
  - Kontrollfall: ein bereits leeres Grid bleibt leer
  - Original 1.10.4.2: die stale Grid-Zelle bleibt erhalten
  - Manuelle Patch-Assembly 0.0.60: Fehler- und Kontrollfall bestehen
  - Original plus Runtime-Patch: Fehler- und Kontrollfall bestehen
  - Magicka 1.4.16.0 und 1.5.1.0: Postfix wird angewendet und beide Szenarien
    bestehen
- [x] `helper-array-equals`
  - Ziel: `Helper.ArrayEquals(byte[], byte[])`
  - Technik: boolescher Prefix als vollständiger Ersatz der kleinen Methode
  - Fehlerfälle: linkes, rechtes oder beide Arrays fehlen
  - Kontrollfälle: gleiche und verschiedene nichtleere Arrays
  - Original 1.10.4.2: alle drei Null-Fälle werfen erwartungsgemäß eine
    NullReferenceException
  - Manuelle Patch-Assembly 0.0.60: alle fünf Szenarien bestehen
  - Original plus Runtime-Patch: alle fünf Szenarien bestehen
  - Magicka 1.4.16.0 und 1.5.1.0: Prefix wird angewendet und alle fünf
    Szenarien bestehen
- [x] `inventory-box-screen-size`
  - Ziel: `InventoryBox.RenderData.Draw(float)`
  - Technik: Prefix
  - Szenarien: erste Auflösung und Änderung der Auflösung am selben Effektobjekt
  - Original 1.10.4.2: beide Patch-Szenarien schlagen erwartungsgemäß fehl
  - Manuelle Patch-Assembly 0.0.60: beide Patch-Szenarien bestehen
  - Original plus Runtime-Patch: beide Patch-Szenarien bestehen
  - Magicka 1.4.16.0 und 1.5.1.0: Prefix wird angewendet und beide Szenarien bestehen
- [x] `magick-camera-follow-entity`
  - Ziel: `MagickCamera.Update(DataChannel, float)`
  - Technik: Prefix; setzt ausschließlich einen körperlosen `mFollowing`-Verweis
    auf `null`, sodass die Originalmethode ihren vorhandenen Null-Fallback nimmt
  - Fehlerfall: `FollowEntity` hält ein Ziel ohne Body
  - Kontrollfälle: bereits fehlendes Ziel und körperloses Ziel bei einem anderen
    Kameraverhalten
  - Original 1.10.4.2: der körperlose FollowEntity-Zustand behält Ziel und Modus
  - Manuelle Patch-Assembly 0.0.60: Fehler- und Kontrollfälle bestehen
  - Original plus Runtime-Patch: Fehler- und Kontrollfälle bestehen
  - Magicka 1.4.16.0 und 1.5.1.0: Prefix wird angewendet und alle drei
    Szenarien bestehen
- [x] `hud-manager-original-hud-enable`
  - Ziel: `HUDManager.Initialise()`
  - Technik: Postfix
  - Fehlerfall: ein deaktiviertes Original-HUD wird wieder aktiviert und die
    ungenutzte Custom-HUD-Canvas bleibt deaktiviert
  - Kontrollfall: ein bereits aktives Original-HUD behält seinen Zustand
  - Original 1.10.4.2: der Fehlerfall schlägt erwartungsgemäß fehl
  - Manuelle Patch-Assembly 0.0.60: Fehler- und Kontrollfall bestehen
  - Original plus Runtime-Patch: Fehler- und Kontrollfall bestehen
  - Magicka 1.4.16.0 und 1.5.1.0: nicht anwendbar, weil `HUDManager` fehlt
- [x] `machine-network-initialize`
  - Ziel: `Machine.NetworkInitialize(ref BossInitializeMessage)`
  - Technik: Transpiler; ersetzt ausschließlich die konstante Zuweisung
    `mNetworkInitialized = true` durch `mNetworkInitialized = mWarlock != null`
  - Fehlerfall: die Nachricht verweist auf keinen vorhandenen Warlock
  - Kontrollfälle: vorhandener Warlock und ein anderer Nachrichtentyp
  - Original 1.10.4.2: der Fehlerfall schlägt erwartungsgemäß fehl
  - Manuelle Patch-Assembly 0.0.60: Fehler- und Kontrollfälle bestehen
  - Original plus Runtime-Patch: Fehler- und Kontrollfälle bestehen
  - Magicka 1.4.16.0 und 1.5.1.0: Transpiler wird angewendet und alle drei
    Szenarien bestehen
- [x] `jormungandr-underground-target`
  - Ziel: `Jormungandr.UndergroundState.OnUpdate(float, Jormungandr)`
  - Technik: Transpiler; prüft `mTarget` unmittelbar nach dem vorhandenen
    `SelectTarget(Random)` und kehrt ohne Ziel aus diesem Update zurück
  - Fehlerfall: alle vier Player-Slots existieren, aber kein Player besitzt
    einen lebenden Avatar
  - Verhalten: Jormungandr bleibt unter der Erde und versucht die Zielwahl im
    nächsten Update erneut
  - Kontrollfall: vor Ablauf des Warn-Timers bleibt der bestehende frühe
    Rücksprung unverändert
  - Original 1.10.4.2: der Fehlerfall endet nach der erfolglosen Zielwahl in
    einer NullReferenceException
  - Manuelle Patch-Assembly 0.0.60: Fehler- und Kontrollfall bestehen
  - Original plus Runtime-Patch: Fehler- und Kontrollfall bestehen
  - Magicka 1.4.16.0 und 1.5.1.0: Transpiler wird angewendet und beide
    Szenarien bestehen
- [x] `play-state-world-sync-spawn-npc-guard`
  - Ziel: `PlayState.AddWorldSyncMessage(WorldSyncMessage)`
  - Technik: boolescher Prefix
  - Fehlerfälle: fehlender Handle, Nicht-NPC und NPC aus einem fremden `PlayState`
  - Kontrollfälle: normale Nachricht, andere Aktion und NPC aus demselben `PlayState`
  - Original 1.10.4.2: alle drei Fehlerfälle schlagen erwartungsgemäß fehl
  - Manuelle Patch-Assembly 0.0.60: alle sechs Szenarien bestehen
  - Original plus Runtime-Patch: alle sechs Szenarien bestehen
  - Magicka 1.4.16.0 und 1.5.1.0: nicht anwendbar, weil Typ und Zielmethode fehlen
- [x] `portal-detached-teleport-entry-guard`
  - Ziel: `Portal.PortalEntity.Update(DataChannel, float)`
  - Technik: Transpiler; ergänzt unmittelbar nach dem Dequeue eine Null- und
    Body-Prüfung und springt für ungültige Einträge zur Queue-Bedingung zurück
  - Fehlerfälle: `null` vor einer körperlosen Entity sowie die umgekehrte
    Reihenfolge
  - Verhalten: jeder ungültige Eintrag wird mit `continue` verworfen; weitere
    Queue-Einträge und das übrige Portal-Update werden nicht abgebrochen
  - Kontrollfall: eine leere Queue bleibt leer
  - Original 1.10.4.2: beide Fehlerfälle brechen beim ersten ungültigen Eintrag
    ab und lassen den zweiten Eintrag in der Queue
  - Manuelle Patch-Assembly 0.0.60: Fehler- und Kontrollfälle bestehen
  - Original plus Runtime-Patch: Fehler- und Kontrollfälle bestehen
  - Magicka 1.4.16.0 und 1.5.1.0: Transpiler wird angewendet und alle drei
    Szenarien bestehen

Die manuelle Hilfsmethode prüft außerdem `Entity.IsDisposed`. Dieses Mitglied
existiert im Original nicht und gehört zu einer noch nicht migrierten Änderung
an `Entity`. Der aktuelle Runtime-Patch nutzt die Prüfung, sobald eine
Zielversion sie anbietet; ein echter Drei-Wege-Test dafür folgt zusammen mit
der `Entity`-Migration. Deshalb bleiben `PlayState.cs` und
`NetworkEntityHandleGuard.cs` in der Datei-Checkliste auf `TEILWEISE`.

Die maschinenlesbaren Einzelergebnisse stehen nach einem Build in
`audit/behavior-matrix.txt`.

## Entfernte Versuchswege

Der statische Patcher, die statische Verifikations-Assembly, C#-Diff-Strings und
die beiden ersetzten Transpiler wurden gelöscht. Sie waren für die gewählte
Runtime-Architektur doppelte Implementierungen. Erhalten bleiben nur:

- der kleine Cecil-Injector, der `Bootstrap.Apply` am Anfang von `Main` einträgt;
- die Harmony-Runtime-Patches;
- die Verhaltenstests gegen echte Magicka-Assemblies;
- `analyze.ps1` für die vollständige Migrations- und Diff-Inventur.

## Einen weiteren Patch übernehmen

- [ ] Manuellen C#-Diff lesen und dnSpy-Rauschen von semantischen Änderungen trennen.
- [ ] Die kleinste geeignete Harmony-Technik wählen.
- [ ] Patchdefinition in einer fachlich benannten Patchklasse anlegen.
- [ ] Patch im passenden Plan eintragen.
- [ ] Mindestens einen Fehlerfall definieren: Original FAIL, manuell PASS, Runtime PASS.
- [ ] Benachbarte Kontrollfälle definieren: alle drei PASS.
- [ ] Relevante Grenzfälle ergänzen.
- [ ] Verhalten gegen jede unterstützte Magicka-Version ausführen.
- [ ] Fehlende Ziele als explizit `NOT_APPLICABLE` behandeln, nicht still ignorieren.
- [ ] Hash und Community-Patch-Version der manuellen Referenz aktualisieren.
- [ ] Den betroffenen Eintrag in der Datei-Checkliste aktualisieren.
- [ ] Vollständigen Build ausführen und `behavior-matrix.txt` prüfen.

## Kompatibilitätsstatus

| Magicka-Version | Runtime-Host | Avatar | AIStateAttack | EntityManager | Helper | InventoryBox | MagickCamera | HUDManager | Machine | Jormungandr | PlayState | Portal |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 1.10.4.2 | erzeugt | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| 1.4.16.0 | erzeugt | PASS | PASS | PASS | PASS | PASS | PASS | NOT_APPLICABLE | PASS | PASS | NOT_APPLICABLE | PASS |
| 1.5.1.0 | erzeugt | PASS | PASS | PASS | PASS | PASS | PASS | NOT_APPLICABLE | PASS | PASS | NOT_APPLICABLE | PASS |

`NOT_APPLICABLE` bedeutet hier nicht „ungeprüft“. Die alten Assemblies
enthalten weder die spätere `HUDManager`-Klasse noch `WorldSyncMessage` und
`PlayState.AddWorldSyncMessage`. Die Patchpläne protokollieren dies und fahren
mit allen anderen Patchgruppen fort.

## Datei-Checkliste der manuellen Patch-Assembly

Grundlage ist der kommentarbereinigte ILSpy-C#-Vergleich zwischen den oben
genannten 1.10.4.2-Hashes. Er enthält 220 unterschiedliche C#-Dateien. Die
Eingaben und Abhängigkeiten werden vor ILSpy isoliert bereitgestellt, damit der
Ablageort einer EXE die Auflösung von Typen und damit die Inventur nicht ändert.

Aktueller Stand: 7 Dateien vollständig, 6 Dateien teilweise und 207 Dateien noch
nicht migriert. `analyze.ps1` erzeugt zusätzlich
`source-analysis/file-diff-ranking.csv`, um weitere Kandidaten nach Diffgröße
auszuwählen.

Ein gesetztes Kästchen bedeutet, dass alle semantischen Änderungen dieser Datei
im Runtime-Patcher übernommen und durch die Drei-Wege-Matrix abgedeckt sind.
Enthält eine Datei ausschließlich semantikfreies Rekompilierungsrauschen, genügt
stattdessen die Prüfung des betroffenen IL-Ausschnitts; dafür ist bewusst kein
Runtime-Patch nötig.
„TEILWEISE“ bleibt absichtlich ungesetzt. Neue manuelle Patch-Versionen müssen
eine neue Dateiliste erzeugen; Dateinamen allein reichen nicht als
Versionsnachweis.

- [ ] `Magicka/GameLogic/GameStates/InGameMenus/InGameMenuTimedObjectiveStatistics.cs`
- [ ] `Magicka/CommunityPatch/TelemetryRuntimeContext.cs`
- [ ] `Magicka/GameLogic/Entities/SpellMine.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SummonPhoenix.cs`
- [ ] `Magicka/CommunityPatch/DialogLayoutCompatibility.cs`
- [ ] `Magicka/GameLogic/Entities/Bosses/Vlad.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Thunderstorm.cs`
- [ ] `Magicka/GameLogic/GameStates/InGameMenus/InGameMenuVersusStatistics.cs`
- [ ] `Magicka/Levels/Triggers/TriggerArea.cs`
- [ ] `Magicka/GameLogic/GameStates/InGameMenus/InGameMenu.cs`
- [ ] `Magicka/CommunityPatch/NetworkEntityHandleGuard.cs` — TEILWEISE: nur die für `AddWorldSyncMessage` benötigte SpawnNPC-Entscheidung, ohne Übernahme der übrigen manuellen Hilfsklasse.
- [ ] `Magicka/GameLogic/Spells/ArcaneBlast.cs`
- [ ] `Magicka/CoreFramework/GameSystem/Store/StoreItemDatabase.cs`
- [ ] `Magicka/GameLogic/UI/IconRenderer.cs`
- [ ] `Magicka/GameLogic/Entities/PhysicsEntity.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Conflagration.cs`
- [ ] `Magicka/GameLogic/UI/Credits.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Wave.cs`
- [ ] `Magicka/AI/Agent.cs`
- [ ] `Magicka/Levels/Campaign/LevelManager.cs`
- [ ] `Magicka/StaticWeakList.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Napalm.cs`
- [ ] `Magicka/GameLogic/Spells/ArcaneBlade.cs`
- [ ] `Magicka/GameLogic/Entities/Shield.cs`
- [ ] `Magicka/GameLogic/Spells/SpellEffects/RailGunSpell.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Polymorph.cs`
- [ ] `Magicka/GameLogic/UI/KeyboardHUD.cs`
- [ ] `Magicka/CommunityPatch/MouseInputCompatibility.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/GreaseLump.cs`
- [ ] `Magicka/StaticList.cs`
- [ ] `Magicka/GameLogic/Controls/KeyboardMouseController.cs`
- [ ] `Magicka/GameLogic/GameStates/InGameMenus/InGameMenuSurvivalStatistics.cs`
- [ ] `Magicka/GameLogic/Entities/Items/BookOfMagick.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Rain.cs`
- [ ] `Magicka/GameLogic/Spells/SpellEffects/PushSpell.cs`
- [ ] `Magicka/GameLogic/Spells/SpellEffects/ShieldSpell.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/VortexEntity.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SpawnSlime.cs`
- [ ] `Magicka/GameLogic/Entities/Entanglement.cs`
- [ ] `Magicka/Levels/Level.cs`
- [ ] `Magicka/Graphics/Effects/RadialBlur.cs`
- [ ] `Magicka/Levels/Lava.cs`
- [ ] `Magicka/GameLogic/Spells/SpellEffects/SpellEffect.cs`
- [ ] `Magicka/CommunityPatch/WarlordAbilityDiagnostic.cs`
- [ ] `Magicka/CommunityPatch/CollisionCallbackCleanup.cs`
- [ ] `Magicka/GameLogic/Entities/Bosses/GenericBoss.cs`
- [ ] `Magicka/Levels/Water.cs`
- [ ] `Magicka/GameLogic/GameStates/InGameMenus/InGameMenuOptionsResolution.cs`
- [ ] `Magicka/Graphics/TutorialManager.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Thunderbolt.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Blizzard.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SummonZombie.cs`
- [ ] `Magicka/Game.cs`
- [ ] `Magicka/CommunityPatch/PayloadContract.cs`
- [ ] `Magicka/CommunityPatch/AnimationClipCompatibility.cs`
- [ ] `Magicka/CommunityPatch/PatchSettings.cs`
- [ ] `Magicka/GameLogic/Entities/Items/Item.cs`
- [ ] `Magicka/SharedContentManager.cs`
- [ ] `Magicka/GameLogic/UI/Tome.cs`
- [ ] `Magicka/GameLogic/Entities/Avatar.cs` — TEILWEISE: `FindInteractable`, Prefix und 5 Drei-Wege-Szenarien; die übrigen manuellen Änderungen dieser großen Klasse sind noch offen.
- [ ] `Magicka/GameLogic/GameStates/PlayState.cs` — TEILWEISE: nur `AddWorldSyncMessage`, Prefix und 6 Drei-Wege-Szenarien.
- [ ] `Magicka/GameLogic/Spells/Magick.cs`
- [ ] `Magicka/GameLogic/Spells/Railgun.cs`
- [ ] `Magicka/Levels/Triggers/Trigger.cs`
- [ ] `Magicka/GameLogic/Entities/Gib.cs`
- [ ] `Magicka/GameLogic/Entities/MissileEntity.cs`
- [ ] `Magicka/GameLogic/Controls/XInputController.cs`
- [ ] `Magicka/Levels/GameScene.cs`
- [ ] `Magicka/CommunityPatch/PatchTelemetry.cs`
- [ ] `Magicka/GameLogic/Entities/Character.cs`
- [ ] `Magicka/GameLogic/Spells/SpellEffects/ProjectileSpell.cs`
- [ ] `Magicka/GameLogic/GameStates/Menu/Main/SubMenuCharacterSelect.cs`
- [ ] `Magicka/Network/NetworkClient.cs`
- [ ] `Magicka/Network/NetworkServer.cs`
- [ ] `Magicka/CommunityPatch/HybridInputSupport.cs`
- [ ] `Magicka/CommunityPatch/OriginalBackupAudit.cs`
- [ ] `Magicka/CommunityPatch/Magicka2ControllerSupport.cs`
- [ ] `Magicka/GameLogic/Entities/CharacterTemplate.cs`
- [ ] `Magicka/CommunityPatch/PatchUpdateManager.cs`
- [ ] `Magicka/CommunityPatch/NetworkLifecycleCompatibility.cs`
- [ ] `Magicka/GameLogic/GameStates/Menu/Main/SubMenuCutscene.cs`
- [ ] `Magicka/CommunityPatch/RuntimeCompatibilityGuards.cs`
- [ ] `Magicka/GameLogic/Entities/Barrier.cs`
- [ ] `Magicka/GameLogic/Spells/SpellEffects/SpraySpell.cs`
- [ ] `Magicka/Levels/LevelModel.cs`
- [ ] `Magicka/GameLogic/Entities/PhysicsEntityTemplate.cs`
- [ ] `Magicka/CommunityPatch/CommunityPatchInfo.cs`
- [ ] `Magicka/Physics/PhysicsManager.cs`
- [ ] `Magicka/GameLogic/GameStates/InGameMenus/InGameMenuMagicks.cs`
- [ ] `Magicka/GameLogic/Entities/Entity.cs`
- [ ] `Magicka/Levels/ForceField.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Revive.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Portal.cs` — TEILWEISE: ungültige Einträge in `PortalEntity.mTeleportQueue`, Transpiler und 3 Drei-Wege-Szenarien; weitere manuelle Änderungen sind noch offen.
- [ ] `Magicka/GameLogic/Entities/ElementalEgg.cs`
- [ ] `Magicka/GameLogic/Entities/Fairy.cs`
- [ ] `Magicka/GameLogic/GameStates/InGameMenus/InGameMenuOptionsControls.cs`
- [ ] `Magicka/GameLogic/Entities/DamageablePhysicsEntity.cs`
- [ ] `Magicka/Levels/AnimatedLevelPart.cs`
- [ ] `Magicka/GameLogic/Entities/Bosses/BossFight.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Grease.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SummonDeath.cs`
- [ ] `Magicka/CommunityPatch/InGameUiCompatibility.cs`
- [ ] `Magicka/CommunityPatch/WidescreenSafeArea.cs`
- [ ] `Magicka/GameLogic/Entities/NonPlayerCharacter.cs`
- [ ] `Magicka/Graphics/TypingText.cs`
- [ ] `Magicka/Program.cs`
- [ ] `Magicka/GameLogic/Entities/AnimatedPhysicsEntity.cs`
- [ ] `Magicka/GameLogic/Spells/UnderGroundAttack.cs`
- [ ] `Magicka/CommunityPatch/NetworkGuardTelemetryBackoff.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/HealingRain.cs`
- [ ] `Magicka/GameLogic/GameStates/Menu/MenuImageTextItem.cs`
- [ ] `Magicka/GameLogic/GameStates/CompanyState.cs`
- [ ] `Magicka/Localization/LanguageManager.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/RandomMine.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/DrainLife.cs`
- [ ] `Magicka/Levels/Liquid.cs`
- [ ] `Magicka/Levels/Triggers/Actions/SetDialogHint.cs`
- [ ] `Magicka/GameLogic/Controls/Controller.cs`
- [ ] `Magicka/GameLogic/Entities/EntityStateStorage.cs`
- [ ] `Magicka/WebTools/Paradox/ParadoxPopupUtils.cs`
- [ ] `Magicka/GameLogic/UI/ShadowBlobs.cs`
- [ ] `Magicka/GameLogic/Entities/AnimationClipAction.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SpawnSlimeOverkill.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/DeflectionAura.cs`
- [ ] `Magicka/Graphics/NotifierButton.cs`
- [ ] `Magicka/GameLogic/Statistics/StatisticsManager.cs`
- [ ] `Magicka/Graphics/TextBox.cs`
- [ ] `Magicka/Levels/Triggers/Actions/AssignItem.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/JudgementSpray.cs`
- [ ] `Magicka/GameLogic/Spells/IceSpikes.cs`
- [ ] `Magicka/GameLogic/GameStates/InGameMenus/InGameMenuOptions.cs`
- [ ] `Magicka/GameLogic/GameStates/Menu/Main/SubMenuMain.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Starfall.cs`
- [ ] `Magicka/GameLogic/Entities/ChantSpellManager.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Zap.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/VladZap.cs`
- [ ] `Magicka/Network/EntityUpdateMessage.cs`
- [ ] `Magicka/GameLogic/Entities/Bosses/CthulhuMist.cs`
- [ ] `Magicka/GameLogic/Entities/Bosses/BossCollisionZone.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/DrinkBlood.cs`
- [x] `Magicka/GameLogic/Entities/Bosses/Jormungandr.cs` — VOLLSTÄNDIG: fehlendes Ziel nach `SelectTarget`, Transpiler und 2 Drei-Wege-Szenarien.
- [ ] `Magicka/GameLogic/Entities/FrogTongue.cs`
- [x] `Magicka/GameLogic/Entities/ChantSpells.cs` — VOLLSTÄNDIG: ausschließlich semantikfreie Eliminierung einer lokalen `LightningBolt`-Variablen; `GetLightning()` und `InitializeEffect(...)` bleiben in derselben Reihenfolge und werden jeweils einmal ausgeführt.
- [ ] `Magicka/GameLogic/Entities/Items/Pickable.cs`
- [ ] `Magicka/GameLogic/Controls/DirectInputController.cs`
- [x] `Magicka/AI/AgentStates/AIStateAttack.cs` — VOLLSTÄNDIG: `OnExecute`, Prefix und 3 Drei-Wege-Szenarien.
- [ ] `Magicka/GlobalSettings.cs`
- [x] `Magicka/CoreFramework/GameSystem/HUDCustomisation/HUDManager.cs` — VOLLSTÄNDIG: `Initialise`, Postfix und 2 Drei-Wege-Szenarien.
- [x] `Magicka/GameLogic/UI/InventoryBox.cs` — VOLLSTÄNDIG: `RenderData.Draw`, Prefix und 2 Drei-Wege-Szenarien.
- [ ] `Magicka/GameLogic/Entities/Bosses/WarlordCharacter.cs`
- [ ] `Magicka/GameLogic/Entities/Bosses/Tentacle.cs`
- [x] `Magicka/GameLogic/Entities/Bosses/Machine.cs` — VOLLSTÄNDIG: `NetworkInitialize`, Transpiler und 3 Drei-Wege-Szenarien.
- [ ] `Magicka/GameLogic/GameStates/LoadingScreen.cs`
- [ ] `Magicka/GameLogic/Entities/Items/Attachment.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SummonElemental.cs`
- [ ] `Magicka/Graphics/CutsceneText.cs`
- [ ] `Magicka/GameLogic/UI/BossHealthBar.cs`
- [ ] `Magicka/GameLogic/GameStates/Menu/Main/Options/SubMenuOptionsControls.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SummonBug.cs`
- [ ] `Magicka/Levels/Versus/VersusRuleset.cs`
- [ ] `Magicka/AI/AgentStates/AIStateMove.cs`
- [ ] `Magicka/Levels/Packs/MagickPack.cs`
- [ ] `Magicka/Levels/Packs/ItemPack.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/OtherworldlyDischarge.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/MutateBeastman.cs`
- [ ] `Properties/AssemblyInfo.cs`
- [ ] `Magicka/Audio/AudioManager.cs`
- [ ] `Magicka/GameLogic/UI/Message.cs`
- [ ] `Magicka/GameLogic/Spells/IceBlade.cs`
- [ ] `Magicka/GameLogic/GameStates/InGameMenus/InGameMenuOptionsGraphics.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Grow.cs`
- [ ] `Magicka/Levels/Triggers/Actions/GiveOrder.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/PerformanceEnchantment.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/EarthQuake.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/BreakBarriers.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/ArrowRain.cs`
- [ ] `Magicka/GameLogic/Entities/SprayEntity.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/ConfuseWho.cs`
- [ ] `Magicka/GameLogic/UI/DialogManager.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Confuse.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/TornadoEntity.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/FloorStomp.cs`
- [ ] `Magicka/Graphics/MagickCamera.cs` — TEILWEISE: körperloses `FollowEntity`-Ziel, Prefix und 3 Drei-Wege-Szenarien; weitere Lifetime- und Dispose-Änderungen sind noch offen.
- [ ] `Magicka/GameLogic/UI/SpellWheel.cs`
- [ ] `Magicka/GameLogic/Entities/EntityManager.cs` — TEILWEISE: `GetClosestIDamageable`, das vierparametrige `GetEntities` und `ClearAndStore` mit 8 Drei-Wege-Szenarien; Konstruktor- und weitere Diagnoseänderungen sind noch offen.
- [ ] `Magicka/GameLogic/Entities/TeslaField.cs`
- [ ] `Magicka/GameLogic/Spells/LightningBolt.cs`
- [ ] `Magicka/Levels/Triggers/Actions/Action.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/TimeWarpStaff.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/TimeWarp.cs`
- [ ] `Magicka/GameLogic/GameStates/InGameMenus/InGameMenuMain.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/MeteorShower.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/GreaseTrail.cs`
- [ ] `Magicka/GameLogic/Entities/Dispenser.cs`
- [x] `Magicka/Helper.cs` — VOLLSTÄNDIG: `ArrayEquals`, Prefix und 5 Drei-Wege-Szenarien.
- [ ] `Magicka/Graphics/Lights/DynamicLight.cs`
- [ ] `Magicka/Graphics/Flash.cs`
- [ ] `Magicka/GameLogic/UI/GenericHealthBar.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/WaveEntity.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/EtherealClone.cs`
- [ ] `Magicka/GameLogic/Entities/Snare.cs`
- [ ] `Magicka/Levels/Triggers/Interactable.cs`
- [ ] `Magicka/Levels/Packs/PackMan.cs`
- [ ] `Magicka/GameLogic/Controls/ControlManager.cs`
- [ ] `Magicka/GameLogic/Player.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SummonSpirit.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SummonFlamer.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/HomingCharge.cs`
- [ ] `Magicka/Graphics/EffectManager.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Shrink.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SummonUndead.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SummonCross.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/StopCharge.cs`
- [ ] `Magicka/GameLogic/GameStates/MenuState.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/ChillyBlast.cs`
- [ ] `Magicka/GameLogic/Spells/SpellEffects/LightningSpell.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Haste.cs`
- [ ] `Magicka/GameLogic/Entities/Bosses/PropBoss.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/StarGaze.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/PoisonSpray.cs`
