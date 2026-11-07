# Respiração no Pixel Art Studio

A ferramenta gera um ciclo geométrico de quatro poses: neutro, inspiração, expansão e expiração. Cada pose parte da mesma origem; não há deformação acumulada. A fase neutra mantém RGBA exato. O algoritmo copia pixels inteiros, sem interpolação e sem criar cores. A região automática usa a caixa visível de cada face e mantém a parte inferior como âncora; não reconhece peito, armadura ou anatomia.

## Uso manual

1. Abra/crie um draft com um único instante e canvas completo (32×32, 32×64, 64×32 ou 64×64).
2. Para controle manual, escolha `select`, arraste o retângulo e clique **Marcar região**. Em quatro direções, repita em N/E/S/W. As regiões são independentes e copiadas antes de qualquer atualização do canvas.
3. Clique **Animação de Repouso** e mantenha o preset **Respiração**. Escolha amplitude de 1 ou 2 px, região automática ou manual e quatro durações. **Gerar no draft** atualiza o mesmo documento do Studio. Para carregar uma pose completa pelo painel Appearance ou combinar outros movimentos, veja [Animação de Repouso](IDLE_ANIMATION.md).
4. Confira Sheet/Play e ajuste o resultado. Undo/Redo desfaz/refaz o ciclo inteiro. PNG/spritesheet e `.nexaart` continuam disponíveis.
5. **Apply as NEW asset** insere um objeto novo após a revisão local. Save/Compile persiste os arquivos separadamente.

## MCP

Depois de habilitar o MCP local, use `nexa_generate_breathing`:

```json
{
  "editId": "draft-id",
  "expectedDocumentRevision": 0,
  "parameters": {
    "amplitude": 1,
    "durationsMs": [500, 300, 500, 300],
    "regionMode": "auto",
    "regions": []
  }
}
```

Para `regionMode: "rectangle"`, forneça exatamente uma região `{direction,x,y,width,height}` por direção. Coordenadas são pixels do canvas. As quatro direções usam 0=N, 1=E, 2=S, 3=W; as células usam `tempo * Directions + direção`. Regiões duplicadas, incompletas, vazias ou fora do canvas são rejeitadas, assim como parâmetros desconhecidos e tipos incorretos. A revisão deve ser a mais recente.

Consulte `nexa_get_edit_preview` (inclui PNG), `nexa_get_edit_sheet` e `nexa_export_spritesheet` para revisar. `nexa_undo_edit` desfaz o ciclo. `nexa_commit_edit` solicita aprovação local; não publica o asset. O gerador antigo `nexa_generate_animation` mantém sua semântica de offsets.

## Limites e compatibilidade

- Uma ou quatro direções, quatro fases, 1–16 camadas e orçamento total de 2 MiB. A estimativa é verificada antes de criar as poses. Todos os layers devem estar desbloqueados para evitar desalinhamento parcial.
- Drafts com animação anterior são rejeitados sem alterações. Use outro draft com a pose desejada. Sprites totalmente transparentes são rejeitadas no modo automático; regiões de um pixel são aceitas e podem produzir poses iguais.
- Fora de cada retângulo, todos os bytes RGBA permanecem iguais, inclusive RGB sob alpha zero. Os metadados de camadas são preservados; novas fases recebem identidades próprias. A origem e a referência não são modificadas.
- 96×96 e 128×128 não são suportados. Um sprite ID legacy abre um tile de 32×32. Para um canvas maior, comece com PNG/canvas/draft completo.
- Formatos sem `improvedAnimations` não gravam os tempos padrão. A prévia mostra esse aviso antes do Apply. Escolha explicitamente **quatro fases de 100 ms**, ou exporte o draft; não há conversão silenciosa.
- Não há garantia de animação em repouso no Tibia 8.6. O adaptador legacy continua criando o idle de outfit a partir do primeiro frame e a sequência completa no grupo seguinte, quando o formato usa frame groups. Não houve mudança na engine do cliente.
- O Studio cria um objeto rasterizado novo; não preserva automaticamente os canais especiais de dye de um outfit existente. O fluxo específico Recolor mantém suas proteções de templates e sprites compartilhadas.

## Validação

`tests/run-mcp.ps1` executa o motor C#, cliente MCP HTTP real e testes AIR do Studio. Inclui oito combinações de geometria/direções, RGBA, regiões, âncora, fases, duração, determinismo, limites, locks, erros atômicos, histórico, sidecar, atlas e igualdade entre chamadas locais/remotas. A UI AIR cobre captura da seleção antes do reload e Undo/Redo pelo fluxo manual. `tests/run-recolor.ps1` verifica a convivência com recoloração.

As imagens `breathing-biped.png` e `breathing-unusual.png`, geradas na pasta de resultados, servem para revisão de silhuetas sintéticas. Testes de pixels não certificam qualidade anatômica de qualquer monstro, nem reprodução dentro do cliente de jogo.

O teste AIR também recebe JSON gerado pelo motor C#, verifica a recusa dos tempos variáveis no formato 8.60 e grava/reabre a variante explícita de 100 ms, conferindo os 64 tiles de um ciclo 64×64 com quatro direções e quatro fases. A conversão de alpha para SPR RGB segue o formato original; preserve `.nexaart` para RGBA exato.
