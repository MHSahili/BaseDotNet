using BaseWebServer.Api.Common.Api;
using BaseWebServer.Api.Common.Paging;
using Microsoft.AspNetCore.Mvc;

namespace BaseWebServer.Api.Features.Sales;

public sealed class OrdersController : ApiControllerBase
{
    [HttpPost]
    [ProducesResponseType<CreateOrder.Response>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateOrder.Response>> Create(
        CreateOrder.Request request,
        [FromServices] CreateOrder.Handler handler,
        CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType<GetOrder.Response>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GetOrder.Response>> Get(
        int id,
        [FromServices] GetOrder.Handler handler,
        CancellationToken cancellationToken) =>
        await handler.HandleAsync(id, cancellationToken);

    [HttpGet]
    [ProducesResponseType<PagedResponse<ListOrders.Item>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<ListOrders.Item>>> List(
        [FromQuery] ListOrders.Request request,
        [FromServices] ListOrders.Handler handler,
        CancellationToken cancellationToken) =>
        await handler.HandleAsync(request, cancellationToken);

    [HttpPost("{id:int}/confirm")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Confirm(
        int id,
        [FromServices] ConfirmOrder.Handler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:int}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(
        int id,
        [FromServices] CancelOrder.Handler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, cancellationToken);
        return NoContent();
    }
}
