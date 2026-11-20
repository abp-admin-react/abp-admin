using System;
using System.Linq;
using AbpAdmin.Grids;
using Shouldly;
using Volo.Abp.Validation;
using Xunit;

namespace AbpAdmin.Domain.Tests.Grids;

public class GridColumnTypeTests
{
    private enum Status : byte { Open = 1, Closed = 2 }
    private sealed record Row(Status? Status, DateTimeOffset? When, short Attempts);

    private static readonly GridQueryDefinition<Row> Grid = new GridQueryDefinition<Row>()
        .Field("status", r => r.Status)
        .Field("when", r => r.When)
        .Field("attempts", r => r.Attempts);

    private static readonly IQueryable<Row> Rows = new[] {
        new Row(Status.Open, DateTimeOffset.Parse("2025-01-02T03:00:00+08:00"), 2),
        new Row(Status.Closed, DateTimeOffset.Parse("2025-01-02T00:00:00Z"), 15),
        new Row(null, null, -3)
    }.AsQueryable();

    private static IQueryable<Row> Filter(string key, string op, string value) => Grid.Filter(Rows,
        new GridQueryInput { Filters = [new() { Field = key, Conditions = [new() { Op = op, Value = value }] }] });

    [Theory]
    [InlineData("status", "eq", "1", 2)]
    [InlineData("status", "ne", "Closed", 2)]
    [InlineData("when", "eq", "2025-01-01", 2)]
    [InlineData("when", "gte", "2025-01-02", 15)]
    [InlineData("attempts", "gt", "9", 15)]
    public void Typed_columns_compare_raw_values(string key, string op, string value, int attempts)
    {
        ((int)Filter(key, op, value).Single().Attempts).ShouldBe(attempts);
    }

    [Theory]
    [InlineData("status", "eq", "3")]
    [InlineData("status", "gt", "1")]
    [InlineData("attempts", "eq", "99999")]
    public void Invalid_values_are_validation_errors(string key, string op, string value)
    {
        Should.Throw<AbpValidationException>(() => Filter(key, op, value));
    }
}
