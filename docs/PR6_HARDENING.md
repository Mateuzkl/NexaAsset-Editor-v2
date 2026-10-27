# PR #6 — correções verificadas em 27/10/2026

A auditoria estática fornecida pelo usuário foi tratada como hipóteses e conferida no código, junto aos seis comentários inline do PR. Alterações continuam na branch do PR para atualizar a mesma revisão.

| Achado | Correção e evidência |
|---|---|
| F01/F02, preview concorrente e traços perdidos | Um snapshot em voo, coalescência e descarte de callbacks antigos; refresh adiado durante drag. Strokes esparsos com comparação before/after, merge de pixels disjuntos e conflito explícito que retém o desenho. Export PNG do traço retido preserva RGBA. AIR real verifica MCP durante drag e máximo de um snapshot. |
| F03, animação | Remapeamento por tempo × direção; duração omitida preserva os tempos. Testes com células distintas, 1→4, 4→1, extensão/redução, undo e atlas. |
| F04, fechamento | Fechar Studio pausa acesso, cancela geração, revoga consentimento e renova token; mantém drafts. Exportar/descartar drafts é necessário antes de trocar/fechar o projeto. 100 ciclos de abrir/fechar no AIR, 50 por formato. |
| F05, notificações duplicadas | Storage events agregados no commit; uma notificação/revisão por transação, incluindo listas e seleção da categoria. Assert no AIR para moderno e legado. |
| F06, render/histórico | Camadas decodificadas em cache, buffers reutilizados, coalescência por render e cópias de histórico sem serialização JSON/base64. Benchmark compara o compositor antigo e atual byte a byte; histórico exercitado com 1000 strokes e limite de 32. |
| F07, JSONL | Blocos de 32 KiB, retenção após LF, UTF-8 estrito, limite de 4 MiB por linha. Testes de múltiplas linhas, fronteiras, EOF e excesso. |
| F08, atlas | Política compartilhada: 1024 por eixo, 262144 pixels, PNG até 2 MiB; HTTP até 3 MiB. Layout recusado informa colunas válidas. Roundtrips de 64 células 32/64, colunas 1/2/4/8/16 e PNG de alta entropia acima de 1 MiB. |
| F09, provedor lento | HTTP fora do lock; uma geração em voo, cancelamento e validação de sessão/revisão/consentimento ao retornar. Provider controlado com prazo de 25s verifica preview/cancelamento responsivos e rejeita resultado obsoleto. |
| F10, callbacks | Timer próprio de 25s na tabela do worker; conclusão remove callback/timer, invalidação cancela todos e respostas tardias não são entregues. |
| F11, payload do pincel | Deltas com RGBA original/final, uma ação Undo por stroke; substituição completa continua disponível para imports. |
| F12, limpeza | Helpers RGBA/ARGB sem consumidores removidos; contador Connections sem consumidor removido; auditoria limitada exposta no botão Activity. Handlers de reflexão/eventos mantidos. |
| PR: query null | Normalização no adaptador legado e regressão com query explicitamente null. |
| PR: CoreOnly | Descoberta do JDK adiada para integração AIR; diretórios .NET temporários criados no build. Workflow Windows executa o core sem AIR/JDK. |
| PR: mensagens MCP | Exceções de domínio convertidas em McpException; cliente oficial verifica o texto DOCUMENT_CONFLICT e sessão expirada. |
| PR: camada travada | Validação writing=true antes de qualquer envio ao provider; teste confirma zero chamadas. |
| Destino de outfits | category/name pertencem ao draft, sidecar e preview; begin_edit exige category; category=outfit inicia quatro direções. nexa_set_asset_type permite corrigir destino antes da aprovação. Apply usa a categoria persistida e seleciona Outfits/Items/Effects/Missiles no editor. |

## Execuções locais

Passaram `tests/run.ps1`, `tests/run-modern.ps1`, `tests/run-ui.ps1`, `tests/run-native.ps1 -RealAssets ../crystal/gameclient-main/assets`, `tests/run-native-interruption.ps1 -Assets ../crystal/gameclient-main/assets` e `tests/run-mcp.ps1`.

O teste integrado cria um outfit 32×64, quatro direções × quatro frames, solicita aprovação por cliente MCP oficial, altera pixels por MCP durante um drag manual, aplica pelo botão real, verifica pixels/categoria/revisão única e executa Save/Compile no moderno e no legado. A suíte mantém watchdog externo de 240s e registra resultados/RSS dos processos em bin-tests. Dados reais de gravação são cópias; originais não foram modificados. Na execução final de 69 s (65 amostras), pico RSS AIR de 243,3 MiB e soma dos filhos de 176,7 MiB. Após GC, os 50 ciclos por formato tiveram deltas gerenciados de +8,4 MiB no moderno e −6,6 MiB no legado; essas medidas incluem caches do Flex e não demonstram vazamento nem crescimento ilimitado.

Os harnesses antigos de layout/native ainda imprimem avisos preexistentes de callbacks de startup/bindings; seus asserts passam. Provider foi testado com implementação controlada, sem serviço pago. Workflow de CI não equivale ao build AIR completo: o toolchain AIR permanece na validação Windows local.

## Perfil do canvas

`tests/profile-studio.ps1` compara com o commit auditado bd0ab04. Uma execução com 120 redesenhos forçados por caso:

| Tamanho | Camadas | Antes | Depois |
|---|---|---|---|
| 32×32 | 1 / 8 / 16 | 57 / 753 / 1572 ms | 56 / 270 / 519 ms |
| 64×64 | 1 / 8 / 16 | 216 / 3213 / 6242 ms | 213 / 1148 / 2119 ms |

RGBA idêntico em todos os casos. A medição força composição completa para comparação; no uso normal, movimentos são coalescidos por render. Não é certificação de FPS/GC em todas as máquinas nem prova de ausência de crescimento de RSS em uso ilimitado. O histórico e os budgets continuam limitados explicitamente.

A leitura JSONL de 4 MiB ficou com p50 de 4,32 ms e máximo de 4,49 ms em cinco amostras de uma execução. Esses números são locais e variam com a máquina.

As limitações funcionais previamente documentadas (slots existentes, máscaras arbitrárias, rebase e undo de assets publicados) permanecem fora desta rodada; a aprovação no editor continua necessária antes da inserção e Save/Compile é separado.
