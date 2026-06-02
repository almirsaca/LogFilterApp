# LogFilterApp

Aplicativo desktop WPF (Windows) para filtrar arquivos de log por padrão de texto e intervalo de data/hora, com auto-detecção do formato de timestamp.

> Pensado para o uso prático de quem precisa investigar logs colados, abertos ou arrastados pra dentro da janela — sem precisar de Splunk/Seq.

---

## Funcionalidades

- **Auto-detecção do formato de timestamp** entre os presets conhecidos.
- **Filtro combinado**: padrão de texto (case-insensitive) + intervalo de data/hora.
- **Multi-fonte**: cole texto, abra arquivo via diálogo, ou arraste-e-solte um ou vários arquivos na janela.
- **Detecção de mistura de formatos**: avisa antes de concatenar arquivos com presets de timestamp diferentes.
- **Pesquisa incremental** dentro do input e do output (Enter / Shift+Enter / Esc).
- **Histórico de diretórios favoritos** com alias, persistido em `%LocalAppData%\LogFilterApp\path_history.json`.
- **Output virtualizado** (`ListView` com recycling) — rola sem travar mesmo com muitos registros.
- **Cancelamento real** da filtragem em andamento ao disparar nova filtragem.
- **Cache de parse**: ao mudar só o padrão de busca, não reparseia o input.

## Formatos de timestamp suportados

| Preset | Exemplo |
|---|---|
| `yyyy-MM-dd HH:mm:ss,fff` | `2026-01-27 08:56:09,121` |
| `dd/MM/yyyy HH:mm:ss.fff` | `19/01/2026 16:44:27.428` |
| `ddd MMM dd yyyy HH:mm:ss` | `Fri Apr 10 2026 14:57:25` |

Linhas sem timestamp são agrupadas como continuação do último registro reconhecido (útil para stack traces multi-linha).

## Requisitos

- Windows 10/11 x64
- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (Desktop). O Release publica como single-file não self-contained.

## Build

Solution: [LogFilterApp.slnx](LogFilterApp.slnx) (formato novo).

```powershell
# Compilar e rodar Debug
dotnet build LogFilterApp.slnx
dotnet run --project LogFilterApp/LogFilterApp.csproj

# Rodar testes
dotnet test LogFilterApp.Tests/LogFilterApp.Tests.csproj
```

## Publicação (Release)

Configurações de Release: `PublishSingleFile=true`, `SelfContained=false`, `ReadyToRun=true`, `RuntimeIdentifier=win-x64`.

```powershell
dotnet publish LogFilterApp/LogFilterApp.csproj -c Release -o <pasta-destino>
```

O script [LogFilterApp/deploy/deploy.ps1](LogFilterApp/deploy/deploy.ps1) automatiza isso para um destino local — ajuste `$destinationPath` antes de usar.

## Estrutura

```
LogFilterApp/
├── LogFilterApp/
│   ├── LogParser.cs              # núcleo: parse + filter + detect (estático, testável)
│   ├── LogFilterCoordinator.cs   # ciclo parse-or-reuse-then-filter (cache + cancel)
│   ├── PathHistoryManager.cs     # persistência do histórico de diretórios
│   ├── MainWindow.xaml(.cs)      # UI principal (code-behind)
│   ├── SavePathDialog.xaml(.cs)  # diálogo de salvar diretório no histórico
│   ├── deploy/deploy.ps1         # script de publish
│   └── docs/TODO.md              # backlog
└── LogFilterApp.Tests/           # xUnit (parser + coordinator + history)
```

## Atalhos

Dentro da caixa de **Buscar** (input ou output):

| Tecla | Ação |
|---|---|
| `Enter` | Próxima ocorrência |
| `Shift+Enter` | Ocorrência anterior |
| `Esc` | Devolve foco ao painel |

## Roadmap

Itens pendentes em [LogFilterApp/docs/TODO.md](LogFilterApp/docs/TODO.md). Destaques: streaming real do input, exportação CSV, tema escuro, MVVM.

## Versão

1.4.0
