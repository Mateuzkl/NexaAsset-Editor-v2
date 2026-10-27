# Estudo de outfits e desenho por MCP — 27/10/2026

Referência pública consultada: [gilfernandes234/Sprite-Editor](https://github.com/gilfernandes234/Sprite-Editor), commit eaa822003d6548ed462dd93fe2d4b8542f4708a6. O código foi somente lido; nenhum código, dependência de upscale ou asset desse repositório foi incorporado.

## O que a referência mostra

`AnimationPreviewWindow` em spriteEditor.py permite reproduzir uma coluna, uma linha ou toda a grade, configurar largura/altura de célula e FPS, com opção de pular idle. `cut_image` percorre colunas antes de linhas e recorta tiles quadrados. O aplicativo público é um editor/slicer de imagens; não há categoria nativa de outfit, escritor DAT/SPR ou ferramenta MCP nesse código.

O vídeo anexado pelo usuário mostra outro estado do aplicativo: uma sheet com 4 colunas, 3 linhas, célula 64, corpo e machados em camadas, e botão Editar com IA. Esse botão não consta no commit público consultado. A comparação foi visual, por quadros extraídos do vídeo local de 43,97s; não foi inferido backend/provedor pelo texto do botão.

## Aplicação no Nexa

- Fluxo padrão pelo agente MCP, sem formulário/chave de IA no Nexa. A opção de provedor externo fica avançada e só aparece quando há configuração explícita no ambiente.
- Seguir MCP seleciona drafts recém-criados e acompanha pixels durante o desenho. O usuário pode desligar o acompanhamento para trabalhar em outro draft.
- A visão de sheet mostra todas as células. Para outfits: colunas = Norte/Leste/Sul/Oeste (ordem nativa 0/1/2/3), linhas = tempos. Clique abre a célula exata no canvas; seletores separados de tempo e direção evitam navegar por índices lineares sem contexto.
- `nexa_get_edit_sheet` retorna o mesmo mapeamento com RGBA exato, para o agente revisar coerência entre fases/direções. Não inventa automaticamente a anatomia de uma caminhada.
- Outfits continuam recebendo category=outfit desde o início; 32×64 funciona sem forçar canvas quadrado. Quatro tempos × quatro direções significam 16 células, e não quatro direções empilhadas em um item.
- Corpo, arma e reflexos podem ser camadas de pintura por célula. Aplicar rasteriza um novo asset; a máscara colorizável e os grupos de um outfit existente não são substituídos por essa operação.

O layout do arquivo externo precisa ser mapeado explicitamente no import. Não adotar o loop coluna-primeiro do slicer como se fosse a ordem nativa do Nexa: a ordem interna permanece `tempo * direções + direção`.

## Verificação

Testes .NET conferem eixos, ordem N/E/S/W, tempo/direção por célula e pixels distintos. O cliente integrado usa pipe privado para receber a conexão efêmera, sem disputar a área de transferência global. Os objetos de preview/sheet materializam células e metadados sob a revisão lida; a serialização tardia não passa a observar uma edição posterior. O harness AIR consulta a sheet via cliente MCP oficial, navega por clique e seletores, e continua o teste de drag concorrente/aplicação/Save no moderno e no legado. Build portátil é gerado na pasta MCP Fixes. Capturas e cópias da referência ficam em bin-tests, fora do Git.

A demonstração da Poção Astral usa só ferramentas MCP: 32×32, seis tempos distintos, três camadas. Foi aplicada como item 19251 e salva no projeto 8.60 do usuário; leitura posterior do DAT e dos seis sprites 95747–95752 conferiu RGBA exato. Sheet PNG, GIF e .nexaart ficam locais em bin-tests/potion-astral-demo.
