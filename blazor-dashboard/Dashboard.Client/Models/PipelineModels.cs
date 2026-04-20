using System.Text.Json.Serialization;

namespace Dashboard.Client.Models;

public class PipelineRun
{
    [JsonPropertyName("run_info")]
    public RunInfo RunInfo { get; set; } = new();

    [JsonPropertyName("totals")]
    public Totals Totals { get; set; } = new();

    [JsonPropertyName("rates")]
    public Rates Rates { get; set; } = new();

    [JsonPropertyName("sentiment_distribution")]
    public SentimentDistribution SentimentDistribution { get; set; } = new();

    [JsonPropertyName("healing_statistics")]
    public Dictionary<string, int> HealingStatistics { get; set; } = new();

    [JsonPropertyName("star_sentiment_correlation")]
    public Dictionary<string, SentimentCount> StarSentimentCorrelation { get; set; } = new();

    [JsonPropertyName("average_confidence")]
    public AverageConfidence AverageConfidence { get; set; } = new();

    [JsonPropertyName("results")]
    public List<ReviewResult> Results { get; set; } = new();
}

public class RunInfo
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("batch_size")]
    public int BatchSize { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("input_file")]
    public string InputFile { get; set; } = "";
}

public class Totals
{
    [JsonPropertyName("processed")]
    public int Processed { get; set; }

    [JsonPropertyName("success")]
    public int Success { get; set; }

    [JsonPropertyName("healed")]
    public int Healed { get; set; }

    [JsonPropertyName("degraded")]
    public int Degraded { get; set; }
}

public class Rates
{
    [JsonPropertyName("success_rate")]
    public double SuccessRate { get; set; }

    [JsonPropertyName("healing_rate")]
    public double HealingRate { get; set; }

    [JsonPropertyName("degradation_rate")]
    public double DegradationRate { get; set; }
}

public class SentimentDistribution
{
    [JsonPropertyName("POSITIVE")]
    public int Positive { get; set; }

    [JsonPropertyName("NEGATIVE")]
    public int Negative { get; set; }

    [JsonPropertyName("NEUTRAL")]
    public int Neutral { get; set; }
}

public class SentimentCount
{
    [JsonPropertyName("POSITIVE")]
    public int Positive { get; set; }

    [JsonPropertyName("NEGATIVE")]
    public int Negative { get; set; }

    [JsonPropertyName("NEUTRAL")]
    public int Neutral { get; set; }
}

public class AverageConfidence
{
    [JsonPropertyName("success")]
    public double Success { get; set; }

    [JsonPropertyName("healed")]
    public double Healed { get; set; }

    [JsonPropertyName("degraded")]
    public double Degraded { get; set; }
}

public class ReviewResult
{
    [JsonPropertyName("review_id")]
    public string ReviewId { get; set; } = "";

    [JsonPropertyName("business_id")]
    public string BusinessId { get; set; } = "";

    [JsonPropertyName("stars")]
    public double Stars { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("original_text")]
    public string? OriginalText { get; set; }

    [JsonPropertyName("predicted_sentiment")]
    public string PredictedSentiment { get; set; } = "";

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("healing_applied")]
    public bool HealingApplied { get; set; }

    [JsonPropertyName("healing_action")]
    public string? HealingAction { get; set; }

    [JsonPropertyName("error_type")]
    public string? ErrorType { get; set; }

    [JsonPropertyName("metadata")]
    public ReviewMetadata Metadata { get; set; } = new();
}

public class ReviewMetadata
{
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("useful")]
    public int Useful { get; set; }

    [JsonPropertyName("funny")]
    public int Funny { get; set; }

    [JsonPropertyName("cool")]
    public int Cool { get; set; }
}

public class PipelineRunSummary
{
    public string FileName { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public int BatchSize { get; set; }
    public int Offset { get; set; }
    public int Processed { get; set; }
    public int Success { get; set; }
    public int Healed { get; set; }
    public int Degraded { get; set; }
    public double SuccessRate { get; set; }
    public string HealthStatus { get; set; } = "HEALTHY";
}

public class FileNode
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsDirectory { get; set; }
    public long? SizeBytes { get; set; }
    public DateTime LastModified { get; set; }
    public List<FileNode> Children { get; set; } = new();
}
