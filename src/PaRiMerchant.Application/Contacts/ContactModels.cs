namespace PaRiMerchant.Application.Contacts;

public sealed record UpsertContactRequest(string Name, string Email, string Phone, string City, string Status);
public sealed record ContactResponse(string Id, string Name, string MaskedEmail, string MaskedPhone, string City, string Status);
