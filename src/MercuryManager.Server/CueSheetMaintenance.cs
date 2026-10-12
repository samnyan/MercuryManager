using System.Text;
namespace MercuryManager.Server;

// Contracts recovered from the UE4.19 Win64 CRI consumers, not a generic ADX2 validator.
public static class CueSheetMaintenance
{
    public const int StreamWorkPerPort = 2048 + 8 + 8;
    public const int WorkMargin = 64;
    public const int MaxWorkBytes = 16 * 1024 * 1024;
    public static bool Has(CriUtf table, string column) => table.Columns.Any(c => c.Name == column);
    public static int CompareNames(string a, string b) => Encoding.UTF8.GetBytes(a).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(b));
    public static int RecommendedWork(int ports) => checked(ports * StreamWorkPerPort + WorkMargin);
    public static int StreamCount(CriUtf top) => Has(top,"StreamAwbHash") && top.Blob(0,"StreamAwbHash").Length > 0
        ? CriUtf.Read(top.Blob(0,"StreamAwbHash")).Rows.Count : 0;
    public static void RequireModern(CriUtf top)
    {
        if(top.Rows.Count!=1 || top.Number(0,"Version") is <0x1260000 or >0x1300000 ||
           top.Columns.FindIndex(c=>c.Name=="StreamAwbTocWork")!=62 ||
           top.Columns.FindIndex(c=>c.Name=="StreamAwbHash")!=27 ||
           top.Columns.FindIndex(c=>c.Name=="StreamAwbAfs2Header")!=63)
            throw new InvalidDataException("Unsupported stream layout; only the verified modern Win64 ACB layout can be maintained.");
    }
    public static bool SortCueNames(CriUtf top)
    {
        if(!Has(top,"CueNameTable") || top.Blob(0,"CueNameTable").Length==0)return false;
        var names=CriUtf.Read(top.Blob(0,"CueNameTable"));var cues=CriUtf.Read(top.Blob(0,"CueTable"));
        var order=Enumerable.Range(0,names.Rows.Count).OrderBy(i=>names.Text(i,"CueName"),Comparer<string>.Create(CompareNames)).ToArray();
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(int i in order)
        {
            if(!seen.Add(names.Text(i,"CueName")))throw new InvalidDataException("Duplicate CueName; automatic sorting cannot choose a definition.");
            if(names.Number(i,"CueIndex")>=cues.Rows.Count)throw new InvalidDataException("Invalid CueName CueIndex.");
        }
        if(order.SequenceEqual(Enumerable.Range(0,names.Rows.Count)))return false;
        var rows=order.Select(i=>names.Rows[i]).ToArray();names.Rows.Clear();names.Rows.AddRange(rows);
        top.SetBlob(0,"CueNameTable",names.Write());return true;
    }
    public static int LookupName(CriUtf names,string column,string name)
    {
        // Exact midpoint/interval updates of the cooked strcmp search, including its failure sentinel.
        int lo=0,hi=names.Rows.Count-1,mid=names.Rows.Count>>1;
        while(lo<=hi)
        {
            int cmp=CompareNames(names.Text(mid,column),name);if(cmp==0)return mid;
            if(lo==hi)break;
            int next;
            if(cmp>0){next=lo+((mid-lo)>>1);if(((mid-lo)&1)!=0)next--;hi=mid-1;next=Math.Max(lo,next);}
            else{next=mid+((hi-mid)>>1);if(((hi-mid)&1)!=0)next++;lo=mid+1;next=Math.Min(hi,next);}
            mid=next;
        }
        return -1;
    }
    public static bool SortStreamSlots(CriUtf top)
    {
        int count=StreamCount(top);if(count==0)return false;RequireModern(top);
        var slots=CriUtf.Read(top.Blob(0,"StreamAwbHash"));var headers=CriUtf.Read(top.Blob(0,"StreamAwbAfs2Header"));
        if(headers.Rows.Count!=count)throw new InvalidDataException("Stream slot/header count mismatch; cannot infer missing headers.");
        var order=Enumerable.Range(0,count).OrderBy(i=>slots.Text(i,"Name"),Comparer<string>.Create(CompareNames)).ToArray();
        var names=order.Select(i=>slots.Text(i,"Name")).ToArray();
        if(names.Distinct(StringComparer.Ordinal).Count()!=count)throw new InvalidDataException("Duplicate stream slot names.");
        foreach(string name in names)AudioBanks.ValidateName(name+".awb");
        if(names[0]!=top.Text(0,"Name"))throw new InvalidDataException("Primary AWB must remain port 0 and first in strcmp order; choose an extension bank name after the sheet name.");
        if(order.SequenceEqual(Enumerable.Range(0,count)))return false;
        var map=new int[count];for(int i=0;i<count;i++)map[order[i]]=i;
        var waves=CriUtf.Read(top.Blob(0,"WaveformTable"));
        for(int i=0;i<waves.Rows.Count;i++)if(waves.Number(i,"Streaming")!=0)
        {
            int old=checked((int)waves.Number(i,"StreamAwbPortNo"));if(old>=count)throw new InvalidDataException("Invalid waveform port.");
            waves.SetNumber(i,"StreamAwbPortNo",map[old]);
        }
        var sr=order.Select(i=>slots.Rows[i]).ToArray();var hr=order.Select(i=>headers.Rows[i]).ToArray();
        slots.Rows.Clear();slots.Rows.AddRange(sr);headers.Rows.Clear();headers.Rows.AddRange(hr);
        top.SetBlob(0,"WaveformTable",waves.Write());top.SetBlob(0,"StreamAwbHash",slots.Write());top.SetBlob(0,"StreamAwbAfs2Header",headers.Write());return true;
    }
    public static void RebuildStreamWork(CriUtf top,int? size=null)
    {
        RequireModern(top);int count=StreamCount(top),bytes=size??RecommendedWork(count);
        if(bytes<checked(count*StreamWorkPerPort+7)||bytes>MaxWorkBytes)throw new ArgumentException("Work bytes must cover all ports plus alignment slack and stay within 16 MiB.");
        top.SetBlob(0,"StreamAwbTocWork",new byte[bytes]);
    }
    public static byte[] Normalize(byte[] acb)
    {
        var top=CriUtf.Read(acb);SortCueNames(top);int count=StreamCount(top);
        if(count>0)
        {
            SortStreamSlots(top);var work=top.Blob(0,"StreamAwbTocWork");
            if(work.Length<RecommendedWork(count)||work.Any(b=>b!=0))RebuildStreamWork(top);
        }
        return top.Write();
    }
    public static void VerifyManaged(byte[] acb)
    {
        var top=CriUtf.Read(acb);var names=CriUtf.Read(top.Blob(0,"CueNameTable"));
        for(int i=0;i<names.Rows.Count;i++)if(LookupName(names,"CueName",names.Text(i,"CueName"))!=i)throw new InvalidDataException("CueName runtime lookup failed.");
        int count=StreamCount(top);if(count==0)return;RequireModern(top);
        var slots=CriUtf.Read(top.Blob(0,"StreamAwbHash"));
        if(slots.Text(0,"Name")!=top.Text(0,"Name"))throw new InvalidDataException("Wrong primary stream slot.");
        for(int i=0;i<count;i++)if(LookupName(slots,"Name",slots.Text(i,"Name"))!=i)throw new InvalidDataException("Stream slot runtime lookup failed.");
        var work=top.Blob(0,"StreamAwbTocWork");int loss=(-top.BlobOffset(0,"StreamAwbTocWork"))&7;
        if(work.Length-loss<count*StreamWorkPerPort)throw new InvalidDataException("Stream awb work overflow.");
    }
}
