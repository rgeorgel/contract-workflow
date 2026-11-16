import { expect, test } from '@playwright/test';
import { fakeProviderEnabled, setPlan, signUp, unique } from './helpers.js';

/** POST/PUT pela sessão da página, com o token antiforgery, como o api.js faz. */
async function apiWrite(page, method, path, data) {
  const { token } = await (await page.request.get('/api/v1/auth/csrf')).json();
  const res = await page.request.fetch(`/api/v1${path}`, { method, data, headers: { 'X-CSRF-TOKEN': token } });
  expect(res.ok(), `${method} ${path}`).toBeTruthy();
  return res.json();
}

async function publishTemplate(page, name) {
  const t = await apiWrite(page, 'POST', '/templates', { name });
  await apiWrite(page, 'PUT', `/templates/${t.id}`, {
    name,
    bodyHtml: '<p>Prestação de serviços para {{cliente.nome}}, no valor de {{valor}}, a partir de {{inicio}}.</p>',
    variables: [
      { key: 'cliente.nome', label: 'Nome do cliente', type: 'Text', required: true },
      { key: 'valor', label: 'Valor', type: 'Money', required: true },
      { key: 'inicio', label: 'Início', type: 'Date', required: false },
    ],
    defaultSignerRoles: [{ role: 'Cliente', action: 'Sign', order: 1 }],
  });
  await apiWrite(page, 'POST', `/templates/${t.id}/publish`);
  return t.id;
}

/** Planilha como o Excel em português salva: ";" e data dd/mm/aaaa. */
const csv = (lines) => Buffer.from(['﻿' + lines.join('\r\n')].join(''), 'utf-8');
const HEADER = 'titulo;cliente.nome;valor;inicio;prazo;mensagem;em_ordem;signatario1_nome;signatario1_email;signatario1_celular;signatario1_canal;signatario1_papel;signatario1_cpf';

test('envio em lote: planilha com erro aponta a linha; corrigida, cria os contratos', async ({ page, request }) => {
  await signUp(page, request); // plano Empresa: tem envio em lote
  const marker = unique('Lote');
  const templateId = await publishTemplate(page, `Serviços ${marker}`);

  await page.goto('/contracts.html');
  await page.getByRole('link', { name: 'Envio em lote' }).click();
  await page.getByLabel('Template', { exact: true }).selectOption(templateId);

  // A planilha modelo traz as colunas do template e um bloco por papel sugerido.
  const [file] = await Promise.all([page.waitForEvent('download'), page.getByRole('button', { name: 'Baixar planilha modelo' }).click()]);
  const model = (await (await file.createReadStream()).toArray()).join('');
  expect(model.replace(/^﻿/, '').trim()).toBe(HEADER);

  // Planilha com erro: e-mail inválido e data ruim.
  await page.getByLabel('Arquivo CSV').setInputFiles({ name: 'lote.csv', mimeType: 'text/csv', buffer: csv([
    HEADER,
    `${marker} A;Café Aurora;1.500,00;15/03/2027;;;;Ana Lima;ana@cliente.com.br;;;;`,
    `${marker} B;Padaria Sol;800,00;31/02/2027;;;;Bruno Reis;bruno@;;;;`,
  ]) });
  await expect(page.getByText('1 de 2 linhas com erro')).toBeVisible();
  const bad = page.getByRole('row').filter({ hasText: `${marker} B` });
  await expect(bad).toContainText('inicio');
  await expect(bad).toContainText('signatario1_email');
  await expect(page.getByRole('button', { name: /Criar 2 rascunhos/ })).toBeDisabled();

  // Corrigida: cria os rascunhos.
  await page.getByLabel('Arquivo CSV').setInputFiles({ name: 'lote.csv', mimeType: 'text/csv', buffer: csv([
    HEADER,
    `${marker} A;Café Aurora;1.500,00;15/03/2027;;;;Ana Lima;ana@cliente.com.br;;;;`,
    `${marker} B;Padaria Sol;800,00;28/02/2027;;;;Bruno Reis;;11 98765-4321;whatsapp;;`,
  ]) });
  await expect(page.getByText('2 contratos prontos')).toBeVisible();
  await page.getByRole('button', { name: 'Criar 2 rascunhos' }).click();
  await expect(page.getByText('2 rascunhos criados')).toBeVisible();
  await expect(page.getByRole('link', { name: new RegExp(`${marker} A`) })).toBeVisible();

  const list = await (await page.request.get(`/api/v1/contracts?q=${encodeURIComponent(marker)}`)).json();
  expect(list.total).toBe(2);
  const b = list.items.find((c) => c.title.endsWith(' B'));
  const detail = await (await page.request.get(`/api/v1/contracts/${b.id}`)).json();
  expect(detail.signers[0]).toMatchObject({ name: 'Bruno Reis', deliveryMethod: 'WhatsApp', phone: '+5511987654321', role: 'Cliente' });
  expect(detail.variableValues).toMatchObject({ 'cliente.nome': 'Padaria Sol', inicio: '2027-02-28' });
});

test('envio em lote: criar e enviar manda um por um', async ({ page, request }) => {
  test.skip(!(await fakeProviderEnabled(request)), 'envia de verdade: só com o provedor fake ("npm run test:isolado")');
  await signUp(page, request);
  const marker = unique('LoteEnvio');
  const templateId = await publishTemplate(page, `Serviços ${marker}`);

  await page.goto(`/contract-bulk.html?template=${templateId}`);
  await expect(page.getByText('Como preencher a planilha')).toBeVisible(); // template pré-selecionado carregado
  await page.getByLabel('Arquivo CSV').setInputFiles({ name: 'lote.csv', mimeType: 'text/csv', buffer: csv([
    HEADER,
    ...[1, 2, 3].map((i) => `${marker} ${i};Cliente ${i};100,00;;;;;Pessoa ${i};p${i}@cliente.com.br;;;;`),
  ]) });
  await page.getByRole('button', { name: 'Criar e enviar 3' }).click();
  await expect(page.getByText('3 de 3 contratos enviados.')).toBeVisible();
  await expect(page.getByText('Enviado', { exact: true })).toHaveCount(3);
});

test('envio em lote: fora dos planos Profissional e Empresa mostra o convite para mudar', async ({ page, request }) => {
  await signUp(page, request, { plan: 'essencial' });
  await page.goto('/contract-bulk.html');
  await expect(page.getByRole('heading', { name: /O envio em lote faz parte dos planos Profissional e Empresa/ })).toBeVisible();
  await expect(page.getByLabel('Arquivo CSV')).toBeHidden();
  await setPlan(page, 'profissional');
  await page.reload();
  await expect(page.getByLabel('Template', { exact: true })).toBeVisible();
});
