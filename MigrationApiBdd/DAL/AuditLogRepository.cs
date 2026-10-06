using Microsoft.EntityFrameworkCore;
using MigrationApiBdd.Models;
using MigrationApiBdd.Models.Context;

namespace MigrationApiBdd.DAL
{
    public class AuditLogRepository : IAuditLogRepository
    {
        private readonly MigApiContext _migApiContext;

        public AuditLogRepository(MigApiContext migApiContext)
        {
            this._migApiContext = migApiContext;
        }

        public void AddRange(List<AuditLog> auditLogs)
        {
            _migApiContext.AuditLogs.AddRange(auditLogs);
        }

        public async Task<AuditLog?> GetAuditLogByIdAsync(int auditLogId, CancellationToken cancellationToken)
        {
           return await _migApiContext.AuditLogs.FirstOrDefaultAsync(a => a.AuditLogId == auditLogId, cancellationToken);
        }
        public async Task<AuditLog?> GetOldAuditLogByIdAsync(int entityId, int clientcommande, CancellationToken cancellationToken)
        {
            return await _migApiContext.AuditLogs.LastOrDefaultAsync(a => a.EntityId == entityId.ToString() && a.NewValue!.Contains(clientcommande.ToString()), cancellationToken);
        }

        public Task<AuditLog?> GetOldCommandeAuditLogByIdAsync(int commandeId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
