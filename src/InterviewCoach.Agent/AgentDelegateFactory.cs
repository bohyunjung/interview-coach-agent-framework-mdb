using System.ComponentModel;
using System.Runtime.CompilerServices;

using GitHub.Copilot;
using GitHub.Copilot.Rpc;

using InterviewCoach.Agent;

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

using ModelContextProtocol.Client;

public enum AgentMode
{
    Single,
    HandOff
}

public enum LlmProvider
{
    GitHubCopilot
}

public static class AgentDelegateFactory
{
    internal const string ResponseLanguageInstructions = """
        Response language:
        - Follow the user's explicit response-language request when one is provided.
        - Otherwise, respond in the primary language of the user's own conversational messages.
        - Do not infer the user's preferred language from documents, URLs, quotations, or tool results.
        - For short or language-ambiguous messages, retain the response language established in the conversation.
        - Preserve the user's language preference across handoffs to other interview specialists.
        - Product names, code, and necessary source quotations may remain in their original language.
        - Apply these rules to user-facing responses, not tool names, structured arguments, or stored source documents.
        """;

    internal const string MemoryCoachInstructions = """
        You are the Memory Coach for an AI Interview Coach system.
        You answer questions about the user's past interviews and feedback, using ONLY the memory tools.

        Choose the tool by the question:
        - A specific company or a date ("when was my interview at X", "summarize the feedback from X"): find_interviews_by_company.
        - The latest interviews ("my last interview", "my last 3 interviews"): list_recent_interviews.
        - Feedback that repeats across recent interviews ("common feedback in my last 5 interviews"): get_recent_feedback_items
          with that interviewCount. Group items by theme yourself, then report each theme with the number of interviews and companies.
        - A skill or trait described in the user's own words ("which company liked my debugging?"): search_feedback.
          Check each hit's text and score and mention only hits that really match; say so when none do.
        - The overall picture, strengths and growth areas over time: get_user_profile.

        Quote dates and company names exactly as returned. Never invent interviews or feedback; when the tools return
        nothing relevant, say that nothing is stored. When done, or if the user wants to practise, hand off to "triage".
        Always maintain a supportive and encouraging tone.
        """;

    public static IHostedAgentBuilder AddAIAgent(this IHostApplicationBuilder builder, string name)
    {
        var provider = Enum.TryParse<LlmProvider>(builder.Configuration[Constants.LlmProvider], ignoreCase: true, out var parsedProvider)
                        ? parsedProvider
                        : throw new InvalidOperationException($"LLM provider not specified or invalid. Please set the '{Constants.LlmProvider}' configuration value.");
        var mode = Enum.TryParse<AgentMode>(builder.Configuration[Constants.AgentMode], ignoreCase: true, out var parsedMode)
                 ? parsedMode
                 : throw new InvalidOperationException($"Agent mode not specified or invalid. Please set the '{Constants.AgentMode}' configuration value.");

        var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger(nameof(AgentDelegateFactory));
        logger.LogInformation("Agent mode: {AgentMode}", mode);
        logger.LogInformation("LLM provider: {LlmProvider}", provider);

        IHostedAgentBuilder agentBuilder = mode switch
        {
            AgentMode.Single => builder.AddAIAgent(name, (sp, key) => CreateSingleAgent(sp, key).WithShortTermMemory(sp)),
            AgentMode.HandOff => builder.AddHandOffWorkflow(name, CreateHandOffWorkflow),
            _ => throw new NotSupportedException($"The specified agent mode '{mode}' is not supported.")
        };

        return agentBuilder;
    }

    private static IHostedAgentBuilder AddHandOffWorkflow(this IHostApplicationBuilder builder, string key, Func<IServiceProvider, string, Workflow> createWorkflowDelegate)
    {
        builder.AddWorkflow(key, createWorkflowDelegate);

        return builder.AddAIAgent(key, (sp, name) =>
        {
            var workflow = sp.GetRequiredKeyedService<Workflow>(key);

            return workflow.AsAIAgent(name: key)
                           .CreateFixedAgent()
                           .WithShortTermMemory(sp);
        });
    }

    private static AIAgent CreateProviderAgent(
        IServiceProvider services,
        string name,
        string description,
        string instructions,
        IList<AITool>? tools = null)
    {
        instructions = WithResponseLanguageInstructions(instructions);

        var config = services.GetRequiredService<IConfiguration>();
        var provider = Enum.TryParse<LlmProvider>(config[Constants.LlmProvider], ignoreCase: true, out var parsedProvider)
            ? parsedProvider
            : throw new InvalidOperationException($"LLM provider not specified or invalid. Please set the '{Constants.LlmProvider}' configuration value.");

        if (provider != LlmProvider.GitHubCopilot)
        {
            throw new NotSupportedException($"The specified LLM provider '{provider}' is not supported.");
        }

        var copilotClient = services.GetRequiredService<CopilotClient>();
        var model = config[Constants.GitHubCopilotModel];
        var baseAgent = CreateCopilotRunAgent(copilotClient, name, description, model, instructions, tools);

        // Handoff orchestration supplies transfer tools through ChatClientAgentRunOptions.
        // The Copilot adapter currently ignores run options, so recreate its lightweight
        // agent wrapper per invocation with the merged static and handoff tool set.
        return new AIAgentBuilder(baseAgent)
            .Use(
                runFunc: (messages, session, options, _, cancellationToken) =>
                    CreateCopilotRunAgent(
                        copilotClient,
                        name,
                        description,
                        model,
                        MergeCopilotInstructions(instructions, options),
                        MergeCopilotTools(tools, options))
                    .RunAsync(messages, session, cancellationToken: cancellationToken),
                runStreamingFunc: (messages, session, options, _, cancellationToken) =>
                    CreateCopilotRunAgent(
                        copilotClient,
                        name,
                        description,
                        model,
                        MergeCopilotInstructions(instructions, options),
                        MergeCopilotTools(tools, options))
                    .RunStreamingAsync(messages, session, cancellationToken: cancellationToken))
            .Build();
    }

    internal static string WithResponseLanguageInstructions(string instructions)
        => $"{instructions}{Environment.NewLine}{Environment.NewLine}{ResponseLanguageInstructions}";

    internal static AIAgent CreateCopilotRunAgent(
        CopilotClient client,
        string name,
        string description,
        string? model,
        string instructions,
        IList<AITool>? tools)
    {
        var agent = client.AsAIAgent(
            CreateCopilotSessionConfig(model, instructions, tools),
            ownsClient: false,
            id: name,
            name: name,
            description: description);

        var handoffToolNames = (tools ?? [])
            .Where(tool => tool is AIFunctionDeclaration and not AIFunction &&
                tool.Name.StartsWith(HandoffWorkflowBuilder.FunctionPrefix, StringComparison.Ordinal))
            .Select(tool => tool.Name)
            .ToHashSet(StringComparer.Ordinal);

        if (handoffToolNames.Count == 0)
        {
            return agent;
        }

        return new AIAgentBuilder(agent)
            .Use(
                runFunc: async (messages, session, options, inner, cancellationToken) =>
                {
                    var updates = new List<AgentResponseUpdate>();
                    await foreach (var update in PreserveCopilotHandoffAsync(
                        inner.RunStreamingAsync(messages, session, options, cancellationToken),
                        handoffToolNames,
                        cancellationToken))
                    {
                        updates.Add(update);
                    }

                    return updates.ToAgentResponse();
                },
                runStreamingFunc: (messages, session, options, inner, cancellationToken) =>
                    PreserveCopilotHandoffAsync(
                        inner.RunStreamingAsync(messages, session, options, cancellationToken),
                        handoffToolNames,
                        cancellationToken))
            .Build();
    }

    internal static async IAsyncEnumerable<AgentResponseUpdate> PreserveCopilotHandoffAsync(
        IAsyncEnumerable<AgentResponseUpdate> updates,
        IReadOnlySet<string> handoffToolNames,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var handoffRequested = false;
        await foreach (var update in updates.WithCancellation(cancellationToken))
        {
            // The workflow routes only unresolved transfer calls. Copilot executes our
            // placeholder locally, so leave its result and post-transfer reply out of the
            // workflow stream. Drain the SDK turn so its session is idle before reuse.
            if (handoffRequested)
            {
                continue;
            }

            handoffRequested = update.Role == ChatRole.Assistant &&
                update.Contents.OfType<FunctionCallContent>().Any(call =>
                    !string.IsNullOrWhiteSpace(call.CallId) && handoffToolNames.Contains(call.Name));

            yield return update;
        }
    }

    internal static SessionConfig CreateCopilotSessionConfig(
        string? model,
        string instructions,
        IList<AITool>? tools)
    {
        var copilotTools = ToCopilotTools(tools);

        return new SessionConfig
        {
            AvailableTools = copilotTools.Select(tool => $"custom:{tool.Name}").ToList(),
            Model = string.IsNullOrWhiteSpace(model) ? Constants.DefaultModel : model,
            OnPermissionRequest = PermissionHandler.ApproveAll,
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Append,
                Content = instructions,
            },
            Tools = copilotTools,
        };
    }

    internal static IList<AITool> MergeCopilotTools(IList<AITool>? configuredTools, AgentRunOptions? options)
    {
        var runTools = (options as ChatClientAgentRunOptions)?.ChatOptions?.Tools;

        return (configuredTools ?? [])
            .Concat(runTools ?? [])
            .DistinctBy(tool => tool.Name, StringComparer.Ordinal)
            .ToList();
    }

    internal static string MergeCopilotInstructions(string instructions, AgentRunOptions? options)
    {
        var runInstructions = (options as ChatClientAgentRunOptions)?.ChatOptions?.Instructions;

        return string.IsNullOrWhiteSpace(runInstructions)
            ? instructions
            : $"{instructions}{Environment.NewLine}{Environment.NewLine}{runInstructions}";
    }

    private static IList<AIFunctionDeclaration> ToCopilotTools(IList<AITool>? tools)
    {
        return (tools ?? [])
            .Select(ToCopilotTool)
            .ToList();
    }

    private static AIFunctionDeclaration ToCopilotTool(AITool tool)
    {
        if (tool is AIFunction function)
        {
            return function;
        }

        if (tool is AIFunctionDeclaration declaration &&
            declaration.Name.StartsWith(HandoffWorkflowBuilder.FunctionPrefix, StringComparison.Ordinal))
        {
            return AIFunctionFactory.Create(
                CompleteHandoff,
                new AIFunctionFactoryOptions
                {
                    Name = declaration.Name,
                    Description = declaration.Description,
                });
        }

        throw new InvalidOperationException($"Tool '{tool.Name}' cannot be executed by GitHub Copilot.");
    }

    private static string CompleteHandoff(
        [Description("The reason for the handoff")] string? reasonForHandoff)
    {
        return "Transferred.";
    }

    // ============================================================================
    // MODE 1: Single Agent
    // The original monolithic agent that handles the entire interview process.
    // It has access to all MCP tools (MarkItDown for document parsing and
    // Memory for session management and long-term recall) and follows a linear interview flow.
    // ============================================================================
    private static AIAgent CreateSingleAgent(IServiceProvider sp, string key)
    {
        var markitdown = sp.GetRequiredKeyedService<McpClient>("mcp-markitdown");
        var memory = sp.GetRequiredKeyedService<McpClient>("mcp-memory");

        var markitdownTools = markitdown.ListToolsAsync().GetAwaiter().GetResult();
        var memoryTools = memory.ListToolsAsync().GetAwaiter().GetResult();
        var sessionTools = MemoryToolSets.SessionTools(memoryTools);
        var recallTools = MemoryToolSets.RecallTools(memoryTools);
        var consolidationTools = MemoryToolSets.ConsolidationTools(memoryTools);

        var agent = CreateProviderAgent(
            services: sp,
            name: key,
            description: "Runs the complete interview coaching process.",
            instructions: """
                You are an AI Interview Coach designed to help users prepare for job interviews.
                You will guide them through the interview process, provide feedback, and help them improve their skills.
                You will be given a session Id to track the interview session progress.
                Use the provided tools to manage interview sessions, capture resume and job description, ask both behavioral and technical questions, analyze responses, and generate summaries.

                Here's the overall process you should follow:
                01. Start by calling get_interview_session with the application-provided SessionId.
                02. If no record is returned, call add_interview_session with that exact ID before any update.
                    update_interview_session cannot create a missing record. Report the session ID after lookup or creation succeeds.
                03. Once you have the session, then keep using this session record for all subsequent interactions. DO NOT create a new session again.
                04. Ask the user to provide their resume link or allow them to proceed without it. The user may provide the resume in text form if they prefer.
                05. Next, request the job description link or let them proceed without it. The user may provide the job description in text form if they prefer.
                06. Once you have the necessary information, update the session record with it, including CompanyName and RoleTitle taken from the job description when known.
                07. Once you have updated the session record with the information, begin the interview by asking behavioral questions first.
                08. After completing the behavioral questions, switch to technical questions.
                09. Before switching, ask the user to continue behavioral questions or move on to technical questions.
                10. The user may stop at any time. Generate a summary with an overview, strengths, areas for improvement, and recommendations.
                11. Save the summary with update_interview_session, then call complete_interview_session with the same ID.
                    Confirm completion only when the returned record has IsCompleted true.
                    Then consolidate long-term memory: call save_session_feedback with the summary and the feedback items
                    (see the item rules below), then call get_user_profile and save_user_profile with the merged profile.
                12. Record questions, answers, feedback and the summary as they occur. Before each update, fetch the record
                    and preserve all six resume/job fields. Set Transcript to ONLY the new text to append.
                    Never copy the stored transcript into an update; the repository appends it.
                    If a tool fails, report the failure and do not claim the change was saved.

                Feedback item rules: 3-8 items, one strength or one improvement point each. Kind is 'strength' or 'improvement',
                Topic is a short label, Text is one self-contained sentence with the concrete evidence from the interview.

                Past interviews: when the user asks about earlier interviews or feedback patterns, answer from memory with
                list_recent_interviews, find_interviews_by_company, get_recent_feedback_items, search_feedback and get_user_profile.
                Only state what the tools return; say so when nothing relevant is stored.

                Always maintain a supportive and encouraging tone.
                """,
            tools: [.. markitdownTools, .. sessionTools, .. recallTools, .. consolidationTools.Where(t => t.Name != "get_user_profile")]
        );

        return agent;
    }

    // ============================================================================
    // MODE 2: Multi-Agent Handoff
    // Splits the interview coach into 5 specialized agents connected via the
    // handoff orchestration pattern from Microsoft Agent Framework.
    //
    // Topology (sequential chain with Triage as fallback):
    //   User → Triage → Receptionist → BehaviouralInterviewer → TechnicalInterviewer → Summariser
    //          Triage ← (any specialist, for out-of-order requests)
    //
    // Each agent has scoped tools and focused instructions. Specialists hand
    // off directly to the next agent in sequence for the happy path, avoiding
    // a stateless Triage re-routing loop. Triage acts as the initial entry
    // point and fallback for out-of-order user requests.
    // ============================================================================
    private static Workflow CreateHandOffWorkflow(IServiceProvider sp, string key)
    {
        var markitdown = sp.GetRequiredKeyedService<McpClient>("mcp-markitdown");
        var memory = sp.GetRequiredKeyedService<McpClient>("mcp-memory");

        var markitdownTools = markitdown.ListToolsAsync().GetAwaiter().GetResult();
        var memoryTools = memory.ListToolsAsync().GetAwaiter().GetResult();
        var sessionTools = MemoryToolSets.SessionTools(memoryTools);
        var recallTools = MemoryToolSets.RecallTools(memoryTools);
        var consolidationTools = MemoryToolSets.ConsolidationTools(memoryTools);

        // --- Triage Agent ---
        // Routes user messages to the correct specialist. No tools — pure routing.
        // FIX: Made state-aware to prevent re-routing loops. The old instructions
        // were purely keyword-driven, causing Triage to repeatedly send users back
        // to the Receptionist when the original message mentioned "resume" or "job
        // description" — even after document intake was already complete.
        var triageAgent = CreateProviderAgent(
            services: sp,
            name: "triage",
            description: "Routes the conversation to the correct interview specialist.",
            instructions: """
                You are the Triage agent for an AI Interview Coach system.
                Your ONLY job is to analyze the conversation and hand off to the right specialist agent.
                You do NOT answer questions or conduct interviews yourself.

                IMPORTANT: Before routing, review the FULL conversation history to determine
                which phases have already been completed. Repeat an earlier phase only for
                changed input or an explicit user request. The interview follows this sequence:
                  1. Receptionist (session setup, document intake)
                  2. Behavioural Interviewer
                  3. Technical Interviewer
                  4. Summariser
                Questions about past interviews can come at any time and go to the Memory Coach.

                Routing rules (apply in order; the latest user request takes priority over earlier messages):
                - If the user asks about past interviews, earlier feedback, patterns across interviews, or their profile
                  (for example "when was my interview at X" or "what feedback do I get repeatedly"), hand off to "memory_coach".
                - If the user wants to stop or finish, hand off to "summariser" immediately.
                  Do not restart intake or ask another interview question.
                - If the latest message supplies a new or replacement resume or job description, hand off to
                  "receptionist" first, even if it also asks to begin practice. A URL alone is not parsed document text.
                - If the user explicitly requests a specific phase, honour that request.
                - If the user is answering the latest technical question, hand off to "technical_interviewer".
                - If the user is answering the latest behavioural question, hand off to "behavioural_interviewer".
                - If the receptionist has NOT yet collected the resume and job description
                  → hand off to "receptionist"
                - If document intake is complete and behavioural interview has NOT started
                  → hand off to "behavioural_interviewer"
                - If behavioural interview is complete and technical interview has NOT started
                  → hand off to "technical_interviewer"
                - If technical interview is complete
                  → hand off to "summariser"
                - If unclear, ask the user to clarify what they'd like to do.

                A specialist may return to you for changed input or an early finish.
                Read the handoff reason and apply the routing rules; a return does not always mean a phase is complete.

                Always be brief and supportive. Let the specialists do the detailed work.
                """);

        // --- Receptionist Agent ---
        // Handles session creation and document intake. Has all MCP tools.
        // FIX: Now hands off directly to behavioural_interviewer (next phase)
        // instead of back to Triage, to avoid the re-routing loop.
        var receptionistAgent = CreateProviderAgent(
            services: sp,
            name: "receptionist",
            description: "Sets up interview sessions and collects resumes and job descriptions.",
            instructions: """
                You are the Receptionist for an AI Interview Coach system.
                Your job is to set up the interview session and collect documents.

                Process:
                1. Call get_interview_session with the application-provided SessionId. If no record exists,
                   call add_interview_session with that exact ID before any update. An update cannot create a record.
                   Let the user know the session ID after lookup or creation succeeds.
                2. Ask the user to provide their resume (link or text). Use MarkItDown to parse document links into markdown.
                3. Ask the user to provide the job description (link or text). Use MarkItDown to parse document links into markdown.
                4. Save parsed or pasted text in ResumeText and JobDescriptionText; saving a URL alone is insufficient.
                   A failed fetch does not complete intake. Ask for corrected input or explicit permission to skip it.
                   Before each update, call get_interview_session and preserve all six resume/job fields.
                   Set Transcript to ONLY new text to append, and verify the returned document fields before handoff.
                   Also set CompanyName and RoleTitle from the job description when you can tell them.
                5. Once document intake is complete, let the user know and hand off directly to "behavioural_interviewer"
                   to begin the interview. Only hand off to "triage" if the user wants to do something unexpected.

                The user may choose to proceed without a resume or job description — that's fine.
                Always maintain a supportive and encouraging tone.
                """,
            tools: [.. markitdownTools, .. sessionTools]);

        // --- Behavioural Interviewer Agent ---
        // Conducts the behavioural part of the interview.
        // FIX: Now hands off directly to technical_interviewer (next phase)
        // instead of back to Triage.
        var behaviouralAgent = CreateProviderAgent(
            services: sp,
            name: "behavioural_interviewer",
            description: "Conducts the behavioural interview and provides feedback.",
            instructions: """
                You are the Behavioural Interviewer for an AI Interview Coach system.
                Your job is to conduct the behavioural part of the interview.

                Process:
                1. Fetch the interview session record to get the resume and job description context.
                2. Ask behavioural questions one at a time, tailored to the job description and resume.
                3. After each answer, provide constructive feedback and analysis.
                4. Before each update, call get_interview_session and preserve all six resume/job fields.
                   Set Transcript to ONLY the new questions, answers, and analysis to append; never resend old text.
                5. After a few questions (typically 3-5), ask if the user wants to continue or move on.
                6. When done, hand off directly to "technical_interviewer" to continue the interview.
                   Only hand off to "triage" if the user wants to do something unexpected.

                Use the STAR method (Situation, Task, Action, Result) to guide your questions.
                Always maintain a supportive and encouraging tone.
                """,
            tools: [.. sessionTools]);

        // --- Technical Interviewer Agent ---
        // Conducts the technical part of the interview.
        // FIX: Now hands off directly to summariser (next phase)
        // instead of back to Triage.
        var technicalAgent = CreateProviderAgent(
            services: sp,
            name: "technical_interviewer",
            description: "Conducts the technical interview and provides feedback.",
            instructions: """
                You are the Technical Interviewer for an AI Interview Coach system.
                Your job is to conduct the technical part of the interview.

                Process:
                1. Fetch the interview session record to get the resume and job description context.
                2. Ask technical questions one at a time, tailored to the skills in the job description and resume.
                3. After each answer, provide constructive feedback, correct any misconceptions, and suggest improvements.
                4. Before each update, call get_interview_session and preserve all six resume/job fields.
                   Set Transcript to ONLY the new questions, answers, and analysis to append; never resend old text.
                5. After a few questions (typically 3-5), ask if the user wants to continue or wrap up.
                6. When done, hand off directly to "summariser" to generate the interview summary.
                   Only hand off to "triage" if the user wants to do something unexpected.

                Focus on practical, real-world scenarios relevant to the job.
                Always maintain a supportive and encouraging tone.
                """,
            tools: [.. sessionTools]);

        // --- Summariser Agent ---
        // Generates the final interview summary.
        // Hands back to Triage only after summary — this is the end of the sequence.
        var summariserAgent = CreateProviderAgent(
            services: sp,
            name: "summariser",
            description: "Produces the final interview summary and recommendations.",
            instructions: """
                You are the Summariser for an AI Interview Coach system.
                Your job is to generate a comprehensive interview summary.

                Process:
                1. Fetch the interview session record to get the full transcript. Also review the latest user
                   message for a final answer that has not been saved yet, and include that new answer in the update.
                2. Generate a summary that includes:
                - Overview of the interview session
                - Key highlights and strong answers
                - Areas for improvement
                - Specific recommendations for the user
                - Overall readiness assessment
                3. Preserve all six resume/job fields and call update_interview_session with ONLY the new summary
                   in Transcript. The repository appends it; never send the stored transcript again.
                4. Call complete_interview_session with the same ID. Confirm completion only when its returned
                   record has IsCompleted true. Report tool failures honestly.
                5. Consolidate long-term memory. Call save_session_feedback with the session ID, the feedback summary,
                   and 3-8 feedback items. Each item is ONE strength or ONE improvement point: Kind is 'strength' or
                   'improvement', Topic is a short label, Text is one self-contained sentence with the concrete evidence.
                6. Call get_user_profile, merge this interview into it (reuse existing topic wording for the same theme,
                   add this session's ID as evidence, and update growth area Status: recurring, improving or resolved),
                   then call save_user_profile with the complete lists.
                7. Present the summary to the user.
                8. Hand off back to triage in case the user wants to do anything else.

                Always maintain a supportive and encouraging tone.
                """,
            tools: [.. sessionTools, .. consolidationTools]);

        // --- Memory Coach Agent ---
        // Answers questions about past interviews from long-term memory. Read-only tools.
        var memoryCoachAgent = CreateProviderAgent(
            services: sp,
            name: "memory_coach",
            description: "Answers questions about past interviews and long-term feedback patterns.",
            instructions: MemoryCoachInstructions,
            tools: [.. recallTools]);

        // Build the handoff workflow — sequential chain with Triage as fallback.
        // FIX: Changed from pure hub-and-spoke (every specialist → Triage → next)
        // to a sequential chain (Receptionist → Behavioural → Technical → Summariser).
        // This prevents the stateless Triage from re-routing to an already-completed
        // phase based on keywords in the original user message.
        // Each specialist can still fall back to Triage for out-of-order requests.
#pragma warning disable MAAIW001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

        var workflow = AgentWorkflowBuilder
                       .CreateHandoffBuilderWith(triageAgent)
                       .WithHandoffs(triageAgent, [receptionistAgent, behaviouralAgent, technicalAgent, summariserAgent, memoryCoachAgent])
                       .WithHandoffs(receptionistAgent, [behaviouralAgent, triageAgent])
                       .WithHandoffs(behaviouralAgent, [technicalAgent, triageAgent])
                       .WithHandoffs(technicalAgent, [summariserAgent, triageAgent])
                       .WithHandoff(summariserAgent, triageAgent)
                       .WithHandoff(memoryCoachAgent, triageAgent)
                       .Build();
#pragma warning restore MAAIW001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.


        return workflow.SetName(key);
    }

}
