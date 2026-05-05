# TODO

Backlog informal do LogFilterApp. Itens estão em ordem aproximada de prioridade dentro de cada seção.

## Em andamento / parcial

- **Streaming real para logs gigantes** — a infraestrutura de parsing por `TextReader` já existe (`LogParser.ParseLogEntries(TextReader, ...)` e `DetectPreset(TextReader, ...)`), mas o input ainda materializa a string inteira em `TxtInputLog.Text`. Migrar requer separar "fonte de dados" de "exibição" (manter um modelo de arquivos abertos e parsear direto do disco em vez de concatenar tudo no TextBox).

## Backlog — UI/UX

- **Tema escuro** estilo ferramenta de log profissional.
- **Barra de status real** (rodapé com nº de arquivos abertos, último filtro, tempo de parse, etc.).
- **Trocar StackPanel aninhado por Grid** nos controles superiores — fica mais alinhado e profissional.
- **Visualizador estilo "Seq simplificado"**: colorir nível (`ERROR`/`WARN`/`INFO`), copy-paste de uma linha, expandir/recolher entradas multi-linha.

## Backlog — funcionalidades

- **Exportação para CSV** do output filtrado.
- **Estatística do período**: contagem por nível (`ERROR`, `WARN`, etc.), top exceções, distribuição por hora.
- **Timeline de eventos** (visualização gráfica simples do volume por unidade de tempo).
- **Agrupar por exceção** — agrupa stack traces idênticos.

## Backlog — arquitetura

- **Migrar para MVVM** corretamente — o code-behind atual já tem cache, cancelamento, busy state e detecção de mistura; um ViewModel deixaria isso testável de ponta a ponta.
