using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Modules.Venues.Application.PaymentMethods;
using Malabak.Modules.Venues.Application.PaymentMethods.CreatePaymentMethod;
using Malabak.Modules.Venues.Application.PaymentMethods.DeletePaymentMethod;
using Malabak.Modules.Venues.Application.PaymentMethods.GetPaymentMethods;
using Malabak.Modules.Venues.Application.PaymentMethods.RevealPaymentMethodDetails;
using Malabak.Modules.Venues.Application.PaymentMethods.UpdatePaymentMethod;
using Malabak.Modules.Venues.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

public static class PaymentMethodEndpoints
{
    public static void MapPaymentMethodEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var group = app.MapGroup("/api/v{version:apiVersion}/venue-manager/venues/{venueId:guid}/payment-methods")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization(Permissions.VenuesManage)
            .AddEndpointFilter<VenueTenantEndpointFilter>()
            .WithTags("Venue Payment Methods");

        group.MapGet("/", async (Guid venueId, ClaimsPrincipal user,
            IQueryHandler<GetPaymentMethodsQuery, Result<IReadOnlyList<PaymentMethodDto>>> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetPaymentMethodsQuery(venueId, user.GetUserId()!.Value), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
        })
        .WithName("GetPaymentMethods")
        .WithSummary("List a venue's payment methods with account details masked");

        group.MapPost("/", async (Guid venueId, ClaimsPrincipal user, [FromBody] PaymentMethodApiRequest request,
            ICommandHandler<CreatePaymentMethodCommand, Result<PaymentMethodDto>> handler, CancellationToken ct) =>
        {
            var command = new CreatePaymentMethodCommand(venueId, user.GetUserId()!.Value, request.MethodType, request.RecipientName,
                request.AccountDetails, request.Instructions, request.DepositType, request.DepositValue);
            var result = await handler.HandleAsync(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/venue-manager/venues/{venueId}/payment-methods/{result.Value.Id}", result.Value)
                : ToProblem(result.Error);
        })
        .WithName("CreatePaymentMethod")
        .WithSummary("Add a bank, wallet or InstaPay payment method (account details are encrypted)");

        group.MapPut("/{id:guid}", async (Guid venueId, Guid id, ClaimsPrincipal user, [FromBody] UpdatePaymentMethodApiRequest request,
            ICommandHandler<UpdatePaymentMethodCommand, Result<PaymentMethodDto>> handler, CancellationToken ct) =>
        {
            var command = new UpdatePaymentMethodCommand(venueId, id, user.GetUserId()!.Value, request.MethodType, request.RecipientName,
                request.AccountDetails, request.Instructions, request.DepositType, request.DepositValue, request.IsActive);
            var result = await handler.HandleAsync(command, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
        })
        .WithName("UpdatePaymentMethod")
        .WithSummary("Update a payment method");

        group.MapDelete("/{id:guid}", async (Guid venueId, Guid id, ClaimsPrincipal user,
            ICommandHandler<DeletePaymentMethodCommand, Result> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new DeletePaymentMethodCommand(venueId, id, user.GetUserId()!.Value), ct);
            return result.IsSuccess ? Results.NoContent() : ToProblem(result.Error);
        })
        .WithName("DeletePaymentMethod")
        .WithSummary("Remove a payment method");

        group.MapPost("/{id:guid}/reveal", async (Guid venueId, Guid id, ClaimsPrincipal user,
            IQueryHandler<RevealPaymentMethodDetailsQuery, Result<PaymentMethodDetailsDto>> handler, HttpContext http, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new RevealPaymentMethodDetailsQuery(venueId, id, user.GetUserId()!.Value), ct);
            if (!result.IsSuccess)
            {
                return ToProblem(result.Error);
            }

            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(result.Value);
        })
        .WithName("RevealPaymentMethodDetails")
        .WithSummary("Reveal full account details (audited, never cached)");
    }

    // The tenant filter already rejects requests without a user id, so GetUserId() is non-null here.
    private static IResult ToProblem(Error error)
    {
        var status = error.Code.EndsWith(".NotFound", StringComparison.Ordinal)
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status400BadRequest;
        return Results.Problem(title: error.Code, detail: error.Description, statusCode: status);
    }

    public sealed record PaymentMethodApiRequest(
        string MethodType,
        string RecipientName,
        string AccountDetails,
        string? Instructions,
        DepositType DepositType,
        decimal DepositValue);

    public sealed record UpdatePaymentMethodApiRequest(
        string MethodType,
        string RecipientName,
        string AccountDetails,
        string? Instructions,
        DepositType DepositType,
        decimal DepositValue,
        bool IsActive);
}
