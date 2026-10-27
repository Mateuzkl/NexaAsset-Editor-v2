# Conectar Codex ao NexaAsset aberto

1. Execute o bundle desta branch e abra seu projeto. No painel **Sprites**, clique **Pixel Art / MCP**.
2. Marque **Ativar MCP** e clique **Copiar config.**. O texto não contém o token. Cole em `%USERPROFILE%/.codex/config.toml`; a porta mostrada pertence à instância atual:

```toml
[mcp_servers.nexaasset]
url = "http://127.0.0.1:PORTA/mcp"
bearer_token_env_var = "NEXA_MCP_TOKEN"
```

3. Clique **Copiar token** no Studio. Em um PowerShell que iniciará o cliente, configure sem imprimir o segredo:

```powershell
$env:NEXA_MCP_TOKEN = Get-Clipboard
codex
```

Para Codex desktop/IDE, o processo do cliente precisa receber essa variável no ambiente de inicialização. Uma aplicação já aberta não recebe automaticamente alterações de ambiente de outro terminal. Reinicie o cliente com o ambiente correto; não coloque o segredo no repositório. Instâncias diferentes têm portas/tokens diferentes. Fechar/trocar projeto invalida o processo e seus drafts.

4. Peça: “Use nexa_get_project_info e nexa_list_assets para inspecionar meu editor aberto. Duplique o sprite 1 num draft, altere alguns pixels e solicite aprovação.” O ID deve existir no projeto; consulte a lista antes.
5. Revise o draft no Studio. **Aplicar como NOVO asset** insere na memória do editor. Depois use **Salvar/Compilar** normalmente.

Não há configuração automática da conta do Codex por esta feature. O teste reproduzível usa um cliente padrão do SDK MCP: `tests/run-mcp.ps1`. A CLI/IDE recebe a mesma URL e autenticação HTTP.

Configuração conferida na [documentação oficial do Codex MCP](https://developers.openai.com/codex/mcp) em 27/10/2026 (`url` e `bearer_token_env_var`).
