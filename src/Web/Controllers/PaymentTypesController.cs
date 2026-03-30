using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.BusinessObjects.Payments;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Web.Controllers;

[ApiController]
[Route("api/payment-types")]
public class PaymentTypesController : ControllerBase
{
    private readonly IPaymentTypeService _service;
    private readonly ILogger<PaymentTypesController> _logger;

    public PaymentTypesController(
        ILogger<PaymentTypesController> logger,
        IPaymentTypeService service)
    {
        _logger = logger;
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<PaymentTypeInfo>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAll()
    {
        var items = await _service.GetAllAsync();
        return Ok(items);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PaymentTypeInfo))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetById(int id)
    {
        var item = await _service.GetByIdAsync(id);
        if (item == null) return NotFound();
        return Ok(item);
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(PaymentTypeInfo))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Create([FromBody] PaymentTypeCreateRequest request)
    {
        var created = await _service.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = created.PaymentTypeId }, created);
    }

    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Update(int id, [FromBody] PaymentTypeUpdateRequest request)
    {
        var result = await _service.UpdateAsync(id, request);
        return result ? NoContent() : NotFound();
    }
}
