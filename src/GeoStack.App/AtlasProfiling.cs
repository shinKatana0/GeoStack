// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Windows.Threading;

namespace GeoStack.App;

/// <summary>Explicit diagnostic sessions only. Samples are bounded and contain no media paths.</summary>
public sealed class AtlasProfiling : EventListener
{
    public static AtlasProfiling? Current { get; private set; }
    private readonly object gate=new();
    private readonly Dictionary<string,List<double>> samples=[];
    private readonly Dictionary<DispatcherOperation,long> operations=[];
    private readonly List<object> timeline=[];
    private readonly Dispatcher dispatcher;
    private DateTime? suspended;
    public string Phase="setup";
    public int RuntimeEvents { get; private set; }
    public AtlasProfiling(Dispatcher dispatcher)
    {
        this.dispatcher=dispatcher;Current=this;
        dispatcher.Hooks.OperationStarted+=Started;dispatcher.Hooks.OperationCompleted+=Completed;
    }
    protected override void OnEventSourceCreated(EventSource source){if(source.Name=="Microsoft-Windows-DotNETRuntime")EnableEvents(source,EventLevel.Informational,(EventKeywords)1);}
    protected override void OnEventWritten(EventWrittenEventArgs data)
    {
        if(gate==null)return;
        if(data.EventName?.StartsWith("GCSuspendEEBegin")==true){suspended=data.TimeStamp;RuntimeEvents++;}
        if(data.EventName?.StartsWith("GCRestartEEEnd")==true && suspended is {} start){Record("Runtime.ExecutionEngineSuspension",(data.TimeStamp-start).TotalMilliseconds);suspended=null;RuntimeEvents++;}
    }
    private void Started(object? sender,DispatcherHookEventArgs e){operations[e.Operation]=Stopwatch.GetTimestamp();}
    private void Completed(object? sender,DispatcherHookEventArgs e){if(operations.Remove(e.Operation,out var start))Record("Dispatcher."+e.Operation.Priority,Stopwatch.GetElapsedTime(start).TotalMilliseconds);}
    public void Record(string name,double milliseconds)
    {
        lock(gate){if(!samples.TryGetValue(name,out var values))samples[name]=values=[];if(values.Count<16384)values.Add(milliseconds);}
    }
    public void Mark(string name)
    {
        lock(gate){if(timeline.Count<8192)timeline.Add(new{Name=name,Phase,Tick=Stopwatch.GetTimestamp(),Thread=Environment.CurrentManagedThreadId});}
    }
    public object Timeline(){lock(gate)return timeline.ToArray();}
    public static Scope Measure(string name)=>new(Current,name);
    public readonly struct Scope(AtlasProfiling? owner,string name):IDisposable
    {
        private readonly long start=owner==null?0:Stopwatch.GetTimestamp();
        public void Dispose(){if(owner!=null)owner.Record(name,Stopwatch.GetElapsedTime(start).TotalMilliseconds);}
    }
    public object Summary()
    {
        lock(gate)return samples.ToDictionary(p=>p.Key,p=>Statistics(p.Value));
    }
    public static object Statistics(IEnumerable<double> values)
    {
        var a=values.Order().ToArray();return new{Count=a.Length,Median=a.Length==0?0:a[a.Length/2],P50=a.Length==0?0:a[a.Length/2],P95=a.Length==0?0:a[(int)Math.Min(a.Length-1,Math.Ceiling(a.Length*.95)-1)],P99=a.Length<100?(double?)null:a[(int)Math.Min(a.Length-1,Math.Ceiling(a.Length*.99)-1)],Maximum=a.DefaultIfEmpty().Max(),Total=a.Sum()};
    }
    public override void Dispose(){dispatcher.Hooks.OperationStarted-=Started;dispatcher.Hooks.OperationCompleted-=Completed;Current=null;base.Dispose();}
}
