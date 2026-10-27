# Conectar Claude Code

Abra projeto → Pixel Art / MCP → Ativar MCP. Copie a URL mostrada e forneça o token pela variável `NEXA_MCP_TOKEN`, por exemplo via `Get-Clipboard` num PowerShell que iniciará o cliente. Não escreva tokens reais em configurações versionadas.

Configuração HTTP com expansão de variável de ambiente, em `.mcp.json`:

```json
{
  "mcpServers": {
    "nexaasset": {
      "type": "http",
      "url": "http://127.0.0.1:PORTA/mcp",
      "headers": { "Authorization": "Bearer ${NEXA_MCP_TOKEN}" }
    }
  }
}
```

Inicie `claude` no ambiente que contém a variável. Use `/mcp` para verificar a conexão. A porta e o token mudam quando o processo Studio é reiniciado. O servidor é local: clientes em nuvem não alcançam esse 127.0.0.1.

Peça leitura do projeto, `nexa_begin_edit`, alterações e `nexa_commit_edit`. Este último apenas solicita revisão. Aprove no botão do Studio e salve no editor.

Os contratos seguem a [documentação oficial do Claude Code MCP](https://code.claude.com/docs/en/mcp), consultada em 27/10/2026. Esta entrega verifica o transporte com o SDK MCP oficial; não afirma ter executado Claude Code/Claude Desktop. Clientes que exigem exclusivamente STDIO ou OAuth precisam de uma integração adicional, ainda não incluída.
