// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using GeoStack.Core;
using Xunit;

namespace GeoStack.Tests;

public sealed class MultiSelectionTests
{
    [Fact] public void ControlClickTogglesExactIdentities()
    {
        var s=new LibrarySelection();string[] order=["a","b","c"];
        s.Click("a",order,false,false);s.Click("c",order,true,false);
        Assert.Equal(2,s.Ids.Count);s.Click("a",order,true,false);Assert.Equal("c",Assert.Single(s.Ids));
    }
    [Fact] public void ShiftUsesCurrentOrderAcrossPages()
    {
        var order=Enumerable.Range(0,250).Select(i=>i.ToString()).ToArray();var s=new LibrarySelection();
        s.Click("118",order,false,false);s.Click("122",order,false,true);Assert.Equal(order.Skip(118).Take(5).ToHashSet(),s.Ids.ToHashSet());
        s.Click("125",order,true,true);Assert.Equal(8,s.Ids.Count);
    }
    [Fact] public void SortingChangesRangeButNeverIdentities()
    {
        var s=new LibrarySelection();s.Click("b",["a","b","c"],false,false);s.Click("a",["c","b","a"],false,true);
        Assert.Contains("a",s.Ids);Assert.Contains("b",s.Ids);Assert.DoesNotContain("c",s.Ids);
    }
    [Fact] public void FilteredSelectAllPreservesExplicitHiddenSelection()
    {
        var s=new LibrarySelection();s.Click("hidden",["hidden"],false,false);s.All(["a","b"]);
        Assert.Equal(3,s.Ids.Count);s.Reconcile(["a","hidden"]);Assert.Equal(2,s.Ids.Count);s.Clear();Assert.Empty(s.Ids);
    }
    [Fact] public void MatchingNamesRemainIndependentAndPathsAreCaseInsensitive()
    {
        var s=new LibrarySelection();s.All([@"C:\a\Photo.jpg",@"C:\b\Photo.jpg"]);
        s.Set(@"c:\a\photo.jpg",false);Assert.Equal(@"C:\b\Photo.jpg",Assert.Single(s.Ids));
    }
    [Fact] public void FiltersUseValidCoordinatesAndExactOwnerCounts()
    {
        var media=Enumerable.Range(0,2005).Select(i=>new MediaItem(i.ToString(),i<3?59.93:null,i<3?30.33:null,i%2==0?"Photo":"Video")).ToArray();
        Assert.Equal(2005,LibraryFilter.Apply(media,LibraryGpsFilter.All).Count());Assert.Equal(3,LibraryFilter.Apply(media,LibraryGpsFilter.WithGps).Count());Assert.Equal(2002,LibraryFilter.Apply(media,LibraryGpsFilter.WithoutGps).Count());
    }
    [Theory][InlineData(91,0)][InlineData(0,181)][InlineData(double.NaN,30)][InlineData(40,double.PositiveInfinity)]
    public void InvalidCoordinatesAreWithoutGps(double lat,double lon)
    {
        MediaItem[] items=[new("bad",lat,lon,"Video")];Assert.Empty(LibraryFilter.Apply(items,LibraryGpsFilter.WithGps));Assert.Single(LibraryFilter.Apply(items,LibraryGpsFilter.WithoutGps));
    }
    [Fact] public void CopyResolutionNeverSubstitutesAnotherMember()
    {
        MediaItem[] media=[new("a",1,2,"Photo"),new("b",null,null,"Video"),new("c",3,4,"Photo")];var s=new LibrarySelection();s.All(["b","c"]);
        Assert.Equal(new[]{"b","c"},s.Resolve(media).Select(m=>m.Path));Assert.False(s.Resolve(media)[0].HasLocation);
    }
    [Fact] public void TransactionalBatchExclusionsSurviveRestartAndLateStore()
    {
        var root=Environment.GetEnvironmentVariable("GEOSTACK_ROOT")!;var folder=TestStorage.Create(root);Directory.CreateDirectory(folder);
        var db=Path.Combine(folder,"index.db");var a=Path.Combine(folder,"one.jpg");var b=Path.Combine(folder,"two.mov");var c=Path.Combine(folder,"keep.jpg");
        using(var index=new MediaIndex(db)){foreach(var p in new[]{a,b,c})index.Store(new(p,59.93,30.33,"Photo"),1,2);index.ExcludeMany([a,b,a.ToUpperInvariant()]);index.Store(new(a,1,2,"Photo"),1,3);Assert.Equal(c,Assert.Single(index.ReadAll()).Path);}
        using(var index=new MediaIndex(db)){Assert.Equal(2,index.Exclusions().Count);index.RestoreExplicitFiles([a]);index.Store(new(a,1,2,"Photo"),1,3);Assert.Equal(2,index.ReadAll().Count);Assert.Contains(MediaInputs.NormalizePath(b),index.Exclusions());}
    }
}


