import { api, DEFAULT_USER, getUser, newId, setUser } from './api.js';

// ---------------------------------------------------------------- helpers

const $ = (selector, root = document) => root.querySelector(selector);
const $$ = (selector, root = document) => [...root.querySelectorAll(selector)];

const ESCAPES = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };
const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ESCAPES[c]);

const rupiah = new Intl.NumberFormat('id-ID', { style: 'currency', currency: 'IDR', maximumFractionDigits: 0 });
const dateTime = new Intl.DateTimeFormat('id-ID', { dateStyle: 'medium', timeStyle: 'short' });
const fmtDate = value => (value ? dateTime.format(new Date(value)) : '—');
const shortId = id => `…${String(id).slice(-8)}`;
const clamp = (value, min, max) => Math.min(max, Math.max(min, Number(value) || min));

const PRODUCT_X = 'a1000000-0000-4000-8000-000000000001';
const INDOMIE = 'a1000000-0000-4000-8000-000000000002';
const AQUA = 'a1000000-0000-4000-8000-000000000003';

const STATUS_STYLE = {
  Pending: 'secondary', Confirmed: 'info', Shipped: 'primary', Delivered: 'success', Cancelled: 'danger'
};
const ACTIONS = {
  Confirmed: { label: 'Confirm', icon: 'check2-circle', css: 'outline-info' },
  Shipped: { label: 'Ship', icon: 'truck', css: 'outline-primary' },
  Delivered: { label: 'Deliver', icon: 'house-check', css: 'outline-success' },
  Cancelled: { label: 'Cancel', icon: 'x-circle', css: 'outline-danger' }
};

const statusBadge = status => `<span class="badge text-bg-${STATUS_STYLE[status] ?? 'secondary'}">${esc(status)}</span>`;
const httpBadge = status => {
  const tone = status >= 200 && status < 300 ? 'success' : status === 409 ? 'warning' : 'danger';
  return `<span class="badge text-bg-${tone}">${status || 'ERR'}</span>`;
};

function toast(html, variant = 'success') {
  const el = document.createElement('div');
  el.className = `toast align-items-center text-bg-${variant} border-0`;
  el.role = 'status';
  el.innerHTML = `<div class="d-flex"><div class="toast-body">${html}</div>
    <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Tutup"></button></div>`;
  $('#toasts').append(el);
  el.addEventListener('hidden.bs.toast', () => el.remove());
  new bootstrap.Toast(el, { delay: 6000 }).show();
}

function problemToast(res) {
  const p = res.data ?? {};
  toast(`<strong>${esc(p.title ?? 'Error')}</strong>
    <span class="opacity-75">(${res.status || 'network'}${p.errorCode ? ` · ${esc(p.errorCode)}` : ''})</span>
    <div>${esc(p.detail ?? '')}</div>
    <div class="small opacity-75 font-mono">correlationId: ${esc(res.correlationId)}</div>`,
    res.status === 409 ? 'warning' : 'danger');
}

function setBusy(button, busy) {
  if (busy) {
    button.dataset.label = button.innerHTML;
    button.disabled = true;
    button.innerHTML = '<span class="spinner-border spinner-border-sm me-1" aria-hidden="true"></span>Memproses…';
  } else {
    button.disabled = false;
    button.innerHTML = button.dataset.label ?? button.innerHTML;
  }
}

// ---------------------------------------------------------------- state

const state = {
  products: [],
  orderKey: newId(),
  query: { page: 1, pageSize: 10 },
  detailOrderId: null
};

// ---------------------------------------------------------------- user

const userInput = $('#user-name');
userInput.value = getUser();
userInput.addEventListener('change', () => {
  const name = userInput.value.trim() || DEFAULT_USER;
  userInput.value = name;
  setUser(name);
  toast(`Request berikutnya dikirim sebagai <strong>${esc(name)}</strong>`, 'secondary');
});

// ---------------------------------------------------------------- products

async function loadProducts() {
  const res = await api.products();
  if (!res.ok) return problemToast(res);
  state.products = res.data;
  renderProducts();
  refreshProductOptions();
}

function renderProducts() {
  const max = Math.max(1, ...state.products.map(p => p.stockQuantity));
  $('#p-rows').innerHTML = state.products.map(p => {
    const tone = p.stockQuantity === 0 ? 'danger' : p.stockQuantity < 20 ? 'warning' : 'success';
    return `<tr>
      <td><div class="fw-medium">${esc(p.name)}</div><div class="order-id text-body-secondary">${esc(p.id)}</div></td>
      <td class="text-end">${rupiah.format(p.price)}</td>
      <td class="text-end">
        <div class="fw-semibold">${p.stockQuantity}</div>
        <div class="progress stock-bar ms-auto" role="progressbar" aria-label="Stock ${esc(p.name)}">
          <div class="progress-bar bg-${tone}" style="width:${(p.stockQuantity / max) * 100}%"></div>
        </div>
      </td>
      <td>${p.isActive ? '<span class="badge text-bg-success">Aktif</span>' : '<span class="badge text-bg-secondary">Nonaktif</span>'}
          ${p.stockQuantity === 0 ? '<span class="badge text-bg-danger">Habis</span>' : p.stockQuantity < 20 ? '<span class="badge text-bg-warning">Menipis</span>' : ''}</td>
    </tr>`;
  }).join('');
}

$('#p-refresh').addEventListener('click', loadProducts);
setInterval(() => {
  const productsVisible = $('#tab-products').classList.contains('active');
  if ($('#p-auto').checked && productsVisible && document.visibilityState === 'visible') loadProducts();
}, 3000);

// ---------------------------------------------------------------- create order form

function productOptions(selected) {
  return state.products.map(p => `<option value="${p.id}" ${p.id === selected ? 'selected' : ''} ${p.isActive ? '' : 'disabled'}>
    ${esc(p.name)} · ${p.stockQuantity}</option>`).join('');
}

function refreshProductOptions() {
  for (const select of $$('.item-product')) {
    select.innerHTML = productOptions(select.value || select.dataset.initial);
  }
  updateTotal();
}

function addItemRow(productId = INDOMIE, quantity = 1) {
  const row = $('#item-row').content.firstElementChild.cloneNode(true);
  const select = $('.item-product', row);
  select.dataset.initial = productId;
  select.innerHTML = productOptions(productId);
  $('.item-qty', row).value = quantity;
  row.addEventListener('input', updateTotal);
  $('.item-remove', row).addEventListener('click', () => {
    if ($$('.item-row').length > 1) row.remove();
    updateTotal();
  });
  $('#f-items').append(row);
  updateTotal();
}

function readItems() {
  return $$('.item-row').map(row => ({
    productId: $('.item-product', row).value,
    quantity: Number($('.item-qty', row).value)
  }));
}

function updateTotal() {
  const total = readItems().reduce((sum, item) => {
    const product = state.products.find(p => p.id === item.productId);
    return sum + (product ? product.price * (item.quantity || 0) : 0);
  }, 0);
  $('#f-total').textContent = rupiah.format(total);
}

function renderKey() {
  $('#f-key').textContent = state.orderKey;
}

function clearFieldErrors() {
  $$('#order-form .is-invalid').forEach(el => el.classList.remove('is-invalid'));
  $$('#order-form [data-field]').forEach(el => { el.textContent = ''; });
}

function showFieldErrors(errors) {
  const itemMessages = [];
  for (const [key, messages] of Object.entries(errors)) {
    if (key.startsWith('items')) {
      const match = key.match(/^items\[(\d+)\]\.(\w+)/);
      itemMessages.push(match ? `Item ${Number(match[1]) + 1}: ${messages.join(' ')}` : messages.join(' '));
      if (match) $$('.item-row')[Number(match[1])]?.querySelector(match[2] === 'quantity' ? '.item-qty' : '.item-product')?.classList.add('is-invalid');
      continue;
    }
    const feedback = $(`#order-form [data-field="${key}"]`);
    if (feedback) {
      feedback.textContent = messages.join(' ');
      feedback.previousElementSibling?.classList.add('is-invalid');
    }
  }
  $('#order-form [data-field="items"]').textContent = itemMessages.join(' · ');
}

$('#f-add-item').addEventListener('click', () => addItemRow(state.products.find(p => p.isActive)?.id));
$('#f-new-key').addEventListener('click', () => { state.orderKey = newId(); renderKey(); });

// The submit button is intentionally NOT disabled: double clicks send the same Idempotency-Key and the
// server returns the same order (replay), which is exactly the behaviour this prototype demonstrates.
$('#order-form').addEventListener('submit', async event => {
  event.preventDefault();
  clearFieldErrors();
  const key = state.orderKey;
  const res = await api.createOrder({
    customerId: $('#f-customer').value.trim(),
    shippingAddress: $('#f-address').value.trim(),
    items: readItems()
  }, key);

  if (res.ok) {
    const replayed = res.headers.get('Idempotent-Replayed') === 'true';
    toast(replayed
      ? `<i class="bi bi-arrow-repeat me-1"></i>Request duplikat terdeteksi — order <strong>${shortId(res.data.id)}</strong> dikembalikan tanpa membuat order baru.`
      : `<i class="bi bi-check-circle me-1"></i>Order <strong>${shortId(res.data.id)}</strong> dibuat · ${rupiah.format(res.data.totalAmount)}`,
      replayed ? 'info' : 'success');
    if (state.orderKey === key) {
      state.orderKey = newId();
      renderKey();
    }
    await Promise.all([loadOrders(), loadProducts()]);
    return;
  }

  if (res.status === 400 && res.data?.errors) showFieldErrors(res.data.errors);
  problemToast(res);
  if (res.data?.errorCode === 'IDEMPOTENCY_KEY_REUSED') {
    state.orderKey = newId();
    renderKey();
  }
});

// ---------------------------------------------------------------- orders list

function readFilter() {
  const from = $('#o-from').value;
  const to = $('#o-to').value;
  let toExclusive;
  if (to) {
    const end = new Date(`${to}T00:00:00`);
    end.setDate(end.getDate() + 1);
    toExclusive = end.toISOString();
  }
  return {
    status: $('#o-status').value,
    customerId: $('#o-customer').value.trim(),
    from: from ? new Date(`${from}T00:00:00`).toISOString() : undefined,
    to: toExclusive
  };
}

async function loadOrders() {
  const res = await api.listOrders({ ...readFilter(), ...state.query });
  if (!res.ok) return problemToast(res);
  const { items, page, pageSize, totalCount, totalPages } = res.data;

  $('#o-rows').innerHTML = items.length === 0
    ? '<tr><td colspan="6" class="text-center text-body-secondary py-5"><i class="bi bi-inbox fs-3 d-block mb-1"></i>Belum ada order</td></tr>'
    : items.map(o => `<tr>
        <td><a href="#" class="order-id" data-detail="${o.id}" title="${o.id}">${shortId(o.id)}</a>
            <div class="small text-body-secondary">${o.itemCount} item · v${o.version}</div></td>
        <td>${esc(o.customerId)}</td>
        <td>${statusBadge(o.status)}</td>
        <td class="text-end">${rupiah.format(o.totalAmount)}</td>
        <td class="d-none d-md-table-cell small">${fmtDate(o.createdAt)}</td>
        <td class="text-end actions">${actionButtons(o, 'btn-sm')}
          <button class="btn btn-sm btn-outline-secondary" data-detail="${o.id}" title="Detail"><i class="bi bi-eye"></i></button></td>
      </tr>`).join('');

  const first = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
  $('#o-summary').textContent = `${first}–${Math.min(page * pageSize, totalCount)} dari ${totalCount} order`;
  renderPager(page, totalPages);
}

function actionButtons(order, size) {
  return order.allowedNextStatuses.map(next => {
    const action = ACTIONS[next];
    return `<button class="btn ${size} btn-${action.css}" data-action="${next}" data-id="${order.id}" data-version="${order.version}" title="${action.label}">
      <i class="bi bi-${action.icon}"></i><span class="d-none d-xl-inline ms-1">${action.label}</span></button>`;
  }).join(' ');
}

function renderPager(page, totalPages) {
  if (totalPages <= 1) {
    $('#o-pager').innerHTML = '';
    return;
  }
  const start = Math.max(1, Math.min(page - 2, totalPages - 4));
  const pages = Array.from({ length: Math.min(5, totalPages) }, (_, i) => start + i);
  const item = (target, label, disabled = false, active = false) =>
    `<li class="page-item ${disabled ? 'disabled' : ''} ${active ? 'active' : ''}">
      <button class="page-link" data-page="${target}" ${disabled ? 'disabled' : ''}>${label}</button></li>`;
  $('#o-pager').innerHTML = item(page - 1, '&laquo;', page === 1)
    + pages.map(p => item(p, p, false, p === page)).join('')
    + item(page + 1, '&raquo;', page === totalPages);
}

$('#o-pager').addEventListener('click', event => {
  const button = event.target.closest('[data-page]');
  if (!button) return;
  state.query.page = Number(button.dataset.page);
  loadOrders();
});
$('#o-page-size').addEventListener('change', event => {
  state.query = { page: 1, pageSize: Number(event.target.value) };
  loadOrders();
});
$('#o-filter').addEventListener('submit', event => {
  event.preventDefault();
  state.query.page = 1;
  loadOrders();
});
$('#o-filter').addEventListener('reset', () => setTimeout(() => { state.query.page = 1; loadOrders(); }));
$('#o-refresh').addEventListener('click', loadOrders);

// ---------------------------------------------------------------- status changes & detail

async function changeStatus(button) {
  const { id, action: status, version } = button.dataset;
  if (status === 'Cancelled' && !confirm('Cancel order ini? Stock akan dikembalikan.')) return;

  setBusy(button, true);
  const res = status === 'Cancelled'
    ? await api.cancel(id, Number(version))
    : await api.updateStatus(id, status, Number(version));
  setBusy(button, false);

  if (res.ok) {
    toast(`Order <strong>${shortId(id)}</strong> → ${statusBadge(res.data.status)}`);
  } else {
    problemToast(res);
  }
  const refreshes = [loadOrders(), loadProducts()];
  if (state.detailOrderId === id) refreshes.push(showDetail(id));
  await Promise.all(refreshes);
}

const detailModal = new bootstrap.Modal('#detail-modal');
$('#detail-modal').addEventListener('hidden.bs.modal', () => { state.detailOrderId = null; });

async function showDetail(id) {
  const res = await api.getOrder(id);
  if (!res.ok) return problemToast(res);
  const o = res.data;
  state.detailOrderId = id;

  $('#detail-title').innerHTML = `Order <span class="order-id">${esc(o.id)}</span>`;
  $('#detail-body').innerHTML = `
    <div class="row g-3 mb-3">
      <div class="col-sm-6"><div class="small text-body-secondary">Customer</div><div class="fw-medium">${esc(o.customerId)}</div></div>
      <div class="col-sm-6"><div class="small text-body-secondary">Status</div>${statusBadge(o.status)} <span class="small text-body-secondary">version ${o.version}</span></div>
      <div class="col-12"><div class="small text-body-secondary">Alamat Pengiriman</div><div>${esc(o.shippingAddress)}</div></div>
      <div class="col-sm-6"><div class="small text-body-secondary">Dibuat</div><div>${fmtDate(o.createdAt)} · <i class="bi bi-person"></i> ${esc(o.createdBy)}</div></div>
      <div class="col-sm-6"><div class="small text-body-secondary">Diupdate</div><div>${o.updatedAt ? `${fmtDate(o.updatedAt)} · <i class="bi bi-person"></i> ${esc(o.updatedBy)}` : '—'}</div></div>
    </div>
    <div class="table-responsive">
      <table class="table table-sm align-middle mb-0">
        <thead><tr><th>Produk</th><th class="text-end">Harga</th><th class="text-end">Qty</th><th class="text-end">Subtotal</th></tr></thead>
        <tbody>${o.items.map(i => `<tr><td>${esc(i.productName)}</td><td class="text-end">${rupiah.format(i.unitPrice)}</td>
          <td class="text-end">${i.quantity}</td><td class="text-end">${rupiah.format(i.lineTotal)}</td></tr>`).join('')}</tbody>
        <tfoot><tr><th colspan="3" class="text-end">Total</th><th class="text-end">${rupiah.format(o.totalAmount)}</th></tr></tfoot>
      </table>
    </div>`;
  $('#detail-actions').innerHTML = (o.allowedNextStatuses.length
    ? actionButtons(o, '')
    : '<span class="text-body-secondary small me-auto"><i class="bi bi-lock me-1"></i>Status terminal — tidak bisa diubah lagi.</span>')
    + ' <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Tutup</button>';
  detailModal.show();
}

document.addEventListener('click', event => {
  const detail = event.target.closest('[data-detail]');
  if (detail) {
    event.preventDefault();
    showDetail(detail.dataset.detail);
    return;
  }
  const action = event.target.closest('[data-action]');
  if (action) changeStatus(action);
});

// ---------------------------------------------------------------- concurrency simulation

const sim = {
  start(title) {
    $('#sim-result').classList.remove('d-none');
    $('#sim-title').textContent = title;
    $('#sim-verdict').innerHTML = '<span class="spinner-border spinner-border-sm" aria-hidden="true"></span>';
    $('#sim-facts').innerHTML = '';
    $('#sim-rows').innerHTML = '';
    this.count = 0;
  },
  row(label, res, outcome) {
    this.count += 1;
    const p = res.data ?? {};
    const result = outcome ?? (res.ok ? '' : esc(p.errorCode ?? p.title));
    $('#sim-rows').insertAdjacentHTML('beforeend', `<tr>
      <td>${this.count}</td><td class="small">${label}</td><td>${httpBadge(res.status)}</td>
      <td class="small">${result}</td><td class="text-end small">${res.ms}</td>
      <td class="d-none d-md-table-cell order-id">${esc(res.correlationId)}</td></tr>`);
  },
  facts(entries) {
    $('#sim-facts').innerHTML = entries.map(([label, value]) =>
      `<div class="col-6 col-md"><div class="fact"><div class="label">${label}</div><div class="value">${value}</div></div></div>`).join('');
  },
  verdict(passed, message) {
    $('#sim-verdict').innerHTML = passed
      ? `<span class="badge text-bg-success fs-6"><i class="bi bi-check-circle me-1"></i>${esc(message)}</span>`
      : `<span class="badge text-bg-danger fs-6"><i class="bi bi-x-circle me-1"></i>${esc(message)}</span>`;
  }
};

async function stockOf(productId) {
  const res = await api.product(productId);
  return res.ok ? res.data.stockQuantity : NaN;
}

// Releases all requests in the same tick so they overlap on the server.
const concurrently = (count, makeRequest) => Promise.all(Array.from({ length: count }, (_, i) => makeRequest(i)));

async function scenarioA() {
  const buyers = clamp($('#sim-a-count').value, 2, 30);
  const initialStock = 15;
  const perOrder = 10;
  sim.start(`Skenario A — ${buyers} pembeli × ${perOrder} unit, stock ${initialStock}`);

  const setup = await api.setStock(PRODUCT_X, initialStock);
  if (!setup.ok) return demoUnavailable(setup);

  const results = await concurrently(buyers, i => api.createOrder({
    customerId: `SIM-A-${i + 1}`,
    shippingAddress: 'Simulasi Skenario A',
    items: [{ productId: PRODUCT_X, quantity: perOrder }]
  }, newId(), { user: `buyer.${i + 1}` }));

  results.forEach((res, i) => sim.row(`POST /orders · buyer.${i + 1} · ${perOrder} unit`, res,
    res.ok ? `Order ${shortId(res.data.id)}` : undefined));

  const finalStock = await stockOf(PRODUCT_X);
  const succeeded = results.filter(r => r.status === 201).length;
  const rejected = results.filter(r => r.data?.errorCode === 'INSUFFICIENT_STOCK').length;
  const expectedSuccess = Math.floor(initialStock / perOrder);

  sim.facts([['Stock awal', initialStock], ['Berhasil', succeeded], ['Ditolak 409', rejected], ['Stock akhir', finalStock]]);
  sim.verdict(succeeded === expectedSuccess && finalStock === initialStock - succeeded * perOrder && finalStock >= 0,
    succeeded === expectedSuccess ? `Lulus — hanya ${succeeded} order berhasil, stock tidak minus` : 'Gagal — stock terdeduksi melebihi persediaan');
}

async function scenarioB() {
  sim.start('Skenario B — Ship vs Cancel bersamaan');

  const created = await api.createOrder({
    customerId: 'SIM-B', shippingAddress: 'Simulasi Skenario B', items: [{ productId: INDOMIE, quantity: 1 }]
  }, newId());
  sim.row('<span class="text-body-secondary">setup:</span> POST /orders', created, created.ok ? `v${created.data.version} Pending` : undefined);
  if (!created.ok) return sim.verdict(false, 'Setup gagal — coba Reset data demo');

  const confirmed = await api.updateStatus(created.data.id, 'Confirmed', 1);
  sim.row('<span class="text-body-secondary">setup:</span> PATCH → Confirmed (v1)', confirmed, confirmed.ok ? 'v2 Confirmed' : undefined);
  const stockBefore = await stockOf(INDOMIE);

  const [ship, cancel] = await concurrently(2, i => i === 0
    ? api.updateStatus(created.data.id, 'Shipped', 2, { user: 'admin.1' })
    : api.cancel(created.data.id, 2, { user: 'admin.2' }));
  sim.row('<strong>admin.1</strong>: PATCH → Shipped (v2)', ship, ship.ok ? '<strong>Menang</strong>' : undefined);
  sim.row('<strong>admin.2</strong>: POST /cancel (v2)', cancel, cancel.ok ? '<strong>Menang</strong>' : undefined);

  const final = (await api.getOrder(created.data.id)).data;
  const stockAfter = await stockOf(INDOMIE);
  const winners = [ship, cancel].filter(r => r.ok).length;
  const loserIs409 = [ship, cancel].some(r => r.status === 409);
  const stockConsistent = final.status === 'Cancelled' ? stockAfter === stockBefore + 1 : stockAfter === stockBefore;

  sim.facts([['Status akhir', statusBadge(final.status)], ['Version', final.version], ['Diupdate oleh', esc(final.updatedBy)],
    ['Stock Indomie', `${stockBefore} → ${stockAfter}`]]);
  sim.verdict(winners === 1 && loserIs409 && stockConsistent,
    winners === 1 ? `Lulus — ${final.updatedBy} menang, yang lain 409` : `Gagal — ${winners} update berhasil`);
}

async function scenarioC() {
  const count = clamp($('#sim-c-count').value, 2, 30);
  const quantity = 2;
  const key = newId();
  sim.start(`Skenario C — ${count} request dengan Idempotency-Key yang sama`);

  const stockBefore = await stockOf(AQUA);
  const body = { customerId: 'SIM-C', shippingAddress: 'Simulasi Skenario C', items: [{ productId: AQUA, quantity }] };
  const results = await concurrently(count, () => api.createOrder(body, key));

  results.forEach(res => {
    const replayed = res.headers.get('Idempotent-Replayed') === 'true';
    sim.row(`POST /orders · key …${key.slice(-6)}`, res,
      res.ok ? `Order ${shortId(res.data.id)} ${replayed ? '<span class="badge text-bg-info">replay</span>' : '<span class="badge text-bg-success">baru</span>'}` : undefined);
  });

  const stockAfter = await stockOf(AQUA);
  const orderIds = new Set(results.filter(r => r.ok).map(r => r.data.id));
  const originals = results.filter(r => r.ok && r.headers.get('Idempotent-Replayed') !== 'true').length;
  const passed = results.every(r => r.status === 201) && orderIds.size === 1 && originals === 1 && stockBefore - stockAfter === quantity;

  sim.facts([['Request', count], ['Order dibuat', orderIds.size], ['Replay', count - originals], ['Stock Aqua', `${stockBefore} → ${stockAfter}`]]);
  sim.verdict(passed, passed ? 'Lulus — 1 order, stock dipotong sekali' : 'Gagal — lihat detail request');
}

function demoUnavailable(res) {
  sim.row('setup: set stock', res);
  sim.verdict(false, 'Endpoint demo hanya tersedia di environment Development');
}

const scenarios = { a: scenarioA, b: scenarioB, c: scenarioC };
$$('[data-sim]').forEach(button => button.addEventListener('click', async () => {
  $$('[data-sim]').forEach(b => { b.disabled = true; });
  try {
    await scenarios[button.dataset.sim]();
  } catch (error) {
    sim.verdict(false, error.message);
  } finally {
    $$('[data-sim]').forEach(b => { b.disabled = false; });
    await Promise.all([loadOrders(), loadProducts()]);
  }
}));

$('#sim-reset').addEventListener('click', async event => {
  const button = event.currentTarget;
  if (!confirm('Hapus semua order dan kembalikan stock produk ke nilai awal?')) return;
  setBusy(button, true);
  const res = await api.resetDemo();
  setBusy(button, false);
  if (!res.ok) return problemToast(res);
  toast('Data demo di-reset');
  await Promise.all([loadOrders(), loadProducts()]);
});

// ---------------------------------------------------------------- boot

renderKey();
await loadProducts();
addItemRow(INDOMIE, 2);
addItemRow(AQUA, 1);
await loadOrders();

