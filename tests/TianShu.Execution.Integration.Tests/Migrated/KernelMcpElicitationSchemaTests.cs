using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.Execution.Integration.Tests;

public sealed class KernelMcpElicitationSchemaTests
{
    [Fact]
    public void NormalizeFormRequestedSchema_ShouldNormalizeSupportedSchema()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "properties": {
                "email": {
                  "type": "string",
                  "title": "Email",
                  "description": "Work email address",
                  "format": "email",
                  "default": "dev@example.com"
                },
                "count": {
                  "type": "integer",
                  "minimum": 1,
                  "maximum": 5,
                  "default": 3
                },
                "confirmed": {
                  "type": "boolean",
                  "default": true
                },
                "action": {
                  "type": "string",
                  "oneOf": [
                    { "const": "allow", "title": "Allow" },
                    { "const": "deny", "title": "Deny" }
                  ],
                  "default": "allow"
                },
                "legacyChoice": {
                  "type": "string",
                  "enum": ["allow", "deny"],
                  "enumNames": ["Allow", "Deny"],
                  "default": "allow"
                },
                "scopes": {
                  "type": "array",
                  "minItems": 1,
                  "items": {
                    "oneOf": [
                      { "const": "read", "title": "Read" },
                      { "const": "write", "title": "Write" }
                    ]
                  },
                  "default": ["read"]
                }
              },
              "required": ["email", "confirmed"]
            }
            """);

        var normalized = McpElicitationSchemaCodec.NormalizeFormRequestedSchema(document.RootElement.Clone());

        Assert.Equal("object", normalized.GetProperty("type").GetString());
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", normalized.GetProperty("$schema").GetString());

        var properties = normalized.GetProperty("properties");
        Assert.Equal("email", normalized.GetProperty("required")[0].GetString());
        Assert.Equal("confirmed", normalized.GetProperty("required")[1].GetString());

        var action = properties.GetProperty("action");
        Assert.Equal("string", action.GetProperty("type").GetString());
        Assert.True(action.TryGetProperty("oneOf", out var oneOf));
        Assert.Equal("allow", oneOf[0].GetProperty("const").GetString());

        var scopes = properties.GetProperty("scopes");
        Assert.Equal("array", scopes.GetProperty("type").GetString());
        Assert.True(scopes.GetProperty("items").TryGetProperty("anyOf", out var anyOf));
        Assert.False(scopes.GetProperty("items").TryGetProperty("oneOf", out _));
        Assert.Equal("read", anyOf[0].GetProperty("const").GetString());

        var legacyChoice = properties.GetProperty("legacyChoice");
        Assert.Equal("allow", legacyChoice.GetProperty("enum")[0].GetString());
        Assert.Equal("Allow", legacyChoice.GetProperty("enumNames")[0].GetString());
    }

    [Fact]
    public void NormalizeFormRequestedSchema_ShouldRejectUnsupportedPropertyType()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "type": "object",
              "properties": {
                "confirmed": {
                  "type": "object"
                }
              }
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(
            () => McpElicitationSchemaCodec.NormalizeFormRequestedSchema(document.RootElement.Clone()));

        Assert.Contains("requested_schema.properties.confirmed.type", ex.Message, StringComparison.Ordinal);
    }
}
