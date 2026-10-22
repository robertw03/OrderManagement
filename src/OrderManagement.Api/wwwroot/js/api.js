// Thin client for the Order Management REST API. Every call sends a fresh X-Correlation-ID so a
// request seen in the UI can be found in the server logs, plus X-User-Id for the audit columns.

const USER_STORAGE_KEY = 'orderManagement.user';
export const DEFAULT_USER = 'admin.demo';

export function getUser() {
  try {
    return localStorage.getItem(USER_STORAGE_KEY) || DEFAULT_USER;
  } catch {
    return DEFAULT_USER;
  }
}

export function setUser(name) {
  try {
    localStorage.setItem(USER_STORAGE_KEY, name);
  } catch {
    // Storage unavailable (private mode); the name just won't be remembered.
  }
}

export function newId() {
  if (globalThis.crypto?.randomUUID) return crypto.randomUUID();
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, c => {
    const r = (Math.random() * 16) | 0;
    return (c === 'x' ? r : (r & 0x3) | 0x8).toString(16);
  });
}

/**
 * Sends a request and always resolves (never throws) with
 * { ok, status, data, headers, correlationId, ms } so callers can inspect failures too.
 */
export async function request(method, path, { body, headers = {}, user } = {}) {
  const correlationId = `ui-${newId().replaceAll('-', '').slice(0, 12)}`;
  const init = {
    method,
    headers: {
      Accept: 'application/json',
      'X-Correlation-ID': correlationId,
      'X-User-Id': user ?? getUser(),
      ...headers
    }
  };
  if (body !== undefined) {
    init.headers['Content-Type'] = 'application/json';
    init.body = JSON.stringify(body);
  }

  const started = performance.now();
  let response;
  try {
    response = await fetch(path, init);
  } catch (error) {
    return {
      ok: false, status: 0, headers: new Headers(), correlationId,
      data: { title: 'Network error', detail: error.message },
      ms: Math.round(performance.now() - started)
    };
  }

  const text = await response.text();
  let data = null;
  if (text) {
    try {
      data = JSON.parse(text);
    } catch {
      data = { detail: text };
    }
  }

  return {
    ok: response.ok,
    status: response.status,
    data,
    headers: response.headers,
    correlationId: response.headers.get('X-Correlation-ID') || correlationId,
    ms: Math.round(performance.now() - started)
  };
}

function query(params) {
  const clean = Object.entries(params).filter(([, v]) => v !== undefined && v !== null && v !== '');
  return new URLSearchParams(clean).toString();
}

export const api = {
  products: () => request('GET', '/api/products'),
  product: id => request('GET', `/api/products/${id}`),
  listOrders: params => request('GET', `/api/orders?${query(params)}`),
  getOrder: id => request('GET', `/api/orders/${id}`),
  createOrder: (body, idempotencyKey, opts = {}) =>
    request('POST', '/api/orders', { ...opts, body, headers: { 'Idempotency-Key': idempotencyKey } }),
  updateStatus: (id, status, expectedVersion, opts = {}) =>
    request('PATCH', `/api/orders/${id}/status`, { ...opts, body: { status, expectedVersion } }),
  cancel: (id, expectedVersion, opts = {}) =>
    request('POST', `/api/orders/${id}/cancel`, { ...opts, body: { expectedVersion } }),
  resetDemo: () => request('POST', '/api/demo/reset'),
  setStock: (id, stockQuantity) => request('PUT', `/api/demo/products/${id}/stock`, { body: { stockQuantity } })
};
