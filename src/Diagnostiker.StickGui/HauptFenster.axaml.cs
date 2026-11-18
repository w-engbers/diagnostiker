using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Diagnostiker.StickKern;
using Diagnostiker.StickKern.Paket;

namespace Diagnostiker.StickGui;

/// <summary>
/// Das Fenster des Stick-Managers: Paket laden und prüfen, Stick wählen, bestätigen, schreiben, Ergebnis.
/// Die gesamte Arbeit (Prüfen, Sicherheitsregeln, Schreiben) leistet der Kern (<c>Diagnostiker.StickKern</c>), das Fenster ruft ihn nur auf.
/// </summary>
public partial class HauptFenster : Window
{
    enum Seite { Paket, Stick, Bestaetigen, Schreiben, Ergebnis }

    readonly ILaufwerkQuelle _laufwerkQuelle;
    readonly IBefehlsausfuehrer _befehle;
    readonly Func<ILaufwerkQuelle, IBefehlsausfuehrer, PaketStickErsteller> _erstellerFabrik;
    readonly PaketHolen _holen;
    readonly string? _quellenangabe;
    readonly DispatcherTimer _aktualisierung;

    GeholtesPaket? _paket;
    List<Laufwerk> _geeignet = [];
    Laufwerk? _gewaehlt;
    bool _beschaeftigtWert;
    /// <summary>Paket wird geladen oder Stick wird geschrieben: Das Fenster lässt sich nicht schließen, die Sprache nicht wechseln.</summary>
    bool Beschaeftigt { get => _beschaeftigtWert; set { _beschaeftigtWert = value; SpracheLeiste.IsEnabled = !value; } }
    Seite _seite = Seite.Paket;
    bool _letzterErfolg;
    string _letztesProtokoll = "";
    int _ddZaehler;

    /// <summary>Pfad der Protokolldatei (null = Standard unter ~/.cache/diagnostiker; im Test ein Wegwerfordner).</summary>
    public string? ProtokollPfad { get; set; }

    /// <summary>
    /// Liefert, wie viele Sektoren der Kernel seit dem Start auf das Laufwerk geschrieben hat (Name wie „sdc“). Standard: <c>/sys/block/&lt;name&gt;/stat</c>,
    /// ohne Administratorrechte lesbar. Daraus entsteht der echte Fortschritt beim Schreiben (die Ausgabe von dd zeigt dort nur den Zwischenspeicher).
    /// </summary>
    public Func<string, long?> GeschriebeneSektoren { get; set; } = SysfsSektoren;

    static long? SysfsSektoren(string name)
    {
        try
        {
            if (name.Contains('/') || name.Contains("..")) return null;
            var f = File.ReadAllText($"/sys/block/{name}/stat").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return f.Length > 6 ? long.Parse(f[6]) : null;      // siebtes Feld: geschriebene Sektoren
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException) { return null; }
    }

    /// <summary>Datei, in der die gewählte Sprache gemerkt wird (null = Standard unter ~/.config/diagnostiker; im Test ein Wegwerfpfad).</summary>
    public string? SprachDatei { get; set; }

    /// <summary>Pause der automatischen Laufwerksliste (kurz im Test).</summary>
    public TimeSpan Aktualisierung { get => _aktualisierung.Interval; set => _aktualisierung.Interval = value; }

    public HauptFenster() : this(null, null, null, null, null) { }

    public HauptFenster(ILaufwerkQuelle? laufwerke, IBefehlsausfuehrer? befehle, string? quellenangabe, PaketHolen? holen,
        Func<ILaufwerkQuelle, IBefehlsausfuehrer, PaketStickErsteller>? erstellerFabrik)
    {
        InitializeComponent();
        _laufwerkQuelle = laufwerke ?? new LsblkQuelle();
        _befehle = befehle ?? new ProzessBefehle();
        _quellenangabe = quellenangabe;
        var vertrauen = new VertrauensSpeicher(VertrauensSpeicher.Standard());
        _holen = holen ?? new PaketHolen(vertrauen.Laden(), vertrauen, PaketHolen.StandardZwischenspeicher());
        _erstellerFabrik = erstellerFabrik ?? ((l, b) => new PaketStickErsteller(l, b));
        _aktualisierung = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _aktualisierung.Tick += async (_, _) => await LaufwerkeAktualisierenAsync();

                PaketNochmal.Click += async (_, _) => await PaketLadenAsync();
        PaketOrdner.Click += async (_, _) => await OrdnerWaehlenAsync();
        StickListe.SelectionChanged += (_, _) => StickAuswahlGeaendert();
        StickWeiter.Click += (_, _) => ZuBestaetigen();
        BestaetigenZurueck.Click += (_, _) => ZuStickWahl();
        BestaetigenEingabe.TextChanged += (_, _) =>
            BestaetigenStart.IsEnabled = _gewaehlt is not null && Bestaetigung.Stimmt(BestaetigenEingabe.Text, _gewaehlt);
        BestaetigenStart.Click += async (_, _) => await ErstellenAsync();
        ErgebnisNochEiner.Click += (_, _) => ZuStickWahl();
        ErgebnisSchliessen.Click += (_, _) => Close();
        SpracheEn.Click += async (_, _) => await SpracheWaehlenAsync("en");
        SpracheDe.Click += async (_, _) => await SpracheWaehlenAsync("de");
        SpracheNl.Click += async (_, _) => await SpracheWaehlenAsync("nl");
        Closing += (_, e) => { if (Beschaeftigt) { e.Cancel = true; Fusszeile.Text = Texte.SchliessenGesperrt; } };
        Opened += async (_, _) => await PaketLadenAsync();
        Closed += (_, _) => _aktualisierung.Stop();
        Beschriften();
        Zeige(Seite.Paket);
        PaketStatus.Text = Texte.PaketSuche;          // schon beim ersten Zeichnen etwas zeigen, nicht erst nach dem Öffnen
        PaketBalken.IsIndeterminate = true;
    }

    void Zeige(Seite s)
    {
        _seite = s;
        SeitePaket.IsVisible = s == Seite.Paket;
        SeiteStick.IsVisible = s == Seite.Stick;
        SeiteBestaetigen.IsVisible = s == Seite.Bestaetigen;
        SeiteSchreiben.IsVisible = s == Seite.Schreiben;
        SeiteErgebnis.IsVisible = s == Seite.Ergebnis;
        UntertitelSetzen();
        if (s != Seite.Stick) _aktualisierung.Stop();
        Fusszeile.Text = s == Seite.Schreiben ? "" : Fusszeile.Text;
    }

    // ---------------------------------------------------------------- 1: Paket

    async Task PaketLadenAsync()
    {
        if (Beschaeftigt) return;
        Beschaeftigt = true;
        Zeige(Seite.Paket);
        PaketNochmal.IsVisible = PaketOrdner.IsVisible = false;
        PaketStatus.Text = Texte.PaketSuche;
        PaketStatus.Foreground = null;
        PaketDetail.Text = "";
        PaketBalken.IsIndeterminate = true;
        PaketBalken.Value = 0;
        try
        {
            var quelle = PaketQuellen.Aus(_ordnerQuelle ?? _quellenangabe);
            _paket = await _holen.HolenAsync(quelle,
                z => Dispatcher.UIThread.Post(() => { if (!Beschaeftigt) return; PaketDetail.Text = z; }),
                (name, bytes, gesamt) => Dispatcher.UIThread.Post(() =>
                {
                    if (!Beschaeftigt) return;             // späte Meldungen dürfen das Endergebnis nicht überschreiben
                    PaketStatus.Text = Texte.PaketLaedt;
                    PaketBalken.IsIndeterminate = false;
                    PaketBalken.Value = gesamt > 0 ? bytes * 100.0 / gesamt : 0;
                    PaketDetail.Text = Texte.Fortschritt(name, Sicherheit.Groesse(bytes), Sicherheit.Groesse(gesamt));
                }), CancellationToken.None);
            PaketBalken.IsIndeterminate = false;
            PaketBalken.Value = 100;
            PaketStatus.Text = Texte.PaketOk(_paket.Manifest.Version);
            PaketDetail.Text = "";
            Beschaeftigt = false;
            ZuStickWahl();
        }
        catch (Exception e) when (e is PaketException or HttpRequestException or IOException or UnauthorizedAccessException)
        {
            Beschaeftigt = false;
            PaketBalken.IsIndeterminate = false;
            PaketBalken.Value = 0;
            PaketStatus.Text = Texte.PaketFehler;
            PaketStatus.Foreground = Brushes.IndianRed;
            PaketDetail.Text = e is HttpRequestException ? Texte.KeineVerbindung(e.Message) : e.Message;
            PaketNochmal.IsVisible = PaketOrdner.IsVisible = true;
        }
    }

    async Task OrdnerWaehlenAsync()
    {
        var ordner = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Texte.PaketOrdnerTitel, AllowMultiple = false });
        var pfad = ordner.FirstOrDefault()?.TryGetLocalPath();
        if (pfad is null) return;
        _ordnerQuelle = pfad;
        await PaketLadenAsync();
    }

    string? _ordnerQuelle;

    // ---------------------------------------------------------------- 2: Stick wählen

    void ZuStickWahl()
    {
        Zeige(Seite.Stick);
        StickWeiter.IsEnabled = StickListe.SelectedIndex >= 0;
        _ = LaufwerkeAktualisierenAsync();
        _aktualisierung.Start();
    }

    async Task LaufwerkeAktualisierenAsync()
    {
        if (!SeiteStick.IsVisible) return;
        List<Laufwerk> neu;
        try { neu = (await _laufwerkQuelle.LesenAsync(CancellationToken.None)).Where(l => Sicherheit.Pruefe(l).Erlaubt).ToList(); }
        catch (Exception e) { StickDetail.Text = Texte.LaufwerkeNichtLesbar(e.Message); return; }
        if (!SeiteStick.IsVisible) return;
        if (neu.Select(l => l.Pfad + l.Groesse).SequenceEqual(_geeignet.Select(l => l.Pfad + l.Groesse)) && StickListe.ItemCount == neu.Count)
        {
            if (neu.Count == 0) StickDetail.Text = Texte.StickKeiner;      // auch beim ersten Lesen ohne Stick den Hinweis zeigen
            return;
        }
        var vorher = (StickListe.SelectedIndex >= 0 && StickListe.SelectedIndex < _geeignet.Count) ? _geeignet[StickListe.SelectedIndex].Pfad : null;
        _geeignet = neu;
        StickListe.ItemsSource = neu.Select(l => $"{l.Pfad}   {l.Beschreibung}   {Sicherheit.Groesse(l.Groesse)}").ToList();
        var index = vorher is null ? -1 : neu.FindIndex(l => l.Pfad == vorher);
        StickListe.SelectedIndex = index;
        StickAuswahlGeaendert();
        if (neu.Count == 0) StickDetail.Text = Texte.StickKeiner;
    }

    void StickAuswahlGeaendert()
    {
        var i = StickListe.SelectedIndex;
        StickWeiter.IsEnabled = i >= 0 && i < _geeignet.Count;
        if (i < 0 || i >= _geeignet.Count) { if (_geeignet.Count > 0) StickDetail.Text = ""; return; }
        var l = _geeignet[i];
        var hinweise = Sicherheit.Pruefe(l).Hinweise;
        StickDetail.Text = Texte.StickPruefung + (hinweise.Count > 0 ? "\n" + string.Join("\n", hinweise.Select(h => "• " + h)) : "");
    }

    // ---------------------------------------------------------------- 3: Bestätigen

    void ZuBestaetigen()
    {
        var i = StickListe.SelectedIndex;
        if (i < 0 || i >= _geeignet.Count) return;
        _gewaehlt = _geeignet[i];
        BestaetigenTexte();
        BestaetigenEingabe.Text = "";
        BestaetigenStart.IsEnabled = false;
        Zeige(Seite.Bestaetigen);
        BestaetigenEingabe.Focus();
    }

    void BestaetigenTexte()
    {
        if (_gewaehlt is null) return;
        BestaetigenStick.Text = $"{_gewaehlt.Pfad}   {_gewaehlt.Beschreibung}   {Sicherheit.Groesse(_gewaehlt.Groesse)}";
        BestaetigenWarnung.Text = Texte.BestaetigenWarnung(_gewaehlt.Pfad);
        BestaetigenFrage.Text = Texte.BestaetigenFrage(Bestaetigung.Erwartet(_gewaehlt));
    }

    // ---------------------------------------------------------------- 4: Schreiben

    async Task ErstellenAsync()
    {
        if (_gewaehlt is null || _paket is null || !Bestaetigung.Stimmt(BestaetigenEingabe.Text, _gewaehlt)) return;
        Beschaeftigt = true;
        _ddZaehler = 0;
        var eingabe = BestaetigenEingabe.Text;
        var auswertung = new FortschrittAuswertung(_paket.Manifest.Boot.EntpacktGroesse ?? 0);
        Zeige(Seite.Schreiben);
        SchreibenLog.Text = "";
        SchreibenSchritt.Text = Texte.SchreibenVorbereitung;
        SchreibenBalken.Value = 0;
        SchreibenTempo.Text = "";
        SchreibenHinweis.Text = Texte.SchreibenPasswort;
        var ersteZeile = false;
        var schreibStart = (DateTime?)null;
        var basis = GeschriebeneSektoren(_gewaehlt.Name);
        var geraet = _gewaehlt.Name;
        var zaehlerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        zaehlerTimer.Tick += (_, _) =>
        {
            if (basis is null || schreibStart is null || GeschriebeneSektoren(geraet) is not { } jetzt) return;
            if (auswertung.Geschrieben(Math.Max(0, jetzt - basis.Value) * 512, (DateTime.UtcNow - schreibStart.Value).TotalSeconds)) AnzeigeAktualisieren(auswertung);
        };
        zaehlerTimer.Start();

        ErstellErgebnis ergebnis;
        string protokollPfad = "";
        try
        {
            using var log = new LaufProtokoll(ProtokollPfad);
            protokollPfad = log.Pfad;
            log.Schreiben($"Start: {_gewaehlt.Pfad} (Fenster)");
            var ersteller = _erstellerFabrik(_laufwerkQuelle, _befehle);
            // Schritte nach dem Helfer (einhängen, kopieren, aushängen) meldet der Ersteller sprachunabhängig.
            ersteller.SchrittMeldung = m => Dispatcher.UIThread.Post(() => { if (auswertung.Zeile(m)) AnzeigeAktualisieren(auswertung); });
            ergebnis = await ersteller.ErstellenAsync(_gewaehlt, eingabe, _paket, z =>
            {
                log.Schreiben(z);
                Dispatcher.UIThread.Post(() =>
                {
                    if (!ersteZeile) { ersteZeile = true; SchreibenHinweis.Text = Texte.SchreibenLaeuft; }     // die Passwortabfrage ist vorbei
                    if (z.Contains("Abbild schreiben") && schreibStart is null) schreibStart = DateTime.UtcNow;
                    if (auswertung.Zeile(z)) AnzeigeAktualisieren(auswertung);
                    if (z.Contains(" copied, ") && ++_ddZaehler % 10 != 0) return;     // dd-Fortschrittszeilen nur gelegentlich im Protokollfeld
                    SchreibenLog.Text += z + "\n";
                    SchreibenLog.CaretIndex = SchreibenLog.Text.Length;
                });
            });
            log.Schreiben($"Ergebnis: Erfolg={ergebnis.Erfolg} {ergebnis.Meldung}");
        }
        catch (Exception e)
        {
            ergebnis = new ErstellErgebnis(false, Texte.Unerwartet(e.Message));
        }
        zaehlerTimer.Stop();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);     // erst alle noch ausstehenden Zeilen verarbeiten
        Beschaeftigt = false;
        if (ergebnis.Erfolg) { auswertung.Fertig(); SchreibenBalken.Value = auswertung.Gesamt; }
        Ergebnis(ergebnis, protokollPfad);
    }

    void AnzeigeAktualisieren(FortschrittAuswertung a)
    {
        SchreibenSchritt.Text = a.Schritt;
        SchreibenBalken.Value = a.Gesamt;
        SchreibenTempo.Text = a.Geschwindigkeit.Length > 0 ? Texte.Geschwindigkeit(a.Geschwindigkeit) : "";
    }

    // ---------------------------------------------------------------- 5: Ergebnis

    void Ergebnis(ErstellErgebnis e, string protokollPfad)
    {
        _letzterErfolg = e.Erfolg;
        _letztesProtokoll = protokollPfad;
        Zeige(Seite.Ergebnis);
        ErgebnisText.Text = e.Meldung;            // bleibt in der Sprache, in der sie entstanden ist
        ErgebnisTexte();
    }

    void ErgebnisTexte()
    {
        ErgebnisTitel.Text = _letzterErfolg ? Texte.ErgebnisErfolg : Texte.ErgebnisFehler;
        ErgebnisTitel.Foreground = _letzterErfolg ? Brushes.SeaGreen : Brushes.IndianRed;
        ErgebnisProtokoll.Text = _letztesProtokoll.Length > 0 ? Texte.ErgebnisProtokoll(_letztesProtokoll) : "";
    }

    // ---------------------------------------------------------------- Sprache

    void UntertitelSetzen() => Untertitel.Text = _seite switch
    {
        Seite.Paket => Texte.UntertitelPaket, Seite.Stick => Texte.UntertitelStick, Seite.Bestaetigen => Texte.UntertitelBestaetigen,
        Seite.Schreiben => Texte.UntertitelSchreiben, _ => Texte.UntertitelErgebnis,
    };

    /// <summary>Setzt alle festen Beschriftungen in der aktuellen Sprache und markiert die gewählte Sprache.</summary>
    void Beschriften()
    {
        Title = Texte.Titel;
        UntertitelSetzen();
        PaketNochmal.Content = Texte.PaketNochmal;
        PaketOrdner.Content = Texte.PaketOrdnerWaehlen;
        StickErklaerung.Text = Texte.StickErklaerung;
        StickWeiter.Content = Texte.Weiter;
        BestaetigenZurueck.Content = Texte.Zurueck;
        BestaetigenStart.Content = Texte.BestaetigenStart;
        SchreibenDetails.Header = Texte.SchreibenDetails;
        ErgebnisNochEiner.Content = Texte.ErgebnisNochEiner;
        ErgebnisSchliessen.Content = Texte.ErgebnisSchliessen;
        foreach (var (knopf, kuerzel) in new[] { (SpracheEn, "en"), (SpracheDe, "de"), (SpracheNl, "nl") })
        {
            var gewaehlt = Sprache.Aktuell == kuerzel;
            knopf.FontWeight = gewaehlt ? FontWeight.Bold : FontWeight.Normal;
            knopf.BorderThickness = new Avalonia.Thickness(gewaehlt ? 2 : 1);
            knopf.BorderBrush = gewaehlt ? Brushes.SteelBlue : Brushes.Transparent;
        }
    }

    async Task SpracheWaehlenAsync(string kuerzel)
    {
        if (Beschaeftigt || Sprache.Aktuell == kuerzel) return;
        Sprache.Aktuell = kuerzel;
        Sprache.Speichern(kuerzel, SprachDatei);
        Beschriften();
        Fusszeile.Text = "";
        switch (_seite)
        {
            case Seite.Paket:
                if (_paket is not null) PaketStatus.Text = Texte.PaketOk(_paket.Manifest.Version);
                else if (PaketNochmal.IsVisible) await PaketLadenAsync();         // Fehlermeldung in der neuen Sprache neu erzeugen
                break;
            case Seite.Stick:
                var vorher = StickListe.SelectedIndex >= 0 && StickListe.SelectedIndex < _geeignet.Count ? _geeignet[StickListe.SelectedIndex].Pfad : null;
                _geeignet = [];                                                   // Liste mit den übersetzten Texten neu aufbauen
                StickListe.ItemsSource = null;
                await LaufwerkeAktualisierenAsync();
                if (vorher is not null) StickListe.SelectedIndex = _geeignet.FindIndex(l => l.Pfad == vorher);     // Auswahl behalten
                break;
            case Seite.Bestaetigen:
                BestaetigenTexte();
                break;
            case Seite.Ergebnis:
                ErgebnisTexte();
                break;
        }
    }
}
