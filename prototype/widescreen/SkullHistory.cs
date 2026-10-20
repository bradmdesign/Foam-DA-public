using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

// Observe the local character's displayed status icons, never incoming packets
// or the other-character cache. SpelledPane in the verified 7.41 client owns
// ten Int16 icons at +190 and ten duration-stage bytes at +1A4.
internal sealed class SkullObservation
{
    internal string Character,Server;internal uint Map;internal int X,Y;internal bool Skulled;
    internal string Identity {get{return Server+":"+Character.ToLowerInvariant();}}
}
internal static class SkullStatus
{
    internal static bool ContainsSkull(byte[] effects)
    {
        if(effects==null||effects.Length!=30)throw new InvalidDataException("Invalid status icon layout.");
        bool found=false;
        for(int i=0;i<10;i++)
        {
            short icon=BitConverter.ToInt16(effects,i*2);int stage=(sbyte)effects[20+i];
            if(icon==-1||stage==0)continue;
            if(icon<0||stage<1||stage>6)throw new InvalidDataException("Status icons are changing.");
            if(icon==89)found=true;
        }
        return found;
    }
    internal static SkullObservation Read(GameFrame frame,GameTools tools,ServerProfile server)
    {
        if(!frame.IsInWorldNow)return null;
        string name=tools.CharacterName();if(String.IsNullOrEmpty(name))return null;
        var result=Read(frame.Read,frame.U32,name,server.Key);
        return tools.CharacterName()==name?result:null;
    }
    internal static SkullObservation Read(Action<uint,byte[],int> read,Func<uint,uint> u,string name,string server)
    {
        uint gui=u(0x82b768),worldInterface=u(0x73d960);if(gui<0x10000||worldInterface<0x102e8)return null;
        uint pane=u(gui+0x4e04),world=worldInterface-0x2e8;
        if(pane<0x10000||u(pane)!=0x684e44||u(world)!=0x68c054)return null;
        uint map=u(world+0x26c),x=u(world+0x23c),y=u(world+0x238);
        if(x>4096||y>4096)return null;
        byte[] effects=new byte[30],confirm=new byte[30];read(pane+0x190,effects,30);read(pane+0x190,confirm,30);
        if(!effects.SequenceEqual(confirm)||u(0x82b768)!=gui||u(gui+0x4e04)!=pane||u(0x73d960)!=worldInterface||u(world+0x26c)!=map||u(world+0x23c)!=x||u(world+0x238)!=y)return null;
        return new SkullObservation{Character=name,Server=server,Map=map,X=(int)x,Y=(int)y,Skulled=ContainsSkull(effects)};
    }
}

internal sealed class SkullIncident
{
    public int Version=1;
    public string Id,Character,Server,ObservedUtc;
    public uint Map;public int X,Y;
    public string Screenshot {get{return Id+".png";}}
    public override string ToString()
    {
        DateTime when;string time=DateTime.TryParse(ObservedUtc,null,System.Globalization.DateTimeStyles.RoundtripKind,out when)?when.ToLocalTime().ToString("MMM d  HH:mm:ss"):ObservedUtc;
        return time+" · "+Character+" · Map "+Map+" ("+X+", "+Y+")";
    }
}
internal static class SkullHistory
{
    internal static string Folder {get{return CaptureStorage.Skulled;}}
    internal static SkullIncident Create(SkullObservation state,DateTime utc)
    {
        return new SkullIncident{Id=utc.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N"),Character=state.Character,Server=state.Server,Map=state.Map,X=state.X,Y=state.Y,ObservedUtc=utc.ToString("o")};
    }
    internal static void Save(SkullIncident item,Bitmap image,string folder=null)
    {
        folder=folder??CaptureStorage.Ensure("skulled");Directory.CreateDirectory(folder);
        string png=Path.Combine(folder,item.Screenshot),temp=png+".tmp";
        try
        {
            image.Save(temp,System.Drawing.Imaging.ImageFormat.Png);File.Move(temp,png);
            LocalProfile.Write(Path.Combine(folder,item.Id+".json"),Program.Json.Serialize(item));
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    internal static List<SkullIncident> Load(string folder=null)
    {
        folder=folder??CaptureStorage.Ensure("skulled");var rows=new List<SkullIncident>();if(!Directory.Exists(folder))return rows;
        foreach(string path in Directory.GetFiles(folder,"*.json").OrderByDescending(p=>p).Take(500))try
        {
            var row=Program.Json.Deserialize<SkullIncident>(File.ReadAllText(path));
            if(row==null||row.Version!=1||String.IsNullOrEmpty(row.Id)||Path.GetFileNameWithoutExtension(path)!=row.Id||Path.GetFileName(row.Screenshot)!=row.Screenshot||row.X<0||row.Y<0||row.X>4096||row.Y>4096)continue;
            if(File.Exists(Path.Combine(folder,row.Screenshot)))rows.Add(row);
        }catch{}
        return rows;
    }
    internal static void OpenFolder()
    {Process.Start(new ProcessStartInfo(CaptureStorage.Ensure("skulled")){UseShellExecute=true});}
    internal static void Show(IWin32Window owner)
    {
        using(var form=new Form{Text="Last skulled · all characters",ClientSize=new Size(1000,680),MinimumSize=new Size(650,440),StartPosition=FormStartPosition.CenterParent,BackColor=DATheme.Dark,ForeColor=DATheme.Gold,Font=DATheme.Font(DATheme.CompanionFontPixels)})
        using(var picture=new PictureBox{Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.Black})
        {
            var list=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat,Dock=DockStyle.Top,Height=26,BackColor=DATheme.Dark,ForeColor=DATheme.Gold,AccessibleName="Skulled history for all characters"};
            var detail=new Label{Dock=DockStyle.Top,Height=43,Padding=new Padding(6)};
            var bar=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=34};
            var folder=new DAButton{Text="Open skulled folder",Width=168,Height=28};folder.Click+=delegate{try{OpenFolder();}catch{detail.Text="Could not open the local skulled folder.";}};bar.Controls.Add(folder);
            var close=new DAButton{Text="Close",Width=70,Height=28};close.Click+=delegate{form.Close();};bar.Controls.Add(close);
            Action update=()=>{
                if(picture.Image!=null){var previous=picture.Image;picture.Image=null;previous.Dispose();}
                var row=list.SelectedItem as SkullIncident;if(row==null){detail.Text="No skull events recorded yet. Events from every hosted character appear here.";return;}
                detail.Text=row.Character+" · "+row.Server+" · Map "+row.Map+" · X: "+row.X+"  Y: "+row.Y+"\nCaptured when the character's skull status appeared.";
                try{using(var source=new Bitmap(Path.Combine(Folder,row.Screenshot)))picture.Image=new Bitmap(source);}catch{detail.Text+=" Image unavailable.";}
            };
            list.SelectedIndexChanged+=delegate{update();};foreach(var row in Load())list.Items.Add(row);
            form.Controls.Add(picture);form.Controls.Add(detail);form.Controls.Add(list);form.Controls.Add(bar);
            if(list.Items.Count>0)list.SelectedIndex=0;else update();
            try{form.ShowDialog(owner);}finally{if(picture.Image!=null){picture.Image.Dispose();picture.Image=null;}}
        }
    }
}
internal sealed class SkullTracker
{
    string identity;bool recorded;
    internal bool Observe(SkullObservation state)
    {
        if(state==null)return false; // an unavailable read must not rearm a skull
        if(identity!=state.Identity){identity=state.Identity;recorded=false;}
        if(!state.Skulled){recorded=false;return false;}
        return !recorded;
    }
    internal void Commit(SkullObservation state){identity=state.Identity;recorded=true;}
}
internal sealed class SkullRecorder
{
    readonly SkullTracker tracker=new SkullTracker();Task work;DateTime retry;SkullObservation pending;
    internal string Notice;
    internal void Tick(GameFrame frame,GameTools tools,ServerProfile server)
    {
        if(work!=null)
        {
            if(!work.IsCompleted)return;
            if(work.Status==TaskStatus.RanToCompletion){tracker.Commit(pending);Notice="Skulled screenshot saved · "+pending.Character+" · ("+pending.X+", "+pending.Y+")";}
            else{var error=work.Exception;Notice="Skulled screenshot could not be saved; retrying.";retry=DateTime.UtcNow.AddSeconds(5);}
            work=null;pending=null;
        }
        if(DateTime.UtcNow<retry)return;
        try
        {
            var state=SkullStatus.Read(frame,tools,server);if(!tracker.Observe(state))return;
            // Publish the latest completed frame even when another tab is selected.
            frame.Update();if(!frame.InWorld||frame.Bitmap==null||frame.Frames<1)return;
            var confirm=SkullStatus.Read(frame,tools,server);
            if(confirm==null||!confirm.Skulled||confirm.Identity!=state.Identity||confirm.Map!=state.Map||confirm.X!=state.X||confirm.Y!=state.Y)return;
            var image=new Bitmap(frame.Bitmap);var incident=SkullHistory.Create(confirm,DateTime.UtcNow);pending=confirm;
            work=Task.Run(()=>{using(image)SkullHistory.Save(incident,image);});
        }
        catch{ /* A loading or changing status bar is retried; never infer a skull from HP. */ }
    }
    internal void Flush(){if(work==null)return;try{work.GetAwaiter().GetResult();}catch{}work=null;}
}
