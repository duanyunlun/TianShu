using System.Reflection;
using System.Text.Json;

namespace TianShu.Cli.Tests;

internal static class ReflectionTestHelper
{
    public static Assembly LoadRequiredAssembly(string simpleName)
        => Assembly.Load(simpleName);

    public static Type GetRequiredType(Assembly assembly, string fullName)
        => assembly.GetType(fullName, throwOnError: true)!;

    public static object? GetProperty(object instance, string name)
    {
        var property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return property!.GetValue(instance);
    }

    public static void SetProperty(object instance, string name, object? value)
    {
        var property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property!.SetValue(instance, value);
    }

    public static object? GetField(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field!.GetValue(instance);
    }

    public static void SetField(object instance, string name, object? value)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(instance, value);
    }

    public static object? InvokeMethod(object? instance, string name, params object?[] args)
    {
        var type = instance is null ? throw new ArgumentNullException(nameof(instance)) : instance.GetType();
        var method = type
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SingleOrDefault(candidate => candidate.Name == name
                && candidate.GetParameters().Length == args.Length
                && ParametersMatch(candidate.GetParameters(), args));
        Assert.NotNull(method);
        return method!.Invoke(instance, args);
    }

    public static object? InvokeStaticMethod(Type type, string name, params object?[] args)
    {
        var method = type
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .SingleOrDefault(candidate => candidate.Name == name
                && candidate.GetParameters().Length == args.Length
                && ParametersMatch(candidate.GetParameters(), args));
        Assert.NotNull(method);
        return method!.Invoke(null, args);
    }

    public static async Task<object?> AwaitTaskResultAsync(object? taskLike)
    {
        Assert.NotNull(taskLike);
        var task = Assert.IsAssignableFrom<Task>(taskLike);
        await task.ConfigureAwait(false);

        var resultProperty = task.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public);
        return resultProperty?.GetValue(task);
    }

    public static JsonElement ParseJsonElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static bool ParametersMatch(IReadOnlyList<ParameterInfo> parameters, IReadOnlyList<object?> args)
    {
        for (var index = 0; index < parameters.Count; index++)
        {
            if (!ParameterMatches(parameters[index].ParameterType, args[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ParameterMatches(Type parameterType, object? arg)
    {
        if (arg is null)
        {
            return !parameterType.IsValueType || Nullable.GetUnderlyingType(parameterType) is not null;
        }

        return parameterType.IsInstanceOfType(arg);
    }
}
