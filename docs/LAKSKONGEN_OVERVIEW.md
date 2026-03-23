# Lakskongen — Salmon Farming Simulator

## Hva er dette?

Lakskongen er en dyptgående simulering av norsk lakseoppdrett — fra rogn til ferdig produkt på middagstallerkenen. Spilleren bygger og driver et oppdrettsimperium langs norskekysten, og må balansere biologi, økonomi, logistikk, miljø, regulering og omdømme.

Tenk Football Manager, men for oppdrettsindustrien.

## Hvor detaljert er simuleringen?

### 🐟 Biologi & Genetikk
- Full livssyklus: rogn → yngel → parr → smolt → matfisk → slakt
- Vekstmodell basert på Von Bertalanffy + Gompertz (reelle biologiske kurver)
- Genetikk-motor: arvelighet for veksthastighet, sykdomsresistens, fôrfaktor
- Fôrkonverteringsrate (FCR) som varierer med temperatur, fiskestørrelse og fôrtype
- Klekkeri med kontrollert miljø, smoltifisering og utsett

### 🌊 Miljø & Havbrukssoner
- 8 norske regioner med unike egenskaper (temperatur, strøm, eksponering)
- Vanntemperatur, oksygennivå og strøm simulert per merd
- Trafikklyssystem (grønn/gul/rød) — nøyaktig som det virkelige reguleringsregimet
- MAB-grenser (Maksimalt Tillatt Biomasse) per konsesjon

### 🦠 Sykdom & Biosikkerhet
- SIR-modell (Susceptible-Infected-Recovered) for smittespredning
- Lakselus med populasjonsdynamikk og behandlingsalternativer
- Avlusningsmetoder: medisinsk, mekanisk, termisk, luseskjørt, rensefisk
- Vaksinasjonskostnad per kg biomasse
- Sonevis smittepress — naboens lus er ditt problem

### 💰 Økonomi & Marked
- Dynamisk lakseprissimulering med sesongvariasjoner og markedssjokk
- Spot-marked vs. forwardkontrakter vs. langsiktige avtaler
- Full P&L, balanse, kontantstrøm og KPI-dashboard
- Investorsystem, forsikring, IPO-mulighet
- Toll-prosessering og egne foredlingsanlegg

### 🏭 Prosessering & Logistikk
- Produksjonskjede: slakt → filetering → pakking → distribusjon
- CAPEX/OPEX-modeller for fabrikkanlegg
- Brønnbåt, lastebil og sjøtransport med ruteplaner
- Leverandørstyring, lagerbeholdning, fôrbestilling
- Flaskehalsanalyse med ROI-kalkulering for oppgraderinger

### 👥 Personell
- Ansatte med kompetansenivåer, erfaring (XP) og spesialisering
- Moral, lønn, opplæring og turnover
- Nøkkelpersoner som påvirker driften

### 🌤️ Vær & Klima
- Markov-kjede værsystem med norsk sesongprofil
- Storm, bølgehøyde, temperaturekstremer
- Stormskade på merder og utstyr
- Sesongbasert lys (midnattssol i nord, mørketid om vinteren)

### 🗺️ Kart & Regioner
- 8 norske kystregioner fra Rogaland til Finnmark
- Haversine-avstandsberegning for logistikk
- Regionale forskjeller i temperatur, regulering og markedstilgang

### 🔬 Forskning & Teknologi
- 25+ teknologier i forskningstre
- Lukket merd-teknologi (RAS, offshore)
- Genetisk seleksjon, fôrinnovasjon, automatisering
- MAB-lisensutvidelse gjennom grønt trafikklys

### 📰 Hendelser & AI-fortelling
- AI-drevet historieforteller med 40+ tilfeldige hendelser
- 5 kampanjer med narrative buer
- Krisehåndteringssystem: varsling → eskalering → full krise → gjenoppbygging
- Beslutningsmotor: meningsfulle valg med konsekvenser og tidsfrist

### 🏆 Progresjon
- 35+ prestasjoner med betingelser
- Produksjonsmål og milepæler
- Omdømmesystem med 6 interessentgrupper
- 51 leksikon-artikler med tooltips
- Konkurrenter med AI-styrt strategi

### 🎮 Spillteknisk
- Godot 4 + C# (simulering) + GDScript (UI)
- Modulært design: 40+ subsystemer
- Save/Load med JSON + backup
- Mod-støtte og CSV-eksport
- Tutorial-system for nye spillere

## Teamet

Utviklet av et AI-agent-team koordinert av Fredrik J. Hansen:
- 3-6 AI-agenter jobber parallelt på ulike systemer
- Orkestreringsagent (Opus) styrer arkitektur og kvalitet
- Builder-agenter (Sonnet, Kimi, DeepSeek, Gemini) implementerer
- Kontinuerlig bygging, testing og integrasjon

## Status

**13 sprinter gjennomført.** Alle kjernesystemer implementert og integrert. Nå i "The Hook"-fasen — gjør simuleringen til et engasjerende spill med beslutninger, kriser og synlig progresjon.

Art direction besluttet: "Nordic Illustrated" — flat illustrert 2D med norsk fargepalett og atmosfærisk lys.

---

*Kontakt: Fredrik J. Hansen — frehan83@gmail.com*
