namespace MigrationApiBdd.Exception
{
    public sealed class BusinessRuleException : System.Exception
    {

        public int StatusCode { get; }
        public BusinessRuleException(string message,
        int statusCode = StatusCodes.Status400BadRequest) : base(message)
        {
           StatusCode = statusCode;
        }
    }
}
