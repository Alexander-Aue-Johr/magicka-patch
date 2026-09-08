# Runtime-Patcher-Bericht

Stand: 8. September 2026

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
13. `src/BehaviorProbe/BehaviorSuite.cs` und die `*Scenarios.cs`-Dateien
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
- [x] `ultrawide-hud-safe-area`
  - Ziele: `KeyboardHUD.RenderData.DrawIcon(...)` und
    `TutorialManager.HintRenderData.Draw(float)`.
  - Technik: Ein Prefix ergänzt den horizontalen Safe-Area-Einzug zum
    vorhandenen Keyboard-HUD-Offset. Ein eng geprüfter Transpiler passt nach
    dem vorhandenen Tutorial-Positionsswitch ausschließlich die lokale
    X-Position für `CenterRight` und `BottomRight` an.
  - Verhalten: Auf breiteren Bildschirmen wird eine auf Bildschirmhöhe
    skalierte, zentrierte 16:9-Fläche verwendet. Bei 16:9 und schmaleren
    Formaten ist der linke Einzug null und die rechte Berechnung identisch zur
    bisherigen 95-Prozent-Verankerung. Welt-Rendering, Kamera und vertikale
    UI-Positionen bleiben unverändert.
  - Vier Rechenszenarien prüfen 5120×1440 und 1920×1080. Der Originalassembly
    fehlt der Helfer; die manuelle Patch-Assembly und alle Runtime-Profile
    liefern dieselben Werte. Beide Zielmethoden werden zusätzlich über ihren
    vollständigen Signatur-, Feld- und Lokalspeichervertrag registriert.
- [x] `tutorial-manager-play-state-lifetime`
  - Ziele: `TutorialManager.Initialize(PlayState)`, `UpdateResolution()` und
    `Update(DataChannel, float)`.
  - Technik: Drei eng geprüfte Transpiler entfernen genau eine gespeicherte
    PlayState-Zuweisung und ersetzen 2 beziehungsweise 11 Feldzugriffe durch
    `PlayState.RecentPlayState`. Der binäre Feldvertrag der Originalassembly
    bleibt erhalten, aber die gepatchten Methoden schreiben oder lesen das Feld
    nicht mehr.
  - Verhalten: Der Prozess-Singleton hält einen beendeten PlayState nicht mehr
    bis zum nächsten Levelstart fest. Tutorial-Timing, Statusänderungen und
    frühe Rückkehrpfade bleiben erhalten und arbeiten mit dem aktuellsten
    PlayState.
  - Zwei Drei-Wege-Szenarien prüfen die Freigabe unmittelbar nach dem früheren
    Zuweisungspunkt sowie einen Update-Pfad mit fehlender Alt- und gültiger
    aktueller Referenz.
- [x] `ethereal-clone-play-state-lifetime`
  - Ziele: `EtherealClone.Execute(ISpellCaster, PlayState)` und
    `SpawnClone(CharacterTemplate, int, uint)`.
  - Technik: Zwei eng geprüfte Transpiler entfernen genau eine gespeicherte
    PlayState-Zuweisung und ersetzen den einzigen späteren Feldzugriff durch
    `PlayState.RecentPlayState`.
  - Verhalten: Der Prozess-Singleton hält keinen beendeten PlayState mehr. Die
    Klonplatzierung fragt das NavMesh des aktuellen PlayState ab; Besitzer,
    Zufallsposition, Netzwerkpaket und Spawnablauf bleiben unverändert.
  - Zwei Drei-Wege-Szenarien stoppen unmittelbar nach der früheren Zuweisung
    beziehungsweise beim NavMesh-Aufruf und unterscheiden alte und aktuelle
    Objektidentität.
- [x] `break-barriers-play-state-lifetime`
  - Ziele: `BreakBarriers.Execute(ISpellCaster, PlayState)` und
    `Update(DataChannel, float)`.
  - Technik: Zwei eng geprüfte Transpiler entfernen genau eine gespeicherte
    PlayState-Zuweisung und ersetzen beide späteren Feldzugriffe durch
    `PlayState.RecentPlayState`.
  - Verhalten: Ein aktiver oder gepoolter Effekt hält keinen alten PlayState
    mehr. Räumliche Abfrage und Listenrückgabe verwenden denselben aktuellen
    EntityManager; TTL, Besitzer und Barrierenfilter bleiben unverändert.
  - Zwei Drei-Wege-Szenarien prüfen den vollständigen Execute-Pfad und zeichnen
    bei getrennten alten und aktuellen Managern beide Update-Empfänger auf.
- [x] `tesla-field-play-state-lifetime`
  - Ziel: privater Konstruktor `TeslaField(PlayState)`.
  - Technik: Ein eng geprüfter Konstruktor-Transpiler entfernt ausschließlich
    die einzige Zuweisung an das ungenutzte Feld `mPlaystate`. Das Feld bleibt
    für den binären Vertrag der Originalassembly bestehen.
  - Verhalten: Weder die von `InitializeCache` vorab erzeugten Poolobjekte noch
    eine Ersatzallokation aus `GetFromCache` halten den Levelzustand fest.
    Poolgröße, Entnahmereihenfolge und Tesla-Effektlogik bleiben unverändert.
  - Zwei Drei-Wege-Szenarien prüfen den vorab erzeugten Pool mit drei Einträgen
    und die Ersatzallokation bei leerem Pool.
- [x] `generic-health-bar-current-scene`
  - Ziel: `GenericHealthBar.Update(DataChannel, float)`.
  - Technik: Ein eng geprüfter Transpiler ersetzt den einzigen Zugriff auf
    `mScene` durch `PlayState.RecentPlayState.Scene`.
  - Fehlerfall: Der Health-Bar-Konstruktor speichert die anfängliche Szene. Ein
    gewöhnlicher Szenenwechsel ersetzt die aktuelle Szene, aktualisiert diesen
    Verweis jedoch nicht. Spätere Renderdaten gehen dadurch an die alte Szene.
  - Verhalten: Berechnungen, Timing und Renderdaten bleiben unverändert; nur
    der Empfänger der vorhandenen Renderübergabe folgt dem aktuellen PlayState.
  - Ein Drei-Wege-Szenario verwendet verschiedene gespeicherte und aktuelle
    Szenen und zeichnet Empfänger sowie Renderdatenidentität auf.
- [x] `grease-trail-play-state-lifetime`
  - Ziele: `GreaseTrail.Execute(ISpellCaster, PlayState)` und
    `Update(DataChannel, float)`.
  - Technik: Zwei eng geprüfte Transpiler entfernen genau eine gespeicherte
    PlayState-Zuweisung und ersetzen beide späteren Feldzugriffe durch
    `PlayState.RecentPlayState`.
  - Verhalten: Der gepoolte Effekt hält keinen beendeten PlayState mehr.
    `GreaseField.GetInstance` und `EntityManager.AddEntity` arbeiten mit
    demselben aktuellen Levelzustand; Timing, Position und Netzwerkpfad bleiben
    unverändert.
  - Zwei Drei-Wege-Szenarien prüfen den normalen Offline-Execute-Pfad sowie bei
    getrennten alten und aktuellen Zuständen beide Update-Empfänger.
- [x] `spell-effect-current-play-state`
  - Ziele: `SpellEffect.IntializeCaches(PlayState, ContentManager)`,
    `LightningSpell.GetFromCache()` und
    `LightningSpell.CastUpdate(float, ISpellCaster, out float)`.
  - Technik: Drei eng geprüfte Transpiler entfernen die einzige globale
    PlayState-Zuweisung und ersetzen genau zwei Cache- sowie einen Cast-Zugriff
    durch `PlayState.RecentPlayState`.
  - Verhalten: Der SpellEffect-Typ hält keinen beendeten Levelzustand mehr.
    LightningSpell registriert beide Cachepfade im aktuellen Effektcontainer
    und übergibt dem Blitzwurf den aktuellen PlayState. Schaden, Timing und
    Cache-Reihenfolge bleiben unverändert.
  - Vier Drei-Wege-Szenarien prüfen alle sechs ursprünglichen Cacheinitialisierer,
    beide Cachepfade und den tatsächlich an `LightningBolt.Cast` übergebenen
    Zustand.
- [x] `effect-manager-duplicate-asset`
  - Ziel: `EffectManager.ReadDirectory(DirectoryInfo)`
  - Technik: ein eng geprüfter Transpiler ergänzt vor dem vorhandenen
    `VisualEffect.FromFile`-Aufruf die Abfrage des bereits berechneten Hashes.
    Bei einem Treffer springt er über Parser und `Dictionary.Add`, schreibt den
    übersprungenen Effektnamen und setzt danach die unveränderte Verzeichnissuche
    fort.
  - Fehlerfall: Zwei XML-Dateien mit demselben kleingeschriebenen Basisnamen
    werden auf denselben Schlüssel abgebildet. Das Original parst beide und
    bricht beim zweiten `Dictionary.Add` mit `ArgumentException` ab.
  - Kontrollverhalten: unterschiedliche Namen werden weiterhin jeweils einmal
    geparst und eingetragen; Traversierung, Hashfunktion und erste Definition
    bleiben unverändert.
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0 laden im Duplikatfall zweimal und
    werfen. Die manuelle Patch-Assembly 0.0.60 und alle Runtime-Patch-Profile
    laden einmal, behalten einen Eintrag und schließen die Initialisierung ab.
    Der Kontrollfall besteht in allen Profilen.
- [x] `time-warp-current-play-state`
  - Ziele: beide `TimeWarp.Execute`-Überladungen sowie `TimeWarp.Update` und
    `TimeWarp.OnRemove`; außerdem `TimeWarpStaff.Execute`, `Update` und
    `OnRemove`.
  - Technik: Sieben eng geprüfte Transpiler entfernen genau die drei
    Zuweisungen an `mPlayState` und ersetzen in den laufenden Effekten genau
    siebzehn Feldzugriffe durch `PlayState.RecentPlayState`.
  - Verhalten: Die beiden Singleton-Effekte halten keinen beendeten PlayState
    mehr. Start, Zeitmultiplikator, Ausblendung und Wiederherstellung der
    Sättigung verwenden den aktuellen PlayState; Dauer, Audio, visueller
    Effekt und Besitzerbehandlung bleiben unverändert.
  - Sechs Drei-Wege-Szenarien verwenden absichtlich verschiedene übergebene,
    veraltete und aktuelle Zustände. Sie prüfen Start, Update und Entfernen für
    beide Effekttypen. Original 1.10.4.2, 1.4.16.0 und 1.5.1.0 behalten oder
    verändern den veralteten Zustand; die manuelle Patch-Assembly und alle
    Runtime-Patch-Profile verwenden ausschließlich den aktuellen Zustand.
- [x] `spell-wheel-current-play-state`
  - Ziele: `SpellWheel.Initialize(PlayState)` und
    `SpellWheel.Update(DataChannel, float)`.
  - Technik: Zwei eng geprüfte Transpiler entfernen die einzige Zuweisung an
    `mPlayState` und ersetzen je nach Magicka-Version den einen oder die drei
    vorhandenen Feldzugriffe durch `PlayState.RecentPlayState`.
  - Verhalten: Das SpellWheel hält keinen beendeten PlayState mehr und reicht
    seine unveränderten Renderdaten an die aktuelle Szene weiter. Icon-Timer,
    Spielerprüfung, Controllerlogik und Position bleiben unverändert.
  - Zwei Drei-Wege-Szenarien prüfen die Freigabe nach `Initialize` und den
    tatsächlichen Szenenempfänger beim Update. Die Matrix umfasst Original,
    manuellen Patch und Runtime-Patch sowie Magicka 1.4.16.0 und 1.5.1.0.
- [x] `earthquake-current-play-state`
  - Ziele: `EarthQuake.Execute(Vector3, PlayState)`, `NewQuake(ref Vector3,
    float)` und `Quake(ref Vector3, float)`.
  - Technik: Drei eng geprüfte Transpiler entfernen die einzige Zuweisung an
    `mPlayState` und ersetzen vier Feldzugriffe durch
    `PlayState.RecentPlayState`.
  - Verhalten: Der Effekt hält keinen beendeten PlayState mehr. Kollisionstest,
    Kamerawackeln sowie Abruf und Rückgabe der Entity-Liste verwenden den
    aktuellen Zustand. Stärke, Radius, Audio und visueller Effekt bleiben
    unverändert.
  - Drei Drei-Wege-Szenarien verwenden unterschiedliche übergebene, veraltete
    und aktuelle Zustände und prüfen die tatsächlichen Empfänger. Die Matrix
    umfasst Original, manuellen Patch und Runtime-Patch sowie Magicka 1.4.16.0
    und 1.5.1.0.
- [x] `arrow-rain-current-play-state`
  - Ziele: beide öffentlichen `ArrowRain.Execute`-Überladungen, die private
    `Execute`-Methode sowie `Launch`, `Update` und `OnRemove`.
  - Technik: Sechs eng geprüfte Transpiler entfernen zwei PlayState- und eine
    Szenenzuweisung. Sechs spätere Zugriffe werden durch
    `PlayState.RecentPlayState` ersetzt.
  - Verhalten: Das Singleton hält weder Aktivierungszustand noch Szene fest.
    Besitzerlose Geschosse, Blitzszene, Kamera, Blitz-Cast und Lichtreset
    verwenden den aktuellen Zustand. Timing, Schaden, Projektil- und
    Netzwerkdaten bleiben unverändert.
  - Vier Drei-Wege-Szenarien prüfen beide Aktivierungsüberladungen, den
    tatsächlichen State-Parameter der Geschosserzeugung im Update und die beim
    Entfernen geänderte Szene. Exakte IL-Verträge decken zusätzlich den
    Launch- und den vollständigen Blitzpfad ab.
- [x] `healing-rain-current-play-state`
  - Ziele: beide öffentlichen `HealingRain.Execute`-Überladungen, die private
    `Execute`-Methode, `Update` und `OnRemove`.
  - Technik: Vier eng geprüfte Transpiler entfernen beide PlayState-Zuweisungen
    und ersetzen sechs spätere Zugriffe durch `PlayState.RecentPlayState`. Ein
    Prefix bildet die vorhandene OnRemove-Reihenfolge nach und gibt danach
    Szene und Caster frei.
  - Verhalten: Indoor-Prüfung, Kamera, Szene, Regenzähler und EntityManager
    stammen aus dem aktuellen Zustand. Cue, visueller Effekt und Licht werden
    in ihrer bisherigen Reihenfolge beendet; eine fehlende Szene ist dabei
    sicher. Heilung, Wet-Status und Filter bleiben unverändert.
  - Fünf Drei-Wege-Szenarien prüfen beide Aktivierungen, den Updatepfad, die
    vollständige Referenzfreigabe und den Abbau ohne Szene. Der manuelle Patch
    besteht die ersten drei, behält aber im Gegensatz zum Runtime-Patch die
    letzten Szene-/Caster-Referenzen und ist ohne Szene nicht null-sicher.
- [x] `in-game-menu-current-play-state`
  - Ziele: `InGameMenu.Initialize`, 20 aktuelle Menümethoden und der nur in
    1.4/1.5 vorhandene `InGameMenuMain.IDraw`-Pfad.
  - Technik: Ein Transpiler entfernt die statische PlayState-Zuweisung. Ein
    Postfix installiert nach dem ersten `InGameMenu.Initialize` die für die
    jeweilige Spielversion passenden Read-Transpiler. Die verzögerten Ziele
    werden beim Bootstrap vollständig aufgelöst und als `DEFERRED`
    protokolliert.
  - Verhalten: Die Menü-Singletons halten keinen beendeten PlayState mehr.
    Rendering, Rückkehr, Neustart, Endgame, Magickfreigabe,
    Auflösungsanpassung und Statistikseiten verwenden den aktuellen Zustand.
  - Der verzögerte Installationszeitpunkt ist erforderlich: Harmony JITtet
    große Menümethoden beim Patchen. Der Versus-Statistikpfad kann dabei den
    `Gamer`-Initialisierer erreichen, der vor `Game.Instance` nicht sicher
    ausgeführt werden kann.
  - Zwei Drei-Wege-Szenarien prüfen die statische Freigabe und die tatsächlich
    angesprochene Renderszene. 1.10 ersetzt 43 Reads, 1.4 ersetzt 41 und 1.5
    ersetzt 44. Der vollständige Build besteht für alle drei Versionen.
- [x] `in-game-menu-stack-cleanup`
  - Ziel: `PlayState.Dispose`
  - Technik: Ein Transpiler fügt unmittelbar nach dem eindeutigen
    `BossFight.Clear()`-Aufruf einen gemeinsamen Cleanup-Aufruf ein.
  - Verhalten: Der vorhandene statische `InGameMenu.sMenuStack` wird nur im
    initialisierten Dispose-Pfad geleert. Ein nicht initialisierter PlayState
    kehrt weiterhin zurück, ohne den Stack zu verändern.
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0 behalten den Testeintrag. Die
    manuelle Patch-Assembly 0.0.60 und alle Runtime-Patch-Profile leeren ihn.
  - Der kommentarlos dekompilierte Runtime-Diff enthält nur die neue
    Patchklasse und ihre Registrierung. Alle fünf geänderten konkreten
    Runtime-Methoden JITten unter CLR 2 und Mono 6.12 ohne Skip.
- [x] `magicks-menu-language-selection`
  - Ziel: `InGameMenuMagicks.LanguageChanged`
  - Technik: Ein Transpiler ersetzt ausschließlich die abschließende
    ungeschützte `mDescriptions[mMarkedItem]`-Auswahl durch einen validierten
    Zugriff. Die vorherige Neuübersetzung aller Beschreibungen bleibt erhalten.
  - Fehlerfälle: `mMarkedItem == -1` und ein Index hinter dem Array leeren den
    Beschreibungstext. Ein gültiger Index setzt weiterhin den neu umbrochenen
    übersetzten Text.
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0 werfen in beiden Fehlerfällen. Die
    manuelle Patch-Assembly 0.0.60 und alle Runtime-Patch-Profile bestehen die
    beiden Fehlerfälle und den gültigen Kontrollfall.
  - Der kommentarlos dekompilierte Runtime-Diff enthält nur die neue
    Patchklasse und ihre Registrierung. Alle sechs geänderten konkreten
    Runtime-Methoden JITten unter CLR 2 und Mono 6.12 ohne Skip.
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
- [x] `loading-screen-depth-buffer-restore`
  - Ziel: `LoadingScreen.EndDraw`
  - Technik: ein Transpiler verschiebt die vorhandene fünfteilige
    `DepthStencilBuffer`-Zuweisung vor den vorhandenen `GraphicsDevice.Clear`.
    Es wird kein Geräteaufruf ergänzt oder entfernt.
  - Fehlerfall: der Originalcode fordert das Leeren von Depth und Stencil an,
    bevor der für den verwalteten Ladevorgang gespeicherte Buffer wieder am
    Gerät hängt.
  - Kontrollverhalten: Render-State und Render-Target werden in unveränderter
    Reihenfolge wiederhergestellt. Im nicht verwalteten Modus finden weder
    Buffer-Zuweisung noch Clear statt.
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0 liefern `clear,depth`; die manuelle
    Patch-Assembly 0.0.60 und alle Runtime-Patch-Profile liefern `depth,clear`.
  - Die Zusammenfassung der beiden lokalen `DirectoryInfo`-Anweisungen in
    `Initialize` ist semantikfreies Compilerrauschen und wird nicht übernommen.
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
- [x] `khan-killplane-defeat-fallback`
  - Ziel: `GiveOrder.Exec()`
  - Technik: ein Transpiler setzt nach dem ersten der beiden exakt erwarteten
    `Agent.SetOrder`-Aufrufe einen eng begrenzten Runtime-Handler ein; der
    zweite, flächenbasierte Order-Pfad bleibt unverändert
  - Fehlerfall: `#boss_n06` ist bereits terminiert, bevor sein erster
    Animation-Event den vorhandenen Defeat-Trigger ausführen kann
  - Verhalten: nur ein terminierter `WarlordCharacter` mit passender Order-ID,
    erstem Animation-Event und Trigger ungleich null führt den vorhandenen
    `GameScene.ExecuteTrigger`-Pfad aus
  - Original 1.10.4.2: der Trigger wird nicht ausgeführt
  - Manuelle Patch-Assembly 0.0.60 und Runtime-Patch: der Trigger wird einmal
    ausgeführt; lebender Kahn, fremde ID und Trigger null bleiben unverändert
  - Magicka 1.4.16.0 und 1.5.1.0 besitzen dieselbe geprüfte Order-Struktur;
    Runtime-Patch und alle vier Szenarien bestehen dort ebenfalls
  - Die statische Action-Liste und ihre PlayState-Referenz werden durch den
    gemeinsamen Level-Pool-Cleanup freigegeben. Nur die bestehende
    Recovery-Telemetrie folgt noch mit dem gemeinsamen Telemetrieblock.
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
- [x] `character-select-pack-display`
  - Ziel: `SubMenuCharacterSelect.DrawPacksList(float)`
  - Technik: ein Transpiler ersetzt nur die vier vorhandenen Vergleiche von
    `ItemPack.License` und `MagickPack.License` mit `HackHelper.License.Yes`
    durch das bereits verifizierte Runtime-Prädikat
  - Verhalten: Vorschaubilder und Nicht-verwendet-Markierungen folgen nun
    derselben Custom-Content-Regel wie die Pack-Setter; Store-, Eingabe-,
    Aktivierungs- und DLC-Eigentumslogik bleiben unverändert
  - Fehlerfälle: `Custom` offline und in einer nicht VAC-geschützten Sitzung
    sowie eine fehlende Registrierung an der Render-Methode
  - Kontrollfälle: `Custom` mit VAC sowie `Yes` und `No`
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0: die beiden erlaubten
    Custom-Anzeigefälle und der Registrierungsfall schlagen vor dem Patch fehl
  - Manuelle Patch-Assembly 0.0.60 und alle Runtime-Patch-Profile: alle sechs
    zusätzlichen Anzeigeszenarien bestehen
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
- [x] `direct-input-load-guards`
  - Ziele: der Controllerlisten-Aufruf im Konstruktor und in `OnEnter` von
    `SubMenuOptionsControls`, `MenuState.FindNewControllers()` und
    `MenuState.Update(...)`
  - Technik: vier Transpiler ersetzen nur die drei DirectInput-gefährdeten
    Aufrufe durch typgenaue Wrapper und fügen den vorhandenen verzögerten
    Warnpfad nach dem Basis-Update ein
  - Fehlerfälle: `FileNotFoundException` beim Öffnen der Controlleroptionen und
    `FileLoadException` bei der periodischen Controllererkennung
  - Verhalten: nach dem ersten Ladefehler bleiben weitere Scans in dieser
    Sitzung aus; die Scanfrist wird auf fünf Sekunden gesetzt und genau eine
    Warnung erscheint, sobald weder ein Paradox-Fehler noch ein anderes Popup
    aktiv ist
  - Kontrollfälle: ein verfügbarer Controllerpfad wird unverändert ausgeführt;
    eine fachfremde `InvalidOperationException` wird weiterhin weitergereicht
  - Original 1.10.4.2: beide Ladefehler verlassen die Call-Sites und es gibt
    keinen Warnpfad. Manuelle Patch-Assembly 0.0.60 und Runtime-Patch bestehen
    alle fünf Szenarien.
  - Magicka 1.4.16.0 und 1.5.1.0 bestehen mit Runtime-Patch die beiden
    Ladefehler- und beide Kontrollszenarien. Das spätere Paradox-Popupsystem
    fehlt dort, daher ist nur das Warnszenario ausdrücklich `NOT_APPLICABLE`.
- [x] `paradox-account-save-data-lifetime`
  - Ziele: `MenuState.OnExit()` und `Game.EndRun()`
  - Technik: zwei eng geprüfte Transpiler; der erste neutralisiert genau den
    geschlossenen `ScopedSingleton<ParadoxAccountSaveData>.Destroy`-Aufruf,
    der zweite fügt denselben Aufruf unmittelbar nach
    `ParadoxServices.Dispose` ein
  - Fehlerfall: ein später Konto-Callback läuft nach dem Menüwechsel, obwohl
    das zugehörige Save-Data-Singleton bereits zerstört wurde
  - Verhalten: Menü-, Store-, Banner-, Controller-, Netzwerk-, GameSparks-
    und Basisklassen-Cleanup bleiben an ihren bisherigen Stellen; nur die
    Kontodaten leben bis zum endgültigen Prozessabbau
  - Original 1.10.4.2: `menu:1, shutdown:0`; der Lebensdauertest schlägt fehl
  - Manuelle Patch-Assembly 0.0.60 und Runtime-Patch: `menu:0, shutdown:1`
  - Magicka 1.4.16.0 besitzt keine Paradox-Kontodaten. Magicka 1.5.1.0 nutzt
    dafür noch das prozessweite `Singleton<T>` statt `ScopedSingleton<T>`.
    Patch und Szenario melden beide Versionen ausdrücklich `NOT_APPLICABLE`.
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
- [x] `menu-image-text-language-refresh`
  - Ziel: `MenuImageTextItem.LanguageChanged()`
  - Technik: Transpiler; ergänzt die aktuelle Font-Zeilenhöhe am
    Methodenanfang und ersetzt nur den frühen Rücksprung für literalen Text
    durch `mTitle.MarkAsDirty()`
  - Fehlerfälle: die Sprache lädt einen Font mit anderer Zeilenhöhe oder ein
    literaler Titel behält Vertexdaten des vorherigen Fonts
  - Kontrollfall: der vorhandene lokalisierte Pfad ruft weiterhin genau einmal
    `GetString`, `SetText` und `UpdateBoundingBox` auf
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0: beide Aktualisierungen fehlen;
    der Kontrollfall besteht
  - Manuelle Patch-Assembly 0.0.60 und alle Runtime-Patch-Profile: alle drei
    Szenarien bestehen
- [x] `simplified-chinese-language-lookup`
  - Ziele: `LanguageManager.GetNativeName(Language)` und
    `LanguageManager.GetLanguage(string)`
  - Technik: zwei typgenaue Prefixe; nur `Language.zho` erhält den festen
    lateinischen Anzeigenamen, und nur die sieben dokumentierten chinesischen
    Konfigurationsnamen überspringen die Originalmethode
  - Fehlerfälle: die feste Sprachauswahlschrift kann den nativen chinesischen
    Namen nicht darstellen, und das Original ordnet alle sieben Namen Englisch zu
  - Kontrollverhalten: `english` sowie das vorhandene Fallback für unbekannte
    Namen liefern weiterhin `Language.eng` durch die unveränderte Originalmethode
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0 bestehen beide Fehlerfälle nicht;
    manuelle Patch-Assembly 0.0.60 und alle Runtime-Patch-Profile bestehen alle
    drei Szenarien
  - `LanguageManager.CurrentLanguage` zeichnet im manuellen Patch zusätzlich
    Telemetriekontext auf. Dieser unabhängige Aufruf folgt mit dem
    Telemetrieblock.
- [x] `paradox-popup-extra-message-cleanup`
  - Ziel: `ParadoxPopupUtils.ShowErrorPopup(string, string)`
  - Technik: Transpiler; fügt unmittelbar vor dem einzigen vorhandenen
    `Show()`-Aufruf `sPopup.ExtraMessage.Text = ""` ein
  - Fehlerfall: ein zuvor mit Fehlercodezusatz verwendetes Popup zeigt den
    alten Zusatz bei einer späteren einfachen Meldung weiter an
  - Kontrollfall: `ShowErrorPopupWithExtra(int, string)` behält Titel,
    Nachricht, Zusatz und Anzeigeaufruf unverändert
  - Original 1.10.4.2: der einfache Popup-Pfad löscht den Zusatz nicht;
    manuelle Patch-Assembly 0.0.60 und Runtime-Patch tun dies
  - Magicka 1.4 enthält das Popupsystem nicht. Magicka 1.5 verwendet die ältere
    Popup-API; beide Szenarien sind dort ausdrücklich `NOT_APPLICABLE`.
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
- [x] `summon-undead-play-state-lifetime`
  - Ziele: beide öffentlichen `SummonUndead.Execute`-Überladungen, die private
    Spawn-Methode und die bestehende Summon-Cleanup-Stelle in
    `PlayState.Dispose`
  - Technik: zwei Transpiler entfernen genau je eine Zuweisung an
    `mPlayState`; ein dritter ersetzt genau vier Feldzugriffe durch
    `PlayState.RecentPlayState`. Die gemeinsame Cleanup-Hilfe setzt das
    statische `CharacterTemplate[]` ausschließlich im initialisierten
    Dispose-Pfad auf `null`.
  - Fehlerfälle: die Fähigkeit hält den übergebenen Levelzustand fest,
    verwendet später dessen NPC-Pool, NavMesh und EntityManager und behält fünf
    levelgeladene Templates über den Levelabbau hinaus.
  - Kontrollverhalten: beide Client-Ausführungen liefern weiterhin `true`, die
    vorgesehene Besitzerbehandlung bleibt erhalten und ein nicht
    initialisierter alter `PlayState` löscht keinen aktiven Template-Cache.
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0 bestehen die vier Fehlerfälle
    nicht. Die manuelle Patch-Assembly 0.0.60 und alle Runtime-Patch-Profile
    bestehen alle fünf Szenarien.
  - Die zusätzliche Netzwerksemantik in dieser Datei gehört zu Issue #28 und
    bleibt bis zu dessen eigener Host-/Client-Verifikation separat offen.
- [x] `summon-undead-network-state`
  - Ziele: die private `SummonUndead.Execute`-Spawn-Methode auf dem Host und
    `Trigger.SpawnNPC` auf dem Client
  - Technik: der Host-Transpiler setzt das vorhandene `Bool2` im Speicher und
    kodiert zusätzlich negative Null in `Color.X`. Der Client-Transpiler
    ersetzt genau den einparametrigen `Summoned`-Aufruf durch den vorhandenen
    booleschen Overload und wertet beide Marker aus.
  - Fehlerfall: der Host kennzeichnet die Kreatur als Undead, der ursprüngliche
    Client ruft jedoch `Summoned(master)` auf und verliert den Zustand.
  - Protokoll: `Bool2` wird für `SpawnNPC` auch in 0.0.60 nicht serialisiert.
    Ein neues Byte wäre inkompatibel, weil Magicka mehrere Nachrichten in einem
    P2P-Paket aneinanderreiht. `Color.X` wird bereits als `HalfSingle`
    übertragen; negative Null bleibt beim Roundtrip erhalten, hat dieselbe
    Paketgröße und ist für alte Clients numerisch unverändert.
  - Kontrollverhalten: positive Null und `Bool2 == false` bleiben ein normaler
    Summon. Ein alter Client kann das unverändert große neue Paket lesen und
    behält sein bisheriges Verhalten.
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0 setzen beziehungsweise übernehmen
    den Undead-Zustand nicht. Die manuelle Assembly setzt nur den nicht
    übertragenen Speichermarker. Alle Runtime-Patch-Profile setzen beide
    Marker, wenden den Zustand an und bestehen den echten Write/Read-Roundtrip.
  - Die vorhandenen Telemetrieaufrufe aus 0.0.60 folgen mit dem gemeinsamen
    Runtime-Telemetrieblock; die Netzwerksemantik ist davon unabhängig.
- [x] `summon-zombie-play-state-lifetime`
  - Ziele: beide öffentlichen `SummonZombie.Execute`-Überladungen, die private
    Startmethode und `Update`
  - Technik: zwei Transpiler entfernen genau je eine Zuweisung an
    `mPlayState`; ein dritter ersetzt genau zwei Zugriffe beim Start, ein
    vierter genau vier Zugriffe während späterer Spawns durch
    `PlayState.RecentPlayState`.
  - Fehlerfall: eine aktive oder gepoolte Fähigkeit hält den beim Auslösen
    übergebenen Levelzustand fest und kann NPC-Pool, NavMesh und EntityManager
    des vorigen Levels weiterverwenden.
  - Kontrollverhalten: die Vector-Ausführung leert weiterhin den Besitzer, die
    Besitzer-Ausführung behält ihn, und ein Client versucht weiterhin keinen
    lokalen Zombie zu erzeugen. Spawnzeit, Effekte, Templates und
    Netzwerkpakete bleiben unverändert.
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0 behalten den Zustand und verwenden
    ihn beim Start und Update. Die manuelle Patch-Assembly 0.0.60 und alle
    Runtime-Patch-Profile bestehen die vier Szenarien.
  - Pool- und Template-Freigabe sind durch die bereits migrierten
    Level-Cleanup-Blöcke abgedeckt. Die sichtbaren statischen Initialisierer
    sind Compilerrauschen; RetentionRegistry-Aufrufe folgen im gemeinsamen
    Diagnostics-Block.
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
- [x] `ability-template-cache-cleanup`
  - Ziel: die bereits gepatchte Cleanup-Stelle in `PlayState.Dispose`
  - Technik: der bestehende Summon-Cleanup-Transpiler validiert zusätzlich die
    fünf exakten `CharacterTemplate`-Felder von `SummonZombie`, `SummonBug`,
    `SummonElemental`, `MutateBeastman` und `OtherworldlyDischarge` und setzt
    sie im initialisierten Dispose-Pfad auf `null`.
  - Fehlerfall: die fünf statischen Felder halten levelgeladene Templates und
    damit Inhalte des abgebauten Levels weiter fest.
  - Kontrollverhalten: ein nicht initialisierter `PlayState` bleibt
    unverändert; bereits leere Template-Felder bleiben leer.
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0 behalten alle fünf Referenzen. Die
    manuelle Patch-Assembly 0.0.60 und alle Runtime-Patch-Profile geben sie
    vollständig frei.
  - Das Original besitzt kein `Magick.DisposeMagicks`. Der Runtime-Patcher
    ergänzt deshalb nur die bestehende `PlayState.Dispose`-Injektion und fügt
    den fünf Ability-Klassen keine Methoden hinzu.
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
- [x] `active-buff-level-cache-cleanup`
  - Ziel: `PlayState.Dispose`
  - Technik: ein Transpiler leert `Haste.sCache`, `Haste.sActiveHastes`,
    `Shrink.sCache` und `Shrink.sActiveCache` ausschließlich nach dem
    vorhandenen Initialisierungs-Guard.
  - Fehlerfall: aktive Einträge halten über `mOwner` Charaktere und deren alten
    Levelzustand fest; auch freie Poolobjekte bleiben ohne Nutzen prozessweit
    verwurzelt.
  - Kontrollverhalten: ein nicht initialisierter `PlayState` kehrt weiterhin
    zurück, ohne einen der vier Pools zu verändern.
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0 behalten alle vier Listen beim
    initialisierten Dispose; die manuelle Patch-Assembly 0.0.60 und alle
    Runtime-Patch-Profile leeren sie. Der Kontrollfall besteht überall.
  - Die statische Hash-Initialisierer-Darstellung ist Compilerrauschen. Die
    `RetentionRegistry`-Aufrufe folgen im Diagnostics-Block.
- [x] `chant-spell-level-cleanup`
  - Ziel: `PlayState.Dispose`
  - Technik: Prefix; validiert das exakte `ChantSpells[]`, das `Active`-Feld
    und `ChantSpells.Stop()`, und stoppt nur aktive Einträge im initialisierten
    Dispose-Pfad
  - Fehlerfall: der statische Manager behält einen aktiven Chant-Spell samt
    `Character Owner`, dynamischem Licht und Effektreferenz nach dem Levelabbau
  - Kontrollverhalten: ein nicht initialisierter `PlayState` lässt den Eintrag
    wie im Original unverändert
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0 behalten den aktiven Eintrag;
    manuelle Patch-Assembly 0.0.60 und alle Runtime-Patch-Profile rufen den
    vorhandenen `Stop()`-Pfad auf und geben den Eintrag frei
- [x] `static-level-pool-cleanup`
  - Ziel: `PlayState.Dispose`
  - Technik: ein Transpiler fügt nach dem vorhandenen Aufruf von
    `Entity.ClearHandles()` genau einen gemeinsamen Cleanup-Aufruf ein
  - Fehlerfall: 41 statische Collections in 35 Ability-, Spell-, SpellEffect-,
    Action- und leichten Entity-Klassen behalten gepoolte Instanzen des
    abgebauten Levels; `GiveOrder.sPlayState` hält den Eigentümer zusätzlich
    direkt
  - Verhalten: ausschließlich die 41 validierten Collections werden geleert;
    zusätzlich wird das exakt validierte `GiveOrder.sPlayState` auf null
    gesetzt. Elemente werden in diesem Patch weder freigegeben noch anderweitig
    verändert
  - Kontrollverhalten: ein nicht initialisierter `PlayState` erreicht den
    eingefügten Aufruf nicht und lässt alle Collections unverändert
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0 behalten alle Einträge. Die manuelle
    Patch-Assembly 0.0.60 und alle Runtime-Patch-Profile leeren alle 41
    Collections und lösen die `GiveOrder`-PlayState-Referenz.
- [x] `entity-update-character-marker-decode`
  - Ziele: `NetworkServer.Update` und `NetworkClient.Update`
  - Technik: zwei Transpiler rufen unmittelbar vor dem vorhandenen
    `ReadMessage` eine gemeinsame, nicht allokierende Prüfung des empfangenen
    `MemoryStream` auf.
  - Fehlerfall: Ein `EntityUpdate` trägt das Feature-Bit `Character` (`0x10`).
    Dieses Bit besitzt kein Payload-Format; der Originaldecoder wirft trotzdem
    eine `NotImplementedException` und liest nachfolgende Features nicht mehr.
  - Verhalten: Nur bei `PacketType.EntityUpdate` wird `0x10` vor dem
    unveränderten Decoder maskiert. Alle folgenden Payload-Felder werden normal
    gelesen. Das Original verwendet das Bit außerhalb von Read und Write nicht.
  - Kontrollfall: `EntityUpdate` ohne das Bit bleibt bytegenau unverändert.
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0 werfen für `Character` sowie
    `Character | Damageable`. Die manuelle Patch-Assembly 0.0.60 und alle
    Runtime-Patch-Profile lesen beide Pakete vollständig; der Kontrollfall
    besteht überall.
  - `EntityUpdateMessage.Read` selbst wird absichtlich nicht mit Harmony 1.2
    gepatcht: Schon ein unveränderter Harmony-Transpiler erzeugt für diese
    unsafe Struct-Methode unter CLR 2 ungültiges Programm-IL. Die äußere
    Receive-Loop-Lösung hält den Originaldecoder unverändert.
  - Die manuelle Diagnose `entity_update_character_feature` folgt zusammen mit
    dem Telemetrieblock.

- [x] `judgement-spray-condition-cache`
  - Ziel: `JudgementSpray.SpawnProjectile(...)`.
  - Technik: Transpiler; fügt unmittelbar vor genau dem einen Aufruf von
    `Queue<ConditionCollection>.Dequeue()` einen Runtime-Helfer ein. Der Helfer
    stellt nur bei leerem Pool eine neue Instanz ein; das ursprüngliche
    `Dequeue()` bleibt innerhalb seines vorhandenen Queue-Monitors erhalten.
  - Fehlerfall: Der gemeinsame `ProjectileSpell.sCachedConditions`-Pool ist
    leer. Das Original beendet den LogicThread mit einer
    `InvalidOperationException`.
  - Verhalten: Ein leerer Pool liefert eine neue `ConditionCollection`; ein
    gefüllter Pool liefert weiterhin exakt dieselbe zwischengespeicherte
    Instanz. Der unveränderte Rest von `SpawnProjectile` initialisiert das
    Projektil und stellt die Collection danach wieder in den Pool.
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0 scheitern im Fehlerfall. Die
    manuelle Patch-Assembly 0.0.60 und alle Runtime-Patch-Profile bestehen den
    Fehler- und den Identitäts-Kontrollfall.
  - Die manuelle Recovery-Telemetrie folgt mit dem gemeinsamen
    Telemetrieblock. Bis dahin ist die betroffene Quelldatei in der Checkliste
    bewusst nur teilweise migriert.

- [x] `blizzard-play-state-lifetime`
  - Ziele: beide öffentlichen `Blizzard.Execute(...)`-Überladungen, das private
    `Execute()`, `Update(...)` und `OnRemove()`.
  - Technik: vier eng geprüfte Transpiler entfernen die beiden Zuweisungen an
    `mPlayState` und ersetzen exakt drei Zugriffe im privaten `Execute()` sowie
    vier Zugriffe in `Update(...)` durch `PlayState.RecentPlayState`. Ein
    boolescher Prefix validiert die drei Referenzfelder, `mTTL`, den XNA-Cue-Typ
    und exakt `Cue.Stop(AudioStopOptions)` und ersetzt `OnRemove()` vollständig.
  - Fehlerfall: Das prozessweit lebende Blizzard-Singleton behält nach dem
    Entfernen die alte Szene, den Caster und den Ambience-Cue. Wirft das
    Stoppen des Cues, bleiben die Referenzen im Original ebenfalls erhalten.
  - Verhalten: Der Cue wird lokal gehalten, `mScene`, `mCaster` und
    `mAmbience` werden geleert und anschließend wird derselbe
    `AudioStopOptions.AsAuthored`-Aufruf ausgeführt. `mTTL` bleibt wie im
    Original auf null gesetzt.
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0 behalten die Referenzen im normalen
    und im absichtlich fehlschlagenden Cue-Pfad. Sie wählen außerdem die beim
    Aufruf übergebene statt der aktuellen Szene. Die manuelle Patch-Assembly
    0.0.60 und alle Runtime-Patch-Profile lösen die Referenzen und verwenden die
    aktuelle Szene. Der Kontrollfall ohne Referenzen und Cue besteht überall.

- [x] `weather-singleton-play-state-lifetime`
  - Ziele: beide `Execute(...)`-Überladungen, `Update(...)` und `OnRemove()`
    von `Rain` und `Thunderstorm`.
  - Technik: acht eng geprüfte Transpiler entfernen vier gespeicherte
    PlayState-Zuweisungen, ersetzen alle 17 verbleibenden Feldzugriffe durch
    `PlayState.RecentPlayState` und lösen den Thunderstorm-Owner direkt nach dem
    vorhandenen Cue-Stop. Ein Prefix bildet `Rain.OnRemove()` mit denselben
    Cue-, Effekt- und Lichtoperationen nach und löst anschließend Szene und
    Caster.
  - Fehlerfall: Beide prozessweit lebenden Wettersingletons können einen alten
    PlayState halten. `Thunderstorm` hält über sein dauerhaftes `mRain` außerdem
    dessen alte Szene. Spätere Update- und Remove-Aufrufe arbeiten dadurch auf
    einem bereits abgebauten Levelgraphen.
  - Verhalten: Laufende Wetterarbeit verwendet den aktuellen PlayState. Rain
    stellt die Lichtintensität weiterhin auf der zuvor verwendeten Szene wieder
    her und löst danach die per-cast Referenzen. Thunderstorm behält sein
    dauerhaftes Rain-Objekt, löst aber den Owner im vorhandenen aktiven
    Cue-Pfad. Timing, Netzwerkformat und Schadenslogik bleiben unverändert.
  - Acht Drei-Wege-Szenarien sind im Original 1.10.4.2 rot und in der manuellen
    Patch-Assembly sowie im Runtime-Patch grün. Dieselbe Runtime-Matrix besteht
    mit 1.4.16.0 und 1.5.1.0.

- [x] `animated-level-part-detached-entity-cleanup`
  - Ziel: `AnimatedLevelPart.Update(DataChannel, float, ref Matrix, GameScene)`.
  - Technik: Transpiler; validiert den exakten Methodenkontrakt, den
    `SortedList<ushort, float>`-Speicher, `Entity.GetFromHandle(int)`, den
    virtuellen Body-Getter und den vorhandenen `RemoveAt`-Block.
  - Fehlerfall: Ein Handle kann noch in `mCollidingEntities` stehen, obwohl
    die Entity nicht mehr auflösbar ist oder ihr Body bei der Deinitialisierung
    bereits gelöst wurde. Der Originalcode dereferenziert danach die Entity
    beziehungsweise den Body.
  - Verhalten: Entity und Body werden vor der Zeit- und Transformlogik genau
    einmal aufgelöst. Fehlt einer von beiden, wird der Eintrag entfernt und die
    Schleife fortgesetzt. Ein gültiger, regulär ablaufender Eintrag erhält wie
    bisher noch seinen letzten Plattform-Transform.
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0 scheitern an den beiden gelösten
    Zuständen. Die manuelle Patch-Assembly 0.0.60 und alle Runtime-Profile
    bestehen diese Fälle sowie den Kontrollfall mit gültigem, ablaufendem Body.
  - Die umfangreichere Dispose-Änderung der Klasse folgt separat. Deshalb
    bleibt die Datei in der Checkliste teilweise migriert.

- [x] `dynamic-light-cache-release`
  - Ziel: `DynamicLight.DisposeCache()`.
  - Technik: Transpiler; validiert die statische `Queue<DynamicLight>`, ihren
    parameterlosen `Clear()`-Aufruf sowie je genau einen Aufruf von
    `Dequeue`, `Enqueue`, `DisposeShadowMap`, `Monitor.Enter` und
    `Monitor.Exit`. Der Clear-Aufruf wird vor dem vorhandenen `leave` und damit
    noch innerhalb desselben Queue-Monitors eingefügt.
  - Fehlerfall: Das Original gibt die Shadow-Map jedes gecachten Lichts frei,
    stellt das Licht jedoch wieder in die prozessweite Queue. Die Queue hält
    damit weiterhin alle levelgebundenen Lichtobjekte.
  - Verhalten: Die vorhandene Shadow-Map-Freigabe läuft unverändert für jedes
    Licht. Anschließend wird die Queue wie im manuellen Patch geleert.
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0 behalten den Testeintrag. Die
    manuelle Patch-Assembly 0.0.60 und alle Runtime-Patch-Profile rufen dessen
    Shadow-Map-Freigabe genau einmal auf und leeren danach die Queue.
  - Die übrigen sichtbaren Unterschiede in `DynamicLight` sind ausschließlich
    Aufrufe der Retention-Diagnostik und ändern das Spielverhalten nicht.

- [x] `meteor-shower-lifetime-cleanup`
  - Ziele: beide öffentlichen `MeteorShower.Execute`-Überladungen, das private
    `Execute()`, `Update(DataChannel, float)` und `OnRemove()`.
  - Technik: Vier Transpiler entfernen jeweils genau die eine erwartete
    `mPlayState`-Zuweisung oder ersetzen genau einen Zugriff durch
    `PlayState.RecentPlayState`. Ein boolescher Prefix bildet die kleine
    `OnRemove`-Methode vollständig nach.
  - Fehlerfall: Das prozessweit lebende Singleton hält den PlayState, die Szene,
    den Besitzer und den Rumble-Cue des alten Levels. Wirft der Cue-Stopp, löst
    das Original keine dieser Referenzen.
  - Verhalten: Neue Ausführungen speichern den übergebenen PlayState nicht.
    Szene und Missile-Pool stammen aus dem aktuellen PlayState. Beim Entfernen
    werden alle alten Referenzen vor dem unveränderten Zurücksetzen der
    Lichtintensität und dem bedingten `Cue.Stop(AsAuthored)` gelöst.
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0 scheitern in den beiden
    PlayState-Szenarien sowie bei aktiver, bereits stoppender und absichtlich
    fehlschlagender Cue-Freigabe. Die manuelle Patch-Assembly 0.0.60 und alle
    Runtime-Patch-Profile bestehen alle fünf Szenarien.

- [x] `dialog-layout-line-breaks`
  - Ziele: `Message.Initialize()` und `SetDialogHint.Initialize()`.
  - Verhalten: Listenpunkte nach einem Dialog-Tag `[P=...]` erhalten vor dem
    Font-Wrapping wieder ihren Zeilenumbruch. Strukturierte Elementhinweise
    erhalten vor dem Auflösen ihrer Sprachreferenzen wieder Abschnitts- und
    Zeilenumbrüche.
  - Abgrenzung: `--`-Einschübe, bereits formatierte Texte und gewöhnliche
    Hinweise ohne alle drei Marker `#TYPE;`, `#PROP;` und `#OPP;` bleiben
    unverändert.
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0 verlieren beide Arten von
    Zeilenumbrüchen. Die manuelle Patch-Assembly 0.0.60 und alle
    Runtime-Patch-Profile bestehen die sechs Szenarien.

- [x] `shadow-blobs-scene-release`
  - Ziel: `PlayState.Dispose()` unmittelbar vor dem vorhandenen Leeren der
    geerbten `GameState.mScene`-Referenz.
  - Verhalten: `ShadowBlobs.mScene` wird nur dann geleert, wenn es noch genau
    die Szene enthält, die dieser PlayState abbaut. Eine bereits installierte
    Nachfolgeszene bleibt erhalten.
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0 behalten die alte Szene. Die
    manuelle Patch-Assembly 0.0.60 und alle Runtime-Patch-Profile lösen sie;
    alle Profile bestehen den Kontrollfall mit einer abweichenden Szene.

- [x] `player-controller-avatar-release`
  - Ziel: `Player.Avatar`-Setter vor dem Aktualisieren seiner `WeakReference`.
  - Verhalten: Wird der Player-Avatar geleert, entfernt der Prefix dieselbe
    Avatarinstanz aus `Controller.mAvatar`. Ein abweichender neuer
    Controller-Avatar und normale Nicht-null-Zuweisungen bleiben erhalten.
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0 leeren nur die schwache
    Player-Referenz. Die manuelle Patch-Assembly 0.0.60 und alle
    Runtime-Patch-Profile lösen auch die passende starke Controller-Referenz;
    alle Profile bestehen beide Kontrollfälle.

- [x] `player-text-box-level-release`
  - Ziel: `Player.DeinitializeGame()`.
  - Technik: Prefix; validiert das Player-TextBox-Feld sowie alle sechs
    zurückgesetzten Felder und räumt sie vor der ursprünglichen Methode auf.
  - Fehlerfall: Die wiederverwendete Obtained-Item-TextBox behält nach dem
    Levelabbau ihren Besitzer, ihre Szene und aktiven Anzeigezustand.
  - Verhalten: Besitzer und Szene werden gelöst; automatische Fortschaltung,
    TTL, Grow-Zustand und Skalierung werden zurückgesetzt. Die TextBox selbst
    bleibt für `InitializeGame` am Player erhalten.
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0 behalten den aktiven Zustand. Die
    manuelle Patch-Assembly 0.0.60 und alle Runtime-Patch-Profile räumen ihn
    auf. Eine bereits leere oder nicht vorhandene TextBox bleibt unverändert.

- [x] `player-notifier-level-release`
  - Ziel: `Player.DeinitializeGame()`.
  - Technik: eigener Prefix; validiert das Player-Notifier-Feld und die vier
    zurückgesetzten Felder unabhängig vom TextBox-Prefix.
  - Fehlerfall: Der wiederverwendete Notifier behält seinen Avatar oder eine
    angehängte TextBox nach dem Levelabbau und bleibt zugleich sichtbar.
  - Verhalten: `mOwner` und `mDialogAttach` werden gelöst, `mAlpha` und
    `mTargetAlpha` werden auf null gesetzt. Der Notifier und seine Grafikobjekte
    bleiben für das nächste Level erhalten.
  - Original 1.10.4.2, 1.4.16.0 und 1.5.1.0 behalten den aktiven Zustand. Die
    manuelle Patch-Assembly 0.0.60 und alle Runtime-Patch-Profile räumen ihn
    auf. Ein leerer oder nicht vorhandener Notifier bleibt unverändert.

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

| Magicka-Version | Runtime-Host | ActiveBuffs | Agent | AudioManager | Avatar | AIStateAttack | AIStateMove | BossHealthBar | ChargeAbilities | ChantSpells | StaticPools | JudgementSpray | Blizzard | AnimatedLevelPart | DynamicLight | ChillyBlast | CompanyState | ControlManager | DeflectionAura | DrainLife | DrinkBlood | EntityManager | EntityStateStorage | EntityUpdate | Flash | Helper | Interactable | InventoryBox | MagickCamera | HUDManager | Machine | Jormungandr | PackLicense | PlayState | PoisonSpray | Portal | RandomMine | SpawnSlime | SummonFlamer | SummonSpirit | SummonUndead | UndeadNetwork | SummonZombie | SummonCross | StarGaze | Starfall | SubMenuMain | VersusRuleset | AbilityTemplates | LoadingScreen | DirectInput | MenuImageText | ParadoxPopup | Language | DialogLayout | ShadowBlobs | PlayerAvatar | PlayerCleanup | MeteorShower | InGameMenu |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1.10.4.2 | erzeugt | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| 1.4.16.0 | erzeugt | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | NOT_APPLICABLE | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | NOT_APPLICABLE | PASS | PASS | PASS | NOT_APPLICABLE | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | NOT_APPLICABLE | NOT_APPLICABLE | NOT_APPLICABLE | PASS | PASS | PASS | PASS | NOT_APPLICABLE | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| 1.5.1.0 | erzeugt | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | NOT_APPLICABLE | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | NOT_APPLICABLE | PASS | PASS | PASS | NOT_APPLICABLE | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | PASS | NOT_APPLICABLE | NOT_APPLICABLE | NOT_APPLICABLE | PASS | PASS | PASS | PASS | NOT_APPLICABLE | PASS | PASS | PASS | PASS | PASS | PASS | PASS |

`NOT_APPLICABLE` bedeutet hier nicht „ungeprüft“. Die alten Assemblies
enthalten weder die spätere `HUDManager`-Klasse noch `WorldSyncMessage` und
`PlayState.AddWorldSyncMessage`. Die Patchpläne protokollieren dies und fahren
mit allen anderen Patchgruppen fort.

## Datei-Checkliste der manuellen Patch-Assembly

Grundlage ist der kommentarbereinigte ILSpy-C#-Vergleich zwischen den oben
genannten 1.10.4.2-Hashes. Er enthält 220 unterschiedliche C#-Dateien. Die
Eingaben und Abhängigkeiten werden vor ILSpy isoliert bereitgestellt, damit der
Ablageort einer EXE die Auflösung von Typen und damit die Inventur nicht ändert.

Aktueller Stand: 72 Dateien vollständig, 58 Dateien teilweise und 90 Dateien noch
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

- [x] `Magicka/GameLogic/GameStates/InGameMenus/InGameMenuTimedObjectiveStatistics.cs` — VOLLSTÄNDIG: alle neun Reads verwenden den aktuellen PlayState; der übrige statische Initialisierer-Diff ist semantikfreies Compilerrauschen.
- [ ] `Magicka/CommunityPatch/TelemetryRuntimeContext.cs`
- [ ] `Magicka/GameLogic/Entities/SpellMine.cs` — TEILWEISE: die statische Poolfreigabe bei Levelende ist migriert; der übrige manuelle Diff ist in diesem Block nicht abgedeckt.
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SummonPhoenix.cs`
- [x] `Magicka/CommunityPatch/DialogLayoutCompatibility.cs` — VOLLSTÄNDIG: beide reinen Formatierungshelfer sind im Runtime-Patcher enthalten und durch sechs Drei-Wege-Szenarien abgedeckt.
- [ ] `Magicka/GameLogic/Entities/Bosses/Vlad.cs`
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Thunderstorm.cs` — VOLLSTÄNDIG: beide gespeicherten PlayState-Zuweisungen, elf laufende PlayState-Zugriffe und die per-cast Owner-Freigabe; 4 Transpiler und 4 Drei-Wege-Szenarien.
- [x] `Magicka/GameLogic/GameStates/InGameMenus/InGameMenuVersusStatistics.cs` — VOLLSTÄNDIG: alle acht Reads verwenden den aktuellen PlayState; der übrige statische Initialisierer-Diff ist semantikfreies Compilerrauschen.
- [ ] `Magicka/Levels/Triggers/TriggerArea.cs`
- [ ] `Magicka/GameLogic/GameStates/InGameMenus/InGameMenu.cs` — TEILWEISE: die statische PlayState-Zuweisung ist entfernt, alle vier Reads verwenden den aktuellen Zustand und der Stack wird beim Levelabbau geleert; nur das Safe-Area-Layout ist noch offen.
- [ ] `Magicka/CommunityPatch/NetworkEntityHandleGuard.cs` — TEILWEISE: nur die für `AddWorldSyncMessage` benötigte SpawnNPC-Entscheidung, ohne Übernahme der übrigen manuellen Hilfsklasse.
- [ ] `Magicka/GameLogic/Spells/ArcaneBlast.cs` — TEILWEISE: die statische Poolfreigabe bei Levelende ist migriert; der übrige manuelle Diff ist in diesem Block nicht abgedeckt.
- [ ] `Magicka/CoreFramework/GameSystem/Store/StoreItemDatabase.cs`
- [ ] `Magicka/GameLogic/UI/IconRenderer.cs`
- [ ] `Magicka/GameLogic/Entities/PhysicsEntity.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Conflagration.cs` — TEILWEISE: die statische Poolfreigabe bei Levelende ist migriert; der übrige manuelle Diff ist in diesem Block nicht abgedeckt.
- [ ] `Magicka/GameLogic/UI/Credits.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Wave.cs` — TEILWEISE: die statische Poolfreigabe bei Levelende ist migriert; der übrige manuelle Diff ist in diesem Block nicht abgedeckt.
- [ ] `Magicka/AI/Agent.cs` — TEILWEISE: Body-Guard in `ChooseTarget`, Transpiler und 2 Drei-Wege-Szenarien; Initialisierungs-, Cleanup- und Dispose-Änderungen sind noch offen.
- [ ] `Magicka/Levels/Campaign/LevelManager.cs`
- [ ] `Magicka/StaticWeakList.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Napalm.cs`
- [ ] `Magicka/GameLogic/Spells/ArcaneBlade.cs` — TEILWEISE: die statische Poolfreigabe bei Levelende ist migriert; der übrige manuelle Diff ist in diesem Block nicht abgedeckt.
- [ ] `Magicka/GameLogic/Entities/Shield.cs` — TEILWEISE: die statische Poolfreigabe bei Levelende ist migriert; der übrige manuelle Diff ist in diesem Block nicht abgedeckt.
- [ ] `Magicka/GameLogic/Spells/SpellEffects/RailGunSpell.cs` — TEILWEISE: die statische Poolfreigabe bei Levelende ist migriert; der übrige manuelle Diff ist in diesem Block nicht abgedeckt.
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Polymorph.cs` — TEILWEISE: die statische Poolfreigabe bei Levelende ist migriert; der übrige manuelle Diff ist in diesem Block nicht abgedeckt.
- [ ] `Magicka/GameLogic/UI/KeyboardHUD.cs` — TEILWEISE: der Safe-Area-Einzug in `RenderData.DrawIcon` ist migriert; die Hybrid-Input-Darstellung und Label-Aktualisierung sind noch offen.
- [ ] `Magicka/CommunityPatch/MouseInputCompatibility.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/GreaseLump.cs` — TEILWEISE: die statische Poolfreigabe bei Levelende ist migriert; der übrige manuelle Diff ist in diesem Block nicht abgedeckt.
- [ ] `Magicka/StaticList.cs`
- [ ] `Magicka/GameLogic/Controls/KeyboardMouseController.cs`
- [x] `Magicka/GameLogic/GameStates/InGameMenus/InGameMenuSurvivalStatistics.cs` — VOLLSTÄNDIG: alle acht Reads verwenden den aktuellen PlayState; die lokale Cast-Darstellung und der statische Initialisierer-Diff sind semantikfreies Compilerrauschen.
- [ ] `Magicka/GameLogic/Entities/Items/BookOfMagick.cs`
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Rain.cs` — VOLLSTÄNDIG: beide gespeicherten PlayState-Zuweisungen, sechs laufende PlayState-Zugriffe und die Szenen-/Caster-Freigabe; 4 Transpiler, 1 Prefix und 4 Drei-Wege-Szenarien.
- [ ] `Magicka/GameLogic/Spells/SpellEffects/PushSpell.cs` — TEILWEISE: die statische Poolfreigabe bei Levelende ist migriert; der übrige manuelle Diff ist in diesem Block nicht abgedeckt.
- [ ] `Magicka/GameLogic/Spells/SpellEffects/ShieldSpell.cs` — TEILWEISE: die statische Poolfreigabe bei Levelende ist migriert; der übrige manuelle Diff ist in diesem Block nicht abgedeckt.
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/VortexEntity.cs` — TEILWEISE: die statische Poolfreigabe bei Levelende ist migriert; der übrige manuelle Diff ist in diesem Block nicht abgedeckt.
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SpawnSlime.cs` — VOLLSTÄNDIG: gespeicherter PlayState in `Execute` und beide veralteten NavMesh-Zugriffe, 3 Transpiler und 3 Drei-Wege-Szenarien; der leere `DisposeCache()` und die statischen Hash-Initialisierer ändern kein Laufzeitverhalten.
- [ ] `Magicka/GameLogic/Entities/Entanglement.cs`
- [ ] `Magicka/Levels/Level.cs`
- [ ] `Magicka/Graphics/Effects/RadialBlur.cs`
- [ ] `Magicka/Levels/Lava.cs`
- [x] `Magicka/GameLogic/Spells/SpellEffects/SpellEffect.cs` — VOLLSTÄNDIG: die globale PlayState-Zuweisung entfällt und die statische Poolfreigabe bei Levelende ist migriert; ein Transpiler und vier gemeinsame Drei-Wege-Szenarien. Die Darstellung der statischen Initialisierer ist semantikfreies Compilerrauschen.
- [ ] `Magicka/CommunityPatch/WarlordAbilityDiagnostic.cs`
- [ ] `Magicka/CommunityPatch/CollisionCallbackCleanup.cs`
- [ ] `Magicka/GameLogic/Entities/Bosses/GenericBoss.cs`
- [ ] `Magicka/Levels/Water.cs`
- [ ] `Magicka/GameLogic/GameStates/InGameMenus/InGameMenuOptionsResolution.cs` — TEILWEISE: der Kamera-Aspektzugriff verwendet den aktuellen PlayState; die Safe-Area-Auswahl bleibt offen.
- [x] `Magicka/Graphics/TutorialManager.cs` — VOLLSTÄNDIG: Safe-Area-Position sowie vollständige PlayState-Lebensdaueränderung in `Initialize`, `UpdateResolution` und `Update`; die verschobene statische Initialisierung ist semantikfreies Compilerrauschen.
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Thunderbolt.cs`
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Blizzard.cs` —
  VOLLSTÄNDIG: beide gespeicherten PlayState-Zuweisungen, drei Zugriffe im
  privaten Startpfad, vier Update-Zugriffe und die ausfallsichere
  `OnRemove`-Bereinigung; 4 Transpiler, 1 Prefix und 6 Drei-Wege-Szenarien.
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SummonZombie.cs` — VOLLSTÄNDIG: beide gespeicherten PlayState-Zuweisungen, zwei Start- und vier Update-Zugriffe sowie Pool- und Template-Freigabe; 4 Transpiler und 4 eigene Drei-Wege-Szenarien. RetentionRegistry-Aufrufe folgen gesammelt im Diagnostics-Block, die statischen Initialisierer sind Compilerrauschen.
- [ ] `Magicka/Game.cs` — TEILWEISE: `EndRun` gibt die Paradox-Kontodaten nach dem Dienstabbau frei; die weiteren manuellen Änderungen der Klasse sind noch offen.
- [ ] `Magicka/CommunityPatch/PayloadContract.cs`
- [ ] `Magicka/CommunityPatch/AnimationClipCompatibility.cs`
- [ ] `Magicka/CommunityPatch/PatchSettings.cs`
- [ ] `Magicka/GameLogic/Entities/Items/Item.cs`
- [ ] `Magicka/SharedContentManager.cs`
- [ ] `Magicka/GameLogic/UI/Tome.cs`
- [ ] `Magicka/GameLogic/Entities/Avatar.cs` — TEILWEISE: `FindInteractable`, Prefix und 5 Drei-Wege-Szenarien; die übrigen manuellen Änderungen dieser großen Klasse sind noch offen.
- [ ] `Magicka/GameLogic/GameStates/PlayState.cs` — TEILWEISE: `AddWorldSyncMessage`, das bedingte Lösen der ShadowBlobs-Szene sowie die dokumentierten levelgebundenen Cleanup-Injektionen sind migriert; weitere Dispose-, Übergangs- und Diagnoseänderungen sind noch offen.
- [ ] `Magicka/GameLogic/Spells/Magick.cs` — TEILWEISE: die statische Poolfreigabe bei Levelende ist migriert; der übrige manuelle Diff ist in diesem Block nicht abgedeckt.
- [ ] `Magicka/GameLogic/Spells/Railgun.cs` — TEILWEISE: die statische Poolfreigabe bei Levelende ist migriert; der übrige manuelle Diff ist in diesem Block nicht abgedeckt.
- [ ] `Magicka/Levels/Triggers/Trigger.cs` — TEILWEISE: `SpawnNPC` übernimmt den über das unveränderte Paketformat transportierten Undead-Zustand; weitere Netzwerk-, Dispose- und Diagnoseänderungen dieser Klasse sind noch offen.
- [ ] `Magicka/GameLogic/Entities/Gib.cs`
- [ ] `Magicka/GameLogic/Entities/MissileEntity.cs`
- [ ] `Magicka/GameLogic/Controls/XInputController.cs`
- [ ] `Magicka/Levels/GameScene.cs`
- [ ] `Magicka/CommunityPatch/PatchTelemetry.cs`
- [ ] `Magicka/GameLogic/Entities/Character.cs`
- [ ] `Magicka/GameLogic/Spells/SpellEffects/ProjectileSpell.cs` — TEILWEISE: die statische Poolfreigabe bei Levelende ist migriert; der übrige manuelle Diff ist in diesem Block nicht abgedeckt.
- [ ] `Magicka/GameLogic/GameStates/Menu/Main/SubMenuCharacterSelect.cs` — TEILWEISE: die vier Pack-Anzeigeprüfungen verwenden die gemeinsame Custom-Content-Lizenzregel; die weiteren manuellen Änderungen der Klasse sind noch offen.
- [ ] `Magicka/Network/NetworkClient.cs`
- [ ] `Magicka/Network/NetworkServer.cs`
- [ ] `Magicka/CommunityPatch/HybridInputSupport.cs`
- [ ] `Magicka/CommunityPatch/OriginalBackupAudit.cs`
- [ ] `Magicka/CommunityPatch/Magicka2ControllerSupport.cs`
- [ ] `Magicka/GameLogic/Entities/CharacterTemplate.cs`
- [ ] `Magicka/CommunityPatch/PatchUpdateManager.cs`
- [ ] `Magicka/CommunityPatch/NetworkLifecycleCompatibility.cs`
- [ ] `Magicka/GameLogic/GameStates/Menu/Main/SubMenuCutscene.cs`
- [ ] `Magicka/CommunityPatch/RuntimeCompatibilityGuards.cs` — TEILWEISE: die DirectInput-Ausfallerkennung und verzögerte Warnung sind migriert; Steam-API-, Store-, Versionszeilen- und Unterstützerdialog-Helfer sind noch offen.
- [ ] `Magicka/GameLogic/Entities/Barrier.cs`
- [ ] `Magicka/GameLogic/Spells/SpellEffects/SpraySpell.cs` — TEILWEISE: die statische Poolfreigabe bei Levelende ist migriert; der übrige manuelle Diff ist in diesem Block nicht abgedeckt.
- [ ] `Magicka/Levels/LevelModel.cs`
- [ ] `Magicka/GameLogic/Entities/PhysicsEntityTemplate.cs`
- [ ] `Magicka/CommunityPatch/CommunityPatchInfo.cs`
- [ ] `Magicka/Physics/PhysicsManager.cs`
- [x] `Magicka/GameLogic/GameStates/InGameMenus/InGameMenuMagicks.cs` — VOLLSTÄNDIG: beide GameType-Reads verwenden den aktuellen PlayState und `LanguageChanged` validiert den markierten Index. Die lokale Variable im Namenpfad, die tote `num2 = 28`-Zuweisung und der statische Initialisierer-Diff ändern kein Verhalten.
- [ ] `Magicka/GameLogic/Entities/Entity.cs`
- [ ] `Magicka/Levels/ForceField.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Revive.cs` — TEILWEISE: die statische Poolfreigabe bei Levelende ist migriert; der übrige manuelle Diff ist in diesem Block nicht abgedeckt.
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Portal.cs` — TEILWEISE: ungültige Einträge in `PortalEntity.mTeleportQueue`, Transpiler und 3 Drei-Wege-Szenarien; weitere manuelle Änderungen sind noch offen.
- [ ] `Magicka/GameLogic/Entities/ElementalEgg.cs`
- [ ] `Magicka/GameLogic/Entities/Fairy.cs`
- [ ] `Magicka/GameLogic/GameStates/InGameMenus/InGameMenuOptionsControls.cs`
- [ ] `Magicka/GameLogic/Entities/DamageablePhysicsEntity.cs`
- [ ] `Magicka/Levels/AnimatedLevelPart.cs` — TEILWEISE: `Update` entfernt
  Einträge ohne auflösbare Entity oder ohne Body, ein Transpiler und 3
  Drei-Wege-Szenarien. Die Dispose-Änderungen sind noch offen.
- [ ] `Magicka/GameLogic/Entities/Bosses/BossFight.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Grease.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SummonDeath.cs`
- [ ] `Magicka/CommunityPatch/InGameUiCompatibility.cs`
- [x] `Magicka/CommunityPatch/WidescreenSafeArea.cs` — VOLLSTÄNDIG: beide Berechnungen liegen CLR-2-kompatibel im Runtime-Patcher und werden durch 4 Rechenszenarien abgedeckt.
- [ ] `Magicka/GameLogic/Entities/NonPlayerCharacter.cs`
- [ ] `Magicka/Graphics/TypingText.cs`
- [ ] `Magicka/Program.cs`
- [ ] `Magicka/GameLogic/Entities/AnimatedPhysicsEntity.cs`
- [ ] `Magicka/GameLogic/Spells/UnderGroundAttack.cs` — TEILWEISE: die statische Poolfreigabe bei Levelende ist migriert; der übrige manuelle Diff ist in diesem Block nicht abgedeckt.
- [ ] `Magicka/CommunityPatch/NetworkGuardTelemetryBackoff.cs`
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/HealingRain.cs` — VOLLSTÄNDIG: aktuelle Zustandsauflösung und abschließende Szene-/Caster-Freigabe mit 4 Transpilern, 1 Prefix und 5 Drei-Wege-Szenarien.
- [x] `Magicka/GameLogic/GameStates/Menu/MenuImageTextItem.cs` — VOLLSTÄNDIG:
  Font-Zeilenhöhe und literale Textvertices werden bei einem Sprachwechsel
  aktualisiert, Transpiler und 3 Drei-Wege-Szenarien.
- [x] `Magicka/GameLogic/GameStates/CompanyState.cs` — VOLLSTÄNDIG: Content-Dispose nach Controller- und Tome-Cleanup, Transpiler und ein Drei-Wege-Reihenfolgetest; die statische Initialisierer-Umschreibung ist semantikfreies Compilerrauschen.
- [ ] `Magicka/Localization/LanguageManager.cs` — TEILWEISE: Anzeigename und
  Konfigurationsaliase für Simplified Chinese sind durch zwei Prefixe und drei
  Drei-Wege-Szenarien migriert; das Aufzeichnen der aktuellen Sprache bleibt
  Teil des späteren Telemetrieblocks.
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/RandomMine.cs` — VOLLSTÄNDIG: ungenutzte PlayState-Referenz im Singleton, Transpiler und 3 Drei-Wege-Szenarien; die sichtbare statische Initialisierer-Umschreibung ist semantikfreies Compilerrauschen.
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/DrainLife.cs` — VOLLSTÄNDIG: ungenutzte PlayState-Referenz im erfolgreichen Effektpfad, Transpiler und 2 Drei-Wege-Szenarien; die statische Initialisierer-Umschreibung ist semantikfreies Compilerrauschen.
- [ ] `Magicka/Levels/Liquid.cs`
- [x] `Magicka/Levels/Triggers/Actions/SetDialogHint.cs` — VOLLSTÄNDIG: strukturierte Elementhinweise erhalten vor `ParseReferences` ihre Zeilenumbrüche, Transpiler und 3 Drei-Wege-Szenarien.
- [x] `Magicka/GameLogic/Controls/Controller.cs` — VOLLSTÄNDIG: bedingtes Lösen des passenden Avatars durch den `Player.Avatar`-Prefix und 3 Drei-Wege-Szenarien.
- [x] `Magicka/GameLogic/Entities/EntityStateStorage.cs` — VOLLSTÄNDIG: PlayState-Lebensdauer in Konstruktor und `Restore`, 2 Runtime-Patches und 3 Drei-Wege-Szenarien.
- [x] `Magicka/WebTools/Paradox/ParadoxPopupUtils.cs` — VOLLSTÄNDIG: einfache
  wiederverwendete Popups löschen alten Zusatztext, Transpiler und 2
  Drei-Wege-Szenarien. Die manuelle Sichtbarkeitserweiterung ermöglicht nur
  den Aufruf aus dem injizierten Helper; der Runtime-Patcher löst dieselbe
  private Methode per Reflection auf.
- [x] `Magicka/GameLogic/UI/ShadowBlobs.cs` — VOLLSTÄNDIG: die beim Levelabbau bedingt gelöste Szenenreferenz, ein Transpiler und 2 Drei-Wege-Szenarien.
- [ ] `Magicka/GameLogic/Entities/AnimationClipAction.cs`
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SpawnSlimeOverkill.cs` — VOLLSTÄNDIG: gespeicherter PlayState in `Execute`, Transpiler und ein Drei-Wege-Szenario; die statische Initialisierer-Darstellung ist semantikfreies Compilerrauschen.
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/DeflectionAura.cs`
  — VOLLSTÄNDIG: ungenutzte PlayState-Referenz in `Execute`, Transpiler und 2
  Drei-Wege-Szenarien; die statische Hash-Initialisierer-Umschreibung ist
  semantikfreies Compilerrauschen.
- [ ] `Magicka/Graphics/NotifierButton.cs` — TEILWEISE: die Freigabe von
  Besitzer, angehängter TextBox und Alpha-Zustand ist durch einen Prefix und 3
  Drei-Wege-Szenarien migriert; die Ultrawide-Zeichenänderung bleibt offen.
- [ ] `Magicka/GameLogic/Statistics/StatisticsManager.cs`
- [ ] `Magicka/Graphics/TextBox.cs` — TEILWEISE: die Freigabe ihrer
  levelgebundenen Besitzer- und Szenenreferenzen ist durch einen Prefix und 3
  Drei-Wege-Szenarien migriert; die Ultrawide-Zeichenänderung bleibt offen.
- [x] `Magicka/Levels/Triggers/Actions/AssignItem.cs` — VOLLSTÄNDIG:
  ausschließlich semantikfreie Darstellung derselben drei statischen
  Hash-Initialisierungen in einem expliziten Typinitialisierer; Reihenfolge,
  aufgerufene Methode und zugewiesene Werte sind im IL identisch.
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/JudgementSpray.cs`
  — TEILWEISE: die Recovery für einen leeren ConditionCollection-Pool ist mit
  einem Transpiler und 2 Drei-Wege-Szenarien migriert; die begrenzte
  Recovery-Telemetrie folgt mit dem gemeinsamen Telemetrieblock.
- [x] `Magicka/GameLogic/Spells/IceSpikes.cs` — VOLLSTÄNDIG: statische Poolfreigabe bei Levelende; das Verschieben der unveränderten `Random`-Initialisierung in den explizit dargestellten Typinitialisierer ist semantikfreies Decompilerrauschen.
- [ ] `Magicka/GameLogic/GameStates/InGameMenus/InGameMenuOptions.cs`
- [x] `Magicka/GameLogic/GameStates/Menu/Main/SubMenuMain.cs` — VOLLSTÄNDIG: Gamepad-B öffnet die vorhandene Beenden-Bestätigung, Keyboard/Maus behält den Cursorpfad; Prefix und 2 Drei-Wege-Szenarien. Die leere manuelle Markermethode hat kein Laufzeitverhalten und wird nicht übernommen.
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Starfall.cs` — VOLLSTÄNDIG: statische PlayState-Retention und veraltete Update-Zugriffe, 2 Transpiler und 3 Drei-Wege-Szenarien; lokale Variablennamen sind nicht Teil des Runtime-Patches.
- [x] `Magicka/GameLogic/Entities/ChantSpellManager.cs` — VOLLSTÄNDIG: aktive
  Chant-Spells werden im initialisierten Levelabbau über ihren vorhandenen
  `Stop()`-Pfad entfernt, Prefix und 2 Drei-Wege-Szenarien.
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Zap.cs` — VOLLSTÄNDIG: statische Poolfreigabe bei Levelende; die explizite Darstellung der unveränderten `SOUND`-Initialisierung ist semantikfreies Decompilerrauschen.
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/VladZap.cs` — VOLLSTÄNDIG: statische Poolfreigabe bei Levelende; die explizite Darstellung der unveränderten `SOUND`-Initialisierung ist semantikfreies Decompilerrauschen.
- [ ] `Magicka/Network/EntityUpdateMessage.cs` — TEILWEISE: das payloadlose
  `Character`-Feature wird vor dem Originaldecoder maskiert, zwei Transpiler und
  3 Drei-Wege-Szenarien; die Diagnose `entity_update_character_feature` folgt
  im Telemetrieblock.
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
- [x] `Magicka/GameLogic/GameStates/LoadingScreen.cs` — VOLLSTÄNDIG: gespeicherten Depth-Stencil-Buffer vor dem Clear wiederherstellen, Transpiler und 2 Drei-Wege-Szenarien; die lokale `DirectoryInfo`-Umschreibung ist semantikfreies Compilerrauschen.
- [ ] `Magicka/GameLogic/Entities/Items/Attachment.cs`
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SummonElemental.cs` — VOLLSTÄNDIG: statischer levelgeladener Template-Cache, bestehender PlayState-Cleanup-Transpiler und 2 gemeinsame Drei-Wege-Szenarien.
- [ ] `Magicka/Graphics/CutsceneText.cs`
- [x] `Magicka/GameLogic/UI/BossHealthBar.cs` — VOLLSTÄNDIG: Konstruktor sowie `Scene`-Getter und -Setter, 3 Runtime-Patches und 3 Drei-Wege-Szenarien.
- [x] `Magicka/GameLogic/GameStates/Menu/Main/Options/SubMenuOptionsControls.cs` — VOLLSTÄNDIG: beide DirectInput-gefährdeten Controllerlisten-Aufrufe, 2 Transpiler und gemeinsame Fünf-Wege-Szenarien.
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SummonBug.cs` — VOLLSTÄNDIG: statischer levelgeladener Template-Cache, bestehender PlayState-Cleanup-Transpiler und 2 gemeinsame Drei-Wege-Szenarien.
- [x] `Magicka/Levels/Versus/VersusRuleset.cs` — VOLLSTÄNDIG: fehlender Avatar beim Wiederbeleben, Transpiler und 3 Drei-Wege-Szenarien.
- [x] `Magicka/AI/AgentStates/AIStateMove.cs` — VOLLSTÄNDIG: Body-Guards in `OnEnter` und `OnExecute`, 2 Transpiler und 4 Drei-Wege-Szenarien.
- [x] `Magicka/Levels/Packs/MagickPack.cs` — VOLLSTÄNDIG: Custom-Lizenz in beiden Settern, 2 Transpiler und gemeinsame Pack-Szenarien.
- [x] `Magicka/Levels/Packs/ItemPack.cs` — VOLLSTÄNDIG: Custom-Lizenz in beiden Settern, 2 Transpiler und gemeinsame Pack-Szenarien.
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/OtherworldlyDischarge.cs` — VOLLSTÄNDIG: statischer levelgeladener Template-Cache, bestehender PlayState-Cleanup-Transpiler und 2 gemeinsame Drei-Wege-Szenarien.
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/MutateBeastman.cs` — VOLLSTÄNDIG: statischer levelgeladener Template-Cache, bestehender PlayState-Cleanup-Transpiler und 2 gemeinsame Drei-Wege-Szenarien.
- [ ] `Properties/AssemblyInfo.cs`
- [x] `Magicka/Audio/AudioManager.cs` — VOLLSTÄNDIG: `StopAll` überspringt
  bereits freigegebene Cues, Transpiler und 2 Drei-Wege-Szenarien; die
  statische String-Initialisierer-Umschreibung ist semantikfreies
  Compilerrauschen.
- [x] `Magicka/GameLogic/UI/Message.cs` — VOLLSTÄNDIG: Dialoglisten erhalten vor `BitmapFont.Wrap` ihre Zeilenumbrüche, Transpiler und 3 Drei-Wege-Szenarien.
- [ ] `Magicka/GameLogic/Spells/IceBlade.cs` — TEILWEISE: die statische Poolfreigabe bei Levelende ist migriert; der übrige manuelle Diff ist in diesem Block nicht abgedeckt.
- [ ] `Magicka/GameLogic/GameStates/InGameMenus/InGameMenuOptionsGraphics.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Grow.cs` — TEILWEISE: die statische Poolfreigabe bei Levelende ist migriert; der übrige manuelle Diff ist in diesem Block nicht abgedeckt.
- [ ] `Magicka/Levels/Triggers/Actions/GiveOrder.cs` — TEILWEISE: der
  Kahn-Kill-Plane-Fallback ist mit einem Transpiler und 4 Drei-Wege-Szenarien
  migriert; die statische Action-Liste und ihre PlayState-Referenz werden beim
  Levelabbau freigegeben. Nur die Recovery-Telemetrie bleibt offen.
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/PerformanceEnchantment.cs` — TEILWEISE: die statische Poolfreigabe bei Levelende ist migriert; der übrige manuelle Diff ist in diesem Block nicht abgedeckt.
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/EarthQuake.cs` — VOLLSTÄNDIG: Aktivierungs-PlayState-Freigabe sowie aktuelle Szene, Kamera und EntityManager mit 3 Transpilern und 3 Drei-Wege-Szenarien.
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/BreakBarriers.cs` — TEILWEISE: PlayState-Lebensdauer und statische Poolfreigabe sind migriert; die RetentionRegistry-Diagnostik ist noch offen.
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/ArrowRain.cs` — VOLLSTÄNDIG: PlayState- und Szenenfreigabe sowie aktuelle Missile-, Blitz-, Kamera- und Entfernungspfade mit 6 Transpilern und 4 Drei-Wege-Szenarien.
- [ ] `Magicka/GameLogic/Entities/SprayEntity.cs` — TEILWEISE: die statische Poolfreigabe bei Levelende ist migriert; der übrige manuelle Diff ist in diesem Block nicht abgedeckt.
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/ConfuseWho.cs`
- [ ] `Magicka/GameLogic/UI/DialogManager.cs`
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Confuse.cs` — TEILWEISE: die statische Poolfreigabe bei Levelende ist migriert; der übrige manuelle Diff ist in diesem Block nicht abgedeckt.
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/TornadoEntity.cs` — TEILWEISE: die statische Poolfreigabe bei Levelende ist migriert; der übrige manuelle Diff ist in diesem Block nicht abgedeckt.
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/FloorStomp.cs` — TEILWEISE: die statische Poolfreigabe bei Levelende ist migriert; der übrige manuelle Diff ist in diesem Block nicht abgedeckt.
- [ ] `Magicka/Graphics/MagickCamera.cs` — TEILWEISE: körperloses `FollowEntity`-Ziel, Prefix und 3 Drei-Wege-Szenarien; weitere Lifetime- und Dispose-Änderungen sind noch offen.
- [ ] `Magicka/GameLogic/UI/SpellWheel.cs` — TEILWEISE: PlayState-Lebensdauer und aktueller Szenenempfänger sind mit 2 Transpilern und 2 Drei-Wege-Szenarien migriert; die UI-Skalierung im Renderpfad bleibt offen.
- [ ] `Magicka/GameLogic/Entities/EntityManager.cs` — TEILWEISE: `GetClosestIDamageable`, das vierparametrige `GetEntities` und `ClearAndStore` mit 8 Drei-Wege-Szenarien; Konstruktor- und weitere Diagnoseänderungen sind noch offen.
- [ ] `Magicka/GameLogic/Entities/TeslaField.cs` — TEILWEISE: Konstruktoren der statischen Poolobjekte speichern den ungenutzten PlayState nicht mehr und die Poolfreigabe bei Levelende ist migriert; die RetentionRegistry-Diagnostik ist noch offen.
- [ ] `Magicka/GameLogic/Spells/LightningBolt.cs`
- [ ] `Magicka/Levels/Triggers/Actions/Action.cs`
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/TimeWarpStaff.cs` — VOLLSTÄNDIG: gespeicherte PlayState-Zuweisung und laufende Zugriffe in `Execute`, `Update` und `OnRemove`, 3 Transpiler und 3 Drei-Wege-Szenarien; die statische Initialisiererdarstellung ist semantikfreies Compilerrauschen.
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/TimeWarp.cs` — VOLLSTÄNDIG: beide gespeicherten PlayState-Zuweisungen und laufende Zugriffe in beiden `Execute`-Überladungen, `Update` und `OnRemove`, 4 Transpiler und 3 Drei-Wege-Szenarien; die statische Initialisiererdarstellung ist semantikfreies Compilerrauschen.
- [x] `Magicka/GameLogic/GameStates/InGameMenus/InGameMenuMain.cs` — VOLLSTÄNDIG: alle elf aktuellen Reads verwenden den aktuellen PlayState; 1.4 und 1.5 erhalten zusätzlich die zwei historischen Draw-Reads. Der statische Initialisierer-Diff ist semantikfreies Compilerrauschen.
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/MeteorShower.cs` — VOLLSTÄNDIG: beide gespeicherten PlayState-Zuweisungen, beide laufenden PlayState-Zugriffe und die Singleton-Freigabe in `OnRemove`, 4 Transpiler, ein Prefix und 5 Drei-Wege-Szenarien; die Darstellung der statischen Initialisierer ist semantikfreies Compilerrauschen.
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/GreaseTrail.cs` — TEILWEISE: PlayState-Lebensdauer, beide aktuellen Spawnzugriffe und statische Poolfreigabe sind migriert; die RetentionRegistry-Diagnostik ist noch offen.
- [ ] `Magicka/GameLogic/Entities/Dispenser.cs` — TEILWEISE: die statische Poolfreigabe bei Levelende ist migriert; der übrige manuelle Diff ist in diesem Block nicht abgedeckt.
- [x] `Magicka/Helper.cs` — VOLLSTÄNDIG: `ArrayEquals`, Prefix und 5 Drei-Wege-Szenarien.
- [x] `Magicka/Graphics/Lights/DynamicLight.cs` — VOLLSTÄNDIG: Die statische
  Licht-Queue wird nach der vorhandenen Shadow-Map-Freigabe geleert, ein
  Transpiler und ein Drei-Wege-Szenario. Alle übrigen manuellen Unterschiede
  sind Retention-Diagnostik ohne Änderung des Spielverhaltens.
- [x] `Magicka/Graphics/Flash.cs` — VOLLSTÄNDIG: gespeicherte Szenenreferenz in
  `Execute` und veralteter Szenenzugriff in `Update`, 2 Transpiler und 2
  Drei-Wege-Szenarien; der leere `IDisposable`-Wrapper und die Darstellung des
  statischen Lock-Initialisierers ändern kein Laufzeitverhalten.
- [x] `Magicka/GameLogic/UI/GenericHealthBar.cs` — VOLLSTÄNDIG: Die Renderübergabe verwendet nach Szenenwechseln den aktuellen PlayState; ein Transpiler und ein Drei-Wege-Szenario. Die Darstellung der statischen Initialisierer ist semantikfreies Compilerrauschen.
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/WaveEntity.cs` — TEILWEISE: die statische Poolfreigabe bei Levelende ist migriert; der übrige manuelle Diff ist in diesem Block nicht abgedeckt.
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/EtherealClone.cs` — VOLLSTÄNDIG: gespeicherte PlayState-Zuweisung entfernt und NavMesh-Zugriff auf `RecentPlayState` migriert; der leere manuelle Cleanup und verschobene statische Initialisierungen sind semantikfrei.
- [ ] `Magicka/GameLogic/Entities/Snare.cs`
- [x] `Magicka/Levels/Triggers/Interactable.cs` — VOLLSTÄNDIG: fehlende Szene
  oder fehlendes Levelmodell in `Highlight`, Prefix und 3
  Drei-Wege-Szenarien; die Wiederverwendung des Schleifenindex im manuellen
  Dekompilat ist semantikfreies Compilerrauschen.
- [x] `Magicka/Levels/Packs/PackMan.cs` — VOLLSTÄNDIG: das gemeinsame Lizenzprädikat ist für beide Pack-Setter und alle vier Anzeigeaufrufe migriert.
- [ ] `Magicka/GameLogic/Controls/ControlManager.cs` — TEILWEISE: die drei `Controller`-Überladungen der Player-Input-Sperre sind mit 3 Prefixen und 3 Drei-Wege-Szenarien migriert; `HybridInputSupport.Update` in `HandleInput` ist noch offen.
- [x] `Magicka/GameLogic/Player.cs` — VOLLSTÄNDIG: Avatar-Setter sowie die
  unabhängige Freigabe von TextBox und Notifier in `DeinitializeGame`, 3
  Prefixe und 9 Drei-Wege-Szenarien.
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SummonSpirit.cs` — VOLLSTÄNDIG: beide gespeicherten PlayState-Zuweisungen, vier veraltete Spawn-Zugriffe und statischer Template-Cache, 4 Transpiler und gemeinsame Drei-Wege-Szenarien.
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SummonFlamer.cs` — VOLLSTÄNDIG: beide gespeicherten PlayState-Zuweisungen, vier veraltete Spawn-Zugriffe und statischer Template-Cache, 4 Transpiler und gemeinsame Drei-Wege-Szenarien.
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/HomingCharge.cs` — TEILWEISE: gespeicherter PlayState, veralteter EntityManager-Zugriff und statischer Levelcache sind mit 3 Transpilern und gemeinsamen Drei-Wege-Szenarien migriert; die GC-Diagnosemarkierungen folgen im Diagnostics-Block.
- [x] `Magicka/Graphics/EffectManager.cs` — VOLLSTÄNDIG: Doppelte
  Effektdateinamen behalten die zuerst geladene Definition und werden vor dem
  XML-Parsing übersprungen; ein Transpiler und zwei Drei-Wege-Szenarien. Die
  verschobene statische Lock-Initialisierung ist semantikfreies Compilerrauschen.
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Shrink.cs` — TEILWEISE: freier und aktiver Pool werden im initialisierten Level-Dispose geleert, ein Transpiler und 2 Drei-Wege-Szenarien; die GC-Diagnosemarkierungen folgen im Diagnostics-Block.
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SummonUndead.cs` — TEILWEISE: beide gespeicherten PlayState-Zuweisungen, vier veraltete Spawn-Zugriffe, statischer Template-Cache und die Netzwerk-Replikation des Undead-Flags sind mit 5 Transpilern und 9 Drei-Wege-Szenarien migriert; die zugehörige Telemetrie folgt mit dem gemeinsamen Diagnostics-Block.
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/SummonCross.cs` — VOLLSTÄNDIG: beide gespeicherten PlayState-Zuweisungen, drei veraltete Spawn-Zugriffe sowie Pool- und Template-Freigabe, 3 Transpiler und 4 Drei-Wege-Szenarien; die statische Hash-Initialisierung ist semantikfreies Compilerrauschen.
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/StopCharge.cs` — TEILWEISE: gespeicherter PlayState, veralteter `GreaseSplash`-Zustand und statischer Levelcache sind mit 3 Transpilern und gemeinsamen Drei-Wege-Szenarien migriert; die GC-Diagnosemarkierungen folgen im Diagnostics-Block.
- [x] `Magicka/GameLogic/GameStates/MenuState.cs` — VOLLSTÄNDIG: Controllererkennung, verzögerte DirectInput-Warnung und die bis zum Prozessende verlängerte Lebensdauer der Paradox-Kontodaten sind migriert.
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/ChillyBlast.cs` — VOLLSTÄNDIG: gespeicherter PlayState in `Execute` und beide veralteten EntityManager-Zugriffe in `Update`, 2 Transpiler und 3 Drei-Wege-Szenarien; statische Hash-Initialisierer sind semantikfreies Compilerrauschen. In 1.4.16.0 und 1.5.1.0 ist die Klasse nicht vorhanden.
- [ ] `Magicka/GameLogic/Spells/SpellEffects/LightningSpell.cs` — TEILWEISE: alle drei Zugriffe auf den global gespeicherten PlayState und die statische Poolfreigabe sind migriert; die RetentionRegistry-Diagnostik ist noch offen.
- [ ] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/Haste.cs` — TEILWEISE: freier und aktiver Pool werden im initialisierten Level-Dispose geleert, ein Transpiler und 2 Drei-Wege-Szenarien; die GC-Diagnosemarkierungen folgen im Diagnostics-Block.
- [ ] `Magicka/GameLogic/Entities/Bosses/PropBoss.cs`
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/StarGaze.cs` — VOLLSTÄNDIG: abgelaufene, bereits deinitialisierte Opfer verwenden bei der Bereinigung die weiterhin verfügbare aktuelle Fraktion, ein Transpiler und 2 Drei-Wege-Szenarien; die statische Initialisierer-Umschreibung ist semantikfreies Compilerrauschen.
- [x] `Magicka/GameLogic/Entities/Abilities/SpecialAbilities/PoisonSpray.cs` — VOLLSTÄNDIG: gespeicherter PlayState in `Execute` und beide veralteten EntityManager-Zugriffe in `Update`, 2 Transpiler und 3 Drei-Wege-Szenarien; lokale `yaw`-Variable und statische Hash-Initialisierer sind semantikfreies Compilerrauschen.
