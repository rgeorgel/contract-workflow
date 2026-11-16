import { expect, test } from '@playwright/test';
import { signUp } from './helpers.js';

// "null" ou "undefined" na tela é sempre bug (ex.: replaceChildren com um filho null vira o texto "null").
const PAGES = ['/dashboard.html', '/contracts.html', '/templates.html', '/users.html', '/settings.html', '/contract-new.html'];
const LEAK = /\b(null|undefined|NaN)\b|\[object Object\]/;

test('nenhuma página logada mostra null, undefined ou NaN', async ({ page, request }) => {
  await signUp(page, request, { plan: null });
  for (const path of PAGES) {
    await page.goto(path);
    await page.waitForLoadState('networkidle');
    const text = await page.locator('body').innerText();
    expect(text, `${path} mostra texto vazado`).not.toMatch(LEAK);
  }
});

test('a página inicial não mostra null, undefined ou NaN', async ({ page }) => {
  await page.goto('/');
  await page.getByText('Começar agora').first().waitFor();
  expect(await page.locator('body').innerText()).not.toMatch(LEAK);
});
