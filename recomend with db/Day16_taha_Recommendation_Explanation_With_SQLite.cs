
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;

namespace HisabDo.AI.RecommendationExplanation
{
    // Day 16 - Aarti
    // AI Recommendation Explanation Layer + SQLite persistence.
    //
    // Golden rule:
    // 1) Recommendation facts come ONLY from the verified Recommendation Engine.
    // 2) This service explains verified facts; it does not calculate new financial facts.
    // 3) Expected benefits are included only when supplied and verified.
    // 4) LLM output should be grounded against the VerifiedRecommendation DTO.
    //
    // NuGet:
    //   dotnet add package Microsoft.Data.Sqlite

    public sealed class VerifiedRecommendation
    {
        public string RecommendationId { get; init; } = "";
        public string UserId { get; init; } = "";
        public string Category { get; init; } = "";
        public string Title { get; init; } = "";
        public string Reason { get; init; } = "";
        public string FinancialPattern { get; init; } = "";
        public string Priority { get; init; } = "Low";
        public string Severity { get; init; } = "Low";
        public string SuggestedAction { get; init; } = "";
        public string? ExpectedBenefit { get; init; }
        public bool ExpectedBenefitVerified { get; init; }
        public bool IsVerified { get; init; }
        public string? Limitation { get; init; }
    }

    public sealed class RecommendationExplanation
    {
        public string RecommendationId { get; init; } = "";
        public string UserId { get; init; } = "";
        public string Summary { get; init; } = "";
        public string WhyGenerated { get; init; } = "";
        public string PatternExplanation { get; init; } = "";
        public string PriorityExplanation { get; init; } = "";
        public string SeverityExplanation { get; init; } = "";
        public string Action { get; init; } = "";
        public string Benefit { get; init; } = "";
        public string Limitation { get; init; } = "";
        public bool Grounded { get; init; }
    }

    public interface IRecommendationExplanationAI
    {
        RecommendationExplanation Explain(VerifiedRecommendation recommendation);
    }

    public sealed class AartiRecommendationExplanationService : IRecommendationExplanationAI
    {
        public RecommendationExplanation Explain(VerifiedRecommendation r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));

            if (!r.IsVerified)
            {
                return new RecommendationExplanation
                {
                    RecommendationId = r.RecommendationId,
                    UserId = r.UserId,
                    Summary = "I cannot provide this recommendation because its source data is not verified.",
                    WhyGenerated = "The recommendation was not marked as verified by the backend.",
                    PatternExplanation = "No unsupported financial pattern is inferred.",
                    PriorityExplanation = "Not available.",
                    SeverityExplanation = "Not available.",
                    Action = "Please retry after verified recommendation data is available.",
                    Benefit = "No benefit is claimed.",
                    Limitation = "Recommendation data is not verified.",
                    Grounded = true
                };
            }

            var benefit = r.ExpectedBenefitVerified && !string.IsNullOrWhiteSpace(r.ExpectedBenefit)
                ? r.ExpectedBenefit!
                : "Expected benefit is not verified, so no specific benefit is claimed.";

            var limitation = string.IsNullOrWhiteSpace(r.Limitation)
                ? "This recommendation is based only on the verified data supplied by HisabDo."
                : r.Limitation!;

            return new RecommendationExplanation
            {
                RecommendationId = r.RecommendationId,
                UserId = r.UserId,
                Summary = $"Recommendation: {r.Title}.",
                WhyGenerated = $"Why this was generated: {r.Reason}",
                PatternExplanation = $"Financial pattern: {r.FinancialPattern}",
                PriorityExplanation = $"Priority: {r.Priority}. This indicates how prominently the recommendation should be surfaced based on the verified rule result.",
                SeverityExplanation = $"Severity: {r.Severity}. This describes the verified magnitude of the underlying condition.",
                Action = $"Suggested action: {r.SuggestedAction}",
                Benefit = benefit,
                Limitation = limitation,
                Grounded = true
            };
        }
    }

    public sealed class RecommendationPromptBuilder
    {
        public string BuildSystemPrompt() => """
You are Aarti, the explanation layer for HisabDo AI recommendations.

Use ONLY the verified recommendation data supplied by the backend.

Rules:
1. Never invent or change financial amounts, percentages, scores, dates, categories, priorities, severities or forecasts.
2. Never create a new recommendation that is not present in the verified input.
3. Explain why the recommendation was generated using the supplied Reason.
4. Explain the supplied financial pattern; do not infer unsupported causes.
5. Explain Priority and Severity exactly as supplied.
6. Give the supplied SuggestedAction in simple natural language.
7. Mention ExpectedBenefit only when ExpectedBenefitVerified=true.
8. If data is missing or insufficient, clearly say it is unavailable; never guess.
9. Do not claim fraud, theft, medical/legal certainty, or guaranteed financial outcomes.
10. Preserve user isolation: use only the current authenticated user's verified context.
11. State limitations when supplied.
12. Keep the response concise, clear and actionable.

Required output:
- Summary
- Why this was generated
- Financial pattern
- Priority
- Severity (when available)
- Action
- Expected benefit (only if verified)
- Limitation/uncertainty
""";
    }

    // SQLite repository. In production, replace this with the application's existing DbContext/repository.
    public sealed class RecommendationExplanationRepository
    {
        private readonly string _connectionString;

        public RecommendationExplanationRepository(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("Database path is required.", nameof(databasePath));

            _connectionString = $"Data Source={databasePath}";
        }

        public void Initialize()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS verified_recommendations (
                    recommendation_id TEXT PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    category TEXT NOT NULL,
                    title TEXT NOT NULL,
                    reason TEXT NOT NULL,
                    financial_pattern TEXT NOT NULL,
                    priority TEXT NOT NULL,
                    severity TEXT NOT NULL,
                    suggested_action TEXT NOT NULL,
                    expected_benefit TEXT NULL,
                    expected_benefit_verified INTEGER NOT NULL,
                    is_verified INTEGER NOT NULL,
                    limitation TEXT NULL,
                    created_at TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_verified_recommendations_user
                ON verified_recommendations(user_id);

                CREATE TABLE IF NOT EXISTS recommendation_explanations (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    recommendation_id TEXT NOT NULL,
                    user_id TEXT NOT NULL,
                    summary TEXT NOT NULL,
                    why_generated TEXT NOT NULL,
                    pattern_explanation TEXT NOT NULL,
                    priority_explanation TEXT NOT NULL,
                    severity_explanation TEXT NOT NULL,
                    action TEXT NOT NULL,
                    benefit TEXT NOT NULL,
                    limitation TEXT NOT NULL,
                    grounded INTEGER NOT NULL,
                    created_at TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_explanations_user
                ON recommendation_explanations(user_id);
                """;
            command.ExecuteNonQuery();
        }

        public void SaveVerifiedRecommendation(VerifiedRecommendation r)
        {
            if (!r.IsVerified)
                throw new InvalidOperationException("Only verified recommendations may be stored as verified input.");

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR REPLACE INTO verified_recommendations
                (recommendation_id,user_id,category,title,reason,financial_pattern,
                 priority,severity,suggested_action,expected_benefit,
                 expected_benefit_verified,is_verified,limitation,created_at)
                VALUES ($id,$user,$category,$title,$reason,$pattern,
                        $priority,$severity,$action,$benefit,
                        $benefitVerified,$verified,$limitation,$createdAt);
                """;

            command.Parameters.AddWithValue("$id", r.RecommendationId);
            command.Parameters.AddWithValue("$user", r.UserId);
            command.Parameters.AddWithValue("$category", r.Category);
            command.Parameters.AddWithValue("$title", r.Title);
            command.Parameters.AddWithValue("$reason", r.Reason);
            command.Parameters.AddWithValue("$pattern", r.FinancialPattern);
            command.Parameters.AddWithValue("$priority", r.Priority);
            command.Parameters.AddWithValue("$severity", r.Severity);
            command.Parameters.AddWithValue("$action", r.SuggestedAction);
            command.Parameters.AddWithValue("$benefit", (object?)r.ExpectedBenefit ?? DBNull.Value);
            command.Parameters.AddWithValue("$benefitVerified", r.ExpectedBenefitVerified ? 1 : 0);
            command.Parameters.AddWithValue("$verified", r.IsVerified ? 1 : 0);
            command.Parameters.AddWithValue("$limitation", (object?)r.Limitation ?? DBNull.Value);
            command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }

        public List<VerifiedRecommendation> GetVerifiedForUser(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("UserId is required.", nameof(userId));

            var result = new List<VerifiedRecommendation>();

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT recommendation_id,user_id,category,title,reason,financial_pattern,
                       priority,severity,suggested_action,expected_benefit,
                       expected_benefit_verified,is_verified,limitation
                FROM verified_recommendations
                WHERE user_id = $user AND is_verified = 1
                ORDER BY created_at DESC;
                """;
            command.Parameters.AddWithValue("$user", userId);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new VerifiedRecommendation
                {
                    RecommendationId = reader.GetString(0),
                    UserId = reader.GetString(1),
                    Category = reader.GetString(2),
                    Title = reader.GetString(3),
                    Reason = reader.GetString(4),
                    FinancialPattern = reader.GetString(5),
                    Priority = reader.GetString(6),
                    Severity = reader.GetString(7),
                    SuggestedAction = reader.GetString(8),
                    ExpectedBenefit = reader.IsDBNull(9) ? null : reader.GetString(9),
                    ExpectedBenefitVerified = reader.GetInt32(10) == 1,
                    IsVerified = reader.GetInt32(11) == 1,
                    Limitation = reader.IsDBNull(12) ? null : reader.GetString(12)
                });
            }

            return result;
        }

        public void SaveExplanation(RecommendationExplanation e)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO recommendation_explanations
                (recommendation_id,user_id,summary,why_generated,pattern_explanation,
                 priority_explanation,severity_explanation,action,benefit,
                 limitation,grounded,created_at)
                VALUES ($id,$user,$summary,$why,$pattern,$priority,$severity,
                        $action,$benefit,$limitation,$grounded,$createdAt);
                """;

            command.Parameters.AddWithValue("$id", e.RecommendationId);
            command.Parameters.AddWithValue("$user", e.UserId);
            command.Parameters.AddWithValue("$summary", e.Summary);
            command.Parameters.AddWithValue("$why", e.WhyGenerated);
            command.Parameters.AddWithValue("$pattern", e.PatternExplanation);
            command.Parameters.AddWithValue("$priority", e.PriorityExplanation);
            command.Parameters.AddWithValue("$severity", e.SeverityExplanation);
            command.Parameters.AddWithValue("$action", e.Action);
            command.Parameters.AddWithValue("$benefit", e.Benefit);
            command.Parameters.AddWithValue("$limitation", e.Limitation);
            command.Parameters.AddWithValue("$grounded", e.Grounded ? 1 : 0);
            command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    // End-to-end usage example:
    public static class Example
    {
        public static void Run()
        {
            var db = new RecommendationExplanationRepository("hisabdo_ai.db");
            db.Initialize();

            var verified = new VerifiedRecommendation
            {
                RecommendationId = "REC-001",
                UserId = "USER-123",
                Category = "SavingImprovement",
                Title = "Improve saving behavior",
                Reason = "Verified saving rate is 6%.",
                FinancialPattern = "Saving rate is below the configured MVP threshold.",
                Priority = "High",
                Severity = "High",
                SuggestedAction = "Review discretionary spending and identify realistic saving opportunities.",
                ExpectedBenefit = null,
                ExpectedBenefitVerified = false,
                IsVerified = true,
                Limitation = "This recommendation is based on the available verified financial results."
            };

            db.SaveVerifiedRecommendation(verified);

            var service = new AartiRecommendationExplanationService();
            var explanation = service.Explain(verified);
            db.SaveExplanation(explanation);

            // For a real LLM integration:
            // 1. Load verified recommendation for the authenticated user.
            // 2. Build the system prompt with RecommendationPromptBuilder.
            // 3. Send ONLY the verified DTO/context to the LLM.
            // 4. Validate the LLM response against the DTO before returning it.
        }
    }
}
