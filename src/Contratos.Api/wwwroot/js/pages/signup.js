import { api, ApiError } from '../api.js';
import { applyErrors, clearErrors, hideAlert, isEmail, serialize, setBusy, setFieldError, showAlert, wirePasswordToggles } from '../ui/form.js';

const MIN_PASSWORD = 6;
const RESEND_COOLDOWN_S = 60;

const states = document.querySelectorAll('[data-state]');
function show(state) {
  for (const el of states) el.hidden = el.dataset.state !== state;
  document.querySelector(`[data-state="${state}"] h1`)?.focus();
}

wirePasswordToggles();

// ---------- Cadastro ----------
const form = document.getElementById('register-form');
const formAlert = form.querySelector('[data-form-alert]');
const meter = document.querySelector('[data-password-meter]');
let sentTo = '';

form.elements.password.addEventListener('input', (e) => {
  meter.classList.toggle('is-ok', e.target.value.length >= MIN_PASSWORD);
});

// Limpa o erro do campo assim que a pessoa corrige.
form.addEventListener('input', (e) => {
  if (e.target.name && e.target.getAttribute('aria-invalid')) setFieldError(form, e.target.name, '');
});

function validate(data) {
  const errors = {};
  if (data.organizationName.trim().length < 2) errors.organizationName = ['Informe o nome da empresa.'];
  if (data.name.trim().length < 2) errors.name = ['Informe seu nome.'];
  if (!data.email.trim()) errors.email = ['Informe seu e-mail.'];
  else if (!isEmail(data.email)) errors.email = ['E-mail inválido.'];
  if (data.password.length < MIN_PASSWORD) errors.password = [`A senha precisa ter ao menos ${MIN_PASSWORD} caracteres.`];
  return errors;
}

form.addEventListener('submit', async (e) => {
  e.preventDefault();
  clearErrors(form);
  hideAlert(formAlert);

  const data = serialize(form);
  if (applyErrors(form, validate(data))) return;

  const button = form.querySelector('[type="submit"]');
  setBusy(button, true);
  try {
    await api('/auth/register', {
      method: 'POST',
      body: {
        organizationName: data.organizationName.trim(),
        name: data.name.trim(),
        email: data.email.trim(),
        password: data.password,
      },
    });
    sentTo = data.email.trim();
    document.querySelector('[data-sent-email]').textContent = sentTo;
    form.elements.password.value = '';
    meter.classList.remove('is-ok');
    show('check-email');
    startCooldown(document.querySelector('[data-resend]'));
  } catch (err) {
    if (err instanceof ApiError && applyErrors(form, err.errors)) return;
    showAlert(formAlert, err instanceof ApiError ? err.message : 'Não foi possível criar a conta. Tente de novo.');
  } finally {
    setBusy(button, false);
  }
});

// ---------- Reenviar (tela "verifique seu e-mail") ----------
const resendBtn = document.querySelector('[data-resend]');
const resendAlert = document.querySelector('[data-resend-alert]');

function startCooldown(button) {
  let left = RESEND_COOLDOWN_S;
  const label = button.textContent.replace(/ \(\d+s\)$/, '');
  button.disabled = true;
  button.textContent = `${label} (${left}s)`;
  const timer = setInterval(() => {
    left -= 1;
    if (left <= 0) {
      clearInterval(timer);
      button.disabled = false;
      button.textContent = label;
    } else {
      button.textContent = `${label} (${left}s)`;
    }
  }, 1000);
}

async function resend(email) {
  return api('/auth/resend-confirmation', { method: 'POST', body: { email } });
}

resendBtn.addEventListener('click', async () => {
  hideAlert(resendAlert);
  setBusy(resendBtn, true);
  try {
    await resend(sentTo);
    showAlert(resendAlert, 'Pronto, enviamos outro link.', 'success');
    setBusy(resendBtn, false);
    startCooldown(resendBtn);
  } catch (err) {
    setBusy(resendBtn, false);
    showAlert(resendAlert, err instanceof ApiError ? err.message : 'Não foi possível reenviar agora.');
  }
});

document.querySelector('[data-change-email]').addEventListener('click', () => {
  show('register');
  form.elements.email.focus();
  form.elements.email.select();
});

// ---------- Confirmação pelo link do e-mail ----------
const resendForm = document.getElementById('resend-form');
const resendFormAlert = resendForm.querySelector('[data-resend-form-alert]');

resendForm.addEventListener('submit', async (e) => {
  e.preventDefault();
  clearErrors(resendForm);
  hideAlert(resendFormAlert);
  const email = resendForm.elements.email.value.trim();
  if (!isEmail(email)) {
    setFieldError(resendForm, 'email', 'E-mail inválido.');
    resendForm.elements.email.focus();
    return;
  }
  const button = resendForm.querySelector('[type="submit"]');
  setBusy(button, true);
  try {
    await resend(email);
    showAlert(resendFormAlert, 'Se houver um cadastro pendente para este e-mail, um novo link já está a caminho.', 'success');
  } catch (err) {
    showAlert(resendFormAlert, err instanceof ApiError ? err.message : 'Não foi possível reenviar agora.');
  } finally {
    setBusy(button, false);
  }
});

async function confirmFromLink(params) {
  const userId = params.get('userId');
  const token = params.get('token');
  // Tira o token da barra de endereço e do histórico.
  history.replaceState(null, '', location.pathname);

  if (!userId || !token) {
    show('confirm-failed');
    return;
  }

  show('confirming');
  try {
    await api('/auth/confirm-email', { method: 'POST', body: { userId, token } });
    show('confirmed');
  } catch {
    show('confirm-failed');
  }
}

const params = new URLSearchParams(location.search);
if (params.get('step') === 'confirm') {
  confirmFromLink(params);
} else {
  form.elements.organizationName.focus();
}
