# MCP / Pixel Art Studio — arquitetura implementada

Baseline: `0d7fccc`; branch `feature/mcp-pixel-art-studio`. Aplicação AS3/Flex/AIR, helper moderno .NET 10. SDK oficial `ModelContextProtocol.AspNetCore` **2.3.0**, fixado e com `packages.lock.json`. Referências consultadas em 27/10/2026: [SDK oficial](https://github.com/modelcontextprotocol/csharp-sdk), [transporte HTTP](https://csharp.sdk.modelcontextprotocol.io/).

## Fluxo real

`Cliente MCP → HTTP 127.0.0.1:porta-dinâmica/mcp → StudioTools → StudioService / ArtDocument → pipe privado do processo filho → StudioController → ObjectBuilder → worker legado OU fila ModernNativeSource → NativeWorkspace`.

O processo é iniciado ao abrir **Pixel Art / MCP** no painel Sprites. O listener responde somente após marcar **Ativar MCP**. O Studio manual usa o mesmo processo e o mesmo documento; não há um PNG paralelo simulando edição do editor.

`ArtDocument` mantém RGBA straight, camadas por célula, duração, direções, histórico e revisão. Célula = `tempo * direções + direção`, respeitando a ordem de `patternX` do formato. `.nexaart` schema 1 preserva frames, nomes, opacidade, locks e bytes das camadas. A composição é rasterizada apenas na inserção. BitmapData é superfície descartável de exibição.

MCP edita drafts. `nexa_commit_edit` solicita aprovação, sem modificar o projeto. O botão **Aplicar como NOVO asset** chama `ui_apply` pelo pipe privado, verifica sessão/revisões e insere o asset. O usuário usa o Save/Compile existente depois. Nenhuma ferramenta MCP grava os binários ativos, substitui objetos ou chama Save.

## Adaptadores

- **Legado:** `StudioCommand` / `StudioResultCommand`, registrados nos comunicadores existentes. `LegacyStudioAdapter` executa no worker dono de `ThingTypeStorage` e `SpriteStorage`. Valida todas as células, tiles, durações e limites antes de criar sprites; restaura a cauda adicionada se a inserção falha. Tiles 64×64 são divididos em 32×32, na ordem bottom-right. A revisão conservadora avança nas notificações do worker, incluindo mudanças por comandos existentes.
- **Moderno:** `native-studio-read` e `native-art-commit`, dentro da fila serial atual, sem alterar schemaVersion 1. O AIR escreve um pacote temporário em `ModernAssetSession.directory`, nome aleatório, SHA-256 dos bytes exatos, no máximo 2 MiB. O helper valida nome, diretório, links, checksum, revisão, dimensões, referências e orçamento antes da publicação em memória. A requisição JSONL continua menor que 64 KiB. O pacote é removido no sucesso/erro e a sessão já possui limpeza no fechamento.
- A criação moderna constrói um novo appearance protobuf, grupos raster, direções e fases reais. Objetos originais, máscaras e campos desconhecidos não são reescritos. Não é uma implementação de substituição estrutural de outfits mascarados existentes.

## Capacidades e limites desta entrega

- Sprites com eixos de 32 ou 64; 4 drafts; 64 células por draft; 16 camadas/célula; 2 MiB de pixels decodificados/draft; histórico de até 32 snapshots/4 MiB por pilha. Tamanhos customizados de tile legado são recusados no commit.
- Novo item, outfit, effect ou missile, com uma camada nativa rasterizada por grupo. Outfits animados modernos, e legados com frameGroups, recebem idle usando o primeiro tempo existente e walk usando toda a timeline; não se inventa uma imagem de idle. Os demais assets recebem um grupo. Uma ou quatro direções. Máximo conservador de ID legado 65.535 por categoria.
- Formatos legados sem improved animations aceitam o fallback de 100 ms; durações customizadas são recusadas. Moderno e legado compatível preservam tempos por fase.
- Edição de draft: pixels, PNG, paleta exata, flip, rotação de 90°, offset, camadas, timeline, undo/redo; sheet PNG + JSON; sidecar editável.
- Referência: leitura de sprite real e dos IDs/grupos/frames. Não há conversão automática de um outfit inteiro com múltiplas máscaras em um novo documento de pintura.
- IA opcional: contrato HTTPS documentado em `AI_GENERATION.md`. Sem provedor, ferramentas determinísticas funcionam. Animação derivada por offsets é explícita; não é geração automática de caminhada coerente.

## Pendências explícitas do prompt completo

Ainda não há substituição/atribuição de slot existente via MCP, edição estrutural arbitrária de grupos existentes, rebase automático, undo de asset já inserido, proxy MCP STDIO, OAuth, perfis de permissões por cliente ou certificação de Claude Desktop. Revisões concorrentes são recusadas; exporte o draft e reabra contra a versão atual. A conexão padrão foi exercitada pelo SDK oficial e pelo editor AIR, conforme o relatório de testes. Não declarar clientes proprietários, credenciais externas ou compatibilidade em jogo como testados por inferência.
