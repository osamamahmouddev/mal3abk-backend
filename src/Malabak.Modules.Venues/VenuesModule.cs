using Malabak.Modules.Venues.Application.BlockedSlots;
using Malabak.Modules.Venues.Application.BlockedSlots.CreateBlockedSlot;
using Malabak.Modules.Venues.Application.BlockedSlots.DeleteBlockedSlot;
using Malabak.Modules.Venues.Application.BlockedSlots.GetBlockedSlotsByVenue;
using Malabak.Modules.Venues.Application.Approvals;
using Malabak.Modules.Venues.Application.Policies;
using Malabak.Modules.Venues.Application.Staff;
using Malabak.SharedKernel.Contracts;
using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.PaymentMethods;
using Malabak.Modules.Venues.Application.PaymentMethods.CreatePaymentMethod;
using Malabak.Modules.Venues.Application.PaymentMethods.DeletePaymentMethod;
using Malabak.Modules.Venues.Application.PaymentMethods.GetPaymentMethods;
using Malabak.Modules.Venues.Application.PaymentMethods.RevealPaymentMethodDetails;
using Malabak.Modules.Venues.Application.PaymentMethods.UpdatePaymentMethod;
using Malabak.SharedKernel.Security;
using Malabak.Modules.Venues.Application.Pitches;
using Malabak.Modules.Venues.Application.Pitches.CreatePitch;
using Malabak.Modules.Venues.Application.Pitches.DeletePitch;
using Malabak.Modules.Venues.Application.Pitches.GetPitchById;
using Malabak.Modules.Venues.Application.Pitches.GetPitchesByVenue;
using Malabak.Modules.Venues.Application.Pitches.UpdatePitch;
using Malabak.Modules.Venues.Application.Schedules;
using Malabak.Modules.Venues.Application.Schedules.GetVenueSchedules;
using Malabak.Modules.Venues.Application.Schedules.GetVenueSlots;
using Malabak.Modules.Venues.Application.Schedules.UpdateVenueSchedules;
using Malabak.Modules.Venues.Application.Venues;
using Malabak.Modules.Venues.Application.Venues.CreateVenue;
using Malabak.Modules.Venues.Application.Venues.DeleteVenue;
using Malabak.Modules.Venues.Application.Venues.GetVenueById;
using Malabak.Modules.Venues.Application.Venues.GetVenuesByOwner;
using Malabak.Modules.Venues.Application.Venues.PublishVenue;
using Malabak.Modules.Venues.Application.Venues.UpdateVenue;
using Malabak.Modules.Venues.Infrastructure.Repositories;
using Malabak.Modules.Venues.Infrastructure.Services;
using Malabak.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Malabak.Modules.Venues;

public static class VenuesModule
{
    public const string Schema = "venues";

    public static IServiceCollection AddVenuesModule(this IServiceCollection services)
    {
        // Repositories
        services.AddScoped<IVenueRepository, VenueRepository>();
        services.AddScoped<IPitchRepository, PitchRepository>();
        services.AddScoped<IBlockedSlotRepository, BlockedSlotRepository>();

        // Services
        services.AddScoped<IVenueAuditLogger, LoggingVenueAuditLogger>();

        // Authorization & tenant isolation
        services.AddScoped<IVenueTenantContext, VenueTenantContext>();
        services.AddScoped<IVenueAccessService, VenueAccessService>();

        // Venue Handlers
        services.AddScoped<ICommandHandler<CreateVenueCommand, Result<VenueDto>>, CreateVenueHandler>();
        services.AddScoped<ICommandHandler<UpdateVenueCommand, Result<VenueDto>>, UpdateVenueHandler>();
        services.AddScoped<ICommandHandler<DeleteVenueCommand, Result>, DeleteVenueHandler>();
        services.AddScoped<ICommandHandler<PublishVenueCommand, Result>, PublishVenueHandler>();
        services.AddScoped<IQueryHandler<GetVenueByIdQuery, Result<VenueDto>>, GetVenueByIdHandler>();
        services.AddScoped<IQueryHandler<GetVenuesByOwnerQuery, Result<IReadOnlyList<VenueDto>>>, GetVenuesByOwnerHandler>();

        // Pitch Handlers
        services.AddScoped<ICommandHandler<CreatePitchCommand, Result<PitchDto>>, CreatePitchHandler>();
        services.AddScoped<ICommandHandler<UpdatePitchCommand, Result<PitchDto>>, UpdatePitchHandler>();
        services.AddScoped<ICommandHandler<DeletePitchCommand, Result>, DeletePitchHandler>();
        services.AddScoped<IQueryHandler<GetPitchByIdQuery, Result<PitchDto>>, GetPitchByIdHandler>();
        services.AddScoped<IQueryHandler<GetPitchesByVenueQuery, Result<IReadOnlyList<PitchDto>>>, GetPitchesByVenueHandler>();

        // Operating Schedule Handlers
        services.AddScoped<ICommandHandler<UpdateVenueSchedulesCommand, Result<IReadOnlyList<OperatingScheduleDto>>>, UpdateVenueSchedulesHandler>();
        services.AddScoped<IQueryHandler<GetVenueSchedulesQuery, Result<IReadOnlyList<OperatingScheduleDto>>>, GetVenueSchedulesHandler>();
        services.AddScoped<IQueryHandler<GetVenueSlotsQuery, Result<IReadOnlyList<ScheduleSlotDto>>>, GetVenueSlotsHandler>();

        // Blocked Slot Handlers
        services.AddScoped<ICommandHandler<CreateBlockedSlotCommand, Result<BlockedSlotDto>>, CreateBlockedSlotHandler>();
        services.AddScoped<ICommandHandler<DeleteBlockedSlotCommand, Result>, DeleteBlockedSlotHandler>();
        services.AddScoped<IQueryHandler<GetBlockedSlotsByVenueQuery, Result<IReadOnlyList<BlockedSlotDto>>>, GetBlockedSlotsByVenueHandler>();

        // Payment Method Handlers (account details encrypted at field level)
        services.AddScoped<ICommandHandler<CreatePaymentMethodCommand, Result<PaymentMethodDto>>, CreatePaymentMethodHandler>();
        services.AddScoped<ICommandHandler<UpdatePaymentMethodCommand, Result<PaymentMethodDto>>, UpdatePaymentMethodHandler>();
        services.AddScoped<ICommandHandler<DeletePaymentMethodCommand, Result>, DeletePaymentMethodHandler>();
        services.AddScoped<IQueryHandler<GetPaymentMethodsQuery, Result<IReadOnlyList<PaymentMethodDto>>>, GetPaymentMethodsHandler>();
        services.AddScoped<IQueryHandler<RevealPaymentMethodDetailsQuery, Result<PaymentMethodDetailsDto>>, RevealPaymentMethodDetailsHandler>();
        services.AddScoped<IReEncryptionTarget, PaymentMethodReEncryptionTarget>();

        // Booking policies (versioned)
        services.AddScoped<IBookingPolicyProvider, BookingPolicyProvider>();
        services.AddScoped<ICommandHandler<SetBookingPolicyCommand, Result<BookingPolicyDto>>, SetBookingPolicyHandler>();
        services.AddScoped<IQueryHandler<GetBookingPoliciesQuery, Result<IReadOnlyList<BookingPolicyDto>>>, GetBookingPoliciesHandler>();
        services.AddScoped<IQueryHandler<GetBookingPolicyPreviewQuery, Result<BookingPolicyPreviewDto>>, GetBookingPolicyPreviewHandler>();

        // Staff management & invitations
        services.AddScoped<IStaffQuotaPolicy, DefaultStaffQuotaPolicy>();
        services.AddScoped<StaffManagementHandlers>();
        services.AddScoped<ICommandHandler<InviteStaffCommand, Result<StaffInvitationDto>>>(sp => sp.GetRequiredService<StaffManagementHandlers>());
        services.AddScoped<ICommandHandler<ResendStaffInvitationCommand, Result>>(sp => sp.GetRequiredService<StaffManagementHandlers>());
        services.AddScoped<ICommandHandler<RevokeStaffInvitationCommand, Result>>(sp => sp.GetRequiredService<StaffManagementHandlers>());
        services.AddScoped<IQueryHandler<GetVenueStaffQuery, Result<VenueStaffDto>>>(sp => sp.GetRequiredService<StaffManagementHandlers>());
        services.AddScoped<ICommandHandler<ChangeStaffRoleCommand, Result<StaffMemberDto>>>(sp => sp.GetRequiredService<StaffManagementHandlers>());
        services.AddScoped<ICommandHandler<RemoveStaffMemberCommand, Result>>(sp => sp.GetRequiredService<StaffManagementHandlers>());
        services.AddScoped<ICommandHandler<AcceptStaffInvitationCommand, Result<StaffMemberDto>>>(sp => sp.GetRequiredService<StaffManagementHandlers>());

        // Galleries (photos come from the media pipeline)
        services.AddScoped<Application.SpecialSchedules.SpecialScheduleHandlers>();
        services.AddScoped<Application.SpecialSchedules.SpecialScheduleQueries>();
        services.AddScoped<IPitchAvailabilityChecker, Application.SpecialSchedules.PitchAvailabilityChecker>();
        services.AddScoped<Application.Pricing.PricingService>();
        services.AddScoped<Application.Pricing.PriceRuleManagement>();
        services.AddScoped<Application.Availability.VenueAvailabilityQuery>();
        services.AddScoped<Application.Availability.CachedVenueAvailability>();
        services.AddScoped<IAvailabilityInvalidator, Application.Availability.AvailabilityInvalidator>();
        services.AddScoped<IPriceQuoteProvider>(sp => sp.GetRequiredService<Application.Pricing.PricingService>());
        services.AddScoped<ICommandHandler<Application.SpecialSchedules.AddScheduleOverrideCommand, Result<Application.SpecialSchedules.ScheduleChangeOutcome>>>(sp => sp.GetRequiredService<Application.SpecialSchedules.SpecialScheduleHandlers>());
        services.AddScoped<ICommandHandler<Application.SpecialSchedules.RemoveScheduleOverrideCommand, Result<Application.SpecialSchedules.ScheduleChangeOutcome>>>(sp => sp.GetRequiredService<Application.SpecialSchedules.SpecialScheduleHandlers>());
        services.AddScoped<ICommandHandler<Application.SpecialSchedules.ApplyHolidayClosuresCommand, Result<Application.SpecialSchedules.ScheduleChangeOutcome>>>(sp => sp.GetRequiredService<Application.SpecialSchedules.SpecialScheduleHandlers>());
        services.AddScoped<ICommandHandler<Application.SpecialSchedules.SetPitchWeeklyHoursCommand, Result<Application.SpecialSchedules.ScheduleChangeOutcome>>>(sp => sp.GetRequiredService<Application.SpecialSchedules.SpecialScheduleHandlers>());
        services.AddScoped<ICommandHandler<Application.SpecialSchedules.SetBookingRulesCommand, Result<Application.SpecialSchedules.BookingRulesDto>>>(sp => sp.GetRequiredService<Application.SpecialSchedules.SpecialScheduleHandlers>());
        services.AddScoped<Application.PlayerLists.PlayerVenueListHandlers>();
        services.AddScoped<IPersonalDataContributor, Application.PlayerLists.PlayerListsPersonalData>();
        services.AddScoped<Application.Gallery.GalleryHandlers>();
        services.AddScoped<ICommandHandler<Application.Gallery.AddGalleryPhotoCommand, Result<IReadOnlyList<string>>>>(sp => sp.GetRequiredService<Application.Gallery.GalleryHandlers>());
        services.AddScoped<ICommandHandler<Application.Gallery.RemoveGalleryPhotoCommand, Result<IReadOnlyList<string>>>>(sp => sp.GetRequiredService<Application.Gallery.GalleryHandlers>());

        // Approval workflow
        services.AddScoped<ICommandHandler<SubmitVenueForApprovalCommand, Result<VenueApprovalDto>>, SubmitVenueForApprovalHandler>();
        services.AddScoped<IQueryHandler<GetVenueApprovalQuery, Result<VenueApprovalDto>>, GetVenueApprovalHandler>();
        services.AddScoped<ICommandHandler<DecideVenueApprovalCommand, Result<VenueApprovalDto>>, DecideVenueApprovalHandler>();
        services.AddScoped<IQueryHandler<GetPendingVenueApprovalsQuery, Result<IReadOnlyList<PendingVenueApprovalDto>>>, GetPendingVenueApprovalsHandler>();

        return services;
    }
}
