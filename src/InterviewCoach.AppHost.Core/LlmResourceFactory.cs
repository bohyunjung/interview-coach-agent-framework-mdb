using Microsoft.Extensions.Configuration;

public static class LlmResourceFactory
{
    private const string DEFAULT_MODEL = "gpt-5-mini";
    private const string COPILOT_GITHUB_TOKEN_KEY = "COPILOT_GITHUB_TOKEN";
    private const string AGENT_MODE_KEY = "AgentMode";
    private const string LLM_PROVIDER_KEY = "LlmProvider";
    private const string SECTION_NAME_GITHUB_COPILOT = "GitHubCopilot";
    private const string TOKEN_KEY = "Token";
    private const string MODEL_KEY = "Model";
    private const string TOKEN_RESOURCE_NAME = "token";

    public static IResourceBuilder<ProjectResource> WithLlmReference(this IResourceBuilder<ProjectResource> source, IConfiguration config, IEnumerable<string> args)
    {
        var (provider, mode) = GetProviderAndAgentMode(config, args);

        source = provider switch
        {
            LlmProvider.GitHubCopilot => source.AddGitHubCopilotResource(config, provider, mode),
            _ => throw new NotSupportedException($"The specified LLM provider '{provider}' is not supported.")
        };

        return source;
    }

    internal static (LlmProvider provider, AgentMode mode) GetProviderAndAgentMode(IConfiguration config, IEnumerable<string> args)
    {
        var provider = Enum.TryParse<LlmProvider>(config[LLM_PROVIDER_KEY], ignoreCase: true, out var parsedProvider) ? parsedProvider : LlmProvider.Unknown;
        var mode = Enum.TryParse<AgentMode>(config[AGENT_MODE_KEY], ignoreCase: true, out var parsedMode) ? parsedMode : AgentMode.Unknown;
        var arguments = args.ToArray();
        for (var index = 0; index < arguments.Length; index++)
        {
            switch (arguments[index])
            {
                case "--provider":
                case "-p":
                    provider = Enum.TryParse<LlmProvider>(GetArgumentValue(arguments, ref index), ignoreCase: true, out var parsedArgProvider) ? parsedArgProvider : LlmProvider.Unknown;
                    break;
                case "--mode":
                case "-m":
                    mode = Enum.TryParse<AgentMode>(GetArgumentValue(arguments, ref index), ignoreCase: true, out var parsedArgMode) ? parsedArgMode : AgentMode.Unknown;
                    break;
            }
        }
        if (provider == LlmProvider.Unknown)
        {
            throw new InvalidOperationException($"Missing configuration: {LLM_PROVIDER_KEY}");
        }
        if (mode == AgentMode.Unknown)
        {
            throw new InvalidOperationException($"Missing configuration: {AGENT_MODE_KEY}");
        }

        return (provider, mode);
    }

    private static string GetArgumentValue(string[] arguments, ref int index)
    {
        if (++index >= arguments.Length)
        {
            throw new InvalidOperationException($"Missing value for command-line argument '{arguments[index - 1]}'.");
        }

        return arguments[index];
    }

    private static IResourceBuilder<ProjectResource> AddGitHubCopilotResource(this IResourceBuilder<ProjectResource> source, IConfiguration config, LlmProvider provider, AgentMode mode)
    {
        var github = config.GetSection(SECTION_NAME_GITHUB_COPILOT);
        var tokenValue = GetGitHubToken(config);
        var model = github[MODEL_KEY] ?? DEFAULT_MODEL;

        Console.WriteLine();
        Console.WriteLine($"\tLLM Provider: {provider}");
        Console.WriteLine($"\tModel: {model}");
        Console.WriteLine($"\tAgent Mode: {mode}");
        Console.WriteLine();

        source = source.WithEnvironment(AGENT_MODE_KEY, mode.ToString())
                       .WithEnvironment(LLM_PROVIDER_KEY, provider.ToString())
                       .WithEnvironment($"{SECTION_NAME_GITHUB_COPILOT}__{MODEL_KEY}", model);

        if (tokenValue is not null)
        {
            var token = source.ApplicationBuilder
                              .AddParameter(name: TOKEN_RESOURCE_NAME, value: tokenValue, secret: true);
            source = source.WithEnvironment(COPILOT_GITHUB_TOKEN_KEY, token);
        }

        return source;
    }

    internal static string? GetGitHubToken(IConfiguration config)
    {
        var token = config[$"{SECTION_NAME_GITHUB_COPILOT}:{TOKEN_KEY}"]
            ?? config[COPILOT_GITHUB_TOKEN_KEY];

        if (string.IsNullOrWhiteSpace(token) ||
            (token.StartsWith("{{", StringComparison.Ordinal) &&
             token.EndsWith("}}", StringComparison.Ordinal)))
        {
            return null;
        }

        return token;
    }
}
