using Microsoft.EntityFrameworkCore;
using MigrationApiBdd.Models;
using MigrationApiBdd.Models.Context;

namespace MigrationApiBdd.DAL
{
    public class OperationLogRepository : IOperationLogRepository
    {
        private readonly MigApiContext _migApiDbContext;
        public OperationLogRepository(MigApiContext migApiDbContext)
        {
            _migApiDbContext = migApiDbContext;
        }

        public void AddRange(List<OperationLog> operationLogs)
        {
           _migApiDbContext.OperationLogs.AddRange(operationLogs);
        }

        public async Task<OperationLog?> GetOperationlogByIdAsync(int operationLogId, CancellationToken cancellationToken)
        {
           return await _migApiDbContext.OperationLogs.FirstOrDefaultAsync(x => x.OperationLogId == operationLogId, cancellationToken);
    
        }

      
    }
}
