# LGPD: dados pessoais no sistema

Revisão da fase 6. Descreve o que o sistema guarda, para quê, onde e por quanto tempo. Não substitui parecer jurídico: a organização cliente é a **controladora** dos dados dos signatários; a plataforma atua como **operadora**.

## Inventário

| Dado | Titular | Onde fica | Finalidade | Retenção |
| --- | --- | --- | --- | --- |
| Nome, e-mail, hash da senha | Usuários internos | `users` | Acesso ao sistema | Enquanto a conta existir; desativar mantém o registro para auditoria |
| Nome, e-mail, telefone, CPF (opcional) | Signatários | `contract_signers` | Entregar o link e, com CPF, restringir quem assina | Até alguém da organização excluir o contrato |
| Valores das variáveis (podem conter nomes, CPF/CNPJ, endereços) | Partes do contrato | `contracts.variable_values`, PDFs no bucket | Gerar o contrato | Até a exclusão do contrato |
| PDFs original e assinado | Partes do contrato | Bucket S3 privado (`org/{org}/contracts/{id}/`) | Prova do que foi assinado | Até a exclusão do contrato |
| Eventos (visualizou, assinou, recusou, motivo da recusa) | Signatários | `contract_events` | Trilha de auditoria | Até a exclusão do contrato |
| Número, título, quem excluiu, quando | — | `deletion_log` | Rastro mínimo de exclusão e contagem de uso | Indefinida (sem conteúdo do contrato nem dados dos signatários) |
| Payload dos webhooks | Signatários (ids do provedor) | `webhook_inbox` | Idempotência e reprocessamento | Ver "Pontos em aberto" |

Nada é apagado por prazo: a exclusão é sempre pedida por um Admin ou por quem criou o contrato, com confirmação pelo número. A exclusão remove contrato, signatários, eventos e PDFs (e, opcionalmente, o documento no provedor).

## Medidas técnicas

- **Isolamento por organização**: filtro global no EF Core (`organization_id`) e testes de integração que tentam ler, alterar, baixar e buscar dados de outra organização.
- **Segredos** (chave da Autentique, secret do webhook, credenciais S3 e SMTP) só em variáveis de ambiente, nunca no banco, no Git ou no frontend.
- **Bucket privado**: PDFs saem só pela API, que confere a organização; não há URL pública.
- **Logs sem dados pessoais**: o log de requisições registra caminho, status, `UserId` e `OrganizationId` (identificadores), nunca a query string; e-mails do sistema não registram destinatário nem assunto. O EF Core não registra valores de parâmetros (`EnableSensitiveDataLogging` desligado).
- **Senhas** com PBKDF2 (ASP.NET Core Identity), bloqueio após 5 tentativas, rate limit nas rotas de autenticação e na API.
- **Transporte**: cookies `Secure`/`HttpOnly`/`SameSite=Strict`, HSTS fora de desenvolvimento, CSP restritiva.
- **Templates sanitizados** e Gotenberg sem acesso à rede (o HTML de um cliente não consegue buscar dados internos).

## Suboperadores

| Serviço | Dados que recebe | Observação |
| --- | --- | --- |
| Autentique | PDF, nome, e-mail/telefone, CPF dos signatários | Uma conta da plataforma para todos os clientes |
| Storage S3 | PDFs | Preferir região no Brasil (AWS sa-east-1, por exemplo) |
| SMTP | Nome e e-mail dos usuários internos | Confirmação de cadastro, convites, nova senha |

## Direitos do titular

- **Usuário interno**: o Admin desativa o acesso; exclusão definitiva hoje é manual (ver abaixo).
- **Signatário**: a organização cliente atende o pedido e, se for o caso, exclui o contrato. Como o contrato assinado pode ter obrigação legal de guarda, a decisão é dela.

## Pontos em aberto

- [ ] Limpar `webhook_inbox` processado após 30 dias (o payload tem ids do provedor e motivos de recusa).
- [ ] Exclusão definitiva de usuário interno (hoje: desativar).
- [ ] Exportação dos dados de uma organização (portabilidade) e encerramento de conta.
- [ ] Criptografia em repouso: ativar no Postgres gerenciado e no bucket (SSE) em produção.
- [ ] Contrato de operador com a Autentique e com o provedor de S3.
