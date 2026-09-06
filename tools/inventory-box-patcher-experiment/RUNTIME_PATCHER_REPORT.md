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
13. `src/BehaviorProbe/BehaviorSuite.cs` und die sechsunddreißig `*Scenarios.cs`-Dateien
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
- [x] `ai-move-detached-target`
  - Ziele: `AIStateMove.OnEnter(IAI)` und `AIStateMove.OnExecute(IAI, float)`
  - Technik: zwei Transpiler; `OnEnter` ergänzt die Body-Prüfung in der
    vorhandenen optionalen Zielbedingung, `OnExecute` ergänzt sie nach `Dead`
    im vorhandenen Pop-State-Zweig
  - Fehlerfälle: ein Agent hält ein nicht totes Ziel, dessen Body bereits
    abgelöst ist
  - Verhalten: `OnEnter` berechnet keinen zielrelativen Wegpunkt;
    `OnExecute` verlässt den Move-State vor `IsUseful` und `Position`
  - Kontrollfälle: beide Methoden mit fehlendem Ziel
  - Original 1.10.4.2: beide körperlosen Zielzustände lesen `Entity.Position`
    und enden in einer NullReferenceException
  - Manuelle Patch-Assembly 0.0.60: alle vier Szenarien bestehen
  - Original plus Runtime-Patch: alle vier Szenarien bestehen
  - Magicka 1.4.16.0 und 1.5.1.0: beide Transpiler werden angewendet und alle
    vier Szenarien bestehen
- [x] `agent-choose-target-detached-candidate`
  - Ziel: `Agent.ChooseTarget(out IDamageable, out Ability)`
  - Technik: Transpiler; ergänzt die Body-Prüfung nach dem vorhandenen
    Null-/Dead-/Owner-Ausschluss und springt zum nächsten Kandidaten
  - Fehlerfall: ein körperloser Avatar gelangt über `Game.Players` in die
    Kandidatenliste
  - Verhalten: der Kandidat wird verworfen, bevor Entfernung, Position oder
    Ausrichtung ausgewertet werden
  - Kontrollfall: Player-Slots ohne Avatar liefern weiterhin weder Ziel noch
    Fähigkeit
  - Original 1.10.4.2: der körperlose Avatar endet in einer
    NullReferenceException
  - Manuelle Patch-Assembly 0.0.60: Fehler- und Kontrollfall bestehen
  - Original plus Runtime-Patch: Fehler- und Kontrollfall bestehen
  - Magicka 1.4.16.0 und 1.5.1.0: Transpiler wird angewendet und beide
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
- [x] `entity-state-storage-play-state-lifetime`
  - Ziele: `EntityStateStorage(PlayState)` und `EntityStateStorage.Restore(...)`
  - Technik: Konstruktor-Postfix zum Freigeben des Legacy-Felds und Transpiler,
    der genau zwei `mPlayState`-Lesezugriffe durch `PlayState.RecentPlayState`
    ersetzt
  - Fehlerfälle: der Konstruktor hält einen alten `PlayState`; nach einem
    Übergang erhält `Pickable.State.Restore` diesen alten Zustand
  - Verhalten: gespeicherte Entity-Zustände halten keinen Levelzustand fest und
    werden in den aktuell aktiven `PlayState` wiederhergestellt
  - Kontrollfall: ein leerer Zustand bleibt beim Wiederherstellen leer
  - Original 1.10.4.2: beide Fehlerfälle schlagen erwartungsgemäß fehl
  - Manuelle Patch-Assembly 0.0.60: alle drei Szenarien bestehen
  - Original plus Runtime-Patch: alle drei Szenarien bestehen
  - Magicka 1.4.16.0 und 1.5.1.0: beide Patches werden angewendet und alle drei
    Szenarien bestehen
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
- [x] `boss-health-bar-scene-lifetime`
  - Ziele: Konstruktor sowie Getter und Setter von `BossHealthBar.Scene`
  - Technik: Konstruktor-Postfix und zwei Prefixe; gespeicherte Legacy-Verweise
    werden freigegeben und der Getter liefert die Szene des aktuellen
    `PlayState`
  - Fehlerfälle: der Konstruktor oder Setter hält eine abgelöste Szene fest;
    der Getter liefert nach einem Szenenwechsel die alte Szene
  - Verhalten: `BossHealthBar` besitzt keinen langfristigen Szenenverweis mehr
  - Original 1.10.4.2: alle drei Fehlerfälle schlagen erwartungsgemäß fehl
  - Manuelle Patch-Assembly 0.0.60: alle drei Szenarien bestehen
  - Original plus Runtime-Patch: alle drei Szenarien bestehen
  - Magicka 1.4.16.0 und 1.5.1.0: alle drei Patches werden angewendet; Getter
    und Setter bestehen ihre Szenarien. Der ältere Konstruktor benötigt selbst
    für einen Headless-Test ein echtes Grafikgerät und ist daher in der
    Verhaltensmatrix als `NOT_APPLICABLE` markiert.
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
- [x] `versus-ruleset-revive-avatar-guard`
  - Ziel: `VersusRuleset.RevivePlayer(int, int, ref Matrix, ushort?)`
  - Technik: Transpiler; prüft den Rückgabewert des vorhandenen
    `Avatar.GetFromCache`-Aufrufs unmittelbar nach dem gemeinsamen lokalen
    Speichern
  - Fehlerfälle: der normale Cachezugriff und der Zugriff mit einem bestimmten
    Handle liefern keinen Avatar
  - Verhalten: die Methode gibt Handle `0` zurück, bevor sie den fehlenden
    Avatar initialisiert
  - Kontrollfall: ein vorhandener Avatar erreicht weiterhin `Avatar.Initialize`
  - Original 1.10.4.2: beide Fehlerfälle enden in einer NullReferenceException;
    der Kontrollfall besteht
  - Manuelle Patch-Assembly 0.0.60: alle drei Szenarien bestehen
  - Original plus Runtime-Patch: alle drei Szenarien bestehen
  - Magicka 1.4.16.0 und 1.5.1.0: der Transpiler registriert sich. Die alte
    `Gamer`-Initialisierung benötigt Grafikgerät und Content, deshalb sind die
    drei Headless-Szenarien dort `NOT_APPLICABLE`.
- [x] `pack-custom-content-license`
  - Ziele: `License`- und `Enabled`-Setter von `ItemPack` und `MagickPack`
  - Technik: vier Transpiler; jeder ersetzt nur den vorhandenen Vergleich mit
    `HackHelper.License.Yes` durch den gemeinsamen Runtime-Prädikatsaufruf
  - Verhalten: `Yes` bleibt erlaubt; `Custom` ist offline oder in einer nicht
    VAC-geschützten Sitzung erlaubt; alle anderen Lizenzen bleiben gesperrt
  - Fehlerfälle: `Custom` über beide Setter, jeweils offline und in einer
    nicht VAC-geschützten Sitzung
  - Kontrollfälle: `Custom` mit VAC sowie `Yes` und `No`
  - Original 1.10.4.2: die vier erlaubten Custom-Fälle schlagen erwartungsgemäß
    fehl; alle Kontrollfälle bestehen
  - Manuelle Patch-Assembly 0.0.60: alle acht Szenarien bestehen
  - Original plus Runtime-Patch: alle acht Szenarien bestehen
  - Magicka 1.4.16.0 und 1.5.1.0: alle vier Patches werden angewendet und alle
    acht Szenarien bestehen
  - Die vier gleichartigen Anzeigeprüfungen in `SubMenuCharacterSelect` sind
    noch nicht Teil dieses Blocks.
- [x] `drink-blood-play-state-lifetime`
  - Ziel: `DrinkBlood.Execute(ISpellCaster, PlayState)`
  - Technik: Transpiler; ersetzt ausschließlich `mPlayState = iPlayState`
    durch drei `nop`-Instruktionen
  - Fehlerfall: der Effekt speichert den übergebenen `PlayState`, obwohl kein
    Code das Feld liest
  - Verhalten: der globale Effektpfad erzeugt keine zusätzliche starke
    Referenz auf den Levelzustand
  - Kontrollfall: Besitzer, TTL, Zielstatus, Effektregistrierung,
    Haste-Ausführung und Rückgabewert bleiben erhalten
  - Original 1.10.4.2: der Fehlerfall behält den `PlayState`; der Kontrollfall
    besteht
  - Manuelle Patch-Assembly 0.0.60: beide Szenarien bestehen
  - Original plus Runtime-Patch: beide Szenarien bestehen
  - Magicka 1.4.16.0 und 1.5.1.0: Transpiler wird angewendet und beide
    Szenarien bestehen
- [x] `random-mine-play-state-lifetime`
  - Ziel: `RandomMine.Execute(Vector3, PlayState)`
  - Technik: Transpiler; entfernt nur die Zuweisung an das ungenutzte
    `mPlayState`-Feld
  - Fehlerfall: der statische `RandomMine`-Singleton hält den zuletzt
    übergebenen `PlayState`
  - Verhalten: der Singleton erzeugt keine dauerhafte Referenz auf den letzten
    Levelzustand
  - Kontrollfälle: Offline-Ausführung aktiviert weiterhin Schaden;
    Client-Ausführung deaktiviert ihn weiterhin; beide geben `true` zurück
  - Original 1.10.4.2: der Fehlerfall behält den `PlayState`; beide
    Kontrollfälle bestehen
  - Manuelle Patch-Assembly 0.0.60: alle drei Szenarien bestehen
  - Original plus Runtime-Patch: alle drei Szenarien bestehen
  - Magicka 1.4.16.0 und 1.5.1.0: Transpiler wird angewendet und alle drei
    Szenarien bestehen
- [x] `starfall-play-state-lifetime`
  - Ziele: vierparametriges `Starfall.Execute(...)` und `Starfall.Update(...)`
  - Technik: zwei Transpiler; einer entfernt die einzige Zuweisung an
    `sPlayState`, der andere ersetzt genau vier Lesezugriffe durch
    `PlayState.RecentPlayState`
  - Fehlerfälle: eine Ausführung hält den übergebenen Levelzustand statisch;
    ein späteres Update liest `Level` aus diesem alten Zustand
  - Verhalten: die statische Queue behält keinen `PlayState`, und jeder
    Verarbeitungsschritt verwendet den aktuellen Zustand
  - Kontrollfall: eine Ausführung ohne Schaden gibt weiterhin `true` zurück
    und fügt keinen Queue-Eintrag hinzu
  - Original 1.10.4.2: beide Fehlerfälle schlagen erwartungsgemäß fehl; der
    Kontrollfall besteht
  - Manuelle Patch-Assembly 0.0.60: alle drei Szenarien bestehen
  - Original plus Runtime-Patch: alle drei Szenarien bestehen
  - Magicka 1.4.16.0 und 1.5.1.0: beide Transpiler werden angewendet und alle
    drei Szenarien bestehen
- [x] `drain-life-play-state-lifetime`
  - Ziel: `DrainLife.Execute(ISpellCaster, PlayState)`
  - Technik: Transpiler; entfernt nur die Zuweisung an das ungenutzte
    `mPlayState`-Feld
  - Fehlerfall: ein erfolgreich gestarteter Effekt behält den übergebenen
    Levelzustand
  - Verhalten: der Effekt besitzt nur noch die tatsächlich verwendete
    Besitzerreferenz
  - Kontrollfall: der echte Erfolgspfad behält Besitzer, 50 Lebensentzug,
    einen Schadensaufruf, Rückgabe der Query-Liste, TTL `1` und Rückgabewert
    `true`
  - Original 1.10.4.2: der Fehlerfall behält den `PlayState`; der Kontrollfall
    besteht
  - Manuelle Patch-Assembly 0.0.60: beide Szenarien bestehen
  - Original plus Runtime-Patch: beide Szenarien bestehen
  - Magicka 1.4.16.0 und 1.5.1.0: Transpiler wird angewendet und beide
    Szenarien bestehen
- [x] `sub-menu-main-controller-back`
  - Ziel: `SubMenuMain.ControllerB(Controller)`
  - Technik: boolescher Prefix; Gamepads rufen `ShowRUSure()` auf und
    überspringen den Cursorpfad, Keyboard/Maus führt die Originalmethode aus
  - Fehlerfall: bei abgelöstem Cursor hängt der erste Gamepad-B-Druck nur den
    Cursor an, statt die vorhandene Beenden-Bestätigung zu öffnen
  - Kontrollfall: Keyboard/Maus hängt den Cursor weiterhin wie im Original an
  - Original 1.10.4.2: Fehlerfall schlägt fehl, Kontrollfall besteht
  - Manuelle Patch-Assembly 0.0.60: beide Szenarien bestehen
  - Original plus Runtime-Patch: beide Szenarien bestehen
  - Magicka 1.4.16.0 und 1.5.1.0: `SubMenuMain` deklariert noch keinen
    `ControllerB`-Override; Patch und Szenarien sind `NOT_APPLICABLE`
- [x] `company-state-exit-order`
  - Ziel: `CompanyState.OnExit()`
  - Technik: Transpiler; verschiebt ausschließlich den vorhandenen
    `mContentManager.Dispose()`-Block vom Methodenanfang vor das einzige `ret`
  - Fehlerfall: Content wird freigegeben, bevor Controller und Tome ihren
    Screen-Zustand verlassen
  - Verhalten: Controller, Kamera und Licht werden zuerst zurückgesetzt;
    Content wird weiterhin genau einmal freigegeben
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0: Reihenfolge ist
    `content,controllers,camera,light`
  - Manuelle Patch-Assembly 0.0.60 und alle Runtime-Patch-Profile: Reihenfolge
    ist `controllers,camera,light,content`
- [x] `control-manager-detached-player-locks`
  - Ziele: die `Controller`-Überladungen von `LockPlayerInput`,
    `IsPlayerInputLocked` und `UnlockPlayerInput`
  - Technik: drei boolesche Prefixe; fehlender Controller oder fehlender
    `Player` überspringt den Arrayzugriff, die Abfrage liefert `false`
  - Fehlerfälle: `null`-Controller und Controller mit `Player == null`
  - Kontrollfall: ein gültiger Controller sperrt Index 2, meldet die Sperre,
    entsperrt ihn und meldet anschließend `false`
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0: beide Fehlerfälle werfen in allen
    drei Methoden eine NullReferenceException; Kontrollfall besteht
  - Manuelle Patch-Assembly 0.0.60 und alle Runtime-Patch-Profile: alle drei
    Szenarien bestehen
  - `HandleInput` und seine Hybrid-Input-Erweiterung bleiben außerhalb dieses
    Blocks
- [x] `interactable-detached-scene-highlight`
  - Ziel: `Interactable.Highlight()`
  - Technik: boolescher Prefix; fehlende Szene oder fehlendes Levelmodell
    überspringt nur den visuellen Highlight-Aufruf
  - Fehlerfälle: `mGameScene == null` und `mGameScene.LevelModel == null`
    bei vorhandenem animiertem Highlightpfad
  - Kontrollfall: eine leere Highlightliste bleibt auch ohne Szene ein
    erfolgreicher No-op
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0: beide Fehlerfälle werfen eine
    NullReferenceException; Kontrollfall besteht
  - Manuelle Patch-Assembly 0.0.60 und alle Runtime-Patch-Profile: alle drei
    Szenarien bestehen
- [x] `audio-manager-disposed-cue`
  - Ziel: `AudioManager.StopAll(AudioStopOptions)`
  - Technik: Transpiler; ergänzt genau eine `Cue.IsDisposed`-Prüfung vor dem
    vorhandenen `Cue.Stop(...)`-Aufruf
  - Fehlerfall: ein bereits freigegebener Cue verbleibt während des Audio- oder
    Szenenabbaus noch in `mActiveCues`
  - Kontrollfall: eine leere Cue-Liste bleibt ein erfolgreicher No-op
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0: der Fehlerfall wirft eine
    ArgumentException; Kontrollfall besteht
  - Manuelle Patch-Assembly 0.0.60 und alle Runtime-Patch-Profile: beide
    Szenarien bestehen
- [x] `deflection-aura-play-state-lifetime`
  - Ziel: `DeflectionAura.Execute(ISpellCaster, PlayState)`
  - Technik: Transpiler; entfernt nur die Zuweisung an das ungenutzte
    `mPlayState`-Feld
  - Fehlerfall: ein ausgeführtes Auraobjekt behält den übergebenen Levelzustand
  - Kontrollfall: Rückgabewert, Besitzer, Kugelradius und -zentrum sowie der
    einzelne `AddAura`-Aufruf bleiben erhalten
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0: der Fehlerfall behält den
    `PlayState`; Kontrollfall besteht
  - Manuelle Patch-Assembly 0.0.60 und alle Runtime-Patch-Profile: beide
    Szenarien bestehen
- [x] `flash-scene-lifetime`
  - Ziele: `Flash.Execute(Scene, float)` und
    `Flash.Update(DataChannel, float)`
  - Technik: zwei Transpiler; der erste entfernt nur die Zuweisung an
    `mScene`, der zweite ersetzt nur den Empfänger des vorhandenen
    `AddRenderableAdditiveObject`-Aufrufs durch
    `PlayState.RecentPlayState.Scene`
  - Fehlerfälle: das Singleton hält die beim Auslösen übergebene Szene fest
    und ein späteres Update reicht Renderdaten an diese veraltete Szene weiter
  - Kontrollverhalten: TTL, Intensität, gerenderte Intensität und der einzelne
    `SpellManager.AddSpellEffect`-Aufruf bleiben erhalten
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0: beide Fehlerfälle verwenden die
    gespeicherte Szene; alle Kontrollwerte bestehen
  - Manuelle Patch-Assembly 0.0.60 und alle Runtime-Patch-Profile: beide
    Szenarien bestehen und verwenden die aktuelle Szene
  - Das manuelle `IDisposable`-Mitglied ruft ausschließlich das leere
    `OnRemove()` auf und hat kein Cleanup-Verhalten; es benötigt keinen
    Runtime-Ersatz. Die explizite Darstellung des statischen Lock-Initialisierers
    ist semantikfreies Compilerrauschen.
- [x] `spawn-slime-play-state-lifetime`
  - Ziele: `SpawnSlime.Execute(ISpellCaster, Elements, PlayState)`, die
    entsprechende `SpawnSlimeOverkill.Execute`-Überladung sowie
    `SpawnSlime.CreateEntities` und `SpawnSlime.SpawnSlimes`
  - Technik: vier Transpiler; zwei entfernen ausschließlich die jeweilige
    Zuweisung an `mPlayState`, zwei ersetzen ausschließlich den späteren
    Feldzugriff durch `PlayState.RecentPlayState`
  - Fehlerfälle: beide prozessweiten Fähigkeitssingletons halten den zuletzt
    übergebenen Levelzustand fest; beide Spawn-Hilfsmethoden verwenden dadurch
    später dessen veralteten NavMesh
  - Kontrollverhalten: Rückgabewert, Besitzerreferenz, erzeugte Entity-Anzahl
    und die übrige Spawnlogik bleiben erhalten
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0: beide Referenzen bleiben erhalten
    und beide Hilfsmethoden verwenden den veralteten NavMesh
  - Manuelle Patch-Assembly 0.0.60 und alle Runtime-Patch-Profile: alle vier
    Szenarien bestehen und verwenden den aktuellen NavMesh
  - Der leere manuelle `DisposeCache()` und anders dargestellte statische
    Hash-Initialisierer ändern kein Laufzeitverhalten und benötigen keinen
    Runtime-Ersatz.
- [x] `poison-spray-play-state-lifetime`
  - Ziele: `PoisonSpray.Execute(ISpellCaster, PlayState)` und
    `PoisonSpray.Update(DataChannel, float)`
  - Technik: zwei Transpiler; der erste entfernt ausschließlich die Zuweisung
    an `mPlayState`, der zweite ersetzt genau zwei Feldzugriffe durch
    `PlayState.RecentPlayState`
  - Fehlerfall: der Effekt hält den beim Auslösen übergebenen Levelzustand fest
    und bezieht seine temporäre Entity-Liste später aus dessen veraltetem
    `EntityManager`
  - Kontrollverhalten: Rückgabewert, Besitzer, TTL, Audio- und Effektstart sowie
    die spätere Effektaktualisierung bleiben erhalten
  - Der EntityManager-Test verwendet die echten Listenpools beider Zustände. Nur
    der aktuelle Manager entnimmt, leert und erhält seine markierte Liste zurück;
    der veraltete Manager bleibt unverändert.
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0: Referenz und veralteter Managerzugriff
    bleiben bestehen; der Kontrollfall besteht
  - Manuelle Patch-Assembly 0.0.60 und alle Runtime-Patch-Profile: alle drei
    Szenarien bestehen
  - Das Inlining der lokalen `yaw`-Variable und die explizite Darstellung der
    beiden statischen Hash-Initialisierer sind semantikfreies Compilerrauschen.
- [x] `chilly-blast-play-state-lifetime`
  - Ziele: `ChillyBlast.Execute(ISpellCaster, PlayState)` und
    `ChillyBlast.Update(DataChannel, float)`
  - Technik: zwei Transpiler; der erste entfernt ausschließlich die Zuweisung
    an `mPlayState`, der zweite ersetzt genau zwei Feldzugriffe durch
    `PlayState.RecentPlayState`
  - Fehlerfall: der Effekt hält den beim Auslösen übergebenen Levelzustand fest
    und bezieht seine temporäre Entity-Liste später aus dessen veraltetem
    `EntityManager`
  - Kontrollverhalten: Rückgabewert, Besitzer, ursprüngliche Drehgeschwindigkeit,
    TTL, Audio- und Effektstart sowie die spätere Effektaktualisierung bleiben
    erhalten
  - Original 1.10.4.2: Referenz und veralteter Managerzugriff bleiben bestehen;
    die manuelle Patch-Assembly 0.0.60 und das Runtime-Patch-Profil bestehen alle
    drei Szenarien
  - Magicka 1.4.16.0 und 1.5.1.0 enthalten `ChillyBlast` noch nicht. Patch und
    Szenarien werden dort ausdrücklich als `NOT_APPLICABLE` protokolliert.
  - Die explizite Darstellung der drei statischen Hash-Initialisierer ist
    semantikfreies Compilerrauschen.
- [x] `summon-play-state-lifetime`
  - Ziele: beide öffentlichen `Execute`-Überladungen und die private
    Spawn-Methode von `SummonFlamer` und `SummonSpirit` sowie
    `PlayState.Dispose`
  - Technik: sechs Transpiler entfernen genau vier Zuweisungen an `mPlayState`
    und ersetzen jeweils genau vier spätere Feldzugriffe durch
    `PlayState.RecentPlayState`. Ein siebter Transpiler fügt die Freigabe beider
    statischen `CharacterTemplate`-Felder ausschließlich in den initialisierten
    Dispose-Pfad ein.
  - Fehlerfälle: beide Fähigkeiten halten den beim Auslösen übergebenen
    Levelzustand fest, verwenden später dessen veralteten NavMesh und behalten
    zusätzlich levelgeladene Templates über das Entladen hinaus.
  - Kontrollverhalten: Rückgabewert, Besitzerbehandlung und der Client-Pfad der
    beiden öffentlichen Überladungen bleiben erhalten. Der Spawn-Test erreicht
    weiterhin genau den ursprünglichen NavMesh-Aufruf.
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0: alle sieben Fehlerfälle bestehen
    nicht; der NavMesh stammt aus dem gespeicherten Zustand.
  - Manuelle Patch-Assembly 0.0.60 und alle Runtime-Patch-Profile: alle sieben
    Szenarien bestehen, verwenden den aktuellen Zustand und geben beide
    Templates frei.
- [x] `summon-cross-play-state-lifetime`
  - Ziele: beide öffentlichen `SummonCross.Execute`-Überladungen, die private
    `Execute()`-Methode und die bereits gepatchte Cleanup-Stelle in
    `PlayState.Dispose`
  - Technik: zwei Transpiler entfernen genau je eine Zuweisung an `mPlayState`;
    ein dritter ersetzt genau drei Feldzugriffe durch
    `PlayState.RecentPlayState`. Die gemeinsame Summon-Cleanup-Hilfe leert
    zusätzlich `sCache` und setzt `sTemplate` auf `null`.
  - Fehlerfälle: eine gepoolte Fähigkeit hält den übergebenen Levelzustand fest,
    verwendet später dessen NPC-Pool, NavMesh und EntityManager und behält den
    eigenen Pool samt levelgeladenem Template über den Levelabbau hinaus.
  - Kontrollverhalten: beide Client-Ausführungen liefern weiterhin `true`,
    behalten den vorgesehenen Besitzer und rufen Bubble- sowie SpellEffect-Pfad
    jeweils genau einmal auf.
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0: alle vier Fehlerfälle bestehen;
    manuelle Patch-Assembly 0.0.60 und alle Runtime-Patch-Profile bestehen alle
    vier Szenarien.
  - Die explizite Darstellung der `EFFECT_BUBBLE`-Initialisierung ist
    semantikfreies Compilerrauschen.
- [x] `star-gaze-detached-victim-faction`
  - Ziel: `StarGaze.Update(DataChannel, float)`
  - Technik: ein Transpiler ersetzt genau den Zugriff
    `Victim.Template.Faction` durch `Victim.Faction`.
  - Fehlerfall: die statische Opferliste kann einen bereits deinitialisierten
    Charakter enthalten. Dessen `CharacterTemplate` wurde freigegeben, während
    die aktuelle Fraktion weiterhin als Wert am Charakter vorliegt.
  - Kontrollverhalten: Effektstopp, `Confuse`, Entfernen des abgelaufenen
    Eintrags und das Verhalten einer leeren Opferliste bleiben erhalten.
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0 werfen im Fehlerfall eine
    `NullReferenceException`; die manuelle Patch-Assembly 0.0.60 und alle
    Runtime-Patch-Profile schließen die Bereinigung vollständig ab.
  - Die sichtbare Verschiebung der statischen Initialisierungen ist
    semantikfreies Compilerrauschen und wird nicht übernommen.
- [x] `charge-ability-play-state-lifetime`
  - Ziele: die Besitzer-`Execute`- und `Update`-Methoden von `HomingCharge` und
    `StopCharge` sowie `PlayState.Dispose`
  - Technik: zwei Transpiler entfernen je eine Zuweisung an `mPlayState`, zwei
    weitere ersetzen je einen späteren Feldzugriff durch
    `PlayState.RecentPlayState`, und ein fünfter leert beide statischen Caches
    im initialisierten Dispose-Pfad.
  - Fehlerfälle: beide gepoolten Fähigkeiten halten den übergebenen
    Levelzustand fest. `HomingCharge` fragt später dessen veralteten
    `EntityManager` ab, `StopCharge` übergibt ihn an `GreaseSplash`, und beide
    Caches können über `mOwner` den alten Entity- und Levelgraphen behalten.
  - Kontrollverhalten: Besitzer, TTL, Arraygrößen und SpellEffect-Registrierung
    bleiben bei `Execute` erhalten. Ein `StopCharge`-Update unterhalb der
    Auslöseschwelle ruft weiterhin kein `GreaseSplash` auf.
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0 zeigen alle fünf
    Lebensdauerfehler; die manuelle Patch-Assembly 0.0.60 und alle
    Runtime-Patch-Profile bestehen alle sechs Szenarien.
  - Die `RetentionRegistry`-Aufrufe sind Diagnoseinstrumentierung. Sie werden
    zusammen mit dem Diagnostics-Block migriert und sind nicht Bestandteil
    dieses funktionalen Fixes.

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

| Magicka-Version | Runtime-Host | Agent | AudioManager | Avatar | AIStateAttack | AIStateMove | BossHealthBar | ChargeAbilities | ChillyBlast | CompanyState | ControlManager | DeflectionAura | DrainLife | DrinkBlood | EntityManager | EntityStateStorage | Flash | Helper | Interactable | InventoryBox | MagickCamera | HUDManager | Machine | Jormungandr | PackLicense | PlayState | PoisonSpray | Portal | RandomMine | SpawnSlime | SummonFlamer | SummonSpirit | SummonCross | StarGaze | Starfall | SubMenuMain | VersusRuleset |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1.10.4.2 | erzeugt | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| 1.4.16.0 | erzeugt | PASS | PASS | PASS | PASS | PASS | PASS | PASS | NOT_APPLICABLE | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | NOT_APPLICABLE | PASS | PASS | PASS | NOT_APPLICABLE | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | NOT_APPLICABLE | NOT_APPLICABLE |
| 1.5.1.0 | erzeugt | PASS | PASS | PASS | PASS | PASS | PASS | PASS | NOT_APPLICABLE | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | NOT_APPLICABLE | PASS | PASS | PASS | NOT_APPLICABLE | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | NOT_APPLICABLE | NOT_APPLICABLE |

`NOT_APPLICABLE` bedeutet hier nicht „ungeprüft“. Die alten Assemblies
enthalten weder die spätere `HUDManager`-Klasse noch `WorldSyncMessage` und
`PlayState.AddWorldSyncMessage`. Die Patchpläne protokollieren dies und fahren
mit allen anderen Patchgruppen fort.

## Datei-Checkliste der manuellen Patch-Assembly

Grundlage ist der kommentarbereinigte ILSpy-C#-Vergleich zwischen den oben
genannten 1.10.4.2-Hashes. Er enthält 220 unterschiedliche C#-Dateien. Die
Eingaben und Abhängigkeiten werden vor ILSpy isoliert bereitgestellt, damit der
Ablageort einer EXE die Auflösung von Typen und damit die Inventur nicht ändert.

Aktueller Stand: 32 Dateien vollständig, 11 Dateien teilweise und 177 Dateien noch
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
- [ ] `Magicka/AI/Agent.cs` — TEILWEISE: Body-Guard in `ChooseTarget`, Transpiler und 2 Drei-Wege-Szenarien; Initialisierungs-, Cleanup- und Dispose-Änderungen sind noch offen.
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
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SpawnSlime.cs` — VOLLSTÄNDIG: gespeicherter PlayState in `Execute` und beide veralteten NavMesh-Zugriffe, 3 Transpiler und 3 Drei-Wege-Szenarien; der leere `DisposeCache()` und die statischen Hash-Initialisierer ändern kein Laufzeitverhalten.
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
- [x] `Magicka/GameLogic/GameStates/CompanyState.cs` — VOLLSTÄNDIG: Content-Dispose nach Controller- und Tome-Cleanup, Transpiler und ein Drei-Wege-Reihenfolgetest; die statische Initialisierer-Umschreibung ist semantikfreies Compilerrauschen.
- [ ] `Magicka/Localization/LanguageManager.cs`
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/RandomMine.cs` — VOLLSTÄNDIG: ungenutzte PlayState-Referenz im Singleton, Transpiler und 3 Drei-Wege-Szenarien; die sichtbare statische Initialisierer-Umschreibung ist semantikfreies Compilerrauschen.
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/DrainLife.cs` — VOLLSTÄNDIG: ungenutzte PlayState-Referenz im erfolgreichen Effektpfad, Transpiler und 2 Drei-Wege-Szenarien; die statische Initialisierer-Umschreibung ist semantikfreies Compilerrauschen.
- [ ] `Magicka/Levels/Liquid.cs`
- [ ] `Magicka/Levels/Triggers/Actions/SetDialogHint.cs`
- [ ] `Magicka/GameLogic/Controls/Controller.cs`
- [x] `Magicka/GameLogic/Entities/EntityStateStorage.cs` — VOLLSTÄNDIG: PlayState-Lebensdauer in Konstruktor und `Restore`, 2 Runtime-Patches und 3 Drei-Wege-Szenarien.
- [ ] `Magicka/WebTools/Paradox/ParadoxPopupUtils.cs`
- [ ] `Magicka/GameLogic/UI/ShadowBlobs.cs`
- [ ] `Magicka/GameLogic/Entities/AnimationClipAction.cs`
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SpawnSlimeOverkill.cs` — VOLLSTÄNDIG: gespeicherter PlayState in `Execute`, Transpiler und ein Drei-Wege-Szenario; die statische Initialisierer-Darstellung ist semantikfreies Compilerrauschen.
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/DeflectionAura.cs`
  — VOLLSTÄNDIG: ungenutzte PlayState-Referenz in `Execute`, Transpiler und 2
  Drei-Wege-Szenarien; die statische Hash-Initialisierer-Umschreibung ist
  semantikfreies Compilerrauschen.
- [ ] `Magicka/Graphics/NotifierButton.cs`
- [ ] `Magicka/GameLogic/Statistics/StatisticsManager.cs`
- [ ] `Magicka/Graphics/TextBox.cs`
- [x] `Magicka/Levels/Triggers/Actions/AssignItem.cs` — VOLLSTÄNDIG:
  ausschließlich semantikfreie Darstellung derselben drei statischen
  Hash-Initialisierungen in einem expliziten Typinitialisierer; Reihenfolge,
  aufgerufene Methode und zugewiesene Werte sind im IL identisch.
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/JudgementSpray.cs`
- [ ] `Magicka/GameLogic/Spells/IceSpikes.cs`
- [ ] `Magicka/GameLogic/GameStates/InGameMenus/InGameMenuOptions.cs`
- [x] `Magicka/GameLogic/GameStates/Menu/Main/SubMenuMain.cs` — VOLLSTÄNDIG: Gamepad-B öffnet die vorhandene Beenden-Bestätigung, Keyboard/Maus behält den Cursorpfad; Prefix und 2 Drei-Wege-Szenarien. Die leere manuelle Markermethode hat kein Laufzeitverhalten und wird nicht übernommen.
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Starfall.cs` — VOLLSTÄNDIG: statische PlayState-Retention und veraltete Update-Zugriffe, 2 Transpiler und 3 Drei-Wege-Szenarien; lokale Variablennamen sind nicht Teil des Runtime-Patches.
- [ ] `Magicka/GameLogic/Entities/ChantSpellManager.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Zap.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/VladZap.cs`
- [ ] `Magicka/Network/EntityUpdateMessage.cs`
- [ ] `Magicka/GameLogic/Entities/Bosses/CthulhuMist.cs`
- [ ] `Magicka/GameLogic/Entities/Bosses/BossCollisionZone.cs`
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/DrinkBlood.cs` — VOLLSTÄNDIG: ungenutzte PlayState-Referenz in `Execute`, Transpiler und 2 Drei-Wege-Szenarien.
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
- [x] `Magicka/GameLogic/UI/BossHealthBar.cs` — VOLLSTÄNDIG: Konstruktor sowie `Scene`-Getter und -Setter, 3 Runtime-Patches und 3 Drei-Wege-Szenarien.
- [ ] `Magicka/GameLogic/GameStates/Menu/Main/Options/SubMenuOptionsControls.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SummonBug.cs`
- [x] `Magicka/Levels/Versus/VersusRuleset.cs` — VOLLSTÄNDIG: fehlender Avatar beim Wiederbeleben, Transpiler und 3 Drei-Wege-Szenarien.
- [x] `Magicka/AI/AgentStates/AIStateMove.cs` — VOLLSTÄNDIG: Body-Guards in `OnEnter` und `OnExecute`, 2 Transpiler und 4 Drei-Wege-Szenarien.
- [x] `Magicka/Levels/Packs/MagickPack.cs` — VOLLSTÄNDIG: Custom-Lizenz in beiden Settern, 2 Transpiler und gemeinsame Pack-Szenarien.
- [x] `Magicka/Levels/Packs/ItemPack.cs` — VOLLSTÄNDIG: Custom-Lizenz in beiden Settern, 2 Transpiler und gemeinsame Pack-Szenarien.
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/OtherworldlyDischarge.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/MutateBeastman.cs`
- [ ] `Properties/AssemblyInfo.cs`
- [x] `Magicka/Audio/AudioManager.cs` — VOLLSTÄNDIG: `StopAll` überspringt
  bereits freigegebene Cues, Transpiler und 2 Drei-Wege-Szenarien; die
  statische String-Initialisierer-Umschreibung ist semantikfreies
  Compilerrauschen.
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
- [x] `Magicka/Graphics/Flash.cs` — VOLLSTÄNDIG: gespeicherte Szenenreferenz in
  `Execute` und veralteter Szenenzugriff in `Update`, 2 Transpiler und 2
  Drei-Wege-Szenarien; der leere `IDisposable`-Wrapper und die Darstellung des
  statischen Lock-Initialisierers ändern kein Laufzeitverhalten.
- [ ] `Magicka/GameLogic/UI/GenericHealthBar.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/WaveEntity.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/EtherealClone.cs`
- [ ] `Magicka/GameLogic/Entities/Snare.cs`
- [x] `Magicka/Levels/Triggers/Interactable.cs` — VOLLSTÄNDIG: fehlende Szene
  oder fehlendes Levelmodell in `Highlight`, Prefix und 3
  Drei-Wege-Szenarien; die Wiederverwendung des Schleifenindex im manuellen
  Dekompilat ist semantikfreies Compilerrauschen.
- [ ] `Magicka/Levels/Packs/PackMan.cs` — TEILWEISE: Lizenzprädikat für Pack-Setter migriert; die vier Aufrufe in `SubMenuCharacterSelect` sind noch offen.
- [ ] `Magicka/GameLogic/Controls/ControlManager.cs` — TEILWEISE: die drei `Controller`-Überladungen der Player-Input-Sperre sind mit 3 Prefixen und 3 Drei-Wege-Szenarien migriert; `HybridInputSupport.Update` in `HandleInput` ist noch offen.
- [ ] `Magicka/GameLogic/Player.cs`
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SummonSpirit.cs` — VOLLSTÄNDIG: beide gespeicherten PlayState-Zuweisungen, vier veraltete Spawn-Zugriffe und statischer Template-Cache, 4 Transpiler und gemeinsame Drei-Wege-Szenarien.
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SummonFlamer.cs` — VOLLSTÄNDIG: beide gespeicherten PlayState-Zuweisungen, vier veraltete Spawn-Zugriffe und statischer Template-Cache, 4 Transpiler und gemeinsame Drei-Wege-Szenarien.
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/HomingCharge.cs` — TEILWEISE: gespeicherter PlayState, veralteter EntityManager-Zugriff und statischer Levelcache sind mit 3 Transpilern und gemeinsamen Drei-Wege-Szenarien migriert; die GC-Diagnosemarkierungen folgen im Diagnostics-Block.
- [ ] `Magicka/Graphics/EffectManager.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Shrink.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SummonUndead.cs`
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SummonCross.cs` — VOLLSTÄNDIG: beide gespeicherten PlayState-Zuweisungen, drei veraltete Spawn-Zugriffe sowie Pool- und Template-Freigabe, 3 Transpiler und 4 Drei-Wege-Szenarien; die statische Hash-Initialisierung ist semantikfreies Compilerrauschen.
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/StopCharge.cs` — TEILWEISE: gespeicherter PlayState, veralteter `GreaseSplash`-Zustand und statischer Levelcache sind mit 3 Transpilern und gemeinsamen Drei-Wege-Szenarien migriert; die GC-Diagnosemarkierungen folgen im Diagnostics-Block.
- [ ] `Magicka/GameLogic/GameStates/MenuState.cs`
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/ChillyBlast.cs` — VOLLSTÄNDIG: gespeicherter PlayState in `Execute` und beide veralteten EntityManager-Zugriffe in `Update`, 2 Transpiler und 3 Drei-Wege-Szenarien; statische Hash-Initialisierer sind semantikfreies Compilerrauschen. In 1.4.16.0 und 1.5.1.0 ist die Klasse nicht vorhanden.
- [ ] `Magicka/GameLogic/Spells/SpellEffects/LightningSpell.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Haste.cs`
- [ ] `Magicka/GameLogic/Entities/Bosses/PropBoss.cs`
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/StarGaze.cs` — VOLLSTÄNDIG: abgelaufene, bereits deinitialisierte Opfer verwenden bei der Bereinigung die weiterhin verfügbare aktuelle Fraktion, ein Transpiler und 2 Drei-Wege-Szenarien; die statische Initialisierer-Umschreibung ist semantikfreies Compilerrauschen.
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/PoisonSpray.cs` — VOLLSTÄNDIG: gespeicherter PlayState in `Execute` und beide veralteten EntityManager-Zugriffe in `Update`, 2 Transpiler und 3 Drei-Wege-Szenarien; lokale `yaw`-Variable und statische Hash-Initialisierer sind semantikfreies Compilerrauschen.
