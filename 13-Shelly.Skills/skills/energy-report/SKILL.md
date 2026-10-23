---
name: energy-report
description: Przygotowuje raport zużycia energii i kosztu świecenia lampki Shelly, także w taryfach G11 i G12. Użyj, gdy użytkownik pyta o koszt, zużycie prądu, rachunek za energię albo porównanie taryf.
---

# Raport energii

## Kroki

1. Odczytaj aktualny pobór mocy narzędziem `get_light_status` (pole `apower`, w watach).
   - Jeśli lampka jest wyłączona (`apower` = 0), nie włączaj jej samodzielnie. Napisz, że do obliczeń potrzebny jest pomiar mocy przy włączonej lampce, i zapytaj, czy ją włączyć.
2. Ustal cenę energii:
   - jeśli użytkownik podał cenę, użyj jej,
   - jeśli pyta o taryfę (G11, G12) albo nie podał ceny, odczytaj zasób `tariffs.md` i użyj cen z tego pliku.
3. Oblicz zużycie i koszt samodzielnie według wzorów poniżej. Ten skill nie zawiera skryptów — nie wywołuj `run_skill_script`.
4. Przygotuj odpowiedź w formacie opisanym niżej.

## Wzory

```text
energia [kWh] = moc [W] × czas [h] / 1000
koszt [zł]    = energia [kWh] × cena [zł/kWh]
```

Dla taryfy dwustrefowej (G12) policz osobno godziny w strefie dziennej i nocnej, a następnie zsumuj koszty. Godziny stref znajdziesz w `tariffs.md`.

Dla okresu „miesiąc” przyjmij 30 dni.

## Format odpowiedzi

```markdown
**Raport energii — lampka Shelly**

| Parametr | Wartość |
|---|---|
| Moc | … W |
| Czas świecenia | … h |
| Zużycie energii | … kWh |
| Taryfa / cena | … zł/kWh |
| **Koszt** | **… zł** |

Obliczenia: <wzór z podstawionymi wartościami>
```

Przy porównaniu taryf dodaj kolumnę dla każdej taryfy i wskaż tańszą.

## Zasady

- Zaokrąglaj energię do 3 miejsc po przecinku, a koszt do 2 miejsc (grosze).
- Zawsze pokaż obliczenia, aby użytkownik mógł je sprawdzić.
- Jeśli korzystasz z cen z `tariffs.md`, zaznacz, że są to ceny przykładowe.
