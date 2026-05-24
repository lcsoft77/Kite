namespace Kite.Core.Models;

public static class LogHelpers
{
    private const int MaxLogEntries = 200;
    private const int MaxSentMessages = 100;

    public static void AddRequestLog(List<RequestLogEntry> log, string operation, string details, int statusCode = 200, string source = "")
    {
        lock (log)
        {
            if (log.Count >= MaxLogEntries)
                log.RemoveAt(0);
            log.Add(new RequestLogEntry
            {
                Operation = operation,
                Details = details,
                StatusCode = statusCode,
                Source = source
            });
        }
    }

    public static void AddSentMessage(List<SentMessageEntry> messages, string messageId, string body, string source = "")
    {
        lock (messages)
        {
            if (messages.Count >= MaxSentMessages)
                messages.RemoveAt(0);
            messages.Add(new SentMessageEntry
            {
                MessageId = messageId,
                Body = body,
                Source = source
            });
        }
    }
}
