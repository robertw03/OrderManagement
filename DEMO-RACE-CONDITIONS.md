# Branch demo: `demo/race-conditions-unfixed`

Branch ini **sengaja rusak** — jangan di-merge. Isinya dua race condition **di luar Skenario A/B/C** (README §7)
dengan perbaikannya dibuang, supaya terlihat apa yang terjadi tanpa pencegahan dan bahwa test benar-benar
menangkapnya. Di `main` semuanya lulus.

## Race condition #1 — Double cancel mengembalikan stock berkali-kali

**Kode di branch ini** (`OrderService.ChangeStatusAsync`): stock dikembalikan begitu order *terlihat* bisa di-cancel,
di-commit sendiri, **sebelum** request ini memenangkan perubahan status.

```
waktu   Cancel #1                           Cancel #2 (double click / admin lain)
  t1    baca order: Pending ✓               baca order: Pending ✓
  t2    stock += 10  (commit)               stock += 10  (commit)        ← dua-duanya sudah terjadi
  t3    UPDATE status → Cancelled ✓
  t4                                        UPDATE status … → 0 baris → 409
        hasil: order di-cancel sekali, tapi stock bertambah 20
```

**Perbaikan di `main`**: pengembalian stock dilakukan di transaksi yang sama, **hanya setelah** *compare-and-swap*
status berhasil (1 baris ter-update). Yang kalah tidak menyentuh stock sama sekali.

## Race condition #8 — Admin mengatur stock bersamaan dengan order masuk (lost update)

**Kode di branch ini** (`ProductService.AdjustStockAsync`): baca stock → hitung angka baru di C# → tulis angka absolut.

```
waktu   Admin: stock masuk +10              Order: −5
  t1    baca stock → 100
  t2                                        UPDATE stock = stock − 5 → 95 (commit)
  t3    tulis stock = 100 + 10 = 110        ← pengurangan dari order hilang (seharusnya 105)
```

Cek "stock tidak boleh minus" juga memakai data basi: 25 pengurangan bersamaan dari stock 10 **semuanya** lolos.

**Perbaikan di `main`**: satu statement atomic
`UPDATE … SET stock = stock + @delta WHERE stock + @delta >= 0`, dan `PUT /products/{id}` tidak bisa mengubah stock.

## Cara demo

```bash
git switch demo/race-conditions-unfixed
dotnet test --filter "FullyQualifiedName~Concurrent_cancels|FullyQualifiedName~Concurrent_stock_removals|FullyQualifiedName~Concurrent_restock"
# → 3 test GAGAL

git switch main
dotnet test --filter "FullyQualifiedName~Concurrent_cancels|FullyQualifiedName~Concurrent_stock_removals|FullyQualifiedName~Concurrent_restock"
# → 3 test LULUS
```

| Test | Di `main` | Di branch ini (5 dari 5 run) |
|---|---|---|
| `Concurrent_cancels_restore_stock_only_once` | stock 100 | **190** (10 cancel bersamaan, semua mengembalikan stock) |
| `Concurrent_stock_removals_never_go_negative` | 10 sukses, stock 0 | **25 sukses** dari stock 10 |
| `Concurrent_restock_and_orders_never_lose_an_update` | stock 100 | **≠ 100** (mis. 101 / 96 — update hilang) |

Efek samping yang juga terlihat: `ScenarioB_ship_and_cancel_…` ikut gagal di branch ini, karena Cancel yang **kalah**
dari Ship tetap sudah mengembalikan stock — barang terkirim tapi stock "kembali" ke gudang.

Demo lewat UI juga bisa: tab Produk → **Atur Stock** di dua tab browser bersamaan, atau buka satu order Pending di dua
tab lalu tekan **Cancel** di keduanya secepat mungkin dan bandingkan stock sebelum/sesudah. Jendela race di branch ini
hanya ±50 ms, jadi cara paling andal tetap lewat test di atas.
