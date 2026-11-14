import { api, ApiError } from '../api.js';
import { isAdmin, requireUser } from '../auth.js';
import { applyErrors, clearErrors, hideAlert, setBusy, showAlert } from '../ui/form.js';
import { toast } from '../ui/toast.js';

const me = await requireUser();
const form = document.getElementById('settings-form');
const alertEl = form.querySelector('[data-form-alert]');

function fill(org) {
  form.elements.name.value = org.name;
  form.elements.reminder.value = org.settings.reminder ?? '';
  form.elements.language.value = org.settings.language;
  form.elements.defaultMessage.value = org.settings.defaultMessage ?? '';

  const [year, month] = org.usage.month.split('-').map(Number);
  const monthName = new Date(year, month - 1, 1).toLocaleDateString('pt-BR', { month: 'long' });
  document.querySelector('[data-usage]').textContent = String(org.usage.contractsSent);
  document.querySelector('[data-usage-label]').textContent =
    `${org.usage.contractsSent === 1 ? 'contrato enviado' : 'contratos enviados'} em ${monthName}`;
}

fill(await api('/organization/settings'));

if (!isAdmin(me)) {
  for (const el of form.elements) el.disabled = true;
  form.querySelector('[data-actions]').hidden = true;
  form.querySelector('[data-readonly-note]').hidden = false;
}

form.addEventListener('submit', async (e) => {
  e.preventDefault();
  clearErrors(form);
  hideAlert(alertEl);
  const button = form.querySelector('[type="submit"]');
  setBusy(button, true);
  try {
    const org = await api('/organization/settings', {
      method: 'PUT',
      body: {
        name: form.elements.name.value.trim(),
        reminder: form.elements.reminder.value || null,
        language: form.elements.language.value,
        defaultMessage: form.elements.defaultMessage.value.trim() || null,
      },
    });
    fill(org);
    toast('Configurações salvas.');
  } catch (err) {
    if (!(err instanceof ApiError && applyErrors(form, err.errors))) showAlert(alertEl, err.message);
  } finally {
    setBusy(button, false);
  }
});
