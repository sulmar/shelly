var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://localhost:8080");

var app = builder.Build();

var device = new ShellyDevice
{
    // Nazwę można zmienić parametrem --name, np. aby zademonstrować prompt injection.
    Name = builder.Configuration["name"] ?? "Lampka"
};

// Turn on / off
app.MapGet("/rpc/Switch.Set", (int id, bool on) =>
{
    if (id != 0)
        return Results.BadRequest("Invalid switch id.");

    device.Output = on;

    Console.WriteLine($"[Shelly] Light {(on ? "ON" : "OFF")}");

    return Results.Ok(new
    {
        id,
        output = device.Output,
        was_on = !on
    });
});

// Get status
app.MapGet("/rpc/Switch.GetStatus", (int id) =>
{
    if (id != 0)
        return Results.BadRequest("Invalid switch id.");

    return Results.Ok(device.GetStatus());
});

// Get config
app.MapGet("/rpc/Switch.GetConfig", (int id) =>
{
    if (id != 0)
        return Results.BadRequest("Invalid switch id.");

    return Results.Ok(device.GetConfig());
});

Console.WriteLine("Shelly Simulator running at http://localhost:8080");

app.Run();

class ShellyDevice
{
    public bool Output { get; set; }

    public string Name { get; set; } = "";

    public object GetConfig()
    {
        return new
        {
            id = 0,
            name = Name,
            initial_state = "match_input",
            auto_on = false,
            auto_off = false
        };
    }

    public object GetStatus()
    {
        return new
        {
            id = 0,
            output = Output,
            apower = Output ? 8.5 : 0.0,
            voltage = 230.0,
            current = Output ? 0.037 : 0.0,
            aenergy = new
            {
                total = 125.4
            },
            temperature = new
            {
                tC = Output ? 32.5 : 25.0
            }
        };
    }
}