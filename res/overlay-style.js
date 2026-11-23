// Estilo de los overlays elegido en Vortexs Live (pestaña Overlays → Personalizar).
// Lo usan las tres páginas (delay, espectadores y chat en pantalla). Lee /style.json cada 2 s y lo
// aplica con variables CSS, así los cambios se ven al momento en OBS:
//   --a1 --mid --a2  colores del borde y de la cifra (degradado)    --bg --bg2  fondo
//   --tx --lbl       texto y etiquetas                              --r --vr --cr  esquinas
//   --bw             grosor del borde                               --glow  brillo
//   --font --lfont   fuente (y la de las etiquetas)
// Clases en <body>: sd-circle (forma círculo), sd-noicon, sd-bubbles (fondo en el chat), sd-noborder.
// Si la página define window.onStyle(st), se llama después de aplicarlo.
(function () {
  // ?formato=vertical: lienzo de 1080x1920 (TikTok, Shorts, Aitum Vertical). Cada página lo ajusta con body.fv.
  if (/[?&]formato=vertical(&|$)/.test(location.search)) document.body.classList.add('fv');
  // Fuentes de Google Fonts y sus grosores (las de un solo grosor van sin nada).
  var FONTS = {
    'Outfit': 'wght@700;800', 'Montserrat': 'wght@600;700;800', 'Poppins': 'wght@600;700;800', 'Inter': 'wght@600;700;800',
    'Bebas Neue': '', 'Oswald': 'wght@600;700', 'Orbitron': 'wght@700;800', 'Russo One': '', 'Rubik': 'wght@600;700;800',
    'Bangers': '', 'Permanent Marker': '', 'Press Start 2P': ''
  };
  var loaded = {}, last = '';
  function rgb(hex) { var n = parseInt(String(hex).slice(1), 16) || 0; return [n >> 16 & 255, n >> 8 & 255, n & 255]; }
  function rgba(hex, a) { var c = rgb(hex); return 'rgba(' + c[0] + ',' + c[1] + ',' + c[2] + ',' + a + ')'; }
  function toWhite(hex, t) { return 'rgb(' + rgb(hex).map(function (v) { return Math.round(v + (255 - v) * t); }).join(',') + ')'; }
  function loadFont(name) {
    if (!(name in FONTS) || loaded[name]) return;
    loaded[name] = true;
    var l = document.createElement('link');
    l.rel = 'stylesheet';
    l.href = 'https://fonts.googleapis.com/css2?family=' + name.replace(/ /g, '+') + (FONTS[name] ? ':' + FONTS[name] : '') + '&display=swap';
    document.head.appendChild(l);
  }
  function apply(st) {
    var key = JSON.stringify(st);
    if (key === last) return;
    last = key;
    window.SD_STYLE = st;
    var r = document.documentElement.style, b = document.body.classList;
    r.setProperty('--a1', st.a1);
    r.setProperty('--a2', st.grad ? st.a2 : st.a1);
    r.setProperty('--mid', st.grad ? toWhite(st.a1, 0.55) : st.a1);
    r.setProperty('--bg', rgba(st.bg, st.op / 100));
    r.setProperty('--bg2', rgba(st.bg, Math.min(1, st.op / 100)));
    r.setProperty('--tx', st.tx);
    r.setProperty('--lbl', rgba(st.tx, 0.62));
    r.setProperty('--r', st.shape === 'circle' ? '50%' : st.shape === 'pill' ? '999px' : st.shape === 'rect' ? '4px' : '22px');
    r.setProperty('--vr', st.shape === 'rect' ? '4px' : st.shape === 'round' ? '18px' : '999px');
    r.setProperty('--cr', st.shape === 'rect' ? '3px' : st.shape === 'round' ? '10px' : '18px');
    r.setProperty('--bw', (st.bd || 0) + 'px');
    r.setProperty('--glow', st.glow ? rgba(st.a1, 0.28) : 'transparent');
    loadFont(st.font);
    r.setProperty('--font', "'" + st.font + "', 'Segoe UI', sans-serif");
    r.setProperty('--lfont', st.font === 'Outfit' ? "'JetBrains Mono', Consolas, monospace" : "'" + st.font + "', 'Segoe UI', sans-serif");
    b.toggle('sd-circle', st.shape === 'circle');
    b.toggle('sd-noicon', !st.icon);
    b.toggle('sd-bubbles', !!st.bubbles);
    b.toggle('sd-noborder', !st.bd);
    if (window.onStyle) window.onStyle(st);
  }
  function tick() {
    fetch('/style.json', { cache: 'no-store' }).then(function (r) { return r.json(); })
      .then(function (st) { if (st && st.a1) apply(st); }).catch(function () {})
      .then(function () { setTimeout(tick, 2000); });
  }
  tick();
})();
