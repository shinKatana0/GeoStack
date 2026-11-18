// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using GeoStack.App;
using Xunit;

namespace GeoStack.Tests;

public sealed class AtlasOptimizationTests
{
    [Theory]
    [InlineData("zurich-city.mbtiles",false,"ru-RU",true)]
    [InlineData("zurich-city.mbtiles",true,"en-US",false)]
    [InlineData("warsaw-dark-ru-RU.mbtiles",true,"ru-RU",true)]
    [InlineData("warsaw-dark-en-US.mbtiles",true,"ru-RU",false)]
    [InlineData("zurich-dark-en-US.mbtiles",false,"en-US",false)]
    public void CityRasterSelectionRespectsThemeAndActualLabelLanguage(string file,bool dark,string language,bool expected)=>Assert.Equal(expected,NativeAtlasPilot.MatchesCityTheme(file,dark,language));
    [Theory] [InlineData(-1,4095)] [InlineData(4096,0)] [InlineData(8193,1)]
    public void LabelSpatialCellsWrapAcrossTheDateLine(int cell,int expected)=>Assert.Equal(expected,NativeAtlasPilot.WrapLabelCell(cell));
}
