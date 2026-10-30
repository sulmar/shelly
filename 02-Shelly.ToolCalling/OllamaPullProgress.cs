using OllamaSharp.Models;
using System.Diagnostics;
using System.Globalization;

// Wyświetla postęp pobierania modelu w stylu `ollama pull`:
//
//   pulling manifest
//   pulling 6e4c38e1172f:  38% ▕███████                 ▏ 1.2 GB/3.2 GB     23 MB/s  1m27s
//
// Report jest wywoływany synchronicznie z pętli pobierania, więc linie są rysowane w kolejności komunikatów.
// (Wbudowana klasa Progress<T> wywołuje callback asynchronicznie na puli wątków, co mogłoby pomieszać kolejność rysowania).
class OllamaPullProgress : IProgress<PullModelResponse>
{
    // Szerokość kolumny ze statystykami (ze spacją przed nią), stała, aby pasek nie zmieniał długości przy każdym odświeżeniu.
    private const int StatsWidth = 35;

    // Etap pobierania: warstwa z paskiem (ma digest) albo zwykły komunikat, np. "pulling manifest", "verifying sha256 digest", "success".
    private sealed class Step
    {
        public required string Status { get; init; }
        public required bool IsLayer { get; init; }
        public long Completed { get; set; }
        public long Total { get; set; }

        // Punkt odniesienia do liczenia prędkości. Wznowione pobieranie startuje od już pobranej części warstwy.
        public long InitialCompleted { get; init; }
        public Stopwatch Elapsed { get; } = Stopwatch.StartNew();
    }

    // Etapy w kolejności pojawienia się w strumieniu. Status jest ich kluczem, bo Ollama powtarza go przy każdej aktualizacji warstwy.
    private readonly List<Step> _steps = [];

    // Liczba linii aktualnie narysowanych na ekranie (o tyle cofamy kursor przy odświeżaniu).
    private int _drawn;

    public void Report(PullModelResponse progress)
    {
        var isLayer = !string.IsNullOrEmpty(progress.Digest);

        // Ollama nie zawsze wysyła końcowy postęp warstwy, więc po przejściu do kolejnego etapu oznaczamy warstwy jako ukończone.
        if (!isLayer)
        {
            foreach (var layer in _steps.Where(s => s.IsLayer))
                layer.Completed = layer.Total;
        }

        var step = _steps.Find(s => s.Status == progress.Status);

        if (step is null)
        {
            step = new Step { Status = progress.Status ?? "", IsLayer = isLayer, InitialCompleted = progress.Completed };
            _steps.Add(step);
        }

        if (isLayer)
        {
            step.Completed = progress.Completed;
            step.Total = progress.Total;
        }

        Redraw();
    }

    // Cofamy kursor na początek bloku (ANSI: ESC[nA) i rysujemy wszystkie linie od nowa, czyszcząc każdą (ESC[2K).
    private void Redraw()
    {
        if (_drawn > 0)
            Console.Write($"\x1b[{_drawn}A");

        // Linia dłuższa niż terminal zawinęłaby się i cofanie kursora o _drawn linii przestałoby się zgadzać.
        var width = Console.IsOutputRedirected || Console.WindowWidth <= 0 ? 80 : Console.WindowWidth;
        var maxLength = Math.Max(width - 1, 1);

        foreach (var step in _steps)
        {
            var line = step.IsLayer ? FormatLayer(step, width) : step.Status;

            Console.WriteLine($"\x1b[2K{(line.Length > maxLength ? line[..maxLength] : line)}");
        }

        _drawn = _steps.Count;
    }

    // Linia warstwy: pulling 6e4c38e1172f:  38% ▕███████       ▏ 1.2 GB/3.2 GB     23 MB/s  1m27s
    private static string FormatLayer(Step step, int width)
    {
        var fraction = step.Total > 0 ? Math.Clamp((double)step.Completed / step.Total, 0, 1) : 0;

        var prefix = $"{step.Status}: {(int)(fraction * 100),3}% ";
        var barWidth = Math.Max(width - 1 - prefix.Length - StatsWidth - 2, 10);
        var filled = (int)(barWidth * fraction);

        return $"{prefix}▕{new string('█', filled)}{new string(' ', barWidth - filled)}▏ {FormatStats(step)}";
    }

    // W trakcie pobierania: rozmiar, prędkość i pozostały czas. Po zakończeniu sam rozmiar warstwy.
    private static string FormatStats(Step step)
    {
        if (step.Total <= 0)
            return "";

        if (step.Completed >= step.Total)
            return FormatSize(step.Total);

        var size = $"{FormatSize(step.Completed)}/{FormatSize(step.Total)}";
        var seconds = step.Elapsed.Elapsed.TotalSeconds;
        var rate = seconds > 0 ? (step.Completed - step.InitialCompleted) / seconds : 0;

        if (rate <= 0)
            return size;

        var remaining = TimeSpan.FromSeconds((step.Total - step.Completed) / rate);

        return $"{size,-17} {FormatSize((long)rate) + "/s",9} {FormatDuration(remaining),6}";
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1_000_000_000 => $"{(bytes / 1_000_000_000.0).ToString("0.0", CultureInfo.InvariantCulture)} GB",
        >= 1_000_000 => $"{bytes / 1_000_000} MB",
        >= 1_000 => $"{bytes / 1_000} KB",
        _ => $"{bytes} B"
    };

    private static string FormatDuration(TimeSpan time) => time switch
    {
        { TotalHours: >= 1 } => $"{(int)time.TotalHours}h{time.Minutes}m",
        { TotalMinutes: >= 1 } => $"{time.Minutes}m{time.Seconds}s",
        _ => $"{time.Seconds}s"
    };
}
