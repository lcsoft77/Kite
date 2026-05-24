using System.Text;
using System.Web;
using Kite.Core;
using Kite.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Kite.SNS;

public class SnsService
{
    private readonly IServiceRegistry _registry;
    private readonly ILogger<SnsService> _logger;

    public SnsService(IServiceRegistry registry, ILoggerFactory loggerFactory)
    {
        _registry = registry;
        _logger = loggerFactory.CreateLogger<SnsService>();
    }

    public async Task HandleAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();
        var form = HttpUtility.ParseQueryString(body);
        var action = form["Action"] ?? string.Empty;
        var sanitizedAction = SanitizeForLog(action);

        _logger.LogDebug("[DEBUG] SNS HandleAsync - Action: {Action}, Method: {Method}", sanitizedAction, context.Request.Method);
        _logger.LogDebug("[DEBUG] SNS action: {Action}, RequestBodyLength: {BodyLength}", sanitizedAction, body.Length);

        switch (action)
        {
            case "CreateTopic":
                await HandleCreateTopicAsync(context, form);
                break;
            case "DeleteTopic":
                await HandleDeleteTopicAsync(context, form);
                break;
            case "ListTopics":
                await HandleListTopicsAsync(context);
                break;
            case "GetTopicAttributes":
                await HandleGetTopicAttributesAsync(context, form);
                break;
            case "SetTopicAttributes":
                await HandleSetTopicAttributesAsync(context, form);
                break;
            case "Subscribe":
                await HandleSubscribeAsync(context, form);
                break;
            case "Unsubscribe":
                await HandleUnsubscribeAsync(context, form);
                break;
            case "Publish":
                await HandlePublishAsync(context, form);
                break;
            case "ListSubscriptions":
                await HandleListSubscriptionsAsync(context);
                break;
            case "ListSubscriptionsByTopic":
                await HandleListSubscriptionsByTopicAsync(context, form);
                break;
            default:
                _logger.LogWarning("Unknown SNS action: {Action}", sanitizedAction);
                context.Response.StatusCode = 400;
                context.Response.ContentType = "text/xml";
                await context.Response.WriteAsync(BuildErrorResponse("InvalidAction", $"Unknown action: {action}"));
                break;
        }
    }

    private async Task HandleCreateTopicAsync(HttpContext context, System.Collections.Specialized.NameValueCollection form)
    {
        var name = form["Name"] ?? string.Empty;
        _logger.LogDebug("[ENTRY] HandleCreateTopicAsync - TopicName: {TopicName}", name);
        
        if (string.IsNullOrWhiteSpace(name))
        {
            _logger.LogError("[ERROR] CreateTopic failed - Topic name is required");
            context.Response.StatusCode = 400;
            context.Response.ContentType = "text/xml";
            await context.Response.WriteAsync(BuildErrorResponse("InvalidParameter", "Topic name is required."));
            return;
        }

        var existing = _registry.GetSnsTopic(name);
        if (existing is not null)
        {
            LogHelpers.AddRequestLog(existing.RequestLog, "CreateTopic", $"Topic={name} (already exists)");
            existing.NotifyChange();
            context.Response.ContentType = "text/xml";
            await context.Response.WriteAsync(BuildCreateTopicResponse(existing.TopicArn));
            return;
        }

        var arn = BuildTopicArn(name);
        var topic = new SnsTopic { Name = name, TopicArn = arn };
        _registry.RegisterSnsTopic(topic);

        LogHelpers.AddRequestLog(topic.RequestLog, "CreateTopic", $"Topic={name}");
        topic.NotifyChange();

        _logger.LogInformation("CreateTopic completed - TopicName: {TopicName}, Arn: {Arn}", name, arn);

        context.Response.ContentType = "text/xml";
        await context.Response.WriteAsync(BuildCreateTopicResponse(arn));
    }

    private async Task HandleDeleteTopicAsync(HttpContext context, System.Collections.Specialized.NameValueCollection form)
    {
        var topicArn = form["TopicArn"] ?? string.Empty;
        _logger.LogDebug("[ENTRY] HandleDeleteTopicAsync - TopicArn: {TopicArn}", topicArn);
        
        var topic = _registry.GetSnsTopic(topicArn);

        if (topic is null)
        {
            _logger.LogError("[ERROR] DeleteTopic failed - Topic not found: {TopicArn}", topicArn);
            context.Response.StatusCode = 404;
            context.Response.ContentType = "text/xml";
            await context.Response.WriteAsync(BuildErrorResponse("NotFound", $"Topic not found: {topicArn}"));
            return;
        }

        _registry.DeleteSnsTopic(topicArn);
        _logger.LogInformation("DeleteTopic completed - TopicArn: {TopicArn}", topicArn);
        
        context.Response.ContentType = "text/xml";
        await context.Response.WriteAsync(BuildEmptyResponse("DeleteTopicResponse"));
    }

    private async Task HandleListTopicsAsync(HttpContext context)
    {
        var topics = _registry.GetAllSnsTopics().ToList();
        var sb = new StringBuilder();
        sb.Append("<ListTopicsResponse xmlns=\"http://sns.amazonaws.com/doc/2010-03-31/\">");
        sb.Append("<ListTopicsResult><Topics>");
        foreach (var topic in topics)
        {
            sb.Append("<member>");
            sb.Append($"<TopicArn>{Escape(topic.TopicArn)}</TopicArn>");
            sb.Append("</member>");
        }
        sb.Append("</Topics></ListTopicsResult>");
        sb.Append(BuildResponseMetadata());
        sb.Append("</ListTopicsResponse>");

        context.Response.ContentType = "text/xml";
        await context.Response.WriteAsync(sb.ToString());
    }

    private async Task HandleGetTopicAttributesAsync(HttpContext context, System.Collections.Specialized.NameValueCollection form)
    {
        var topicArn = form["TopicArn"] ?? string.Empty;
        var topic = _registry.GetSnsTopic(topicArn);

        if (topic is null)
        {
            context.Response.StatusCode = 404;
            context.Response.ContentType = "text/xml";
            await context.Response.WriteAsync(BuildErrorResponse("NotFound", $"Topic not found: {topicArn}"));
            return;
        }

        LogHelpers.AddRequestLog(topic.RequestLog, "GetTopicAttributes", $"TopicArn={topicArn}");
        topic.NotifyChange();

        var sb = new StringBuilder();
        sb.Append("<GetTopicAttributesResponse xmlns=\"http://sns.amazonaws.com/doc/2010-03-31/\">");
        sb.Append("<GetTopicAttributesResult><Attributes>");
        AppendAttribute(sb, "TopicArn", topic.TopicArn);
        AppendAttribute(sb, "DisplayName", topic.Name);
        AppendAttribute(sb, "SubscriptionsConfirmed", topic.Subscriptions.Count.ToString());
        AppendAttribute(sb, "SubscriptionsPending", "0");
        AppendAttribute(sb, "SubscriptionsDeleted", "0");
        AppendAttribute(sb, "Owner", _registry.AccountId);
        sb.Append("</Attributes></GetTopicAttributesResult>");
        sb.Append(BuildResponseMetadata());
        sb.Append("</GetTopicAttributesResponse>");

        context.Response.ContentType = "text/xml";
        await context.Response.WriteAsync(sb.ToString());
    }

    private async Task HandleSetTopicAttributesAsync(HttpContext context, System.Collections.Specialized.NameValueCollection form)
    {
        var topicArn = form["TopicArn"] ?? string.Empty;
        var topic = _registry.GetSnsTopic(topicArn);

        if (topic is null)
        {
            context.Response.StatusCode = 404;
            context.Response.ContentType = "text/xml";
            await context.Response.WriteAsync(BuildErrorResponse("NotFound", $"Topic not found: {topicArn}"));
            return;
        }

        LogHelpers.AddRequestLog(topic.RequestLog, "SetTopicAttributes", $"TopicArn={topicArn}");
        topic.NotifyChange();

        context.Response.ContentType = "text/xml";
        await context.Response.WriteAsync(BuildEmptyResponse("SetTopicAttributesResponse"));
    }

    private async Task HandleSubscribeAsync(HttpContext context, System.Collections.Specialized.NameValueCollection form)
    {
        var topicArn = form["TopicArn"] ?? string.Empty;
        var protocol = form["Protocol"] ?? string.Empty;
        var endpoint = form["Endpoint"] ?? string.Empty;

        var topic = _registry.GetSnsTopic(topicArn);
        if (topic is null)
        {
            context.Response.StatusCode = 404;
            context.Response.ContentType = "text/xml";
            await context.Response.WriteAsync(BuildErrorResponse("NotFound", $"Topic not found: {topicArn}"));
            return;
        }

        using var activity = KiteActivitySource.SNS.StartActivity("sns.subscribe");
        activity?.SetTag("topic.name", topic.Name);
        activity?.SetTag("topic.arn", topic.TopicArn);
        activity?.SetTag("protocol", protocol);
        activity?.SetTag("endpoint", endpoint);

        var subscriptionId = Guid.NewGuid().ToString();
        var subscriptionArn = $"{topicArn}:{subscriptionId}";

        var subscription = new SnsSubscription
        {
            SubscriptionArn = subscriptionArn,
            TopicArn = topicArn,
            Protocol = protocol,
            Endpoint = endpoint,
            Owner = _registry.AccountId
        };

        lock (topic.Subscriptions)
        {
            topic.Subscriptions.Add(subscription);
        }
        LogHelpers.AddRequestLog(topic.RequestLog, "Subscribe", $"Protocol={protocol} Endpoint={endpoint}");
        topic.NotifyChange();

        var sb = new StringBuilder();
        sb.Append("<SubscribeResponse xmlns=\"http://sns.amazonaws.com/doc/2010-03-31/\">");
        sb.Append("<SubscribeResult>");
        sb.Append($"<SubscriptionArn>{Escape(subscriptionArn)}</SubscriptionArn>");
        sb.Append("</SubscribeResult>");
        sb.Append(BuildResponseMetadata());
        sb.Append("</SubscribeResponse>");

        context.Response.ContentType = "text/xml";
        await context.Response.WriteAsync(sb.ToString());
    }

    private async Task HandleUnsubscribeAsync(HttpContext context, System.Collections.Specialized.NameValueCollection form)
    {
        var subscriptionArn = form["SubscriptionArn"] ?? string.Empty;

        using var activity = KiteActivitySource.SNS.StartActivity("sns.unsubscribe");
        activity?.SetTag("subscription.arn", subscriptionArn);

        foreach (var topic in _registry.GetAllSnsTopics())
        {
            SnsSubscription? sub;
            lock (topic.Subscriptions)
            {
                sub = topic.Subscriptions.FirstOrDefault(s => s.SubscriptionArn == subscriptionArn);
                if (sub is not null)
                    topic.Subscriptions.Remove(sub);
            }
            if (sub is not null)
            {
                activity?.SetTag("topic.name", topic.Name);
                activity?.SetTag("topic.arn", topic.TopicArn);
                LogHelpers.AddRequestLog(topic.RequestLog, "Unsubscribe", $"SubscriptionArn={subscriptionArn}");
                topic.NotifyChange();
                break;
            }
        }

        context.Response.ContentType = "text/xml";
        await context.Response.WriteAsync(BuildEmptyResponse("UnsubscribeResponse"));
    }

    private async Task HandlePublishAsync(HttpContext context, System.Collections.Specialized.NameValueCollection form)
    {
        var topicArn = form["TopicArn"] ?? string.Empty;
        var message = form["Message"] ?? string.Empty;
        var subject = form["Subject"] ?? string.Empty;

        var topic = _registry.GetSnsTopic(topicArn);
        if (topic is null)
        {
            context.Response.StatusCode = 404;
            context.Response.ContentType = "text/xml";
            await context.Response.WriteAsync(BuildErrorResponse("NotFound", $"Topic not found: {topicArn}"));
            return;
        }

        using var activity = KiteActivitySource.SNS.StartActivity("sns.publish");
        activity?.SetTag("topic.name", topic.Name);
        activity?.SetTag("topic.arn", topic.TopicArn);
        activity?.SetTag("message.subject", subject);
        activity?.SetTag("subscribers.count", topic.Subscriptions.Count);

        var messageId = Guid.NewGuid().ToString();
        LogHelpers.AddSentMessage(topic.PublishedMessages, messageId, message, "SDK");
        LogHelpers.AddRequestLog(topic.RequestLog, "Publish", $"MessageId={messageId} Subject={subject}");
        topic.NotifyChange();

        var sb = new StringBuilder();
        sb.Append("<PublishResponse xmlns=\"http://sns.amazonaws.com/doc/2010-03-31/\">");
        sb.Append("<PublishResult>");
        sb.Append($"<MessageId>{messageId}</MessageId>");
        sb.Append("</PublishResult>");
        sb.Append(BuildResponseMetadata());
        sb.Append("</PublishResponse>");

        context.Response.ContentType = "text/xml";
        await context.Response.WriteAsync(sb.ToString());
    }

    private async Task HandleListSubscriptionsAsync(HttpContext context)
    {
        var allSubscriptions = _registry.GetAllSnsTopics()
            .SelectMany(t => { lock (t.Subscriptions) { return t.Subscriptions.ToList(); } })
            .ToList();

        var sb = new StringBuilder();
        sb.Append("<ListSubscriptionsResponse xmlns=\"http://sns.amazonaws.com/doc/2010-03-31/\">");
        sb.Append("<ListSubscriptionsResult><Subscriptions>");
        foreach (var sub in allSubscriptions)
            AppendSubscriptionMember(sb, sub);
        sb.Append("</Subscriptions></ListSubscriptionsResult>");
        sb.Append(BuildResponseMetadata());
        sb.Append("</ListSubscriptionsResponse>");

        context.Response.ContentType = "text/xml";
        await context.Response.WriteAsync(sb.ToString());
    }

    private async Task HandleListSubscriptionsByTopicAsync(HttpContext context, System.Collections.Specialized.NameValueCollection form)
    {
        var topicArn = form["TopicArn"] ?? string.Empty;
        var topic = _registry.GetSnsTopic(topicArn);

        if (topic is null)
        {
            context.Response.StatusCode = 404;
            context.Response.ContentType = "text/xml";
            await context.Response.WriteAsync(BuildErrorResponse("NotFound", $"Topic not found: {topicArn}"));
            return;
        }

        var sb = new StringBuilder();
        sb.Append("<ListSubscriptionsByTopicResponse xmlns=\"http://sns.amazonaws.com/doc/2010-03-31/\">");
        sb.Append("<ListSubscriptionsByTopicResult><Subscriptions>");
        List<SnsSubscription> subscriptions;
        lock (topic.Subscriptions)
            subscriptions = topic.Subscriptions.ToList();
        foreach (var sub in subscriptions)
            AppendSubscriptionMember(sb, sub);
        sb.Append("</Subscriptions></ListSubscriptionsByTopicResult>");
        sb.Append(BuildResponseMetadata());
        sb.Append("</ListSubscriptionsByTopicResponse>");

        context.Response.ContentType = "text/xml";
        await context.Response.WriteAsync(sb.ToString());
    }

    private string BuildTopicArn(string name) =>
        $"arn:aws:sns:{_registry.Region}:{_registry.AccountId}:{name}";

    private static string BuildCreateTopicResponse(string topicArn)
    {
        return $"<CreateTopicResponse xmlns=\"http://sns.amazonaws.com/doc/2010-03-31/\">" +
               $"<CreateTopicResult><TopicArn>{Escape(topicArn)}</TopicArn></CreateTopicResult>" +
               BuildResponseMetadata() +
               "</CreateTopicResponse>";
    }

    private static string BuildEmptyResponse(string elementName) =>
        $"<{elementName} xmlns=\"http://sns.amazonaws.com/doc/2010-03-31/\">" +
        BuildResponseMetadata() +
        $"</{elementName}>";

    private static string BuildErrorResponse(string code, string message) =>
        $"<ErrorResponse xmlns=\"http://sns.amazonaws.com/doc/2010-03-31/\">" +
        $"<Error><Type>Sender</Type><Code>{Escape(code)}</Code><Message>{Escape(message)}</Message></Error>" +
        BuildResponseMetadata() +
        "</ErrorResponse>";

    private static string BuildResponseMetadata() =>
        $"<ResponseMetadata><RequestId>{Guid.NewGuid()}</RequestId></ResponseMetadata>";

    private static void AppendAttribute(StringBuilder sb, string key, string value)
    {
        sb.Append("<entry>");
        sb.Append($"<key>{Escape(key)}</key>");
        sb.Append($"<value>{Escape(value)}</value>");
        sb.Append("</entry>");
    }

    private static void AppendSubscriptionMember(StringBuilder sb, SnsSubscription sub)
    {
        sb.Append("<member>");
        sb.Append($"<TopicArn>{Escape(sub.TopicArn)}</TopicArn>");
        sb.Append($"<Protocol>{Escape(sub.Protocol)}</Protocol>");
        sb.Append($"<Endpoint>{Escape(sub.Endpoint)}</Endpoint>");
        sb.Append($"<Owner>{Escape(sub.Owner)}</Owner>");
        sb.Append($"<SubscriptionArn>{Escape(sub.SubscriptionArn)}</SubscriptionArn>");
        sb.Append("</member>");
    }

    private static string Escape(string value) =>
        System.Security.SecurityElement.Escape(value) ?? value;

    private static string SanitizeForLog(string value) =>
        value.Replace('\n', '_').Replace('\r', '_');
}
