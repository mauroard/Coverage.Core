# Coverage.Core

Applicazione desktop Avalonia per eseguire i test di un progetto .NET e riepilogare il line coverage dei sorgenti.

## Tipi di progetto

- **Microservizio .NET**: analizza i file `.cs`.
- **Frontend MVC**: analizza i file `.cs`, `.ts`, `.tsx` e `.cshtml` presenti nella cartella applicativa.

Seleziona la cartella del progetto applicativo dall'interfaccia. Quando trova un `.csproj`, l'app mostra nome e percorso completo del progetto applicativo e del progetto unit test rilevato automaticamente. La ricerca avviene nella soluzione o nel repository più vicino; se trova più progetti senza poterli associare, mostra un messaggio invece di sceglierne uno arbitrariamente.

Al termine dell'analisi, usa il filtro nella sezione dei file sorgente per cercare per nome o percorso e verificare il coverage di un singolo file. Il filtro agisce solo sull'elenco: il riepilogo complessivo resta invariato. Nell'elenco, i file senza dati di coverage sono evidenziati in giallo; quelli con coverage misurato pari a 0% sono evidenziati in rosso.

## Soglie di qualità

È possibile configurare soglie minime di copertura, sia complessive che per singolo file. Entrambe le soglie sono attive per impostazione predefinita e possono essere disattivate o modificate separatamente.

Il valore iniziale di entrambe le soglie è **80%**. L'opzione che segnala le violazioni come esito negativo è attiva per impostazione predefinita; disattivandola, le violazioni generano solo un avviso.

Le verifiche delle soglie per singolo file considerano solo i file per i quali sono disponibili dati di copertura. Se non ci sono file misurabili, la soglia non può essere verificata.

## Requisiti per il coverage

- .NET SDK disponibile nel `PATH`.
- Il progetto unit test deve usare `Microsoft.NET.Test.Sdk` e includere il collector `coverlet.collector`, necessario per `dotnet test --collect "XPlat Code Coverage"`. Se manca, aggiungilo al progetto test con `dotnet add <progetto-test.csproj> package coverlet.collector`.
- Per il TypeScript, il frontend può definire uno script `test:coverage` nel proprio `package.json` che generi `lcov.info` (ad esempio in `coverage/lcov.info`). In modalità Frontend MVC l’app esegue gli script `test:coverage` trovati nella cartella del progetto; se non ne trova, legge gli eventuali report LCOV già presenti.
- Le view `.cshtml` sono misurate solo quando un report Cobertura o LCOV include esplicitamente quelle view. Il collector .NET non garantisce da solo la strumentazione delle Razor view.

I file senza linee presenti nei report sono indicati come **Non misurato** e non vengono conteggiati come linee scoperte. Il riepilogo percentuale usa solo le linee effettivamente misurate.

## Analisi statica

Il pulsante **Analisi statica** avvia una verifica separata dal coverage. Per i progetti .NET raccoglie diagnostici di compilatore e analyzer tramite `dotnet build`; per Frontend MVC esegue gli script `lint` compatibili che invocano ESLint e producono JSON. Gli script con `--fix`, `--write` o più comandi concatenati vengono saltati per evitare modifiche automatiche ai sorgenti e output non interpretabile. I rilievi possono essere filtrati per gravità, regola e file. Se ESLint non è configurato, l'app mostra un avviso.
