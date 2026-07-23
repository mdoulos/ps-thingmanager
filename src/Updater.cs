using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace SimpleNotes;

/// <summary>Details of the latest published release on GitHub.</summary>
public record UpdateInfo(Version Version, string Tag, string DownloadUrl, string Notes);

/// <summary>
/// Checks GitHub Releases for a newer version and, if the user agrees,
/// downloads and launches the installer.
///
/// The "repository to update" bridge is a GitHub *Release*: you publish a
/// release whose tag is vX.Y.Z with the installer (SimpleNotesSetup.exe)
/// attached as an asset. This class reads the repo's latest release, compares
/// its version to the running app, and runs the installer to update in place.
///
/// NOTE: this reads the release anonymously, so it requires the repository
/// (its releases) to be public. For a private repo you'd need an authenticated
/// download host instead.
/// </summary>
public static class Updater
{
    private const string Owner = "mdoulos";
    private const string Repo  = "ps-thingmanager";

    public static string ReleasesPageUrl =>
        $"https://github.com/{Owner}/{Repo}/releases/latest";

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    /// <summary>Queries GitHub for the latest release. Returns null if none/parse fails.</summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        using var http = NewClient(TimeSpan.FromSeconds(20));

        string url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
        string json = await http.GetStringAsync(url);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string tag = root.TryGetProperty("tag_name", out var t) ? (t.GetString() ?? "") : "";
        var version = ParseVersion(tag);
        if (version == null)
            return null;

        string notes = root.TryGetProperty("body", out var b) ? (b.GetString() ?? "") : "";

        // Find the installer asset (first *.exe attached to the release).
        string download = "";
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var a in assets.EnumerateArray())
            {
                string name = a.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    a.TryGetProperty("browser_download_url", out var u))
                {
                    download = u.GetString() ?? "";
                    break;
                }
            }
        }

        return new UpdateInfo(version, tag, download, notes);
    }

    /// <summary>True when <paramref name="other"/> is a higher version than the running app.</summary>
    public static bool IsNewer(Version other) => Normalize(other) > Normalize(CurrentVersion);

    /// <summary>Downloads the installer to a temp file and returns its path.</summary>
    public static async Task<string> DownloadInstallerAsync(string url)
    {
        using var http = NewClient(TimeSpan.FromMinutes(5));
        byte[] bytes = await http.GetByteArrayAsync(url);
        string path = Path.Combine(Path.GetTempPath(), "SimpleNotesSetup.exe");
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }

    /// <summary>Launches the downloaded installer and closes the app so it can update.</summary>
    public static void RunInstallerAndExit(string installerPath)
    {
        Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
        System.Windows.Application.Current.Shutdown();
    }

    public static void OpenReleasesPage()
        => Process.Start(new ProcessStartInfo(ReleasesPageUrl) { UseShellExecute = true });

    private static HttpClient NewClient(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        // GitHub's API requires a User-Agent header.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SimpleNotes-Updater");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    private static Version? ParseVersion(string tag)
    {
        tag = tag.TrimStart('v', 'V').Trim();
        return Version.TryParse(tag, out var v) ? v : null;
    }

    // Compare on major.minor.build only (ignore the 4th/revision field and
    // treat unspecified fields as 0) so "1.2" and "1.2.0.0" compare equal.
    private static Version Normalize(Version v)
        => new Version(v.Major, v.Minor, Math.Max(0, v.Build));
}
