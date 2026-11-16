import { expect, test } from '@playwright/test';
import { setPlan, signUp } from './helpers.js';

/** POST/PUT pela sessão da página, com o token antiforgery, como o api.js faz. */
async function apiWrite(page, method, path, data) {
  const { token } = await (await page.request.get('/api/v1/auth/csrf')).json();
  const res = await page.request.fetch(`/api/v1${path}`, { method, data, headers: { 'X-CSRF-TOKEN': token } });
  expect(res.ok(), `${method} ${path}`).toBeTruthy();
  return res.json();
}

test('plano grátis: uso em Configurações e canal fora do plano bloqueado', async ({ page, request }) => {
  await signUp(page, request, { plan: 'gratis' });

  await page.goto('/settings.html');
  const usage = page.getByRole('region', { name: 'Plano e uso' });
  await expect(usage.getByText('Plano Grátis', { exact: true }).first()).toBeVisible();
  await expect(usage.getByText('0 de 3')).toBeVisible();
  await expect(usage.getByText('Nenhum crédito avulso.')).toBeVisible();

  const plans = page.getByRole('region', { name: 'Planos' });
  await expect(plans.getByRole('article')).toHaveCount(4);
  await expect(plans.getByRole('article').filter({ hasText: 'Seu plano' })).toContainText('Grátis');
  await expect(plans.getByRole('row', { name: /10 contratos/ })).toContainText('R$ 29,90');

  // Contrato com um template mínimo, direto pela API, para abrir o assistente na etapa de signatários.
  const template = await apiWrite(page, 'POST', '/templates', { name: 'Simples' });
  await apiWrite(page, 'PUT', `/templates/${template.id}`, { name: 'Simples', bodyHtml: '<p>Contrato simples.</p>', variables: [], defaultSignerRoles: [] });
  await apiWrite(page, 'POST', `/templates/${template.id}/publish`);
  const contract = await apiWrite(page, 'POST', '/contracts', { templateId: template.id, title: 'Contrato do plano grátis' });

  await page.goto(`/contract-new.html?id=${contract.id}&step=3`);
  await page.getByRole('button', { name: 'Adicionar signatário' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('radio', { name: /E-mail/ })).toBeEnabled();
  await expect(dialog.getByRole('radio', { name: /WhatsApp/ })).toBeDisabled();
  await expect(dialog.getByText('No plano Essencial')).toBeVisible();
  await expect(dialog.getByText('No plano Profissional')).toBeVisible();

  // Mudou de plano: o WhatsApp libera.
  await dialog.getByRole('button', { name: 'Cancelar' }).click();
  await setPlan(page, 'essencial');
  await page.reload();
  await page.getByRole('button', { name: 'Adicionar signatário' }).click();
  await expect(page.getByRole('dialog').getByRole('radio', { name: /WhatsApp/ })).toBeEnabled();
});

test('página inicial mostra os planos do catálogo, com preço mensal e anual', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('navigation', { name: 'Seções' }).getByRole('link', { name: 'Preços' }).click();
  const pricing = page.getByRole('region', { name: /Comece grátis/ });
  const pro = pricing.getByRole('article', { name: 'Profissional' });
  await expect(pricing.getByRole('article')).toHaveCount(4);
  await expect(pro).toContainText('R$ 89,90/mês');
  await expect(pro).toContainText('80 contratos por mês');
  await expect(pro).toContainText('Envio por e-mail, WhatsApp e SMS');
  await expect(pro).toContainText('Recomendado');

  await pricing.getByRole('button', { name: /Anual/ }).click();
  await expect(pro).toContainText('R$ 74,92/mês');
  await expect(pro).toContainText('cobrado R$ 899,00 por ano');
  await expect(pricing.getByRole('article', { name: 'Grátis' })).toContainText('para sempre');

  await expect(pricing.getByRole('listitem').filter({ hasText: /^50 contratos/ })).toContainText('R$ 2,40 cada');
  await pro.getByRole('link', { name: 'Começar agora' }).click();
  await expect(page).toHaveURL(/signup\.html/);
});

test('assinar: botão abre o checkout e a volta ativa o plano (Stripe simulado no navegador)', async ({ page, request }) => {
  await signUp(page, request, { plan: 'gratis' });
  // Sem chaves do Stripe no ambiente local: a API de cobrança é simulada aqui, a tela é a real.
  let checkoutBody = null;
  await page.route('**/api/v1/organization/billing', async (route) => {
    const res = await route.fetch();
    await route.fulfill({ response: res, json: { ...(await res.json()), onlineCheckout: true } });
  });
  await page.route('**/api/v1/billing/checkout', async (route) => {
    checkoutBody = route.request().postDataJSON();
    await route.fulfill({ json: { url: '/settings.html?checkout=success&session_id=cs_test_e2e' } });
  });
  await page.route('**/api/v1/billing/checkout/confirm', async (route) => {
    const billing = await (await page.request.get('/api/v1/organization/billing')).json();
    const essencial = billing.plans.find((p) => p.id === 'essencial');
    await route.fulfill({ json: { ...billing, onlineCheckout: true, plan: essencial, contractsLimit: 20,
      subscription: { status: 'active', interval: 'year', cancelAtPeriodEnd: false, currentPeriodEnd: '2027-11-15T03:00:00Z' } } });
  });

  await page.goto('/settings.html');
  const plans = page.getByRole('region', { name: 'Planos' });
  await plans.getByRole('button', { name: /Anual/ }).click();
  await plans.getByRole('article').filter({ hasText: 'Essencial' }).getByRole('button', { name: 'Assinar anual' }).click();

  await expect(page.getByText('Assinatura ativada: plano Essencial.')).toBeVisible();
  expect(checkoutBody).toEqual({ plan: 'essencial', interval: 'year' });
  await expect(page).toHaveURL(/\/settings\.html$/); // sem session_id na barra
  await expect(page.getByRole('button', { name: 'Gerenciar assinatura' })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Plano e uso' })).toContainText('assinatura anual, renova em 15/11');
  await expect(page.getByRole('region', { name: 'Plano e uso' })).not.toContainText('null');
});
