# Pixel Art Studio

Abra um projeto e clique **Pixel Art / MCP** no painel Sprites. O editor manual funciona com MCP desativado. **32×32 +** / **64×64 +** abrem drafts vazios; **Usar sprite selecionado** clona os bytes do sprite selecionado quando a janela foi aberta. Também é possível escolher outro draft na lista, inclusive os criados por um cliente MCP.

- Ferramentas: pencil, eraser, picker, line, rect, ellipse, fill, select, move. Lápis/borracha aceitam tamanho de pincel; traços contínuos interpolam coordenadas inteiras. Cores em **RRGGBBAA**, incluindo alpha.
- Grade, zoom inteiro de 1–16, fundo xadrez e scroll do canvas. `B` lápis, `E` borracha, `I` conta-gotas, `G` balde. Seleção limita pintura; `Esc` limpa seleção; move arrasta a região; copiar/colar é interno ao canvas, preservando os quatro canais. Ctrl+C/V e Ctrl+Z / Ctrl+Shift+Z.
- A lista de camadas é ordenada de baixo para cima. `+` cria, `×` remove, `↕` reordena; visibilidade, lock e opacidade são aplicados por **OK**. Duplicar e mesclar visíveis compartilham o histórico. São camadas de pintura, independentes das máscaras/layers do jogo.
- Timeline: Cell navega todas as células. As configurações abaixo são número de tempos, 1/4 direções e duração em ms. **OK** configura a estrutura; novos tempos copiam o último tempo de cada direção e precisam ser desenhadas para ter imagens diferentes. Os seletores Tempo e N/E/S/W navegam sem misturar fases e direções. **Ver sheet** mostra a animação completa, com direções nas colunas e tempos nas linhas; clique em uma célula para abrir sua pintura. Play avança preservando a direção escolhida; velocidade 0,25×–4×. Onion mostra a fase anterior da mesma direção.
- **Importar PNG** coloca pixels na camada selecionada; **Exportar PNG** rasteriza a célula atual. **Importar sheet** divide PNG pela dimensão do draft e pela ordem row-major (tempo/direção), usando duração e direções atuais; MCP também aceita o JSON de mapeamento exportado. **Sheet PNG + JSON** exporta todas as células com tempos e direções explícitos. **Salvar .nexaart** preserva as camadas; **Abrir .nexaart** carrega no draft atual e mantém a checagem de revisão do projeto. Escolha um nome novo para preservar arquivos anteriores.
- **Aplicar como NOVO asset** cria um item/outfit/effect/missile no projeto ativo. Não salva o binário automaticamente. Use Salvar/Compilar do aplicativo. Originais e suas máscaras não são substituídos.

Um stroke é um passo de undo. O histórico é limitado; undo/redo vale para o draft. Após aplicar, o draft fica fechado para escrita. Não há falso undo de um asset já inserido. Descartar um documento já aplicado somente fecha o draft e preserva o asset.

Durante um stroke, o preview fica adiado. Deltas em pixels diferentes se unem; sobreposição conflitante retém seu desenho com resolução explícita e export PNG. A estrutura de frames/camadas deve coincidir para fazer merge. Se o projeto mudar desde o início do draft, exporte seu trabalho e inicie outro contra a revisão atual. Trocar/fechar o projeto invalida drafts e token. Fechar somente a janela mantém os drafts em memória, pausa o MCP, renova o token e revoga consentimento externo. Para reconectar, reabra, ative MCP e copie o token novo.

## Desenho pelo agente MCP

O fluxo normal não pede chave de provedor no Nexa: conecte o agente MCP, peça um item/outfit/efeito e acompanhe os pixels no Studio. **Seguir MCP** seleciona drafts novos e acompanha suas revisões; desmarque para continuar em outro draft. O agente escolhe `category` ao criar; o destino aparece no preview e na aplicação.

`nexa_get_edit_sheet` permite ao agente revisar todas as células com tempo, direção, duração e RGBA exato. O editor mostra Norte/Leste/Sul/Oeste na ordem nativa 0/1/2/3. Camadas como corpo/arma/reflexos podem ser pintadas separadamente por célula. Uma camada de pintura não equivale à máscara colorizável nativa de um outfit.

Um serviço externo só aparece como opção avançada quando NEXA_ART_PROVIDER_URL foi configurada antes de iniciar o aplicativo; não é requisito do MCP. A aprovação pelo botão e Save/Compile continuam no editor.
