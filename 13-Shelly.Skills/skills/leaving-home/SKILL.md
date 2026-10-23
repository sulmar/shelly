---
name: leaving-home
description: Procedura „wychodzę z domu” dla lampki Shelly. Użyj, gdy użytkownik wychodzi z domu, idzie spać, wyjeżdża albo prosi, żeby „wszystko wyłączyć” i upewnić się, że nic nie zostało włączone.
---

# Wychodzę z domu

Celem procedury jest pewność, że lampka jest wyłączona i nie pobiera prądu. Użytkownik musi dostać potwierdzenie oparte na odczycie z urządzenia, a nie na założeniu.

## Kroki

1. Odczytaj aktualny stan lampki narzędziem `get_light_status`.
2. Jeśli lampka jest włączona (`output: true`), wyłącz ją narzędziem `turn_off_light`.
3. Jeśli lampka już jest wyłączona, nie wywołuj `turn_off_light`.
4. Zawsze odczytaj stan ponownie narzędziem `get_light_status`, nawet jeśli nic nie zmieniałeś.
5. Sprawdź oba warunki:
   - `output` ma wartość `false`,
   - `apower` wynosi `0`.

## Odpowiedź

Odpowiedz jednym z dwóch komunikatów:

- Gdy oba warunki są spełnione:

  `✅ Możesz spokojnie wyjść. Lampka jest wyłączona i nie pobiera prądu (0 W).`

  Jeśli lampka była włączona i ją wyłączyłeś, dopisz: `Lampka była włączona — wyłączyłem ją.`

- Gdy którykolwiek warunek nie jest spełniony:

  `⚠️ Uwaga! Lampka nadal pobiera prąd (<apower> W). Sprawdź urządzenie przed wyjściem.`

## Zasady

- Nigdy nie włączaj lampki w ramach tej procedury.
- Nie twierdź, że lampka jest wyłączona, bez ponownego odczytu stanu.
