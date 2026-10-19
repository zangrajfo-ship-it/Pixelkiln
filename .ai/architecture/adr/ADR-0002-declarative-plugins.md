# ADR-0002: Vtičniki so samo podatki

**Status:** sprejeto (odločitev lastnika, 19. 10. 2026)

## Kontekst
.NET (Core in novejši) nima več izolacije kode znotraj procesa (AppDomain, CAS). Vtičnik v obliki DLL bi dobil vse pravice aplikacije in uporabnika. Ločen proces bi bil varen, a precej dela za osebno orodje.

## Odločitev
Vtičnik je mapa `plugins/<id>/` z `plugin.json` (`apiVersion`, `id`, `name`, `version`, `contributions`). Prispevki so samo podatki:
- izvozne predloge;
- prednastavitve generatorjev;
- barvne palete;
- makri ukazov (zaporedje obstoječih ukazov iz CommandRegistry).

PixelForge ne nalaga in ne izvaja kode iz vtičnikov.

## Posledice
- Varnost: vtičnik ne more brati datotek, omrežja ali sistema; le ponudi podatke, ki jih aplikacija preveri po shemi.
- Nezdružljiv `apiVersion` ali neveljavna shema: vtičnik je zavrnjen z razlogom, aplikacija teče naprej.
- Omejitev: novih algoritmov ni mogoče dodati kot vtičnik; ti gredo v Core. Če bo kdaj potreben kodni vtičnik, ga obravnava nov ADR (ločen proces).
