using System.Text;

namespace TianShu.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;

        var parseResult = CliCommandParser.Parse(args);
        if (parseResult.ErrorMessage is not null)
        {
            Console.Error.WriteLine(parseResult.ErrorMessage);
            Console.Error.WriteLine();
            Console.Error.WriteLine(CliCommandParser.GetHelpText());
            return (int)SendCommandExitCode.InvalidArguments;
        }

        if (parseResult.ShowHelp || parseResult.Command is null)
        {
            Console.WriteLine(CliCommandParser.GetHelpText());
            return 0;
        }

        CliProviderAssemblyPreloader.TryLoadPackagedProviders();

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            if (ConsoleCancelInterceptor.TryHandle())
            {
                eventArgs.Cancel = true;
                return;
            }

            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            var commandRunner = new CliRuntimeCommandRunner();
            return parseResult.Command switch
            {
                CompletionCommandOptions completionOptions => CompletionCommandRunner.Run(completionOptions),
                InitCommandOptions initOptions => CliOnboardingCommandRunner.RunInit(initOptions),
                DoctorCommandOptions doctorOptions => await CliOnboardingCommandRunner.RunDoctorAsync(doctorOptions, cancellation.Token).ConfigureAwait(false),
                SendCommandOptions sendOptions => await RunSendAsync(sendOptions, cancellation.Token).ConfigureAwait(false),
                FollowUpCliCommandOptions followUpOptions => await new ConversationTurnCommandRunner().RunFollowUpAsync(followUpOptions, cancellation.Token).ConfigureAwait(false),
                ChatCommandOptions chatOptions => await new InteractiveChatRunner().RunAsync(chatOptions, cancellation.Token).ConfigureAwait(false),
                ThreadCommandOptions threadOptions => await commandRunner.RunThreadAsync(threadOptions, cancellation.Token).ConfigureAwait(false),
                RpcCommandOptions rpcOptions => await commandRunner.RunRpcAsync(rpcOptions, cancellation.Token).ConfigureAwait(false),
                ModelRouteDiagnosticCommandOptions modelRouteOptions => commandRunner.RunModelRouteDiagnostic(modelRouteOptions),
                RuntimeSurfaceCommandOptions runtimeSurfaceOptions => await commandRunner.RunRuntimeSurfaceAsync(runtimeSurfaceOptions, cancellation.Token).ConfigureAwait(false),
                CommandExecCommandOptions commandExecOptions => await commandRunner.RunCommandExecAsync(commandExecOptions, cancellation.Token).ConfigureAwait(false),
                ExecCommandOptions execOptions => await new ExecCommandRunner().RunAsync(execOptions, cancellation.Token).ConfigureAwait(false),
                CodeModeCommandOptions codeModeOptions => await commandRunner.RunCodeModeAsync(codeModeOptions, cancellation.Token).ConfigureAwait(false),
                FuzzyFileSearchCommandOptions fuzzyFileSearchOptions => await commandRunner.RunFuzzyFileSearchAsync(fuzzyFileSearchOptions, cancellation.Token).ConfigureAwait(false),
                FeedbackCommandOptions feedbackOptions => await commandRunner.RunFeedbackAsync(feedbackOptions, cancellation.Token).ConfigureAwait(false),
                AppServerCommandOptions appServerOptions => await new AppServerCommandRunner().RunAsync(appServerOptions, cancellation.Token).ConfigureAwait(false),
                McpCommandOptions mcpOptions => await commandRunner.RunMcpAsync(mcpOptions, cancellation.Token).ConfigureAwait(false),
                McpServerCommandOptions mcpServerOptions => await new McpServerCommandRunner().RunAsync(mcpServerOptions, cancellation.Token).ConfigureAwait(false),
                WindowsSandboxCommandOptions windowsSandboxOptions => await commandRunner.RunWindowsSandboxAsync(windowsSandboxOptions, cancellation.Token).ConfigureAwait(false),
                RealtimeCommandOptions realtimeOptions => await commandRunner.RunRealtimeAsync(realtimeOptions, cancellation.Token).ConfigureAwait(false),
                DebugCommandOptions debugOptions => await commandRunner.RunDebugAsync(debugOptions, cancellation.Token).ConfigureAwait(false),
                _ => (int)SendCommandExitCode.InvalidArguments,
            };
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or InvalidOperationException or FormatException or System.Text.Json.JsonException)
        {
            if (CliCommandFailureWriter.TryWriteStructuredFailure(parseResult.Command, ex))
            {
                return 1;
            }

            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            if (CliCommandFailureWriter.TryWriteStructuredFailure(parseResult.Command, ex))
            {
                return 1;
            }

            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<int> RunSendAsync(SendCommandOptions options, CancellationToken cancellationToken)
    {
        var runner = new SendCommandRunner();
        var result = await runner.RunAsync(options, cancellationToken).ConfigureAwait(false);
        if (options.OutputJson)
        {
            Console.WriteLine(result.SummaryJson);
        }
        else
        {
            Console.WriteLine(result.ConsoleSummary);
        }

        return (int)result.ExitCode;
    }
}




