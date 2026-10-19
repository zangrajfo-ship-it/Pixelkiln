# Delovanje razvojnega agenta

## Vloga
Glavni agent je hkrati arhitekt, implementator in tehnični vodja. Odgovoren je za integracijo, konflikte in končno preverjanje. Delo poteka po fazah iz `.ai/prompts/master-prompt.md`; po vsaki fazi se ustavi in počaka odobritev lastnika.

## Analiza naloge
1. Preberi `CLAUDE.md`, zadnjo sejo v `.ai/sessions/`, `.ai/architecture/current.md` in `target.md`.
2. Preveri `git status`; tujih sprememb ne zavrzi.
3. Preberi kodo, ki jo naloga zadeva, do konca toka (UI → Core → format).
4. Preveri, ali funkcija že obstaja (tudi delno, tudi brez UI).
5. Zapiši cilj, obseg, merila za sprejem, datoteke in tveganja (korak »Načrt« v `.ai/workflows/phase-cycle.md`).

## Pravila spreminjanja kode
- Samo dogovorjeni obseg; dodatne ideje gredo v sejo kot prihodnje delo.
- Najprej ponovna uporaba obstoječega (`Tools`, `PixelOps`, `History<T>`, `Ui`, `IEditor` …), šele nato nova koda.
- Nobenih praznih gumbov ali navideznih funkcij.
- Formati: glej »Združljivost formatov« v `CLAUDE.md`.
- En smiseln commit na zaključen korak; sporočilo v angleščini, z vrstico `Co-Authored-By`.

## Orodja
- Datoteke: Read/Edit/Write; iskanje: Grep/Glob.
- Bash za `dotnet`, `git`; večvrstične skripte zapiši v datoteko (heredoc z narekovaji se v tem okolju lomi).
- UI preverjanje: `tools/smoke.ps1`, UI Automation, posnetek samo okna aplikacije.
- Specializirane vloge so v `.ai/agents/`; uporabi jih le, kadar dejansko skrajšajo delo. Vzporedni agenti ne smejo hkrati spreminjati istih datotek.

## Zahtevani testi
- `dotnet build` brez napak in opozoril.
- `dotnet test`: vsi testi zeleni, vključno z `FormatRegressionTests`.
- Nova domenska logika: vsaj en test, ki pade, če se logika pokvari; robni primeri pri vhodih iz datotek.
- Spremembe UI: `tools/smoke.ps1` in posnetek prizadetega zavihka.

## Merila za zaključek
Naloga ali faza je zaključena šele, ko:
- so izpolnjena njena merila za sprejem in je to preverjeno (ne le prevedeno);
- je posodobljen zapis seje (`.ai/sessions/SESSION-YYYY-MM-DD-[naloga].md`);
- so posodobljeni arhitekturni dokumenti, če se je struktura spremenila;
- poročilo navaja, kaj ni bilo preverjeno.

## Napake in blokade
- Neuspel test ali build: najprej vzrok, nato popravek; testa ne izklopi.
- Blokada (manjkajoče orodje, nejasna zahteva, odločitev lastnika): zapiši v sejo, predlagaj možnosti s posledicami in vprašaj.
- Destruktivne operacije (brisanje, prepis zgodovine, migracija brez varnostne kopije) samo z izrecno odobritvijo.
