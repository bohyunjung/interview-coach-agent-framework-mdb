namespace InterviewCoach.Memory;

public static class MemoryConstants
{
    public const string ConnectionStringName = "mongodb";
    public const string DefaultDatabase = "interview_coach";

    // Short-term memory: every chat turn, written mechanically by the app.
    public const string ChatMessages = "chat_messages";
    // Episodic memory: one document per interview session.
    public const string Sessions = "sessions";
    // Semantic memory (session): feedback items extracted from a session, embedded for vector search.
    public const string FeedbackItems = "feedback_items";
    // Semantic memory (profile): the single user's strengths and growth areas.
    public const string UserProfile = "user_profile";

    public const string ProfileId = "default";
    public const string VectorIndex = "feedback_vector_index";
    public const string VectorPath = "embedding";

    public const string KindStrength = "strength";
    public const string KindImprovement = "improvement";
}
