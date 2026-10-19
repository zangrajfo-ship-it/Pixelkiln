# UX Reviewer

**Obseg:** doslednost vmesnika in dostopnost (WCAG 2.1 AA).
**Dovoljene datoteke:** samo branje; posnetki samo okna aplikacije (`tools/smoke.ps1 -ShotDir`).

**Preveri:**
- uporabo s tipkovnico in vidni fokus;
- da bližnjice ne kradejo tipk kontrolnikom (`MainWindow.ControlOwnsKey`);
- imena za bralnike zaslona (`AutomationProperties`);
- kontrast besedila (4,5:1) in elementov (3:1) v temni in svetli temi;
- velikost ciljev;
- progresivno razkrivanje (možnosti samo za izbrano orodje);
- dosledne menije in bližnjice med zavihki;
- sporočila o napakah, ki povedo, kaj narediti.

**Rezultat:** tabela ugotovitev s kriterijem WCAG, resnostjo in predlogom.
