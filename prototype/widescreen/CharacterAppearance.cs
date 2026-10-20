using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class CharacterAppearance
{
    public int Version=1,Width,Height,Direction;
    public string Key,Appearance,Png;
    public DateTime CapturedUtc;
    internal static readonly string Folder=Path.Combine(LocalProfile.Root,"character-appearances");
    internal static string FilePath(string key,string folder=null)
    {
        if(key==null||key.Length!=64||key.Any(c=>!Uri.IsHexDigit(c)))throw new InvalidDataException("Invalid appearance key.");
        return Path.Combine(folder??Folder,key+".json");
    }
    internal void Save(string folder=null){Validate();LocalProfile.Write(FilePath(Key,folder),new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(this));}
    internal void Validate()
    {
        FilePath(Key);if(Version!=1||Width<1||Height<1||Width>256||Height>256||Direction<0||Direction>1||Png==null||Png.Length>400000)throw new InvalidDataException();
        var look=Convert.FromBase64String(Appearance??"");if(look.Length!=48)throw new InvalidDataException();
    }
    internal Bitmap Image()
    {
        Validate();var bytes=Convert.FromBase64String(Png);
        byte[] signature={137,80,78,71,13,10,26,10};
        if(bytes.Length<33||!bytes.Take(8).SequenceEqual(signature)||bytes[12]!=73||bytes[13]!=72||bytes[14]!=68||bytes[15]!=82)throw new InvalidDataException();
        Func<int,int> number=o=>(bytes[o]<<24)|(bytes[o+1]<<16)|(bytes[o+2]<<8)|bytes[o+3];
        if(number(16)!=Width||number(20)!=Height)throw new InvalidDataException();
        using(var stream=new MemoryStream(bytes))using(var image=new Bitmap(stream))return new Bitmap(image);
    }
    internal static CharacterAppearance Load(string key,string folder=null)
    {
        try
        {
            string path=FilePath(key,folder);if(!File.Exists(path)||new FileInfo(path).Length>600000)return null;
            var result=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<CharacterAppearance>(File.ReadAllText(path));if(result==null||result.Key!=key)return null;result.Validate();return result;
        }catch{return null;}
    }
    internal static string Encode(Bitmap image)
    {using(var stream=new MemoryStream()){image.Save(stream,ImageFormat.Png);return Convert.ToBase64String(stream.ToArray());}}
}

// Read only the local User object's already-composed sprite. No game inputs,
// inventory text, chat, world screenshots, or other players are captured.
internal static class CharacterAppearanceReader
{
    internal static byte[] CleanAppearance(byte[] raw)
    {
        if(raw.Length!=48)throw new InvalidDataException();var clean=new byte[48];
        foreach(int offset in new[]{0,2,3,4,6,7,8,10,11,12,13,14,16,17,18,20,21,22,24,25,26,27,28,29,30,32,33,34,35,36,38,39,40,42,43,44,45,46,47})clean[offset]=raw[offset];
        return clean; // Never serialize uninitialized alignment/padding bytes.
    }
    internal static Bitmap Decode(byte[] raw,int width,int height,int pitch,bool rgb565)
    {
        if(width<1||height<1||width>256||height>256||pitch<width*2||pitch>1024||raw.Length!=pitch*height)throw new InvalidDataException();
        var pixels=new int[width*height];int visible=0,left=width,top=height,right=0,bottom=0;
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            int at=y*pitch+x*2,value=raw[at]|raw[at+1]<<8;if(value==0)continue;
            int r=(value>>(rgb565?11:10))&31,g=(value>>5)&(rgb565?63:31),b=value&31;
            pixels[y*width+x]=unchecked((int)0xff000000)|(r*255/31<<16)|(g*255/(rgb565?63:31)<<8)|b*255/31;visible++;
            left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x+1);bottom=Math.Max(bottom,y+1);
        }
        if(visible<8)throw new InvalidDataException("Character sprite is not ready.");
        int cropWidth=right-left,cropHeight=bottom-top;
        var image=new Bitmap(cropWidth,cropHeight,PixelFormat.Format32bppArgb);
        var data=image.LockBits(new Rectangle(0,0,cropWidth,cropHeight),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
        try{for(int y=0;y<cropHeight;y++)Marshal.Copy(pixels,(y+top)*width+left,new IntPtr(data.Scan0.ToInt64()+y*data.Stride),cropWidth);}finally{image.UnlockBits(data);}
        return image;
    }
    internal static CharacterAppearance Read(GameFrame frame,string name,string server)
    {return Read(frame.Read,frame.U32,name,server,frame.HasFrameSequence,DateTime.UtcNow);}
    internal static CharacterAppearance Read(Action<uint,byte[],int> read,Func<uint,uint> u,string name,string server,bool sequenceAvailable,DateTime now)
    {
        var nameBytes=new byte[16];read(0x73d910,nameBytes,16);int nameEnd=Array.IndexOf(nameBytes,(byte)0);
        if(nameEnd<2||!String.Equals(System.Text.Encoding.ASCII.GetString(nameBytes,0,nameEnd),name,StringComparison.OrdinalIgnoreCase))return null;
        uint sequence=sequenceAvailable?u(0x8e241c):0;if((sequence&1)!=0)return null;
        uint map=u(0x73d960);if(map<0x102e8)return null;uint world=map-0x2e8;
        uint list=u(world+0x194),id=u(world+0x214);if(list<0x10000||u(list)!=0x68ba70||id==0)return null;
        uint head=u(list+0x20);if(head<0x10000)return null;uint node=u(head+4),obj=0;
        for(int i=0;i<64&&node>=0x10000&&node!=head;i++)
        {uint key=u(node+12);if(key==id){obj=u(node+16);break;}uint next=u(node+(id<key?0u:8u));if(next==node)return null;node=next;}
        if(obj<0x10000||u(obj)!=0x68ba2c||u(obj+0x24)!=id)return null;
        var body=new byte[0x112];read(obj,body,body.Length);
        if(body[0x98]!=1||body[0x104]!=1||body[0xfc]!=0||body[0x105]!=0)return null;
        // Keep the last ordinary outfit while dead, swimming, or disguised.
        int bodySprite=BitConverter.ToUInt16(body,0xaa);if(bodySprite!=1&&bodySprite!=4)return null;
        var look=new byte[48];Array.Copy(body,0xa4,look,0,48);look=CleanAppearance(look);
        uint canvas=u(obj+0xd8);if(canvas<0x10000)return null;
        var header=new byte[0xf8];read(canvas,header,header.Length);
        int w=BitConverter.ToInt32(header,0xe8),h=BitConverter.ToInt32(header,0xec),pitch=BitConverter.ToInt32(header,0x9c);
        uint allocation=BitConverter.ToUInt32(header,0x98);
        if(BitConverter.ToUInt32(header,0x6c)!=4||header[0xd4]!=0||w<1||h<1||w>256||h>256||pitch<w*2||pitch>1024||allocation<0x10000)return null;
        byte[] pixels=new byte[pitch*h],confirm=new byte[pixels.Length];read(allocation+16,pixels,pixels.Length);read(allocation+16,confirm,confirm.Length);
        var after=new byte[body.Length];read(obj,after,after.Length);var afterHeader=new byte[header.Length];read(canvas,afterHeader,afterHeader.Length);
        var direction=new byte[1];read(obj+0x192,direction,1);
        // The saved login portrait should face toward the viewer: left (0)
        // or down (1). Keep the previous image while facing right/up.
        if(!body.SequenceEqual(after)||!header.SequenceEqual(afterHeader)||!pixels.SequenceEqual(confirm)||direction[0]>1||u(0x73d960)!=map||u(world+0x194)!=list||u(world+0x214)!=id||u(node+16)!=obj)return null;
        var nameAfter=new byte[16];read(0x73d910,nameAfter,16);
        if(!nameBytes.SequenceEqual(nameAfter)||(sequenceAvailable&&u(0x8e241c)!=sequence))return null;
        using(var image=Decode(pixels,w,h,pitch,u(0x6d8fdc)!=0))
            return new CharacterAppearance{Key=LocalProfile.CharacterKey(name,server),Appearance=Convert.ToBase64String(look),Direction=direction[0],Width=image.Width,Height=image.Height,CapturedUtc=now,Png=CharacterAppearance.Encode(image)};
    }
}

internal sealed class CharacterAppearanceRecorder
{
    Task work;DateTime nextRead,lastSave;CharacterAppearance latest;bool finished;
    internal volatile bool Changed;
    internal void Tick(GameFrame frame,GameTools tools,ServerProfile server)
    {
        if(finished)return;
        if(work!=null){if(!work.IsCompleted)return;work=null;}
        if(DateTime.UtcNow<nextRead)return;nextRead=DateTime.UtcNow.AddSeconds(15);
        work=Task.Run(()=>Capture(frame,tools,server,false));
    }
    void Capture(GameFrame frame,GameTools tools,ServerProfile server,bool flush)
    {
        try
        {
            string name=tools.CharacterName();CharacterAppearance snapshot=null;
            if(!String.IsNullOrEmpty(name))for(int attempt=0;attempt<3&&snapshot==null;attempt++)
            {snapshot=CharacterAppearanceReader.Read(frame,name,server.Key);if(snapshot==null&&attempt<2)System.Threading.Thread.Sleep(5);}
            if(snapshot!=null)
            {
                bool first=latest==null||latest.Key!=snapshot.Key;
                bool preferredViewChanged=!first&&latest.Direction!=snapshot.Direction;
                bool outfitChanged=!first&&latest.Appearance!=snapshot.Appearance;
                if(first&&latest!=null){latest.Save();Changed=true;}
                latest=snapshot;
                // A newly chosen left/down view should replace the old portrait
                // promptly instead of waiting for the ten-minute outfit refresh.
                if(first||preferredViewChanged||outfitChanged||flush||(DateTime.UtcNow-lastSave).TotalMinutes>=10)
                {latest.Save();lastSave=DateTime.UtcNow;Changed=true;}
            }
            else if(latest!=null&&(flush||!frame.IsInWorldNow))
            {latest.Save();lastSave=DateTime.UtcNow;Changed=true;latest=null;}
        }catch{if(flush&&latest!=null)try{latest.Save();Changed=true;}catch{}}
    }
    internal void Flush(GameFrame frame,GameTools tools,ServerProfile server)
    {if(finished)return;finished=true;if(work!=null){try{work.GetAwaiter().GetResult();}catch{}work=null;}Capture(frame,tools,server,true);latest=null;}
}

internal sealed class CharacterPortraitView : Control
{
    internal Bitmap Portrait;internal string Character="Select a character",Detail="Last login activity will appear here.";
    internal CharacterPortraitView(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);Font=DATheme.Font(14);ForeColor=DATheme.Gold;BackColor=DATheme.Dark;}
    internal static void DrawSprite(Graphics g,Bitmap image,Rectangle area)
    {
        if(image==null)return;int scale=Math.Max(1,Math.Min(4,Math.Min(area.Width/image.Width,area.Height/image.Height)));
        var dest=new Rectangle(area.X+(area.Width-image.Width*scale)/2,area.Y+(area.Height-image.Height*scale)/2,image.Width*scale,image.Height*scale);
        g.InterpolationMode=InterpolationMode.NearestNeighbor;g.PixelOffsetMode=PixelOffsetMode.Half;g.DrawImage(image,dest,0,0,image.Width,image.Height,GraphicsUnit.Pixel);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        DATheme.Paint(e.Graphics,ClientRectangle,false);var area=new Rectangle(10,34,Width-20,Math.Max(1,Height-84));
        if(Portrait!=null)DrawSprite(e.Graphics,Portrait,area);
        else
        {
            using(var p=new Pen(Color.FromArgb(137,103,58),2))
            {int cx=Width/2,cy=area.Top+area.Height/2;e.Graphics.DrawEllipse(p,cx-12,cy-30,24,24);e.Graphics.DrawArc(p,cx-28,cy,56,50,180,180);}
        }
        TextRenderer.DrawText(e.Graphics,Character,Font,new Rectangle(10,10,Width-20,25),ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics,Detail,Font,new Rectangle(12,Height-43,Width-24,38),ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.WordBreak);
    }
}
