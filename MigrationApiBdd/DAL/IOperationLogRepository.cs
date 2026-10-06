using MigrationApiBdd.Models;

namespace MigrationApiBdd.DAL
{
    public interface IOperationLogRepository
    {
        void AddRange(List<OperationLog> operationLogs);
        Task<OperationLog?> GetOperationlogByIdAsync(int operationLogId, CancellationToken cancellationToken);
    }
}
