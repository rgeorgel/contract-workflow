import { api, ApiError, resetCsrf, safeNext } from '../api.js';
import { clearErrors, hideAlert, isEmail, serialize, setBusy, setFieldError, showAlert, wirePasswordToggles } from '../ui/form.js';

wirePasswordToggles();

const form = document.getElementById('login-form');
const formAlert = form.querySelector('[data-form-alert]');
const unconfirmed = form.querySelector('[data-unconfirmed]');
const resendBtn = form.querySelector('[data-resend]');
const params = new URLSearchParams(location.search);

if (params.get('confirmed') === '1') form.querySelector('[data-confirmed-alert]').hidden = false;
form.elements.email.focus();

form.addEventListener('submit', async (e) => {
  e.preventDefault();
  clearErrors(form);
  hideAlert(formAlert);
  unconfirmed.hidden = true;

  const data = serialize(form);
  let invalid = false;
  if (!isEmail(data.email)) { setFieldError(form, 'email', 'Informe um e-mail válido.'); invalid = true; }
  if (!data.password) { setFieldError(form, 'password', 'Informe a senha.'); invalid = true; }
  if (invalid) { form.querySelector('[aria-invalid]')?.focus(); return; }

  const button = form.querySelector('[type="submit"]');
  setBusy(button, true);
  try {
    await api('/auth/login', { method: 'POST', body: { email: data.email.trim(), password: data.password }, redirectOn401: false });
    resetCsrf();
    location.assign(safeNext(params.get('next')));
  } catch (err) {
    setBusy(button, false);
    if (err instanceof ApiError && err.code === 'email_not_confirmed') {
      unconfirmed.hidden = false;
      return;
    }
    showAlert(formAlert, err instanceof ApiError ? err.message : 'Não foi possível entrar. Tente de novo.');
    form.elements.password.select();
  }
});

resendBtn.addEventListener('click', async () => {
  setBusy(resendBtn, true);
  try {
    await api('/auth/resend-confirmation', { method: 'POST', body: { email: form.elements.email.value.trim() } });
    resendBtn.textContent = 'Link reenviado';
    resendBtn.disabled = true;
    resendBtn.classList.remove('is-busy');
  } catch (err) {
    setBusy(resendBtn, false);
    showAlert(formAlert, err instanceof ApiError ? err.message : 'Não foi possível reenviar agora.');
  }
});
