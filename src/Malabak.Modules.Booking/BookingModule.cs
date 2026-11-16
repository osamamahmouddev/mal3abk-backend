using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Application.Bookings.CancelBooking;
using Malabak.Modules.Booking.Application.Bookings.CreateBooking;
using Malabak.Modules.Booking.Application.Bookings.ManualBooking;
using Malabak.Modules.Booking.Application.Bookings.NoShow;
using Malabak.Modules.Booking.Application.Bookings.PaymentDetails;
using Malabak.Modules.Booking.Application.Bookings.PlayerQueries;
using Malabak.Modules.Booking.Application.Bookings.Proofs;
using Malabak.Modules.Booking.Application.Bookings.Refunds;
using Malabak.Modules.Booking.Application.Bookings.ReviewProof;
using Malabak.Modules.Booking.Application.Bookings.SubmitProof;
using Malabak.Modules.Booking.Application.Bookings.VenueQueries;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.Modules.Booking.Infrastructure.Jobs;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Malabak.Modules.Booking;

public static class BookingModule
{
    public const string Schema = "booking";

    public static IServiceCollection AddBookingModule(this IServiceCollection services)
    {
        // The use cases depend on this abstraction, never on the concrete context (architecture rule)
        services.AddScoped<IBookingDbContext>(sp => sp.GetRequiredService<BookingDbContext>());

        // Reference Generator
        services.AddScoped<IBookingReferenceGenerator, BookingReferenceGenerator>();

        // Cross-module contracts
        services.AddScoped<IUpcomingBookingsReader, UpcomingBookingsReader>();
        services.AddScoped<Malabak.SharedKernel.Contracts.IUserActivityProvider, Application.Admin.BookingUserActivityProvider>();
        services.AddScoped<Malabak.SharedKernel.Contracts.IVenueBookingStatsReader, Application.Admin.VenueBookingStatsReader>();
        services.AddScoped<Application.Admin.AdminBookingService>();
        services.AddScoped<Application.Disputes.PaymentDisputeService>();
        services.AddScoped<Malabak.SharedKernel.Contracts.IAdminMetricsProvider, Application.Admin.BookingMetricsProvider>();
        services.AddScoped<IPersonalDataContributor, BookingPersonalData>();
        services.AddScoped<IBookingPaymentConfirmationContract, BookingPaymentConfirmationService>();
        services.AddScoped<IBookingPaymentDetailsProvider, BookingPaymentConfirmationService>();

        // Handlers
        services.AddScoped<ICommandHandler<CreateBookingCommand, Result<CreateBookingResponse>>, CreateBookingHandler>();
        services.AddScoped<ICommandHandler<SubmitProofCommand, Result>, SubmitProofHandler>();
        services.AddScoped<ICommandHandler<ReviewProofCommand, Result>, ReviewProofHandler>();
        services.AddScoped<ICommandHandler<CancelBookingCommand, Result>, CancelBookingHandler>();
        services.AddScoped<ICommandHandler<VenueCancelBookingCommand, Result>, VenueCancelBookingHandler>();
        services.AddScoped<IQueryHandler<GetCancelPreviewQuery, Result<CancelPreviewResponse>>, GetCancelPreviewHandler>();
        services.AddScoped<IQueryHandler<GetBookingPaymentMethodsQuery, Result<IReadOnlyList<BookingPaymentMethodDto>>>, GetBookingPaymentMethodsHandler>();
        services.AddScoped<ICommandHandler<CreateManualBookingCommand, Result<ManualBookingResponse>>, CreateManualBookingHandler>();
        services.AddScoped<ICommandHandler<ConfirmManualBookingCommand, Result>, ConfirmManualBookingHandler>();
        services.AddScoped<ICommandHandler<RecordManualRefundCommand, Result>, RecordManualRefundHandler>();
        services.AddScoped<ICommandHandler<MarkNoShowCommand, Result>, MarkNoShowHandler>();

        // Query Handlers
        services.AddScoped<IQueryHandler<GetPlayerBookingsQuery, Result<PagedList<PlayerBookingSummaryDto>>>, PlayerBookingQueriesHandler>();
        services.AddScoped<IQueryHandler<GetPlayerBookingByIdQuery, Result<PlayerBookingDetailDto>>, PlayerBookingQueriesHandler>();

        services.AddScoped<IQueryHandler<GetVenueBookingsQuery, Result<PagedList<VenueBookingItemDto>>>, VenueBookingQueriesHandler>();
        services.AddScoped<IQueryHandler<GetVenuePendingBookingsQuery, Result<IReadOnlyList<VenueBookingItemDto>>>, VenueBookingQueriesHandler>();
        services.AddScoped<IQueryHandler<GetVenueBookingDetailQuery, Result<VenueBookingDetailDto>>, VenueBookingQueriesHandler>();
        services.AddScoped<IQueryHandler<GetVenueCalendarQuery, Result<IReadOnlyList<CalendarSlotDto>>>, VenueBookingQueriesHandler>();
        services.AddScoped<IQueryHandler<Application.Dashboard.GetVenueDashboardQuery, Result<Application.Dashboard.VenueDashboardDto>>, Application.Dashboard.GetVenueDashboardHandler>();
        services.AddScoped<Application.Customers.VenueCustomerService>();
        services.AddScoped<Application.Reports.VenueReportService>();
        services.AddScoped<Malabak.SharedKernel.Security.IReEncryptionTarget, CustomerNoteReEncryptionTarget>();

        services.AddScoped<IQueryHandler<GetBookingProofsQuery, Result<IReadOnlyList<BookingProofDto>>>, GetBookingProofsHandler>();

        // Hangfire Jobs & Schedulers
        services.AddScoped<IHoldExpiryScheduler, HangfireHoldExpiryScheduler>();
        services.AddScoped<ExpireBookingHoldsJob>();
        services.AddScoped<CompleteBookingsJob>();
        services.AddScoped<BookingRemindersJob>();
        services.AddScoped<ProofRetentionJob>();

        return services;
    }
}
