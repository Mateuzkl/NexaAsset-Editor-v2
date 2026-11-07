# Animação de Repouso

Selecione um objeto na lista, abra **Texture → Appearance** e clique em **Respiração / Animação de Repouso**. O Pixel Art Studio abre um draft com a pose do grupo, quadro, addon e montaria selecionados. A leitura reúne todos os tiles da pose e as direções disponíveis. Para escolher a pose de origem de um outfit animado, ajuste primeiro o quadro em Appearance.

Na janela **Animação de Repouso**, escolha um preset ou marque os movimentos individualmente.

- **Respiração** eleva a parte superior e expande a região central no pico da inspiração. O preset de quatro fases segue os tempos do exemplo Python de referência, 500, 300, 500 e 300 ms.
- **Bobbing** alterna o deslocamento vertical.
- **Sway** alterna a oscilação lateral.

Cada movimento aceita 1 ou 2 pixels. Use 3 a 16 fases e informe uma duração inteira de 10 a 10.000 ms para cada uma, separadas por vírgula. **Ancorar base/pés** reduz o movimento até zero na última linha da região e, no modo automático, deixa o quarto inferior da silhueta fora da transformação. O modo automático usa os limites dos pixels visíveis. Para controlar a área, feche as opções, selecione um retângulo com `select` e use **Marcar região** em cada direção antes de abrir a janela novamente.

Clique em **Gerar no draft** e use Play, Sheet e a referência original no Studio para comparar. Undo e Redo tratam o ciclo como uma única operação. Exporte PNG/spritesheet ou `.nexaart` se quiser manter o trabalho editável. **Aplicar como NOVO asset** adiciona o resultado ao projeto. Use **Save/Compile** para gravar o projeto em disco.

## MCP

Use `nexa_get_project_info` para obter a identidade e revisão do projeto. `nexa_begin_asset_edit` aceita `projectSessionId`, `expectedRevision`, `category`, `assetId` e, opcionalmente, `group`, `frame`, `patternY` e `patternZ`, todos com índices a partir de zero. A ferramenta cria um draft com um instante e todas as direções da pose. Também é possível começar de um draft existente com um instante.

Passe o ID e a revisão retornados a `nexa_generate_idle_animation`.

```json
{
  "editId": "id-do-draft",
  "expectedDocumentRevision": 0,
  "parameters": {
    "breathing": true,
    "bobbing": true,
    "sway": true,
    "breathingAmplitude": 1,
    "bobbingAmplitude": 1,
    "swayAmplitude": 1,
    "frameCount": 4,
    "durationsMs": [500, 300, 500, 300],
    "anchorBase": true,
    "regionMode": "auto",
    "regions": []
  }
}
```

Em `rectangle`, envie uma região `{direction,x,y,width,height}` por direção. Use **Só a direção atual** na janela ou `direction` (0–3) nos parâmetros MCP para animar uma direção e repetir as outras sem alteração. Nesse caso, passe apenas a região da direção selecionada. Use `nexa_get_edit_preview`, `nexa_get_edit_sheet` e `nexa_export_spritesheet` para revisar. `nexa_undo_edit` desfaz a geração, e `nexa_commit_edit` solicita revisão e Apply no editor. `nexa_generate_breathing` mantém os parâmetros e o ciclo de quatro fases existentes. Os dois comandos usam o mesmo motor local.

## Formatos e validação

- Canvases de 32×32, 32×64, 64×32 e 64×64. A ordem das células é fase × direções + direção, com N/E/S/W quando há quatro direções. O draft suporta até 64 células, 16 camadas e 2 MiB de pixels. Use um novo draft com a pose desejada para partir de uma animação existente.
- Cada fase parte dos pixels originais. A reamostragem usa coordenadas inteiras e preserva RGBA fora da região. A folga da região limita a deformação em sprites encostadas na borda. Regiões pequenas ou amplitudes subpixel arredondadas podem produzir fases visualmente iguais. No ciclo de quatro fases de respiração, inspiração e expiração intermediárias compartilham a mesma pose, como no Python. Camadas bloqueadas impedem a geração. No modo automático, direções vazias são mantidas e uma pose totalmente vazia é rejeitada.
- Camadas de template de outfit entram como referências ocultas. Apply cria um novo asset rasterizado. Os canais de dye e os grupos da origem permanecem no outfit original.
- No DAT/SPR sem tempos por fase, use o perfil explícito de 100 ms. Quando o formato usa frame groups, o Apply existente guarda a primeira pose em **Idle / Stand** e o ciclo completo em **Walking** no asset novo. A reprodução em repouso no jogo depende do cliente e fica fora da validação de armazenamento deste recurso.
- `tests/run-mcp.ps1` cobre geração e combinações, equivalência dos bytes da UI e MCP, história, revisões, PNG/atlas, 8.60 Save/Compile/Reopen e a entrada de Appearance nos dois formatos. `tests/run-recolor.ps1` cobre recoloração e seu MCP. O arquivo `idle-all-directions.png` na pasta de resultados do Studio mostra as quatro fases e direções de uma fixture.
- `tests/run-recolor.ps1 -RealAssets <pasta-do-cliente> -RealOutfitId 7` cobre o DAT/FileParts do projeto 792. O teste gera `bin-tests/idle-outfit-7.png` pelo botão de Appearance e verifica que a origem permanece intacta. A leitura da pose usa uma direção e uma camada por resposta e verifica a revisão durante a montagem do draft.
