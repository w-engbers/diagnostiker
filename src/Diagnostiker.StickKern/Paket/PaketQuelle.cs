using System.Net;
using System.Net.Http.Headers;

namespace Diagnostiker.StickKern.Paket;

/// <summary>Woher die Dateien eines Pakets kommen (Ordner oder Webadresse).</summary>
public interface IPaketQuelle
{
    string Beschreibung { get; }
    Task<byte[]> LesenAsync(string name, CancellationToken ct);

    /// <summary>Holt die Datei nach <paramref name="ziel"/>. Ist <paramref name="ziel"/> bereits teilweise vorhanden, darf fortgesetzt werden.
    /// <paramref name="fortschritt"/> bekommt die bisher vorhandenen Bytes.</summary>
    Task HerunterladenAsync(string name, string ziel, long erwarteteGroesse, Action<long>? fortschritt, CancellationToken ct);

    /// <summary>Nach dem Lesen des Manifests: auf genau diese Version festlegen (bei „neueste Version“ sonst könnte zwischen den
    /// Downloads ein neues Release erscheinen).</summary>
    IPaketQuelle FuerVersion(string version);
}

/// <summary>Ein lokaler Ordner mit den Paketdateien (zum Testen und zum Weitergeben per Stick oder Netzlaufwerk).</summary>
public sealed class OrdnerQuelle(string ordner) : IPaketQuelle
{
    public string Beschreibung => ordner;
    string Pfad(string name) => Path.Combine(ordner, name);

    public async Task<byte[]> LesenAsync(string name, CancellationToken ct)
    {
        if (!File.Exists(Pfad(name))) throw new PaketException($"{name} fehlt in {ordner}.");
        return await File.ReadAllBytesAsync(Pfad(name), ct);
    }

    public async Task HerunterladenAsync(string name, string ziel, long erwarteteGroesse, Action<long>? fortschritt, CancellationToken ct)
    {
        if (!File.Exists(Pfad(name))) throw new PaketException($"{name} fehlt in {ordner}.");
        await using var ein = File.OpenRead(Pfad(name));
        await using var aus = File.Create(ziel);
        var puffer = new byte[1 << 20]; long summe = 0; int n;
        while ((n = await ein.ReadAsync(puffer, ct)) > 0) { await aus.WriteAsync(puffer.AsMemory(0, n), ct); summe += n; fortschritt?.Invoke(summe); }
    }

    public IPaketQuelle FuerVersion(string version) => this;
}

/// <summary>
/// Eine Webadresse, unter der die Dateien liegen (bei GitHub: <c>https://github.com/&lt;besitzer&gt;/&lt;repo&gt;/releases/latest/download</c>
/// oder <c>…/releases/download/v&lt;version&gt;</c>). Nur HTTPS. Es werden keine Angaben über den Benutzer gesendet.
/// </summary>
public sealed class HttpQuelle : IPaketQuelle
{
    public const string GitHubNeueste = "https://github.com/{0}/releases/latest/download";
    public const string GitHubVersion = "https://github.com/{0}/releases/download/v{1}";

    readonly string _basis;
    readonly string? _repo;
    readonly HttpClient _http;

    public HttpQuelle(string basis, HttpClient? http = null, string? githubRepo = null)
    {
        if (!basis.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) throw new PaketException("Pakete werden nur über HTTPS geladen.");
        _basis = basis.TrimEnd('/');
        _repo = githubRepo;
        _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        if (!_http.DefaultRequestHeaders.UserAgent.Any()) _http.DefaultRequestHeaders.UserAgent.ParseAdd("Diagnostiker-Stick-Manager");
    }

    /// <summary>Die neueste Version des GitHub-Repos <c>besitzer/repo</c>.</summary>
    public static HttpQuelle GitHub(string repo, HttpClient? http = null) =>
        new(string.Format(GitHubNeueste, repo), http, repo);

    public string Beschreibung => _basis;

    public IPaketQuelle FuerVersion(string version) =>
        _repo is null ? this : new HttpQuelle(string.Format(GitHubVersion, _repo, version), _http, null);

    public async Task<byte[]> LesenAsync(string name, CancellationToken ct)
    {
        using var antwort = await _http.GetAsync($"{_basis}/{name}", HttpCompletionOption.ResponseContentRead, ct);
        if (antwort.StatusCode == HttpStatusCode.NotFound) throw new PaketException($"{name} wurde unter {_basis} nicht gefunden.");
        antwort.EnsureSuccessStatusCode();
        var bytes = await antwort.Content.ReadAsByteArrayAsync(ct);
        if (bytes.Length > 1 << 20) throw new PaketException($"{name} ist ungewöhnlich groß.");
        return bytes;
    }

    public async Task HerunterladenAsync(string name, string ziel, long erwarteteGroesse, Action<long>? fortschritt, CancellationToken ct)
    {
        var vorhanden = File.Exists(ziel) ? new FileInfo(ziel).Length : 0;
        if (vorhanden > erwarteteGroesse) { File.Delete(ziel); vorhanden = 0; }
        using var anfrage = new HttpRequestMessage(HttpMethod.Get, $"{_basis}/{name}");
        if (vorhanden > 0) anfrage.Headers.Range = new RangeHeaderValue(vorhanden, null);
        using var antwort = await _http.SendAsync(anfrage, HttpCompletionOption.ResponseHeadersRead, ct);
        if (antwort.StatusCode == HttpStatusCode.NotFound) throw new PaketException($"{name} wurde unter {_basis} nicht gefunden.");
        if (antwort.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) { File.Delete(ziel); vorhanden = 0; throw new PaketException("Der Server kann den Download nicht fortsetzen. Bitte erneut starten."); }
        antwort.EnsureSuccessStatusCode();
        var anhaengen = vorhanden > 0 && antwort.StatusCode == HttpStatusCode.PartialContent;
        if (!anhaengen) vorhanden = 0;
        await using var ein = await antwort.Content.ReadAsStreamAsync(ct);
        await using var aus = new FileStream(ziel, anhaengen ? FileMode.Append : FileMode.Create, FileAccess.Write);
        var puffer = new byte[1 << 20]; var summe = vorhanden; int n;
        while ((n = await ein.ReadAsync(puffer, ct)) > 0)
        {
            await aus.WriteAsync(puffer.AsMemory(0, n), ct);
            summe += n; fortschritt?.Invoke(summe);
            if (summe > erwarteteGroesse) throw new PaketException($"{name} ist größer als im Manifest angegeben.");
        }
    }
}
