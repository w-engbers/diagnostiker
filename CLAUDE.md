# Projekt „Diagnostiker"

USB-Prüfstick für PCs und Notebooks (Windows und Linux), mit Stick-Manager zum Erstellen und Aktualisieren. Architektur: `docs/ARCHITEKTUR.md`.
Der Stick wird nur unter Linux erstellt. Dieses Verzeichnis ist ein eigenes Git-Repository und von allen anderen Projekten unabhängig.

## Regeln

- **Das Repo kann später veröffentlicht werden.** Deshalb: keine Zugangsdaten, Tokens, privaten Daten, internen Adressen, Rechnernamen und
  absoluten Pfade im Repo, nur relative Pfade. Keine Verweise auf andere Projekte. Konfiguration über `*.beispiel`, echte Dateien in `.gitignore`.
- Bibliotheken und Fremdwerkzeuge nur mit Version, Prüfsumme und Lizenz (`werkzeuge.lock.json`), nichts ohne Prüfsumme einbinden.
- Standardwerkzeuge vor Eigenbau. Bauweg und Aktualisierung müssen auf einem frischen Linux nachvollziehbar sein.
- Nichts auf einen echten Datenträger schreiben ohne ausdrückliche Laufwerksauswahl und Bestätigung des Auftraggebers.
- Antworten auf Deutsch, freundlich, mit Begründung. Untersuchen, umsetzen, testen, berichten, vor Unumkehrbarem fragen. Testdaten und
  Wegwerfskripte als solche benennen. Commit und Push nur auf Wunsch.
- Die Doku (`docs/`) im selben Zug fortschreiben wie den Code.
