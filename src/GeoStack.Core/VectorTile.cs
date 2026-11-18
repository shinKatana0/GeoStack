// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Buffers.Binary;
using System.Text;

namespace GeoStack.Core;

public sealed record TileFeature(string Layer,int Type,int Extent,IReadOnlyDictionary<string,string> Properties,IReadOnlyList<(int X,int Y)[]> Paths);

/// <summary>Bounded Mapbox Vector Tile protobuf decoder; no scripting or native conversion dependency.</summary>
public static class VectorTile
{
    private sealed class Proto(byte[] data)
    {
        public int At;
        public bool More=>At<data.Length;
        public ulong Number()=>PmtilesSource.Varint(data,ref At);
        public byte[] Bytes(){var count=checked((int)Number());if(count<0 || count>data.Length-At)throw new InvalidDataException("Invalid protobuf length");var value=data.AsSpan(At,count).ToArray();At+=count;return value;}
        public void Skip(int wire){switch(wire){case 0:Number();break;case 1:At=checked(At+8);break;case 2:Bytes();break;case 5:At=checked(At+4);break;default:throw new InvalidDataException("Unsupported protobuf field");}if(At>data.Length)throw new InvalidDataException("Truncated protobuf");}
    }
    public static IReadOnlyList<TileFeature> Decode(byte[] bytes)
    {
        if(bytes.Length>32*1024*1024)throw new InvalidDataException("Vector tile budget exceeded");
        var tile=new Proto(bytes);var output=new List<TileFeature>();var points=0;
        while(tile.More)
        {
            var key=tile.Number();if(key!=26){tile.Skip((int)(key&7));continue;}
            var layer=new Proto(tile.Bytes());var name="";var extent=4096;var version=1;var keys=new List<string>();var values=new List<string>();var features=new List<byte[]>();
            while(layer.More)
            {
                var field=layer.Number();switch(field)
                {
                    case 10:name=Encoding.UTF8.GetString(layer.Bytes());break;
                    case 18:features.Add(layer.Bytes());break;
                    case 26:keys.Add(Encoding.UTF8.GetString(layer.Bytes()));break;
                    case 34:
                        var value=new Proto(layer.Bytes());var text="";
                        while(value.More){var tag=value.Number();if(tag==10)text=Encoding.UTF8.GetString(value.Bytes());else if((tag&7)==0)text=value.Number().ToString(System.Globalization.CultureInfo.InvariantCulture);else value.Skip((int)(tag&7));}
                        values.Add(text);break;
                    case 40:extent=checked((int)layer.Number());break;
                    case 120:version=checked((int)layer.Number());break;
                    default:layer.Skip((int)(field&7));break;
                }
                if(features.Count>50000 || keys.Count>10000 || values.Count>100000)throw new InvalidDataException("Vector layer budget exceeded");
            }
            if(extent<1 || extent>65536 || version is not (1 or 2))throw new InvalidDataException("Unsupported vector layer");
            foreach(var raw in features)
            {
                var feature=new Proto(raw);var tags=Array.Empty<ulong>();var commands=Array.Empty<ulong>();var type=0;
                ulong[] Packed(byte[] content){var p=new Proto(content);var numbers=new List<ulong>();while(p.More){if(numbers.Count>4000000)throw new InvalidDataException("Vector command budget exceeded");numbers.Add(p.Number());}return numbers.ToArray();}
                while(feature.More){var field=feature.Number();switch(field){case 18:tags=Packed(feature.Bytes());break;case 24:type=checked((int)feature.Number());break;case 34:commands=Packed(feature.Bytes());break;default:feature.Skip((int)(field&7));break;}}
                if(type is <1 or >3 || tags.Length%2!=0)throw new InvalidDataException("Invalid vector feature");
                var properties=new Dictionary<string,string>();for(var i=0;i<tags.Length;i+=2){if(tags[i]>=(ulong)keys.Count || tags[i+1]>=(ulong)values.Count)throw new InvalidDataException("Invalid vector properties");properties[keys[(int)tags[i]]]=values[(int)tags[i+1]];}
                var paths=new List<(int,int)[]>();var path=new List<(int,int)>();var x=0;var y=0;
                for(var at=0;at<commands.Length;)
                {
                    var cmd=commands[at++];var op=cmd&7;var count=cmd>>3;if(count==0 || count>2000000)throw new InvalidDataException("Invalid vector command");
                    if(op==7){if(type!=3 || count!=1)throw new InvalidDataException("Invalid polygon closure");continue;}
                    if(op is not (1 or 2))throw new InvalidDataException("Unsupported vector command");
                    for(ulong n=0;n<count;n++)
                    {
                        if(at+1>=commands.Length)throw new InvalidDataException("Truncated vector path");
                        if(op==1 && path.Count>0){paths.Add(path.ToArray());path.Clear();}
                        int Delta(ulong v)=>checked((int)(v>>1)) ^ -((int)v&1);
                        x=checked(x+Delta(commands[at++]));y=checked(y+Delta(commands[at++]));
                        if(Math.Abs((long)x)>extent*16L || Math.Abs((long)y)>extent*16L || ++points>2000000)throw new InvalidDataException("Vector geometry exceeds budget");path.Add((x,y));
                    }
                }
                if(path.Count>0)paths.Add(path.ToArray());output.Add(new(name,type,extent,properties,paths));if(output.Count>100000)throw new InvalidDataException("Vector tile feature budget exceeded");
            }
        }
        return output;
    }
}
