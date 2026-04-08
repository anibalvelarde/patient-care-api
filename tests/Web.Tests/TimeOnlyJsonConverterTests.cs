using System.Text;
using System.Text.Json;
using Neurocorp.Api.Web.Middleware;

namespace Web.Tests;

public class TimeOnlyJsonConverterTests
{
    private readonly JsonSerializerOptions _options;

    public TimeOnlyJsonConverterTests()
    {
        _options = new JsonSerializerOptions();
        _options.Converters.Add(new TimeOnlyJsonConverterFactory());
    }

    [Theory]
    [InlineData("\"10:00\"", 10, 0)]
    [InlineData("\"08:30\"", 8, 30)]
    [InlineData("\"18:00\"", 18, 0)]
    [InlineData("\"00:00\"", 0, 0)]
    [InlineData("\"23:45\"", 23, 45)]
    public void Read_HHmm_ParsesCorrectly(string json, int expectedHour, int expectedMinute)
    {
        var result = JsonSerializer.Deserialize<TimeOnly>(json, _options);
        Assert.Equal(new TimeOnly(expectedHour, expectedMinute), result);
    }

    [Theory]
    [InlineData("\"10:00:00\"", 10, 0, 0)]
    [InlineData("\"08:30:15\"", 8, 30, 15)]
    [InlineData("\"23:59:59\"", 23, 59, 59)]
    public void Read_HHmmss_ParsesCorrectly(string json, int expectedHour, int expectedMinute, int expectedSecond)
    {
        var result = JsonSerializer.Deserialize<TimeOnly>(json, _options);
        Assert.Equal(new TimeOnly(expectedHour, expectedMinute, expectedSecond), result);
    }

    [Theory]
    [InlineData("\"25:00\"")]
    [InlineData("\"10\"")]
    [InlineData("\"abc\"")]
    [InlineData("\"10:00:00:00\"")]
    public void Read_InvalidFormat_ThrowsJsonException(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TimeOnly>(json, _options));
    }

    [Fact]
    public void Read_NullValue_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TimeOnly>("null", _options));
    }

    [Fact]
    public void Write_SerializesAsHHmmss()
    {
        var time = new TimeOnly(14, 30, 0);
        var json = JsonSerializer.Serialize(time, _options);
        Assert.Equal("\"14:30:00\"", json);
    }

    [Fact]
    public void Write_PreservesSeconds()
    {
        var time = new TimeOnly(9, 5, 45);
        var json = JsonSerializer.Serialize(time, _options);
        Assert.Equal("\"09:05:45\"", json);
    }

    [Fact]
    public void RoundTrip_NullableTimeOnly_WithHHmm()
    {
        // Simulates the actual BulkScheduleRequest.LineOverride.Time scenario
        var json = """{"Time":"10:00"}""";
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        opts.Converters.Add(new TimeOnlyJsonConverterFactory());
        var result = JsonSerializer.Deserialize<NullableTimeHolder>(json, opts);
        Assert.NotNull(result);
        Assert.Equal(new TimeOnly(10, 0), result!.Time);
    }

    [Fact]
    public void RoundTrip_NullableTimeOnly_WithNull()
    {
        var json = """{"Time":null}""";
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        opts.Converters.Add(new TimeOnlyJsonConverterFactory());
        var result = JsonSerializer.Deserialize<NullableTimeHolder>(json, opts);
        Assert.NotNull(result);
        Assert.Null(result!.Time);
    }

    [Fact]
    public void Deserialize_NullableTimeOnly_Directly()
    {
        var result = JsonSerializer.Deserialize<TimeOnly?>("\"10:00\"", _options);
        Assert.Equal(new TimeOnly(10, 0), result);
    }

    private class NullableTimeHolder
    {
        public TimeOnly? Time { get; set; }
    }
}
