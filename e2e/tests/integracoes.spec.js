import { expect, test } from '@playwright/test';
import { signUp } from './helpers.js';

test('integrações: chave aparece uma vez, abre a API pública e para ao revogar', async ({ page, request }) => {
  await signUp(page, request);
  await page.goto('/dashboard.html');
  await page.getByRole('navigation', { name: 'Principal' }).getByRole('link', { name: 'Integrações' }).click();
  await expect(page.getByRole('heading', { name: 'Integrações', level: 1 })).toBeVisible();
  await expect(page.getByText('Nenhuma chave ainda')).toBeVisible();

  await page.getByRole('button', { name: 'Nova chave' }).click();
  await page.getByLabel('Nome').fill('CRM');
  await page.getByRole('button', { name: 'Criar chave' }).click();
  const token = await page.getByLabel('Chave “CRM”').inputValue();
  expect(token).toMatch(/^ctr_[A-Za-z0-9_-]{40,}$/);
  await expect(page.getByText('Copie a chave agora')).toBeVisible();
  await page.getByRole('button', { name: 'Já copiei' }).click();

  const row = page.getByRole('row').filter({ hasText: 'CRM' });
  await expect(row).toContainText(`${token.slice(0, 12)}…`);
  await expect(page.locator('body')).not.toContainText(token);

  const account = await request.get('/api/public/v1/account', { headers: { Authorization: `Bearer ${token}` } });
  expect(account.status()).toBe(200);
  expect((await account.json()).organization.planId).toBe('empresa');
  await page.reload();
  await expect(page.getByRole('row').filter({ hasText: 'CRM' })).not.toContainText('Nunca');

  await page.getByRole('row').filter({ hasText: 'CRM' }).getByRole('button', { name: 'Revogar' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Revogar' }).click();
  await expect(page.getByRole('row').filter({ hasText: 'CRM' })).toContainText('Revogada');
  const denied = await request.get('/api/public/v1/account', { headers: { Authorization: `Bearer ${token}` } });
  expect(denied.status()).toBe(401);
});

test('integrações: webhook com segredo, evento de teste e histórico de entregas', async ({ page, request }) => {
  await signUp(page, request);
  await page.goto('/integrations.html');

  await page.getByRole('button', { name: 'Novo webhook' }).click();
  // Ninguém escuta nesta porta: a entrega falha e entra em nova tentativa (rede interna é liberada só em dev).
  await page.getByLabel('URL').fill('http://127.0.0.1:9/webhooks/contratos');
  await page.getByLabel('Descrição').fill('CRM de teste');
  await page.getByRole('dialog').getByRole('button', { name: 'Cadastrar' }).click();
  const secret = await page.getByLabel('Segredo de assinatura').inputValue();
  expect(secret).toMatch(/^whsec_/);
  await page.getByRole('button', { name: 'Concluir' }).click();

  const hook = page.getByRole('region', { name: 'Webhooks' }).getByRole('row').filter({ hasText: '127.0.0.1:9' });
  await expect(hook).toContainText('Todos os eventos');
  await expect(hook).toContainText('Ativo');

  // O segredo pode ser visto de novo.
  await hook.getByRole('button', { name: 'Segredo' }).click();
  await expect(page.getByRole('dialog').getByLabel('Segredo', { exact: true })).toHaveValue(secret);
  await page.getByRole('dialog').getByRole('button', { name: 'Fechar' }).click();

  await hook.getByRole('button', { name: 'Enviar teste' }).click();
  const delivery = page.locator('#entregas').getByRole('row').filter({ hasText: 'webhook.test' });
  await expect(delivery).toContainText(/Tentando de novo \(1\)/, { timeout: 15_000 });
  await delivery.getByRole('button', { name: 'Detalhes' }).click();
  await expect(page.getByRole('dialog')).toContainText('Próxima tentativa');
  await page.getByRole('dialog').getByRole('button', { name: 'Fechar' }).click();

  // Só eventos escolhidos.
  await hook.getByRole('button', { name: 'Editar' }).click();
  await page.getByRole('dialog').getByText('Todos os eventos', { exact: true }).click();
  for (const box of await page.getByRole('dialog').locator('input[name="events"]').all()) await box.uncheck();
  await page.getByRole('dialog').getByText('contract.completed', { exact: true }).click();
  await page.getByRole('button', { name: 'Salvar' }).click();
  await expect(hook).toContainText('contract.completed');
});

test('integrações: liberadas também no plano Avulso', async ({ page, request }) => {
  await signUp(page, request, { plan: 'avulso' });
  await page.goto('/integrations.html');
  await expect(page.getByRole('button', { name: 'Nova chave' })).toBeEnabled();
  await expect(page.getByRole('button', { name: 'Novo webhook' })).toBeEnabled();
  await expect(page.getByRole('heading', { name: /fazem parte do plano/ })).toBeHidden();
});

test('portal de desenvolvedores: endereço real nos exemplos, abas e OpenAPI', async ({ page, baseURL }) => {
  const scriptErrors = [];
  page.on('pageerror', (e) => scriptErrors.push(e.message));
  await page.goto('/desenvolvedores/');
  await expect(page.getByRole('heading', { name: 'API e webhooks do Contratos' })).toBeVisible();
  await expect(page.locator('body')).not.toContainText('{{BASE_URL}}');
  await expect(page.getByText(`${baseURL}/api/public/v1`, { exact: true })).toBeVisible();

  // Escolher Python vale para todos os exemplos da página.
  const quick = page.getByRole('tablist', { name: 'Consultar a conta' });
  await quick.getByRole('tab', { name: 'Python' }).click();
  await expect(page.getByRole('tablist', { name: 'Criar e enviar um contrato' }).getByRole('tab', { name: 'Python' }))
    .toHaveAttribute('aria-selected', 'true');
  await expect(page.getByRole('tabpanel').filter({ hasText: 'api/public/v1/contracts' }).filter({ hasText: 'requests.post' })).toBeVisible();
  // Inclusive nos exemplos do embed.
  await expect(page.getByRole('tablist', { name: 'Criar uma sessão de embed' }).getByRole('tab', { name: 'Python' }))
    .toHaveAttribute('aria-selected', 'true');

  const spec = await page.request.get('/desenvolvedores/openapi.json');
  expect(spec.ok()).toBeTruthy();
  expect((await spec.json()).paths['/contracts'].post.operationId).toBe('createContract');
  expect((await spec.json()).paths['/embed/sessions'].post.operationId).toBe('createEmbedSession');
  expect(scriptErrors).toEqual([]);
});
