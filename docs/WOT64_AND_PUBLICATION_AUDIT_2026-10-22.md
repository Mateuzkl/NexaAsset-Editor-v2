# Publicação legacy e assets Wot 64x64

## Alterações

- Publicação assíncrona, progresso por arquivo, prazo por movimentação e rollback em ordem inversa. Backups e staging necessários à recuperação são preservados.
- Estado explícito de compilação impede alterações e troca de projeto durante publicação/recuperação. Comando terminal libera a interface em sucesso ou falha.
- Barras não agendam mais fechamentos atrasados que poderiam atingir outra operação; callbacks toleram o fechamento síncrono da barra ao completar.
- Detecção de dimensões OTFI, seleção do perfil Wot e leitura em streaming do SPR, inclusive acima de 2 GiB.
- Saída 64x64 preservada; conversão 64→32 divide cada tile em quatro, sem redimensionar a arte. A conversão salva uma cópia em outra pasta e não limpa o estado de alterações do projeto original.
- OBD usa tiles padrão de 32 pixels; importação para um projeto de 64 pixels recompõe os tiles, mantendo a ancoragem inferior direita.
- Sprites temporários obtidos do reader são descartados após a escrita; sprites editados pertencentes ao storage não são descartados.

## Formato customizado e limites

O Wot fornecido contém 673.384 sprites de 64 pixels, DAT com 59.479 como maior ID de item, 2.431 outfits, 450 efeitos e 62 missiles. O diretório `1105` não identifica sozinho uma versão oficial; o par de assinaturas corresponde ao perfil customizado já configurado como 13.10.

A flag `0x28` está presente nos outfits 2417–2425. A análise estrutural confirmou payload de zero bytes e EOF exato, mas **não** estabeleceu sua semântica. O perfil `wot64` preserva essa flag também na cópia retileada para 32 pixels. Exportar esses objetos para OBD padrão é rejeitado explicitamente, evitando perda silenciosa. O perfil Wot não pode ser escrito como um DAT antigo com layout incompatível.

O bridge moderno existente aceita objetos de até 64 pixels físicos por eixo. Objetos Wot maiores continuam exportáveis como OBD dentro dos limites desse formato, mas não são artificialmente reduzidos para caber no bridge. OBD está limitado a 4.096 tiles por grupo. SPR único continua sujeito ao limite de endereçamento de 4 GiB; multi-SPR requer suporte do cliente.

## Validação realizada

- Build de worker, interface e pacote portátil: passou.
- Suítes existentes `tests/run.ps1`: passaram.
- `tests/run-publication.ps1`: sucesso, progresso, backup, arquivo bloqueado, rollback, retry, timeout injetado e rejeição de caminho inseguro passaram.
- `tests/run-wot.ps1`: leitura de 166 sprites amostrados, inclusive acima de 2 GiB; DAT completo compilado/reaberto em 64 e 32 pixels; comparação de todas as referências e preservação das nove flags customizadas passaram.
- Fixtures de item/outfit/effect/missile: retile 64↔32, alpha, camadas, padrões, fases, dois grupos de outfit, OBD v3 repetido e SPR padrão/multi passaram.
- `tests/run-wot.ps1 -FullCompile`: SPR inteiro de 64 pixels compilou/reabriu em 31,29 s (3.242.652.274 bytes); cópia de 32 pixels em 574,17 s (3.273.100.906 bytes), com comparação amostral dos pixels. São medições locais, não garantias de desempenho.
- O harness opcional `tests/run-legacy-publish.ps1` ainda está **inconclusivo**: não confirmou o fluxo completo de importação PNG/replace pela interface. Não é contado como teste aprovado nem prova de reprodução/resolução de todos os travamentos relatados. O fluxo real ainda precisa de teste manual.

Os testes gravaram somente em `bin-tests` no volume D. Os dois SPRs grandes gerados para benchmark foram removidos após a validação; são reproduzíveis pelo script. Os assets Wot originais não foram modificados. Executáveis, datasets e resultados temporários não fazem parte do commit.
