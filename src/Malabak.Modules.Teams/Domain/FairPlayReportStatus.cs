namespace Malabak.Modules.Teams.Domain;

public enum FairPlayReportStatus
{
    PendingReview = 1,
    Approved = 2,
    Dismissed = 3,
    Appealed = 4,
    AppealUpheld = 5,
    AppealOverturned = 6
}
