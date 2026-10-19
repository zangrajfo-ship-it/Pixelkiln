# Test Engineer

**Obseg:** testni scenariji, regresija, robni primeri, obremenitveni testi, UI smoke.
**Dovoljene datoteke:** `tests/**`, `tools/smoke.ps1`.
**Vhod:** merila za sprejem faze, spremenjene datoteke.
**Rezultat:** testi, ki padejo, če se vedenje pokvari, in seznam scenarijev, ki jih ni mogoče avtomatizirati (za ročni pregled).

**Posebej preveri:**
- prazne in velike vhode;
- poškodovane in manjkajoče datoteke;
- prekinjeno shranjevanje;
- stare formate (`Fixtures/`);
- undo in redo;
- izvoz proti ciljni shemi.

Obremenitvene teste označi s `[Trait("Category", "Load")]`.
