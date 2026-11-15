using System.ComponentModel;
using System.Diagnostics;

namespace Ontology.Cli;

internal static class ChromeViewerLauncher
{
    internal static void Open(string htmlPath)
    {
        var url = new Uri(Path.GetFullPath(htmlPath)).AbsoluteUri;
        var executable = OperatingSystem.IsWindows()
            ? new[] { Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86 }
                .Select(folder => Path.Combine(Environment.GetFolderPath(folder), "Google", "Chrome", "Application", "chrome.exe"))
                .FirstOrDefault(File.Exists) ?? "chrome.exe"
            : OperatingSystem.IsMacOS() ? "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" : "google-chrome";
        try
        {
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("--new-tab");
            start.ArgumentList.Add(url);
            using var process = Process.Start(start);
            if (process is null) throw new InvalidOperationException("Chrome did not start.");
            Console.WriteLine("Opened ontology explorer in Chrome. Choose Load ontology.json to select the export.");
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Warning: Could not open Chrome: {exception.Message} Open {htmlPath} manually. The export succeeded.");
        }
    }
}
