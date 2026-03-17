using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaRiMerchant.Api.Extensions;
using PaRiMerchant.Application.Transactions;

namespace PaRiMerchant.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/transactions")]
public sealed class TransactionsController(TransactionService transactionService) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<TransactionResponse>> GetAsync(CancellationToken cancellationToken)
        => transactionService.GetAllAsync(User.GetRequiredTenantId(), cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<TransactionResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => transactionService.GetByIdAsync(User.GetRequiredTenantId(), id, cancellationToken);

    [HttpGet("{id:guid}/events")]
    public Task<IReadOnlyList<TransactionEventResponse>> GetEventsAsync(Guid id, CancellationToken cancellationToken)
        => transactionService.GetEventsAsync(User.GetRequiredTenantId(), id, cancellationToken);
}
