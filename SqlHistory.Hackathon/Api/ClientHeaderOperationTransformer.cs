using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SqlHistory.Hackathon.Api;

/// <summary>
/// Documents the header that <see cref="RequireClientHeaderFilter"/> enforces on every POST,
/// pre-filled so "Try it out" in Swagger UI works without extra steps.
/// </summary>
public sealed class ClientHeaderOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (!HttpMethods.IsPost(context.Description.HttpMethod ?? string.Empty))
            return Task.CompletedTask;

        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = RequireClientHeaderFilter.HeaderName,
            In = ParameterLocation.Header,
            Required = true,
            Description = "Any value. Required on state-changing requests so other websites cannot trigger them.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String, Default = JsonValue.Create("swagger") }
        });

        return Task.CompletedTask;
    }
}
