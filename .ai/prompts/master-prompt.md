# PixelForge: Master Implementation Prompt

> Poziv lastnika (19. 10. 2026). Razdelki 1–4 so dobesedni; razdelki 5–10 so skrajšani na vsebino, merila za sprejem posameznih faz pa so ohranjena. Odločitve, sprejete ob Fazi 0, so v `../architecture/adr/` in v zapisu seje.

## 1. Vloga in cilj
Deluj kot Principal Software Architect, Senior Software Engineer in Technical Lead. Tvoja naloga je postopoma razširiti obstoječo aplikacijo PixelForge v profesionalno, večnamensko razvojno okolje za 2D igre.
PixelForge trenutno vsebuje štiri glavne module:

* Sprite: pixel-art urejevalnik, plasti, barve, animacije in izvoz.
* Tilemap: tileseti, samodejno povezovanje ploščic, objekti in zemljevidi.
* Music: chiptune sekvencer s štirimi kanali in izvozom WAV.
* SFX: generator zvočnih učinkov in izvoz WAV.

Obstoječe funkcionalnosti, formati datotek in delovni procesi morajo ostati delujoči.

### Glavni cilj
Razvij aplikacijo, v kateri lahko uporabnik na enem mestu:

1. Ustvarja in ureja grafiko, like, animacije, ploščice in zvočne vsebine.
2. Organizira vsa sredstva v projektih.
3. Sestavlja prizore in nivoje 2D iger.
4. Preizkuša grafiko, animacije, kolizije in zvok.
5. Izvaža sredstva v formate za zunanje igralne pogone.
6. Razširja aplikacijo z dodatnimi orodji in vtičniki.

Ne izdeluj nove aplikacije od začetka. Najprej razumi obstoječo implementacijo in jo razširi z najmanjšimi potrebnimi arhitekturnimi spremembami.

## 2. Obvezna pravila dela

### 2.1 Najprej analiza, nato implementacija
Pred spreminjanjem kode:

* Preglej celotno relevantno strukturo repozitorija.
* Prepoznaj programski jezik, ogrodje, različice, knjižnice in arhitekturo.
* Analiziraj vse štiri obstoječe module in njihove podatkovne modele.
* Preveri, kako delujejo shranjevanje, undo/redo, izvoz, nastavitve in bližnjice.
* Zaženi obstoječe teste, build in aplikacijo, če je okolje to omogoča.
* Zabeleži obstoječe napake, opozorila in tehnični dolg.
* Preveri obstoječe formate .pxf, .pxm, .pxs in .sfx.
* Ugotovi, katere predlagane funkcije že obstajajo delno ali v celoti.

Ne sklepaj, da funkcija ne obstaja, samo zato, ker ni vidna v uporabniškem vmesniku.
Pred implementacijo pripravi arhitekturni načrt, odvisnosti med funkcijami, predlagane spremembe podatkovnih modelov, testni načrt in vrstni red izvedbe.

### 2.2 Ohranjanje obstoječe funkcionalnosti

* Ne odstranjuj obstoječih funkcij brez izrecne odobritve.
* Ne prepisuj stabilnih modulov brez dokazane potrebe.
* Ohrani združljivost obstoječih datotek.
* Za spremembe formatov uporabi različice shem in migracije.
* Dodaj regresijske teste za pomembne obstoječe funkcije.
* Ne uporabljaj lažnih implementacij, praznih gumbov ali navidezno delujočih funkcij.
* Ne označi opravila kot dokončanega, če ni implementirano in preverjeno.

Če je sprememba nezdružljiva s starimi projekti, pripravi migracijo in preveri obnovljivost podatkov.

### 2.3 Kakovost kode
Uporabljaj vzorce in standarde, ki ustrezajo ugotovljenemu tehnološkemu skladu:

* SOLID in visoka povezanost komponent.
* Jasna ločitev uporabniškega vmesnika, domenske logike, shranjevanja in izvoza.
* Močno tipizirani podatkovni modeli in strogo obravnavanje napak.
* Injektiranje odvisnosti, kjer je smiselno.
* Majhne, preverljive spremembe.
* Asinhrono izvajanje dolgotrajnih opravil, kjer je potrebno.
* Brez nepotrebnih odvisnosti, podvojenih implementacij ali prezgodnje kompleksnosti.

Ne uvajaj mikroservisov ali zunanjih storitev, če za to ni konkretne potrebe. PixelForge naj ostane preprosta aplikacija za namestitev in uporabo, razen če obstoječa arhitektura utemeljuje drugače.

### 2.4 Uporabniška izkušnja

* Ohrani enoten vizualni jezik vseh modulov.
* Podpri svetlo in temno temo, kjer je to že del sistema nastavitev.
* Zagotovi vidno stanje fokusa in uporabo tipkovnice.
* Uporabljaj dosledne bližnjice, ukaze in kontekstne menije.
* Dodaj razumljiva sporočila o napakah in možnost razveljavitve, kjer je smiselna.
* Ne preobremenjuj uporabniškega vmesnika z vsemi nastavitvami hkrati.
* Uporabljaj progresivno razkrivanje naprednih možnosti.
* Ohrani odzivnost pri velikih slikah, številnih sličicah in velikih projektih.
* Upoštevaj dostopnost, kontrast, velikost klikljivih elementov in bralnike zaslona.

## 3. Projektna dokumentacija za agenta
Ob začetku dela preveri in po potrebi ustvari naslednje datoteke.

### CLAUDE.md
Vsebuje trajna navodila za delo v projektu: arhitekturo in tehnološki sklad; strukturo repozitorija; konvencije poimenovanja in programiranja; pravila testiranja in gradnje; varnostne zahteve; pravila združljivosti formatov; ukaze za razvoj in preverjanje; pravila za spreminjanje arhitekture.
Dokument naj bo kratek in naj vsebuje stabilna pravila, ne dnevnega poročila o napredku.

### AGENT.md
Določa delovanje razvojnega agenta: vlogo in odgovornosti; način analize nalog; pravila spreminjanja kode; uporabo orodij; zahtevane teste; merila za zaključek; postopek ob napakah ali blokadah.

### Organizacija `.ai/`
* `.ai/agents/`: specializirane vloge agentov.
* `.ai/skills/`: ponovljiva strokovna navodila.
* `.ai/prompts/`: projektni in implementacijski pozivi.
* `.ai/workflows/`: večfazni postopki.
* `.ai/architecture/`: arhitekturne odločitve, diagrami in načrti.
* `.ai/context/`: stabilen kontekst projekta.
* `.ai/sessions/`: zapisi posameznih delovnih sej.

### Dnevnik vsake seje
Za vsako večjo nalogo ustvari datoteko `.ai/sessions/SESSION-YYYY-MM-DD-[task].md`. Po vsaki pomembni iteraciji jo samodejno posodobi. Pred zaključkom ali ponastavitvijo seje zapiši: cilj naloge; dokončano, delno dokončano in nedokončano delo; spremenjene datoteke; pomembne arhitekturne odločitve; izvedene teste in njihove rezultate; znane napake in blokade; naslednje konkretne korake; stanje gradnje in CodeGraph.
Ob ponovnem začetku dela najprej preberi `CLAUDE.md`, `AGENT.md`, zadnji relevantni zapis seje in arhitekturno dokumentacijo.

### CodeGraph in arhitekturni diagrami
Če je CodeGraph že integriran, ga uporabi. Sicer preveri možnost obstoječega ustreznega orodja, preden dodaš novo odvisnost.
Pred in po pomembnih arhitekturnih spremembah posodobi indeks kode, `codegraph/index.html`, če ga projekt uporablja, in ustrezne arhitekturne diagrame.
Ne ustvarjaj lažnih indeksov ali diagramov, ki ne odražajo dejanske kode.

## 4. Ciljna funkcionalnost

### A. Project System
Ustvarjanje, odpiranje, shranjevanje in zapiranje projektov. Manifest projekta z različico sheme, nastavitvami in seznamom sredstev. Stabilni identifikatorji sredstev, neodvisni od imen datotek. Relativne poti in varen premik ali preimenovanje sredstev. Samodejno shranjevanje, varnostne kopije in obnovitev. Zaznavanje manjkajočih, poškodovanih in neuporabljenih sredstev. Migracije starejših različic projekta. Pravilno upravljanje nedokončanih opravil in neshranjenih sprememb.

### B. Asset Browser & Manager
Grafično brskanje po sprajtih, animacijah, tilesetih, prizorih, glasbi in SFX. Iskanje, filtriranje, oznake, priljubljeni elementi in mape. Predogled slik, animacij in zvoka. Metapodatki o dimenzijah, formatu, trajanju in uporabi. Povratne reference: kateri prizori in liki uporabljajo sredstvo. Preimenovanje, premikanje, podvajanje in brisanje z varnostnimi preverjanji. Iskanje podvojenih sredstev in nedelujočih referenc.

### C. Character Studio
Organizacija likov, sovražnikov, NPC-jev in predmetov. Predloge idle, walk, run, jump, attack, hurt in death. Povezovanje več animacij z enim likom. Uvoz in izvoz sprite sheetov. Ločeni deli telesa, če je to smiselno za izbrani tip animacije. Metapodatki, izhodiščne točke in smeri lika. Predogled animacij in njihovih prehodov.

### D. Animation Studio
Časovnica z več plastmi. Ključne sličice za položaj, merilo, vrtenje in prosojnost. Interpolacija med ključnimi sličicami. Animiranje objektov in elementov prizora. Predvajanje in sinhronizacija več animacij. Ohranitev obstoječega onion skina, oznak in časov sličic. Izvoz v dogovorjene formate.

### E. Tileset Generator
Samodejno rezanje slik v ploščice. Pravila za povezovanje robov in vogalov. Podpora različnim dimenzijam ploščic. Naključne vizualne različice terena. Preverjanje podvojenih ploščic. Predogled ponavljajočih se vzorcev. Shranjevanje in izvoz pravil autotilinga.

### F. Scene & Level Studio
Postavljanje tilemapov, likov, predmetov in dekoracije. Več ozadnih plasti in paralaksa. Upravljanje kamere in meja prizora. Kontrolne točke, sprožilci in območja interakcije. Upravljanje vrstnega reda risanja. Predogled prizora v ciljni ločljivosti. Organizacija prizorov po nivojih. Povezave do sredstev iz Asset Browserja.

### G. Collision & Physics Editor
Urejanje pravokotnih in drugih podprtih kolizijskih območij. Hitboxi, območja poškodbe in izhodiščne točke. Prikaz kolizij čez grafiko. Kolizijska pravila za like, ploščice in objekte. Nastavljive kategorije in maske kolizij. Izvoz podatkov v dokumentirane formate. Predogled in validacija prekrivanja območij.
Ne implementiraj celotnega fizičnega pogona, če je za potrebe urejevalnika dovolj vizualno urejanje in izvoz metapodatkov.

### H. Audio Studio
Enotna knjižnica glasbe, vzorcev in zvočnih učinkov. Mešalna miza za glasnost kanalov. Fade-in, fade-out, rezanje in združevanje zvokov. Zvočne zanke in seznami predvajanja. Predvajanje glasbe in učinkov skupaj. Predogled zvočnih sredstev v kontekstu prizora. Ohranitev obstoječega sekvencerja in generatorja učinkov. Izvoz v WAV in dodatne formate, kjer so smiselni.

### I. Palette & Style Studio
Upravljanje globalnih barvnih palet. Shranjevanje in uvoz barvnih shem. Globalna zamenjava barv z možnostjo predogleda. Barvni prehodi za svetlobo, senco in materiale. Preverjanje uporabe barv zunaj izbrane palete. Uporaba palet v več sredstvih. Varno uveljavljanje sprememb in razveljavitev.

### J. Game Preview & Sandbox
Predogled likov na ozadju in v prizoru. Preizkušanje animacij pri različnih hitrostih. Prikaz hitboxov, izhodiščnih točk in kolizijskih območij. Preizkušanje kamere in ločljivosti. Skupno predvajanje glasbe in zvočnih učinkov. Ponastavitev predogleda brez spreminjanja izvirnih sredstev.
Predogled naj ne bo predstavljen kot popoln igralni pogon. Če je potrebna simulacija zunanjega pogona, pripravi jasno omejeno integracijo ali izvozno možnost.

### K. Export Pipeline
Izvozni profili za različne ciljne platforme in igralne pogone. Izvoz sprite sheetov, animacij, tilemapov, prizorov in zvoka. JSON metapodatki, kjer jih ciljna oblika podpira. Preverjanje manjkajočih sredstev in neveljavnih referenc. Predogled rezultatov pred izvozom. Poročilo o napakah in uspešnih izvozih. Paketiranje sredstev po prizoru ali celotnem projektu. Preverjanje združljivosti izvoženih podatkov s ciljno shemo.
Obstoječi izvoz Aseprite JSON in Tiled TMJ ohrani ter pokrij s testi. Podporo za Godot, Phaser in druge pogone dodajaj po posameznih, jasno definiranih profilih.

### L. Asset Diff & Version History
Zgodovina sprememb sredstev. Primerjava različic sprajtov in tilemapov. Vizualna primerjava slik in animacij. Primerjava metapodatkov. Obnova starejše različice. Zgodovina shranjenih sprememb projekta. Jasno opozorilo pred prepisom novejših sprememb.
Zgodovina mora biti učinkovita pri velikih sredstvih in ne sme povzročati nenadzorovane porabe prostora.

### M. Procedural Asset Tools
Generiranje variacij tekstur in ploščic. Generatorji naključnih variacij sprajtov. Generatorji zemljevidov z osnovnimi pravili. Generiranje zvokov in zvočnih variacij. Nastavitve generatorjev in shranjevanje predlog. Ponovljivost rezultatov z uporabo semena (seed). Predogled in možnost zavrnitve rezultata pred shranjevanjem.
Generatorji naj bodo modularni in ne smejo neposredno prepisovati obstoječih sredstev brez potrditve.

### N. Command Palette
Iskanje vseh razpoložljivih ukazov. Odpiranje sredstev in preklapljanje med moduli. Iskanje nastavitev in orodij. Prikaz bližnjic. Podpora za razširitev seznama ukazov z novimi moduli. Dosledno upravljanje dovoljenih in trenutno nedostopnih ukazov.

### O. Plugin API
Dokumentiran API za razširitve. Razširitve za orodja, izvozne formate in generatorje. Različice API-ja in preverjanje združljivosti. Jasno določeni vstopni vmesniki. Upravljanje namestitve, omogočanja in odstranitve vtičnikov. Obravnava napak posameznega vtičnika brez sesutja celotne aplikacije. Varnostne omejitve za vtičnike in zunanje izvajanje kode.
Ne izvajaj nepreverjene kode vtičnikov z neomejenimi pravicami. Najprej opredeli varnostni model in minimalno uporaben API.

## 5. Faze implementacije
Faze izvajaj po vrsti. Vsaka faza mora imeti samostojno vrednost, teste in jasno določena merila za sprejem.

### Faza 0: Analiza in tehnična ocena
Analiza repozitorija in modulov; build, testi, aplikacija; funkcionalnost in tehnični dolg; podatkovni modeli in formati; diagram obstoječe arhitekture; ponovna uporaba; razvrstitev funkcij po odvisnostih, tveganjih in koristih; predlog arhitekture in načrt migracij; projektna dokumentacija za agenta. Rezultat: poročilo o stanju, arhitekturni diagram, seznam manjkajočih funkcij, načrt faz, seznam tveganj, osnovni rezultati testov. V tej fazi ne izvajaj obsežnih implementacij novih funkcij.

### Faza 1: Projektna osnova in skupni modeli
Projektni manifest in različice shem; stabilni identifikatorji sredstev; skupne abstrakcije za nalaganje, shranjevanje in izvoz; upravljanje poti in referenc; migracije obstoječih formatov; enotno upravljanje napak in validacije; testi za shranjevanje, nalaganje in združljivost.
Merila: stari projekti se še vedno odprejo; shranjevanje in ponovno odpiranje ohranita podatke; poškodovane datoteke povzročijo razumljivo napako; neuspešna migracija ne uniči izvornih podatkov; obstoječi moduli ostanejo delujoči.

### Faza 2: Asset Browser in upravljanje projektov
Brskalnik vseh sredstev; iskanje, filtri, oznake, priljubljeni; predogledi slik, animacij in zvoka; povezave med sredstvi; zaznavanje manjkajočih in neuporabljenih sredstev; varen premik in preimenovanje; samodejno shranjevanje in obnovitev projekta.
Merila: vse obstoječe vrste sredstev so prepoznavne; preimenovanje ne pokvari referenc; iskanje ostane odzivno pri večjem številu sredstev; zapiranje in ponovno odpiranje ohranita projekt.

### Faza 3: Razširitve grafike in animacij
Character Studio; Animation Studio; Tileset Generator; Palette & Style Studio. Ponovno uporabi obstoječe funkcije Sprite in Tilemap.
Merila: animacije ohranijo časovne podatke in oznake; spremembe palete so predogledljive in razveljavljive; autotiling pravilno obravnava robove in vogale; uvoz in izvoz ohranita razporeditev sličic; novi moduli ne podvajajo obstoječih modelov brez razloga.

### Faza 4: Collision Editor in Scene & Level Studio
Vizualno urejanje kolizij; povezovanje objektov in sredstev; sestavljanje prizorov; plasti, kamera, vrstni red risanja; sprožilci in kontrolne točke; shranjevanje in ponovno odpiranje prizorov.
Merila: prizor ohrani reference; kolizije poravnane z grafiko; sprememba dimenzij ne poškoduje objektov; velik prizor ne blokira vmesnika.

### Faza 5: Audio Studio
Skupna knjižnica; mešalna miza; rezanje, združevanje, prehodi; zanke in seznami predvajanja; sinhroniziran predogled; izvoz in preverjanje.
Merila: Music in SFX ostanejo združljivi; WAV pravilni vzorci in trajanje; brez puščanja zvočnih virov; dolgotrajne operacije ne zamrznejo vmesnika.

### Faza 6: Game Preview & Sandbox
Predogled prizorov; animacije; hitboxi in kolizije; kamera in ločljivost; zvok; ponastavitev.
Merila: predogled ne spreminja virov brez potrditve; ponastavitev vrne znano stanje; napake v predogledu ne poškodujejo projekta.

### Faza 7: Centraliziran Export Pipeline
Enoten sistem izvoznih opravil; preverjanje pred izvozom; profili za obstoječe formate; izvoz animacij, prizorov, tilemapov in zvoka; integracije s pogoni; poročila o napakah.
Merila: ponovljiv in determinističen izvoz; brez delno prepisanih datotek; skladnost s ciljno shemo; regresijski testi obstoječih izvozov.

### Faza 8: Zgodovina različic in proceduralna orodja
Zgodovina različic; vizualna primerjava; obnovitev; generatorji tekstur, sprajtov, zemljevidov in zvokov; nastavitve generatorjev; semena.
Merila: obnova ne prepiše novejše različice brez jasnega postopka; generatorji ponovljivi; nadzorovana poraba pomnilnika in diska.

### Faza 9: Command Palette in Plugin API
Enotno iskanje ukazov; registracija ukazov iz modulov; dokumentiran API; različice API-ja; upravljanje vtičnikov; validacija in obravnava napak; dokumentacija in primer vtičnika.
Merila: moduli registrirajo ukaze brez podvajanja logike; nezdružljivi vtičniki zavrnjeni z razlogom; napaka v vtičniku ne povzroči izgube podatkov; vtičniki brez nepotrebnih privilegijev.

### Faza 10: Stabilizacija in izdaja
Regresijski in integracijski testi; testi migracij; obremenitveni testi; dostopnost in tipkovnica; doslednost oblikovanja; beleženje napak; viri in pomnilnik; dokumentacija; namestitev, nadgradnja in obnova; končno preverjanje gradnje.

## 6. Obvezni razvojni cikel za vsako fazo
Načrt → Implementacija → Preverjanje → Pregled → Zaključek (podrobno v `../workflows/phase-cycle.md`). Fazo označi kot dokončano samo, če so njena merila za sprejem izpolnjena.

## 7. Pravila za testiranje in kakovost
Enotski testi domenske logike; integracijski testi shranjevanja in izvozov; regresijski testi starih formatov; robni primeri in neveljavni vhodi; testi uporabniških interakcij, kadar jih infrastruktura podpira; obremenitveni testi. Posebej: prazni in zelo veliki projekti; manjkajoče ali poškodovane datoteke; prekinjeno shranjevanje; nepričakovano zapiranje; podvojeni ID-ji in prekinjene reference; spremembe velikosti platna in prizora; velike animacije in tileseti; neuspešen izvoz; undo in redo; obnova prejšnje različice; migracije. Ne zadostuje, da se projekt uspešno prevede.

## 8. Pravila za uporabo razvojnih agentov
Vloge: Architect, Implementer, Test Engineer, Reviewer, UX Reviewer (`../agents/`). Vsak agent ima jasen obseg, dovoljene datoteke, pričakovane rezultate in merila. Vzporedni agenti ne smejo brez usklajevanja spreminjati istih datotek ali modelov. Glavni agent je odgovoren za integracijo in končno preverjanje. Dodatne agente uporabi le, kadar dejansko skrajšajo delo.

## 9. Pravila za upravljanje sprememb
Preveri stanje Git; nikoli ne zavrzi uporabnikovih sprememb; majhne povezane spremembe; brez destruktivnih migracij brez varnostne kopije; ne prepisuj konfiguracij ali skrivnosti; brez nepotrebnih odvisnosti; dokumentiraj arhitekturne odločitve; ob konfliktu z arhitekturo pripravi možnosti in posledice. Delo zunaj trenutne faze zabeleži kot prihodnje delo.

## 10. Začetno navodilo agentu
Začni izključno s Fazo 0. Na koncu pripravi poročilo z razdelki: trenutno stanje, ponovno uporabne funkcije, manjkajoče funkcije, arhitekturne omejitve, predlagani podatkovni modeli, struktura modulov, migracije in združljivost, testni načrt, vrstni red faz, tveganja in odprta vprašanja, naslednji koraki. Ne začni Faze 1, dokler analiza ni zaključena in načrt dokumentiran. Po vsaki fazi preveri merila, posodobi zapis seje in šele nato nadaljuj.

Končni cilj je stabilen, vzdrževan in razširljiv PixelForge, v katerem so grafika, animacije, nivoji, zvok, sredstva in izvoz povezani v enoten delovni proces, brez nepotrebnega podvajanja kode in brez izgube obstoječih funkcionalnosti.
