# ADR-0003: Zgodovina različic v mapi `.history`

**Status:** sprejeto (odločitev lastnika, 19. 10. 2026), izvedba v Fazi 8

## Kontekst
Uporabnik želi primerjati in obnavljati starejše različice sredstev, brez git-a in brez nenadzorovane porabe prostora.

## Odločitev
- Ob vsakem shranjevanju sredstva v projektu se vsebina stisne (GZip) in zapiše v `.history/objects/<sha256>`. Če zgoščenka že obstaja, se ne zapiše znova (deduplikacija).
- `.history/index.json`: za vsak `assetId` seznam `{ hash, utc, note }`.
- Meje: največ N različic na sredstvo in skupaj M MB (privzeto 50 in 500 MB, nastavljivo). Ob prekoračitvi se brišejo najstarejši vnosi in nato objekti brez reference.
- Obnova starejše različice ne prepiše trenutne. Najprej se trenutna shrani kot nova različica, nato obnova, z opozorilom uporabniku.

## Posledice
- Deluje brez zunanjih orodij in je prenosljivo s projektom.
- Brisanje `.history` ne vpliva na projekt.
- Primerjava (diff) bere dve različici iz objektov; slike se primerjajo po pikah v Core.
