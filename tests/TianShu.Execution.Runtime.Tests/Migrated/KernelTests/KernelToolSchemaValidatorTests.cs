using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.Execution.Runtime.Tests;

public sealed class KernelToolSchemaValidatorTests
{
    [Fact]
    public void TryValidate_ShouldRejectAdditionalProperties_WhenSchemaDisallows()
    {
        var schema = ParseElement(
            """
            {
              "type": "object",
              "required": ["path"],
              "additionalProperties": false,
              "properties": {
                "path": { "type": "string" }
              }
            }
            """);
        var arguments = ParseElement(
            """
            {
              "path": "demo.txt",
              "unexpected": true
            }
            """);

        var valid = KernelToolSchemaValidator.TryValidate(schema, arguments, out var error);

        Assert.False(valid);
        Assert.Contains("未声明字段", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidate_ShouldValidateNestedObjectAndArrayItems()
    {
        var schema = ParseElement(
            """
            {
              "type": "object",
              "required": ["options", "files"],
              "properties": {
                "options": {
                  "type": "object",
                  "required": ["recursive"],
                  "properties": {
                    "recursive": { "type": "boolean" }
                  }
                },
                "files": {
                  "type": "array",
                  "minItems": 1,
                  "items": {
                    "type": "string",
                    "minLength": 2
                  }
                }
              }
            }
            """);
        var validArguments = ParseElement(
            """
            {
              "options": { "recursive": true },
              "files": ["ab", "cd"]
            }
            """);
        var invalidArguments = ParseElement(
            """
            {
              "options": { "recursive": true },
              "files": ["a"]
            }
            """);

        var valid = KernelToolSchemaValidator.TryValidate(schema, validArguments, out var validError);
        var invalid = KernelToolSchemaValidator.TryValidate(schema, invalidArguments, out var invalidError);

        Assert.True(valid, validError);
        Assert.False(invalid);
        Assert.Contains("长度不能小于", invalidError, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidate_ShouldValidateAnyOfAndOneOf()
    {
        var schema = ParseElement(
            """
            {
              "type": "object",
              "properties": {
                "selector": {
                  "anyOf": [
                    { "type": "string", "minLength": 3 },
                    { "type": "integer", "minimum": 10 }
                  ]
                },
                "target": {
                  "oneOf": [
                    { "type": "string" },
                    { "type": "object", "required": ["id"], "properties": { "id": { "type": "string" } } }
                  ]
                }
              }
            }
            """);
        var validArguments = ParseElement(
            """
            {
              "selector": 12,
              "target": { "id": "abc" }
            }
            """);
        var invalidArguments = ParseElement(
            """
            {
              "selector": 2,
              "target": 12
            }
            """);

        var valid = KernelToolSchemaValidator.TryValidate(schema, validArguments, out var validError);
        var invalid = KernelToolSchemaValidator.TryValidate(schema, invalidArguments, out var invalidError);

        Assert.True(valid, validError);
        Assert.False(invalid);
        Assert.Contains("anyOf", invalidError, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidate_ShouldValidateEnumPatternAndRange()
    {
        var schema = ParseElement(
            """
            {
              "type": "object",
              "required": ["mode", "name", "timeout"],
              "properties": {
                "mode": { "enum": ["safe", "fast"] },
                "name": { "type": "string", "pattern": "^[a-z0-9_-]+$" },
                "timeout": { "type": "integer", "minimum": 1, "maximum": 30 }
              }
            }
            """);
        var validArguments = ParseElement(
            """
            {
              "mode": "safe",
              "name": "agent_1",
              "timeout": 10
            }
            """);
        var invalidArguments = ParseElement(
            """
            {
              "mode": "unsafe",
              "name": "Bad Name",
              "timeout": 0
            }
            """);

        var valid = KernelToolSchemaValidator.TryValidate(schema, validArguments, out var validError);
        var invalid = KernelToolSchemaValidator.TryValidate(schema, invalidArguments, out var invalidError);

        Assert.True(valid, validError);
        Assert.False(invalid);
        Assert.Contains("enum", invalidError, StringComparison.Ordinal);
    }

    private static JsonElement ParseElement(string json)
    {
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}
