// Thin client for the Order Management REST API. Every call sends the session's bearer token and a
// fresh X-Correlation-ID, so a request seen in the UI can be found in the server logs.

const SESSION_KEY = 'orderManagement.session';
let onUnauthorized = () => {};
let memory = null;

export const session = {
  get() {
    try {
      return JSON.parse(sessionStorage.getItem(SESSION_KEY)) ?? null;
    } catch {
      return null;
    }
  },
  set(value) {
    try {
      sessionStorage.setItem(SESSION_KEY, JSON.stringify(value));
    } catch {
      // Storage unavailable (private mode): the user will need to sign in again after a reload.
    }
    memory = value;
  },
  clear() {
    try {
      sessionStorage.removeItem(SESSION_KEY);
    } catch {
      // ignore
    }
    memory = null;
  },
  get token() {
    return (memory ?? this.get())?.accessToken;
  },
  get user() {
    return (memory ?? this.get())?.user;
  }
};

/** Called when the API rejects the token (expired / revoked) so the app can return to the login screen. */
export function setUnauthorizedHandler(handler) {
  onUnauthorized = handler;
}

export function newId() {
  if (globalThis.crypto?.randomUUID) return crypto.randomUUID();
  // randomUUID needs a secure context (https / localhost); getRandomValues works everywhere.
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = [...bytes].map(b => b.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

/**
 * Sends a request and always resolves (never throws) with
 * { ok, status, data, headers, correlationId, ms } so callers can inspect failures too.
 */
export async function request(method, path, { body, headers = {}, anonymous = false } = {}) {
  const correlationId = `ui-${newId().replaceAll('-', '').slice(0, 12)}`;
  const init = {
    method,
    headers: { Accept: 'application/json', 'X-Correlation-ID': correlationId, ...headers }
  };
  if (!anonymous && session.token) init.headers.Authorization = `Bearer ${session.token}`;
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

  if (response.status === 401 && !anonymous) onUnauthorized();

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
  login: (username, password) => request('POST', '/api/auth/login', { body: { username, password }, anonymous: true }),
  me: () => request('GET', '/api/auth/me'),

  products: () => request('GET', '/api/products'),
  product: id => request('GET', `/api/products/${id}`),
  createProduct: body => request('POST', '/api/products', { body }),
  updateProduct: (id, body) => request('PUT', `/api/products/${id}`, { body }),
  adjustStock: (id, quantity, reason) => request('POST', `/api/products/${id}/stock-adjustments`, { body: { quantity, reason } }),
  deleteProduct: id => request('DELETE', `/api/products/${id}`),

  listOrders: params => request('GET', `/api/orders?${query(params)}`),
  getOrder: id => request('GET', `/api/orders/${id}`),
  createOrder: (body, idempotencyKey) =>
    request('POST', '/api/orders', { body, headers: { 'Idempotency-Key': idempotencyKey } }),
  updateStatus: (id, status, expectedVersion) =>
    request('PATCH', `/api/orders/${id}/status`, { body: { status, expectedVersion } }),
  cancel: (id, expectedVersion) =>
    request('POST', `/api/orders/${id}/cancel`, { body: { expectedVersion } }),

  resetDemo: () => request('POST', '/api/demo/reset'),
  setStock: (id, stockQuantity) => request('PUT', `/api/demo/products/${id}/stock`, { body: { stockQuantity } })
};
