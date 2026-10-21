if (args.Length == 0)
{
    Console.WriteLine("Usage: Shelly.Http <url>");
    return;
}

var shellyUrl = args[0];

Console.WriteLine($"Connecting to Shelly at {shellyUrl}");

using var http = new HttpClient
{
    BaseAddress = new Uri(shellyUrl)
};

// Turn on
Console.WriteLine("Turning light on...");

var response = await http.GetAsync("/rpc/Switch.Set?id=0&on=true");
response.EnsureSuccessStatusCode();

Console.WriteLine("Light is on.");

await Task.Delay(2000);

// Turn off
Console.WriteLine("Turning light off...");

response = await http.GetAsync("/rpc/Switch.Set?id=0&on=false");
response.EnsureSuccessStatusCode();

Console.WriteLine("Light is off.");