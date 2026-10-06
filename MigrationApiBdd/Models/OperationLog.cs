using System.ComponentModel.DataAnnotations;

namespace MigrationApiBdd.Models
{
    public class OperationLog
    {
        public long OperationLogId { get; set; }

        [Required]
        [MaxLength(100)]
        public string OperationName { get; set; } = null!;

        [Required]
        [MaxLength(20)]
        public string Level { get; set; } = "Information";

        [Required]
        public string Message { get; set; } = null!;

        public string? Exception { get; set; }

        public DateTime ExecutedAtUtc { get; set; } = DateTime.UtcNow;

        public int DurationMs { get; set; }

        [MaxLength(100)]
        public string? CorrelationId { get; set; }
    }
}
