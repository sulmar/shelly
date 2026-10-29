using OllamaSharp.Models;

// Wyświetla postęp pobierania modelu w stylu `docker pull`: jedna odświeżana linia z paskiem na każdą warstwę.
// Report jest wywoływany synchronicznie z pętli pobierania, więc linie są rysowane w kolejności komunikatów.
// (Wbudowana klasa Progress<T> wywołuje callback asynchronicznie na puli wątków, co mogłoby pomieszać kolejność rysowania).
class DockerPullProgress : IProgress<PullModelResponse>
{
    // Warstwy w kolejności pojawienia się w strumieniu i ich aktualne linie postępu.
    private readonly List<string> _layers = [];
    private readonly Dictionary<string, string> _lines = [];

    // Liczba linii warstw aktualnie narysowanych na ekranie (o tyle cofamy kursor przy odświeżaniu).
    private int _drawn;

    public void Report(PullModelResponse progress)
    {
        // Etapy bez warstwy (np. "pulling manifest", "verifying sha256 digest", "success") wypisujemy zwykłą linią pod paskami.
        if (string.IsNullOrEmpty(progress.Digest))
        {
            // Ollama nie zawsze wysyła końcowy postęp warstwy, więc po zakończeniu pobierania oznaczamy wszystkie jako ukończone.
            foreach (var layer in _layers)
                _lines[layer] = $"{layer}: Pull complete";

            Redraw();

            Console.WriteLine(progress.Status);
            _layers.Clear();
            _lines.Clear();
            _drawn = 0;
            return;
        }

        // Digest ma postać "sha256:<hash>", jak Docker pokazujemy pierwsze 12 znaków hasha.
        var id = progress.Digest.Replace("sha256:", "")[..12];

        if (!_lines.ContainsKey(id))
            _layers.Add(id);

        _lines[id] = progress.Total > 0 && progress.Completed >= progress.Total
            ? $"{id}: Pull complete"
            : $"{id}: Downloading {ProgressBar(progress.Completed, progress.Total)} {FormatSize(progress.Completed)}/{FormatSize(progress.Total)}";

        Redraw();
    }

    // Cofamy kursor na początek bloku warstw (ANSI: ESC[nA) i rysujemy wszystkie linie od nowa, czyszcząc każdą (ESC[2K).
    private void Redraw()
    {
        if (_drawn > 0)
            Console.Write($"\x1b[{_drawn}A");

        foreach (var layer in _layers)
            Console.WriteLine($"\x1b[2K{_lines[layer]}");

        _drawn = _layers.Count;
    }

    // Pasek w stylu Dockera: [=========>                    ]
    private static string ProgressBar(long completed, long total, int width = 40)
    {
        var filled = total > 0 ? (int)(width * completed / total) : 0;

        return filled > 0
            ? $"[{new string('=', filled - 1)}>{new string(' ', width - filled)}]"
            : $"[{new string(' ', width)}]";
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1_000_000_000 => $"{bytes / 1_000_000_000.0:0.00}GB",
        >= 1_000_000 => $"{bytes / 1_000_000.0:0.0}MB",
        >= 1_000 => $"{bytes / 1_000.0:0.0}kB",
        _ => $"{bytes}B"
    };
}
