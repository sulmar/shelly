using System.Text;
using DotNet.Testcontainers.Builders;

public static class PythonExecutor
{
    public static async Task<string> ExecuteAsync(string code)
    {
        // Kontener nie ma dostępu do sieci, ma ograniczone zasoby i jest usuwany po wykonaniu kodu.
        // To podstawowa izolacja na potrzeby demonstracji. Nie należy traktować jej jako pełnego zabezpieczenia przed złośliwym kodem.
        await using var container = new ContainerBuilder("python:3.13-slim")
            .WithEntrypoint("sleep", "infinity")
            .WithCreateParameterModifier(config =>
            {
                config.HostConfig.NetworkMode = "none";
                config.HostConfig.Memory = 256L * 1024 * 1024;
                config.HostConfig.NanoCPUs = 1_000_000_000;
                config.HostConfig.CapDrop = ["ALL"];
                config.HostConfig.SecurityOpt =
                    ["no-new-privileges:true"];
                config.HostConfig.PidsLimit = 64;
            })
            .Build();

        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(15));

        try
        {
            await container.StartAsync(timeout.Token);

            await container.CopyAsync(
                Encoding.UTF8.GetBytes(code),
                "/tmp/script.py",
                ct: timeout.Token);

            var result = await container.ExecAsync(
                ["python", "-I", "/tmp/script.py"],
                timeout.Token);

            if (result.ExitCode != 0)
            {
                return $"Execution failed:\n{result.Stderr}";
            }

            return result.Stdout;
        }
        catch (OperationCanceledException)
        {
            return "Execution timed out.";
        }
    }
}