using System;
using AbpAdmin.Monitoring;
using Shouldly;
using Xunit;

namespace AbpAdmin.Monitoring;

public class RuntimeInfoFactoryTests
{
    [Fact]
    public void Should_Format_Positive_Negative_And_Zero_Offsets()
    {
        RuntimeInfoFactory.FormatOffset(TimeSpan.FromHours(8)).ShouldBe("+08:00");
        RuntimeInfoFactory.FormatOffset(TimeSpan.FromMinutes(-(5 * 60 + 30))).ShouldBe("-05:30");
        RuntimeInfoFactory.FormatOffset(TimeSpan.Zero).ShouldBe("+00:00");
    }

    [Fact]
    public void Should_Keep_Utc_Instant_And_Label_The_Utc_Zone()
    {
        var instant = new DateTimeOffset(2026, 11, 13, 2, 34, 5, TimeSpan.Zero);

        var dto = RuntimeInfoFactory.Create(typeof(RuntimeInfoFactory).Assembly, instant, TimeZoneInfo.Utc);

        dto.ApiVersion.ShouldNotBeNullOrWhiteSpace();
        dto.ServerUnixTimeMilliseconds.ShouldBe(instant.ToUnixTimeMilliseconds());
        dto.ServerTime.ToUnixTimeMilliseconds().ShouldBe(instant.ToUnixTimeMilliseconds());
        dto.ServerTime.Offset.ShouldBe(TimeSpan.Zero);
        dto.ServerTime.Hour.ShouldBe(2);
        dto.TimeZoneId.ShouldBe("UTC");
        dto.IanaTimeZoneId.ShouldBe("UTC");
        dto.UtcOffset.ShouldBe("+00:00");
    }

    [Fact]
    public void Should_Shift_Wall_Clock_Into_The_Given_Zone()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone(
            "Test-Plus-Eight",
            TimeSpan.FromHours(8),
            "(UTC+08:00) 测试东八区",
            "测试东八区");
        var instant = new DateTimeOffset(2026, 11, 13, 2, 34, 5, TimeSpan.Zero);

        var dto = RuntimeInfoFactory.Create(typeof(RuntimeInfoFactory).Assembly, instant, zone);

        dto.UtcOffset.ShouldBe("+08:00");
        dto.TimeZoneId.ShouldBe("Test-Plus-Eight");
        dto.IanaTimeZoneId.ShouldBeNull();
        dto.TimeZoneDisplayName.ShouldBe("(UTC+08:00) 测试东八区");
        dto.ServerTime.Hour.ShouldBe(10);
        dto.ServerTime.Minute.ShouldBe(34);
        dto.ServerTime.Offset.ShouldBe(TimeSpan.FromHours(8));
        dto.ServerUnixTimeMilliseconds.ShouldBe(instant.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void Should_Shift_Wall_Clock_Backward_Across_Midnight()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone(
            "Test-Minus-Five",
            TimeSpan.FromHours(-5),
            "Test -5",
            "Test -5");
        var instant = new DateTimeOffset(2026, 11, 13, 2, 34, 5, TimeSpan.Zero);

        var dto = RuntimeInfoFactory.Create(typeof(RuntimeInfoFactory).Assembly, instant, zone);

        dto.UtcOffset.ShouldBe("-05:00");
        dto.ServerTime.Day.ShouldBe(12);
        dto.ServerTime.Hour.ShouldBe(21);
        dto.ServerTime.Minute.ShouldBe(34);
    }
}
