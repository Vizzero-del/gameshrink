using GameShrink.Core.Models;

namespace GameShrink.Core.Abstractions;

public interface IOperationJournal
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task AddAsync(OperationRecord record, CancellationToken cancellationToken);
    Task UpdateAsync(OperationRecord record, CancellationToken cancellationToken);

    Task<IReadOnlyList<OperationRecord>> GetRecentAsync(int take, CancellationToken cancellationToken);
    Task<OperationRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}
