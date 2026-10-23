# Shelly — od HTTP do agentów AI

Przykłady aplikacji w **.NET / C#**, pokazujące kolejne etapy integracji dużych modeli językowych (LLM) z rzeczywistymi urządzeniami.

Na przykładzie inteligentnego gniazdka **Shelly Plug S Gen3** poznajemy mechanizmy Tool Calling, MCP, agentów AI, Human-in-the-loop oraz Code Execution.

**Od prostego wywołania HTTP do agenta, który samodzielnie podejmuje działania w środowisku.**

## Przykłady

Repozytorium zawiera zestaw niezależnych aplikacji demonstracyjnych. Wszystkie korzystają z tego samego urządzenia Shelly lub jego symulatora.

| Projekt | Temat | Opis |
|---|---|---|
| [00-Shelly.Simulator](./00-Shelly.Simulator) | Symulator | Symuluje urządzenie Shelly i udostępnia jego API HTTP. |
| [01-Shelly.Http](./01-Shelly.Http) | HTTP API | Bezpośrednie sterowanie urządzeniem Shelly z aplikacji .NET. |
| [02-Shelly.ToolCalling](./02-Shelly.ToolCalling) | Tool Calling | Model LLM wybiera i wywołuje funkcje sterujące urządzeniem. |
| [03-Shelly.Mcp](./03-Shelly.Mcp) | MCP Server | Udostępnia funkcje sterowania Shelly jako narzędzia protokołu MCP. |
| [04-Shelly.McpClient](./04-Shelly.McpClient) | MCP Client | Model odkrywa i wykorzystuje narzędzia udostępniane przez serwer MCP. |
| [05-Shelly.Agent](./05-Shelly.Agent) | Agent AI | Agent dobiera kolejne działania, obserwuje stan urządzenia i weryfikuje rezultat. |
| [06-Shelly.HumanInTheLoop](./06-Shelly.HumanInTheLoop) | Human-in-the-loop | Operacje zmieniające stan urządzenia wymagają zgody użytkownika. |
| [07-Shelly.CodeExecution](./07-Shelly.CodeExecution) | Code Execution | Agent generuje kod Pythona i wykonuje go w kontenerze Docker. |

### Ścieżka rozwoju

**HTTP → Tool Calling → MCP → Agent AI → Human-in-the-loop → Code Execution**

Każdy kolejny przykład wprowadza nowe możliwości, zachowując wspólny kontekst sterowania lampką Shelly.

## Wymagania

Do uruchomienia przykładów potrzebne są:

- .NET SDK
- [Ollama](https://ollama.com) — lokalne uruchamianie modeli LLM
- Docker — wymagany w przykładzie Code Execution
- Shelly Plug S Gen3 lub symulator dostępny w repozytorium

Przykłady wykorzystujące LLM domyślnie korzystają z modelu `qwen3:8b`.

Pobranie modelu:

```bash
ollama pull qwen3:8b
```

## Szybki start

### 1. Uruchom symulator

```bash
dotnet run --project 00-Shelly.Simulator
```

Symulator udostępnia API Shelly pod adresem:

`http://localhost:8080`

### 2. Sprawdź działanie API

Włączenie lampki:
- Bash
```bash
curl -s "http://localhost:8080/rpc/Switch.Set?id=0&on=true"
```

- PowerShell
```powershell
iwr "http://localhost:8080/rpc/Switch.Set?id=0&on=true"
```

Odczyt stanu:

- Bash
```bash
curl -s "http://localhost:8080/rpc/Switch.GetStatus?id=0"
```

- PowerShell
```powershell
iwr "http://localhost:8080/rpc/Switch.GetStatus?id=0"
```


Wyłączenie lampki:
- Bash
```bash
curl -s "http://localhost:8080/rpc/Switch.Set?id=0&on=false"
```

- PowerShell
```powershell
iwr "http://localhost:8080/rpc/Switch.Set?id=0&on=false"
```

### 3. Uruchom wybrany przykład

Na przykład agenta AI:

```bash
dotnet run --project 05-Shelly.Agent -- http://localhost:8080
```

Przykładowe polecenie:

> Upewnij się, że lampka jest włączona.

Agent może odczytać stan urządzenia, włączyć lampkę, jeśli jest wyłączona, i sprawdzić rezultat.

## Rzeczywiste urządzenie

Zamiast symulatora można wykorzystać Shelly Plug S Gen3 dostępne w sieci lokalnej.

Wystarczy przekazać adres urządzenia jako argument aplikacji:

```bash
dotnet run --project 05-Shelly.Agent -- http://172.20.10.2
```

Adres IP należy dostosować do własnej sieci.

## Technologie

- **.NET / C#** — implementacja aplikacji i narzędzi
- **Microsoft.Extensions.AI** — integracja z modelami LLM i Tool Calling
- **Ollama** — lokalne uruchamianie modeli
- **Model Context Protocol (MCP)** — udostępnianie i odkrywanie narzędzi
- **Testcontainers / Docker** — wykonywanie kodu wygenerowanego przez model
- **Shelly RPC API** — komunikacja z urządzeniem

## Bezpieczeństwo

Przykłady mają charakter edukacyjny.

Modele językowe mogą popełniać błędy, wybierać niewłaściwe narzędzia lub generować niepoprawny kod.

W szczególności:

- Nie należy zakładać, że odpowiedź modelu potwierdza rzeczywiste wykonanie operacji.
- Działania zmieniające stan urządzeń powinny podlegać kontroli uprawnień (zob. [06-Shelly.HumanInTheLoop](./06-Shelly.HumanInTheLoop)).
- Kod generowany przez LLM należy traktować jako niezaufany.
- Kontener Docker z przykładu Code Execution nie stanowi w pełni zabezpieczonego środowiska uruchomieniowego.

## Cel edukacyjny

Repozytorium pokazuje, że budowanie agentów AI nie sprowadza się do wysyłania zapytań do modelu językowego.

Istotne jest projektowanie narzędzi, komunikacja z otoczeniem, obserwacja rezultatów oraz kontrolowanie zachowania systemu.

> **Tool Calling jest mechanizmem. Agentowość wynika ze sposobu wykorzystania narzędzi do realizacji celu.**

## Autor
© Marcin Sulecki 

## Licencja

Materiały dydaktyczne w tym repozytorium są udostępniane na licencji Creative Commons Attribution 4.0 International (CC BY 4.0).

Kod źródłowy przykładów jest udostępniany na licencji MIT.

Materiały pochodzące z zewnętrznych źródeł podlegają licencjom i prawom ich odpowiednich autorów.
