using System.ComponentModel;

using InterviewCoach.Memory;

using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace InterviewCoach.Mcp.Memory;

/// <summary>Episodic memory write path: the interview session record that the coaching agents maintain.</summary>
[McpServerToolType]
public class InterviewSessionTool(ISessionRepository sessions, ILogger<InterviewSessionTool> logger)
{
    [McpServerTool(Name = "add_interview_session", Title = "Add an interview session")]
    [Description("Creates a new interview session in the database. Use this after get_interview_session returns no record; update_interview_session cannot create one.")]
    public async Task<InterviewSession> AddInterviewSessionAsync(
        [Description("The interview session details. Id must be the application-provided SessionId.")] InterviewSession record)
    {
        var result = await sessions.AddAsync(record);
        logger.LogInformation("Added interview session '{Id}'", result.Id);

        return result;
    }

    [McpServerTool(Name = "get_interview_session", Title = "Get an interview session", ReadOnly = true)]
    [Description("Gets an interview session by ID, including its transcript. Returns no record when the session has not been created.")]
    public async Task<InterviewSession?> GetInterviewSessionAsync(
        [Description("The ID of the interview session")] string id)
        => await sessions.GetAsync(id);

    [McpServerTool(Name = "update_interview_session", Title = "Update an interview session")]
    [Description("Updates an existing session: replaces the six document fields and APPENDS Transcript. CompanyName and RoleTitle are set when provided (take them from the job description). Fetch first, preserve ResumeLink, ResumeText, ProceedWithoutResume, JobDescriptionLink, JobDescriptionText and ProceedWithoutJobDescription, and send ONLY NEW transcript text. Never resend the stored transcript. This cannot create a record or set IsCompleted; use add_interview_session or complete_interview_session for those operations.")]
    public async Task<InterviewSession> UpdateInterviewSessionAsync(
        [Description("The interview session details")] InterviewSession record)
    {
        var updated = await sessions.UpdateAsync(record)
            ?? throw new McpException($"Interview session '{record.Id}' does not exist. Call add_interview_session before updating it.");
        logger.LogInformation("Updated interview session '{Id}'", record.Id);

        return updated;
    }

    [McpServerTool(Name = "complete_interview_session", Title = "Complete an interview session")]
    [Description("Marks an existing interview session as complete and returns the saved record with IsCompleted true. Call this after saving the summary; update_interview_session cannot mark completion. This does not create a missing session.")]
    public async Task<InterviewSession> CompleteInterviewSessionAsync(
        [Description("The exact application-provided session ID to complete")] string id)
    {
        var completed = await sessions.CompleteAsync(id)
            ?? throw new McpException($"Interview session '{id}' does not exist. Call add_interview_session before completing it.");
        logger.LogInformation("Completed interview session '{Id}'", id);

        return completed;
    }
}
