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

  // 3. Editar o texto (aqui no modo HTML) atualiza a pré-visualização sem salvar.
  await page.getByRole('tab', { name: 'HTML' }).click();
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

test('editor visual: formatação, variável digitada vira etiqueta e o HTML sai limpo', async ({ page, request }) => {
  await signUp(page, request);
  await page.goto('/templates.html');
  await page.getByRole('button', { name: 'Novo template' }).click();
  await page.getByRole('dialog').getByLabel('Nome', { exact: true }).fill('Do zero');
  await page.getByRole('dialog').getByRole('button', { name: 'Criar e editar' }).click();
  await expect(page).toHaveURL(/template-editor\.html/);

  // O modo visual é o padrão.
  await expect(page.getByRole('tab', { name: 'Visual' })).toHaveAttribute('aria-selected', 'true');
  const editor = page.getByRole('textbox', { name: 'Texto do contrato', exact: true });
  await editor.click();
  await page.keyboard.type('Contrato simples');
  await page.getByLabel('Estilo do parágrafo').selectOption('h1');
  await page.keyboard.press('End');
  await page.keyboard.press('Enter');
  await page.keyboard.type('Cliente: {{cliente.nome}}, com prazo ');
  await page.keyboard.press('ControlOrMeta+b');
  await page.keyboard.type('importante');
  await page.keyboard.press('ControlOrMeta+b');
  await page.keyboard.type('.');

  // {{chave}} digitada vira etiqueta, destacada até a variável ser cadastrada.
  const chip = editor.locator('.var-chip');
  await expect(chip).toHaveText('cliente.nome');
  await expect(chip).toHaveClass(/var-chip--missing/);
  await page.getByRole('button', { name: 'Cadastrar variáveis do texto' }).click();
  await expect(chip).not.toHaveClass(/var-chip--missing/);
  await expect(chip).toHaveText('Cliente nome');

  // Inserir variável pelo menu, no fim do texto.
  await page.keyboard.press('End');
  await page.getByLabel('Inserir variável').selectOption('cliente.nome');
  await expect(editor.locator('.var-chip')).toHaveCount(2);

  const preview = page.frameLocator('[data-frame]');
  await expect(preview.getByRole('heading', { name: 'Contrato simples' })).toBeVisible();
  await expect(preview.getByText(/Cliente:/)).toBeVisible();

  // O HTML gerado é o de sempre: tags simples e {{chaves}}.
  await page.getByRole('tab', { name: 'HTML' }).click();
  const html = page.getByLabel('Texto do contrato em HTML');
  await expect(html).toHaveValue(/<h1>Contrato simples<\/h1>/);
  await expect(html).toHaveValue(/<p>Cliente: \{\{cliente\.nome\}\}, com prazo <strong>importante<\/strong>\.\{\{cliente\.nome\}\}<\/p>/);

  await page.getByRole('button', { name: /^Publicar v1/ }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Publicar' }).click();
  await expect(page.getByText('Publicado · v1')).toBeVisible();
});
