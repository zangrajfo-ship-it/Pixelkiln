# Razvojni cikel faze

Velja za vsako fazo iz `../prompts/master-prompt.md`.

1. **Načrt**
   - Preglej povezano kodo.
   - Določi cilj, obseg in merila za sprejem.
   - Naštej datoteke in komponente.
   - Odvisnosti in tveganja.
   - Razdeli na majhne, preverljive korake.
2. **Implementacija**
   - Samo dogovorjeni obseg.
   - Obstoječe konvencije in komponente.
   - Po vsaki pomembni iteraciji posodobi sejo.
3. **Preverjanje**
   - `dotnet build`, `dotnet test`.
   - Novi testi za vedenje in robne primere.
   - Regresija starih formatov in izvozov.
   - Shranjevanje, ponovno odpiranje in izvoz.
   - `tools/smoke.ps1` in posnetek pri vidnih spremembah.
4. **Pregled**
   - Kompleksnost, podvojena in neuporabljena koda.
   - Varnost, odzivnost, viri (datoteke, zvok, pomnilnik).
   - Posodobi `architecture/current.md` in ADR, če je treba.
5. **Zaključek**
   - Poročilo: kaj je narejeno, datoteke, testi z rezultati, odprte težave, izpolnjena merila, naslednji korak.
   - Commit.
   - **Ustavi se** do odobritve naslednje faze.
