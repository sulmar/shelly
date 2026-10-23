# Shelly.Simulator

Prosty symulator **Shelly Plug S Gen3** udostępniający wybrane endpointy HTTP RPC.

Umożliwia uruchamianie projektów 01–13 bez fizycznego urządzenia Shelly.

## Uruchomienie

Z katalogu głównego `Shelly`:

```bash
dotnet run --project 00-Shelly.Simulator
```

Symulator działa pod adresem:

```
http://localhost:8080
```

## Dostępne operacje

| Operacja      | Endpoint                        |
|---------------|---------------------------------|
| Włącz lampkę  | `/rpc/Switch.Set?id=0&on=true`  |
| Wyłącz lampkę | `/rpc/Switch.Set?id=0&on=false` |
| Odczytaj stan | `/rpc/Switch.GetStatus?id=0`    |
| Odczytaj konfigurację | `/rpc/Switch.GetConfig?id=0` |

## Przykłady użycia

### Włączenie lampki

```bash
curl "http://localhost:8080/rpc/Switch.Set?id=0&on=true"
```

### Wyłączenie lampki

```bash
curl "http://localhost:8080/rpc/Switch.Set?id=0&on=false"
```

### Odczyt stanu

```bash
curl -s "http://localhost:8080/rpc/Switch.GetStatus?id=0" | jq
```

Przykładowa odpowiedź:

```json
{
  "id": 0,
  "output": true,
  "apower": 8.5,
  "voltage": 230,
  "current": 0.037,
  "aenergy": {
    "total": 125.4
  },
  "temperature": {
    "tC": 32.5
  }
}
```

### Odczyt konfiguracji

```bash
curl -s "http://localhost:8080/rpc/Switch.GetConfig?id=0" | jq
```

Nazwę urządzenia zwracaną w polu `name` można ustawić parametrem `--name` (domyślnie `Lampka`):

```bash
dotnet run --project 00-Shelly.Simulator -- --name "Lampka w salonie"
```

### Odczyt wybranych parametrów

```bash
# Stan lampki
curl -s "http://localhost:8080/rpc/Switch.GetStatus?id=0" | jq '.output'

# Moc chwilowa [W]
curl -s "http://localhost:8080/rpc/Switch.GetStatus?id=0" | jq '.apower'

# Temperatura [°C]
curl -s "http://localhost:8080/rpc/Switch.GetStatus?id=0" | jq '.temperature.tC'
```

## Integracja z pozostałymi projektami

Uruchom symulator w osobnym terminalu, a następnie wybrany projekt:

### 01 — HTTP

```bash
dotnet run --project 01-Shelly.Http -- http://localhost:8080
```

### 02 — Tool Calling

```bash
dotnet run --project 02-Shelly.ToolCalling -- http://localhost:8080
```

### 03 — MCP Server (test przez MCP Inspector)

```bash
npx @modelcontextprotocol/inspector \
  dotnet run --project 03-Shelly.Mcp -- http://localhost:8080
```

### 04 — MCP Client + LLM

```bash
dotnet run --project 04-Shelly.McpClient -- http://localhost:8080
```

### 05 — Agent

```bash
dotnet run --project 05-Shelly.Agent -- http://localhost:8080
```

### 06 — Human-in-the-loop

```bash
dotnet run --project 06-Shelly.HumanInTheLoop -- http://localhost:8080
```

### 07 — Code Execution

```bash
dotnet run --project 07-Shelly.CodeExecution -- http://localhost:8080
```

### 09 — Observability

```bash
dotnet run --project 09-Shelly.Observability -- http://localhost:8080
```

### 11 — A2A

```bash
dotnet run --project 11-Shelly.A2A/Shelly.A2A.Server -- http://localhost:8080
dotnet run --project 11-Shelly.A2A/Shelly.A2A.Client
```

### 12 — Structured Output

```bash
dotnet run --project 12-Shelly.StructuredOutput -- http://localhost:8080
```

### 13 — Skills

```bash
dotnet run --project 13-Shelly.Skills -- http://localhost:8080
```

> **Uwaga:** Projekty 02, 04 i 12 wymagają uruchomionej [Ollama](https://ollama.com) z modelem `qwen2.5:3b`, a projekty 05, 06, 07, 09, 11 i 13 — z modelem `qwen3:8b`. Projekty 07 i 09 wymagają dodatkowo uruchomionego Dockera.

## Symulator a rzeczywiste Shelly

Wystarczy zmienić adres urządzenia:

```bash
# Symulator
dotnet run --project 04-Shelly.McpClient -- http://localhost:8080

# Fizyczne Shelly (przykładowy adres IP)
dotnet run --project 04-Shelly.McpClient -- http://172.20.10.2
```

## Ograniczenia

- Stan urządzenia przechowywany jest w pamięci procesu.
- Parametry elektryczne i temperatura mają przykładowe wartości.
- Energia nie jest naliczana w czasie.
- Obsługiwany jest jeden przełącznik (`id=0`).
- Symulator implementuje tylko wybrane endpointy Shelly RPC.
