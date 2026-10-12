using System.Diagnostics;
using SchleimigeScreenshots.Core.Updates;

namespace SchleimigeScreenshots;

internal enum InstallKind { Setup, Portable }

internal sealed record UpdateCheckResult(UpdateInfo? Update, string? Error);

/// <summary>
/// Sucht in den GitHub-Releases nach neueren Versionen und installiert sie: mit Setup-Installation per stillem Setup,
/// bei portabler EXE durch Austausch der Datei. Läuft auf dem UI-Thread (Timer + async/await).
/// </summary>
internal sealed class UpdateService : IDisposable
{
    public const string Repository = "Treaffox/schleimigescreenshots";
    private static readonly Uri DefaultFeed = new($"https://api.github.com/repos/{Repository}/releases?per_page=20");
    private const string DefaultAssetPrefix = $"https://github.com/{Repository}/releases/download/";

    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan MinAgeBetweenChecks = TimeSpan.FromHours(12);

    private readonly Func<AppSettings> _settings;
    private readonly Action _persist;
    private readonly HttpClient _http;
    private readonly UpdateChecker _checker;
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly bool _testMode;
    private bool _checking;
    private bool _started;

    /// <summary>Von einer automatischen (Hintergrund-)Prüfung gefundenes Update; bei manueller Prüfung zeigt der Aufrufer selbst an.</summary>
    public event Action<UpdateInfo>? UpdateFound;

    public UpdateInfo? Pending { get; private set; }

    /// <summary>
    /// Testhilfen (nur per Umgebungsvariablen, nie in den Einstellungen): SCHLEIMIGE_UPDATE_URL ersetzt die Feed-Adresse
    /// (dann entfällt die Beschränkung der Download-Adressen), SCHLEIMIGE_UPDATE_DELAY_SECONDS die Startverzögerung.
    /// </summary>
    public UpdateService(Func<AppSettings> settings, Action persist)
    {
        _settings = settings;
        _persist = persist;

        var feedOverride = Environment.GetEnvironmentVariable("SCHLEIMIGE_UPDATE_URL");
        _testMode = !string.IsNullOrWhiteSpace(feedOverride);
        var feed = _testMode ? new Uri(feedOverride!) : DefaultFeed;

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"SchleimigeScreenshots/{AppVersion.Current}");
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        _checker = new UpdateChecker(_http, feed, _testMode ? null : DefaultAssetPrefix);
        _timer.Tick += async (_, _) => await OnTimerAsync();
    }

    public static InstallKind DetectInstallKind()
    {
        var dir = Path.GetDirectoryName(Environment.ProcessPath);
        return dir is not null && File.Exists(Path.Combine(dir, "unins000.exe")) ? InstallKind.Setup : InstallKind.Portable;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;

        var delay = InitialDelay;
        if (double.TryParse(Environment.GetEnvironmentVariable("SCHLEIMIGE_UPDATE_DELAY_SECONDS"), out var seconds) && seconds >= 0)
            delay = TimeSpan.FromSeconds(seconds);
        _timer.Interval = Math.Max(1, (int)delay.TotalMilliseconds);
        _timer.Start();
    }

    private async Task OnTimerAsync()
    {
        _timer.Interval = (int)CheckInterval.TotalMilliseconds; // ab jetzt regelmäßig
        try
        {
            var s = _settings();
            if (!s.AutoCheckUpdates)
            {
                Log.Info("Automatische Update-Prüfung ist ausgeschaltet");
                return;
            }
            if (_checking) return;
            if (!_testMode && s.LastUpdateCheckUtc is { } last && DateTime.UtcNow - last < MinAgeBetweenChecks)
            {
                Log.Info($"Update-Prüfung übersprungen: letzte Prüfung vor {(DateTime.UtcNow - last).TotalHours:0.0} h (frühestens nach {MinAgeBetweenChecks.TotalHours:0} h)");
                return;
            }

            var result = await CheckAsync();
            if (result.Update is { } update) UpdateFound?.Invoke(update);
        }
        catch (Exception ex)
        {
            Log.Error("Automatische Update-Prüfung", ex);
        }
    }

    /// <summary>Prüft sofort (manuell oder automatisch); Fehler werden als Text zurückgegeben, nicht geworfen.</summary>
    public async Task<UpdateCheckResult> CheckAsync()
    {
        if (_checking) return new UpdateCheckResult(Pending, null);
        _checking = true;
        try
        {
            var s = _settings();
            var update = await _checker.CheckAsync(AppVersion.Current, s.IncludePreReleases);
            s.LastUpdateCheckUtc = DateTime.UtcNow;
            _persist();
            Pending = update;
            Log.Info(update is null
                ? $"Update-Prüfung: {AppVersion.Current} ist aktuell"
                : $"Update-Prüfung: {update.Tag} verfügbar (installiert: {AppVersion.Current})");
            return new UpdateCheckResult(update, null);
        }
        catch (UpdateCheckException ex)
        {
            Log.Error("Update-Prüfung fehlgeschlagen: " + ex.Message);
            return new UpdateCheckResult(null, ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error("Update-Prüfung fehlgeschlagen", ex);
            return new UpdateCheckResult(null, ex.Message);
        }
        finally
        {
            _checking = false;
        }
    }

    /// <summary>
    /// Lädt das Update, prüft es und startet die Installation. Rückgabe null = Installation wurde gestartet, die App muss
    /// sich jetzt beenden; sonst der Fehlertext (die App läuft unverändert weiter).
    /// </summary>
    public async Task<string?> InstallAsync(UpdateInfo update)
    {
        var kind = DetectInstallKind();
        var asset = kind == InstallKind.Setup ? update.Setup : update.Portable;
        if (asset is null)
            return $"Das Release {update.Tag} enthält keine passende Datei für diese Installation ({(kind == InstallKind.Setup ? "Setup" : "portable EXE")}).";
        if (string.IsNullOrEmpty(asset.Sha256))
            return $"Für {asset.Name} liegt keine Prüfsumme vor – aus Sicherheitsgründen wird nicht automatisch installiert.";

        var folder = Path.Combine(Log.Directory, "updates");
        var file = Path.Combine(folder, asset.Name);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            await UpdateDownloader.DownloadAsync(_http, asset, file, cts.Token);
            Log.Info($"Update {update.Tag} heruntergeladen und geprüft: {file}");
        }
        catch (Exception ex)
        {
            Log.Error("Update-Download fehlgeschlagen", ex);
            return "Download fehlgeschlagen: " + (ex is OperationCanceledException ? "Zeitüberschreitung" : ex.Message);
        }

        try
        {
            if (kind == InstallKind.Setup) StartSetup(file);
            else ReplacePortable(file);
        }
        catch (Exception ex)
        {
            Log.Error("Update-Installation fehlgeschlagen", ex);
            return "Installation fehlgeschlagen: " + ex.Message;
        }
        return null;
    }

    /// <summary>Stilles Setup: beendet die App (sie schließt sich vorher selbst), aktualisiert und startet die neue Version.</summary>
    private static void StartSetup(string setupPath)
    {
        var logFile = Path.Combine(Log.Directory, "update-setup.log");
        Process.Start(new ProcessStartInfo(setupPath,
            $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTAPP=1 \"/LOG={logFile}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(setupPath)!,
        });
        Log.Info("Setup gestartet: " + setupPath);
    }

    /// <summary>
    /// Portable EXE: die laufende Datei lässt sich unter Windows umbenennen, aber nicht überschreiben. Also alt → .old,
    /// neu an ihre Stelle (bei Fehler zurück), dann die neue Version starten. Sie räumt .old beim Start auf.
    /// </summary>
    private static void ReplacePortable(string newExe)
    {
        var current = Environment.ProcessPath ?? throw new InvalidOperationException("Pfad der laufenden EXE unbekannt.");
        var staged = current + ".new";
        var old = current + ".old";

        File.Copy(newExe, staged, overwrite: true); // im selben Ordner, damit das Umbenennen sicher funktioniert
        try
        {
            try { File.Delete(old); } catch { /* Rest eines früheren Updates */ }
            File.Move(current, old);
            try
            {
                File.Move(staged, current);
            }
            catch
            {
                File.Move(old, current, overwrite: true); // zurückrollen: die App darf nie ohne EXE dastehen
                throw;
            }
        }
        catch
        {
            try { File.Delete(staged); } catch { }
            throw;
        }

        Process.Start(new ProcessStartInfo(current, "--restarted --updated")
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(current)!,
        });
        Log.Info("Portable EXE ersetzt, neue Version gestartet");
    }

    /// <summary>Räumt Reste früherer Updates auf (best effort).</summary>
    public static void Cleanup()
    {
        try
        {
            if (Environment.ProcessPath is { } exe)
            {
                var old = exe + ".old";
                if (File.Exists(old)) File.Delete(old);
            }
            var folder = Path.Combine(Log.Directory, "updates");
            if (Directory.Exists(folder))
                foreach (var f in Directory.GetFiles(folder))
                    if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-2)) File.Delete(f);
        }
        catch (Exception ex)
        {
            Log.Error("Aufräumen nach Update", ex);
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        _http.Dispose();
    }
}
