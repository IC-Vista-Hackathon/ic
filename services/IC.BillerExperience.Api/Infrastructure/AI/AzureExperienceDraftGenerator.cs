using System.Diagnostics;
using System.Text.Json;
using Azure.AI.OpenAI;
using IC.BillerExperience.Api.Domain;
using IC.BillerExperience.Api.Infrastructure;
using IC.BillerExperience.Contracts.V1.Onboarding;
using OpenAI.Chat;

namespace IC.BillerExperience.Api.Infrastructure.AI;

public sealed partial class AzureExperienceDraftGenerator(
    AzureOpenAIClient client,
    string deployment,
    ILogger<AzureExperienceDraftGenerator> logger) : IExperienceDraftGenerator
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public string Provider => "AzureAI";

    public async ValueTask<DraftGenerationResult> GenerateAsync(
        BillerRecord biller,
        ExperienceRecord current,
        IReadOnlyList<OnboardingChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        using var activity = BillerExperienceTelemetry.Source.StartActivity("model:azure-draft");
        activity?.SetTag("gen_ai.system", "azure.ai.openai");
        activity?.SetTag("gen_ai.request.model", deployment);
        activity?.SetTag("ic.biller_id", biller.Id);
        try
        {
            var prompt = JsonSerializer.Serialize(new
            {
                biller = new { biller.Name, biller.BillType, biller.PostalCode, biller.Website },
                current_definition = current.Definition,
                recent_messages = messages.TakeLast(12).Select(message => new { message.Role, message.Content })
            }, SerializerOptions);
            var schema = BinaryData.FromString(JsonSchema);
            var options = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    "biller_experience_draft",
                    schema,
                    jsonSchemaIsStrict: true)
            };
            var completion = await client.GetChatClient(deployment).CompleteChatAsync(
                [
                    new SystemChatMessage(SystemInstructions),
                    new UserChatMessage(prompt)
                ],
                options,
                cancellationToken);
            var json = completion.Value.Content.FirstOrDefault()?.Text
                ?? throw new InvalidOperationException("Azure AI returned no structured draft.");
            var result = JsonSerializer.Deserialize<DraftGenerationResult>(json, SerializerOptions)
                ?? throw new InvalidOperationException("Azure AI returned an invalid structured draft.");
            BillerExperienceTelemetry.ModelCalls.Add(1, new("provider", Provider), new("outcome", "success"));
            return result;
        }
        catch (Exception exception)
        {
            LogGenerationError(logger, biller.Id, deployment, activity?.TraceId.ToString(), exception);
            BillerExperienceTelemetry.ModelCalls.Add(1, new("provider", Provider), new("outcome", "error"));
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            throw;
        }
        finally
        {
            BillerExperienceTelemetry.ModelDuration.Record(
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                new KeyValuePair<string, object?>("provider", Provider));
        }
    }

    private const string SystemInstructions = """
        You configure a biller payment experience. Return only the requested structured object.
        Preserve known values unless the biller explicitly changes them. Never create payment
        credentials, executable code, legal conclusions, or Kubernetes content. Compliance output
        is reviewable guidance only. AutoPay and paperless enrollment must remain separate,
        optional payer choices. Use only the supported payment capabilities already present.
        """;

    private const string JsonSchema = """
        {
          "type":"object",
          "additionalProperties":false,
          "required":["reply","definition","missingFields","findings"],
          "properties":{
            "reply":{"type":"string"},
            "missingFields":{"type":"array","items":{"type":"string"}},
            "findings":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["code","message","severity","requiresReview"],"properties":{"code":{"type":"string"},"message":{"type":"string"},"severity":{"type":"integer","minimum":0,"maximum":2},"requiresReview":{"type":"boolean"}}}},
            "definition":{"type":"object","additionalProperties":false,"required":["schemaVersion","billerId","brand","content","pwa","enabledPaymentCapabilities"],"properties":{
              "schemaVersion":{"type":"string"},"billerId":{"type":"string"},
              "enabledPaymentCapabilities":{"type":"array","items":{"type":"string"}},
              "brand":{"type":"object","additionalProperties":false,"required":["displayName","primaryColor","secondaryColor","logoAssetId","fontFamily"],"properties":{"displayName":{"type":"string"},"primaryColor":{"type":"string"},"secondaryColor":{"type":"string"},"logoAssetId":{"type":["string","null"]},"fontFamily":{"type":["string","null"]}}},
              "content":{"type":"object","additionalProperties":false,"required":["heading","introduction","supportText","privacyPolicyUrl","termsOfServiceUrl"],"properties":{"heading":{"type":"string"},"introduction":{"type":"string"},"supportText":{"type":"string"},"privacyPolicyUrl":{"type":"string","format":"uri"},"termsOfServiceUrl":{"type":"string","format":"uri"}}},
              "pwa":{"type":"object","additionalProperties":false,"required":["name","shortName","themeColor","backgroundColor","iconAssetId"],"properties":{"name":{"type":"string"},"shortName":{"type":"string"},"themeColor":{"type":"string"},"backgroundColor":{"type":"string"},"iconAssetId":{"type":["string","null"]}}}
            }}
          }
        }
        """;

    [LoggerMessage(2201, LogLevel.Error,
        "Azure AI draft generation failed for biller {BillerId}, deployment {Deployment}; trace {TraceId}")]
    private static partial void LogGenerationError(
        ILogger logger,
        string billerId,
        string deployment,
        string? traceId,
        Exception exception);
}
