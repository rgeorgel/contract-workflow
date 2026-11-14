import { expect, test } from '@playwright/test';
import { signUp } from './helpers.js';

test('landing leva ao cadastro, que leva ao painel', async ({ page, request }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1 })).toContainText('Do modelo à assinatura');
  await page.getByRole('link', { name: 'Criar conta da empresa' }).first().click();
  await expect(page).toHaveURL(/signup\.html/);

  const account = await signUp(page, request);

  await expect(page.getByRole('heading', { name: /Olá, Ana/ })).toBeVisible();
  await expect(page.getByText(account.org).first()).toBeVisible();
  await expect(page.getByRole('link', { name: 'Montar o primeiro template' })).toBeVisible();
});

test('área logada exige sessão', async ({ page }) => {
  await page.goto('/contracts.html');
  await expect(page).toHaveURL(/login\.html\?next=/);
});

test('cadastro valida os campos antes de enviar', async ({ page }) => {
  await page.goto('/signup.html');
  await page.getByRole('button', { name: 'Criar conta' }).click();
  await expect(page.getByText('Informe o nome da empresa.')).toBeVisible();
  await expect(page.getByText('A senha precisa ter ao menos 6 caracteres.')).toBeVisible();
});
