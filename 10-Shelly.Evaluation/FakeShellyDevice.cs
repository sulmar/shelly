using System.Net;
using System.Net.Http.Json;
using System.Web;

namespace Shelly.Evaluation;

// Urządzenie Shelly w pamięci testu.
// Agent wywołuje to samo API HTTP co w symulatorze, ale test może ustawić stan początkowy
// i sprawdzić stan końcowy bez uruchamiania dodatkowych procesów.
public sealed class FakeShellyDevice : HttpMessageHandler
{
    public bool Output { get; set; }

    public HttpClient CreateClient() => new(this)
    {
        BaseAddress = new Uri("http://shelly.test")
    };

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var query = HttpUtility.ParseQueryString(request.RequestUri.Query);

        object? body = path switch
        {
            "/rpc/Switch.Set" => Set(bool.Parse(query["on"]!)),
            "/rpc/Switch.GetStatus" => GetStatus(),
            _ => null
        };

        var response = body is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

        return Task.FromResult(response);
    }

    private object Set(bool on)
    {
        var wasOn = Output;
        Output = on;

        return new { id = 0, was_on = wasOn };
    }

    private object GetStatus() => new
    {
        id = 0,
        output = Output,
        apower = Output ? 8.5 : 0.0,
        voltage = 230.0
    };
}
