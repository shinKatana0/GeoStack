// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Windows;

namespace GeoStack.App;

/// <summary>Retain geographic surfaces independently of the pending viewport's tile-window identity.</summary>
public sealed class RasterPresentation
{
    public const double RefillMargin=64,PromotionMargin=64,FadeMilliseconds=180;
    public RasterFrame? Front { get; private set; }
    public RasterFrame? Context { get; private set; }
    public RasterFrame? Previous { get; private set; }
    public int Swaps { get; private set; }
    public int SourceChanges { get; private set; }
    private double transitionStart,previousOpacity=1;
    public bool Transitioning(double now)=>Previous!=null && now-transitionStart<FadeMilliseconds;
    public double Progress(double now)=>Math.Clamp((now-transitionStart)/FadeMilliseconds,0,1);
    public void Clear(){Front=Context=Previous=null;}
    public static double Coverage(RasterFrame frame,double world,double width,double height,Point center)
    {
        var rect=Rect.Intersect(frame.CoverageScreen(world,width,height,center),new Rect(0,0,width,height));
        return rect.IsEmpty?0:rect.Width*rect.Height/(width*height);
    }
    public double FrontOpacity(double world,double width,double height,Point center)
    {
        if(Front==null)return 0;
        // A warm complete context is revealed gradually, never by dropping the detail reference.
        if(Context==null || Context.Plan.Key==Front.Plan.Key || Coverage(Context,world,width,height,center)<.999)return 1;
        return Math.Clamp((Coverage(Front,world,width,height,center)-.75)/.20,0,1);
    }
    public double PreviousOpacity(double now)=>previousOpacity*(1-Progress(now));
    public void Update(IReadOnlyList<RasterFrame> ready,double world,double width,double height,Point center,bool moving,double now)
    {
        if(!Transitioning(now))Previous=null;
        var viewport=new Rect(0,0,width,height);
        var complete=ready.Where(f=>Coverage(f,world,width,height,center)>=.999999).ToArray();
        var context=complete.OrderBy(f=>f.Plan.Package.MaxZoom).FirstOrDefault();
        if(context!=null && (Context==null || context.Plan.Package.MaxZoom<=Context.Plan.Package.MaxZoom || Coverage(Context,world,width,height,center)<.999))Context=context;
        var candidate=complete.FirstOrDefault();
        if(candidate==null)return;
        if(Front!=null && candidate.Plan.Zoom>Front.Plan.Zoom)
        {
            // Re-enter detail only well inside coverage, and never refine during a live drag.
            if(moving || !RasterComposition.CoversViewport(candidate.Plan.Package,world,width+PromotionMargin*2,height+PromotionMargin*2,center))
                candidate=complete.FirstOrDefault(f=>f.Plan.Package.Id==Front.Plan.Package.Id);
        }
        if(candidate==null || candidate.Plan.Key==Front?.Plan.Key)return;
        if(Front!=null && moving)
        {
            if(candidate.Plan.Package.Id==Front.Plan.Package.Id && candidate.Plan.Zoom==Front.Plan.Zoom)
            {
                var refill=viewport;refill.Inflate(RefillMargin,RefillMargin);
                if(Front.CoverageScreen(world,width,height,center).Contains(refill))return;
            }
            else if(Coverage(Front,world,width,height,center)>.80)return;
        }
        var different=Front!=null && (Front.Plan.Package.Id!=candidate.Plan.Package.Id || Front.Plan.Zoom!=candidate.Plan.Zoom);
        if(different){previousOpacity=FrontOpacity(world,width,height,center);Previous=Front;transitionStart=now;SourceChanges++;}
        else Previous=null;
        Front=candidate;Swaps++;
    }
}
