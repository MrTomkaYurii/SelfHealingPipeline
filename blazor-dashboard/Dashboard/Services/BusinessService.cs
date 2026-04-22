using System.Text;
using System.Text.Json;
using Dashboard.Client.Models;
using DuckDB.NET.Data;

namespace Dashboard.Services;

public record ReviewDto(
    string ReviewId,
    string BusinessId,
    string UserId,
    int    Stars,
    string Text,
    string Date,
    int    Useful,
    int    Funny,
    int    Cool
);

public class BusinessService
{
    private static readonly string DbPath =
        @"C:\Git\SelfHealingPipeline\airflow-pipeline\input\yelp.duckdb";
    private static readonly string OutputPath =
        @"C:\Git\SelfHealingPipeline\airflow-pipeline\input";

    private static readonly JsonSerializerOptions NdjsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task<List<BusinessDto>> GetBusinessesAsync(
        string? search = null, int limit = 100, CancellationToken ct = default)
    {
        EnsureDbExists();
        return await Task.Run(() =>
        {
            var list = new List<BusinessDto>();
            using var conn = OpenConnection();
            using var cmd  = conn.CreateCommand();

            var hasSearch = !string.IsNullOrWhiteSpace(search);
            cmd.CommandText = $"""
                SELECT business_id, name, city, state, stars, review_count, categories
                FROM business
                {(hasSearch ? "WHERE name ILIKE $q OR city ILIKE $q" : "")}
                ORDER BY review_count DESC
                LIMIT {limit}
                """;

            if (hasSearch)
                cmd.Parameters.Add(new DuckDBParameter("q", $"%{search}%"));

            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                ct.ThrowIfCancellationRequested();
                list.Add(new BusinessDto(
                    BusinessId:  Safe<string>(rdr, 0, ""),
                    Name:        Safe<string>(rdr, 1, ""),
                    City:        Safe<string>(rdr, 2, ""),
                    State:       Safe<string>(rdr, 3, ""),
                    Stars:       SafeDouble(rdr, 4),
                    ReviewCount: SafeInt(rdr, 5),
                    Categories:  Safe<string>(rdr, 6, "")
                ));
            }
            return list;
        }, ct);
    }

    public async Task<List<ReviewDto>> GetReviewsByBusinessIdAsync(
        string businessId, CancellationToken ct = default)
    {
        EnsureDbExists();
        return await Task.Run(() =>
        {
            var list = new List<ReviewDto>();
            using var conn = OpenConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = """
                SELECT review_id, business_id, user_id, stars, text, date, useful, funny, cool
                FROM review
                WHERE business_id = $bid
                """;
            cmd.Parameters.Add(new DuckDBParameter("bid", businessId));
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                ct.ThrowIfCancellationRequested();
                list.Add(new ReviewDto(
                    ReviewId:   Safe<string>(rdr, 0, ""),
                    BusinessId: Safe<string>(rdr, 1, ""),
                    UserId:     Safe<string>(rdr, 2, ""),
                    Stars:      SafeInt(rdr, 3),
                    Text:       Safe<string>(rdr, 4, ""),
                    Date:       Safe<string>(rdr, 5, ""),
                    Useful:     SafeInt(rdr, 6),
                    Funny:      SafeInt(rdr, 7),
                    Cool:       SafeInt(rdr, 8)
                ));
            }
            return list;
        }, ct);
    }

    public async Task<string> SaveReviewsToJsonAsync(
        string businessId, List<ReviewDto> reviews, CancellationToken ct = default)
    {
        var fileName = $"business-analysis_{businessId}.json";
        var filePath = Path.Combine(OutputPath, fileName);

        await using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
        foreach (var review in reviews)
        {
            ct.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(JsonSerializer.Serialize(review, NdjsonOptions));
        }
        return filePath;
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static DuckDBConnection OpenConnection()
    {
        var conn = new DuckDBConnection($"Data Source={DbPath}");
        conn.Open();
        return conn;
    }

    private static void EnsureDbExists()
    {
        if (!File.Exists(DbPath))
            throw new FileNotFoundException($"DuckDB файл не знайдено: {DbPath}");
    }

    private static T Safe<T>(System.Data.IDataRecord rdr, int i, T fallback) =>
        rdr.IsDBNull(i) ? fallback : (T)Convert.ChangeType(rdr.GetValue(i), typeof(T));

    private static double SafeDouble(System.Data.IDataRecord rdr, int i) =>
        rdr.IsDBNull(i) ? 0d : Convert.ToDouble(rdr.GetValue(i));

    private static int SafeInt(System.Data.IDataRecord rdr, int i) =>
        rdr.IsDBNull(i) ? 0 : Convert.ToInt32(rdr.GetValue(i));
}
