namespace Tatkal.Messaging;

public class MessagingOptions
{
    public string Host { get; set; } = "rabbitmq";
    public string Username { get; set; } = "tatkal";
    public string Password { get; set; } = "change_me";

    /// <summary>How many messages a single consumer instance pulls concurrently.
    /// Bounds how much of the Tatkal spike any one worker absorbs at once
    /// (spec section 8) — raise this to scale a worker's throughput,
    /// but watch downstream DB/Redis load when you do.</summary>
    public ushort PrefetchCount { get; set; } = 16;

    /// <summary>Max retry attempts before a message is moved to its
    /// `<queue>_error` dead-letter queue (spec section 16).</summary>
    public int MaxRetryCount { get; set; } = 3;
}
