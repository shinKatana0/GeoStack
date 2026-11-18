// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using GeoStack.App;
using Xunit;

namespace GeoStack.Tests;

public sealed class AtlasGateStatisticsTests
{
    [Fact]
    public void TailPercentileUsesNearestRankAndRequiresEnoughSamples()
    {
        var small=JsonSerializer.SerializeToElement(AtlasProfiling.Statistics(Enumerable.Range(0,99).Select(i=>(double)i)));
        Assert.Equal(JsonValueKind.Null,small.GetProperty("P99").ValueKind);
        var enough=JsonSerializer.SerializeToElement(AtlasProfiling.Statistics(Enumerable.Range(0,100).Select(i=>(double)i)));
        Assert.Equal(98,enough.GetProperty("P99").GetDouble());
        Assert.Equal(enough.GetProperty("Median").GetDouble(),enough.GetProperty("P50").GetDouble());
    }
}
