using BaseWebServer.Api.Common.Api;
using Microsoft.AspNetCore.Mvc;

namespace BaseWebServer.Api.Features.Sales;

public sealed class CustomersController : ApiControllerBase
{
    [HttpPost]
    [ProducesResponseType<CreateCustomer.Response>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateCustomer.Response>> Create(
        CreateCustomer.Request request,
        [FromServices] CreateCustomer.Handler handler,
        CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType<GetCustomer.Response>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GetCustomer.Response>> Get(
        int id,
        [FromServices] GetCustomer.Handler handler,
        CancellationToken cancellationToken) =>
        await handler.HandleAsync(id, cancellationToken);
}
