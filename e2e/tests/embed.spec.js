import { expect, test } from '@playwright/test';
import { signUp } from './helpers.js';

// O "sistema do cliente": outra origem (127.0.0.1 em vez de localhost), servida pelo próprio Playwright.
const APP = new URL(process.env.BASE_URL ?? 'http://localhost:7700');
const CRM = `${APP.protocol}//127.0.0.1:${APP.port}`;

// A página do CRM vem do route do Playwright (conta como rede pública) e o app roda em localhost: o Chrome
// barraria o iframe pela checagem de rede local. Em produção os dois são públicos.
test.use({ launchOptions: { args: ['--disable-features=LocalNetworkAccessChecks,BlockInsecurePrivateNetworkRequests'] } });

async function createKey(page) {
  const csrf = (await (await page.request.get('/api/v1/auth/csrf')).json()).token;
  const res = await page.request.post('/api/v1/integrations/keys', { headers: { 'X-CSRF-TOKEN': csrf }, data: { name: 'CRM' } });
  expect(res.status()).toBe(201);
  return (await res.json()).token;
}

/** Página do CRM com o iframe do embed; guarda os avisos (postMessage) em window.messages. */
async function openCrm(page, url) {
  await page.route(`${CRM}/crm.html`, (route) => route.fulfill({
    contentType: 'text/html',
    body: `<!doctype html><html lang="pt-BR"><head><title>CRM</title></head><body>
      <h1>CRM do cliente</h1>
      <!-- Altura que cabe na tela (como recomenda o portal): as janelas de confirmação ficam à vista. -->
      <iframe id="embed" title="Templates de contrato" width="1200" height="600"></iframe>
      <script>
        window.messages = [];
        addEventListener('message', (e) => {
          if (e.origin === ${JSON.stringify(APP.origin)} && e.data && e.data.source === 'contratos') window.messages.push(e.data);
        });
        document.getElementById('embed').src = ${JSON.stringify(url)};
      </script></body></html>`,
  }));
  await page.goto(`${CRM}/crm.html`);
  return page.frameLocator('#embed');
}

const messageTypes = (page) => page.evaluate(() => window.messages.map((m) => m.type));

test('embed: o cliente cria e publica um template dentro do próprio sistema', async ({ page, request, browser }) => {
  await signUp(page, request);

  // Admin cadastra o site do CRM.
  await page.goto('/integrations.html');
  const embedPanel = page.getByRole('region', { name: 'Embed de templates' });
  await embedPanel.getByLabel('Endereço do site').fill(`${CRM}/`);
  await embedPanel.getByRole('button', { name: 'Cadastrar' }).click();
  await expect(embedPanel.getByRole('row').filter({ hasText: CRM })).toBeVisible();

  // O servidor do CRM cria a sessão com a chave de API.
  const key = await createKey(page);
  const created = await request.post('/api/public/v1/embed/sessions', {
    headers: { Authorization: `Bearer ${key}` },
    data: { user: { id: 'u-1', name: 'Maria Souza' }, parentOrigin: CRM },
  });
  expect(created.status()).toBe(201);
  const session = await created.json();

  const crm = await browser.newPage();
  const frame = await openCrm(crm, session.url);
  await expect(frame.getByRole('heading', { name: 'Templates', level: 1 })).toBeVisible();
  // Sem o menu do Contratos: a navegação é a do CRM.
  await expect(frame.getByRole('navigation', { name: 'Principal' })).toHaveCount(0);
  await expect.poll(() => messageTypes(crm)).toContain('ready');

  await frame.getByRole('button', { name: 'Novo template' }).click();
  await frame.getByRole('dialog').getByRole('button', { name: 'Continuar' }).click();
  await frame.getByRole('dialog').getByLabel('Nome').fill('Locação residencial');
  await frame.getByRole('button', { name: 'Criar e editar' }).click();
  await expect(frame.getByRole('heading', { name: 'Locação residencial', level: 1 })).toBeVisible();

  await frame.getByRole('tab', { name: 'HTML' }).click();
  await frame.getByLabel('Texto do contrato em HTML').fill('<h1>Locação</h1><p>Locatário: {{locatario.nome}}</p>');
  await frame.getByRole('button', { name: 'Cadastrar variáveis do texto' }).click();
  // A pré-visualização (iframe dentro do iframe) abre com o ticket e atualiza enquanto edita.
  await expect(frame.frameLocator('[data-frame]').getByText('Locatário:')).toBeVisible();

  await frame.getByRole('button', { name: 'Publicar v1' }).click();
  await frame.getByRole('dialog').getByRole('button', { name: 'Publicar' }).click();
  await expect(frame.getByText('Publicado · v1')).toBeVisible();
  await expect(frame.getByText('Maria Souza (pela integração)')).toBeVisible();
  await expect.poll(() => messageTypes(crm)).toEqual(expect.arrayContaining(['template.created', 'template.saved', 'template.published']));
  const published = await crm.evaluate(() => window.messages.find((m) => m.type === 'template.published'));
  expect(published.template).toMatchObject({ name: 'Locação residencial', publishedVersion: 1 });

  // Já disponível para criar contratos pela API.
  const templates = await (await request.get('/api/public/v1/templates', { headers: { Authorization: `Bearer ${key}` } })).json();
  expect(templates.map((t) => t.name)).toContain('Locação residencial');

  await frame.getByRole('link', { name: '← Templates' }).click();
  await expect(frame.getByRole('row').filter({ hasText: 'Locação residencial' })).toContainText('Publicado · v1');

  // O mesmo link não abre de novo, e encerrar a sessão fecha a tela dentro do CRM.
  const again = await browser.newPage();
  await again.goto(session.url);
  await expect(again.getByRole('heading', { name: 'Sessão encerrada' })).toBeVisible();
  await again.close();

  expect((await request.delete(`/api/public/v1/embed/sessions/${session.id}`, { headers: { Authorization: `Bearer ${key}` } })).status()).toBe(204);
  await frame.getByRole('tab', { name: 'Arquivados' }).click();
  await expect(frame.getByRole('heading', { name: 'Sessão encerrada' })).toBeVisible();
  await expect.poll(() => messageTypes(crm)).toContain('session.expired');
  await crm.close();
});
