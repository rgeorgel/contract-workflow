// Validação, erros por campo e estado de envio dos formulários.

/** Lê os campos nomeados como objeto simples (checkbox → boolean). */
export function serialize(form) {
  const data = {};
  for (const el of form.elements) {
    if (!el.name || el.disabled) continue;
    data[el.name] = el.type === 'checkbox' ? el.checked : el.value;
  }
  return data;
}

function errorSlot(form, name) {
  return form.querySelector(`[data-error-for="${name}"]`);
}

export function setFieldError(form, name, message) {
  const input = form.elements.namedItem(name);
  const slot = errorSlot(form, name);
  if (slot) slot.textContent = message || '';
  if (input instanceof HTMLElement) {
    if (message) input.setAttribute('aria-invalid', 'true');
    else input.removeAttribute('aria-invalid');
  }
}

export function clearErrors(form) {
  for (const slot of form.querySelectorAll('[data-error-for]')) slot.textContent = '';
  for (const el of form.querySelectorAll('[aria-invalid]')) el.removeAttribute('aria-invalid');
}

/**
 * Aplica erros por campo ({ campo: [mensagens] }). Retorna true se algum foi aplicado.
 * Chaves vindas do servidor podem chegar em PascalCase; normaliza para camelCase.
 */
export function applyErrors(form, errors) {
  let first = null;
  for (const [key, messages] of Object.entries(errors || {})) {
    const name = key.charAt(0).toLowerCase() + key.slice(1);
    const msg = Array.isArray(messages) ? messages[0] : String(messages);
    if (!form.elements.namedItem(name)) continue;
    setFieldError(form, name, msg);
    first ??= name;
  }
  if (first) form.elements.namedItem(first)?.focus();
  return first !== null;
}

export function setBusy(button, busy) {
  button.disabled = busy;
  button.classList.toggle('is-busy', busy);
  button.setAttribute('aria-busy', String(busy));
}

/** Mostra uma mensagem num contêiner .alert (texto via textContent, nunca innerHTML). */
export function showAlert(el, message, kind = 'error') {
  el.className = `alert alert--${kind}`;
  el.textContent = message;
  el.hidden = !message;
}

export function hideAlert(el) {
  el.hidden = true;
  el.textContent = '';
}

/** Botão "Mostrar/Ocultar" senha. */
export function wirePasswordToggles(root = document) {
  for (const btn of root.querySelectorAll('[data-toggle-password]')) {
    const input = document.getElementById(btn.dataset.togglePassword);
    btn.addEventListener('click', () => {
      const show = input.type === 'password';
      input.type = show ? 'text' : 'password';
      btn.textContent = show ? 'Ocultar' : 'Mostrar';
      btn.setAttribute('aria-pressed', String(show));
      input.focus();
    });
  }
}

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
export const isEmail = (v) => EMAIL_RE.test(v.trim());
