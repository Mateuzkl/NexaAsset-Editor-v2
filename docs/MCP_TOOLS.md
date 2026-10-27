# Ferramentas MCP reais

Leituras de objeto são limitadas a 4.096 referências por resposta, com `referenceCount` e `referencesTruncated`; use get_frame/get_sprite para consultar slots específicos além desse limite.

Os schemas completos são gerados pelo SDK oficial a partir dos métodos em `tools/mcp-gateway/StudioTools.cs` e retornados por `tools/list`. Transporte Streamable HTTP, endpoint `/mcp`, bearer obrigatório. Ferramentas não recebem paths locais.

## Leitura

| Ferramenta | Entrada |
|---|---|
| nexa_get_project_info / nexa_get_capabilities | nenhuma |
| nexa_list_assets | category (item/outfit/effect/missile/sprite), page=0, query vazio |
| nexa_get_asset | category, assetId |
| nexa_get_sprite | spriteId; retorna dimensões, base64 ARGB straight e conteúdo de imagem PNG MCP |
| nexa_get_frame | category, assetId, group=0, frame=0, direction=0, layer=0; padrões Y/Z são zero nesta ferramenta |
| nexa_list_art_documents | nenhuma |
| nexa_get_edit_sheet | editId; todas as células achatadas, columns/rows, directionOrder, RGBA, tempo/direção/duração |
| nexa_get_edit_preview | editId, frame=0; RGBA, hash, diferenças, camadas/timeline e avisos |

Resources: `nexa://session/{sessionId}/sprite/{spriteId}` e `nexa://session/{sessionId}/asset/{category}/{assetId}`, MIME application/json. Incluem bytes/mapeamentos limitados; não incluem paths do usuário.

## Início/fim

- `nexa_begin_edit`: projectSessionId, expectedRevision, width=32, height=32, spriteId=0, category obrigatório (item/outfit/effect/missile), name="". Use category=outfit para personagens; quatro direções são iniciadas automaticamente. Eixos width/height são independentes (por exemplo 32×64). Retorna editId e documentRevision. spriteId existente faz cópia exata.
- `nexa_duplicate_sprite`: projectSessionId, expectedRevision, spriteId.
- `nexa_cancel_edit`: editId. Fecha o documento de arte; quando já aplicado, mantém o asset no projeto.
- `nexa_export_spritesheet`: editId, columns=4. Retorna PNG base64 e metadata schema 1; não escreve no disco.

## Operações em draft

Todas abaixo recebem **editId**, **expectedDocumentRevision** e **parameters** (objeto JSON). A revisão retornada deve ser usada na operação seguinte; uma revisão antiga falha sem alterar pixels. Campos desconhecidos não concedem permissões.

| Ferramenta | parameters |
|---|---|
| nexa_set_asset_type | category (item/outfit/effect/missile),name opcional; destino persistido no draft |
| nexa_create_sprite | width,height; reinicia canvas vazio |
| nexa_set_pixels | frame=0,layer=0,pixels:[{x,y,rgba:[r,g,b,a]}]; até 4096 |
| nexa_apply_palette | frame,layer,colors:[{from:[r,g,b,a],to:[r,g,b,a]}] |
| nexa_transform_sprite | frame,layer,operation: flipX/flipY/rotate90/offset,dx=0,dy=0 |
| nexa_add_layer | frame,name,layer=0,duplicate=false |
| nexa_update_layer | frame,layer,name,visible,locked,opacity 0–255,moveTo,delete,merge |
| nexa_set_animation | frames (tempos),directions 1/4,duration opcional em ms (omitido preserva tempos); até 64 células |
| nexa_import_sprite_png | frame,layer,png base64; dimensões devem coincidir |
| nexa_import_spritesheet | png base64,cellWidth,cellHeight,directions,duration,cells opcional com metadata exportada |
| nexa_generate_variation | prompt,frame,layer; exige provider e consentimento no editor |
| nexa_generate_animation | offsets:[[dx,dy],...]; deriva movimento local, sem inventar caminhada |
| nexa_undo_edit | {} |
| nexa_commit_edit | {}; solicita aprovação, nunca aplica sozinho |

Exemplo de sequência (IDs/revisões devem vir das respostas reais):

```json
{"projectSessionId":"SESSAO_ATUAL","expectedRevision":42,"category":"item","spriteId":512}
```

Chame `nexa_begin_edit` com a entrada acima. Então:

```json
{"editId":"ID_RETORNADO","expectedDocumentRevision":0,"parameters":{"frame":0,"layer":0,"pixels":[{"x":10,"y":12,"rgba":[40,120,220,255]}]}}
```

Chame `nexa_set_pixels`, examine `nexa_get_edit_preview`, e use a revisão atual em `nexa_commit_edit`. A resposta `awaiting-human-approval` mantém o projeto intacto. A categoria já vem do draft MCP e aparece selecionada no Studio. O usuário revisa e clica **Aplicar como NOVO asset**; Save/Compile segue separado.

## Exemplos de pedidos

“Duplique o sprite 512, recolora para espada de gelo e solicite aplicar como novo item.”

“Leia os grupos do outfit 150 e use seus sprites como referência; preserve o original e mostre a variante em draft.”

“Configure quatro direções e três tempos; preencha cada célula com pixels próprios antes de solicitar aprovação.”

“Crie oito frames de efeito, revise o loop e exporte sheet PNG + JSON.”

“Recolora apenas as coordenadas da armadura; deixe os pixels do rosto intactos.”

Não há ferramenta para trocar máscaras/grupos existentes, gravar binários, apagar objetos ou salvar o projeto. Um agente deve consultar as capabilities e explicar essas limitações antes de prometer tais ações.

Inventário atual: 27 ferramentas. A descoberta automatizada cobre os nomes e schemas efetivamente publicados.

As conversões 1↔4 direções preservam os tempos e usam a direção zero ao reduzir. Novos tempos repetem o último tempo de cada direção. Erros esperados chegam ao cliente com textos como `DOCUMENT_CONFLICT` e `PROJECT_CONFLICT`.

Sheets compartilham os limites de import/export: eixo ≤1024, área ≤262144 pixels, PNG ≤2 MiB. Um layout inválido é recusado com sugestão de colunas; o transporte HTTP aceita 3 MiB para o base64.
