# 🌊 Nitrox to Singleplayer Save Converter

**Ein hochentwickeltes BepInEx-Plugin für Subnautica, das Nitrox-Multiplayer-Spielstände nahtlos analysiert, dekomprimiert und in voll funktionsfähige Singleplayer-Saves konvertiert.**

---

Hast du Dutzende von Stunden auf einem Nitrox-Multiplayer-Server verbracht und möchtest diese Welt im Einzelspielermodus weiterspielen? Diese Mod liest die serialisierten JSON-Speicherdaten deines Nitrox-Servers ein und rekonstruiert die gesamte Spielwelt – einschließlich gigantischer Basenstrukturen, Energierelais, personalisierter Fahrzeuge, Containerinhalte, PDA-Daten und des Story-Fortschritts – direkt in einer neuen oder bestehenden Singleplayer-Sitzung.

---

## 🚀 Kernfunktionen & Technische Umsetzung

Die Konvertierung einer Multiplayer-Welt in eine Singleplayer-Welt erfordert weit mehr als das bloße Laden von Koordinaten. Die Mod führt einen präzise getakteten, mehrstufigen asynchronen Workflow (Unity Coroutines) aus, um physikalische Glitches zu vermeiden und sicherzustellen, dass alle komplexen Subnautica-Systeme korrekt initialisiert werden.

### 1. 🏗️ Pass 1: Rekonstruktion der Basis-Geometrie
* **Wie es funktioniert:** Nitrox speichert Basen als komprimierte Grid-Daten in Base64-Formaten. Die Mod dekomprimiert diese Daten (`Faces`, `Cells`, `Links`, `Masks`, `IsGlass`) mithilfe eines hochentwickelten Run-Length-Encoding-Algorithmus (RLE) und der `DeflateStream`-Klasse (`DecompressRleBytes`).
* **Technische Umsetzung:** Über Reflection werden die privaten Felder der nativen `Base`-Klasse des Spiels (wie `faces`, `cells`, `links`, `cellOffset`, `masks`, `isGlass` und `anchor`) befüllt. Anschließend triggert die Mod das native Deserialisierungs-Event `FinishDeserialization`, wodurch die Geometrie im Spiel physikalisch korrekt und ohne FPS-Einbrüche erzeugt wird.

### 2. 🗄️ Pass 2: Ausrüstung, Dekoration & Container-Synchronisation
* **Wie es funktioniert:** Sobald die Basen physisch existieren, werden Einrichtungsgegenstände (Fabrikatoren, Batterieladestationen, Poster, Funkgeräte) an ihren exakten relativen Positionen platziert.
* **Technische Umsetzung:** 
  * **Automatisches Reparenting:** Gegenstände, die zu einer Basis gehören, werden dynamisch als Child-Objekte der entsprechenden Basis-Transform (`GetModulesRoot()`) registriert, damit sie sich bei Erschütterungen nicht verschieben.
  * **Beschriftungssynchronisation:** Wandbeschriftungen und Schilder (wie bei Schränken) werden über `uGUI_SignInput` rekonstruiert.
  * **Behälterinhalte:** Ein universeller, auf Reflection basierender Container-Finder sucht nach allen Feldern und Properties vom Typ `ItemsContainer` (z. B. in Schränken, Reaktoren, Filtermaschinen) und befüllt diese asynchron mit den ursprünglichen Nitrox-Items.

### 3. 🏎️ Pass 3: Fahrzeug-Wiederherstellung (Cyclops, Seamoth, Prawn)
* **Wie es funktioniert:** Fahrzeuge werden mit ihren originalen Namen, Farben, Upgrades und Energiewerten geladen.
* **Technische Umsetzung:**
  * **Spezial-Handling für Cyclops:** Um physikalische Instabilitäten beim Laden von über 60 potentiellen Inneneinrichtungsobjekten zu verhindern, wird die Cyclops temporär physikalisch eingefroren (`isKinematic = true`) und exakt waagerecht ausgerichtet.
  * **Namen & kosmetische Farben:** Werden direkt auf das `SubName`-Skript übertragen (`DeserializeName` und `DeserializeColors`).
  * **Upgrade-Module & Slots:** Die Mod instanziiert die Upgrades und fügt sie den korrekten Slots im Modul-Grid des Fahrzeugs hinzu (`vehicle.modules.AddItem` bzw. `upgradeConsole.modules.AddItem` beim Cyclops).
  * **Energiemanagement:** Batterien und Energiezellen werden über `EnergyMixin` geladen und auf die ursprünglichen Nitrox-Ladungsstände geladen.
  * **Docking-Integration:** Die Mod sucht im Umkreis nach passenden Mondpools oder Cyclops-Docking-Buchten und dockt Seamoth/Prawn-Fahrzeuge automatisch ein (`VehicleDockingBay.DockVehicle`).

### 4. 👤 Spieler-Profil & Inventar-Synchronisation
* **Wie es funktioniert:** Die Identität und der Zustand des Spielers werden komplett wiederhergestellt.
* **Technische Umsetzung:**
  * **Sicherer Teleport:** Um Fall- oder Erstickungstode beim Teleportieren zu verhindern, deaktiviert die Mod temporär den `CharacterController`, setzt die 3D-Koordinaten (`SafeTeleport`) und reaktiviert ihn. Zudem werden kurzzeitig Sauerstoff- und Unverwundbarkeits-Cheats aktiviert.
  * **Lebenswerte:** Hunger, Durst und Gesundheit werden exakt auf den Zustand im Nitrox-Save gesetzt.
  * **Inventar & Equipment:** Das Standard-Startinventar wird gelöscht. Alle Multiplayer-Gegenstände werden neu erzeugt und in die exakten Slots (Anzug, Flasche, Handschuhe usw.) ausgerüstet.

### 5. 📖 PDA, Blueprints & Story-Fortschritt
* **Wie es funktioniert:** Dein gesamter Spielfortschritt bleibt erhalten.
* **Technische Umsetzung:**
  * **Blueprints & Datenbank:** Unlocked Blueprints werden über `KnownTech.Add` registriert, Enzyklopädie-Einträge via `PDAEncyclopedia.Add`.
  * **Audio- & Radio-Logs:** Werden rekonstruiert und ihre ursprünglichen Timestamps per Reflection in `PDALog.entries` eingetragen.
  * **Story-Trigger:** Bereits abgeschlossene Story-Ziele (wie Aurora-Explosionen oder Precursor-Events) werden über `Story.StoryGoalManager.main.OnGoalComplete` getriggert, um alle verknüpften Events im Spiel auszulösen.
  * **Signal-Schutz:** Wichtige Signale und Rettungskapsel-Marker werden explizit wieder sichtbar geschaltet (`PingInstance.SetVisible(true)`).
  * **Weltzeit:** Die Tageszeit wird über `DayNightCycle.main.timePassedAsDouble` synchronisiert.

### 6. ⚡ Verzögerte Energie-Registrierung (Delayed Power Registration)
* **Wie es funktioniert:** Reaktoren und Filtermaschinen benötigen Zeit zur physikalischen Initialisierung.
* **Technische Umsetzung:** Ein zweistufiger verzögerter Hintergrundprozess (`DelayedRegisterAllBasePowerDevices`) scannt 8 und 13 Sekunden nach der Konvertierung die gesamte Welt ab. Er verknüpft Kernreaktoren (`BaseNuclearReactor`), Bioreaktoren (`BaseBioReactor`) und Wasserfilterungsanlagen (`FiltrationMachine`) mit dem Energienetzwerk (`PowerRelay`) der jeweils nächsten Basis, um Strom, Licht und Sauerstoffproduktion sofort zu aktivieren.

---

## 🛠️ Harmony-Patches (Die technischen Lebensretter)

Da die Subnautica-Engine nicht für das plötzliche Einfügen von Multiplayer-Netzwerkdaten im laufenden Singleplayer-Betrieb ausgelegt ist, verwendet die Mod **Harmony (2.x)**-Patches, um Abstürze und Fehlermeldungen (NullReferenceExceptions) abzufangen:

1. **`BaseFiltrationMachineGeometry_UpdateVisuals_Patch` (Prefix & Finalizer):** Verhindert NullReferenceExceptions in der Geometrie-Visualisierung von Wasserfiltern. Wenn das Spiel die Verbindung zwischen der Basis-Wand und der Maschine verliert, führt der Patch eine distanzbasierte Suche durch und stellt die visuelle Verbindung sicher, andernfalls wird der Aufruf sicher übersprungen.
2. **`FiltrationMachine_Start_Patch` & `BaseNuclearReactor_Start_Patch` & `BaseBioReactor_Start_Patch` (Postfix):** Erzwingen direkt nach dem Start der Komponenten, dass diese eine gültige Referenz auf ihre Basis (`Base`) und deren Energierelais (`PowerRelay`) erhalten.

---

## 📂 Nitrox-Speicherstruktur (Was wird gelesen?)

Die Mod sucht im Save-Verzeichnis nach den folgenden vier JSON-Dateien aus deinem Nitrox-Server-Save:

| Datei | Beschreibung | In der Mod abgebildet als |
|---|---|---|
| **`WorldData.json`** | Enthält den World-Seed, PDA-Scans, Enzyklopädie-Einträge und Story-Fortschritte. | `WorldData` / `GameData` |
| **`PlayerData.json`** | Profile aller Spieler inklusive Ausrüstung, Position, Ausrichtung und Überlebenswerten. | `List<PlayerData>` |
| **`GlobalRootData.json`** | Der Haupt-Spielstand mit allen großen Entitäten (Basen-Grids, Fahrzeuge). | `List<GlobalEntityData>` |
| **`EntityData.json`** | Kleinere, dynamische Weltobjekte (gedroppte Items, Fische, etc.). | `List<GlobalEntityData>` |

---

## 📋 Anforderungen

* **Subnautica** (Version 2025 oder neuer empfohlen)
* **BepInEx 5.x** (Mod Loader)
* **Newtonsoft.Json** (im Release-Paket der Mod enthalten)

---

## 📦 Installation & Einrichtung

1. **BepInEx 5.x installieren:**
   * Lade [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) herunter.
   * Entpacke das Archiv in das Hauptverzeichnis deines Subnautica-Spiels (dort, wo sich die `Subnautica.exe` befindet).
   * Starte das Spiel einmal kurz, damit BepInEx seine Ordnerstrukturen anlegt, und schließe es wieder.

2. **Mod-Dateien kopieren:**
   * Verschiebe die `NitroxConverterMod.dll` (und die mitgelieferte `Newtonsoft.Json.dll`, falls vorhanden) in das Verzeichnis `Subnautica/BepInEx/plugins/`.

3. **Nitrox-Spielstand bereitstellen:**
   * Erstelle im `plugins`-Ordner einen Unterordner namens `save`.
   * Kopiere die vier JSON-Dateien (`GlobalRootData.json`, `EntityData.json`, `PlayerData.json`, `WorldData.json`) deines Nitrox-Servers in diesen Ordner:
     ```
     Subnautica/
     └── BepInEx/
         └── plugins/
             └── save/
                 ├── GlobalRootData.json
                 ├── EntityData.json
                 ├── PlayerData.json
                 └── WorldData.json
     ```

---

## 🚀 Nutzung

1. Starte Subnautica und **lade einen beliebigen Spielstand** (oder erstelle einen neuen Überlebensmodus-Spielstand).
2. Verlasse die **Rettungskapsel (Lifepod 5)** und schwimme ins offene Wasser. 
   > ⚠️ **Wichtig:** Das Plugin wartet darauf, dass der Spieler die Kapsel verlässt, um Spawning-Konflikte zu vermeiden. Dies wird oben rechts in einem eleganten Statusfenster angezeigt.
3. Sobald der Status auf **"Active & Ready"** springt, drücke die Taste **`F10`** oder die Tastenkombination **`Strg + L`**.
4. Ein modernes, integriertes Overlay zeigt dir in Echtzeit den Fortschritt der Konvertierung an.
5. Sobald die Konvertierung abgeschlossen ist, **speichere das Spiel manuell ab**. Dein Spielstand ist nun dauerhaft im Einzelspielermodus spielbar! Die Mod-Dateien können danach wieder entfernt werden.

---

## ⚙️ Konfiguration

Nach dem ersten Spielstart wird eine Konfigurationsdatei unter `BepInEx/config/com.ctmn61.nitroxconverter.cfg` erstellt:

```ini
[General]
# Der Name des Spielers, dessen Inventar und Position wiederhergestellt werden soll.
# Bleibt dieses Feld leer oder steht dort "Player", wird automatisch das erste Spielerprofil 
# aus der PlayerData.json geladen.
PlayerName = Player
```

---

## 📄 Lizenz

Dieses Projekt ist lizenziert unter der [MIT-Lizenz](LICENSE).

---
**Plugin ID:** `com.ctmn61.nitroxconverter` | **Autor:** CTMN61 | **Version:** 1.0.0
