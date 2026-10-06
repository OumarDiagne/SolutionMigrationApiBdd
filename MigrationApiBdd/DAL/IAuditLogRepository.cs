using MigrationApiBdd.Models;

namespace MigrationApiBdd.DAL
{
    public interface IAuditLogRepository
    {
        void AddRange(List<AuditLog> auditLogs);
        Task<AuditLog?> GetOldCommandeAuditLogByIdAsync(int commandeId, CancellationToken cancellationToken);
        Task<AuditLog?> GetAuditLogByIdAsync(int auditLogId, CancellationToken cancellationToken);
    }
}
