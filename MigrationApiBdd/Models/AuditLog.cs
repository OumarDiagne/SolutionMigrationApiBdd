using System.ComponentModel.DataAnnotations;

namespace MigrationApiBdd.Models
{
    public class AuditLog
    {
        public long AuditLogId { get; set; }

        [Required]
        [MaxLength(100)]
        public string EntityId { get; set; } = null!;

        [Required]
        [MaxLength(100)]
        public string EntityName { get; set; } = null!;
        

        [Required]
        [MaxLength(50)]
        public string ActionType { get; set; } = null!;

        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
            
        [Required]
        [MaxLength(100)]
        public string? ChangedBy { get; set; }

        [Required]
        public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;

        [MaxLength(100)]
        public string? CorrelationId { get; set; }

        [MaxLength(250)]
        public string? Reason { get; set; }
    }
}
