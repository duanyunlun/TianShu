namespace TianShu.Cli;

internal static class CompletionCommandRunner
{
    private static readonly string[] RootCommands =
    [
        "completion",
        "send",
        "follow-up",
        "chat",
        "resume",
        "fork",
        "thread",
        "agent",
        "review",
        "rpc",
        "model-route",
        "skills",
        "plugin",
        "app",
        "config",
        "command",
        "exec",
        "e",
        "code-mode",
        "features",
        "experimental-feature",
        "mode",
        "collaboration-mode",
        "app-server",
        "mcp",
        "mcp-server",
        "conversation-summary",
        "summary",
        "git-diff",
        "gitdiff",
        "fuzzy-file-search",
        "fuzzy",
        "feedback",
        "windows-sandbox",
        "sandbox",
        "realtime",
        "debug",
    ];

    private static readonly string[] CommonFlags =
    [
        "--help",
        "-h",
        "--cwd",
        "--apphost-project",
        "--config",
        "--config-file",
        "-c",
        "--profile",
        "--resume-thread-id",
        "--resume-latest",
        "--resume-latest-any-cwd",
        "--collaboration-mode",
        "--web-search",
        "--enable",
        "--disable",
        "--dynamic-tools-json",
        "--dynamic-tools-file",
        "--json",
    ];

    public static int Run(CompletionCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Console.Write(GenerateScript(options));
        return 0;
    }

    internal static string GenerateScript(CompletionCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Shell switch
        {
            CompletionShellKind.Bash => BuildBashScript(),
            CompletionShellKind.Zsh => BuildZshScript(),
            CompletionShellKind.Fish => BuildFishScript(),
            CompletionShellKind.PowerShell => BuildPowerShellScript(),
            _ => throw new InvalidOperationException($"Unsupported completion shell: {options.Shell}"),
        };
    }

    private static string BuildBashScript()
    {
        var commands = string.Join(' ', RootCommands);
        var commonFlags = string.Join(' ', CommonFlags);
        return $$"""
            # TianShu CLI bash completion
            _tianshu_completion() {
              local cur prev words cword
              if declare -F _init_completion >/dev/null 2>&1; then
                _init_completion -n : || return
              else
                COMPREPLY=()
                cur="${COMP_WORDS[COMP_CWORD]}"
                prev="${COMP_WORDS[COMP_CWORD-1]}"
                words=("${COMP_WORDS[@]}")
                cword=$COMP_CWORD
              fi

              local commands="{{commands}}"
              local common_flags="{{commonFlags}}"

              if [[ $cword -eq 1 ]]; then
                COMPREPLY=( $(compgen -W "$commands $common_flags" -- "$cur") )
                return
              fi

              case "${words[1]}" in
                completion)
                  COMPREPLY=( $(compgen -W "bash zsh fish powershell" -- "$cur") )
                  return
                  ;;
                mcp)
                  COMPREPLY=( $(compgen -W "list get add remove --json --cwd --config-file -c" -- "$cur") )
                  return
                  ;;
                features)
                  COMPREPLY=( $(compgen -W "list enable disable" -- "$cur") )
                  return
                  ;;
                app-server)
                  COMPREPLY=( $(compgen -W "--listen --config-file -c --cwd --apphost-project" -- "$cur") )
                  return
                  ;;
                thread)
                  COMPREPLY=( $(compgen -W "list start fork archive rename resume loaded-list compact clean-background-terminals unsubscribe increment-elicitation decrement-elicitation read unarchive metadata rollback" -- "$cur") )
                  return
                  ;;
                realtime)
                  COMPREPLY=( $(compgen -W "start append-text append-audio handoff-output stop" -- "$cur") )
                  return
                  ;;
                debug)
                  COMPREPLY=( $(compgen -W "clear-memories" -- "$cur") )
                  return
                  ;;
              esac

              COMPREPLY=( $(compgen -W "$common_flags" -- "$cur") )
            }

            local completion_command="$TIANSHU_COMPLETION_COMMAND"
            if [[ -z "$completion_command" ]]; then
              completion_command="tianshu"
            fi
            complete -F _tianshu_completion "$completion_command"
            """;
    }

    private static string BuildZshScript()
    {
        var commands = string.Join(' ', RootCommands);
        var commonFlags = string.Join(' ', CommonFlags);
        return $$"""
            #compdef tianshu
            # TianShu CLI zsh completion

            local -a tianshu_commands
            tianshu_commands=({{commands}})

            local -a tianshu_common_flags
            tianshu_common_flags=({{commonFlags}})

            if (( CURRENT == 2 )); then
              _describe 'command' tianshu_commands
              _describe 'flag' tianshu_common_flags
              return
            fi

            case "${words[2]}" in
              completion)
                _values 'shell' bash zsh fish powershell
                ;;
              mcp)
                _values 'mcp command' list get add remove
                ;;
              features)
                _values 'features command' list enable disable
                ;;
              app-server)
                _values 'app-server option' --listen --config-file -c --cwd --apphost-project
                ;;
              thread)
                _values 'thread command' list start fork archive rename resume loaded-list compact clean-background-terminals unsubscribe increment-elicitation decrement-elicitation read unarchive metadata rollback
                ;;
              realtime)
                _values 'realtime command' start append-text append-audio handoff-output stop
                ;;
              debug)
                _values 'debug command' clear-memories
                ;;
              *)
                _describe 'flag' tianshu_common_flags
                ;;
            esac
            """;
    }

    private static string BuildFishScript()
    {
        return """
            # TianShu CLI fish completion
            set -l cmd tianshu
            if set -q TIANSHU_COMPLETION_COMMAND
              set cmd $TIANSHU_COMPLETION_COMMAND
            else if set -q TIANSHU_COMPLETION_COMMAND
              set cmd $TIANSHU_COMPLETION_COMMAND
            end

            complete -c $cmd -f
            complete -c $cmd -n '__fish_use_subcommand' -a 'completion send follow-up chat resume fork thread agent review rpc model-route skills plugin app config command exec e code-mode features experimental-feature mode collaboration-mode app-server mcp mcp-server conversation-summary summary git-diff gitdiff fuzzy-file-search fuzzy feedback windows-sandbox sandbox realtime debug'
            complete -c $cmd -n '__fish_use_subcommand' -l help
            complete -c $cmd -n '__fish_use_subcommand' -s h
            complete -c $cmd -n '__fish_seen_subcommand_from completion' -a 'bash zsh fish powershell'
            complete -c $cmd -n '__fish_seen_subcommand_from mcp' -a 'list get add remove'
            complete -c $cmd -n '__fish_seen_subcommand_from features' -a 'list enable disable'
            complete -c $cmd -n '__fish_seen_subcommand_from thread' -a 'list start fork archive rename resume loaded-list compact clean-background-terminals unsubscribe increment-elicitation decrement-elicitation read unarchive metadata rollback'
            complete -c $cmd -n '__fish_seen_subcommand_from realtime' -a 'start append-text append-audio handoff-output stop'
            complete -c $cmd -n '__fish_seen_subcommand_from debug' -a 'clear-memories'
            """;
    }

    private static string BuildPowerShellScript()
    {
        return """
            # TianShu CLI PowerShell completion
            $commandName = if ($env:TIANSHU_COMPLETION_COMMAND) { $env:TIANSHU_COMPLETION_COMMAND } elseif ($env:TIANSHU_COMPLETION_COMMAND) { $env:TIANSHU_COMPLETION_COMMAND } else { 'tianshu' }

            Register-ArgumentCompleter -CommandName $commandName -ScriptBlock {
                param($commandName, $wordToComplete, $cursorPosition, $commandAst, $fakeBoundParameters)

                $tokens = $commandAst.CommandElements | ForEach-Object { $_.Extent.Text }
                $root = @(
                    'completion','send','follow-up','chat','resume','fork','thread','agent','review','rpc','model-route','skills','plugin','app','config','command','exec','e','code-mode',
                    'features','experimental-feature','mode','collaboration-mode','app-server','mcp','mcp-server','conversation-summary','summary','git-diff','gitdiff',
                    'fuzzy-file-search','fuzzy','feedback','windows-sandbox','sandbox','realtime','debug',
                    '--help','-h','--cwd','--apphost-project','--config','--config-file','-c','--profile','--resume-thread-id','--resume-latest',
                    '--resume-latest-any-cwd','--collaboration-mode','--web-search','--enable','--disable','--dynamic-tools-json','--dynamic-tools-file','--json'
                )

                $candidates = if ($tokens.Count -le 2) {
                    $root
                }
                else {
                    switch ($tokens[1]) {
                        'completion' { @('bash', 'zsh', 'fish', 'powershell') }
                        'mcp' { @('list', 'get', 'add', 'remove') }
                        'features' { @('list', 'enable', 'disable') }
                        'thread' { @('list', 'start', 'fork', 'archive', 'rename', 'resume', 'loaded-list', 'compact', 'clean-background-terminals', 'unsubscribe', 'increment-elicitation', 'decrement-elicitation', 'read', 'unarchive', 'metadata', 'rollback') }
                        'realtime' { @('start', 'append-text', 'append-audio', 'handoff-output', 'stop') }
                        'debug' { @('clear-memories') }
                        default { @('--help', '-h', '--cwd', '--apphost-project', '--config', '--config-file', '-c', '--profile', '--resume-thread-id', '--resume-latest', '--resume-latest-any-cwd', '--collaboration-mode', '--web-search', '--enable', '--disable', '--dynamic-tools-json', '--dynamic-tools-file', '--json') }
                    }
                }

                $candidates |
                    Where-Object { $_ -like "$wordToComplete*" } |
                    ForEach-Object { [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_) }
            }
            """;
    }
}
