import { api, ApiError, resetCsrf } from '../api.js';
import { applyErrors, clearErrors, hideAlert, serialize, setBusy, showAlert, wirePasswordToggles } from '../ui/form.js';

const MIN_PASSWORD = 6;
const params = new URLSearchParams(location.search);
const userId = params.get('userId');
const token = params.get('token');
history.replaceState(null, '', location.pathname); // tira o token da barra de endereço

function show(state) {
  for (const el of document.querySelectorAll('[data-state]')) el.hidden = el.dataset.state !== state;
  document.querySelector(`[data-state="${state}"] h1`)?.focus();
}

wirePasswordToggles();
const form = document.getElementById('invite-form');
const alertEl = form.querySelector('[data-form-alert]');

try {
  if (!userId || !token) throw new Error('sem token');
  const invite = await api('/auth/invite/lookup', { method: 'POST', body: { userId, token } });
  document.querySelector('[data-org]').textContent = invite.organizationName;
  document.getElementById('email').value = invite.email;
  form.elements.name.value = invite.name ?? '';
  show('form');
  (invite.name ? form.elements.password : form.elements.name).focus();
} catch {
  show('invalid');
}

form.addEventListener('submit', async (e) => {
  e.preventDefault();
  clearErrors(form);
  hideAlert(alertEl);
  const data = serialize(form);
  const errors = {};
  if (data.name.trim().length < 2) errors.name = ['Informe seu nome.'];
  if (data.password.length < MIN_PASSWORD) errors.password = [`A senha precisa ter ao menos ${MIN_PASSWORD} caracteres.`];
  if (applyErrors(form, errors)) return;

  const button = form.querySelector('[type="submit"]');
  setBusy(button, true);
  try {
    await api('/auth/accept-invite', { method: 'POST', body: { userId, token, name: data.name.trim(), password: data.password } });
    resetCsrf();
    location.assign('/dashboard.html');
  } catch (err) {
    setBusy(button, false);
    if (err instanceof ApiError && err.code === 'invalid_invite') { show('invalid'); return; }
    if (!(err instanceof ApiError && applyErrors(form, err.errors))) showAlert(alertEl, err.message);
  }
});
