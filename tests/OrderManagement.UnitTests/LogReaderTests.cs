using OrderManagement.Api.Features.Logs;

namespace OrderManagement.UnitTests;

public sealed class LogReaderTests
{
    [Fact]
    public void Parses_a_compact_json_line_and_renders_the_message()
    {
        const string line = """
            {"@t":"2026-10-22T03:32:21.5794228Z","@mt":"Insufficient stock for product {ProductId}: requested {Requested}, available {Available}","@l":"Warning","@tr":"3caa4bf21d9428346db37168db270cd9","ProductId":"a1000000-0000-4000-8000-000000000001","Requested":99,"Available":15,"SourceContext":"OrderManagement.Api.Features.Orders.OrderService","RequestPath":"/api/orders","CorrelationId":"K7M2Q","UserName":"sales"}
            """;

        var entry = LogReader.TryParse(line)!;

        entry.Level.Should().Be("Warning");
        entry.Message.Should().Be("Insufficient stock for product a1000000-0000-4000-8000-000000000001: requested 99, available 15");
        entry.CorrelationId.Should().Be("K7M2Q");
        entry.UserName.Should().Be("sales");
        entry.RequestPath.Should().Be("/api/orders");
        entry.TraceId.Should().Be("3caa4bf21d9428346db37168db270cd9");
        entry.Timestamp.Should().Be(new DateTimeOffset(2026, 10, 22, 3, 32, 21, 579, TimeSpan.Zero).AddTicks(4228));
        entry.Properties.Should().ContainKey("Requested").And.NotContainKey("@mt");
    }

    [Fact]
    public void Missing_level_means_information_and_exception_is_kept()
    {
        var entry = LogReader.TryParse("""{"@t":"2026-10-22T00:00:00Z","@mt":"Boom","@x":"System.Exception: Boom\n   at X()"}""")!;

        entry.Level.Should().Be("Information");
        entry.Exception.Should().StartWith("System.Exception: Boom");
    }

    [Theory]
    [InlineData("HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0} ms",
        "HTTP POST /api/orders responded 409 in 12.5 ms")]
    [InlineData("Literal {{braces}} and {Unknown}", "Literal {braces} and {Unknown}")]
    [InlineData("Destructured {@Order}", "Destructured {\"Id\":1}")]
    public void Renders_templates_with_formats_escapes_and_unknown_properties(string template, string expected)
    {
        var line = """{"@t":"2026-10-22T00:00:00Z","@mt":"""
                   + System.Text.Json.JsonSerializer.Serialize(template)
                   + ""","RequestMethod":"POST","RequestPath":"/api/orders","StatusCode":409,"Elapsed":12.5,"Order":{"Id":1}}""";

        LogReader.TryParse(line)!.Message.Should().Be(expected);
    }

    [Fact]
    public void A_half_written_line_is_skipped_instead_of_failing()
    {
        LogReader.TryParse("""{"@t":"2026-10-22T00:00:00Z","@mt":"cut off""").Should().BeNull();
    }
}
