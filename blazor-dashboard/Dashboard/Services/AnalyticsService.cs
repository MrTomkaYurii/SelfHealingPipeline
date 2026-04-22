using System.Text.Json;
using Dashboard.Client.Models;
using DuckDB.NET.Data;
using System.Numerics;

namespace Dashboard.Services;

public class AnalyticsService
{
    private readonly IConfiguration      _config;
    private readonly IWebHostEnvironment _env;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the","a","an","is","it","was","i","and","or","to","of","in","that","this","for",
        "on","with","at","by","from","as","are","be","have","had","not","but","they","we",
        "you","my","so","if","he","she","all","do","did","its","very","just","me","no",
        "up","out","there","about","what","which","who","get","got","been","would","will",
        "can","could","his","her","our","than","then","them","has","more","also","when",
        "into","like","some","were","their","one","s","re","ve","ll","m","t","d"
    };

    public AnalyticsService(IConfiguration config, IWebHostEnvironment env)
    {
        _config = config;
        _env    = env;
    }

    private string DbPath => Path.GetFullPath(Path.Combine(
        _env.ContentRootPath,
        _config["PipelineBasePath"] ?? "../../airflow-pipeline",
        "input", "yelp.duckdb"));

    private string GetOutputFilePath(string businessId)
    {
        var basePath  = _config["PipelineBasePath"] ?? "../../airflow-pipeline";
        var outputDir = Path.GetFullPath(Path.Combine(_env.ContentRootPath, basePath, "output"));
        return Path.Combine(outputDir, $"business-analysis_{businessId}-result.json");
    }

    // ── JSON-based (reads DAG result file) ───────────────────────────────────

    public async Task<SentimentSummaryDto> GetSentimentSummaryAsync(string businessId)
    {
        var filePath = GetOutputFilePath(businessId);
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Файл результату не знайдено: {filePath}");

        var json = await File.ReadAllTextAsync(filePath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var totals   = root.GetProperty("totals");
        int total    = SafeInt(totals, "processed");
        int healed   = SafeInt(totals, "healed");

        int pos = 0, neg = 0, neu = 0;
        if (root.TryGetProperty("sentiment_distribution", out var sd))
        {
            pos = SafeInt(sd, "POSITIVE");
            neg = SafeInt(sd, "NEGATIVE");
            neu = SafeInt(sd, "NEUTRAL");
        }

        var confBuckets = new Dictionary<string, int>
        {
            ["0.0–0.6"] = 0, ["0.6–0.7"] = 0, ["0.7–0.8"] = 0, ["0.8–0.9"] = 0, ["0.9–1.0"] = 0
        };
        var starMap  = new Dictionary<int, (int p, int n, int u)>();
        var posWords = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var negWords = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        double sumConf = 0; int confCount = 0;

        if (root.TryGetProperty("results", out var results))
        {
            foreach (var r in results.EnumerateArray())
            {
                // confidence
                if (r.TryGetProperty("confidence", out var cv))
                {
                    double c = cv.GetDouble();
                    sumConf += c; confCount++;
                    if      (c < 0.6) confBuckets["0.0–0.6"]++;
                    else if (c < 0.7) confBuckets["0.6–0.7"]++;
                    else if (c < 0.8) confBuckets["0.7–0.8"]++;
                    else if (c < 0.9) confBuckets["0.8–0.9"]++;
                    else              confBuckets["0.9–1.0"]++;
                }

                // stars sentiment
                int stars = r.TryGetProperty("stars", out var sv) ? (int)sv.GetDouble() : 0;
                string sent = r.TryGetProperty("predicted_sentiment", out var ssv)
                    ? ssv.GetString() ?? "" : "";
                if (stars >= 1 && stars <= 5)
                {
                    var cur = starMap.GetValueOrDefault(stars);
                    starMap[stars] = sent == "POSITIVE" ? (cur.p + 1, cur.n,     cur.u)
                                   : sent == "NEGATIVE" ? (cur.p,     cur.n + 1, cur.u)
                                   :                      (cur.p,     cur.n,     cur.u + 1);
                }

                // word counts
                if (sent is "POSITIVE" or "NEGATIVE")
                {
                    string text = r.TryGetProperty("text", out var tv) ? tv.GetString() ?? "" : "";
                    if (text.Length > 0)
                    {
                        var target = sent == "POSITIVE" ? posWords : negWords;
                        foreach (var word in Tokenize(text))
                            target[word] = target.GetValueOrDefault(word) + 1;
                    }
                }
            }
        }

        var healingStats = new Dictionary<string, int>();
        if (root.TryGetProperty("healing_statistics", out var hs))
            foreach (var p in hs.EnumerateObject())
                healingStats[p.Name] = p.Value.GetInt32();

        return new SentimentSummaryDto
        {
            Total         = total,
            Positive      = pos,
            Negative      = neg,
            Neutral       = neu,
            Healed        = healed,
            AvgConfidence = confCount > 0 ? sumConf / confCount : 0,
            ConfidenceBuckets = confBuckets
                .Select(kv => new BucketDto { Range = kv.Key, Count = kv.Value }).ToList(),
            HealingStats = healingStats,
            SentimentByStars = Enumerable.Range(1, 5).Select(s =>
            {
                var (p2, n2, u2) = starMap.GetValueOrDefault(s);
                return new StarSentimentDto { Stars = s, Positive = p2, Negative = n2, Neutral = u2 };
            }).ToList(),
            TopPositiveWords = posWords.OrderByDescending(kv => kv.Value).Take(10)
                .Select(kv => new WordCountDto { Word = kv.Key, Count = kv.Value }).ToList(),
            TopNegativeWords = negWords.OrderByDescending(kv => kv.Value).Take(10)
                .Select(kv => new WordCountDto { Word = kv.Key, Count = kv.Value }).ToList(),
        };
    }

    // ── DuckDB-based ──────────────────────────────────────────────────────────

    public Task<List<MonthlyCountDto>> GetMonthlyTrendAsync(string businessId) => Task.Run(() =>
    {
        using var conn = Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT strftime(TRY_CAST(date AS TIMESTAMP), '%Y-%m') AS month,
                   COUNT(*) AS cnt
            FROM review
            WHERE business_id = $bid
              AND TRY_CAST(date AS TIMESTAMP) IS NOT NULL
            GROUP BY month
            ORDER BY month
            """;
        cmd.Parameters.Add(new DuckDBParameter("bid", businessId));
        using var rdr = cmd.ExecuteReader();
        var result = new List<MonthlyCountDto>();
        while (rdr.Read())
            result.Add(new MonthlyCountDto
            {
                Month = rdr.GetString(0),
                Count = Convert.ToInt32(rdr.GetValue(1))
            });
        return result;
    });

    public Task<List<HeatmapCellDto>> GetHeatmapAsync(string businessId) => Task.Run(() =>
    {
        using var conn = Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT CAST(EXTRACT(dow  FROM TRY_CAST(date AS TIMESTAMP)) AS INTEGER) AS dow,
                   CAST(EXTRACT(hour FROM TRY_CAST(date AS TIMESTAMP)) AS INTEGER) AS hr,
                   COUNT(*) AS cnt
            FROM review
            WHERE business_id = $bid
              AND TRY_CAST(date AS TIMESTAMP) IS NOT NULL
            GROUP BY dow, hr
            ORDER BY dow, hr
            """;
        cmd.Parameters.Add(new DuckDBParameter("bid", businessId));
        using var rdr = cmd.ExecuteReader();
        var result = new List<HeatmapCellDto>();
        while (rdr.Read())
            result.Add(new HeatmapCellDto
            {
                DayOfWeek = Convert.ToInt32(rdr.GetValue(0)),
                Hour      = Convert.ToInt32(rdr.GetValue(1)),
                Count     = Convert.ToInt32(rdr.GetValue(2))
            });
        return result;
    });

    public Task<List<MonthlyRatingDto>> GetRatingTrendAsync(string businessId) => Task.Run(() =>
    {
        using var conn = Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT strftime(TRY_CAST(date AS TIMESTAMP), '%Y-%m') AS month,
                   AVG(CAST(stars AS DOUBLE)) AS avg_stars
            FROM review
            WHERE business_id = $bid
              AND TRY_CAST(date AS TIMESTAMP) IS NOT NULL
            GROUP BY month
            ORDER BY month
            """;
        cmd.Parameters.Add(new DuckDBParameter("bid", businessId));
        using var rdr = cmd.ExecuteReader();
        var result = new List<MonthlyRatingDto>();
        while (rdr.Read())
            result.Add(new MonthlyRatingDto
            {
                Month    = rdr.GetString(0),
                AvgStars = Convert.ToDouble(rdr.GetValue(1))
            });
        return result;
    });

    public Task<List<TopReviewerDto>> GetTopReviewersAsync(string businessId) => Task.Run(() =>
    {
        using var conn = Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT u.name, COUNT(*) AS review_count, AVG(CAST(r.stars AS DOUBLE)) AS avg_stars
            FROM review r
            JOIN "user" u ON r.user_id = u.user_id
            WHERE r.business_id = $bid
            GROUP BY u.user_id, u.name
            ORDER BY review_count DESC
            LIMIT 10
            """;
        cmd.Parameters.Add(new DuckDBParameter("bid", businessId));
        using var rdr = cmd.ExecuteReader();
        var result = new List<TopReviewerDto>();
        while (rdr.Read())
            result.Add(new TopReviewerDto
            {
                Name        = rdr.IsDBNull(0) ? "Unknown" : rdr.GetString(0),
                ReviewCount = Convert.ToInt32(rdr.GetValue(1)),
                AvgStars    = Convert.ToDouble(rdr.GetValue(2))
            });
        return result;
    });

    public Task<List<ScatterPointDto>> GetUserScatterAsync(string businessId) => Task.Run(() =>
    {
        using var conn = Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) AS total_reviews, AVG(CAST(stars AS DOUBLE)) AS avg_rating
            FROM review
            WHERE business_id = $bid
            GROUP BY user_id
            LIMIT 800
            """;
        cmd.Parameters.Add(new DuckDBParameter("bid", businessId));
        using var rdr = cmd.ExecuteReader();
        var result = new List<ScatterPointDto>();
        while (rdr.Read())
            result.Add(new ScatterPointDto
            {
                TotalReviews = Convert.ToInt32(rdr.GetValue(0)),
                AvgRating    = Convert.ToDouble(rdr.GetValue(1))
            });
        return result;
    });

    public Task<UsefulnessDto> GetUsefulnessAsync(string businessId) => Task.Run(() =>
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        SELECT
            SUM(CAST(useful AS BIGINT)) AS u,
            SUM(CAST(funny  AS BIGINT)) AS f,
            SUM(CAST(cool   AS BIGINT)) AS c
        FROM review
        WHERE business_id = $bid
        """;
        cmd.Parameters.Add(new DuckDBParameter("bid", businessId));

        using var rdr = cmd.ExecuteReader();
        if (rdr.Read())
        {
            return new UsefulnessDto
            {
                TotalUseful = rdr.IsDBNull(0) ? 0 : (long)(BigInteger)rdr.GetValue(0),
                TotalFunny = rdr.IsDBNull(1) ? 0 : (long)(BigInteger)rdr.GetValue(1),
                TotalCool = rdr.IsDBNull(2) ? 0 : (long)(BigInteger)rdr.GetValue(2)
            };
        }

        return new UsefulnessDto();
    });

    public Task<List<LengthBucketDto>> GetLengthDistributionAsync(string businessId) => Task.Run(() =>
    {
        using var conn = Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                CASE
                    WHEN LENGTH(text) <  100  THEN '0–100'
                    WHEN LENGTH(text) <  300  THEN '100–300'
                    WHEN LENGTH(text) <  500  THEN '300–500'
                    WHEN LENGTH(text) < 1000  THEN '500–1000'
                    WHEN LENGTH(text) < 2000  THEN '1000–2000'
                    ELSE '2000+'
                END AS range,
                COUNT(*) AS cnt
            FROM review
            WHERE business_id = $bid
            GROUP BY range
            """;
        cmd.Parameters.Add(new DuckDBParameter("bid", businessId));
        using var rdr = cmd.ExecuteReader();
        var dict = new Dictionary<string, int>();
        while (rdr.Read())
            dict[rdr.GetString(0)] = Convert.ToInt32(rdr.GetValue(1));

        var order = new[] { "0–100", "100–300", "300–500", "500–1000", "1000–2000", "2000+" };
        return order.Select(r => new LengthBucketDto { Range = r, Count = dict.GetValueOrDefault(r) }).ToList();
    });

    public Task<List<ResultFileDto>> GetResultFilesAsync() => Task.Run(() =>
    {
        var basePath  = _config["PipelineBasePath"] ?? "../../airflow-pipeline";
        var outputDir = Path.GetFullPath(Path.Combine(_env.ContentRootPath, basePath, "output"));
        if (!Directory.Exists(outputDir)) return new List<ResultFileDto>();

        return Directory.GetFiles(outputDir, "business-analysis_*-result.json")
            .Select(f =>
            {
                var name = Path.GetFileName(f);
                var mid  = name.Replace("business-analysis_", "").Replace("-result.json", "");
                return new ResultFileDto { FileName = name, FilePath = f, BusinessId = mid };
            })
            .OrderByDescending(f => f.FileName)
            .ToList();
    });

    // ── helpers ─────────────────────────────────────────────────────────────

    private DuckDBConnection Open()
    {
        var conn = new DuckDBConnection($"Data Source={DbPath}");
        conn.Open();
        return conn;
    }

    private static int SafeInt(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null
            ? v.GetInt32() : 0;

    private static IEnumerable<string> Tokenize(string text) =>
        text.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\n', '\r', ',', '.', '!', '?', '"', '\'',
                           '(', ')', '-', '/', '\\', ':', ';', '*', '&' },
                   StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !StopWords.Contains(w) && w.All(char.IsLetter));
}
