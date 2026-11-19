import { test, expect } from '@playwright/test';
import { signUp } from './helpers.js';

test.use({ colorScheme: 'light' });

test('botão da navegação alterna o tema escuro e a escolha fica salva', async ({ page, request }) => {
  await signUp(page, request);
  const html = page.locator('html');
  await expect(html).toHaveAttribute('data-theme', 'light');

  await page.getByRole('button', { name: 'Ativar tema escuro' }).click();
  await expect(html).toHaveAttribute('data-theme', 'dark');
  await expect(page.locator('body')).not.toHaveCSS('background-color', 'rgb(246, 247, 251)');
  if (process.env.THEME_SHOTS) await page.screenshot({ path: `${process.env.THEME_SHOTS}/dashboard-escuro.png`, fullPage: true });

  // Vale nas outras páginas e depois de recarregar.
  await page.goto('/contracts.html');
  await expect(html).toHaveAttribute('data-theme', 'dark');
  if (process.env.THEME_SHOTS) await page.screenshot({ path: `${process.env.THEME_SHOTS}/contratos-escuro.png`, fullPage: true });

  await page.getByRole('button', { name: 'Ativar tema claro' }).click();
  await expect(html).toHaveAttribute('data-theme', 'light');
  await page.reload();
  await expect(html).toHaveAttribute('data-theme', 'light');
});

test('sem escolha salva, segue o tema do sistema', async ({ browser, request }) => {
  const context = await browser.newContext({ colorScheme: 'dark' });
  const page = await context.newPage();
  await signUp(page, request);
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await expect(page.getByRole('button', { name: 'Ativar tema claro' })).toBeVisible();
  await context.close();
});
