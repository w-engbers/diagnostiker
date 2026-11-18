namespace Diagnostiker.StickGui;

/// <summary>Alle Texte der Oberfläche an einer Stelle (Grundlage für spätere Übersetzungen).</summary>
static class Texte
{
    public const string Titel = "Diagnostiker – Stick-Manager";
    public const string UntertitelPaket = "Schritt 1 von 4: Paket laden und prüfen";
    public const string UntertitelStick = "Schritt 2 von 4: Stick wählen";
    public const string UntertitelBestaetigen = "Schritt 3 von 4: Bestätigen";
    public const string UntertitelSchreiben = "Schritt 4 von 4: Stick wird erstellt";
    public const string UntertitelErgebnis = "Ergebnis";

    public const string PaketSuche = "Neueste Version wird gesucht …";
    public const string PaketLaedt = "Paket wird geladen und geprüft …";
    public const string PaketOk = "Version {0}: Die Signatur des Herausgebers ist in Ordnung.";
    public const string PaketFehler = "Das Paket lässt sich nicht laden oder ist nicht in Ordnung:";
    public const string PaketOrdnerWaehlen = "Paket aus Ordner wählen …";
    public const string PaketNochmal = "Erneut versuchen";
    public const string PaketOrdnerTitel = "Ordner mit manifest.json wählen";

    public const string StickErklaerung = "Gesteckte USB-Sticks erscheinen hier von selbst. Angeboten werden nur USB-Sticks von 4 bis 512 GiB, die das System nicht benutzt.";
    public const string StickKeiner = "Kein geeigneter USB-Stick gefunden. Bitte einen Stick einstecken.";
    public const string StickWeiter = "Weiter";
    public const string StickPruefung = "Dieser Stick wird für den Diagnostiker vollständig überschrieben.";

    public const string BestaetigenWarnung = "ALLES auf {0} wird gelöscht und lässt sich nicht wiederherstellen.";
    public const string BestaetigenFrage = "Zur Bestätigung den Gerätenamen eingeben ({0}):";
    public const string BestaetigenStart = "Stick jetzt erstellen";
    public const string Zurueck = "Zurück";

    public const string SchreibenPasswort = "Bitte das Passwort eingeben, wenn ein Fenster danach fragt. Den Stick nicht abziehen.";
    public const string SchreibenLaeuft = "Der Stick wird beschrieben. Das kann je nach Stick einige Minuten dauern, auch wenn die Anzeige zwischendurch kaum weiterrückt. Bitte den Stick nicht abziehen.";
    public const string SchreibenDetails = "Einzelheiten";

    public const string ErgebnisErfolg = "Der Stick ist fertig.";
    public const string ErgebnisFehler = "Der Stick konnte nicht erstellt werden.";
    public const string ErgebnisNochEiner = "Weiteren Stick erstellen";
    public const string ErgebnisSchliessen = "Schließen";
    public const string ErgebnisProtokoll = "Protokoll: {0}";

    public const string SchliessenGesperrt = "Der Stick wird gerade geschrieben. Bitte warten, bis der Vorgang beendet ist.";
}
