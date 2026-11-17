// Página de teste do embed de templates: faz o papel do "sistema do cliente" (um CRM fictício).
// Como na integração de verdade, a chave de API fica só aqui no servidor; o navegador recebe só o link da sessão.
//
//   CONTRATOS_API_KEY=ctr_... node tools/embed-demo/server.js
//
// As variáveis também podem ficar em tools/embed-demo/.env.local (fora do git), uma por linha: NOME=valor.
// Variáveis:
//   CONTRATOS_API_KEY  chave criada em Integrações (obrigatória)
//   CONTRATOS_URL      endereço do Contratos (padrão: http://localhost:7700)
//   PORT               porta desta página (padrão: 3000). Cadastre http://localhost:PORT em Integrações › Embed.
// Sem dependências: só Node 18+.

import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const DIR = fileURLToPath(new URL('.', import.meta.url));

// .env.local opcional (o ambiente tem prioridade).
try {
  for (const line of (await readFile(join(DIR, '.env.local'), 'utf8')).split('\n')) {
    const match = line.match(/^\s*([A-Z_]+)\s*=\s*(.*?)\s*$/);
    if (match && process.env[match[1]] === undefined) process.env[match[1]] = match[2];
  }
} catch { /* sem arquivo */ }
const PORT = Number(process.env.PORT ?? 3000);
const CONTRATOS_URL = (process.env.CONTRATOS_URL ?? 'http://localhost:7700').replace(/\/+$/, '');
const API_KEY = process.env.CONTRATOS_API_KEY ?? '';
const SELF = `http://localhost:${PORT}`;

if (!API_KEY.startsWith('ctr_')) {
  console.error('Defina CONTRATOS_API_KEY com uma chave criada em Integrações (começa com ctr_).');
  process.exit(1);
}

const TYPES = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8' };

/** Chama a API pública do Contratos com a chave (só daqui do servidor). */
async function contratos(method, path, body) {
  const res = await fetch(`${CONTRATOS_URL}/api/public/v1${path}`, {
    method,
    headers: {
      Authorization: `Bearer ${API_KEY}`,
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  return { status: res.status, body: text ? JSON.parse(text) : null };
}

async function readJson(req) {
  let raw = '';
  for await (const chunk of req) raw += chunk;
  return raw ? JSON.parse(raw) : {};
}

function send(res, status, body) {
  res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store' });
  res.end(body === null || body === undefined ? '' : JSON.stringify(body));
}

const routes = {
  // O que a página precisa saber para conferir a origem dos avisos (postMessage).
  'GET /api/config': async () => ({ status: 200, body: { contratosUrl: CONTRATOS_URL, contratosOrigin: new URL(CONTRATOS_URL).origin, self: SELF } }),
  'GET /api/account': () => contratos('GET', '/account'),
  'GET /api/templates': () => contratos('GET', '/templates'),
  // Cria a sessão do embed. Num sistema de verdade, aqui entra o usuário logado e as permissões dele.
  'POST /api/session': async (req) => {
    const input = await readJson(req);
    return contratos('POST', '/embed/sessions', {
      permissions: input.permissions,
      user: input.userName ? { id: 'demo-user', name: input.userName } : undefined,
      parentOrigin: SELF,
      expiresInMinutes: input.expiresInMinutes,
      templateId: input.templateId || undefined,
    });
  },
};

createServer(async (req, res) => {
  try {
    const url = new URL(req.url, SELF);
    const sessionMatch = url.pathname.match(/^\/api\/session\/([0-9a-f-]{36})$/);
    if (req.method === 'DELETE' && sessionMatch) {
      const r = await contratos('DELETE', `/embed/sessions/${sessionMatch[1]}`);
      return send(res, r.status, r.body);
    }
    const route = routes[`${req.method} ${url.pathname}`];
    if (route) {
      const r = await route(req);
      return send(res, r.status, r.body);
    }
    if (req.method !== 'GET') return send(res, 405, { title: 'Método não permitido' });

    const file = url.pathname === '/' ? 'index.html' : url.pathname.slice(1);
    if (!['index.html', 'demo.js', 'demo.css'].includes(file)) return send(res, 404, { title: 'Não encontrado' });
    res.writeHead(200, { 'Content-Type': TYPES[extname(file)], 'Cache-Control': 'no-store' });
    res.end(await readFile(join(DIR, file)));
  } catch (err) {
    send(res, 502, { title: `Falha ao falar com o Contratos (${CONTRATOS_URL}): ${err.message}` });
  }
}).listen(PORT, () => {
  console.log(`Página de teste do embed: ${SELF}`);
  console.log(`Contratos: ${CONTRATOS_URL}. Cadastre ${SELF} em Integrações › Embed de templates.`);
});
