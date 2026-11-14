// Borda no cabeçalho ao rolar e ano corrente no rodapé.
const header = document.querySelector('[data-header]');
const onScroll = () => header?.classList.toggle('is-scrolled', window.scrollY > 8);
window.addEventListener('scroll', onScroll, { passive: true });
onScroll();

const year = document.querySelector('[data-year]');
if (year) year.textContent = String(new Date().getFullYear());
