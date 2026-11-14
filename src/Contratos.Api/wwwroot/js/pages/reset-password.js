import { api, ApiError } from '../api.js';
import { applyErrors, clearErrors, hideAlert, isEmail, setBusy, setFieldError, showAlert, wirePasswordToggles } from '../ui/form.js';

const MIN_PASSWORD = 6;
const params = new URLSearchParams(location.search);
const userId = params.get('userId');
const token = params.get('token');

function show(state) {
  for (const el of document.querySelectorAll('[data-state]')) el.hidden = el.dataset.state !== state;
  document.querySelector(`[data-state="${state}"] h1`)?.focus();
}

wirePasswordToggles();

// Pedido do link
const requestForm = document.getElementById('request-form');
requestForm.addEventListener('submit', async (e) => {
  e.preventDefault();
  clearErrors(requestForm);
  const alertEl = requestForm.querySelector('[data-form-alert]');
  hideAlert(alertEl);
  const email = requestForm.elements.email.value.trim();
  if (!isEmail(email)) { setFieldError(requestForm, 'email', 'Informe um e-mail válido.'); requestForm.elements.email.focus(); return; }

  const button = requestForm.querySelector('[type="submit"]');
  setBusy(button, true);
  try {
    await api('/auth/forgot-password', { method: 'POST', body: { email } });
    document.querySelector('[data-sent-email]').textContent = email;
    show('sent');
  } catch (err) {
    showAlert(alertEl, err.message);
  } finally {
    setBusy(button, false);
  }
});

// Nova senha (link do e-mail)
const resetForm = document.getElementById('reset-form');
resetForm.addEventListener('submit', async (e) => {
  e.preventDefault();
  clearErrors(resetForm);
  const alertEl = resetForm.querySelector('[data-form-alert]');
  hideAlert(alertEl);
  const password = resetForm.elements.password.value;
  if (password.length < MIN_PASSWORD) {
    setFieldError(resetForm, 'password', `A senha precisa ter ao menos ${MIN_PASSWORD} caracteres.`);
    resetForm.elements.password.focus();
    return;
  }

  const button = resetForm.querySelector('[type="submit"]');
  setBusy(button, true);
  try {
    await api('/auth/reset-password', { method: 'POST', body: { userId, token, password } });
    show('done');
  } catch (err) {
    if (err instanceof ApiError && err.code === 'invalid_token') {
      show('request');
      showAlert(requestForm.querySelector('[data-form-alert]'), err.message);
      return;
    }
    if (!(err instanceof ApiError && applyErrors(resetForm, err.errors))) showAlert(alertEl, err.message);
  } finally {
    setBusy(button, false);
  }
});

if (userId && token) {
  history.replaceState(null, '', location.pathname);
  show('reset');
  resetForm.elements.password.focus();
} else {
  requestForm.elements.email.focus();
}
