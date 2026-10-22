# Branch demo: `demo/broken-stock`

Branch ini **sengaja rusak** — jangan di-merge. Tujuannya membuktikan bahwa test concurrency benar-benar
menangkap race condition, bukan sekadar selalu hijau.

Perbedaannya dengan `main` hanya satu blok di `OrderService.CreateAsync`: pemotongan stock atomic

```sql
UPDATE "mProduct" SET "intStockQuantity" = "intStockQuantity" - @qty
WHERE "uidProductUUID" = @id AND "intStockQuantity" >= @qty
```

diganti pola lama *baca → cek di C# → tulis*:

```csharp
var product = await db.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId && p.IsActive, ct);
if (product is null || product.StockQuantity < item.Quantity) throw ...;
product.StockQuantity -= item.Quantity;   // ditulis saat SaveChanges
```

## Cara demo

```bash
git switch demo/broken-stock
dotnet test --filter "FullyQualifiedName~ScenarioA"   # GAGAL
git switch main
dotnet test --filter "FullyQualifiedName~ScenarioA"   # LULUS
```

Hasil di branch ini:

| Test | Seharusnya | Yang terjadi |
|---|---|---|
| `ScenarioA_two_orders_for_10_units_with_stock_15_only_one_succeeds` | 1 order sukses | **2** order sukses (20 unit terjual dari stock 15) |
| `ScenarioA_under_heavy_contention_stock_is_never_oversold` | 15 order sukses | **40** order sukses (40 unit terjual dari stock 15) |

Kenapa stock tidak sampai minus padahal oversold? Setiap request menulis nilai **absolut** yang dihitung dari data
basi (`15 - 10 = 5`), sehingga pengurangan request lain tertimpa (*lost update*). Stock di database terlihat wajar,
padahal barang yang terjual melebihi persediaan — bug yang paling sulit dilacak di sistem lama.
