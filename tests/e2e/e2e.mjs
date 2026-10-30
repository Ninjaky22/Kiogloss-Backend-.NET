// End-to-end run of FrontendKioglossUser (:5173) and FrontendKioglossAdmin (:5174) against the .NET API (:8000).
//
//   JWT_ACCESS_EXPIRATION_MS=70000 docker compose up -d --build   # short tokens so the refresh steps take ~1 min
//   (cd FrontendKioglossUser && npx vite --port 5173 --strictPort) & (cd FrontendKioglossAdmin && npx vite --port 5174 --strictPort) &
//   cd tests/e2e && npm install && npx playwright install chromium && node e2e.mjs
//
// CHROMIUM_PATH=/path/to/chrome overrides the browser Playwright downloaded (useful on distros Playwright
// does not support yet). Screenshots go to tests/e2e/shots/.
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';

const API = process.env.API_URL ?? 'http://localhost:8000/API/V1.0';
const SHOP = process.env.SHOP_URL ?? 'http://127.0.0.1:5173';
const ADMIN = process.env.ADMIN_URL ?? 'http://127.0.0.1:5174';
// the refresh steps wait for the access token to expire: run the API with JWT_ACCESS_EXPIRATION_MS=70000
const ACCESS_TOKEN_MS = Number(process.env.ACCESS_TOKEN_MS ?? 70000);
const SHOTS = new URL('./shots/', import.meta.url).pathname;
mkdirSync(SHOTS, { recursive: true });

const uniq = String(Date.now()).slice(-9);
const userEmail = `cliente.e2e.${uniq}@kiogloss.com`;
const adminEmail = `admin.e2e.${uniq}@kiogloss.com`;
const password = 'Secret123!';
const slug = `labial-e2e-${uniq}`;
const title = `Labial E2E ${uniq}`;

const results = [];
const apiErrors = [];
const consoleErrors = [];
const step = async (name, fn) => {
  const t0 = Date.now();
  try {
    const detail = await fn();
    results.push({ name, ok: true, ms: Date.now() - t0, detail });
    console.log(`  OK   ${name}${detail ? '  — ' + detail : ''}`);
  } catch (e) {
    results.push({ name, ok: false, ms: Date.now() - t0, detail: String(e.message || e).split('\n')[0] });
    console.log(`  FAIL ${name}  — ${String(e.message || e).split('\n')[0]}`);
  }
};
const freshAdmin = async () => (await api('POST', '/login', { email: adminEmail, password })).access;
const api = async (method, path, body, token) => {
  const res = await fetch(API + path, {
    method,
    headers: { ...(body ? { 'Content-Type': 'application/json' } : {}), ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  });
  const text = await res.text();
  if (res.status >= 400) throw new Error(`${method} ${path} -> ${res.status} ${text.slice(0, 200)}`);
  return text ? JSON.parse(text) : null;
};
const T0 = Date.now();
const sseLog = [];
const watch = (page, tag) => {
  page.on('request', (r) => { if (r.url().includes('/stream')) sseLog.push(`${((Date.now() - T0) / 1000).toFixed(1)}s ${tag} SSE abre (iat=${JSON.parse(Buffer.from(r.url().split('token=')[1].split('.')[1], 'base64url')).iat}) página=${page.url().replace(/^https?:\/\/[^/]+/, '')}`); });
  page.on('requestfinished', (r) => { if (r.url().includes('/stream')) sseLog.push(`${((Date.now() - T0) / 1000).toFixed(1)}s ${tag} SSE terminó`); });
  page.on('requestfailed', (r) => { if (r.url().includes('/stream')) sseLog.push(`${((Date.now() - T0) / 1000).toFixed(1)}s ${tag} SSE cerrada por el navegador (${r.failure()?.errorText})`); });
  page.on('response', (r) => { if (r.status() === 404) consoleErrors.push(`${tag} 404 ${r.url()}`); });
  page.on('response', (r) => {
    if (r.url().startsWith(API) && r.status() >= 400)
      apiErrors.push(`${((Date.now() - T0) / 1000).toFixed(1)}s ${tag} ${r.request().method()} ${r.url().replace(API, '').replace(/token=[^&]+/, 'token=…')} -> ${r.status()}`);
  });
  page.on('console', (m) => { if (m.type() === 'error') consoleErrors.push(`${tag} ${m.text().slice(0, 200)}`); });
  page.on('pageerror', (e) => consoleErrors.push(`${tag} pageerror ${e.message.slice(0, 200)}`));
};

console.log(`E2E run ${uniq}`);
// ------------------------------------------------------------ setup through the API
let adminToken, product;
await step('setup: crear admin (POST /user/ con isSuperuser)', async () => {
  adminToken = (await api('POST', '/user/', { email: adminEmail, name: 'Admin E2E', password, phoneNumber: '31' + uniq.slice(-8), isSuperuser: true })).access;
  return adminEmail;
});
await step('setup: crear producto publicado (POST /admin/products)', async () => {
  product = await api('POST', '/admin/products', {
    title, price: 45000, description: '<p>Labial de prueba E2E</p>', slug, status: 'published', stock: 5,
    attributes: { Marca: 'Kiogloss', Tono: 'Rojo' }, tagIds: [1],
  }, adminToken);
  return `id ${product.id}`;
});

const browser = await chromium.launch({ headless: true, ...(process.env.CHROMIUM_PATH ? { executablePath: process.env.CHROMIUM_PATH } : {}) });

// ------------------------------------------------------------ customer frontend
const shopCtx = await browser.newContext({ viewport: { width: 1366, height: 900 }, locale: 'es-CO' });
const shop = await shopCtx.newPage();
watch(shop, '[tienda]');
const refreshCalls = { shop: [], admin: [] };
shop.on('response', (r) => { if (r.url().endsWith('/refresh')) refreshCalls.shop.push(r.status()); });
const swal = (p) => p.locator('.swal2-title').first();

await step('tienda: registro de usuario', async () => {
  await shop.goto(`${SHOP}/register`);
  await shop.getByPlaceholder('Tu nombre').fill('Cliente E2E');
  await shop.getByPlaceholder('tucorreo@dominio.com').fill(userEmail);
  await shop.getByPlaceholder('••••••••').first().fill(password);
  await shop.getByPlaceholder('3001234567').fill('32' + uniq.slice(-8));
  await shop.getByPlaceholder('Calle *').fill('Calle 10');
  await shop.getByPlaceholder('Número *').fill('5-20');
  await shop.getByPlaceholder('Barrio *').fill('Centro');
  await shop.getByPlaceholder('Ciudad *').fill('Neiva');
  await shop.locator('button[type="submit"]').click();
  await shop.waitForURL('**/account', { timeout: 15000 });
  console.log(`      (registro completado a los ${((Date.now() - T0) / 1000).toFixed(1)}s)`);
  await shop.getByText('Cliente E2E').first().waitFor({ timeout: 10000 });
  await shop.screenshot({ path: SHOTS + '01-cuenta.png' });
  return 'redirigido a /account con el nombre visible';
});
await step('tienda: catálogo (/shop) muestra el producto nuevo', async () => {
  await shop.goto(`${SHOP}/shop`);
  await shop.getByText(title).first().waitFor({ timeout: 15000 });
  await shop.screenshot({ path: SHOTS + '02-shop.png' });
});
await step('tienda: detalle del producto (/product/:slug)', async () => {
  await shop.goto(`${SHOP}/product/${slug}`);
  await shop.getByText(title).first().waitFor({ timeout: 15000 });
  await shop.getByText('Kiogloss').first().waitFor({ timeout: 5000 });
  await shop.screenshot({ path: SHOTS + '03-producto.png' });
  return 'título y atributos visibles';
});
await step('tienda: agregar al carrito', async () => {
  await shop.getByRole('button', { name: /Agregar al carrito/ }).first().click();
  await swal(shop).waitFor({ timeout: 8000 });
  return await swal(shop).innerText();
});
await step('tienda: agregar a favoritos', async () => {
  await shop.waitForTimeout(2500);
  await shop.getByRole('button', { name: /Agregar a favoritos/ }).first().click();
  await shop.getByText('Agregado a favoritos').first().waitFor({ timeout: 8000 });
  return 'toast "Agregado a favoritos"';
});
await step('tienda: carrito (/cart)', async () => {
  await shop.goto(`${SHOP}/cart`);
  await shop.getByText(title).first().waitFor({ timeout: 15000 });
  await shop.getByText('Proceder al Pago').first().waitFor({ timeout: 5000 });
  await shop.screenshot({ path: SHOTS + '04-carrito.png' });
});
await step('tienda: favoritos (/wishlist)', async () => {
  await shop.goto(`${SHOP}/wishlist`);
  await shop.getByText(title).first().waitFor({ timeout: 15000 });
  await shop.screenshot({ path: SHOTS + '05-wishlist.png' });
});

let orderId;
await step('tienda: crear la orden como lo hace Checkout tras pagar con PayPal', async () => {
  const token = await shop.evaluate(() => localStorage.getItem('accessToken'));
  const userId = JSON.parse(Buffer.from(token.split('.')[1], 'base64url').toString()).user_id;
  const me = await api('GET', `/user/${userId}`, null, token);
  const cart = await api('GET', '/user/cart?page=0&page_size=200', null, token);
  await api('POST', '/user/order', {
    account: me.account.id,
    shopping: cart.content.map((i) => ({ product: i.variantId, quantity: i.quantity, price: i.price })),
    amount: cart.grandTotal, status: 'PENDING',
  }, token);
  await api('DELETE', '/user/cart', null, token);
  return `cuenta ${me.account.id}, total ${cart.grandTotal}`;
});
await step('tienda: mis órdenes (/orders)', async () => {
  await shop.goto(`${SHOP}/orders`);
  await shop.getByText(title).first().waitFor({ timeout: 15000 });
  await shop.getByText('Pendiente').first().waitFor({ timeout: 5000 });
  await shop.screenshot({ path: SHOTS + '06-ordenes.png' });
  return 'estado "Pendiente"';
});

// ------------------------------------------------------------ admin frontend
const adminCtx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'es-CO' });
const admin = await adminCtx.newPage();
watch(admin, '[admin]');
admin.on('response', (r) => { if (r.url().endsWith('/refresh')) refreshCalls.admin.push(r.status()); });
let adminLoginAt;

await step('admin: iniciar sesión', async () => {
  await admin.goto(`${ADMIN}/#/log-in`);
  await admin.locator('#email').fill(adminEmail);
  await admin.locator('#password').fill(password);
  await admin.locator('button[type="submit"]').click();
  await admin.waitForFunction(() => !location.hash.includes('log-in'), null, { timeout: 15000 });
  adminLoginAt = Date.now();
  return admin.url().replace(ADMIN, '');
});
await step('admin: listado de pedidos', async () => {
  await admin.goto(`${ADMIN}/#/orders`);
  await admin.getByText('Cliente E2E').first().waitFor({ timeout: 15000 });
  const orders = await api('GET', '/admin/orders?page=0&page_size=50', null, await freshAdmin());
  orderId = orders.data.content.find((o) => o.customer?.email === userEmail).id;
  await admin.screenshot({ path: SHOTS + '07-admin-pedidos.png' });
  return `pedido #${orderId}`;
});
await step('admin: cambiar estado del pedido a Enviado (SHIPPED)', async () => {
  await admin.goto(`${ADMIN}/#/orders/${orderId}`);
  await admin.getByText(title).first().waitFor({ timeout: 15000 });
  // MUI marks the main content aria-hidden while the drawer/popovers are mounted, so use CSS locators
  await admin.locator('div[role="combobox"]').first().click();
  await admin.locator('li[data-value="SHIPPED"]').click();
  await admin.locator('button:has-text("Actualizar")').click();
  await admin.waitForFunction(() => location.hash === '#/orders', null, { timeout: 10000 });
  const order = await api('GET', `/admin/orders/${orderId}`, null, await freshAdmin());
  if (order.data.status !== 'Enviado') throw new Error('status = ' + order.data.status);
  return 'API confirma "Enviado"';
});
await step('tienda: llega la notificación en vivo (SSE) a la campana', async () => {
  const bell = shop.locator('button[aria-label^="Notificaciones"]').first();
  await shop.waitForFunction(() => {
    const b = document.querySelector('button[aria-label^="Notificaciones"]');
    return b && b.getAttribute('aria-label').includes('sin leer');
  }, null, { timeout: 15000 });
  const label = await bell.getAttribute('aria-label');
  await bell.click();
  await shop.getByText('Estado de tu pedido actualizado').first().waitFor({ timeout: 5000 });
  await shop.screenshot({ path: SHOTS + '08-notificacion.png' });
  return `${label}, sin recargar la página`;
});
await step('admin: productos', async () => {
  await admin.goto(`${ADMIN}/#/products`);
  await admin.getByText(title).first().waitFor({ timeout: 15000 });
  await admin.screenshot({ path: SHOTS + '09-admin-productos.png' });
});
await step('admin: usuarios', async () => {
  await admin.goto(`${ADMIN}/#/users`);
  await admin.getByText(userEmail).first().waitFor({ timeout: 15000 });
});
await step('admin: reportes', async () => {
  await admin.goto(`${ADMIN}/#/reports`);
  await admin.getByText('Ingresos totales').first().waitFor({ timeout: 15000 });
  await admin.waitForTimeout(1500);
  await admin.screenshot({ path: SHOTS + '10-admin-reportes.png' });
});
await step('admin: campana de notificaciones (pedido nuevo)', async () => {
  const unread = await api('GET', '/admin/notifications/unread-count', null, await freshAdmin());
  if (unread.count < 1) throw new Error('sin notificaciones');
  return `${unread.count} sin leer en la API`;
});

// ------------------------------------------------------------ token expiry (access token = 70 s for this run)
await step('admin: sesión sigue viva tras expirar el access token (403 → /refresh)', async () => {
  const wait = Math.max(0, adminLoginAt + ACCESS_TOKEN_MS + 6000 - Date.now());
  await admin.waitForTimeout(wait);
  const before = refreshCalls.admin.length;
  await admin.goto(`${ADMIN}/#/orders`);
  await admin.reload();
  await admin.getByText('Cliente E2E').first().waitFor({ timeout: 20000 });
  if (admin.url().includes('log-in')) throw new Error('redirigió al login');
  const calls = refreshCalls.admin.slice(before);
  if (!calls.includes(200)) throw new Error('no hubo /refresh exitoso: ' + JSON.stringify(calls));
  return `/refresh -> ${calls.join(',')}; pedidos visibles`;
});
await step('tienda: sesión sigue viva (refresh proactivo antes de expirar)', async () => {
  await shop.goto(`${SHOP}/account`);
  await shop.getByText('Cliente E2E').first().waitFor({ timeout: 15000 });
  if (!refreshCalls.shop.includes(200)) throw new Error('no hubo /refresh: ' + JSON.stringify(refreshCalls.shop));
  return `/refresh llamados: ${refreshCalls.shop.length}, todos ${[...new Set(refreshCalls.shop)].join('/')}`;
});

await browser.close();

const failed = results.filter((r) => !r.ok);
console.log(`\n${results.length - failed.length}/${results.length} pasos OK`);
console.log(`Respuestas API >= 400 vistas por el navegador (${apiErrors.length}):`);
for (const e of [...new Set(apiErrors)]) console.log('   ', e);
console.log('Eventos SSE:'); for (const e of sseLog) console.log('   ', e);
console.log(`Errores de consola (${consoleErrors.length}):`);
for (const e of [...new Set(consoleErrors)].slice(0, 25)) console.log('   ', e);
process.exit(failed.length ? 1 : 0);
