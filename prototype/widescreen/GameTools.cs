using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

// 7.41 layouts checked against SleepHunter4's MIT-licensed ClientLayout and
// ability parser. See THIRD-PARTY-NOTICES.txt. No packet interception is used.
internal sealed class LearnedAbility
{
    internal int Slot, Argument;
    internal bool Spell;
    internal string Name, Caption;
    internal int Panel {get {return Slot>72?9:Spell?(Slot>36?2:1):(Slot>36?4:3);}}
    internal string Key {get {return (Spell?"spell:":"skill:")+Slot+":"+Name;}}
    public override string ToString(){return (Spell?"Spell · ":"Skill · ")+Caption;}
}

internal sealed class PlayContext
{
    internal uint Root, Character, Map;
    internal int X,Y,Panel;
    internal bool Compact, Chat, Dialog, Transfer, Alive;
    internal bool CanTrain {get {return Alive && !Compact && !Chat && !Dialog && !Transfer;}}
}

internal sealed class GameTools : IDisposable
{
    readonly GameFrame frame;
    IntPtr writer;
    uint ownedMap;
    int originalScale,lastScale;
    internal int DesiredMapScale; // zero leaves native settings alone
    internal bool MapVisible;
    internal int MapScale;
    internal int MapRefreshCount;
    internal GameTools(GameFrame value){frame=value;}
    internal ProgressSample Progression(string server)
    {
        string name=CharacterName();if(String.IsNullOrEmpty(name))return null;
        uint root=U(0x73d964),gui=U(0x82b768);if(root<0x10020||gui<0x10000)return null;
        uint character=U(root-0x20),stats=U(gui+0x4e10);if(character<0x10000||stats<0x10000)return null;
        var result=new ProgressSample{Identity=server+":"+name.ToLowerInvariant(),Experience=U(character+0x1060),Ability=U(stats+0x1e0)};
        if(U(0x73d964)!=root||U(root-0x20)!=character||U(0x82b768)!=gui||U(gui+0x4e10)!=stats)return null;
        return result;
    }
    byte B(uint a){var b=new byte[1];frame.Read(a,b,1);return b[0];}
    uint U(uint a){return frame.U32(a);}
    internal string CharacterName()
    {
        if(!frame.IsInWorldNow)return "";
        var name=new byte[16];var confirm=new byte[16];
        frame.Read(0x73d910,name,16);frame.Read(0x73d910,confirm,16);
        for(int i=0;i<16;i++)if(name[i]!=confirm[i])return "";
        int end=Array.IndexOf(name,(byte)0);if(end<2)return "";
        string text=Encoding.ASCII.GetString(name,0,end);
        return Regex.IsMatch(text,"^[A-Za-z]{2,15}$")?text:"";
    }
    internal PlayContext Context()
    {
        if(!frame.InWorld)throw new InvalidOperationException("Enter the world first.");
        uint root=U(0x73d964),ui=U(0x82b768),world=U(0x73d960);
        if(root<0x10020 || ui<0x10000 || world<0x102e8)throw new InvalidOperationException("The world is changing.");
        uint character=U(root-0x20);
        if(character<0x10000)throw new InvalidOperationException("The character is loading.");
        var s=new PlayContext {Root=root,Character=character,Map=U(root-0x80),X=unchecked((int)U(root-0xb0)),Y=unchecked((int)U(root-0xb4)),
            Panel=B(ui+0x4e18),Compact=B(ui+0x4c60)!=0,Transfer=B(root-0x77)!=0,Alive=U(character+0x1078)>0};
        uint input=U(0x6d9260),focus=input==0?0:U(input+0x444);
        if(focus!=0)
        {
            uint vt=U(focus);
            // Any visible native text editor blocks automation, including
            // chat, mail and dialog fields with IDs other than the chat ID.
            s.Chat=(vt==0x682fec || vt==0x68306c || vt==0x6830ec) && B(focus+0x130)!=0;
            if(U(input+0x444)!=focus)throw new InvalidOperationException("The game input focus changed.");
        }
        uint dispatcher=U(0x73d944);
        // The native window is parked behind the host. Its focused-pane
        // pointer can be null while the visible chat editor still receives
        // forwarded keys, so also inspect the active pane tree.
        s.Chat=s.Chat || NativeChatGuard.Visible(frame.Read,U,dispatcher);
        s.Dialog=dispatcher!=0 && U(dispatcher)==0x672a84;
        if(s.Panel>10 || U(0x73d964)!=root || U(root-0x20)!=character || U(0x73d960)!=world || U(root-0x80)!=s.Map)
            throw new InvalidOperationException("The world is changing.");
        return s;
    }
    internal static List<LearnedAbility> ParseBook(byte[] bytes,bool spell)
    {
        int size=spell?518:260,start=spell?5:4;
        if(bytes.Length!=89*size)throw new ArgumentException("Unexpected ability table size.");
        var list=new List<LearnedAbility>();
        for(int i=0;i<89;i++)
        {
            int offset=i*size;
            if(BitConverter.ToUInt16(bytes,offset)==0)continue;
            int length=0;
            while(length<256 && bytes[offset+start+length]!=0)
            {
                byte ch=bytes[offset+start+length];
                if(ch<32 || ch>126)throw new InvalidOperationException("Ability data is still loading.");
                length++;
            }
            if(length==0 || length==256)throw new InvalidOperationException("Ability data is still loading.");
            string caption=Encoding.ASCII.GetString(bytes,offset+start,length).Trim();
            string name=Regex.Replace(caption,@"\s*\(Lev:\s*\d+\s*/\s*\d+\)\s*$","",RegexOptions.IgnoreCase);
            list.Add(new LearnedAbility {Slot=i+1,Spell=spell,Name=name,Caption=caption,Argument=spell?bytes[offset+4]:5});
        }
        return list;
    }
    internal List<LearnedAbility> Abilities()
    {
        PlayContext before=Context();var result=new List<LearnedAbility>();
        foreach(bool spell in new[]{false,true})
        {
            var bytes=new byte[89*(spell?518:260)];var confirm=new byte[bytes.Length];
            uint address=before.Character+(spell?0x4dfau:0x10210u);
            frame.Read(address,bytes,bytes.Length);frame.Read(address,confirm,confirm.Length);
            for(int i=0;i<bytes.Length;i++)if(bytes[i]!=confirm[i])throw new InvalidOperationException("Abilities changed; refresh again.");
            result.AddRange(ParseBook(bytes,spell));
        }
        if(U(0x73d964)!=before.Root || U(before.Root-0x20)!=before.Character)throw new InvalidOperationException("The character changed.");
        return result;
    }
    uint Minimap()
    {
        if(!frame.InWorld)return 0;
        uint world=U(0x73d960);
        if(world<0x102e8)return 0;
        uint map=U(world-0x2e8+0x1e0);
        // WorldMinimapInterface; verified vtable and two method entries.
        if(map<0x10190 || U(map)!=0x68ae5c || U(0x68ae5c+0x2c)!=0x5d8b50 || U(0x68ae5c+0x30)!=0x5d8b70)
            throw new InvalidOperationException("Tab map is not ready.");
        if(U(0x73d960)!=world)return 0;
        return map;
    }
    void Write(uint address,byte[] value)
    {
        if(writer==IntPtr.Zero)writer=ToolNative.OpenProcess(0x28,false,frame.ProcessId);
        if(writer==IntPtr.Zero)throw new InvalidOperationException("Tab zoom could not open the isolated client.");
        IntPtr done;
        if(!ToolNative.WriteProcessMemory(writer,new IntPtr(unchecked((int)address)),value,new IntPtr(value.Length),out done) || done.ToInt32()!=value.Length)
            throw new InvalidOperationException("Tab zoom could not update the map setting.");
    }
    void Scale(uint map,int scale)
    {
        if(scale<1 || scale>10 || Minimap()!=map)throw new InvalidOperationException("Tab map changed before the update.");
        // Change only the existing integer scale and refresh flag, never the
        // visibility mask, map data, class, code pages, or renderer dimensions.
        Write(map+0x43c,BitConverter.GetBytes(scale));
        if(Minimap()==map)Write(map+0x30,new byte[]{1});
        ToolNative.InvalidateRect(frame.Window,IntPtr.Zero,false);
        lastScale=scale;
        TapKey(Keys.F5);
        MapRefreshCount++;
    }
    internal void PollMap()
    {
        uint map=Minimap();
        MapVisible=map!=0 && B(map+0x32)!=0;
        if(map==0){MapScale=0;return;}
        MapScale=checked((int)U(map+0x43c));
        if(DesiredMapScale==0){RestoreMap();return;}
        if(DesiredMapScale<1 || DesiredMapScale>10)throw new InvalidOperationException("Tab scale must be between 1 and 10.");
        if(!MapVisible)return;
        if(ownedMap!=map){ownedMap=map;originalScale=MapScale;lastScale=MapScale;}
        if(MapScale!=DesiredMapScale){Scale(map,DesiredMapScale);MapScale=DesiredMapScale;}
    }
    void RestoreMap()
    {
        if(ownedMap!=0)
        {
            uint saved=ownedMap;ownedMap=0;
            if(originalScale>=1 && originalScale<=10 && originalScale!=lastScale && Minimap()==saved && U(saved+0x43c)==lastScale)Scale(saved,originalScale);
        }
    }
    internal static Point SlotPoint(LearnedAbility a,int height)
    {
        if(a.Slot<1 || a.Slot>89)throw new ArgumentOutOfRangeException("slot");
        int world=a.Slot>72?1:0,relative=(a.Slot-1)%(world==1?18:36),columns=world==1?6:12;
        return new Point(110+(relative%columns+(world==1 && a.Spell?6:0))*35,350+relative/columns*35+height-480);
    }
    internal static Point SelfPoint(int width,int height){return new Point(315+(width-640)/2,160+(height-480)/2);}
    internal void Click(Point p,bool twice,bool shift)
    {
        if(p.X<0 || p.X>=frame.Width || p.Y<0 || p.Y>=frame.Height)throw new InvalidOperationException("Click outside the game.");
        if(shift)Key(0x10,true);
        try
        {
            for(int i=0;i<(twice?2:1);i++)
            {
                var at=new IntPtr((p.Y<<16)|(p.X&65535));
                Post(0x200,shift?4:0,at);Post(0x201,shift?5:1,at);Post(0x202,shift?4:0,at);
            }
        }
        finally {if(shift)Key(0x10,false);}
    }
    void Post(uint message,int key,IntPtr at)
    {
        if(!HostNative.PostMessage(frame.Window,message,new IntPtr(key),at))throw new InvalidOperationException("The game input queue is unavailable.");
    }
    void Key(int key,bool down)
    {
        int code=1|((int)ToolNative.MapVirtualKey((uint)key,0)<<16);
        if(!down)code|=unchecked((int)0xc0000000);
        Post(down?0x100u:0x101u,key,new IntPtr(code));
    }
    internal void CancelTarget(){Key(0x1b,true);Key(0x1b,false);}
    internal void TapKey(Keys key){Key((int)key,true);Key((int)key,false);}
    internal void SelectPanel(LearnedAbility a,int panel)
    {
        bool medenia=a.Panel==2 || a.Panel==4;
        // The same button cycles Temuair/Medenia: returning from Medenia
        // uses a plain click, while selecting Medenia uses Shift+click.
        Click(new Point(545,(a.Slot>72?460:a.Spell?390:360)+frame.Height-480),false,medenia);
    }
    internal void SelectPage(int target,int current)
    {
        if(target!=1 && target!=2 && target!=3 && target!=4 && target!=9)throw new ArgumentOutOfRangeException("target");
        SelectPanel(new LearnedAbility{Spell=target==1 || target==2,Slot=target==9?73:target==2 || target==4?37:1},current);
    }
    internal void Activate(LearnedAbility a){Click(SlotPoint(a,frame.Height),true,false);}
    internal void TargetSelf(){Click(SelfPoint(frame.Width,frame.Height),false,false);}
    public void Dispose()
    {
        try {RestoreMap();}catch{}finally {if(writer!=IntPtr.Zero){HostNative.CloseHandle(writer);writer=IntPtr.Zero;}}
    }
}

internal static class NativeChatGuard
{
    internal static bool IsInput(uint vtable){return vtable==0x682fec || vtable==0x68306c || vtable==0x6830ec;}
    internal static bool Visible(Action<uint,byte[],int> read,Func<uint,uint> u,uint root)
    {
        if(root<0x10000)throw new InvalidOperationException("The input pane tree is changing.");
        uint entries=u(root+0x64),count=u(root+0x68);
        if(entries<0x10000 || count==0 || count>2048)throw new InvalidOperationException("The input pane list is unavailable.");
        var data=new byte[count*12];read(entries,data,data.Length);
        var parents=new List<bool>();bool found=false;
        for(int i=0;i<count;i++)
        {
            uint pane=BitConverter.ToUInt32(data,i*12);int depth=BitConverter.ToInt32(data,i*12+4);
            if(pane<0x10000 || depth<0 || depth>64 || depth>parents.Count)throw new InvalidOperationException("The input pane hierarchy changed.");
            if(parents.Count>depth)parents.RemoveRange(depth,parents.Count-depth);
            bool active=(u(pane+0x130)&255)!=0 && (depth==0 || parents[depth-1]);parents.Add(active);
            if(active && IsInput(u(pane))){found=true;break;}
        }
        if(u(root+0x64)!=entries || u(root+0x68)!=count || u(0x73d944)!=root)
            throw new InvalidOperationException("The input pane tree changed during the read.");
        return found;
    }
}

internal static class ToolNative
{
    [DllImport("kernel32.dll",SetLastError=true)] internal static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool WriteProcessMemory(IntPtr process,IntPtr address,byte[] data,IntPtr length,out IntPtr done);
    [DllImport("user32.dll")] internal static extern bool InvalidateRect(IntPtr window,IntPtr rect,bool erase);
    [DllImport("user32.dll")] internal static extern uint MapVirtualKey(uint code,uint kind);
}

internal static class FogOverlay
{
    // Names describe the visible screen outline: Square is axis-aligned,
    // Diamond has its tips on the axes. Both are estimates of visibility.
    internal static PointF[] Boundary(Size logical,bool compact,float range,bool square)
    {
        Point self=GameTools.SelfPoint(logical.Width,logical.Height);
        float x=self.X,y=self.Y+20+(compact?58:0),rx=28*range,ry=14*range;
        return !square?new[]{new PointF(x-2*rx,y),new PointF(x,y-2*ry),new PointF(x+2*rx,y),new PointF(x,y+2*ry)}:
            new[]{new PointF(x-rx,y-ry),new PointF(x+rx,y-ry),new PointF(x+rx,y+ry),new PointF(x-rx,y+ry)};
    }
    internal static void Draw(Graphics g,Rectangle view,Size logical,bool compact,int range,bool square,int opacity=30,bool fade=false,Color? tint=null)
    {
        if(opacity<=0)return;
        var save=g.Save();
        try
        {
            g.TranslateTransform(view.Left,view.Top);g.ScaleTransform((float)view.Width/logical.Width,(float)view.Height/logical.Height);
            var map=new Rectangle(3,3,logical.Width-24,logical.Height-(compact?56:172));
            g.SetClip(map,CombineMode.Intersect);
            int alpha=(int)Math.Round(Math.Min(100,opacity)*2.55);Color color=tint??Color.Black;
            using(var path=new GraphicsPath())
            using(var shade=new SolidBrush(Color.FromArgb(alpha,color)))
            {
                path.AddPolygon(Boundary(logical,compact,range+(fade?3:0),square));
                using(var outside=new Region(map)){outside.Exclude(path);g.FillRegion(shade,outside);}
            }
            // Nonoverlapping rings preserve the clear interior and smoothly
            // reach the chosen opacity over three additional tile ranges.
            if(fade)for(int i=0;i<48;i++)
            using(var inner=new GraphicsPath())using(var outer=new GraphicsPath())
            {
                inner.AddPolygon(Boundary(logical,compact,range+3f*i/48,square));
                outer.AddPolygon(Boundary(logical,compact,range+3f*(i+1)/48,square));
                double t=(i+.5)/48,taper=t*t*(3-2*t);
                using(var ring=new Region(outer))using(var brush=new SolidBrush(Color.FromArgb((int)Math.Round(alpha*taper),color)))
                {ring.Exclude(inner);g.FillRegion(brush,ring);}
            }
        }
        finally {g.Restore(save);}
    }
}
