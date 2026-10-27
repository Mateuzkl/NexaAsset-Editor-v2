# Geração opcional e ferramentas locais

MCP é o canal de ferramentas. Recoloração exata, flip, rotação de 90°, deslocamento, camadas, PNG, sheets e edição de pixels são locais e independem de qualquer conta de IA. `nexa_generate_animation` aceita offsets explícitos por tempo e deriva movimento da primeira célula de cada direção. Não inventa anatomia/caminhada nem afirma consistência artística.

`IArtGenerationProvider` recebe prompt, largura/altura e RGBA de referência. `HttpArtGenerationProvider` é um adaptador HTTPS para um serviço configurado pelo próprio usuário, não uma integração proprietária automaticamente habilitada.

Antes de iniciar o NexaAsset, forneça `NEXA_ART_PROVIDER_URL` (HTTPS) e, se o serviço exigir, `NEXA_ART_PROVIDER_TOKEN` no ambiente do processo. O token é mantido em memória; não é gravado em config/log. A aplicação não lê credenciais arbitrárias. No Studio, marque **Permitir enviar referências ao provedor IA configurado** e informe o prompt. O mesmo consentimento habilita `nexa_generate_variation` para MCP nesta sessão.

O serviço deve aceitar POST JSON:

```json
{"schemaVersion":1,"prompt":"variação de armadura azul","width":32,"height":32,"referencePng":"BASE64","pixelArt":true,"transparent":true}
```

Resposta: `{"png":"BASE64_DE_UM_PNG"}`. O adapter valida CRC, formato, tamanho e dimensões exatas, sem redimensionar imagens borradas. Máximo 1 MiB de resposta e 20 segundos; redirects são recusados. O provider não recebe o projeto inteiro nem paths locais. Envia apenas o prompt e a imagem de referência necessária. Desmarcar o consentimento bloqueia solicitações seguintes.

Não há provedor real ou créditos incluídos. Custos, privacidade e retenção dependem do serviço configurado. O resultado permanece no draft e exige revisão/aplicação normal; qualidade de contorno, sombra e animação exige inspeção humana. Esta interface não traduz automaticamente os formatos de requisição de OpenAI/Anthropic/outros: um endpoint que implemente esse contrato deve ser configurado.
