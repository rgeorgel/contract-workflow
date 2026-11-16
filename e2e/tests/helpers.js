import { expect } from '@playwright/test';

const MAILPIT = process.env.MAILPIT_URL ?? 'http://localhost:8026';
const PLATFORM_KEY = process.env.PLATFORM_API_KEY ?? 'dev-platform-key-troque-por-uma-de-32-caracteres';

export const unique = (prefix) => `${prefix}-${Date.now()}-${Math.floor(Math.random() * 1e4)}`;

/** Espera o e-mail chegar no Mailpit e devolve o primeiro link que contém `path`. */
export async function linkFromEmail(request, to, path) {
  let link = null;
  await expect.poll(async () => {
    const search = await request.get(`${MAILPIT}/api/v1/search`, { params: { query: `to:"${to}"` } });
    const { messages } = await search.json();
    for (const m of messages ?? []) {
      const full = await (await request.get(`${MAILPIT}/api/v1/message/${m.ID}`)).json();
      const match = full.Text.match(new RegExp(`https?://\\S*${path.replace(/[.?]/g, '\\$&')}\\S*`));
      if (match) { link = match[0]; return true; }
    }
    return false;
  }, { message: `e-mail para ${to} com ${path}`, timeout: 20_000 }).toBe(true);
  return link;
}

/** Troca o plano da organização logada na página pela API da plataforma. */
export async function setPlan(page, plan) {
  const org = await (await page.request.get('/api/v1/organization/settings')).json();
  const res = await page.request.put(`/api/platform/organizations/${org.slug}/plan`, {
    headers: { Authorization: `Bearer ${PLATFORM_KEY}` },
    data: { plan },
  });
  expect(res.status(), 'API da plataforma (PLATFORM_API_KEY)').toBe(200);
}

/**
 * Cadastro pela tela, confirmação pelo link do e-mail e login. Devolve os dados da conta.
 * A conta vai para o plano Empresa (sem esbarrar em cota), a não ser que o teste peça outro.
 */
export async function signUp(page, request, { plan = 'empresa' } = {}) {
  const account = {
    org: `Empresa ${unique('e2e')}`,
    name: 'Ana Teste',
    email: `${unique('ana')}@e2e.test`,
    password: 'senha-e2e-123',
  };

  await page.goto('/signup.html');
  await page.getByLabel('Nome da empresa').fill(account.org);
  await page.getByLabel('Seu nome').fill(account.name);
  await page.getByLabel('E-mail de trabalho').fill(account.email);
  await page.getByLabel('Senha', { exact: true }).fill(account.password);
  await page.getByRole('button', { name: 'Criar conta' }).click();
  await expect(page.getByRole('heading', { name: 'Verifique seu e-mail' })).toBeVisible();

  const confirm = await linkFromEmail(request, account.email, '/signup.html?step=confirm');
  await page.goto(confirm);
  await expect(page.getByRole('heading', { name: 'E-mail confirmado' })).toBeVisible();
  // O token sai da barra de endereço.
  expect(page.url()).not.toContain('token=');

  await page.getByRole('link', { name: 'Entrar' }).last().click();
  await page.getByLabel('E-mail').fill(account.email);
  await page.getByLabel('Senha', { exact: true }).fill(account.password);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page).toHaveURL(/dashboard\.html/);
  if (plan) await setPlan(page, plan);
  return account;
}
