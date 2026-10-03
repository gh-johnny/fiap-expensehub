using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ExpenseHub.Api.Contracts.Responses;
using ExpenseHub.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ExpenseHub.Api.Documentation;

internal static class ApiDocumentation
{
    public static void Configure(OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, context, cancellationToken) =>
        {
            document.Info.Title = "ExpenseHub reimbursement API";
            document.Info.Version = "v1";
            document.Info.Description = "Register an account, login with useCookies=false, then paste the returned accessToken into Bearer authentication. "
                + "An Admin grants roles; login again after any role change. Employee reads own expenses, Approver reads Submitted, Finance reads Approved/Paid, Auditor reads all. "
                + "Roles combine; owners cannot approve, reject or pay their own expenses. Admin alone grants account administration access.";
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                Description = "Access token returned by POST /login?useCookies=false. Leave credentials and tokens out of shared examples.",
            };
            document.Tags = new HashSet<OpenApiTag>
            {
                new() { Name = "Identity" },
                new() { Name = "Admin" },
                new() { Name = "Expenses" },
                new() { Name = "History" },
                new() { Name = "System" },
            };
            return Task.CompletedTask;
        });

        options.AddOperationTransformer(async (operation, context, cancellationToken) =>
        {
            string route = context.Description.RelativePath ?? string.Empty;
            string method = context.Description.HttpMethod ?? "GET";
            operation.OperationId ??= method.ToLowerInvariant() + "_" + route.Replace("/", "_", StringComparison.Ordinal)
                .Replace("{", string.Empty, StringComparison.Ordinal).Replace("}", string.Empty, StringComparison.Ordinal);
            string tag = route.StartsWith("api/admin/", StringComparison.Ordinal) ? "Admin"
                : route.StartsWith("api/expenses", StringComparison.Ordinal) ? (route.EndsWith("/history", StringComparison.Ordinal) ? "History" : "Expenses")
                : route == "health" ? "System" : "Identity";
            operation.Tags = new HashSet<OpenApiTagReference> { new(tag, context.Document) };
            IList<object> metadata = context.Description.ActionDescriptor.EndpointMetadata;
            bool protectedOperation = metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any();
            operation.Security = new List<OpenApiSecurityRequirement>();
            if (protectedOperation)
            {
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = new List<string>(),
                });
                var errors = new List<int> { 401, 403, 500 };
                if (method != "GET")
                {
                    errors.Add(400);
                }

                if (route.Contains("{id}", StringComparison.Ordinal))
                {
                    errors.Add(404);
                    if (method != "GET")
                    {
                        errors.Add(409);
                    }
                }

                OpenApiSchema schema = await context.GetOrCreateSchemaAsync(typeof(ProblemDetails), cancellationToken: cancellationToken);
                operation.Responses ??= new OpenApiResponses();
                foreach (int status in errors)
                {
                    operation.Responses.TryAdd(status.ToString(CultureInfo.InvariantCulture), new OpenApiResponse
                    {
                        Description = "ProblemDetails containing status, code and traceId.",
                        Content = new Dictionary<string, OpenApiMediaType>
                        {
                            ["application/problem+json"] = new() { Schema = schema },
                        },
                    });
                }
            }

            if (operation.RequestBody?.Content?.TryGetValue("application/json", out OpenApiMediaType? body) == true)
            {
                if (route == "api/expenses" || (method == "PUT" && route.StartsWith("api/expenses/", StringComparison.Ordinal)))
                {
                    body.Examples = new Dictionary<string, IOpenApiExample>
                    {
                        ["valid"] = new OpenApiExample
                        {
                            Summary = "Single amount and current UTC date",
                            Value = JsonSerializer.SerializeToNode(new { description = "Client train ticket expense", amount = 25.50m, expenseDate = DateOnly.FromDateTime(DateTime.UtcNow) }),
                        },
                        ["invalid"] = new OpenApiExample
                        {
                            Summary = "Invalid description, amount and future date return 400",
                            Value = JsonSerializer.SerializeToNode(new { description = "short", amount = 0m, expenseDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1) }),
                        },
                    };
                }
                else if (route.EndsWith("/reject", StringComparison.Ordinal))
                {
                    body.Example = JsonSerializer.SerializeToNode(new { reason = "Receipt is missing" });
                }
            }
        });

        options.AddSchemaTransformer((schema, context, cancellationToken) =>
        {
            if (typeof(ProblemDetails).IsAssignableFrom(context.JsonTypeInfo.Type))
            {
                schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
                schema.Properties["code"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "Stable machine-readable error code." };
                schema.Properties["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "Request correlation identifier." };
            }

            if ((context.JsonTypeInfo.Type == typeof(decimal) || context.JsonTypeInfo.Type == typeof(decimal?)))
            {
                schema.Format = "decimal";
                schema.Description = "Exact decimal amount; a single value per expense.";
            }

            if (context.JsonTypeInfo.Type == typeof(ExpenseResponse) || context.JsonTypeInfo.Type == typeof(ExpenseHistoryResponse))
            {
                string property = context.JsonTypeInfo.Type == typeof(ExpenseResponse) ? "status" : "newStatus";
                if (schema.Properties?.TryGetValue(property, out IOpenApiSchema? value) == true && value is OpenApiSchema status)
                {
                    status.Enum = Enum.GetNames<ExpenseStatus>().Select(name => (JsonNode)JsonValue.Create(name)!).ToList();
                }
            }

            return Task.CompletedTask;
        });
    }
}
