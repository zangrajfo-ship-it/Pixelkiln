# ADR-0001: Projektni manifest in stabilni ID-ji sredstev

**Status:** sprejeto, izvedeno v Fazi 1
**Datum:** 19. 10. 2026

## Kontekst
Pixelkiln ureja posamezne datoteke. Tilemap kopira pike tileseta in hrani absolutno pot do vira. Za prizore, like, brskalnik sredstev in izvoz morajo sredstva sklicevati drugo na drugo, tudi po preimenovanju ali premiku.

## Odločitev
- Projekt je mapa z manifestom `.pfproj` (JSON, `Version`).
- Vsako sredstvo ima v manifestu stabilen GUID, relativno pot in metapodatke.
- Reference med sredstvi uporabljajo samo `assetId`; pot razreši manifest.
- Obstoječi formati ostanejo nosilci vsebine. Projekt jih ne spremeni, samo našteje.
- Samostojne datoteke se še naprej odpirajo brez projekta.

## Posledice
- Preimenovanje ali premik sredstva spremeni samo manifest.
- Manjkajoča datoteka je zaznavna (ID brez datoteke), neuporabljena prav tako (datoteka brez reference).
- `.pxm` v3 doda `tilesetAssetId`; vgrajene pike ostanejo kot rezerva, zato samostojne mape delujejo kot prej.
- Treba je vzdrževati manifest ob vsakem dodajanju, brisanju in preimenovanju (zbrano v razredu `Project` v Core).

## Zavrnjene možnosti
- **Reference po relativni poti:** preimenovanje bi pokvarilo vse reference.
- **En velik projektni datoteka z vsemi sredstvi:** slabo za git, za velike projekte in za združljivost s starimi datotekami.
