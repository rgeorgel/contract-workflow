// Tema claro/escuro da área logada. Script clássico no <head> (antes do CSS) para o tema valer já na primeira
// pintura, sem a tela piscar em branco. A escolha do botão fica no localStorage; sem escolha, segue o sistema.
(function () {
  // No embed a tela fica dentro do sistema do cliente: mantém o visual claro.
  if (location.pathname.indexOf('/embed/') === 0) return;

  var KEY = 'contratos.theme';
  var root = document.documentElement;
  var media = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;

  function stored() {
    try {
      var value = localStorage.getItem(KEY);
      return value === 'dark' || value === 'light' ? value : null;
    } catch (e) { return null; }
  }
  function system() { return media && media.matches ? 'dark' : 'light'; }
  function apply(theme) {
    root.setAttribute('data-theme', theme);
    document.dispatchEvent(new CustomEvent('themechange', { detail: theme }));
  }

  apply(stored() || system());
  if (media && media.addEventListener) {
    media.addEventListener('change', function () { if (!stored()) apply(system()); });
  }

  window.contratosTheme = {
    get: function () { return root.getAttribute('data-theme') || 'light'; },
    set: function (theme) {
      try { localStorage.setItem(KEY, theme); } catch (e) { /* sem storage: vale só nesta página */ }
      apply(theme);
    },
  };
})();
