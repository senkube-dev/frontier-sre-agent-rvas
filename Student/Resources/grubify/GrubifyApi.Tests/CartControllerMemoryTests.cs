using GrubifyApi.Controllers;
using GrubifyApi.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace GrubifyApi.Tests
{
    // Regression tests for the ca-food-api HTTP 500 / System.OutOfMemoryException incident
    // (alert-food-http-5xx, 2026-10-28): every POST /api/cart/{userId}/items allocated a 10 MiB
    // buffer and retained it in a static list, so ~75 cart writes exhausted the GC heap hard limit
    // of a 1 GiB container and every subsequent request on the replica failed.
    public class CartControllerMemoryTests
    {
        // A cart write carries a few hundred bytes of JSON. One MiB is an order of magnitude below the
        // 10 MiB per-request buffer that caused the incident while leaving ample headroom for normal
        // per-request allocations (cart objects, food item lookup list, LINQ enumerators).
        private const long MaxBytesPerCartWrite = 1024 * 1024;

        // Enough writes that the defect would retain 200 MiB, far beyond any GC noise.
        private const int CartWrites = 20;

        private static AddCartItemRequest Request() =>
            new() { FoodItemId = 1, Quantity = 1, SpecialInstructions = "regression" };

        [Fact]
        public void AddItemToCart_DoesNotRetainMemoryAcrossRequests()
        {
            var controller = new CartController();
            var userId = $"retention-{Guid.NewGuid()}";

            controller.AddItemToCart(userId, Request()); // warm up JIT and static state
            long before = GC.GetTotalMemory(forceFullCollection: true);

            for (var i = 0; i < CartWrites; i++)
            {
                controller.AddItemToCart(userId, Request());
            }

            long retained = GC.GetTotalMemory(forceFullCollection: true) - before;

            Assert.True(
                retained < MaxBytesPerCartWrite,
                $"{CartWrites} cart writes retained {retained / (1024 * 1024)} MiB after a full GC; cart writes must not accumulate memory.");
        }

        [Fact]
        public void AddItemToCart_DoesNotAllocateLargeBufferPerRequest()
        {
            var controller = new CartController();
            var userId = $"allocation-{Guid.NewGuid()}";

            controller.AddItemToCart(userId, Request()); // warm up
            long before = GC.GetAllocatedBytesForCurrentThread();
            controller.AddItemToCart(userId, Request());
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.True(
                allocated < MaxBytesPerCartWrite,
                $"A single cart write allocated {allocated / 1024} KiB; expected well under {MaxBytesPerCartWrite / 1024} KiB.");
        }

        [Fact]
        public void AddItemToCart_PreservesCartContract()
        {
            var controller = new CartController();
            var userId = $"contract-{Guid.NewGuid()}";

            controller.AddItemToCart(userId, new AddCartItemRequest { FoodItemId = 1, Quantity = 2 });
            var result = controller.AddItemToCart(userId, new AddCartItemRequest { FoodItemId = 1, Quantity = 1, SpecialInstructions = "no basil" });

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var cart = Assert.IsType<Cart>(ok.Value);
            var item = Assert.Single(cart.Items);
            Assert.Equal(userId, cart.UserId);
            Assert.Equal(1, item.FoodItemId);
            Assert.Equal(3, item.Quantity);
            Assert.Equal("no basil", item.SpecialInstructions);
            Assert.Equal("Margherita Pizza", item.FoodItem.Name);
        }
    }
}
