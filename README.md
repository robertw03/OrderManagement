# Order Management API

Prototype REST API (ASP.NET Core .NET 10 + PostgreSQL) untuk Order Management, dibangun sebagai fondasi
rewrite sistem order klien distribusi. Fokus utamanya adalah empat masalah di sistem lama:

| Masalah di sistem lama | Solusi di prototype ini |
|---|---|
| Double order karena user klik *Submit* berkali-kali | Header `Idempotency-Key` + unique index di database ([§5](#5-idempotency)) |
| Stock minus karena dua order masuk bersamaan | *Atomic conditional UPDATE* + `CHECK` constraint ([§6.1](#61-skenario-a--concurrent-stock-deduction)) |
| Status order tidak konsisten saat banyak admin update | *Optimistic concurrency* (version) dengan *compare-and-swap* ([§6.2](#62-skenario-b--concurrent-status-update)) |
| Sulit tracing error | Serilog *structured logging* + Correlation ID & user di setiap log & response ([§9](#9-logging--tracing)) |

Ditambah login (JWT, role Admin/Sales), manajemen master produk, dan UI demo untuk menjalankan skenario concurrency dari browser.

---

## Daftar Isi
1. [Cara Menjalankan](#1-cara-menjalankan)
2. [Tech Stack & Alasan](#2-tech-stack--alasan)
3. [Struktur Project](#3-struktur-project)
4. [Desain Database](#4-desain-database)
5. [Idempotency](#5-idempotency)
6. [Concurrency Handling](#6-concurrency-handling)
7. [Race Condition Lain yang Ditangani](#7-race-condition-lain-yang-ditangani)
8. [Validasi & Error Handling](#8-validasi--error-handling)
9. [Logging & Tracing](#9-logging--tracing)
10. [Testing](#10-testing)
11. [API Reference](#11-api-reference)
12. [Trade-off & Pengembangan Lanjutan](#12-trade-off--pengembangan-lanjutan)

---

## 1. Cara Menjalankan

### Opsi A — Lokal (.NET SDK 10 + PostgreSQL)

Prasyarat: .NET SDK 10.0.300+, PostgreSQL 14+ di `localhost:5432` (user/password `postgres`/`postgres`).
Ubah `ConnectionStrings:OrderDb` di [`appsettings.json`](src/OrderManagement.Api/appsettings.json) bila berbeda.

```bash
dotnet run --project src/OrderManagement.Api
```

- Database `OrderManagement` dibuat dan di-migrate otomatis saat startup (environment Development), termasuk seed produk dan akun demo.
- **UI demo**: <http://localhost:5155/> — login dengan akun di [§1.2](#12-login--hak-akses)
- Dokumentasi interaktif (Scalar/OpenAPI): <http://localhost:5155/scalar>
- Contoh request siap pakai: [`OrderManagement.http`](OrderManagement.http)
- Health check: <http://localhost:5155/health>

### Opsi B — Docker Compose

```bash
docker compose up --build
```

UI di <http://localhost:8080/>, dokumentasi API di <http://localhost:8080/scalar>, PostgreSQL di `localhost:5433`.

### Menjalankan Test

```bash
dotnet test
```

Integration test butuh PostgreSQL (lihat [§10](#10-testing)). Default memakai `Host=localhost;Port=5432;Username=postgres;Password=postgres`;
override dengan environment variable `ORDERMGMT_TEST_CONNECTION`. Setiap test run membuat database sementara
`OrderManagement_Test_<guid>` lalu menghapusnya kembali.

### 1.1 UI Demo

Halaman sederhana (Bootstrap 5 + JavaScript tanpa build step) di [`wwwroot`](src/OrderManagement.Api/wwwroot), disajikan
oleh API yang sama dan **hanya memanggil REST API publik** — tidak ada logic bisnis di frontend.

| Halaman | Isi |
|---|---|
| **Login** | Username/password → token JWT disimpan di `sessionStorage`; daftar akun demo bisa diklik untuk mengisi form |
| **Orders** | Cari order berdasarkan ID (Get Order), form buat order (multi item, estimasi total, Idempotency-Key otomatis), daftar order dengan filter status/customer/tanggal + pagination, detail order, tombol transisi status sesuai `allowedNextStatuses` dari API **dan** role user |
| **Produk** | Stock per produk (auto refresh). Admin: tambah produk, edit nama/harga/status aktif, atur stock (masuk/keluar), hapus (soft delete) |
| **Simulasi Concurrency** *(Admin)* | Satu klik menjalankan Skenario A, B, dan C memakai **token user yang sedang login**: request dikirim **bersamaan** dari browser, lalu hasilnya diverifikasi dan ditampilkan (status HTTP, error code, waktu, correlation ID tiap request) |

Detail yang bisa didemokan:
- Tombol *Submit* sengaja **tidak** di-disable: klik berkali-kali mengirim `Idempotency-Key` yang sama dan
  muncul notifikasi "request duplikat terdeteksi" — tetap satu order.
- User yang login tercatat di kolom audit (`txtInsertedBy`/`txtUpdatedBy`) dan di setiap baris log.
- Skenario B dari dua tab: login sebagai `admin` di satu tab dan `admin2` di tab lain, ubah order yang sama —
  yang kedua mendapat 409, dan detail order menunjukkan siapa yang menang.
- Setiap error (validasi, 401/403/404/409/422, jaringan, maupun error JavaScript tak terduga) tampil sebagai **toast**
  berisi judul, `errorCode`, detail, daftar field yang salah, dan `correlationId` yang bisa langsung dicari di log server.
- Endpoint pendukung demo (`POST /api/demo/reset`, `PUT /api/demo/products/{id}/stock`) **hanya ada di environment Development** dan hanya untuk Admin.

### 1.2 Login & Hak Akses

Semua endpoint `/api/*` (kecuali `POST /api/auth/login`) membutuhkan header `Authorization: Bearer <token>`.
Token JWT (HMAC-SHA256, berlaku 8 jam) didapat dari `POST /api/auth/login`. Password disimpan sebagai hash
(`PasswordHasher` ASP.NET Core Identity — PBKDF2), dan login gagal selalu memberi pesan yang sama tanpa
membedakan "user tidak ada" dan "password salah".

Akun demo (dibuat otomatis bila tabel user kosong, hanya saat `Database:SeedDemoUsers = true` — Development & test):

| Username | Password | Role |
|---|---|---|
| `admin` | `Admin123!` | Admin |
| `admin2` | `Admin123!` | Admin |
| `sales` | `Sales123!` | Sales |

| Aksi | Admin | Sales |
|---|:-:|:-:|
| Lihat produk & order, buat order | ✓ | ✓ |
| Cancel order | ✓ | ✓ |
| Confirm / Ship / Deliver (`PATCH /status`) | ✓ | — (403) |
| Tambah / edit / atur stock / hapus produk | ✓ | — (403) |
| Endpoint demo & tab Simulasi | ✓ | — |

`Jwt:SigningKey` untuk Development ada di `appsettings.Development.json`; di environment lain wajib diisi lewat
environment variable `Jwt__SigningKey` (minimal 32 byte) — aplikasi menolak start bila kosong.

### Schema SQL

Migration EF Core ada di [`src/OrderManagement.Infrastructure/Migrations`](src/OrderManagement.Infrastructure/Migrations).
Versi SQL murni (hasil `dotnet ef migrations script`) ada di [`db/schema.sql`](db/schema.sql) untuk dibaca / dijalankan manual.

---

## 2. Tech Stack & Alasan

| Komponen | Pilihan | Alasan |
|---|---|---|
| Runtime | .NET 10 (LTS) | Versi LTS terbaru. |
| Database | **PostgreSQL** | Lihat di bawah. |
| ORM | EF Core 10 + Npgsql | Migration & query yang aman; operasi kritis concurrency memakai `ExecuteUpdate` / SQL *set-based* sehingga tetap atomic. |
| Validasi | FluentValidation | Aturan validasi terpusat, pesan error per field. |
| Error | RFC 7807 `ProblemDetails` | Standar format error HTTP, satu bentuk untuk semua endpoint. |
| Logging | Serilog | *Structured logging*, enrichment Correlation ID, sink console & file JSON. |
| Test | xUnit + `WebApplicationFactory` + PostgreSQL asli | Concurrency hanya bisa dibuktikan di database sungguhan. |

**Kenapa PostgreSQL (bukan SQLite / SQL Server)?**

- **Row-level locking & MVCC**: `UPDATE` mengunci baris, dan pada isolation `READ COMMITTED` PostgreSQL
  *mengevaluasi ulang* klausa `WHERE` terhadap versi baris terbaru setelah lock dilepas. Properti inilah yang membuat
  *atomic conditional update* aman (§6).
- **`INSERT … ON CONFLICT DO NOTHING`** pada unique index: transaksi kedua *menunggu* transaksi pertama selesai,
  lalu mendapat konflik — dasar penanganan Skenario C.
- **`CHECK` constraint** sebagai jaring pengaman terakhir (stock ≥ 0).
- **SQLite tidak dipilih** karena write-nya diserialisasi per file database: test concurrency akan "lulus" tanpa
  membuktikan apa pun.
- SQL Server sebenarnya juga valid (desainnya sama: `rowversion`, unique index, `UPDLOCK` bila perlu). PostgreSQL
  dipilih karena open-source, ringan dijalankan di Docker, dan matang untuk beban OLTP.

---

## 3. Struktur Project

```
src/
  OrderManagement.Domain/          Entity, OrderStatus + state machine (tanpa dependency)
  OrderManagement.Infrastructure/  DbContext, konvensi penamaan DB, audit interceptor, seed, migrations
  OrderManagement.Api/
    Common/                        Error handling, correlation ID, validation filter, current user
    Features/Auth/                 Login, JWT, role, seed akun demo
    Features/Orders/               Controller, service, DTO, validator  (vertical slice per fitur)
    Features/Products/             Master produk: CRUD, atur stock atomic, soft delete
    Features/Demo/                 Endpoint reset data demo (Development only)
    wwwroot/                       UI demo (Bootstrap 5 + JS)
tests/
  OrderManagement.UnitTests/       State machine, validator, request hash, standar database
  OrderManagement.IntegrationTests/  API end-to-end + skenario concurrency di PostgreSQL asli
db/schema.sql                      Script schema SQL
OrderManagement.http               Contoh request
```

Layering sengaja dibuat pragmatis untuk prototype: logic aplikasi ada di `OrderService` dan langsung memakai
`DbContext`, karena operasi kritisnya (atomic update, `ON CONFLICT`) adalah perilaku database yang justru tidak
ingin disembunyikan di balik repository generik.

---

## 4. Desain Database

Memakai standar penamaan berikut, diterapkan otomatis untuk seluruh model oleh
[`DatabaseNaming`](src/OrderManagement.Infrastructure/DatabaseNaming.cs) dan dijaga oleh unit test
[`DatabaseStandardTests`](tests/OrderManagement.UnitTests/DatabaseStandardTests.cs).

| Aturan | Contoh |
|---|---|
| Nama database PascalCase | `OrderManagement` |
| Prefix tabel: `m` = master data, `tr` = transaksi | `mUser`, `mProduct`, `trOrder`, `trOrderItem`, `trIdempotencyKey` |
| Primary key UUID: `uid{Tabel}UUID` | `uidOrderUUID` |
| Foreign key: `uid{Referensi}UUID` | `trOrderItem.uidProductUUID` |
| Prefix kolom sesuai tipe | `txt` string, `int` integer, `dec` decimal, `bol` boolean, `dt` tanggal, `uid` uuid |
| Nama constraint | `PK_trOrder`, `FK_trOrderItem_mProduct`, `IX_trOrder_txtStatus_dtInserted`, `UX_…` (unique), `CK_…` (check) |

**Kolom standar di setiap tabel:**

| Kolom | Tipe | Keterangan |
|---|---|---|
| `uid{Tabel}UUID` | `uuid` PK | UUID v7 (berurutan waktu → ramah index B-tree) |
| `bolActive` | `boolean` default `true` | Data aktif / nonaktif (mis. produk tidak dijual lagi) |
| `bolDeleted` | `boolean` default `false` | Soft delete; disaring otomatis oleh EF *global query filter* |
| `dtInserted` | `timestamptz` | Waktu dibuat |
| `txtInsertedBy` | `varchar(100)` | Dibuat oleh |
| `dtUpdated` | `timestamptz` null | Waktu update terakhir |
| `txtUpdatedBy` | `varchar(100)` null | Diupdate oleh |

Kolom audit diisi otomatis oleh [`AuditInterceptor`](src/OrderManagement.Infrastructure/Auditing.cs) untuk perubahan
lewat `SaveChanges`; operasi *set-based* (`ExecuteUpdate`, raw SQL) mengisinya secara eksplisit. Identitas user
diambil dari token JWT user yang login (claim `name`); pekerjaan di luar request (migrasi, seeding) tercatat sebagai `system`.

**Tabel:**

- `mUser` — `txtUsername` (unique di antara yang belum dihapus), `txtDisplayName`, `txtPasswordHash`, `txtRole` (`CHECK` Admin/Sales).
- `mProduct` — `txtName` (unique di antara yang belum dihapus), `intStockQuantity` (`CHECK >= 0`), `decPrice`.
- `trOrder` — `txtCustomerId`, `txtShippingAddress`, `txtStatus` (`CHECK` enum), `decTotalAmount`, `intVersion` (concurrency token).
- `trOrderItem` — `uidOrderUUID`, `uidProductUUID`, `txtProductName` & `decUnitPrice` (*snapshot* saat order dibuat), `intQuantity` (`CHECK > 0`), `decLineTotal`. Unique (`uidOrderUUID`, `uidProductUUID`).
- `trIdempotencyKey` — `txtCustomerId`, `txtIdempotencyKey` (unique bersama customer), `txtRequestHash`, `uidOrderUUID`.

Aturan `bolActive` / `bolDeleted` pada order: produk nonaktif → ditolak `422 PRODUCT_INACTIVE`; produk
soft-deleted → dianggap tidak ada (`422 UNKNOWN_PRODUCT`). Saat cancel, stock tetap dikembalikan walaupun
produknya sudah dinonaktifkan/dihapus setelah order dibuat.

---

## 5. Idempotency

**Strategi: header `Idempotency-Key` (wajib) di-scope per `customerId`, disimpan di tabel `trIdempotencyKey`
dengan unique index, ditambah hash payload.**

Alur `POST /api/orders`:

1. Normalisasi request (gabungkan `productId` duplikat, urutkan) lalu hitung SHA-256 payload.
2. *Fast path*: bila key sudah ada → langsung *replay*.
3. Mulai transaksi. Langkah **pertama** di dalam transaksi adalah meng-*claim* key:
   ```sql
   INSERT INTO "trIdempotencyKey" (...) VALUES (...)
   ON CONFLICT ("txtCustomerId", "txtIdempotencyKey") DO NOTHING
   ```
4. Kurangi stock, simpan order, commit — semuanya dalam transaksi yang sama.

Respons:

| Kondisi | Respons |
|---|---|
| Key baru | `201 Created` |
| Key sama, payload sama (retry / double click) | `201 Created` dengan **order yang sama**, header `Idempotent-Replayed: true`, stock **tidak** dipotong lagi |
| Key sama, payload berbeda | `422 IDEMPOTENCY_KEY_REUSED` |
| Header tidak dikirim | `400 IDEMPOTENCY_KEY_MISSING` |
| Request gagal (mis. stock kurang) | Transaksi rollback → key **tidak** tersimpan, client boleh retry dengan key yang sama |

**Kenapa Idempotency-Key, bukan hash payload saja?**
Hash payload saja tidak bisa membedakan *retry* dengan *order baru yang kebetulan isinya sama* — customer
distribusi sangat wajar memesan barang yang sama dua kali sehari. Key dari client (UUID yang dibuat sekali saat form
dibuka / tombol diklik dan dipakai ulang saat retry) secara eksplisit menyatakan "ini request yang sama". Ini juga
pola standar industri (Stripe, draft IETF *Idempotency-Key HTTP Header*). Hash payload tetap dipakai sebagai
**pelengkap** untuk mendeteksi penyalahgunaan key.

**Kenapa di-scope per customer?** Supaya key dari customer A tidak pernah bisa bertabrakan / membaca order customer B.

---

## 6. Concurrency Handling

Prinsip umum: **koordinasi dilakukan oleh database, bukan oleh `lock` di C#.** `lock`/`SemaphoreSlim` hanya berlaku
di satu proses; begitu API di-*scale* ke beberapa instance, satu-satunya titik bersama adalah database.
Semua transaksi memakai isolation default `READ COMMITTED`.

### 6.1 Skenario A — Concurrent Stock Deduction

> Dua user order 10 unit Product X, stock tinggal 15.

**Solusi: atomic conditional UPDATE** (cek dan kurangi dalam satu statement):

```sql
UPDATE "mProduct"
SET "intStockQuantity" = "intStockQuantity" - @qty, "dtUpdated" = @now, "txtUpdatedBy" = @user
WHERE "uidProductUUID" = @id AND "bolActive" AND NOT "bolDeleted" AND "intStockQuantity" >= @qty;
-- rows affected = 0  →  stock tidak cukup → rollback seluruh transaksi → 409 INSUFFICIENT_STOCK
```

Yang terjadi: transaksi T1 mengunci baris Product X dan mengubah stock 15 → 5. T2 menunggu lock. Setelah T1 commit,
PostgreSQL mengevaluasi ulang `WHERE` T2 terhadap stock terbaru (5 ≥ 10 → false) → 0 baris → T2 ditolak.

- **Defense in depth**: `CHECK ("intStockQuantity" >= 0)` — kalaupun ada bug di aplikasi, database menolak stock negatif.
- **Order multi-item**: semua item dalam satu transaksi; satu item gagal → semua di-rollback.
- **Pencegahan deadlock**: item diurutkan berdasarkan `productId` sebelum di-update, sehingga semua transaksi
  mengunci baris produk dengan urutan yang sama.

**Kenapa bukan alternatif lain?**

| Alternatif | Kenapa tidak dipilih |
|---|---|
| Read → cek di C# → `SaveChanges` | Klasik *lost update*: dua request membaca 15, sama-sama lolos cek → stock −5. Ini bug sistem lama. |
| Optimistic locking (version) di produk | Benar, tapi produk "hot" akan sering konflik → banyak retry. Atomic UPDATE tidak butuh retry sama sekali. |
| `SELECT … FOR UPDATE` lalu update | Benar juga, tapi dua round-trip dan lock lebih lama; atomic UPDATE mencapai hal yang sama dalam satu statement. |
| `SERIALIZABLE` isolation | Benar, tapi semua transaksi harus siap di-retry (serialization failure) dan throughput turun. |

### 6.2 Skenario B — Concurrent Status Update

> Dua admin bersamaan: satu ubah ke Shipped, satu Cancel.

**Solusi: optimistic concurrency dengan *compare-and-swap* pada (status, version):**

```sql
UPDATE "trOrder"
SET "txtStatus" = @new, "intVersion" = "intVersion" + 1, "dtUpdated" = @now, "txtUpdatedBy" = @user
WHERE "uidOrderUUID" = @id AND "txtStatus" = @statusYangDibaca AND "intVersion" = @versionYangDibaca;
-- 0 rows → 409 CONCURRENT_MODIFICATION
```

- Client boleh mengirim versi yang terakhir dilihatnya (`expectedVersion` di body atau header `If-Match: "3"`;
  `GET` mengembalikan `ETag`). Bila tidak cocok → `409 VERSION_MISMATCH`.
- Tanpa `expectedVersion` pun server tetap aman, karena update hanya berlaku bila status & version di database masih
  sama dengan yang dibaca server.
- Respons 409 menyertakan `currentStatus` dan `currentVersion` supaya UI bisa langsung menampilkan keadaan terbaru.
- Stock hanya dikembalikan oleh transaksi yang **menang** (lihat §7).

**Kenapa optimistic (bukan pessimistic)?** Konflik antar admin jarang terjadi, dan soal meminta "yang lain mendapat
error yang jelas" — persis perilaku optimistic locking. Pessimistic lock membuat admin kedua menunggu lalu
tetap harus divalidasi ulang, tanpa manfaat tambahan.

### 6.3 Skenario C — Idempotent Create Under Race

> Dua `POST /orders` dengan key sama tiba bersamaan, sebelum salah satu commit.

Pola "SELECT dulu, kalau belum ada baru INSERT" **tidak aman** — keduanya melihat "belum ada". Solusinya:
unique index `UX_trIdempotencyKey_txtCustomerId_txtIdempotencyKey` dan `INSERT … ON CONFLICT DO NOTHING` sebagai
**langkah pertama** transaksi:

1. T1 meng-*insert* key (belum commit).
2. T2 meng-*insert* key yang sama → PostgreSQL **memblokir T2** pada unique index sampai T1 selesai.
3. T1 commit → T2 mendapat konflik (0 rows) → T2 rollback (belum menyentuh stock) → membaca record milik T1 → *replay* order T1.
4. Bila T1 justru rollback (mis. stock kurang) → insert T2 berhasil dan T2 diproses normal.

Karena key di-*claim* **sebelum** stock dikurangi, request duplikat tidak pernah ikut mengunci/mengurangi stock.

---

## 7. Race Condition Lain yang Ditangani

| # | Race condition | Dampak bila tidak ditangani | Pencegahan | Test |
|---|---|---|---|---|
| 1 | **Double cancel bersamaan** (dua admin / double click tombol cancel) | Stock dikembalikan dua kali → stock "bertambah" palsu | Stock hanya di-restore bila *compare-and-swap* status berhasil (1 row affected), dalam transaksi yang sama | `Concurrent_cancels_restore_stock_only_once` |
| 2 | **Deadlock order multi-produk** (order A: P1,P2 — order B: P2,P1) | Transaksi saling tunggu → salah satu di-abort database → error 500 | Item digabung & diurutkan per `productId` sebelum update → urutan lock selalu sama | `Multi_item_orders_locking_products_in_opposite_order_do_not_deadlock` |
| 3 | **Harga berubah saat order diproses** | Total order memakai harga yang berbeda dengan harga saat stock dipotong | Harga & nama dibaca setelah baris produk terkunci oleh UPDATE di transaksi yang sama, lalu di-*snapshot* ke `trOrderItem` | — |
| 4 | **ProductId duplikat dalam satu request** (`[{P1,10},{P1,10}]`, stock 15) | Dua UPDATE terpisah masing-masing lolos cek | Quantity per produk digabung sebelum diproses (`20 > 15` → ditolak) | `Create_merges_duplicate_lines…` |
| 5 | **Key sama, payload berbeda, bersamaan** | Dua order berbeda untuk satu key | Unique index + hash payload: satu menang, lainnya `422 IDEMPOTENCY_KEY_REUSED` | `ScenarioC_same_key_with_different_payloads…` |
| 6 | **Cancel lewat `PATCH /status` vs `POST /cancel`** | Satu jalur lupa mengembalikan stock | Satu-satunya jalur perubahan status (`ChangeStatusAsync`) dipakai kedua endpoint | `Cancelling_via_status_endpoint_also_restores_stock` |
| 7 | **Produk dinonaktifkan/dihapus setelah order dibuat, lalu order di-cancel** | Stock tidak kembali karena produk "tidak terlihat" | Restore stock memakai `IgnoreQueryFilters()` | `Cancel_returns_stock_even_if_product_was_deactivated_meanwhile` |
| 8 | **Admin mengubah stock bersamaan dengan order masuk** (admin membaca stock 50, mengetik 60; sementara itu 5 order mengurangi stock) | *Lost update*: form admin menimpa stock dengan angka absolut yang sudah basi → pengurangan dari order hilang, stock "bertambah" sendiri | Stock produk yang sudah ada **tidak bisa** di-set absolut. Perubahan lewat `POST /products/{id}/stock-adjustments` berisi **delta** (+/−) yang dieksekusi atomic `SET stock = stock + @delta WHERE stock + @delta >= 0`. Edit produk (`PUT`) hanya menulis kolom nama/harga/aktif | `Concurrent_restock_and_orders_never_lose_an_update`, `Update_changes_name_and_price_without_touching_stock`, `Concurrent_stock_removals_never_go_negative` |
| 9 | **Dua admin menambah produk dengan nama sama bersamaan** | Duplikat master data (cek "nama sudah ada?" lalu insert sama-sama lolos) | Partial unique index `UX_mProduct_txtName WHERE bolDeleted = false`; pelanggaran → `409 PRODUCT_NAME_TAKEN` | `Concurrent_creates_with_the_same_name_produce_exactly_one_product` |

---

## 8. Validasi & Error Handling

Semua error — validasi, business rule, konflik concurrency, route tidak ditemukan, maupun exception tak terduga —
memakai **satu format** [RFC 7807 ProblemDetails](https://www.rfc-editor.org/rfc/rfc7807) (`application/problem+json`):

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Insufficient stock",
  "status": 409,
  "detail": "Product a1000000-… (Product X) has 5 unit(s) available but 10 were requested.",
  "instance": "/api/orders",
  "errorCode": "INSUFFICIENT_STOCK",
  "correlationId": "c0a8017f9b2e4c1d",
  "productId": "a1000000-0000-4000-8000-000000000001",
  "requested": 10,
  "available": 5
}
```

Error validasi menambahkan `errors` per field: `{"errors": {"items[0].quantity": ["'Quantity' must be between 1 and 10000."]}}`.

| HTTP | `errorCode` | Kapan |
|---|---|---|
| 400 | `VALIDATION_FAILED` | Body/query tidak valid (field kosong, quantity ≤ 0, JSON rusak, pageSize > 100, `to` ≤ `from`) |
| 400 | `IDEMPOTENCY_KEY_MISSING` / `IDEMPOTENCY_KEY_INVALID` | Header `Idempotency-Key` tidak ada / > 100 karakter |
| 401 | `UNAUTHORIZED` | Token tidak ada / kedaluwarsa / tidak valid |
| 401 | `INVALID_CREDENTIALS` | Login gagal (username atau password salah, atau akun nonaktif) |
| 403 | `FORBIDDEN` | Role tidak berhak (mis. Sales mengubah status / produk) |
| 404 | `ORDER_NOT_FOUND`, `PRODUCT_NOT_FOUND`, `NOT_FOUND` | Resource / route tidak ada |
| 409 | `INSUFFICIENT_STOCK` | Stock tidak cukup (order, atau pengurangan stock oleh admin) |
| 409 | `PRODUCT_NAME_TAKEN` | Nama produk sudah dipakai produk lain yang belum dihapus |
| 409 | `CONCURRENT_MODIFICATION` | Kalah balapan update status (Skenario B) |
| 409 | `VERSION_MISMATCH` | `expectedVersion` / `If-Match` sudah basi |
| 422 | `INVALID_STATUS_TRANSITION` | Transisi tidak diizinkan state machine (menyertakan `allowedStatuses`) |
| 422 | `ORDER_NOT_CANCELLABLE` | Cancel order yang sudah Shipped/Delivered/Cancelled |
| 422 | `IDEMPOTENCY_KEY_REUSED` | Key dipakai ulang dengan payload berbeda |
| 422 | `UNKNOWN_PRODUCT` / `PRODUCT_INACTIVE` | Produk tidak ada / tidak aktif |
| 500 | `INTERNAL_ERROR` | Error tak terduga — detail teknis **tidak** dibocorkan; cari log dengan `correlationId` |

**Kenapa stock kurang = 409, bukan 422?** Request-nya valid secara bisnis; ia gagal karena *konflik dengan state
resource saat ini* (stock bisa saja cukup beberapa detik kemudian). 422 dipakai untuk request yang **tidak akan
pernah** berhasil sebagaimana adanya (transisi ilegal, produk tidak ada).

Implementasi: exception domain (`ConflictException`, `BusinessRuleException`, …) →
[`GlobalExceptionHandler`](src/OrderManagement.Api/Common/Errors/GlobalExceptionHandler.cs). Controller tidak berisi
try/catch. Pelanggaran constraint database (unique/check) dan deadlock juga dipetakan ke 409, bukan 500.

---

## 9. Logging & Tracing

- **Serilog**, *structured* (properti bisa di-query, bukan string). Bukan `Console.WriteLine`.
- **Correlation ID**: middleware membaca header `X-Correlation-ID` (atau membuat baru), mengembalikannya di response
  header, memasukkannya ke `ProblemDetails.correlationId`, dan mendorongnya ke `LogContext` sehingga **setiap baris
  log** dalam request tersebut membawa `CorrelationId`. Client / API gateway dapat meneruskan ID yang sama antar service.
- **User**: username dari token juga didorong ke `LogContext` (`UserName`), jadi setiap log menunjukkan siapa pelakunya.
- **Request log** satu baris per request: method, path, status, durasi, user, client IP, user agent, idempotency key.
- **Log bisnis** di titik penting: order dibuat (by user), replay idempotency, stock tidak cukup (Warning),
  kalah balapan status (Warning, berisi siapa yang menang), perubahan status, restore stock.
- **Sink**: console (format teks yang mudah dibaca, ada `[CorrelationId] [UserName]`) dan file `logs/order-management-YYYYMMDD.json`
  (*compact JSON*, siap di-*ship* ke ELK/Seq/Loki). Level dan sink bisa diubah dari `appsettings.json` tanpa rebuild.

Contoh tracing satu request yang gagal:

```
[10:31:55.426 WRN] [c0a8017f9b2e4c1d] [sales] OrderManagement.Api.Features.Orders.OrderService: Insufficient stock for product a1000000-… requested 10, available 5
[10:31:55.430 WRN] [c0a8017f9b2e4c1d] [sales] OrderManagement.Api.Common.Errors.GlobalExceptionHandler: Request rejected with 409 INSUFFICIENT_STOCK: …
[10:31:55.431 INF] [c0a8017f9b2e4c1d] [sales] Serilog.AspNetCore.RequestLoggingMiddleware: HTTP POST /api/orders responded 409 in 12.3 ms
```

---

## 10. Testing

**95 test** (46 unit + 49 integrasi). Integration test menjalankan API sungguhan (`WebApplicationFactory`) terhadap
**PostgreSQL sungguhan** — concurrency bergantung pada locking database, sehingga provider in-memory/SQLite tidak
akan membuktikan apa pun. Request concurrent dilepas bersamaan dengan *start gate* (`TaskCompletionSource`) agar
benar-benar tumpang tindih di database.

**Concurrency** ([`ConcurrencyTests`](tests/OrderManagement.IntegrationTests/ConcurrencyTests.cs)):

| Test | Membuktikan |
|---|---|
| `ScenarioA_two_orders_for_10_units_with_stock_15_only_one_succeeds` | Skenario A persis seperti soal: 1 sukses, 1 `409`, stock akhir 5 |
| `ScenarioA_under_heavy_contention_stock_is_never_oversold` | 40 pembeli memperebutkan 15 unit → tepat 15 sukses, stock 0, tidak pernah minus |
| `ScenarioB_ship_and_cancel_at_the_same_time_exactly_one_wins` | 10 ronde Ship vs Cancel bersamaan → selalu tepat satu menang, yang kalah `409`, stock konsisten dengan pemenang |
| `ScenarioC_same_idempotency_key_sent_concurrently_creates_exactly_one_order` | 10 request key sama → 1 order, semua respons berisi order yang sama, stock dipotong sekali |
| `ScenarioC_same_key_with_different_payloads…` | Key sama payload beda bersamaan → 1 order, lainnya `422` |
| `Concurrent_cancels_restore_stock_only_once` | 10 cancel bersamaan → stock dikembalikan sekali |
| `Multi_item_orders_locking_products_in_opposite_order_do_not_deadlock` | 30 order dengan urutan produk terbalik → semua sukses tanpa deadlock |
| `Concurrent_restock_and_orders_never_lose_an_update` | 20 order (−1) dan 20 restock (+1) bersamaan → stock tetap 100, tidak ada update yang hilang |
| `Concurrent_stock_removals_never_go_negative` | 25 pengurangan stock bersamaan pada stock 10 → tepat 10 berhasil, stock 0 |
| `Concurrent_creates_with_the_same_name_produce_exactly_one_product` | 8 admin membuat produk bernama sama bersamaan → 1 produk, sisanya `409` |

**Fungsional & kontrak error** ([`OrderApiTests`](tests/OrderManagement.IntegrationTests/OrderApiTests.cs),
[`ProductApiTests`](tests/OrderManagement.IntegrationTests/ProductApiTests.cs)): lifecycle status, cancel mengembalikan
stock (lewat kedua endpoint), rollback order multi-item, replay idempotency (termasuk retry setelah gagal & validasi panjang key), filter + pagination, kolom audit,
produk nonaktif/terhapus, CRUD produk, format error 400/404/409/422 dan correlation ID.

**Auth** ([`AuthTests`](tests/OrderManagement.IntegrationTests/AuthTests.cs)): login berhasil/gagal (pesan identik
untuk user tidak ada & password salah), akun nonaktif ditolak, semua endpoint butuh token (`401`), Sales tidak boleh
mengubah status order atau produk (`403`).

**Unit**: seluruh 25 kombinasi transisi state machine, validator, hash payload, dan
[`DatabaseStandardTests`](tests/OrderManagement.UnitTests/DatabaseStandardTests.cs) yang memastikan setiap tabel
memenuhi standar penamaan & kolom wajib.

---

## 11. API Reference

Dokumentasi lengkap & interaktif: `/scalar` (OpenAPI di `/openapi/v1.json`; isi token di menu *Authentication*).
Semua endpoint `/api/*` kecuali login butuh `Authorization: Bearer <token>`. Header opsional: `X-Correlation-ID`.

| Method | Endpoint | Role | Keterangan |
|---|---|---|---|
| `POST` | `/api/auth/login` | — | Body `{ "username", "password" }` → `{ accessToken, expiresAt, user }` |
| `GET` | `/api/auth/me` | semua | User yang sedang login |
| `POST` | `/api/orders` | semua | Buat order. Header **`Idempotency-Key` wajib**. → `201` + `Location` + `ETag` |
| `GET` | `/api/orders/{id}` | semua | Detail order + items. → `200` + `ETag` (= version) |
| `GET` | `/api/orders?status=&customerId=&from=&to=&page=1&pageSize=20` | semua | List + filter + pagination (`from` inklusif, `to` eksklusif, terbaru dulu, `pageSize` maks 100) |
| `PATCH` | `/api/orders/{id}/status` | Admin | Body `{ "status": "Confirmed", "expectedVersion": 1 }` (`expectedVersion` opsional, atau header `If-Match`) |
| `POST` | `/api/orders/{id}/cancel` | semua | Cancel (hanya Pending/Confirmed) + kembalikan stock. Body opsional `{ "expectedVersion": 2 }` |
| `GET` | `/api/products`, `/api/products/{id}` | semua | Lihat produk & stock |
| `POST` | `/api/products` | Admin | Tambah produk `{ "name", "price", "stockQuantity", "isActive" }` → `201` |
| `PUT` | `/api/products/{id}` | Admin | Ubah `{ "name", "price", "isActive" }` (stock **tidak** bisa di-set di sini, lihat §7 #8) |
| `POST` | `/api/products/{id}/stock-adjustments` | Admin | `{ "quantity": 20, "reason": "Barang masuk" }` — delta +/−, atomic, tidak bisa membuat stock minus |
| `DELETE` | `/api/products/{id}` | Admin | Soft delete (`bolDeleted = true`) → `204` |
| `GET` | `/health` | — | Health check (termasuk koneksi database) |
| `POST` | `/api/demo/reset` | Admin | *Development only* — hapus semua order, kembalikan stock produk seed |
| `PUT` | `/api/demo/products/{id}/stock` | Admin | *Development only* — set stock produk (body `{ "stockQuantity": 15 }`) |

Respons order menyertakan `allowedNextStatuses` (transisi yang sah dari status saat ini) sehingga client tidak perlu
menduplikasi state machine.

State machine:

```
Pending ──► Confirmed ──► Shipped ──► Delivered
   │            │
   └────────────┴──► Cancelled          (Delivered & Cancelled = terminal)
```

Contoh create order:

```http
POST /api/orders
Authorization: Bearer eyJhbGciOiJIUzI1NiIs…
Idempotency-Key: 6f1c2a9e-0b7d-4a51-9a33-1f0e8f6f2c11
Content-Type: application/json

{
  "customerId": "CUST-001",
  "shippingAddress": "Jl. Asia Afrika No. 1, Bandung",
  "items": [
    { "productId": "a1000000-0000-4000-8000-000000000002", "quantity": 2 },
    { "productId": "a1000000-0000-4000-8000-000000000003", "quantity": 1 }
  ]
}
```

Produk seed: `Product X` (`…0001`, stock **15** — untuk demo Skenario A), Indomie (`…0002`), Aqua (`…0003`),
Beras (`…0004`), Minyak Goreng (`…0005`). UUID lengkap ada di [`SeedData`](src/OrderManagement.Infrastructure/SeedData.cs).

---

## 12. Trade-off & Pengembangan Lanjutan

Hal yang sengaja **tidak** dikerjakan di prototype, dan rencana untuk production:

- **Identity provider**: prototype menerbitkan JWT sendiri dari tabel `mUser`. Production: identity provider
  terpusat (Keycloak / Entra ID / dsb.) dengan refresh token, revocation, dan manajemen user; serta pembatasan data
  per customer/wilayah sales.
- **Rate limiting login** (mis. 5 percobaan/menit per IP & username) untuk mencegah *brute force*.
- **Retensi idempotency key**: key disimpan permanen. Production: background job menghapus/soft-delete key > 24 jam
  (index `IX_trIdempotencyKey_dtInserted` sudah disiapkan).
- **Replay menampilkan state terkini**: replay mengembalikan order dalam kondisi saat ini (bukan snapshot respons
  pertama). Cukup untuk mencegah double order; bila butuh respons identik byte-per-byte, simpan body respons di tabel key.
- **Event / integrasi**: perubahan status belum mem-publish event. Production: *transactional outbox* agar event
  (mis. ke gudang/notifikasi) konsisten dengan commit database.
- **Inventory sebagai service terpisah**: bila stock dipisah ke service lain, transaksi lokal diganti pola reservasi
  (reserve → confirm/release) atau saga.
- **Observability**: tambah OpenTelemetry (trace terdistribusi; `traceparent` dapat dipakai sebagai correlation ID)
  dan metrics (jumlah 409 per menit sebagai indikator contention).
- **Rate limiting** per customer pada `POST /orders`.
- **Pagination**: offset-based cukup untuk prototype; untuk data besar ganti ke *keyset pagination* (`dtInserted`, `uid`).
