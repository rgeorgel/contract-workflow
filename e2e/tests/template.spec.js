import { expect, test } from '@playwright/test';
import { signUp } from './helpers.js';

test('template a partir de modelo pronto, com pré-visualização ao vivo antes de salvar', async ({ page, request }) => {
  await signUp(page, request);

  // 1. Modal: os modelos prontos aparecem ao lado do "Em branco", e escolher um sugere o nome.
  await page.goto('/templates.html');
  await page.getByRole('button', { name: 'Novo template' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('radio', { name: /Em branco/ })).toBeChecked();
  await expect(dialog.getByRole('radio')).toHaveCount(4);
  await dialog.getByRole('radio', { name: /Prestação de serviços/ }).check();
  await expect(dialog.getByLabel('Nome', { exact: true })).toHaveValue('Prestação de serviços');
  await dialog.getByRole('button', { name: 'Criar e editar' }).click();

  // 2. Editor já vem com texto, variáveis e signatários do modelo.
  await expect(page).toHaveURL(/template-editor\.html/);
  await expect(page.getByLabel('Texto do contrato em HTML')).toHaveValue(/Contrato de Prestação de Serviços/);
  await expect(page.getByRole('textbox', { name: 'Chave' })).toHaveCount(14);
  const preview = page.frameLocator('[data-frame]');
  await expect(preview.getByRole('heading', { name: 'Contrato de Prestação de Serviços' })).toBeVisible();

  // 3. Editar o texto atualiza a pré-visualização sem salvar.
  const body = page.getByLabel('Texto do contrato em HTML');
  await body.fill(`${await body.inputValue()}\n<p>Cláusula escrita agora: {{contratante.nome}}.</p>`);
  await expect(page.getByText('Alterações não salvas')).toBeVisible();
  await expect(preview.getByText(/Cláusula escrita agora/)).toBeVisible();

  // Renomear o rótulo de uma variável também aparece (o texto de exemplo usa o rótulo).
  await page.getByRole('textbox', { name: 'Rótulo' }).first().fill('Razão social do cliente');
  await expect(preview.getByText(/Razão social do cliente/).first()).toBeVisible();

  // 4. O modelo publica sem ajustes.
  await page.getByRole('button', { name: /^Publicar v1/ }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Publicar' }).click();
  await expect(page.getByText('Publicado · v1')).toBeVisible();
});
