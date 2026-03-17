using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaRiMerchant.Api.Extensions;
using PaRiMerchant.Application.Contacts;

namespace PaRiMerchant.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/contacts")]
public sealed class ContactsController(ContactService contactService) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ContactResponse>> GetAsync(CancellationToken cancellationToken)
        => contactService.GetAllAsync(User.GetRequiredTenantId(), cancellationToken);

    [HttpGet("search")]
    public Task<ContactResponse> SearchAsync([FromQuery] string phone, CancellationToken cancellationToken)
        => contactService.SearchByPhoneAsync(User.GetRequiredTenantId(), phone, cancellationToken);

    [HttpPost]
    public Task<ContactResponse> CreateAsync([FromBody] UpsertContactRequest request, CancellationToken cancellationToken)
        => contactService.CreateAsync(User.GetRequiredTenantId(), request, cancellationToken);

    [HttpPut("{id:guid}")]
    public Task<ContactResponse> UpdateAsync(Guid id, [FromBody] UpsertContactRequest request, CancellationToken cancellationToken)
        => contactService.UpdateAsync(User.GetRequiredTenantId(), id, request, cancellationToken);
}
