using GrubifyApi.Controllers;
using GrubifyApi.Models;
using Microsoft.AspNetCore.Mvc;

namespace GrubifyApi.Tests;

public class CartControllerTests
{
    [Fact]
    public void AddItemToCart_DoesNotRetainRequestDataAcrossRequests()
    {
        var controller = new CartController();
        var userId = "user-1";

        for (var i = 0; i < 25; i++)
        {
            var result = controller.AddItemToCart(userId, new AddCartItemRequest
            {
                FoodItemId = 1,
                Quantity = 1,
                SpecialInstructions = $"request-{i}"
            });

            var actionResult = Assert.IsType<ActionResult<Cart>>(result);
            var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
            var cart = Assert.IsType<Cart>(okResult.Value);
            Assert.Equal(userId, cart.UserId);
            Assert.NotEmpty(cart.Items);
        }
    }
}
