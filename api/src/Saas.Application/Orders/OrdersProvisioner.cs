using Microsoft.EntityFrameworkCore;
using Saas.Application.Auth;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.Orders;

namespace Saas.Application.Orders;

public sealed class OrdersProvisioner(IAppDbContext db) : IProductProvisioner
{
    public string Product => Products.WhatsAppOrders;

    public Task ProvisionAsync(Tenant tenant, SignupRequest request, Guid ownerUserId, CancellationToken ct)
    {
        var whatsapp = PhoneNumbers.Normalize(request.WhatsappNumber, tenant.Country)
            ?? throw BadRequestException.Field("whatsappNumber", "Enter a valid WhatsApp number.");
        db.Stores.Add(new Store { Name = tenant.Name, Slug = tenant.Slug, WhatsappNumber = whatsapp, Currency = tenant.Currency });
        return Task.CompletedTask;
    }
}

public sealed class OrdersUsage(IAppDbContext db) : IUsageProvider
{
    public string Product => Products.WhatsAppOrders;

    public async Task<IReadOnlyList<UsageItemDto>> GetAsync(Plan plan, CancellationToken ct) =>
        [new(LimitKeys.Products, "Products", await db.Products.CountAsync(ct), plan.Limit(LimitKeys.Products))];
}
