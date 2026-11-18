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
    bool _beschaeftigt;                 // Paket wird geladen oder Stick wird geschrieben: Fenster darf nicht geschlossen werden
    int _ddZaehler;

    /// <summary>Pfad der Protokolldatei (null = Standard unter ~/.cache/diagnostiker; im Test ein Wegwerfordner).</summary>
    public string? ProtokollPfad { get; set; }

    /// <summary>Pause der automatischen Laufwerksliste (kurz im Test).</summary>
    public TimeSpan Aktualisierung { get => _aktualisierung.Interval; set => _aktualisierung.Interval = value; }

    public HauptFenster() : this(null, null, null, null, null) { }

    public HauptFenster(ILaufwerkQuelle? laufwerke, IBefehlsausfuehrer? befehle, string? quellenangabe, PaketHolen? holen,
        Func<ILaufwerkQuelle, IBefehlsausfuehrer, PaketStickErsteller>? erstellerFabrik)
    {
        InitializeComponent();
        Title = Texte.Titel;
        _laufwerkQuelle = laufwerke ?? new LsblkQuelle();
        _befehle = befehle ?? new ProzessBefehle();
        _quellenangabe = quellenangabe;
        var vertrauen = new VertrauensSpeicher(VertrauensSpeicher.Standard());
        _holen = holen ?? new PaketHolen(vertrauen.Laden(), vertrauen, PaketHolen.StandardZwischenspeicher());
        _erstellerFabrik = erstellerFabrik ?? ((l, b) => new PaketStickErsteller(l, b));
        _aktualisierung = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _aktualisierung.Tick += async (_, _) => await LaufwerkeAktualisierenAsync();

        StickErklaerung.Text = Texte.StickErklaerung;
        SchreibenHinweis.Text = Texte.SchreibenHinweis;
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
        Closing += (_, e) => { if (_beschaeftigt) { e.Cancel = true; Fusszeile.Text = Texte.SchliessenGesperrt; } };
        Opened += async (_, _) => await PaketLadenAsync();
        Closed += (_, _) => _aktualisierung.Stop();
        Zeige(Seite.Paket);
    }

    void Zeige(Seite s)
    {
        SeitePaket.IsVisible = s == Seite.Paket;
        SeiteStick.IsVisible = s == Seite.Stick;
        SeiteBestaetigen.IsVisible = s == Seite.Bestaetigen;
        SeiteSchreiben.IsVisible = s == Seite.Schreiben;
        SeiteErgebnis.IsVisible = s == Seite.Ergebnis;
        Untertitel.Text = s switch
        {
            Seite.Paket => Texte.UntertitelPaket, Seite.Stick => Texte.UntertitelStick, Seite.Bestaetigen => Texte.UntertitelBestaetigen,
            Seite.Schreiben => Texte.UntertitelSchreiben, _ => Texte.UntertitelErgebnis,
        };
        if (s != Seite.Stick) _aktualisierung.Stop();
        Fusszeile.Text = s == Seite.Schreiben ? "" : Fusszeile.Text;
    }

    // ---------------------------------------------------------------- 1: Paket

    async Task PaketLadenAsync()
    {
        if (_beschaeftigt) return;
        _beschaeftigt = true;
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
                z => Dispatcher.UIThread.Post(() => { if (!_beschaeftigt) return; PaketStatus.Text = z.Contains("laden") ? Texte.PaketLaedt : PaketStatus.Text; PaketDetail.Text = z; }),
                (name, bytes, gesamt) => Dispatcher.UIThread.Post(() =>
                {
                    if (!_beschaeftigt) return;             // späte Meldungen dürfen das Endergebnis nicht überschreiben
                    PaketBalken.IsIndeterminate = false;
                    PaketBalken.Value = gesamt > 0 ? bytes * 100.0 / gesamt : 0;
                    PaketDetail.Text = $"{name}: {Sicherheit.Groesse(bytes)} von {Sicherheit.Groesse(gesamt)}";
                }), CancellationToken.None);
            PaketBalken.IsIndeterminate = false;
            PaketBalken.Value = 100;
            PaketStatus.Text = string.Format(Texte.PaketOk, _paket.Manifest.Version);
            PaketDetail.Text = "";
            _beschaeftigt = false;
            ZuStickWahl();
        }
        catch (Exception e) when (e is PaketException or HttpRequestException or IOException or UnauthorizedAccessException)
        {
            _beschaeftigt = false;
            PaketBalken.IsIndeterminate = false;
            PaketBalken.Value = 0;
            PaketStatus.Text = Texte.PaketFehler;
            PaketStatus.Foreground = Brushes.IndianRed;
            PaketDetail.Text = e is HttpRequestException ? "Keine Verbindung oder das Paket ist nicht erreichbar. " + e.Message : e.Message;
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
        catch (Exception e) { StickDetail.Text = "Die Laufwerke lassen sich nicht lesen: " + e.Message; return; }
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
        BestaetigenStick.Text = $"{_gewaehlt.Pfad}   {_gewaehlt.Beschreibung}   {Sicherheit.Groesse(_gewaehlt.Groesse)}";
        BestaetigenWarnung.Text = string.Format(Texte.BestaetigenWarnung, _gewaehlt.Pfad);
        BestaetigenFrage.Text = string.Format(Texte.BestaetigenFrage, Bestaetigung.Erwartet(_gewaehlt));
        BestaetigenEingabe.Text = "";
        BestaetigenStart.IsEnabled = false;
        Zeige(Seite.Bestaetigen);
        BestaetigenEingabe.Focus();
    }

    // ---------------------------------------------------------------- 4: Schreiben

    async Task ErstellenAsync()
    {
        if (_gewaehlt is null || _paket is null || !Bestaetigung.Stimmt(BestaetigenEingabe.Text, _gewaehlt)) return;
        _beschaeftigt = true;
        _ddZaehler = 0;
        var eingabe = BestaetigenEingabe.Text;
        var auswertung = new FortschrittAuswertung(_paket.Manifest.Boot.EntpacktGroesse ?? 0);
        Zeige(Seite.Schreiben);
        SchreibenLog.Text = "";
        SchreibenSchritt.Text = "Stick wird vorbereitet …";
        SchreibenBalken.Value = 0;
        SchreibenTempo.Text = "";

        ErstellErgebnis ergebnis;
        string protokollPfad = "";
        try
        {
            using var log = new LaufProtokoll(ProtokollPfad);
            protokollPfad = log.Pfad;
            log.Schreiben($"Start: {_gewaehlt.Pfad} (Fenster)");
            var ersteller = _erstellerFabrik(_laufwerkQuelle, _befehle);
            ergebnis = await ersteller.ErstellenAsync(_gewaehlt, eingabe, _paket, z =>
            {
                log.Schreiben(z);
                Dispatcher.UIThread.Post(() =>
                {
                    if (auswertung.Zeile(z))
                    {
                        SchreibenSchritt.Text = auswertung.Schritt;
                        SchreibenBalken.Value = auswertung.Gesamt;
                        SchreibenTempo.Text = auswertung.Geschwindigkeit.Length > 0 ? $"Geschwindigkeit: {auswertung.Geschwindigkeit}" : "";
                    }
                    if (z.Contains(" copied, ") && ++_ddZaehler % 10 != 0) return;     // dd-Fortschrittszeilen nur gelegentlich im Protokollfeld
                    SchreibenLog.Text += z + "\n";
                    SchreibenLog.CaretIndex = SchreibenLog.Text.Length;
                });
            });
            log.Schreiben($"Ergebnis: Erfolg={ergebnis.Erfolg} {ergebnis.Meldung}");
        }
        catch (Exception e)
        {
            ergebnis = new ErstellErgebnis(false, "Unerwarteter Fehler: " + e.Message);
        }
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);     // erst alle noch ausstehenden Zeilen verarbeiten
        _beschaeftigt = false;
        if (ergebnis.Erfolg) { auswertung.Fertig(); SchreibenBalken.Value = auswertung.Gesamt; }
        Ergebnis(ergebnis, protokollPfad);
    }

    // ---------------------------------------------------------------- 5: Ergebnis

    void Ergebnis(ErstellErgebnis e, string protokollPfad)
    {
        Zeige(Seite.Ergebnis);
        ErgebnisTitel.Text = e.Erfolg ? Texte.ErgebnisErfolg : Texte.ErgebnisFehler;
        ErgebnisTitel.Foreground = e.Erfolg ? Brushes.SeaGreen : Brushes.IndianRed;
        ErgebnisText.Text = e.Meldung;
        ErgebnisProtokoll.Text = protokollPfad.Length > 0 ? string.Format(Texte.ErgebnisProtokoll, protokollPfad) : "";
    }
}
