namespace Malabak.SharedKernel.Persistence;

public sealed class IdempotencyRecord
{
    public Guid Id { get; init; } = IdGenerator.NewGuid();
    public string Key { get; init; } = string.Empty;
    public string RequestHash { get; init; } = string.Empty;
    public string HttpMethod { get; init; } = string.Empty;
    public string RequestPath { get; init; } = string.Empty;
    public int StatusCode { get; private set; }
    public string? ResponseContentType { get; private set; }
    public string? ResponseBody { get; private set; }
    public IdempotencyStatus Status { get; private set; } = IdempotencyStatus.Pending;
    public DateTime CreatedAtUtc { get; init; }
    public DateTime ExpiresAtUtc { get; init; }

    public void MarkAsCompleted(int statusCode, string? responseContentType, string? responseBody)
    {
        StatusCode = statusCode;
        ResponseContentType = responseContentType;
        ResponseBody = responseBody;
        Status = IdempotencyStatus.Completed;
    }

    public void MarkAsFailed()
    {
        Status = IdempotencyStatus.Failed;
    }

    public bool IsExpired(DateTime currentUtc) => currentUtc >= ExpiresAtUtc;
}
