import { expect, test } from '@playwright/test';
import { signUp } from './helpers.js';

// "null" ou "undefined" na tela é sempre bug (ex.: replaceChildren com um filho null vira o texto "null").
const PAGES = ['/dashboard.html', '/contracts.html', '/templates.html', '/users.html', '/settings.html', '/contract-new.html', '/contract-bulk.html', '/integrations.html'];
const LEAK = /\b(null|undefined|NaN)\b|\[object Object\]/;

test('nenhuma página logada mostra null, undefined ou NaN, nem tem erro de script', async ({ page, request }) => {
  const scriptErrors = [];
  page.on('pageerror', (e) => scriptErrors.push(`${page.url()}: ${e.message}`));
  await signUp(page, request);
  for (const path of PAGES) {
    await page.goto(path);
    await page.waitForLoadState('networkidle');
    const text = await page.locator('body').innerText();
    expect(text, `${path} mostra texto vazado`).not.toMatch(LEAK);
  }
  expect(scriptErrors).toEqual([]);
});

test('a página inicial não mostra null, undefined ou NaN', async ({ page }) => {
  await page.goto('/');
  await page.getByText('Começar agora').first().waitFor();
  expect(await page.locator('body').innerText()).not.toMatch(LEAK);
});
