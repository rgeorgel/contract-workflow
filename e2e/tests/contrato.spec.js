import { expect, test } from '@playwright/test';
import { FAKE_PROVIDER_HINT, fakeProviderEnabled, signUp, unique } from './helpers.js';

const BODY = `<h1>Contrato de Locação</h1>
<p>LOCADOR: {{locador.nome}}. LOCATÁRIO: {{locatario.nome}}.</p>
<p>Imóvel na {{endereco}}.</p>`;

test('do template ao contrato assinado, e encontrado pela busca', async ({ page, request }) => {
  // O signatário é simulado pelo provedor fake; com a Autentique de verdade, quem assina é uma pessoa.
  test.skip(!(await fakeProviderEnabled(request)), FAKE_PROVIDER_HINT);
  await signUp(page, request);
  const marker = unique('Locacao');

  // 1. Template: escreve, cadastra as variáveis do texto, publica.
  await page.goto('/templates.html');
  await page.getByRole('button', { name: 'Novo template' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Continuar' }).click();
  await page.getByLabel('Nome', { exact: true }).fill('Locação residencial');
  await page.getByRole('button', { name: 'Criar e editar' }).click();
  await expect(page).toHaveURL(/template-editor\.html/);
  await page.getByRole('tab', { name: 'HTML' }).click();
  await page.getByLabel('Texto do contrato em HTML').fill(BODY);
  await page.getByRole('button', { name: 'Cadastrar variáveis do texto' }).click();
  await expect(page.getByRole('textbox', { name: 'Chave' })).toHaveCount(3);
  await page.getByRole('button', { name: /^Publicar v1/ }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Publicar' }).click();
  await expect(page.getByText('Publicado · v1')).toBeVisible();

  // 2. Assistente: template → dados → signatários → revisar e enviar.
  await page.goto('/contract-new.html');
  await page.getByRole('radio', { name: /Locação residencial/ }).click();
  await page.getByLabel('Título do contrato').fill(`${marker} Apartamento 42`);
  await page.getByRole('button', { name: 'Continuar' }).click();

  await page.getByLabel('Locador nome').fill('Maria Proprietária');
  await page.getByLabel('Locatario nome').fill('Pedro Inquilino');
  await page.getByLabel('Endereco').fill('Rua das Flores, 42');
  await page.getByRole('button', { name: 'Salvar e continuar' }).click();

  await page.getByRole('button', { name: 'Adicionar signatário' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Nome completo').fill('Pedro Inquilino');
  await dialog.getByRole('textbox', { name: 'E-mail' }).fill('pedro@e2e.test');
  await dialog.getByRole('button', { name: 'Adicionar' }).click();
  await expect(page.getByText('E-mail: pedro@e2e.test')).toBeVisible();

  await page.getByRole('button', { name: 'Revisar' }).click();
  await expect(page.getByText('Rua das Flores, 42')).toBeVisible();
  await page.getByRole('button', { name: 'Enviar para assinatura' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Enviar' }).click();
  await expect(page).toHaveURL(/contract\.html\?id=/);
  const status = page.locator('[data-status] .badge');
  await expect(status).toHaveText('Enviado');

  // 3. O worker entrega ao provedor (fake); o simulador aparece e faz o papel do signatário.
  await expect.poll(async () => {
    await page.reload();
    return page.getByText('Simular (provedor fake):').count();
  }, { timeout: 30_000 }).toBeGreaterThan(0);
  await page.getByRole('button', { name: 'Assinar', exact: true }).click();
  await expect(status).toHaveText('Concluído');
  await expect(page.getByText('1 de 1 assinou')).toBeVisible();

  // PDF assinado arquivado pelo worker.
  await expect.poll(async () => {
    await page.reload();
    // A página monta o conteúdo depois do load: espera o status antes de contar os links.
    await expect(status).toHaveText('Concluído');
    return page.getByRole('link', { name: 'PDF assinado' }).count();
  }, { timeout: 30_000 }).toBe(1);
  const pdf = await page.request.get(await page.getByRole('link', { name: 'PDF assinado' }).getAttribute('href'));
  expect(pdf.headers()['content-type']).toBe('application/pdf');
  expect((await pdf.body()).subarray(0, 5).toString()).toBe('%PDF-');

  // 4. Busca sem acento por um valor de variável e pelo marcador do título.
  await page.goto(`/contracts.html?q=${encodeURIComponent('proprietaria')}`);
  await expect(page.getByRole('link', { name: `${marker} Apartamento 42` })).toBeVisible();
  await page.goto(`/contracts.html?q=${encodeURIComponent(marker.toLowerCase())}`);
  await expect(page.getByRole('link', { name: `${marker} Apartamento 42` })).toBeVisible();

  // 5. Painel conta o concluído.
  await page.goto('/dashboard.html');
  await expect(page.getByRole('link', { name: /Concluídos no mês/ })).toContainText('1');
});
