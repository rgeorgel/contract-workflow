import { expect, test } from '@playwright/test';

for (const path of ['/', '/signup.html', '/login.html']) {
  test(`${path} cabe na tela do celular, sem rolagem lateral`, async ({ page }) => {
    await page.goto(path);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
    expect(overflow).toBeLessThanOrEqual(0);
  });
}
