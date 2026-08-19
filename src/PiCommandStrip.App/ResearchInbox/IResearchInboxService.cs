namespace PiCommandStrip.App.ResearchInbox;

public interface IResearchInboxService
{
    ResearchInboxState Current { get; }

    Task<ResearchSaveResult> SaveAsync(ResearchCapture capture, CancellationToken cancellationToken);

    Task<ResearchInboxPage> GetPageAsync(long? beforeId, int limit, CancellationToken cancellationToken);

    Task<ResearchItem?> GetAsync(long id, CancellationToken cancellationToken);

    Task<bool> SetReviewedAsync(long id, bool isReviewed, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken);
}

public interface IResearchInboxStateBroadcaster
{
    Task BroadcastAsync(ResearchInboxState state, CancellationToken cancellationToken);
}

