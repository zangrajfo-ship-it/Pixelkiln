# Dostopnost v WPF (PixelForge)

- **Ikone in simboli:** gumb z ikono ali simbolom dobi `ToolTip`, `Ui.NameSymbolButtons` ga uporabi kot `AutomationProperties.Name`. Bližnjico zapiši v oklepaju: `"Zoom in (Ctrl+=)"`.
- **Polja:** `Label Content="_Ime" Target="{Binding ElementName=Polje}"` (dostopna tipka in ime za bralnik).
- **Barvni vzorci:** `Ui.Swatch` (pravi gumb, fokus, ime z barvo).
- **Napačen vnos:** `Ui.MarkInvalid(box, "kaj je narobe in kaj narediti")`, ob pravilnem vnosu `Ui.MarkInvalid(box, null)`.
- **Bližnjice:** obdela jih `IEditor.HandleKey`; `MainWindow.ControlOwnsKey` poskrbi, da tipk ne vzamejo kontrolnikom.
- **Elementi po meri** (platno, piano-roll): `Focusable="True"`, viden fokus, upravljanje s puščicami, stanje v `AutomationProperties.Name` ali `ItemStatus`.
- **Barve:**
  - sekundarno besedilo `{DynamicResource MutedText}` (se menja s temo);
  - fokus `{StaticResource FocusRing}`;
  - kontrast besedila vsaj 4,5:1, elementov 3:1.
- **Pred zaključkom:** `tools/smoke.ps1` in pregled v temni in svetli temi.
