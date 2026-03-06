using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using SmileApi.Application.DTOs;
using SmileApi.Application.Interfaces;
using SmileApi.Domain.Entities;

namespace SmileApi.Infrastructure.Persistence;

public class SupabaseSmileScanRepository : ISmileScanRepository
{
    private readonly string _connectionString;
    private readonly ILogger<SupabaseSmileScanRepository> _logger;

    public SupabaseSmileScanRepository(IConfiguration configuration, ILogger<SupabaseSmileScanRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("SupabaseConnection")
                            ?? configuration["SUPABASE_CONNECTION_STRING"]
                            ?? throw new InvalidOperationException("Supabase connection string is not configured.");
        _logger = logger;
    }

    public async Task SaveAsync(SmileScan scan, AIUsageLog usageLog)
    {
        _logger.LogInformation("Saving scan {ScanId} to Supabase...", scan.Id);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            const string insertScanSql = @"
                INSERT INTO ""SmileScans"" (
                    ""Id"", ""UserId"", ""ExternalPatientId"", ""ImageUrl"", ""SmileScore"",
                    ""AlignmentScore"", ""GumHealthScore"", ""WhitenessScore"", ""SymmetryScore"",
                    ""PlaqueRiskLevel"", ""ConfidenceScore"", ""CarePlanActionsJson"", ""CreatedAt""
                ) VALUES (
                    @id, @userId, @patientId, @imageUrl, @smileScore,
                    @alignment, @gumHealth, @whiteness, @symmetry,
                    @plaqueRisk, @confidence, CAST(@carePlanActionsJson AS jsonb), @createdAt
                );";

            await using var command = new NpgsqlCommand(insertScanSql, connection, transaction);
            command.Parameters.AddWithValue("id", scan.Id);
            command.Parameters.AddWithValue("userId", scan.UserId.HasValue ? (object)scan.UserId.Value : DBNull.Value);
            command.Parameters.AddWithValue("patientId", scan.ExternalPatientId);
            command.Parameters.AddWithValue("imageUrl", scan.ImageUrl);
            command.Parameters.AddWithValue("smileScore", scan.SmileScore);
            command.Parameters.AddWithValue("alignment", scan.AlignmentScore);
            command.Parameters.AddWithValue("gumHealth", scan.GumHealthScore);
            command.Parameters.AddWithValue("whiteness", scan.WhitenessScore);
            command.Parameters.AddWithValue("symmetry", scan.SymmetryScore);
            command.Parameters.AddWithValue("plaqueRisk", scan.PlaqueRiskLevel);
            command.Parameters.AddWithValue("confidence", scan.ConfidenceScore);
            command.Parameters.AddWithValue("carePlanActionsJson", scan.CarePlanActionsJson ?? "[]");
            command.Parameters.AddWithValue("createdAt", scan.CreatedAt);

            await command.ExecuteNonQueryAsync();

            const string insertUsageLogSql = @"
                INSERT INTO ""AIUsageLogs"" (
                    ""Id"", ""ScanId"", ""UserId"", ""ExternalPatientId"", ""ModelUsed"", ""TokensUsed"", ""ProcessingTimeMs"", ""CostEstimate"", ""CreatedAt""
                ) VALUES (
                    @logId, @scanId, @userId, @patientId, @model, @tokens, @time, @cost, @createdAt
                );";

            await using var logCommand = new NpgsqlCommand(insertUsageLogSql, connection, transaction);
            logCommand.Parameters.AddWithValue("logId", usageLog.Id);
            logCommand.Parameters.AddWithValue("scanId", usageLog.ScanId);
            logCommand.Parameters.AddWithValue("userId", usageLog.UserId.HasValue ? (object)usageLog.UserId.Value : DBNull.Value);
            logCommand.Parameters.AddWithValue("patientId", usageLog.ExternalPatientId);
            logCommand.Parameters.AddWithValue("model", usageLog.ModelUsed);
            logCommand.Parameters.AddWithValue("tokens", usageLog.TokensUsed);
            logCommand.Parameters.AddWithValue("time", usageLog.ProcessingTimeMs);
            logCommand.Parameters.AddWithValue("cost", usageLog.CostEstimate);
            logCommand.Parameters.AddWithValue("createdAt", usageLog.CreatedAt);

            await logCommand.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
            _logger.LogInformation("Scan {ScanId} successfully saved.", scan.Id);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to save scan to database for patient {PatientId}", scan.ExternalPatientId);
            throw;
        }
    }

    public async Task<List<SmileScan>> GetScansAsync(string externalPatientId, Guid? userId = null)
    {
        _logger.LogInformation("Retrieving scans for patient {PatientId} (UserId={UserId}) from Supabase...", externalPatientId, userId);

        var scans = new List<SmileScan>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        string querySql = @"
            SELECT
                ""Id"", ""UserId"", ""ExternalPatientId"", ""ImageUrl"", ""SmileScore"",
                ""AlignmentScore"", ""GumHealthScore"", ""WhitenessScore"", ""SymmetryScore"",
                ""PlaqueRiskLevel"", ""ConfidenceScore"", ""CarePlanActionsJson"", ""CreatedAt""
            FROM ""SmileScans""
            WHERE (@userId IS NOT NULL AND ""UserId"" = @userId)
               OR (@userId IS NULL AND ""ExternalPatientId"" = @patientId AND ""UserId"" IS NULL)
            ORDER BY ""CreatedAt"" DESC;";

        await using var command = new NpgsqlCommand(querySql, connection);
        var patientIdParam = new NpgsqlParameter("patientId", NpgsqlTypes.NpgsqlDbType.Text);
        patientIdParam.Value = externalPatientId;
        command.Parameters.Add(patientIdParam);
        var userIdParam = new NpgsqlParameter("userId", NpgsqlTypes.NpgsqlDbType.Uuid);
        userIdParam.Value = userId.HasValue ? (object)userId.Value : DBNull.Value;
        command.Parameters.Add(userIdParam);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            scans.Add(new SmileScan
            {
                Id = reader.GetGuid(0),
                UserId = reader.IsDBNull(1) ? null : reader.GetGuid(1),
                ExternalPatientId = reader.GetString(2),
                ImageUrl = reader.GetString(3),
                SmileScore = reader.GetInt32(4),
                AlignmentScore = reader.GetInt32(5),
                GumHealthScore = reader.GetInt32(6),
                WhitenessScore = reader.GetInt32(7),
                SymmetryScore = reader.GetInt32(8),
                PlaqueRiskLevel = reader.GetString(9),
                ConfidenceScore = reader.GetDouble(10),
                CarePlanActionsJson = reader.GetString(11),
                CreatedAt = reader.GetDateTime(12)
            });
        }

        return scans;
    }

    public async Task<ObservabilityStatsDto> GetObservabilityStatsAsync(Guid? userId = null)
    {
        _logger.LogInformation("Retrieving observability stats for user {UserId} from AIUsageLogs...", userId);

        var stats = new ObservabilityStatsDto();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        const string statsSql = @"
            SELECT
                COUNT(*)::int AS total_scans,
                COALESCE(AVG(""ProcessingTimeMs""), 0) AS avg_processing_time,
                COALESCE(SUM(""TokensUsed""), 0)::bigint AS total_tokens,
                COALESCE(AVG(""TokensUsed""), 0) AS avg_tokens_per_scan,
                COALESCE(SUM(""CostEstimate""), 0) AS total_cost,
                COALESCE(AVG(""CostEstimate""), 0) AS avg_cost_per_scan,
                COALESCE(SUM(CASE WHEN ""TokensUsed"" = 0 THEN 1 ELSE 0 END), 0)::int AS failed_scans
            FROM ""AIUsageLogs""
            WHERE (@userId IS NULL AND ""UserId"" IS NULL) OR (@userId IS NOT NULL AND ""UserId"" = @userId);";

        await using var command = new NpgsqlCommand(statsSql, connection);
        var userIdParam = new NpgsqlParameter("userId", NpgsqlTypes.NpgsqlDbType.Uuid);
        userIdParam.Value = userId.HasValue ? (object)userId.Value : DBNull.Value;
        command.Parameters.Add(userIdParam);

        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            stats.TotalScans = reader.GetInt32(0);
            stats.AverageProcessingTimeMs = reader.GetDouble(1);
            stats.TotalTokensUsed = reader.GetInt64(2);
            stats.AverageTokensPerScan = reader.GetDouble(3);
            stats.TotalCost = reader.GetDecimal(4);
            stats.AverageCostPerScan = reader.GetDecimal(5);
            stats.FailedScans = reader.GetInt32(6);
            stats.FailureRate = stats.TotalScans > 0
                ? Math.Round((double)stats.FailedScans / stats.TotalScans * 100, 2)
                : 0;
        }

        return stats;
    }
}
