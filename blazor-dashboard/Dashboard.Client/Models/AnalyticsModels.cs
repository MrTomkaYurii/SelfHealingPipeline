namespace Dashboard.Client.Models;

public class SentimentSummaryDto
{
    public int    Total         { get; set; }
    public int    Positive      { get; set; }
    public int    Negative      { get; set; }
    public int    Neutral       { get; set; }
    public int    Healed        { get; set; }
    public double AvgConfidence { get; set; }

    public List<BucketDto>      ConfidenceBuckets { get; set; } = [];
    public Dictionary<string,int> HealingStats    { get; set; } = new();
    public List<StarSentimentDto> SentimentByStars { get; set; } = [];
    public List<WordCountDto>   TopPositiveWords  { get; set; } = [];
    public List<WordCountDto>   TopNegativeWords  { get; set; } = [];
}

public class BucketDto
{
    public string Range { get; set; } = "";
    public int    Count { get; set; }
}

public class StarSentimentDto
{
    public int Stars    { get; set; }
    public int Positive { get; set; }
    public int Negative { get; set; }
    public int Neutral  { get; set; }
}

public class MonthlyCountDto
{
    public string Month { get; set; } = "";
    public int    Count { get; set; }
}

public class HeatmapCellDto
{
    public int DayOfWeek { get; set; }
    public int Hour      { get; set; }
    public int Count     { get; set; }
}

public class MonthlyRatingDto
{
    public string Month    { get; set; } = "";
    public double AvgStars { get; set; }
}

public class TopReviewerDto
{
    public string Name        { get; set; } = "";
    public int    ReviewCount { get; set; }
    public double AvgStars    { get; set; }
}

public class ScatterPointDto
{
    public int    TotalReviews { get; set; }
    public double AvgRating   { get; set; }
}

public class UsefulnessDto
{
    public long TotalUseful { get; set; }
    public long TotalFunny  { get; set; }
    public long TotalCool   { get; set; }
}

public class LengthBucketDto
{
    public string Range { get; set; } = "";
    public int    Count { get; set; }
}

public class WordCountDto
{
    public string Word  { get; set; } = "";
    public int    Count { get; set; }
}

public class ResultFileDto
{
    public string FileName   { get; set; } = "";
    public string FilePath   { get; set; } = "";
    public string BusinessId { get; set; } = "";
}
