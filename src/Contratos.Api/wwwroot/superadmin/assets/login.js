import { api, ApiError, resetCsrf, safeNext } from './api.js';
import { clearErrors, hideAlert, setBusy, setFieldError, showAlert } from './lib/form.js';

const form = document.getElementById('login-form');
const formAlert = form.querySelector('[data-form-alert]');
const params = new URLSearchParams(location.search);

// Já logado no portal: segue direto.
api('/auth/me', { redirectOn401: false }).then(() => location.replace(safeNext(params.get('next')))).catch(() => {});
form.elements.email.focus();

form.addEventListener('submit', async (e) => {
  e.preventDefault();
  clearErrors(form);
  hideAlert(formAlert);
  const email = form.elements.email.value.trim();
  const password = form.elements.password.value;
  if (!email) { setFieldError(form, 'email', 'Informe o e-mail.'); form.elements.email.focus(); return; }
  if (!password) { setFieldError(form, 'password', 'Informe a senha.'); form.elements.password.focus(); return; }

  const button = form.querySelector('[type="submit"]');
  setBusy(button, true);
  try {
    await api('/auth/login', { method: 'POST', body: { email, password }, redirectOn401: false });
    resetCsrf();
    location.assign(safeNext(params.get('next')));
  } catch (err) {
    setBusy(button, false);
    showAlert(formAlert, err instanceof ApiError ? err.message : 'Não foi possível entrar. Tente de novo.');
    form.elements.password.select();
  }
});
