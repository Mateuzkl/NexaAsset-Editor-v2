# Entrega MCP + Pixel Art Studio — 27/10/2026

Branch: `feature/mcp-pixel-art-studio`. Baseline preservado: `0d7fccc`.

## Resultado implementado

O NexaAsset mantém AS3/Flex/AIR e seus fluxos existentes. Um Studio real permite pintar sprites, trabalhar em camadas e tempos/direções e compartilhar o mesmo draft com clientes MCP. A ponte lê o projeto que está aberto. Não há edição paralela dos binários de assets pelo gateway.

- Gateway .NET 10 com SDK oficial MCP 2.3.0, 25 ferramentas, resources e imagens PNG em respostas de preview/sprite.
- HTTP loopback autenticado, porta/token por instância, desligado por padrão, checagens de Host/Origin, limites de payload/fila/memória, cancelamento e timeout.
- RGBA straight, preservação de bytes de camadas, paleta exata, flips, 90°, offset, PNG validado, composição e histórico limitado.
- Canvas: lápis/pincel, borracha, conta-gotas, linha, retângulo, elipse, balde, seleção/movimento/copiar/colar, grade, zoom inteiro e atalhos.
- Camadas visíveis/opacas/travadas, reordenação, duplicação e merge; timeline com duração, uma/quatro direções, play/velocidade e onion skin.
- `.nexaart` versionado, PNG e spritesheet PNG/JSON. Importação de sheets valida dimensões, ordem, duração e direção.
- Preview/diff e revisão compartilhada. MCP solicita aprovação; o botão humano insere um **novo** asset. Save/Compile permanece no fluxo normal do editor.
- Legado: worker proprietário, tiles 32×32, composição 32/64 por eixo, validação antes da mutação, compensação da cauda de sprites e preservação de IDs originais.
- Moderno: fila existente, pacote temporário de sessão com SHA-256, manutenção do limite JSONL de 64 KiB, publicação em memória validada e Save existente.
- Novos outfits animados: idle referencia o primeiro tempo já desenhado; walk usa a timeline, no moderno e no legado com frameGroups. Não se sintetiza uma imagem de idle nem se substituem máscaras existentes.
- Provider HTTPS opcional/configurável, consentimento para enviar a referência, credencial no ambiente e validação rigorosa da imagem retornada. Sem credenciais, o Studio e as operações locais funcionam.

## Arquivos e responsabilidades

| Arquivos | Responsabilidade |
|---|---|
| `src/ob/studio/PixelArtStudio.mxml`, `PixelCanvas.as` | Janela, ferramentas, timeline, revisão e import/export humanos |
| `src/ob/studio/StudioController.as`, `StudioEvent.as`, `StudioPixels.as` | Processo privado, callbacks, timeout e conversões exatas |
| `src/ob/studio/LegacyStudioAdapter.as`, `src/ob/commands/Studio*Command.as` | Leitura e inserção na fila do worker legado |
| `src/ObjectBuilder.mxml`, `src/ObjectBuilderWorker.as` | Pontos explícitos de integração, seleção, dirty state, sessão e lifecycle |
| `tools/mcp-gateway/ArtDocument.cs`, `StudioService.cs` | Documento, transações, histórico, composição, revisões e operações compartilhadas |
| `tools/mcp-gateway/Program.cs`, `EditorBridge.cs`, `StudioTools.cs`, `StudioResources.cs` | HTTP/MCP, autenticação, limites, schemas e IPC |
| `tools/mcp-gateway/ArtGeneration.cs` | Contrato e provider HTTPS opcional |
| `tools/modern-helper/StudioWorkspace.cs`, `Program.cs`, `NativeWorkspace.cs`, `NativePng.cs` | Integração moderna, pacote validado, criação animada e decoder separado de sheet |
| `build.ps1`, `tools/mcp-gateway/build.ps1`, csproj/lock/licenças | Build portátil reproduzível e dependências do gateway |
| `locale/*/strings.properties` | Controles do Studio em en_US/pt_BR/es_ES |
| `tests/StudioMainTests*`, `LegacyStudioTests.as`, `run-mcp.ps1`, `tools/mcp-gateway-tests/*` | Contratos, segurança, imagens, estrutura e teste do editor real |
| `docs/MCP_*.md`, `PIXEL_ART_STUDIO.md`, `AI_GENERATION.md`, `README.md` | Instalação, operação, limites e evidência |

## Executável e uso

Bundle separado: `bin/NexaAsset Editor v2 - MCP Studio/NexaAsset Editor v2.exe`.

1. Abra esse executável e um projeto legado ou moderno.
2. No painel **Sprites**, clique **Pixel Art / MCP**.
3. Crie um canvas ou use o sprite selecionado como referência; desenhe, configure camadas/timeline e salve `.nexaart` se quiser continuar depois.
4. Para um agente, marque **Ativar MCP**, copie configuração e forneça o token pelo ambiente do cliente. Consulte [Codex](MCP_SETUP_CODEX.md) e [Claude Code](MCP_SETUP_CLAUDE.md).
5. O agente usa get_project_info → begin_edit → set_pixels/import/transform/layers/animation → get_edit_preview → commit_edit.
6. Revise e clique **Aplicar como NOVO asset**, escolhendo item/outfit/effect/missile. Depois use **Salvar/Compilar** no aplicativo.

Build reproduzível:

```powershell
./build.ps1 -BundleName 'NexaAsset Editor v2 - MCP Studio'
./tests/run-mcp.ps1
```

A publicação inclui o runtime .NET dos helpers e as licenças correspondentes; não depende de o usuário instalar o SDK para executar o bundle. O toolchain para reconstruir continua sendo ObjectBuilder-tools + dotnet, conforme o build existente.

## Testes executados

| Comando | Resultado |
|---|---|
| `tests/run.ps1` | PASS: storage legado, transparência, IDs, multi-SPR, publicação/rollback, filas, progress/layout e janelas |
| `tests/run-modern.ps1` | PASS: helper, PNG/RGBA, metadados desconhecidos, import/export, Save/Save As, limites e 10.000 casos de wire fuzz |
| `tests/run-ui.ps1` | PASS: layout real en_US/pt_BR/es_ES, escalas, navegação e diálogos |
| `tests/run-native.ps1 -RealAssets ../crystal/gameclient-main/assets` | PASS: cópia dos assets reais, categorias, import/export OBD/AEC, clipboard, Save/reopen e Save As |
| `tests/run-native-interruption.ps1 -Assets ../crystal/gameclient-main/assets` | PASS: helper encerrado durante Save; catálogo anterior intacto |
| `tests/run-mcp.ps1` | PASS: core, provider de teste, cliente MCP oficial, auth/Host/Origin, oversized, concorrência, sheets, AIR moderno e legado |
| `build.ps1 -BundleName ...` | PASS: compilação AIR e empacotamento do bundle |

O teste AIR/MCP usa um processo cliente independente, o editor real e o botão real de Aplicar. Confere pixels de IA e de um stroke manual no storage, alpha/canais, dirty state e Save/Compile. Também compila/reabre um outfit legado 64×64 com 4 direções × 3 frames, timings, grupos e tiles exatos. O helper moderno passa pelo mesmo cenário estrutural e por Save/reopen independente.

Capturas da execução final: `bin-tests/studio-ddbeab93a1084313b856cee9607dc7f0/studio-modern.png` e `studio-legacy.png` (pt_BR). Os diretórios de teste são ignorados pelo Git.

Os avisos já presentes de registro de callbacks internos e binding de category continuam aparecendo nos harnesses AIR; as suítes terminam em PASS. O gateway compila com zero avisos/erros. Não foram usados créditos ou uma API de geração externa real: o contrato do provider foi verificado com handler em processo. Codex/Claude proprietários não foram executados como clientes nesta validação; foi usado o SDK MCP oficial, com configurações conferidas nas respectivas documentações oficiais.

## Compatibilidade e pendências

| Área | Estado |
|---|---|
| Legado com tile padrão 32 | Novo asset com arte 32/64; timings customizados exigem improved animations; ID de categoria limitado conservadoramente a 65.535 |
| SPR RGB | Alpha não zero é promovido a opaco pelo formato; preservar `.nexaart` para os bytes editáveis exatos |
| Moderno | Eixos 32/64, referências, novos grupos/fases; originais e metadados desconhecidos preservados |
| Máscaras/outfits existentes | Leitura/referência; não há substituição estrutural nem remapeamento automático de máscaras |
| Conflitos | Revisão obsoleta é recusada; ainda não há rebase automático |
| Undo | Draft somente; não desfaz um asset já inserido/publicado |
| IA | Endpoint HTTPS com contrato documentado; animação por offsets é derivação explícita, não geração artística de caminhada |
| Clientes/transporte | Streamable HTTP; ainda sem proxy STDIO, OAuth ou perfis individuais por cliente |

O Markdown mestre não está 100% concluído: faltam atribuição a slots existentes via MCP, edição arbitrária de grupos/máscaras existentes, rebase, undo de assets aplicados e geração multiframe artística automática. Esta entrega contém o núcleo funcional integrado e verificável, sem apresentar essas pendências como prontas. Veja [arquitetura](MCP_ARCHITECTURE.md), [ferramentas](MCP_TOOLS.md), [segurança](MCP_SECURITY.md) e [provider](AI_GENERATION.md).

Commits locais de implementação e testes: `5392283` (Studio/gateway/adaptadores) e `b677958` (testes). A documentação está no commit seguinte.

Os assets reais de origem foram somente lidos; os testes de gravação/interrupção trabalharam em cópias sob bin-tests. Alterações e commits são locais nesta branch; não houve push, PR ou merge.
