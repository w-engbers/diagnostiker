using System.Text.Json.Serialization;

namespace Diagnostiker.Kern;

/// <summary>Bewertung eines Schritts oder Moduls. Reihenfolge = Schweregrad (höher = schlimmer).</summary>
public enum Status
{
    Info = 0,
    Ok = 1,
    Uebersprungen = 2,
    Warnung = 3,
    Fehler = 4,
}

/// <summary>Ein Prüfmodul, geladen aus einer JSON-Manifestdatei in /module.</summary>
public sealed class ModulDefinition
{
    public string Id { get; set; } = "";
    public string Titel { get; set; } = "";
    public string Gruppe { get; set; } = "Allgemein";
    public string Beschreibung { get; set; } = "";

    /// <summary>Gehört zur "Schnellprüfung" (harmlos, nur lesend, schnell).</summary>
    public bool Schnellpruefung { get; set; }

    /// <summary>Verändert Daten auf dem Prüfling. Wird nur nach ausdrücklicher Bestätigung ausgeführt.</summary>
    public bool Schreibend { get; set; }

    public List<ParameterDefinition> Parameter { get; set; } = [];
    public List<SchrittDefinition> Schritte { get; set; } = [];
}

/// <summary>Eine Frage an den Bediener; der Wert ersetzt {name} in Befehl und Argumenten.</summary>
public sealed class ParameterDefinition
{
    public string Name { get; set; } = "";
    public string Frage { get; set; } = "";
    public string Standard { get; set; } = "";
}

public sealed class SchrittDefinition
{
    public string Titel { get; set; } = "";

    /// <summary>"windows" oder "linux"; leer = beide.</summary>
    public string Os { get; set; } = "";

    /// <summary>Programm. Platzhalter {werkzeuge} = Verzeichnis der mitgelieferten Tools für das aktuelle OS.</summary>
    public string Befehl { get; set; } = "";

    public List<string> Argumente { get; set; } = [];
    public bool Admin { get; set; }
    public int TimeoutSekunden { get; set; } = 60;

    /// <summary>"oem" (Windows-Konsolenprogramme, Standard unter Windows) oder "utf8".</summary>
    public string Kodierung { get; set; } = "";

    /// <summary>Wird als Hinweis angezeigt, wenn der Schritt nicht ausführbar ist (Tool fehlt, keine Rechte).</summary>
    public string Hinweis { get; set; } = "";

    /// <summary>Ausgabezeilen unverändert als Ergebnis zeigen (für Inventar-/Informationsschritte).</summary>
    public bool ZeigeAusgabe { get; set; }

    public List<Regel> Regeln { get; set; } = [];
}

public sealed class Regel
{
    /// <summary>enthaelt | regex | exitcode | exitcodeNicht</summary>
    public string Typ { get; set; } = "enthaelt";
    public string Wert { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Status Status { get; set; } = Status.Info;

    public string Meldung { get; set; } = "";
}

public sealed class SchrittErgebnis
{
    public string Titel { get; set; } = "";
    public Status Status { get; set; } = Status.Info;
    public List<string> Meldungen { get; set; } = [];
    public string Befehlszeile { get; set; } = "";
    public int? ExitCode { get; set; }
    public string Ausgabe { get; set; } = "";
    public double Sekunden { get; set; }
}

public sealed class ModulErgebnis
{
    public string Id { get; set; } = "";
    public string Titel { get; set; } = "";
    public DateTime Beginn { get; set; }
    public Status Status { get; set; } = Status.Info;
    public List<SchrittErgebnis> Schritte { get; set; } = [];
}
