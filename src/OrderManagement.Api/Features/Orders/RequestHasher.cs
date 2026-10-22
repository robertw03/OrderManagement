using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OrderManagement.Api.Features.Orders;

/// <summary>
/// Fingerprints the normalized create-order payload so that reusing an Idempotency-Key
/// with a different body can be detected and rejected.
/// </summary>
public static class RequestHasher
{
    public static string Hash(string customerId, string shippingAddress, IEnumerable<(Guid ProductId, int Quantity)> normalizedItems)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            customerId,
            shippingAddress,
            items = normalizedItems.Select(i => new { productId = i.ProductId, quantity = i.Quantity })
        });

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
