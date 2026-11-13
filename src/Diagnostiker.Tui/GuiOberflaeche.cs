using System.Collections.ObjectModel;
using System.Text;
using Diagnostiker.Kern;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Status = Diagnostiker.Kern.Status;

namespace Diagnostiker.Tui;

/// <summary>
/// Bildschirmoberfläche (Terminal.Gui): links die Prüfungen mit Bewertung, rechts die Einzelheiten der markierten Prüfung
/// (scrollbar), unten die Tasten. Läuft auf der Linux-Textkonsole und in Windows-Konsolen, ohne Grafikmodus.
/// Die Prüfungen laufen im Hintergrund, die Oberfläche bleibt bedienbar.
/// Bewusst nur ASCII-Kennzeichen ([OK], [FEHLER] …): Die Konsolenschrift von Linux hat keine Glyphen für ✔ ✘ ▲ ▼.
/// </summary>
sealed class GuiOberflaeche(Kontext k)
{
    enum Folgeaktion { Ende, Netzwerk }

    readonly ObservableCollection<string> _zeilen = [];
    readonly Scheme _schema = Farbschema();
    ModulDefinition? _laeuft;
    bool _beschaeftigt;
    bool _zeigeZusammenfassung;
    string? _hinweis;            // einmalige Meldung beim (Neu-)Start der Oberfläche
    int _letzteAuswahl;

    public async Task<int> LaufenAsync()
    {
        var wiederholeNetzwerk = false;
        while (true)
        {
            var aktion = Anzeigen(wiederholeNetzwerk);
            wiederholeNetzwerk = false;
            if (aktion != Folgeaktion.Netzwerk) break;
            // Terminal.Gui ist beendet, das Terminal gehört jetzt nmtui; danach startet die Oberfläche neu.
            _hinweis = await Kontext.NetzwerkEinrichtenAsync();
            wiederholeNetzwerk = _hinweis is null;
        }
        k.Session.Speichern();
        Console.WriteLine($"Bericht: {Path.Combine(k.Session.Verzeichnis, "bericht.md")}");
        return 0;
    }

    // Eigenes Farbschema: Die Standardfarben von Terminal.Gui sind auf der Linux-Konsole mit ihren 16 Farben
    // unleserlich (grau auf grau). Weiß auf Schwarz, Auswahl weiß auf Blau.
    static Scheme Farbschema()
    {
        var normal = new Attribute(Color.White, Color.Black);
        var fokus = new Attribute(Color.White, Color.Blue);
        return new Scheme(normal)
        {
            Focus = fokus, HotNormal = new(Color.BrightCyan, Color.Black), HotFocus = new(Color.BrightCyan, Color.Blue),
            Active = fokus, HotActive = new(Color.BrightCyan, Color.Blue), Highlight = fokus,
            Editable = normal, ReadOnly = normal, Disabled = new(Color.DarkGray, Color.Black),
        };
    }

    Folgeaktion Anzeigen(bool wiederholeNetzwerk)
    {
        var aktion = Folgeaktion.Ende;
        using IApplication app = Application.Create();
        app.Init();

        using Window fenster = new() { Title = "Diagnostiker – Prüfstick" };
        fenster.SetScheme(_schema);

        var kopf = new Label
        {
            X = 1, Y = 0, Width = Dim.Fill(1),
            Text = $"Rechner: {k.Session.Rechnername}   System: {Plattform.OsName}   " +
                   (Plattform.HatAdminRechte ? "Administratorrechte: ja" : "Administratorrechte: NEIN (manche Prüfungen werden übersprungen)"),
        };
        var links = new FrameView { Title = "Prüfungen (Enter = ausführen)", X = 0, Y = 1, Width = Dim.Percent(50), Height = Dim.Fill(1) };
        var liste = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        _zeilen.Clear();
        foreach (var m in k.Module) _zeilen.Add(Zeile(m));
        liste.SetSource(_zeilen);
        links.CanFocus = true;
        links.Add(liste);

        var rechts = new FrameView { Title = "Einzelheiten", X = Pos.Right(links), Y = 1, Width = Dim.Fill(), Height = Dim.Fill(1) };
        var detail = new TextView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), ReadOnly = true, WordWrap = true };
        rechts.CanFocus = true;
        detail.CanFocus = true;
        rechts.Add(detail);

        void ZeigeText(string text)
        {
            detail.Text = text;
            detail.ScrollTo(new System.Drawing.Point(0, 0));
        }

        void Aktualisieren()
        {
            var auswahl = liste.SelectedItem ?? _letzteAuswahl;
            for (var i = 0; i < k.Module.Count; i++)
            {
                var neu = Zeile(k.Module[i]);
                if (_zeilen[i] != neu) _zeilen[i] = neu;
            }
            liste.SelectedItem = auswahl;
            ZeigeText(_zeigeZusammenfassung ? ZusammenfassungText() : DetailText(k.Module.ElementAtOrDefault(auswahl)));
            fenster.SetNeedsDraw();
        }

        // ---- Prüfungen starten ----
        void Starten(IReadOnlyList<ModulDefinition> module, bool fragen, bool mitZusammenfassung)
        {
            if (_beschaeftigt) return;
            var auftraege = new List<(ModulDefinition, Dictionary<string, string>)>();
            foreach (var m in module)
            {
                if (m.Schreibend && MessageBox.Query(app, "Daten verändern", $"'{m.Titel}' verändert Daten auf dem Prüfling.\nWirklich ausführen?", "Nein", "Ja") != 1) continue;
                var parameter = new Dictionary<string, string>();
                var abgebrochen = false;
                foreach (var p in m.Parameter)
                {
                    var wert = fragen ? Eingabe(app, m.Titel, p.Frage, p.Standard) : p.Standard;
                    if (wert is null) { abgebrochen = true; break; }
                    parameter[p.Name] = wert;
                }
                if (!abgebrochen) auftraege.Add((m, parameter));
            }
            if (auftraege.Count == 0) return;

            _beschaeftigt = true;
            _zeigeZusammenfassung = false;
            _ = Task.Run(async () =>
            {
                try
                {
                    foreach (var (m, parameter) in auftraege)
                    {
                        app.Invoke(() => { _laeuft = m; Aktualisieren(); });
                        var erg = await k.Ausfuehrer.AusfuehrenAsync(m, parameter);
                        app.Invoke(() => { k.Session.Hinzufuegen(erg); _laeuft = null; Aktualisieren(); });
                    }
                }
                catch (Exception e)
                {
                    app.Invoke(() => { _laeuft = null; ZeigeText($"Fehler beim Ausführen: {e.Message}"); });
                }
                finally
                {
                    app.Invoke(() =>
                    {
                        _beschaeftigt = false;
                        if (mitZusammenfassung) { _zeigeZusammenfassung = true; k.Session.Speichern(); }
                        Aktualisieren();
                    });
                }
            });
        }

        liste.ValueChanged += (_, _) =>
        {
            if (_beschaeftigt && _laeuft is not null) return;
            _letzteAuswahl = liste.SelectedItem ?? 0;
            _zeigeZusammenfassung = false;
            ZeigeText(DetailText(k.Module.ElementAtOrDefault(_letzteAuswahl)));
        };
        liste.Accepting += (_, e) =>
        {
            e.Handled = true;
            var m = k.Module.ElementAtOrDefault(liste.SelectedItem ?? -1);
            if (m is not null) Starten([m], fragen: true, mitZusammenfassung: false);
        };

        // ---- Tasten unten ----
        var status = new StatusBar();
        status.SetScheme(_schema);
        status.Add(new Shortcut { Key = Key.F5, Title = "Schnellprüfung", Action = () => Starten(k.Schnellpruefungen.ToList(), fragen: false, mitZusammenfassung: true) });
        if (k.NetzwerkEinrichten)
            status.Add(new Shortcut { Key = Key.F6, Title = "Netzwerk", Action = () => { if (_beschaeftigt) return; aktion = Folgeaktion.Netzwerk; app.RequestStop(); } });
        status.Add(new Shortcut
        {
            Key = Key.F7, Title = "Notiz", Action = () =>
            {
                var text = Eingabe(app, "Notiz zum Protokoll", "Notiz (z. B. Name/Beschwerde des Besitzers):", k.Session.Notiz);
                if (text is null) return;
                k.Session.Notiz = text; k.Session.Speichern();
            }
        });
        status.Add(new Shortcut { Key = Key.F8, Title = "Zusammenfassung", Action = () => { _zeigeZusammenfassung = true; k.Session.Speichern(); Aktualisieren(); } });
        status.Add(new Shortcut
        {
            Key = Key.F9, Title = "Liste/Einzelheiten", Action = () =>
            {
                if (detail.HasFocus) liste.SetFocus(); else detail.SetFocus();
            }
        });
        status.Add(new Shortcut { Key = Key.F10, Title = "Beenden", Action = () => app.RequestStop() });

        fenster.Add(kopf, links, rechts, status);
        ZeigeText(StartText());
        app.Invoke(() => { liste.SetFocus(); });
        if (wiederholeNetzwerk)
            app.Invoke(() => Starten(k.Module.Where(m => m.Gruppe == "Netzwerk").ToList(), fragen: false, mitZusammenfassung: false));

        app.Run(fenster);
        return aktion;
    }

    // ---- Texte ----

    string Zeile(ModulDefinition m)
    {
        var e = k.Session.Aktuelle.FirstOrDefault(x => x.Id == m.Id);
        var marke = ReferenceEquals(m, _laeuft) ? "[laeuft]" : e is null ? "" : Marke(e.Status);
        return $"{marke,-10} {m.Titel}";
    }

    static string Marke(Status s) => s switch
    {
        Status.Ok => "[OK]",
        Status.Warnung => "[WARNUNG]",
        Status.Fehler => "[FEHLER]",
        Status.Uebersprungen => "[uebersp.]",
        _ => "[Info]",
    };

    string StartText()
    {
        var t = new StringBuilder();
        if (_hinweis is not null) { t.AppendLine(_hinweis).AppendLine(); _hinweis = null; }
        foreach (var f in k.Ladefehler) t.AppendLine($"Modul fehlerhaft: {f}");
        if (k.Ladefehler.Count > 0) t.AppendLine();
        t.AppendLine("Willkommen beim Diagnostiker.");
        t.AppendLine();
        t.AppendLine("Pfeiltasten: Prüfung wählen        Enter: Prüfung ausführen");
        t.AppendLine("F5: Schnellprüfung (alle harmlosen Standardtests)");
        if (k.NetzwerkEinrichten) t.AppendLine("F6: Netzwerk verbinden (WLAN/LAN einrichten)");
        t.AppendLine("F7: Notiz zum Protokoll        F8: Zusammenfassung");
        t.AppendLine("F9 oder Tab: zwischen Liste und Einzelheiten wechseln (dort mit Pfeiltasten und Bild-auf/Bild-ab blättern)");
        t.AppendLine("F10: Beenden");
        t.AppendLine();
        t.AppendLine($"Protokoll: {k.Session.Verzeichnis}");
        return t.ToString();
    }

    string DetailText(ModulDefinition? m)
    {
        if (m is null) return StartText();
        var t = new StringBuilder();
        t.AppendLine(m.Titel).AppendLine($"Gruppe: {m.Gruppe}");
        if (m.Beschreibung.Length > 0) t.AppendLine().AppendLine(m.Beschreibung);
        if (m.Schreibend) t.AppendLine().AppendLine("ACHTUNG: Diese Prüfung verändert Daten auf dem Prüfling (mit Rückfrage).");
        if (m.Schritte.Any(s => s.Admin) && !Plattform.HatAdminRechte)
            t.AppendLine().AppendLine("Hinweis: Braucht Administratorrechte, ohne werden Schritte übersprungen.");
        t.AppendLine();
        if (ReferenceEquals(m, _laeuft)) { t.AppendLine("Läuft gerade …"); return t.ToString(); }
        var e = k.Session.Aktuelle.FirstOrDefault(x => x.Id == m.Id);
        if (e is null) { t.AppendLine("Noch nicht ausgeführt. Enter startet die Prüfung."); return t.ToString(); }
        t.AppendLine($"Letzter Lauf {e.Beginn:HH:mm}: {Marke(e.Status)}");
        var vorher = k.Session.Vorheriger(e);
        if (vorher is not null) t.AppendLine($"Vorher {vorher.Beginn:HH:mm}: {Marke(vorher.Status)}");
        foreach (var s in e.Schritte)
        {
            t.AppendLine().AppendLine($"{Marke(s.Status)} {s.Titel}");
            foreach (var meldung in s.Meldungen) t.AppendLine($"    - {meldung}");
        }
        return t.ToString();
    }

    string ZusammenfassungText()
    {
        var t = new StringBuilder();
        t.AppendLine("Zusammenfassung (jeweils der neueste Lauf)").AppendLine();
        foreach (var e in k.Session.Aktuelle)
        {
            var vorher = k.Session.Vorheriger(e);
            t.AppendLine($"{Marke(e.Status),-10} {e.Beginn:HH:mm}  {e.Titel}" +
                         (vorher is null ? "" : $"   (vorher {vorher.Beginn:HH:mm} {Session.Text(vorher.Status)})"));
        }
        if (k.Session.Aktuelle.Count == 0) t.AppendLine("Noch keine Prüfung ausgeführt.");
        t.AppendLine().AppendLine($"Gesamtbewertung: {Marke(k.Gesamt)}");
        t.AppendLine($"Bericht: {Path.Combine(k.Session.Verzeichnis, "bericht.md")}");
        return t.ToString();
    }

    // ---- Eingabedialog ----

    string? Eingabe(IApplication app, string titel, string frage, string standard)
    {
        string? ergebnis = null;
        var dialog = new Dialog { Title = titel, Width = Dim.Percent(70), Height = 11 };
        dialog.SetScheme(_schema);
        var text = new TextField { Text = standard, X = 1, Y = 3, Width = Dim.Fill(1) };
        var ok = new Button { Text = "OK", IsDefault = true };
        ok.Accepting += (_, e) => { ergebnis = text.Text; app.RequestStop(); e.Handled = true; };
        // Enter im Eingabefeld übernimmt den Text (sonst würde der zuletzt hinzugefügte Knopf, „Abbrechen“, ausgelöst).
        text.Accepting += (_, e) => { ergebnis = text.Text; app.RequestStop(); e.Handled = true; };
        var abbrechen = new Button { Text = "Abbrechen" };
        abbrechen.Accepting += (_, e) => { ergebnis = null; app.RequestStop(); e.Handled = true; };
        dialog.AddButton(ok);
        dialog.AddButton(abbrechen);
        // Das Eingabefeld zuletzt hinzufügen und gezielt fokussieren, sonst landet der Fokus auf dem letzten Knopf.
        dialog.Add(new Label { Text = frage, X = 1, Y = 1 }, text);
        text.SetFocus();
        dialog.Initialized += (_, _) => text.SetFocus();
        app.Run(dialog);
        return ergebnis;
    }
}
