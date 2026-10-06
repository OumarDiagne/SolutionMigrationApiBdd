namespace MigrationApiBdd.Models
{

    /// <summary>
    /// Represents an idempotency record that is used to ensure that the same request is not processed multiple times.
    /// </summary>
    public class IdempotencyRecord
        {
        /// <summary>
        /// The unique identifier for the idempotency record.
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// The scope of the idempotency record, which can be used to group related requests together.
        /// </summary>
        public string Scope { get; set; } = default!;

        /// <summary>
        /// The idempotency key for the request, which is used to ensure that the same request is not processed multiple times.
        /// </summary>
        public string IdempotencyKey { get; set; } = default!;

        /// <summary>
        /// The hash of the request body, which can be used to detect changes in the request data.
        /// </summary>
        public string RequestHash { get; set; } = default!;

        /// <summary>
        /// The status of the idempotency record, which can be "Processing", "Completed", or "Failed".
        /// </summary>
        public string Status { get; set; } = "Processing";

        /// <summary>
        /// The HTTP status code of the response, if the request has been completed.
        /// </summary>
        public int? ResponseStatusCode { get; set; }

        /// <summary>
        /// The body of the response, if the request has been completed.
        /// </summary>
        public string? ResponseBody { get; set; }

        /// <summary>
        /// The date and time when the idempotency record was created, in UTC.
        /// </summary>
        public DateTime CreatedAtUtc { get; set; }

        /// <summary>
        /// The date and time when the idempotency record was last updated, in UTC.
        /// </summary>
        public DateTime? CompletedAtUtc { get; set; }

        /// <summary>
        /// The date and time when the idempotency record will expire, in UTC. After this time, the record may be deleted or ignored.
        /// </summary>
        public DateTime ExpiresAtUtc { get; set; }
        }
}
