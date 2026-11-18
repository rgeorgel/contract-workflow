# Operação

## Qual arquivo do Compose é lido

| Arquivo | Conteúdo | Quando entra |
| --- | --- | --- |
| `docker-compose.yml` | app, Gotenberg, S3 (SeaweedFS), backup | Sempre |
| `docker-compose.override.yml` | Postgres local, Mailpit, portas de debug, ajustes de dev | **Automaticamente**, se o arquivo existir na pasta |

O `docker compose` procura o `docker-compose.override.yml` sozinho e junta com o base. Para usar **só o base**, escolha um jeito:

1. **`COMPOSE_FILE` no `.env`** (recomendado no servidor): com `COMPOSE_FILE=docker-compose.yml`, todo `docker compose ...` naquela pasta ignora o override, sem precisar lembrar de flag. O [.env.example](../.env.example), modelo do servidor, já vem assim; o de desenvolvimento é o [.env.example.local](../.env.example.local).
2. **`-f` explícito**: `docker compose -f docker-compose.yml up -d`. Repita o `-f` em todo comando (`logs`, `ps`, `down`).
3. **Não copiar o override** para o servidor.

Para conferir o que vai rodar: `docker compose config --services` lista os serviços, e `docker compose config` mostra a configuração final já mesclada.

## Servidor de testes

```bash
cp .env.example .env   # preencha DB_HOST, DB_PASSWORD, S3_*, RESEND_API_KEY, EMAIL_FROM, AUTENTIQUE_*, PUBLIC_BASE_URL
docker compose up -d --build
curl -fs http://localhost:7700/health/ready
```

- O banco é externo: crie o database e o usuário antes. Com `MIGRATE_ON_STARTUP=true`, a app cria as tabelas na subida; o usuário precisa poder criar extensões (`unaccent`, `pg_trgm`) ou elas devem ser criadas antes por um superusuário.
- Fora de `Development` os cookies são `Secure`: o acesso precisa ser por HTTPS (proxy na frente, com `FORWARDED_HEADERS=true`). Por HTTP o login não funciona.
- Sem `DB_HOST` ou `DB_PASSWORD` no `.env`, o Compose para com um erro dizendo o que falta.

## E-mails (Resend)

Confirmação de cadastro, convites e nova senha saem pela [Resend](https://resend.com) (`EMAIL_PROVIDER=Resend`, o padrão do `docker-compose.yml`):

1. Em resend.com/domains, adicione o domínio do remetente e publique os registros DNS (SPF/DKIM) que ela mostrar, até o domínio aparecer como verificado.
2. Em resend.com/api-keys, crie uma chave com permissão **Sending access** (de preferência restrita a esse domínio).
3. No `.env` do servidor: `RESEND_API_KEY=re_...`, `EMAIL_FROM=nao-responda@seu-dominio` (do domínio verificado) e, se quiser, `EMAIL_FROM_NAME`.

Sem `RESEND_API_KEY` a app não sobe e o log diz o que falta. Se a Resend devolver 429 ou 5xx, a app tenta de novo até 3 vezes com a mesma chave de idempotência (o e-mail não sai duplicado); outros erros (remetente não verificado, chave inválida) aparecem no log com o código e o nome do erro da Resend, sem o destinatário. Para usar um SMTP qualquer no lugar: `EMAIL_PROVIDER=Smtp` e as variáveis `SMTP_*`.

Em desenvolvimento o `docker-compose.override.yml` força `Email__Provider=Smtp` com o Mailpit (http://localhost:8026): nada sai para a Resend.

## Assinatura (Autentique)

Uma conta Autentique da plataforma atende todos os clientes. `SIGNATURE_PROVIDER=Autentique` liga a integração; `Fake` mantém o simulador (botões "Abrir/Assinar/Recusar" na tela do contrato) e não fala com a Autentique.

1. **Chave da API**: no painel da Autentique, em painel.autentique.com.br/perfil/api, gere a chave e ponha em `AUTENTIQUE_API_KEY`.
2. **Webhooks**: em painel.autentique.com.br/perfil/webhooks, cada endpoint ouve um tipo de entidade só, então cadastre dois, os dois com a URL `https://<seu-domínio>/api/v1/webhooks/autentique` e formato JSON:
   - **Document**: `document.finished` e `document.deleted`;
   - **Signature**: `signature.viewed`, `signature.accepted`, `signature.rejected` e `signature.delivery_failed`.

   Ponha o código de cada endpoint em `AUTENTIQUE_WEBHOOK_SECRET`, separados por vírgula (`codigo-documentos,codigo-assinaturas`). A URL precisa ser pública e HTTPS (em dev, use um túnel como Cloudflare Tunnel ou ngrok).
3. **Sandbox**: `AUTENTIQUE_SANDBOX=true` cria documentos de teste, que não gastam créditos e somem depois de alguns dias. Em produção, `false`.

Sem a chave ou o secret, a app não sobe e o log diz o que falta.

Como funciona:

- **Envio**: o worker manda o PDF (`createDocument`, upload multipart) com os signatários (e-mail, WhatsApp ou SMS, CPF quando informado), a ordem, o prazo e o lembrete da organização. Falha (rede, limite de 60 requisições por minuto, falta de créditos) volta para a fila: 1, 5, 15 e 60 min; depois disso o contrato fica como "Erro no envio" com a mensagem da Autentique.
- **Webhooks**: validados por HMAC-SHA256 do corpo bruto (cabeçalho `X-Autentique-Signature`), gravados e processados em segundo plano; reentregas do mesmo evento são ignoradas. Webhook com assinatura inválida recebe 401.
- **Reconciliação**: a cada 30 min, até 20 contratos em aberto há mais de 24 h (sem consulta nas últimas 24 h) são consultados na API, e o que faltou (visualização, assinatura, recusa, conclusão) é aplicado. Cobre webhooks perdidos.
- **PDF assinado**: quando o documento termina, o worker baixa o PDF assinado e guarda no storage.
- **Cancelar ou excluir**: o prazo do documento na Autentique vence na hora (para ninguém mais assinar) e o documento é excluído lá.
- **Dados recusados** (e-mail em formato que a Autentique não aceita, SMS para telefone de fora do Brasil): sem retentativa, o contrato vai direto para "Erro no envio" com uma mensagem que diz qual signatário e qual campo. O botão "Voltar para rascunho e corrigir" leva à etapa de signatários; o PDF é gerado de novo no envio.
- **Saldo**: a cada 6 h o worker consulta o saldo da conta (`me { subscription }`). Abaixo de `AUTENTIQUE_LOW_DOCUMENTS` documentos, ou quando um envio é recusado por falta de créditos, o `/health/ready` fica degradado (`signature-balance`) e `AUTENTIQUE_ALERT_EMAIL` recebe um e-mail, no máximo um por dia. Sem créditos, os envios continuam na fila tentando de novo.

Para conferir depois de configurar: crie um contrato com o seu próprio e-mail como signatário, envie, assine pelo link da Autentique e veja o status mudar para Concluído e o "PDF assinado" aparecer. Se o status não mudar, veja os "Error Logs" na tela de webhooks da Autentique e procure por `Webhook recusado` no log da app. A mensagem diz se o cabeçalho `X-Autentique-Signature` não veio (a Autentique não está assinando; o log lista os cabeçalhos que vieram) ou se veio e não confere com nenhum secret configurado.

## Planos e créditos

O catálogo de planos e de pacotes fica em código (`src/Contratos.Domain/Billing/Plans.cs`):

| Plano | Mensal | Anual | Contratos/ciclo | Usuários | Templates | Canais |
|---|---|---|---|---|---|---|
| `avulso` | sem mensalidade | – | 0 (só créditos) | 1 | 2 | e-mail, WhatsApp e SMS |
| `essencial` | R$ 39,90 | R$ 399 | 20 | 3 | 10 | + WhatsApp |
| `profissional` | R$ 89,90 | R$ 899 | 80 | 10 | ilimitados | + SMS |
| `empresa` | R$ 199,90 | R$ 1.999 | 250 | ilimitados | ilimitados | + SMS |

Pacotes avulsos: 1 (R$ 3,90), 5 (R$ 17,50), 10 (R$ 29,90), 25 (R$ 64,90) e 50 (R$ 119,90), válidos por 12 meses.

Regras:

- **Ciclo:** o mês do calendário (horário de Brasília) ou, quando definido, o período pago da assinatura.
- **Ordem de consumo:** o envio usa primeiro a cota do plano no ciclo. Quando ela acaba, usa o crédito avulso que vence antes. Sem cota e sem crédito, o envio é recusado (`quota_exceeded`).
- **O que conta:** o envio desfeito antes de chegar à Autentique não conta e devolve o crédito. Isso vale para voltar para rascunho, cancelar ou excluir. Depois que chegou à Autentique, conta, mesmo cancelado.
- **Avisos:** os Admins recebem e-mail ao chegar em 80% e em 100% da cota.
- **Downgrade:** quem já passou do limite de usuários ou templates continua com o que tem, mas não cria mais. Canais fora do plano são recusados ao adicionar signatário e no envio.
- **Contas novas** entram em `BILLING_DEFAULT_PLAN` (padrão `avulso`). Contas que já existiam quando os planos entraram foram para `empresa`.
- **Avulso:** não tem cota mensal; cada contrato enviado usa 1 crédito.
  - Ao confirmar o e-mail do cadastro, a conta ganha **3 contratos de boas-vindas, válidos por 30 dias**. Uma vez por organização, e só se ela entrar no Avulso.
  - Depois, compra pacotes ou assina um plano.
  - Os Admins recebem e-mail quando resta 1 crédito e quando os créditos acabam.
  - Contas que estavam no antigo plano Grátis viraram Avulso e ganharam os mesmos 3 contratos de boas-vindas.

Sem o Stripe configurado (ou para casos especiais, como cortesia), a equipe troca o plano e concede créditos pelo [portal super admin](#portal-super-admin) ou pela API da plataforma. Ela só existe com `PLATFORM_API_KEY` definida (mínimo de 32 caracteres; gere com `openssl rand -hex 32`). A API não usa cookie: a chave vai no cabeçalho.

```bash
# Organizações (busca por nome ou slug), com plano, uso do ciclo e créditos
curl -s -H "Authorization: Bearer $PLATFORM_API_KEY" "https://SEU_DOMINIO/api/platform/organizations?q=aurora"

# Trocar o plano (ciclo opcional; sem ele vale o mês do calendário)
curl -s -X PUT -H "Authorization: Bearer $PLATFORM_API_KEY" -H "Content-Type: application/json" \
  -d '{"plan":"profissional"}' https://SEU_DOMINIO/api/platform/organizations/SLUG/plan

# Conceder créditos (reference opcional: repetir a mesma não duplica)
curl -s -X POST -H "Authorization: Bearer $PLATFORM_API_KEY" -H "Content-Type: application/json" \
  -d '{"quantity":10,"note":"Pacote 10 pago por Pix","reference":"pix-2026-11-15-aurora"}' \
  https://SEU_DOMINIO/api/platform/organizations/SLUG/credits
```

`BILLING_CONTACT_EMAIL`, se definido, aparece em Configurações como o contato para mudar de plano ou comprar contratos.

## Portal super admin

Em `/superadmin/` a equipe da plataforma acompanha e gerencia todas as organizações:

- **Painel**: total de organizações, cadastros e contratos enviados por dia (30 dias), ativas em 7 dias, MRR estimado pelos preços de tabela, distribuição por plano e por situação, contas que pedem atenção (pagamento pendente, suspensas, cancelamento agendado)
- **Tenants**: cartões ou lista com plano, situação, uso do ciclo (contratos, usuários, templates, créditos), contratos por status, integrações e último acesso. Busca por nome, slug ou e-mail de qualquer usuário; filtros por situação, plano e atividade; ordenação por último acesso, cadastro, nome ou contratos em 30 dias
- **Detalhe**: linha do tempo agrupada por dia e tipo (conta, contratos, assinaturas, templates, plano, integrações e ações da equipe), usuários com último acesso, templates mais usados, créditos e assinatura
- **Ações**: trocar plano (só sem assinatura ativa no Stripe), conceder créditos, suspender e reativar. Toda ação fica registrada (`platform_audit_log`) com quem fez; as feitas pela API da plataforma aparecem como "API da plataforma"

Quem entra: usuários cujo e-mail está em `PLATFORM_ADMIN_EMAILS` (separados por vírgula), pelo login normal e com o e-mail confirmado. A pessoa continua sendo de uma organização (a da equipe, por exemplo); o link "Super admin" aparece no menu dela. Tirar o e-mail da lista corta o acesso em até 1 minuto (quando o cookie é revalidado).

Suspender uma organização: ninguém dela entra (login recusado com `account_suspended`, sessões abertas caem em até 1 minuto), as chaves de API e as sessões de embed param de valer. Contratos já enviados seguem com os signatários e os webhooks continuam saindo. A equipe não consegue suspender a própria organização.

"Último acesso" é gravado no login e quando a pessoa abre uma página (no máximo a cada 5 minutos); contas de antes desta versão aparecem como "Nunca acessou" até o próximo acesso.

## Assinatura online (Stripe)

Os clientes assinam e gerenciam o plano sozinhos, pagando **só com cartão de crédito**: o pagamento confirma na hora e o plano ativa na volta do checkout. Sem `STRIPE_SECRET_KEY`, Configurações mostra o contato (`BILLING_CONTACT_EMAIL`) no lugar dos botões.

Como funciona:

- **Assinar:** "Assinar mensal/anual" em Configurações (só Admin) abre o Stripe Checkout. Na volta, o sistema confere a sessão e ativa o plano na hora, sem esperar o webhook.
- **Gerenciar:** "Gerenciar assinatura" abre o portal do Stripe para trocar de plano, trocar o cartão, atualizar os dados de cobrança (CNPJ), ver faturas e cancelar.
  - Upgrade cobra a diferença na hora.
  - Downgrade e troca de anual para mensal ficam para o fim do ciclo.
  - Cancelar mantém o plano até o fim do ciclo pago.
- **Ciclo:** a cota de contratos segue o período pago da assinatura.
- **Pagamento atrasado (`past_due`):** nada novo, **na hora e sem tolerância**.
  - Ficam bloqueados: criar e enviar contratos, criar, duplicar ou desarquivar templates, e convidar ou reativar usuários. A API responde 402 (`payment_overdue`).
  - Continua disponível o que já existe: ver e baixar contratos, acompanhar, cancelar e reenviar links. Os contratos enviados já foram pagos pela assinatura ou pelos créditos da época.
  - Todas as páginas mostram um aviso no topo; para Admins, com o botão para atualizar o pagamento.
  - Os Admins recebem um e-mail, uma vez por atraso.
  - Quando o pagamento é aprovado (cartão trocado no portal ou nova tentativa do Stripe), tudo é liberado na hora.
- **Pacotes avulsos:** "Comprar" na tabela de pacotes em Configurações (só Admin) abre um pagamento único, só cartão, com o preço de `Plans.cs`.
  - Os créditos entram na volta do pagamento e de novo pelo webhook `checkout.session.completed`, uma vez por sessão.
  - O valor pago precisa ser o do pacote no catálogo; se não for, nada é liberado e o erro vai para o log.
  - Valem 12 meses e funcionam em qualquer plano, inclusive o Avulso.
- **Fim da assinatura:** cancelada ou não paga (`canceled`, `unpaid`), a conta passa para o Avulso e segue enviando com os créditos que tiver.
- **Webhooks:** cada evento é guardado na `webhook_inbox` (o mesmo evento reenviado conta uma vez só). O worker então busca a assinatura atual na API do Stripe, então a ordem dos eventos e a versão da API do webhook não importam.
- **Plano e preços:** os produtos e preços no Stripe são criados pelo sistema na primeira assinatura, a partir de `Plans.cs`.
  - Produtos `contratos_<plano>`; preços com lookup key `contratos_<plano>_mensal` e `contratos_<plano>_anual`.
  - Não edite preços no painel: mude no código. No deploy seguinte, o sistema cria o preço novo e quem já assina continua no antigo até trocar.
  - A configuração do portal ("Contratos") também é criada e mantida pelo sistema.
- **Troca manual pela plataforma:** com assinatura ativa, a API da plataforma recusa trocar o plano (`managed_by_stripe`); o plano é o do Stripe. Créditos avulsos continuam liberados.

### Configurar

1. **Conta:** crie a conta no Stripe com o CNPJ (país Brasil). Comece em **modo de teste**.
2. **Dados públicos:** em Configurações → Dados públicos da empresa, preencha nome, e-mail de suporte e descrição na fatura. Aparecem no checkout e na fatura do cartão.
3. **Chave da API:** em Desenvolvedores → Chaves de API, copie a chave secreta (`sk_test_...`) para `STRIPE_SECRET_KEY`.
4. **Webhook:** em Desenvolvedores → Webhooks → Adicionar destino:
   - URL: `https://SEU_DOMINIO/api/v1/webhooks/stripe`.
   - Eventos: `checkout.session.completed`, `customer.subscription.created`, `customer.subscription.updated`, `customer.subscription.deleted`, `customer.subscription.paused`, `customer.subscription.resumed`, `invoice.paid`, `invoice.payment_failed`.
   - Copie o *signing secret* (`whsec_...`) para `STRIPE_WEBHOOK_SECRET`.
   - A app não sobe com a chave e sem o secret: sem webhook, renovações e cancelamentos não chegariam.
5. **Testar:** reinicie a app e assine pela tela de Configurações com o cartão de teste `4242 4242 4242 4242` (qualquer validade futura e qualquer CVC). Para testar recusa, use `4000 0000 0000 0341` (o cartão é aceito no cadastro e recusado na cobrança).
6. **Recuperação de receita:** em Configurações → Billing → Recuperação de receita (*Revenue recovery*):
   - Deixe as novas tentativas de cobrança ligadas (*Smart Retries*).
   - Escolha o que fazer quando todas falharem: **cancelar a assinatura**. A conta passa para o Avulso e desbloqueia; com "marcar como não paga" também passa para o Avulso.
   - Opcional: ligue os e-mails do próprio Stripe para cartão recusado.
7. **Produção:** troque para as chaves `sk_live_...` e crie o webhook de novo no modo de produção (o *signing secret* é outro). O catálogo é recriado sozinho no modo de produção.

Em desenvolvimento, o webhook chega pelo Stripe CLI: `stripe listen --forward-to localhost:7700/api/v1/webhooks/stripe` mostra o `whsec_...` para usar em `STRIPE_WEBHOOK_SECRET`.

## Alertas e lembretes

O worker procura, a cada 15 minutos (`Worker__AlertIntervalMinutes`), contratos enviados que precisam de atenção:

- **Parado:** esperando assinatura há mais dias que o configurado (padrão: 3).
- **Prazo chegando:** faltam tantos dias para o prazo (padrão: 3). Junto com esse aviso, o link é reenviado a quem ainda não assinou, se "reenviar automaticamente" estiver ligado. Fica na linha do tempo como "Lembrete automático".

Quem criou o contrato recebe **um e-mail por rodada**, com todos os seus contratos que precisam de atenção. Se essa pessoa foi desativada, o aviso vai para os Admins. Cada contrato gera no máximo um aviso de cada tipo por envio; se ele voltar para rascunho e for enviado de novo, os avisos recomeçam.

Os dias (0 desliga) e o reenvio automático ficam em Configurações, "Alertas e lembretes", por empresa. Na primeira rodada depois da implantação, contratos que já estavam parados geram um aviso.

## Envio em lote

Planos com envio em lote (Profissional e Empresa): em Contratos, "Envio em lote".

1. **Template:** escolha o template e baixe a planilha modelo. Ela traz as colunas `titulo`, as variáveis do template, `prazo`, `mensagem`, `em_ordem` e um bloco `signatarioN_*` (nome, email, celular, canal, papel, cpf) por papel sugerido no template.
2. **Planilha:** preencha uma linha por contrato e envie como CSV. O sistema aceita `;` ou `,`, UTF-8 ou o padrão do Excel no Brasil (Windows-1252), e datas em `dd/mm/aaaa`. São até 100 contratos por vez.
3. **Conferir:** a prévia valida tudo no servidor e aponta a linha e a coluna de cada erro.
4. **Criar:** só cria quando todas as linhas estão certas, e cria tudo numa transação, com números em sequência. "Criar rascunhos" para revisar antes; "Criar e enviar" envia um por um.
   - Cada envio usa a cota ou um crédito.
   - Se a cota acabar no meio, o resto fica como rascunho.

API: `POST /api/v1/contracts/bulk` com `{ templateId, rows, dryRun }`.

## API pública e webhooks

Todos os planos (`Plan.ApiAccess`, ligado em todos hoje). Documentação para os clientes em `/desenvolvedores/` (portal público) e `/desenvolvedores/openapi.json`. Um teste confere que o OpenAPI lista exatamente as rotas de `/api/public/v1`: ao criar ou mudar uma rota, atualize o `openapi.json` e o portal.

**Chaves:** criadas por um Admin em Integrações (`/integrations.html`).
- Formato `ctr_` mais 43 caracteres. O banco guarda só o SHA-256 e o começo da chave, para reconhecer na lista.
- A chave autentica só em `/api/public/v1`; o cookie da tela não vale lá, e a chave não vale na `/api/v1`.
- Age como Editor, em nome de quem a criou. A linha do tempo mostra "pela API (chave X)".
- Revogar vale na hora. São até 10 chaves ativas por conta.

**Limite:** 120 requisições por minuto por chave (`PUBLIC_API_PERMITS_PER_MINUTE`). Acima disso a resposta é 429 com `Retry-After`.

**Webhooks:** até 10 endereços por conta, cada um com um segredo `whsec_`, cifrado com o Data Protection.
- **Segredo:** se as chaves do Data Protection se perderem (volume `/keys`), as entregas falham com "Não foi possível ler o segredo". Basta gerar um segredo novo na tela.
- **Quando nascem:** os eventos saem do mesmo `SaveChanges` que grava a linha do tempo do contrato (`WebhookEventWriter`), então não há evento sem mudança nem mudança sem evento. Ficam em `webhook_events` e `webhook_deliveries`.
- **Como são entregues:** o worker entrega no fim de cada rodada.
  - Endpoints diferentes saem em paralelo (8 por vez); no mesmo endpoint, na ordem dos eventos.
  - Cada entrega tem 10 s de timeout e não segue redirecionamentos.
  - Assinatura: `X-Contratos-Signature: t={unix},v1={HMAC-SHA256(segredo, "t.corpo")}`.
- **Tentativas:** 8, depois de 1 min, 5 min, 30 min, 2 h, 6 h, 12 h e 24 h (`Webhooks__RetryDelaysSeconds`). Esgotadas, a entrega fica "Falhou" e os Admins recebem um e-mail, uma vez por sequência de falhas. Na tela dá para entregar de novo.
- **SSRF:** a URL precisa ser `https://` e pública. No cadastro, localhost, IPs privados, link-local (169.254.x, metadados da nuvem) e `.internal`/`.local` são recusados. Na conexão, o IP resolvido é conferido de novo, o que barra um DNS trocado depois do cadastro.
  - `WEBHOOKS_ALLOW_PRIVATE_NETWORKS=true` libera http e rede interna. Só em dev: o `docker-compose.override.yml` já liga.
  - Nunca ligue em produção.
**Embed de templates:** a tela de templates dentro do sistema do cliente, num iframe (`/embed/templates` e `/embed/template-editor`, os mesmos HTML da tela).
- **Sites:** o Admin cadastra em Integrações → Embed os sites que podem mostrar o iframe (até 10; `https://`, `https://*.dominio` ou `http://localhost:porta`). Viram o `frame-ancestors` da CSP da página; sem site cadastrado, `POST /embed/sessions` responde 409 `embed_origin_required`. As outras páginas continuam com `frame-ancestors 'none'`.
- **Sessão:** o servidor do cliente cria com a chave de API (`POST /api/public/v1/embed/sessions`), com permissões (`edit`, `publish`, `archive`), nome de quem usa e duração (5 min a 8 h). A URL leva um token de uso único no `#`, válido por 10 minutos. A página troca esse token por um token de acesso `ces_` (`POST /api/embed/v1/session`), guardado na `sessionStorage` do iframe. O banco guarda só os hashes.
- **API do embed:** `/api/embed/v1` repete as rotas de templates da tela, autenticada só pelo token da sessão (sem cookie nem antiforgery) e com as permissões dela. A pré-visualização é carregada pelo `src` do iframe, então usa um ticket de 10 minutos que só abre aquele template.
- **Encerramento:** a sessão acaba no horário, em `DELETE /embed/sessions/{id}`, ao revogar a chave ou ao remover o site. O worker apaga sessões vencidas há mais de 7 dias.
- **Autoria:** as versões criadas pelo embed ficam no nome do dono da chave, com o nome informado na sessão (`created_by_external`). O histórico mostra "Maria Souza (pela integração)".
- **Webhooks de template:** `template.published` e `template.archived` saem do mesmo `SaveChanges` que publica ou arquiva, tanto pela tela quanto pelo embed.

- **Plano sem API:** hoje todos os planos incluem a API. Se algum plano voltar a ficar sem (`ApiAccess: false`), as chaves dessa conta respondem 403 `api_not_in_plan` e nenhum evento é gerado; nada é apagado, e voltando a um plano com API tudo volta a funcionar.

## Saúde

| Rota | Para quê | Olha |
| --- | --- | --- |
| `GET /health/live` | Reiniciar o processo travado (liveness) | Só se a app responde |
| `GET /health/ready` | Tirar do balanceador quando algo essencial cai (readiness) | Banco e storage (falha = 503); Gotenberg (falha = degradado, 200) |

As respostas trazem só o status por dependência, sem mensagens internas.

## Produção atrás de proxy

A app escuta HTTP na porta 7700. Em produção, ponha um proxy com TLS na frente (Caddy, Nginx, load balancer) e:

- `FORWARDED_HEADERS=true` para a app ver o IP real (rate limit) e o esquema https (cookies `Secure`, HSTS);
- não exponha a porta da app diretamente: com `FORWARDED_HEADERS=true` ela confia no `X-Forwarded-For` de quem estiver na frente;
- `PUBLIC_BASE_URL=https://seu-dominio` (links dos e-mails);
- `ASPNETCORE_ENVIRONMENT=Production` (HSTS, logs em JSON, cookies sempre `Secure`, sem Scalar);
- migrations com bundle antes do deploy: `dotnet ef migrations bundle -p src/Contratos.Infrastructure -s src/Contratos.Api` e rode o executável com a connection string de produção.

## Limites

| Variável | Padrão | Escopo |
| --- | --- | --- |
| `RateLimiting__AuthPermitsPerMinute` | 10 | Cadastro, login, confirmação, senha: por IP |
| `API_RATE_LIMIT_PER_MINUTE` | 300 | Toda a `/api`: por usuário logado, ou por IP |
| `PUBLIC_API_PERMITS_PER_MINUTE` | 120 | API pública (`/api/public/v1`): por chave |
| `API_RATE_LIMIT_PER_MINUTE` | 300 | API do embed (`/api/embed/v1`): por sessão |

## Backup

### Banco

```bash
docker compose --profile backup up -d backup
```

Faz `pg_dump` a cada 24 h em `./backups` (`BACKUP_DIR`) e apaga os com mais de 14 dias (`BACKUP_RETENTION_DAYS`). Um backup avulso:

```bash
docker compose --profile backup run --rm --entrypoint /bin/sh backup /scripts/backup-db.sh
```

Restaurar (substitui os dados do banco de destino):

```bash
docker compose --profile backup run --rm --entrypoint /bin/sh backup /scripts/restore-db.sh /backups/contratos-AAAAMMDD-HHMMSS.dump
```

Teste a restauração de vez em quando num banco separado (`-e PGDATABASE=contratos_teste`). Leve a pasta de backups para fora da máquina (outro bucket, outra região).

### Arquivos

O banco aponta para os PDFs; um sem o outro não basta. No S3 de produção:

- ative **versionamento** no bucket (exclusão acidental vira versão anterior);
- ative **replicação** para outra região ou conta;
- regra de ciclo de vida para expirar versões antigas depois de N dias, se desejado.

## Logs

Em produção, JSON compacto no stdout (`docker compose logs app`). Cada requisição traz método, caminho, status, duração, `UserId` e `OrganizationId`. Ver [lgpd.md](lgpd.md) sobre o que não vai para o log.
