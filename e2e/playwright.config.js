import { defineConfig, devices } from '@playwright/test';

/**
 * Testes de ponta a ponta contra o sistema rodando (docker compose up).
 * BASE_URL: a app; MAILPIT_URL: a API do Mailpit, de onde vêm os links de confirmação.
 */
export default defineConfig({
  testDir: './tests',
  timeout: 90_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: process.env.BASE_URL ?? 'http://localhost:7700',
    locale: 'pt-BR',
    timezoneId: 'America/Sao_Paulo',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'] }, testIgnore: /responsivo/ },
    { name: 'celular', use: { ...devices['Pixel 7'] }, testMatch: /responsivo/ },
  ],
});
