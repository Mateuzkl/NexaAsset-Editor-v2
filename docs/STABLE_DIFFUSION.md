# Stable Diffusion no NexaAsset

Stable Diffusion é geração de imagem por texto/referência. A integração é opcional: o agente MCP continua podendo desenhar diretamente por pixels. Este adaptador gera **uma camada de uma célula do draft**. Categoria, frames, direções e aplicação continuam pertencendo ao Studio. Gerar um outfit não cria automaticamente uma caminhada coerente em quatro direções.

## Avaliação dos projetos

Fontes primárias consultadas em 27/10/2026:

| Projeto | API / licença do software | Decisão para o Nexa |
|---|---|---|
| [AUTOMATIC1111](https://github.com/AUTOMATIC1111/stable-diffusion-webui), [API oficial](https://github.com/AUTOMATIC1111/stable-diffusion-webui/wiki/API) | WebUI `/sdapi/v1/txt2img` e `/img2img`, habilitados por `--api`; AGPL-3.0 | Contrato pequeno, acessível por HTTP nativo sem incorporar Python. Implementado. |
| [Forge](https://github.com/lllyasviel/stable-diffusion-webui-forge), [rotas no código oficial](https://github.com/lllyasviel/stable-diffusion-webui-forge/blob/main/modules/api/api.py) | API WebUI; AGPL-3.0; foco declarado em gestão de memória/performance | Mesmo adaptador. Possível ponto de partida para GPU com pouca VRAM, sujeito a teste local. |
| [ComfyUI](https://github.com/Comfy-Org/ComfyUI), [protocolo oficial](https://docs.comfy.org/development/comfyui-server/comms_routes) | Workflows em `/prompt`, fila/histórico e `/view`; GPL-3.0 | Mais flexível, mas exige outro adaptador e workflow definido. **Não suportado por este adaptador WebUI.** |

Vale a pena como ferramenta de composição/variação. A redução para 32/64 pixels pode perder detalhes; nearest-neighbour e paleta limitada não garantem clusters, contorno ou animação de qualidade. A inspeção no Studio continua necessária. Não foi incorporado código desses projetos, executáveis, checkpoints ou LoRAs. As licenças dos modelos são separadas das licenças dos servidores.

## Instalar o servidor separado

1. Siga as instruções oficiais do [WebUI](https://github.com/AUTOMATIC1111/stable-diffusion-webui#installation-and-running) ou [Forge](https://github.com/lllyasviel/stable-diffusion-webui-forge#installing-forge). Instale um checkpoint compatível, observando sua licença.
2. Inicie com `--api`, mantenha o listener local e selecione o checkpoint na interface do servidor. Normalmente o endereço é `http://127.0.0.1:7860`; a documentação interativa fica em `/docs`.
3. No **Sprites → Pixel Art / MCP**, abra **Stable Diffusion**, confirme o endereço numérico local e clique **Configurar / testar**. O teste consulta `/sdapi/v1/sd-models`: verifica a API e se existem checkpoints, sem gerar imagens.
4. Marque o consentimento, informe a descrição e clique **Gerar draft**. O servidor recebe somente texto e, no modo variação, a camada selecionada como PNG. Depois revise pixels/transparência e aplique pelo Studio.

O Nexa não instala o servidor nem baixa modelos automaticamente. Em GPU de 4 GB, comece avaliando um checkpoint leve baseado em SD 1.5, 512 pixels, batch 1 e poucos passos. Isso é uma sugestão de configuração inicial, não uma medição de velocidade/compatibilidade nesta GPU. Pode ser necessário ajustar o ambiente do servidor conforme sua documentação.

Sem servidor, **Configurar / testar** informa que não conseguiu conectar; sem checkpoint, informa que falta o modelo. O endpoint fica apenas na memória do gateway. Reabrir só o Studio mantém a configuração; reiniciar o editor exige configurá-la novamente. Fechar o Studio revoga o consentimento.

Se houver `--api-auth`, defina `NEXA_SD_USERNAME` e `NEXA_SD_PASSWORD` no ambiente antes de iniciar o Nexa. Credenciais não são aceitas no URL, não aparecem no MCP e não são salvas em arquivos pelo adaptador.

## Controles e limites

- Modo `auto`: camada vazia usa txt2img; camada com alpha usa img2img. `create` força texto; `variation` exige referência.
- Seed -1 significa aleatória; use uma seed fixa 0–4294967295 para solicitar repetição, mantendo modelo/configuração do servidor iguais. Passos 1–40; CFG 1–15; força 0–1 em img2img.
- Uma imagem por trabalho; eixo maior 512, preservando proporção (32×64 trabalha em 256×512). Entrada e saída são convertidas por vizinho mais próximo. Paleta 0 mantém RGB; 16/32/64 usa redução determinística. Não há interpolação suave.
- Transparência `auto` preserva alpha exato da referência quando existe; caso contrário remove cores próximas às dos cantos conectadas à borda. `reference`, `corners` e `opaque` permitem escolher a política. Essa remoção simples pode apagar partes do objeto que toquem a borda e não remove buracos internos: revise manualmente.
- PNG validado, dimensões exatas de trabalho, máximo 2 MiB de PNG / 8 MiB de resposta HTTP. Até 180 s por geração; teste de conexão até 5 s. Um único trabalho entre os provedores por gateway.
- Endpoints somente HTTP/HTTPS em IP numérico de loopback, sem paths/query/credenciais; sem redirects, DNS ou proxy no cliente SD. MCP não configura endpoints, modelos, scripts, downloads ou workflows.

## MCP

Após configuração/consentimento local, o agente chama:

```json
{"editId":"ID_REAL","expectedDocumentRevision":0,"parameters":{"prompt":"Sauron helmet, dark iron, orange glow","frame":0,"layer":0,"mode":"create","seed":42,"steps":20,"cfgScale":6,"strength":0.35,"paletteColors":32,"background":"corners","negativePrompt":"text, multiple objects"}}
```

`nexa_generate_stable_diffusion` retorna imediatamente `jobId` e `status: running`. Consulte `nexa_get_generation_status(editId)` até `complete`, `failed`, `conflict` ou `cancelled`; `none` indica ausência de trabalho para aquele draft. Um sucesso publica uma revisão e um passo de undo; atualize o preview antes da próxima operação. `nexa_cancel_generation(editId)` cancela o resultado local. O servidor pode continuar calculando: não usamos `/interrupt`, que interromperia também trabalhos feitos fora do Nexa.

Não há espera de três minutos dentro de uma requisição AIR/MCP. Rede ocorre fora do bloqueio de documentos. Publicação revalida sessão, consentimento, identidade do draft, lock e revisão. Edição concorrente produz conflito, sem sobrescrever o usuário. Fechar a janela de geração cancela seu trabalho; fechar Studio, pausar MCP, revogar consentimento, descartar o draft ou trocar projeto também impedem publicação tardia. O resultado não aplica/salva assets automaticamente.

## Verificação desta entrega

`tests/run-mcp.ps1` cobre contrato WebUI, HTTP real contra stub local, texto/variação, conversão/alpha/paleta, limites/erros, jobs sem bloqueio, cancelamento, consentimento, sessão e conflitos. O AIR abre a janela em inglês/português, captura layout e valida erro de endpoint; o cliente MCP oficial descobre as três ferramentas, recebe erros acionáveis sem configuração e gera um draft completo através do gateway real contra o stub WebUI. Os fluxos de outfit moderno/legado continuam passando.

**Não houve inferência real de Stable Diffusion nesta entrega:** o computador ainda não tinha servidor nem checkpoint instalado. O stub valida integração/protocolo, não qualidade do modelo, consumo de VRAM ou compatibilidade de cada instalação do WebUI/Forge.
