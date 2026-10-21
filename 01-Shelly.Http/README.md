# Shelly.Http

Sterowanie inteligentnym gniazdkiem Shelly Plug S Gen3 za pomocą HTTP.

## Cel

Demonstracja bezpośredniego wywołania API urządzenia z aplikacji .NET.

## Wymagania

* .NET SDK
* Shelly Plug S Gen3
* Komputer i Shelly podłączone do tej samej sieci Wi-Fi

## Uruchomienie

Z katalogu Shelly:

```
dotnet run --project 01-Shelly.Http -- http://172.20.10.2
```

Adres IP należy zastąpić rzeczywistym adresem urządzenia Shelly.

## Działanie

Program:

1. Łączy się z urządzeniem przez HTTP.
2. Włącza gniazdko.
3. Czeka 2 sekundy.
4. Wyłącza gniazdko.

Wykorzystywane API:
```
GET /rpc/Switch.Set?id=0&on=true
GET /rpc/Switch.Set?id=0&on=false
```

## Co dalej?

W kolejnym przykładzie, Shelly.ToolCalling, udostępnimy sterowanie lampką modelowi językowemu jako narzędzie.

Model będzie mógł wybrać odpowiednią operację na podstawie polecenia użytkownika.