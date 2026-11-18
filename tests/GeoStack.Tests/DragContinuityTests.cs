// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GeoStack.App;
using GeoStack.Core;
using Xunit;

namespace GeoStack.Tests;

public sealed class DragContinuityTests
{
    [Fact] public void ZoomInCannotReverseAfterDraggingIntoLowerResolutionCoverage()
    {
        Assert.Equal(100,AtlasControl.ClampRequestedZoom(100,1.4,50));
        Assert.Equal(100/1.4,AtlasControl.ClampRequestedZoom(100,1/1.4,50));
        Assert.Equal(150,AtlasControl.ClampRequestedZoom(100,2,150));
    }
    private static readonly Point Center=new(Geo.Project(52.2297,21.0122).X,Geo.Project(52.2297,21.0122).Y);
    private const double World=8388608,Width=950,Height=700;
    private static readonly MapPackage Detail=new("detail","detail","unused",1,"png",10,15,20.9372,52.1747,21.0872,52.2847,"test","test");
    private static readonly MapPackage Context=Detail with {Id="context",MinZoom=8,MaxZoom=12,West=20.4122,East=21.6122,South=51.7297,North=52.7297};
    private static RasterFrame Frame(RasterFramePlan plan)
    {
        var bitmap=BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[4],4);bitmap.Freeze();return new(plan,bitmap);
    }
    private static RasterFrame[] Ready(Point center)=>RasterComposition.Plans([Detail,Context],Detail.Id,World,Width,Height,center,OfflineTileLayer.Overscan).Select(Frame).ToArray();
    [Fact] public void PendingWindowDoesNotRemoveFrontAndEveryPointerDeltaMovesTheSameSurface()
    {
        var state=new RasterPresentation();var ready=Ready(Center);state.Update(ready,World,Width,Height,Center,false,0);var front=state.Front!;
        for(var i=1;i<=80;i++)
        {
            var moved=new Point(Center.X-i/World,Center.Y);state.Update([],World,Width,Height,moved,true,i);
            Assert.Same(front,state.Front);Assert.Equal(i,front.Screen(World,Width,Height,moved).X-front.Screen(World,Width,Height,Center).X,5);
        }
    }
    [Fact] public void OverscanContainsViewportAfterSmallHorizontalAndVerticalDrags()
    {
        var frame=Ready(Center)[0];foreach(var delta in new[]{new Vector(96,0),new Vector(-96,0),new Vector(0,96),new Vector(0,-96)})
            Assert.True(frame.CoverageScreen(World,Width,Height,Center+delta/World).Contains(new Rect(0,0,Width,Height)));
        Assert.True(frame.Image.IsFrozen);Assert.InRange(frame.Plan.Tiles.Count,1,RasterTilePlan.Limit);
    }
    [Fact] public void TinyBoundaryCrossingsDoNotFlapSourcesAndReturningRequiresAnInset()
    {
        var edgeX=(Detail.East+180)/360-Width/World/2;
        var start=new Point(edgeX-80/World,Center.Y);var state=new RasterPresentation();state.Update(Ready(start),World,Width,Height,start,false,0);var front=state.Front;
        for(var i=0;i<40;i++)
        {
            var p=new Point(edgeX+(i%2==0?2:-2)/World,Center.Y);state.Update(Ready(p),World,Width,Height,p,true,i*16);
            Assert.Same(front,state.Front);Assert.NotNull(state.Context);
        }
        var beyond=new Point(edgeX+20/World,Center.Y);state.Update(Ready(beyond),World,Width,Height,beyond,false,1000);Assert.Equal("context",state.Front!.Plan.Package.Id);
        for(var i=0;i<40;i++){var p=new Point(edgeX-2/World,Center.Y);state.Update(Ready(p),World,Width,Height,p,false,1300+i*16);Assert.Equal("context",state.Front.Plan.Package.Id);}
        state.Update(Ready(Center),World,Width,Height,Center,false,3000);Assert.Equal("detail",state.Front.Plan.Package.Id);
        Assert.Equal(2,state.SourceChanges);
    }
    [Fact] public void ObsoleteCompletionAbsentFromReadyCannotReplaceCurrentFront()
    {
        var state=new RasterPresentation();state.Update(Ready(Center),World,Width,Height,Center,false,0);var old=state.Front;
        var p=new Point(Center.X+.0002,Center.Y);state.Update([],World,Width,Height,p,true,30);Assert.Same(old,state.Front);
        state.Clear();state.Update([],World,Width,Height,p,false,40);Assert.Null(state.Front);Assert.Null(state.Context);
    }
    [Fact] public void SourceReplacementFadesOnlyAfterCompleteContextIsReady()
    {
        var state=new RasterPresentation();state.Update(Ready(Center),World,Width,Height,Center,false,0);
        var p=new Point(Center.X+.0003,Center.Y);state.Update([],World,Width,Height,p,true,20);Assert.Equal("detail",state.Front!.Plan.Package.Id);
        state.Update(Ready(p),World,Width,Height,p,true,40);Assert.Equal("context",state.Front.Plan.Package.Id);Assert.True(state.Transitioning(40));Assert.Equal(0,state.Progress(40));Assert.Equal(1,state.Progress(220));
        Assert.InRange(state.PreviousOpacity(100),0,1);Assert.False(state.Transitioning(221));
    }
}
