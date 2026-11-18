// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace GeoStack.Core;

public interface IRegionalMapSource : IDisposable
{
    long DownloadedBytes { get; }
    string Identity { get; }
    string Freshness { get; }
    Task Initialize(CancellationToken token);
    Task<byte[]?> Tile(int zoom,int x,int y,CancellationToken token);
}

/// <summary>Explicit-operation PMTiles v3 regional reader. Never used by normal map browsing.</summary>
public sealed class PmtilesSource : IRegionalMapSource
{
    public const string ProviderUrl="https://data.source.coop/protomaps/openstreetmap/v4.pmtiles";
    public const string Attribution="OpenStreetMap contributors (ODbL); Natural Earth; Protomaps. openstreetmap.org/copyright";
    public const long TransferLimit=128L*1024*1024;
    private readonly HttpClient client;
    private readonly Uri uri;
    private readonly BoundedLru<(long,int),byte[]> directories=new(32);
    private long directoryBytes;
    private byte[] header=[];
    private string? etag;
    private long objectLength;
    public long DownloadedBytes { get; private set; }
    public string Identity=>etag??"";
    public string Freshness { get; private set; }="Unknown";
    public PmtilesSource(HttpMessageHandler? handler=null,Uri? source=null)
    {
        uri=source??new(ProviderUrl);
        if(uri.Scheme!="https" || uri.Host!="data.source.coop")throw new ArgumentException("Unsupported map provider");
        client=new(handler??new HttpClientHandler{AllowAutoRedirect=false});client.Timeout=TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GeoStack/0.5");
    }
    public async Task Initialize(CancellationToken token)
    {
        header=await Range(0,127,token);
        if(!header.AsSpan(0,7).SequenceEqual("PMTiles"u8) || header[7]!=3 || header[99]!=1 || header[97] is not (1 or 2) || header[98] is not (1 or 2) || header[101]<15)throw new InvalidDataException("Unsupported PMTiles archive");
        var metadata=Expand(await Range(Number(24),checked((int)Number(32)),token),header[97]);
        using var document=JsonDocument.Parse(metadata);
        if(document.RootElement.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Unsupported source metadata");
        if(document.RootElement.TryGetProperty("planetiler:osm:osmosisreplicationtime",out var date) && date.ValueKind==JsonValueKind.String)Freshness=date.GetString()??"Unknown";
    }
    private long Number(int at)=>checked((long)BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(at,8)));
    private async Task<byte[]> Range(long offset,int length,CancellationToken token)
    {
        if(offset<0 || length<1 || length>4*1024*1024 || DownloadedBytes+length>TransferLimit || objectLength>0 && offset>objectLength-length)throw new InvalidDataException("Source range budget exceeded");
        for(var attempt=0;;attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var request=new HttpRequestMessage(HttpMethod.Get,uri);request.Headers.Range=new(offset,offset+length-1);
                if(etag!=null)request.Headers.IfMatch.Add(EntityTagHeaderValue.Parse(etag));
                using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
                if(response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway)throw new HttpRequestException("Provider temporarily unavailable");
                var range=response.Content.Headers.ContentRange;
                if(response.StatusCode!=HttpStatusCode.PartialContent || range?.From!=offset || range.To!=offset+length-1 || range.Length is not >127)throw new InvalidDataException("Provider did not honor bounded range");
                var identity=response.Headers.ETag?.ToString();
                if(string.IsNullOrEmpty(identity) || identity.StartsWith("W/",StringComparison.Ordinal) || etag!=null && identity!=etag)throw new InvalidDataException("Source identity changed or cannot be pinned");
                etag=identity;objectLength=range.Length.Value;
                await using var stream=await response.Content.ReadAsStreamAsync(token);var data=new byte[length];
                var position=0;
                while(position<length){var read=await stream.ReadAsync(data.AsMemory(position),token);if(read==0)throw new HttpRequestException("Incomplete map source response");position+=read;DownloadedBytes+=read;if(DownloadedBytes>TransferLimit)throw new InvalidDataException("Transfer budget exceeded");}
                var extra=new byte[1];if(await stream.ReadAsync(extra,token)!=0)throw new InvalidDataException("Oversized source range");
                return data;
            }
            catch(TaskCanceledException ex) when(!token.IsCancellationRequested){if(attempt>=2)throw new HttpRequestException("Map provider timed out",ex);await Task.Delay(TimeSpan.FromMilliseconds(300*(attempt+1)),token);}
            catch(HttpRequestException) when(attempt<2){await Task.Delay(TimeSpan.FromMilliseconds(300*(attempt+1)),token);}
        }
    }
    internal static byte[] Expand(byte[] data,int compression)
    {
        if(compression==1)return data;
        if(compression!=2)throw new InvalidDataException("Unsupported source compression");
        using var gzip=new GZipStream(new MemoryStream(data),CompressionMode.Decompress);using var output=new MemoryStream();var buffer=new byte[65536];int n;
        while((n=gzip.Read(buffer))>0){if(output.Length+n>32*1024*1024)throw new InvalidDataException("Expanded source exceeds budget");output.Write(buffer,0,n);}return output.ToArray();
    }
    public static ulong TileId(int zoom,int x,int y)
    {
        if(zoom<0 || zoom>22 || x<0 || y<0 || x>=(1<<zoom) || y>=(1<<zoom))throw new ArgumentOutOfRangeException(nameof(zoom));
        ulong id=((1UL<<(2*zoom))-1)/3;
        for(var bit=zoom-1;bit>=0;bit--){var s=1<<bit;var rx=x&s;var ry=y&s;id+=(ulong)(((3*rx)^ry))<<bit;if(ry==0){if(rx!=0){x=s-1-x;y=s-1-y;}(x,y)=(y,x);}}
        return id;
    }
    public async Task<byte[]?> Tile(int zoom,int x,int y,CancellationToken token)
    {
        if(header.Length!=127)throw new InvalidOperationException("Initialize source after confirmation first");
        var id=TileId(zoom,x,y);var offset=Number(8);var length=checked((int)Number(16));
        for(var depth=0;depth<4;depth++)
        {
            if(!directories.TryGet((offset,length),out var data)){data=Expand(await Range(offset,length,token),header[97]);if(data.Length>16*1024*1024)throw new InvalidDataException("Directory budget exceeded"); if(directoryBytes+data.Length>16*1024*1024){directories.Clear();directoryBytes=0;} directories.Set((offset,length),data);directoryBytes+=data.Length;}
            var entries=Directory(data);var found=entries.LastOrDefault(e=>e.Id<=id);
            if(found==null)return null;
            if(found.Run==0){offset=checked(Number(40)+(long)found.Offset);length=checked((int)found.Length);continue;}
            if(id-found.Id>=found.Run)return null;
            return Expand(await Range(checked(Number(56)+(long)found.Offset),checked((int)found.Length),token),header[98]);
        }
        throw new InvalidDataException("Source directory depth exceeded");
    }
    private sealed record Entry(ulong Id,ulong Run,ulong Length,ulong Offset);
    internal static ulong Varint(byte[] data,ref int at)
    {
        ulong value=0;for(var shift=0;shift<64;shift+=7){if(at>=data.Length)throw new InvalidDataException("Truncated varint");var b=data[at++];if(shift==63 && b>1)throw new InvalidDataException("Varint overflow");value|=(ulong)(b&127)<<shift;if(b<128)return value;}throw new InvalidDataException("Invalid varint");
    }
    private static Entry[] Directory(byte[] data)
    {
        var p=0;var count=checked((int)Varint(data,ref p));if(count>100000)throw new InvalidDataException("Directory too large");
        var ids=new ulong[count];var runs=new ulong[count];var lengths=new ulong[count];var offsets=new ulong[count];ulong id=0;
        for(var i=0;i<count;i++){id=checked(id+Varint(data,ref p));ids[i]=id;}
        for(var i=0;i<count;i++)runs[i]=Varint(data,ref p);
        for(var i=0;i<count;i++)lengths[i]=Varint(data,ref p);
        for(var i=0;i<count;i++){var n=Varint(data,ref p);offsets[i]=n==0 && i>0?checked(offsets[i-1]+lengths[i-1]):checked(n-1);}
        return Enumerable.Range(0,count).Select(i=>new Entry(ids[i],runs[i],lengths[i],offsets[i])).ToArray();
    }
    public void Dispose()=>client.Dispose();
}
