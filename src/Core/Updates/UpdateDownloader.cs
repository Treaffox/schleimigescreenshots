using System.Security.Cryptography;

namespace SchleimigeScreenshots.Core.Updates;

public static class UpdateDownloader
{
    /// <summary>
    /// Lädt ein Release-Asset und prüft SHA-256 (und, falls bekannt, die Größe), bevor die Datei ihren endgültigen Namen
    /// bekommt. Bei Abweichung wird nichts zurückgelassen und <see cref="InvalidDataException"/> geworfen.
    /// </summary>
    public static async Task DownloadAsync(HttpClient http, ReleaseAsset asset, string destinationPath,
        CancellationToken ct = default, IProgress<double>? progress = null)
    {
        if (string.IsNullOrEmpty(asset.Sha256))
            throw new InvalidOperationException($"Für {asset.Name} gibt es keinen Prüfwert - es wird nicht automatisch installiert.");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        var partial = destinationPath + ".partial";
        try
        {
            using var response = await http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? asset.Size;

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long received = 0;
            await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    received += read;
                    if (total > 0) progress?.Report((double)received / total);
                }
            }

            if (asset.Size > 0 && received != asset.Size)
                throw new InvalidDataException($"Download unvollständig: {received} von {asset.Size} Bytes.");

            var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Prüfsumme stimmt nicht überein (erwartet {asset.Sha256[..12]}…, erhalten {actual[..12]}…).");

            File.Move(partial, destinationPath, overwrite: true);
        }
        catch
        {
            try { File.Delete(partial); } catch { /* Aufräumen darf nicht den eigentlichen Fehler verdecken */ }
            throw;
        }
    }
}
