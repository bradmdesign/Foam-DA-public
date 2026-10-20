using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.IO;
using System.Windows.Forms;
using System.Runtime.InteropServices;

internal static class ToolChecks
{
    [DllImport("user32.dll")] static extern bool DrawIconEx(IntPtr dc,int x,int y,IntPtr icon,int width,int height,uint step,IntPtr brush,uint flags);
    static void Require(bool test,string message){if(!test)throw new Exception(message);}
    internal static int Run()
    {
        int checks=0;
        int selectedTab=0,closedTab=0;
        using(var tabs=new Form{ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000),ClientSize=new Size(500,28),FormBorderStyle=FormBorderStyle.None})
        using(var first=new DAGameTab("Game 1",()=>selectedTab=1,()=>closedTab=1))
        using(var second=new DAGameTab("Game 2",()=>selectedTab=2,()=>closedTab=2)) {
            tabs.Controls.Add(first);tabs.Controls.Add(second);second.Location=new Point(200,0);tabs.Show();Application.DoEvents();
            first.SelectButton.PerformClick();second.CloseButton.PerformClick();
            Require(selectedTab==1&&closedTab==2,"Closing a background tab must target that tab without selecting it");checks++;
            first.Text="Changed character";
            Require(first.SelectButton.Text=="Changed character"&&first.CloseButton.Text=="×","Tab title refresh must preserve the close button");checks++;
            first.CloseButton.PerformClick();Require(closedTab==1,"Active tab close callback");checks++;
            bool wrongProcessRejected=false;
            try{GameSessions.CloseGame(System.Diagnostics.Process.GetCurrentProcess().Id,tabs.Handle,Program.Game).GetAwaiter().GetResult();}catch(InvalidOperationException){wrongProcessRejected=true;}
            Require(wrongProcessRejected,"A tab must never terminate a process whose executable does not match its game path");checks++;
            using(var bitmap=new Bitmap(500,28)){tabs.DrawToBitmap(bitmap,tabs.ClientRectangle);bitmap.Save(Path.Combine(Program.Root,"build","tab-close-preview.png"));}
        }
        checks+=WorldZoomChecks.Run();
        checks+=HotkeyChecks.Run();
        checks+=HostFeatureChecks.Run();
        IntPtr selected=new IntPtr(1),other=new IntPtr(2);
        Require(HostPointer.NeedsRestore(true,true,0,IntPtr.Zero,selected,true),"Restore missing cursor over active game");checks++;
        Require(HostPointer.NeedsRestore(true,true,1,other,selected,true),"Restore selected game cursor after toolbar transition");checks++;
        Require(!HostPointer.NeedsRestore(false,true,0,IntPtr.Zero,selected,true),"Inactive host must not change another app cursor");checks++;
        Require(!HostPointer.NeedsRestore(true,false,0,IntPtr.Zero,selected,true),"Pointer outside host must remain untouched");checks++;
        Require(!HostPointer.NeedsRestore(true,true,2,IntPtr.Zero,selected,true),"Respect Windows pen/touch cursor suppression");checks++;
        Require(!HostPointer.NeedsRestore(true,true,1,selected,selected,true),"Visible correct pointer needs no timer reset");checks++;
        Require(!HostPointer.NeedsRestore(true,true,1,other,selected,false),"Preserve non-game control cursor choices");checks++;
        Require(HostPointer.NeedsRestore(true,true,0,IntPtr.Zero,selected,false),"Recover missing pointer over host toolbar");checks++;
        using(var cursor=new NativeGameCursor(Path.Combine(Program.Root,"theme","game.cur")))
        using(var expected=new Bitmap(Path.Combine(Program.Root,"theme","game-cursor.png")))
        using(var actual=new Bitmap(32,32,PixelFormat.Format32bppArgb))
        {
            Require(cursor.Loaded && cursor.Value.Size==new Size(32,32),"Native DA color cursor load/size");checks++;
            using(var g=Graphics.FromImage(actual))
            {
                g.Clear(Color.Magenta);IntPtr dc=g.GetHdc();
                try{Require(DrawIconEx(dc,0,0,cursor.Value.Handle,32,32,0,IntPtr.Zero,3),"Native cursor rendering");}
                finally{g.ReleaseHdc(dc);}
            }
            int shaded=0;
            for(int y=0;y<32;y++)for(int x=0;x<32;x++)
            {
                Color art=expected.GetPixel(x,y),want=art.A==0?Color.Magenta:art,got=actual.GetPixel(x,y);
                Require(want.R==got.R&&want.G==got.G&&want.B==got.B,"Cursor color/mask mismatch");
                if(art.A!=0&&art.R>0&&art.R<255)shaded++;
            }
            Require(shaded>20,"Cursor lost original gray shading");checks++;
            actual.Save(Path.Combine(Program.Root,"build","cursor-native-check.png"),ImageFormat.Png);
        }
        Require(GameSessions.IsGamePath(Program.Game),"Legacy isolated runtime rejected");checks++;
        Require(GameSessions.IsGamePath(Path.Combine(GameSessions.Root,"game-0001","Darkages.exe")),"Session path rejected");checks++;
        foreach(string path in new[]{Path.Combine(GameSessions.Root,"game-0000","Darkages.exe"),Path.Combine(GameSessions.Root,"game-0009","Darkages.exe"),Path.Combine(GameSessions.Root,"game-0001","child","Darkages.exe"),@"C:\Program Files (x86)\KRU\Dark Ages\Darkages.exe"})
        {Require(!GameSessions.IsGamePath(path),"Unmanaged client path accepted");checks++;}
        var distinct=new HashSet<string>();
        Require(ServerProfile.Kru().Arguments==""&&ServerProfile.Local().Arguments==" 127.0.0.1 2610","Server command line");checks++;
        foreach(string invalid in new[]{"\"127.0.0.1\"","127.1","127.0.0.1 2610","0.0.0.0","::1"})
        {bool denied=false;try{ServerProfile.Create("bad",invalid,2610);}catch(ArgumentException){denied=true;}Require(denied,"Malformed endpoint accepted");checks++;}
        var sourceImage=File.ReadAllBytes(Path.Combine(Program.Root,"build","profiles","1088x640","Darkages.exe"));
        string profileFolder=Path.Combine(Program.Root,"build","profiles","1088x640");
        long newBytes=GameSessions.AdditionalBytes(Path.Combine(Program.Root,"build","nonexistent-estimate-only"),profileFolder);
        long reusedBytes=GameSessions.AdditionalBytes(Path.Combine(Program.Root,"runtime"),profileFolder);
        Require(newBytes>reusedBytes+100L*1024*1024,"Fresh launch disk estimate must include uncopied assets");checks++;
        Require(reusedBytes>0&&reusedBytes<100L*1024*1024,"Reused launch disk estimate must include profile replacement");checks++;
        var patchedEndpoint=EndpointPatch.Apply(sourceImage);
        int endpointPe=BitConverter.ToInt32(patchedEndpoint,0x3c);
        Require(BitConverter.ToUInt16(patchedEndpoint,endpointPe+6)==7&&patchedEndpoint.Length==sourceImage.Length+512,"Endpoint section header");checks++;
        bool duplicateDenied=false;try{EndpointPatch.Apply(patchedEndpoint);}catch(InvalidOperationException){duplicateDenied=true;}
        Require(duplicateDenied,"Endpoint patch must reject an already patched image");checks++;
        File.WriteAllBytes(Path.Combine(Program.Root,"build","endpoint-image-check.exe"),GameSessions.UniqueImage(patchedEndpoint,8));
        for(int slot=1;slot<=8;slot++)
        {
            byte[] client=File.ReadAllBytes(Path.Combine(Program.Root,"build","profiles","1088x640","Darkages.exe"));
            byte[] changed=GameSessions.UniqueImage(client,slot);
            string text=Encoding.ASCII.GetString(changed);
            Require(text.Contains("DALib.Session"+slot.ToString("0000"))&&!text.Contains("DALib.WidePrototype"),"Session mutex not replaced");checks++;
            Require(distinct.Add(Convert.ToBase64String(changed)),"Two tabs have the same game mutex");checks++;
        }
        var skills=new byte[89*260];skills[0]=1;
        byte[] name=Encoding.ASCII.GetBytes("Test skill (Lev:3/100)");Array.Copy(name,0,skills,4,name.Length);
        var parsed=GameTools.ParseBook(skills,false);
        Require(parsed.Count==1 && parsed[0].Name=="Test skill" && parsed[0].Slot==1 && parsed[0].Panel==3,"Skill parsing");checks++;
        var spells=new byte[89*518];int offset=72*518;spells[offset]=1;spells[offset+4]=2;
        name=Encoding.ASCII.GetBytes("Test spell");Array.Copy(name,0,spells,offset+5,name.Length);
        var spell=GameTools.ParseBook(spells,true)[0];Require(spell.Slot==73 && spell.Argument==2 && spell.Panel==9,"World spell parsing");checks++;
        skills[4]=255;bool rejected=false;try{GameTools.ParseBook(skills,false);}catch(InvalidOperationException){rejected=true;}
        Require(rejected,"Corrupt ability accepted");checks++;
        Require(GameTools.SlotPoint(parsed[0],640)==new Point(110,510),"Translated skill slot");checks++;
        Require(GameTools.SlotPoint(spell,640)==new Point(320,510),"World spell column");checks++;
        Require(GameTools.SelfPoint(1088,640)==new Point(539,240),"Self target center");checks++;
        var now=new DateTime(2026,1,1);
        var state=new PlayContext{Root=0x100000,Map=1,X=10,Y=10,Panel=3,Alive=true};
        var events=new List<string>();var abilities=new List<LearnedAbility>(parsed);
        var runner=new MacroRunner {ReadContext=()=>state,ReadAbilities=()=>abilities,
            SelectPanel=(a,p)=>events.Add("panel"),Activate=a=>events.Add("activate"),TargetSelf=()=>events.Add("self"),CancelTarget=()=>events.Add("cancel")};
        var queue=new[]{new TrainingAction{Ability=parsed[0],Seconds=10}};
        runner.Start(queue,now);runner.Tick(now,true);runner.Tick(now.AddSeconds(9),true);
        Require(events.Count==1 && events[0]=="activate","Repeat delay");checks++;
        runner.Tick(now.AddSeconds(10),true);Require(events.Count==2,"Repeat execution");checks++;
        state.X++;runner.Tick(now.AddSeconds(11),true);Require(runner.Running && events.Count==2,"Movement must keep macros armed");checks++;runner.Stop("test");
        state.X--;state.Panel=7;events.Clear();runner.Start(queue,now);runner.Tick(now,true);
        Require(events.Count==1 && events[0]=="panel","Select before activate");checks++;
        runner.Tick(now.AddMilliseconds(400),true);Require(events.Count==1,"Wait for panel confirmation");checks++;
        runner.Tick(now.AddSeconds(3),true);Require(!runner.Running && events.Count==1,"Panel timeout");checks++;
        state.Panel=3;events.Clear();runner.Start(queue,now);abilities.Clear();runner.Tick(now,true);
        Require(!runner.Running && events.Count==0,"Missing ability stop");checks++;
        abilities.Add(parsed[0]);queue[0].Self=true;runner.Start(queue,now);runner.Tick(now,true);
        Require(events.Count==1,"Self target should be deferred");checks++;
        runner.Tick(now.AddMilliseconds(350),true);Require(events.Count==2 && events[1]=="self","Self target order");checks++;
        runner.Stop("test");events.Clear();runner.Start(queue,now);runner.Tick(now,true);runner.Stop("manual");
        Require(events.Count==2 && events[1]=="cancel","Cancel pending target");checks++;
        events.Clear();runner.Start(queue,now);runner.Tick(now,false);Require(!runner.Running && events.Count==0,"Focus loss stop");checks++;
        runner.Start(queue,now);state.Chat=true;runner.Tick(now,true);Require(runner.Running && events.Count==0,"Chat pauses input without ending the macro");checks++;
        state.Chat=false;runner.Tick(now.AddMilliseconds(101),true);Require(runner.Running&&events.Count==1,"Macro resumes after chat closes");checks++;runner.Stop("test");
        state.Chat=false;state.Compact=true;rejected=false;try{runner.Start(queue,now);}catch(InvalidOperationException){rejected=true;}
        Require(rejected,"Compact layout must not accept full UI coordinates");checks++;
        state.Compact=false;spell.Argument=1;rejected=false;try{runner.Start(new[]{new TrainingAction{Ability=spell,Seconds=10}},now);}catch(InvalidOperationException){rejected=true;}
        Require(rejected,"Text prompt spell accepted");checks++;
        using(var image=new Bitmap(1088,640,PixelFormat.Format32bppRgb))using(var g=Graphics.FromImage(image))
        {
            g.Clear(Color.White);FogOverlay.Draw(g,new Rectangle(0,0,1088,640),image.Size,false,12,false);
            Require(image.GetPixel(539,260).R==255 && image.GetPixel(30,30).R<255,"Fog interior/exterior");checks++;
            Require(image.GetPixel(10,500).R==255 && image.GetPixel(1,1).R==255,"Fog must exclude HUD and frame");checks++;
            g.Clear(Color.White);FogOverlay.Draw(g,new Rectangle(0,0,1088,640),image.Size,false,12,false,0);
            Require(image.GetPixel(30,30).R==255,"Zero opacity");checks++;
            FogOverlay.Draw(g,new Rectangle(0,0,1088,640),image.Size,false,12,false,100);
            Require(image.GetPixel(30,30).R==0 && image.GetPixel(539,260).R==255,"Full opacity with clear interior");checks++;
            g.Clear(Color.White);FogOverlay.Draw(g,new Rectangle(0,0,1088,640),image.Size,false,12,false,50);
            Require(Math.Abs(image.GetPixel(30,30).R-127)<=1,"Half opacity");checks++;
            g.Clear(Color.White);FogOverlay.Draw(g,new Rectangle(0,0,1088,640),image.Size,false,6,true,100,true,Color.FromArgb(72,43,24));
            Require(image.GetPixel(30,30).ToArgb()==Color.FromArgb(72,43,24).ToArgb(),"Fog tint at full opacity");checks++;
            Require(image.GetPixel(539,260).R==255&&image.GetPixel(700,260).R==255,"Fade must retain clear interior");checks++;
            Require(image.GetPixel(722,260).R>image.GetPixel(750,260).R&&image.GetPixel(750,260).R>image.GetPixel(790,260).R,"Fog fade must progress gradually");checks++;
            Require(image.GetPixel(10,500).R==255&&image.GetPixel(1,1).R==255,"Colored fade must exclude HUD and border");checks++;
            g.Clear(Color.White);FogOverlay.Draw(g,new Rectangle(0,0,1088,640),image.Size,false,6,false,100,true,Color.FromArgb(27,43,67));
            Require(image.GetPixel(539,260).R==255&&image.GetPixel(30,30).B==67,"Diamond fade and blue tint");checks++;
            g.Clear(Color.White);FogOverlay.Draw(g,new Rectangle(0,0,1088,640),image.Size,true,6,true,0,true,Color.Red);
            Require(image.GetPixel(30,30).R==255&&image.GetPixel(30,30).G==255,"Transparent colored fade");checks++;
        }
        PointF[] square=FogOverlay.Boundary(new Size(1088,640),false,12,true),diamond=FogOverlay.Boundary(new Size(1088,640),false,12,false);
        Require(square[0].Y==square[1].Y && square[1].X==square[2].X,"Square outline must have axis-aligned edges");checks++;
        Require(diamond[0].Y==diamond[2].Y && diamond[1].X==diamond[3].X && diamond[0].Y!=diamond[1].Y,"Diamond outline must have diagonal edges");checks++;
        using(var options=new MapPanel()){Require(options.Opacity.Value==30 && options.Shape.SelectedIndex==1&&options.Fade.Checked,"Fog defaults");checks++;}
        var protection=new byte[12];
        NativeLayerMask.ProtectPixels(protection,4,3,new byte[]{0,0,1,0,255,255,0,0,2,0,0,0},6,2,2,new Point(1,1),new Rectangle(0,0,4,3));
        Require(protection[5]==0&&protection[6]==1&&protection[9]==0&&protection[10]==1&&protection[7]==0,"Layer zero transparency and padded pitch");checks++;
        var sourceColors=new[]{Color.White.ToArgb(),Color.Red.ToArgb(),Color.Green.ToArgb()};var composed=new int[3];
        FogComposition.Blend(sourceColors,composed,new byte[]{255,255,128},new byte[]{0,1,0});
        Require(composed[0]==Color.Black.ToArgb()&&composed[1]==sourceColors[1]&&Color.FromArgb(composed[2]).G==64,"Fog darkens terrain while preserving protected UI/map pixels");checks++;
        Array.Clear(protection,0,protection.Length);
        NativeLayerMask.ProtectPixels(protection,4,3,new byte[]{1,0,1,0,1,0,1,0},4,2,2,new Point(-1,-1),new Rectangle(0,0,4,3));
        Require(protection[0]==1&&protection[1]==0&&protection[4]==0,"Layer origin clipping");checks++;
        var aBytes=new byte[32];int reads=0;
        Action<byte[],int> fill=(buffer,length)=>{reads++;for(int i=0;i<length;i++)buffer[i]=42;};
        Require(!FrameSnapshot.TryRead(aBytes,32,()=>true,fill,()=>true) && reads==0,"Busy renderer was sampled");checks++;
        Require(FrameSnapshot.TryRead(aBytes,32,()=>false,fill,()=>true) && reads==1,"Single completed frame rejected");checks++;
        reads=0;Require(!FrameSnapshot.TryRead(aBytes,32,()=>reads==1,fill,()=>true),"Redraw beginning mid-copy accepted");checks++;
        reads=0;Require(!FrameSnapshot.TryRead(aBytes,32,()=>false,fill,()=>false),"Changed surface accepted");checks++;
        var mailbox=new FrameMailbox(32);int pitch;
        Require(!mailbox.Take(ref aBytes,out pitch),"Empty mailbox must not block or publish");checks++;
        mailbox.Writing[0]=1;mailbox.Publish(8);mailbox.Writing[0]=2;mailbox.Publish(16);
        Require(mailbox.Take(ref aBytes,out pitch) && aBytes[0]==2 && pitch==16,"Mailbox must deliver latest completed frame");checks++;
        mailbox.Writing[0]=3;mailbox.Publish(16);mailbox.Writing[0]=4;
        Require(aBytes[0]==2,"Worker must not mutate the displayed frame");checks++;
        Require(mailbox.Take(ref aBytes,out pitch) && aBytes[0]==3 && !mailbox.Take(ref aBytes,out pitch),"Consume each publication once");checks++;
        var paired=new FrameMailbox(32);var protectedBytes=new byte[8];bool layersReady;
        paired.Writing[0]=7;paired.ProtectionWriting[0]=1;paired.Publish(16,true);
        paired.Writing[0]=8;paired.ProtectionWriting[0]=0;paired.Publish(16,false);
        Require(paired.Take(ref aBytes,ref protectedBytes,out pitch,out layersReady)&&aBytes[0]==8&&protectedBytes[0]==0&&!layersReady,"Frame and protection must publish together");checks++;
        paired.Writing[0]=9;paired.ProtectionWriting[0]=1;paired.Publish(16,true);
        Require(aBytes[0]==8&&protectedBytes[0]==0,"Worker mutated displayed protection");checks++;
        Require(paired.Take(ref aBytes,ref protectedBytes,out pitch,out layersReady)&&aBytes[0]==9&&protectedBytes[0]==1&&layersReady,"Protected publication lost");checks++;
        paired.Writing[0]=10;paired.ProtectionWriting[0]=0;
        Require(!paired.PublishComplete(16,true,false,false)&&!paired.Take(ref aBytes,ref protectedBytes,out pitch,out layersReady)&&aBytes[0]==9&&protectedBytes[0]==1,"Failed layer read must retain the complete displayed frame/mask");checks++;
        paired.Writing[0]=11;paired.ProtectionWriting[0]=1;
        Require(paired.PublishComplete(16,true,true,true)&&paired.Take(ref aBytes,ref protectedBytes,out pitch,out layersReady)&&layersReady&&aBytes[0]==11,"Fog capture must recover after a missed layer read");checks++;
        paired.Writing[0]=12;
        Require(paired.PublishComplete(16,true,true,false)&&paired.Take(ref aBytes,ref protectedBytes,out pitch,out layersReady)&&!layersReady&&aBytes[0]==12,"Stable travel screen must update without world fog");checks++;
        Require(paired.PublishComplete(16,false,false,false)&&paired.Take(ref aBytes,ref protectedBytes,out pitch,out layersReady)&&!layersReady,"Fog disabled must bypass mask validation");checks++;
        checks+=LayerTreeChecks();checks+=MapFeatureChecks.Run();
        using(var surface=new GameSurface()){surface.SetGameCursor(true);surface.SetGameCursor(false);checks++;}
        Require((KeyScan.Ensure((int)Keys.Tab,1)&0xff0000)==0x0f0000,"Tab scan-code fallback");checks++;
        Require(KeyScan.Ensure((int)Keys.Tab,unchecked((int)0xc00f0001))==unchecked((int)0xc00f0001),"Hardware key-up scan changed");checks++;
        Require((KeyScan.Ensure((int)Keys.Left,1)&0x1ff0000)==0x14b0000,"Extended scan-code fallback");checks++;
        Require(GameSurface.IsHostedScreenshotKey(0x100,(int)Keys.F12,Keys.None)&&GameSurface.IsHostedScreenshotKey(0x101,(int)Keys.F12,Keys.None),"Plain F12 press and release use the host screenshot path");checks++;
        Require(!GameSurface.IsHostedScreenshotKey(0x100,(int)Keys.F12,Keys.Shift)&&!GameSurface.IsHostedScreenshotKey(0x100,(int)Keys.F11,Keys.None),"Modified F12 and other keys retain normal input handling");checks++;
        Keys[] simpleKeys=SimpleMacroRunner.Parse("1 2 3 4 sPaCe a F5 /");
        Require(simpleKeys.Length==8 && simpleKeys[0]==Keys.D1 && simpleKeys[4]==Keys.Space && simpleKeys[5]==Keys.A && simpleKeys[6]==Keys.F5,"Simple key parser");checks++;
        rejected=false;try{SimpleMacroRunner.Parse("banana");}catch(InvalidOperationException){rejected=true;}Require(rejected,"Unknown key accepted");checks++;
        var presses=new List<Keys>();state.Compact=true;state.Chat=state.Dialog=state.Transfer=false;state.Alive=true;
        var simple=new SimpleMacroRunner{ReadContext=()=>state,Tap=key=>presses.Add(key)};
        simple.Start("1 2 3 4",2,false,now);simple.Tick(now,true);simple.Tick(now.AddMilliseconds(50),true);
        Require(presses.Count==1 && presses[0]==Keys.D1,"Simple first key / pacing");checks++;
        for(int i=1;i<4;i++)simple.Tick(now.AddMilliseconds(i*100),true);
        Require(presses.Count==4 && presses[3]==Keys.D4,"Simple sequence order");checks++;
        simple.Tick(now.AddSeconds(1.99),true);Require(presses.Count==4,"Simple interval too short");checks++;
        simple.Tick(now.AddSeconds(2),true);Require(presses.Count==5 && presses[4]==Keys.D1,"Simple cycle interval");checks++;
        simple.Stop("test");presses.Clear();simple.Start("",1,true,now);
        simple.Tick(now,true);simple.Tick(now.AddMilliseconds(499),true);Require(presses.Count==1,"Assail faster than 0.5s");checks++;
        simple.Tick(now.AddMilliseconds(500),true);Require(presses.Count==2 && presses[1]==Keys.Space,"Assail 0.5s cadence");checks++;
        simple.Tick(now.AddSeconds(10),true);Require(presses.Count==3,"Assail backlog burst");checks++;
        state.Chat=true;simple.Tick(now.AddSeconds(11),true);Require(simple.Running && presses.Count==3,"Simple chat pause");checks++;
        state.Chat=false;state.X++;simple.Tick(now.AddSeconds(12),true);Require(simple.Running&&presses.Count==4,"Simple resumes after chat and keeps running after movement");checks++;state.X--;simple.Stop("test");
        state.Chat=false;simple.Start("1",1,false,now);simple.Tick(now,false);Require(!simple.Running,"Simple focus stop");checks++;
        simple.Start("1",1,false,now);state.Map++;simple.Tick(now,true);Require(!simple.Running,"Simple map change stop");checks++;
        state.Map--;simple.Start("1",1,false,now);simple.Stop("manual");int before=presses.Count;simple.Tick(now,true);Require(presses.Count==before,"Simple manual stop");checks++;
        state.Compact=false;state.Panel=7;presses.Clear();var switches=new List<int>();
        simple.SelectPage=(wanted,current)=>switches.Add(wanted);
        simple.Start("1",1,true,now,3);simple.Tick(now,true);
        Require(switches.Count==1 && switches[0]==3 && presses.Count==0,"Select skills before sequence or Assail");checks++;
        simple.Tick(now.AddMilliseconds(500),true);Require(switches.Count==1 && presses.Count==0,"Wait for page confirmation");checks++;
        state.Panel=3;simple.Tick(now.AddMilliseconds(600),true);
        Require(presses.Count==2 && presses[0]==Keys.D1 && presses[1]==Keys.Space,"Resume only after skills page opens");checks++;
        simple.Stop("test");presses.Clear();simple.Start("2",1,false,now,1);simple.Tick(now,true);
        Require(switches[switches.Count-1]==1 && presses.Count==0,"Select spells before sequence");checks++;
        simple.Tick(now.AddSeconds(3),true);Require(!simple.Running && presses.Count==0,"Page failure must stop without pressing keys");checks++;
        state.Compact=true;rejected=false;try{simple.Start("1",1,false,now,3);}catch(InvalidOperationException){rejected=true;}
        Require(rejected,"Automatic page selection must require full UI");checks++;
        state.Compact=false;state.Panel=3;events.Clear();queue[0].Self=false;queue[0].Seconds=1;
        var normalSpell=new LearnedAbility{Slot=1,Spell=true,Argument=5,Name="Test spell"};abilities.Add(normalSpell);
        runner.Start(new[]{queue[0],new TrainingAction{Ability=normalSpell,Seconds=1}},now);runner.Tick(now,true);runner.Tick(now.AddSeconds(1),true);
        Require(events.Count==2 && events[0]=="activate" && events[1]=="panel","Mixed learned queue must switch skills to spells");checks++;
        state.Panel=1;runner.Tick(now.AddSeconds(1.4),true);runner.Tick(now.AddSeconds(2.4),true);
        Require(events.Count==4 && events[2]=="activate" && events[3]=="panel","Mixed learned queue must return to skills");checks++;
        runner.Stop("test");
        return checks;
    }
    static int LayerTreeChecks()
    {
        const uint image=0x400000,dispatcher=0x1000,list=0x2000,world=0x3000,hud=0x4000,backing=0x5000;
        var memory=new byte[0x340000];var mask=new byte[12];bool changeTree=false;
        Action<uint,uint> put=(at,value)=>Array.Copy(BitConverter.GetBytes(value),0,memory,(int)at,4);
        put(0x33d944,image+dispatcher);put(dispatcher+0x64,image+list);put(dispatcher+0x68,2);put(dispatcher+0x1b4,1);
        put(list,image+world);put(list+12,image+hud);
        foreach(uint pane in new[]{world,hud}){put(pane+0xe8,4);put(pane+0xec,3);put(pane+0x188,2);memory[pane+0x130]=1;}
        put(world,image+0x28c054);put(hud,image+0x288a84);put(hud+0x6c,4);put(hud+0x9c,8);put(hud+0x98,image+backing);memory[backing+16]=1;
        var reader=new NativeLayerMask((at,buffer,length)=>{
            Array.Copy(memory,(int)(at-image),buffer,0,length);
            if(changeTree&&at==image+backing+16)put(dispatcher+0x1b4,2);
        },image,4,3);
        Require(reader.Capture(mask)&&reader.WorldVisible&&mask[0]==1&&mask[1]==0,"Stable native pane tree and transparent HUD");
        changeTree=true;
        Require(!reader.Capture(mask)&&reader.Failure=="tree changed","Changing pane generation must reject its frame");
        changeTree=false;
        Require(reader.Capture(mask)&&reader.WorldVisible,"Layer reader must recover without persistent invalid state");
        memory[world+0x130]=0;
        Require(reader.Capture(mask)&&!reader.WorldVisible,"Hidden world/travel UI is a valid scene transition");
        memory[world+0x130]=1;memory[hud+0x130]=0;
        Require(!reader.Capture(mask)&&reader.Failure=="world/HUD missing","Incomplete world UI must never produce an unfogged world frame");
        memory[hud+0x130]=1;
        put(world,image+0x274b44);
        Require(reader.Capture(mask,false)&&reader.TravelMap==new Rectangle(0,0,4,3),"Travel map detection must work with fog disabled");
        memory[world+0x130]=0;
        Require(reader.Capture(mask,false)&&reader.TravelMap.IsEmpty,"Closing travel map must clear its display state");
        var pixels=new[]{1,2,3,4,5,6,7,8,9,10,11,12};
        FramePixels.BlackOutside(pixels,4,3,new Rectangle(1,1,2,1));
        Require(pixels[5]==6&&pixels[6]==7&&pixels[0]==unchecked((int)0xff000000)&&pixels[7]==unchecked((int)0xff000000),"Travel backdrop must retain map pixels and black out HUD/world pixels");
        Require(FrameSnapshot.SequenceStable(12,12)&&!FrameSnapshot.SequenceStable(12,14)&&!FrameSnapshot.SequenceStable(13,13),"Reject whole redraws during capture even when both busy flags were clear");
        Require(!FrameSnapshot.SequenceStable(uint.MaxValue-1,0)&&FrameSnapshot.SequenceStable(0,0),"Sequence wraparound must not accept a mixed frame");
        var mailbox=new FrameMailbox(48);var frame=new byte[48];var protection=new byte[12];int pitch;bool ready;
        mailbox.TravelMapWriting=new Rectangle(1,1,2,1);mailbox.Publish(8);
        mailbox.TravelMapWriting=Rectangle.Empty;
        Require(mailbox.Take(ref frame,ref protection,out pitch,out ready)&&mailbox.TravelMapReading==new Rectangle(1,1,2,1),"Map visibility must be paired with the published pixels");
        mailbox.Publish(8);mailbox.Take(ref frame,ref protection,out pitch,out ready);
        Require(mailbox.TravelMapReading.IsEmpty,"Next complete frame restores the corner panel");
        // The transient top-left notification is not registered for input.
        // Its pixels must still survive zoom/fog, while transparent gaps and
        // targeting retain the world beneath it. Never store the text itself.
        put(world,image+0x28c054);memory[world+0x130]=1;
        const uint message=0x6000,messageBacking=0x7000;
        put(0x33baec,image+message);put(message,image+0x275314);
        put(message+0xe8,3);put(message+0xec,2);put(message+0x188,1);put(message+0x130,1);put(message+0x134,1);
        put(message+0x6c,4);put(message+0x9c,8);put(message+0x98,image+messageBacking);
        memory[messageBacking+16]=31;memory[messageBacking+20]=123;memory[messageBacking+24]=12;
        Require(reader.Capture(mask)&&mask[0]==1&&mask[2]==2&&mask[4]==2&&mask[1]==0,"Visual-only notifications must protect glyphs and preserve existing interactive masks");
        var messageRoot=new[]{1,2,3,4,5,6,7,8,9,10,11,12};var fogged=new int[12];var alpha=new byte[12];for(int i=0;i<12;i++)alpha[i]=255;
        FogComposition.Blend(messageRoot,fogged,alpha,mask);
        Require(fogged[2]==3&&fogged[4]==5&&fogged[1]==unchecked((int)0xff000000),"Fog must leave notifications visible without protecting transparent gaps");
        using(var rootImage=new Bitmap(4,3,PixelFormat.Format32bppRgb))using(var terrain=new Bitmap(4,3,PixelFormat.Format32bppRgb))using(var painter=new WorldZoomPainter())
        {
            using(var g=Graphics.FromImage(rootImage))g.Clear(Color.Red);using(var g=Graphics.FromImage(terrain))g.Clear(Color.Blue);
            var painted=painter.PaintNative(rootImage,terrain,new Rectangle(0,0,4,3),200,mask,new Size(8,6));
            Require(painted.GetPixel(4,0).R==255&&painted.GetPixel(0,2).R==255&&painted.GetPixel(2,0).B==255,"Zoom must pin notification glyphs at normal scale and enlarge terrain through gaps");
        }
        put(message+0x134,7);Require(reader.Capture(mask)&&mask[2]==0&&mask[4]==0&&mask[0]==1,"Expired notification with cached glyphs must not cut terrain-colored letters out of the world");
        put(message+0x134,43);Require(reader.Capture(mask)&&mask[2]==0,"Zero-weight notification fade must not retain background pixels");
        put(message+0x134,74);Require(reader.Capture(mask)&&mask[2]==2,"Visible notification fade must remain protected");
        put(message+0x134,1);Require(reader.Capture(mask)&&mask[2]==2,"A new message must restore its mask after expiry");
        put(message+0x130,0);Require(reader.Capture(mask)&&mask[2]==0,"Hidden notification must not leave a stale mask");
        put(message+0x130,1);put(message+0x6c,2);Require(!reader.Capture(mask)&&reader.Failure=="message backing","Unexpected notification backing must retain the last complete frame");
        put(message+0x6c,4);
        var changingMessages=new NativeLayerMask((at,buffer,length)=>{Array.Copy(memory,(int)(at-image),buffer,0,length);if(at==image+messageBacking+16)put(message+0x134,44);},image,4,3);
        Require(!changingMessages.Capture(mask)&&changingMessages.Failure=="message changed","Changing notification fade during capture must reject the mixed frame");
        put(0x33baec,0);Require(reader.Capture(mask)&&mask[2]==0,"Missing notification singleton is a valid state");
        // Input geometry is separate from fog's per-pixel protection, and is
        // available even with all optional rendering effects disabled.
        put(world+0x2d8,3);put(world+0x2dc,4);
        const uint chat=0x9000;
        put(dispatcher+0x68,3);put(list+24,image+chat);put(list+28,1);
        put(chat,image+0x26febc);put(chat+0x130,1);put(chat+0x188,2);
        put(chat+0x12c,1);put(chat+0x128,1);put(chat+0xe8,2);put(chat+0xec,2);
        Require(reader.Capture(mask,false)&&reader.WorldArea==new Rectangle(0,0,4,3)&&reader.InputRegions.Count==1,"Wheel hit testing needs world and UI bounds with fog disabled");
        var scene=new NativeWheelScene(reader.WorldArea,reader.InputRegions.ToArray());
        Require(!scene.IsWorld(new Point(1,1))&&scene.IsWorld(new Point(0,1)),"Hovered chat must scroll, adjacent terrain must zoom");
        put(chat+0x12c,3);
        Require(reader.Capture(mask,false)&&reader.InputRegions[0]==new Rectangle(3,1,1,2),"Native child regions inherit their parent's clip");
        put(chat+0x130,0);
        Require(reader.Capture(mask,false)&&reader.InputRegions.Count==0,"Hidden lists must not block terrain zoom");
        put(chat+0x130,1);put(chat,image+0x28aeac);
        Require(reader.Capture(mask,false)&&reader.InputRegions.Count==0,"Tab map itself must keep Ctrl-wheel zoom available");
        put(world+0x130,0);
        Require(reader.Capture(mask,false)&&reader.WorldArea.IsEmpty,"Leaving the world must clear zoom hit geometry");
        // The expanded NPCSession parent spans the canvas, but its visible
        // children alone may suppress world zoom/fog. Its own pixel backing
        // is deliberately absent in this fixture: it is a layout container.
        put(world+0x130,1);
        put(chat+0x12c,0);put(chat+0x128,0);put(chat+0xe8,4);put(chat+0xec,3);
        const uint dialogue=0xa000;
        put(dispatcher+0x68,4);put(list+36,image+dialogue);put(list+40,2);
        put(dialogue+0x130,1);put(dialogue+0x188,2);
        put(dialogue+0x12c,1);put(dialogue+0x128,1);put(dialogue+0xe8,2);put(dialogue+0xec,1);
        foreach(uint container in new uint[]{0x280a54,0x26ebdc})
        {
            put(chat,image+container);put(dialogue,image+(container==0x280a54?0x281804u:0x26ecf4u));
            string kind=container==0x280a54?"NPC dialogue":"Mail/board";
            Require(reader.Capture(mask)&&mask[5]==1&&mask[6]==1&&mask[3]==0&&mask[11]==0,kind+" parent must protect its visible child without marking the whole scene as UI");
            using(var rootImage=new Bitmap(4,3,PixelFormat.Format32bppRgb))using(var terrain=new Bitmap(4,3,PixelFormat.Format32bppRgb))using(var painter=new WorldZoomPainter())
            {
                using(var g=Graphics.FromImage(rootImage))g.Clear(Color.Red);using(var g=Graphics.FromImage(terrain))g.Clear(Color.Blue);
                var painted=painter.PaintNative(rootImage,terrain,new Rectangle(0,0,4,3),125,mask,new Size(8,6));
                Require(painted.GetPixel(2,2).R==255&&painted.GetPixel(6,4).B==255,kind+" must leave terrain zoomed at 125 percent while keeping the child at normal size");
            }
            scene=new NativeWheelScene(reader.WorldArea,reader.InputRegions.ToArray());
            Require(!scene.IsWorld(new Point(3,2)),kind+" modal parent must still prevent wheel zoom behind the window");
        }
        return 35;
    }
}
