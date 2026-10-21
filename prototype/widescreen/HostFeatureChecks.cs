using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

internal static class HostFeatureChecks
{
    internal static void ShowMap()
    {
        var processes=GameSessions.Running();
        try
        {
            foreach(var process in processes)using(var frame=new GameFrame(process))
            {
                frame.ExtraWorldRequested=true;frame.StandardMiniMapRequested=true;frame.GuardRedraws=true;
                var watch=System.Diagnostics.Stopwatch.StartNew();while(watch.ElapsedMilliseconds<2000){frame.Update();System.Threading.Thread.Sleep(16);}
                if(!frame.InWorld||!frame.Scene.Ready)continue;
                using(var form=new Form{Text="Foam DA · Area map preview",ClientSize=new Size(1280,800),StartPosition=FormStartPosition.CenterScreen,MinimumSize=new Size(620,420),BackColor=DATheme.Dark})
                using(var overview=new MapOverview())using(var timer=new Timer{Interval=100})
                {
                    var session=new HostedSession{Frame=frame,GamePath=process.MainModule.FileName,Server=ServerProfile.Read(process.MainModule.FileName)};
                    form.Controls.Add(overview);overview.Closed+=delegate{form.Close();};form.Shown+=delegate{overview.ShowMap(session);};
                    Action arrange=()=>overview.Bounds=MapOverview.BoundsFor(form.ClientSize,overview.Full);overview.LayoutChanged+=delegate{arrange();};form.Resize+=delegate{arrange();};arrange();
                    timer.Tick+=delegate{frame.Update();overview.RefreshScene(session);};timer.Start();Application.Run(form);
                }
                return;
            }
        }finally{foreach(var process in processes)process.Dispose();}
    }
    internal static void Probe()
    {
        var processes=GameSessions.Running();
        try
        {
            foreach(var process in processes)
            using(var frame=new GameFrame(process))
            {
                frame.ExtraWorldRequested=true;frame.StandardMiniMapRequested=true;frame.TileGridEnabled=true;frame.GuardRedraws=true;
                var timer=System.Diagnostics.Stopwatch.StartNew();while(timer.ElapsedMilliseconds<3500){frame.Update();System.Threading.Thread.Sleep(16);}
                if(!frame.InWorld||!frame.Scene.Ready)continue;
                using(var tools=new GameTools(frame))
                {
                    var progress=tools.Progression(ServerProfile.Read(process.MainModule.FileName).Key);
                    var scene=frame.Scene;string mapPath=Path.Combine(Path.GetDirectoryName(process.MainModule.FileName),"maps","lod"+scene.MapId+".map");
                    bool mapAvailable=File.Exists(mapPath)&&new FileInfo(mapPath).Length==(long)scene.MapWidth*scene.MapHeight*6;
                    if(mapAvailable)using(var bitmap=OverviewCanvas.Build(File.ReadAllBytes(mapPath),scene.MapWidth,scene.MapHeight))bitmap.Save(Path.Combine(Program.Root,"build","overview-map-check.png"));
                    frame.Bitmap.Save(Path.Combine(Program.Root,"build","ground-grid-check.png"));
                    frame.WorldBitmap.Save(Path.Combine(Program.Root,"build","world-grid-check.png"));
                    uint world=frame.U32(0x73d960)-0x2e8,context=frame.U32(world+0x20c),cache=frame.U32(context+0xc),canvas=frame.U32(cache+0x54);
                    int w=(int)frame.U32(world+0xe8),h=(int)frame.U32(world+0xec),pitch=(int)frame.U32(world+0x9c),gp=(int)frame.U32(canvas+0x9c),matches=0,nonzero=0;
                    var raw=new byte[pitch*h];var ground=new byte[gp*h];frame.Read(frame.U32(world+0x98)+16,raw,raw.Length);frame.Read(frame.U32(canvas+0x98)+16,ground,ground.Length);
                    using(var bitmap=new Bitmap(w,h))
                    {for(int y=0;y<h;y++)for(int x=0;x<w;x++){int at=y*gp+x*2,c=ground[at]|ground[at+1]<<8;if(c!=0){nonzero++;if(TileGrid.GroundAt(raw,pitch,ground,gp,w,h,x,y))matches++;}bitmap.SetPixel(x,y,Color.FromArgb((c>>11&31)*255/31,(c>>5&63)*255/63,(c&31)*255/31));}bitmap.Save(Path.Combine(Program.Root,"build","ground-cache-check.png"));}
                    File.WriteAllText(Path.Combine(Program.Root,"build","host-feature-probe.json"),Program.Json.Serialize(new{frames=frame.Frames,captured=frame.CapturedFrames,ground_grid=frame.TileGridEnabled,world_ready=frame.WorldReady,scene=scene.Ready,map_available=mapAvailable,map_dimensions=new[]{scene.MapWidth,scene.MapHeight},progression_ready=progress!=null,read_error=frame.ReadStatus,
                        world_canvas=new[]{frame.U32(world+0xe8),frame.U32(world+0xec),frame.U32(world+0x9c),frame.U32(world+0x6c)},ground_canvas=new[]{frame.U32(canvas+0xe8),frame.U32(canvas+0xec),frame.U32(canvas+0x9c),frame.U32(canvas+0x6c)},ground_pixels=nonzero,matching_ground_pixels=matches,grid_pixels=frame.GridPixels,grid_status=frame.GridStatus,mask_zero=frame.ProtectedPixels.Count(p=>p==0),layers_ready=frame.LayersReady}));
                    return;
                }
            }
            throw new InvalidOperationException("No ready managed world was available for the read-only feature check.");
        }
        finally{foreach(var process in processes)process.Dispose();}
    }
    static void Check(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);}
    internal static void PreviewCharacters()
    {
        string folder=Path.Combine(Program.Root,"build","character-gallery-preview");Directory.CreateDirectory(folder);
        var vault=new CharacterVault(Path.Combine(folder,"private","characters.json"));var processes=GameSessions.Running();int captured=0;
        try
        {
            foreach(var process in processes)
            using(var frame=new GameFrame(process))using(var tools=new GameTools(frame))
            {
                CharacterAppearance look=null;
                for(int tries=0;tries<30&&look==null;tries++)
                {try{string name=tools.CharacterName();if(name.Length>0)look=CharacterAppearanceReader.Read(frame,name,ServerProfile.Kru().Key);}catch{}if(look==null)System.Threading.Thread.Sleep(30);}
                if(look==null)continue;captured++;
                string alias=captured==1?"PreviewOne":captured==2?"PreviewTwo":"PreviewThree";
                var saved=new SavedCharacter{Name=alias,Server=ServerProfile.Kru()};vault.Save(saved);look.Key=saved.Key;look.Save(folder);
                using(var image=look.Image())image.Save(Path.Combine(folder,"sprite-"+captured+".png"));
            }
            File.WriteAllText(Path.Combine(folder,"result.json"),Program.Json.Serialize(new{sprites=captured,source="Native local-player composites only",credentials_used=false}));
            using(var form=new Form{Text="Foam DA · Character gallery preview",ClientSize=new Size(960,780),StartPosition=FormStartPosition.CenterScreen,MinimumSize=new Size(520,560)})
            using(var panel=new CharacterPanel(vault,folder){Dock=DockStyle.Fill,CanDismiss=true})
            {panel.Dismissed+=delegate{form.Close();};form.Controls.Add(panel);Application.Run(form);}
        }finally{foreach(var process in processes)process.Dispose();}
    }
    internal static void ProbeLogin()
    {
        var rows=new System.Collections.Generic.List<object>();var processes=GameSessions.Running();
        try
        {
            foreach(var process in processes)using(var frame=new GameFrame(process))
            {
                var scene=LoginScene.Read(frame);
                rows.Add(new{process=process.Id,world=frame.IsInWorldNow,menu=scene.Menu,agreement=scene.Agreement!=0,
                    login_ready=scene.Ready,blocked=scene.Blocked,reusable=scene.Reusable,
                    name_focused=scene.FocusedField==scene.NameField&&scene.NameField!=0,
                    password_focused=scene.FocusedField==scene.PasswordField&&scene.PasswordField!=0,
                    geometry=scene.Agreement!=0?LoginPaneGeometry(frame):null});
            }
            File.WriteAllText(Path.Combine(Program.Root,"build","login-layout-check.json"),Program.Json.Serialize(rows));
        }finally{foreach(var process in processes)process.Dispose();}
    }
    static object[] LoginPaneGeometry(GameFrame frame)
    {
        // Control identities and rectangles only. Editor contents are never read.
        var rows=new System.Collections.Generic.List<object>();
        uint dispatcher=frame.U32(0x73d944),array=frame.U32(dispatcher+0x64),count=frame.U32(dispatcher+0x68);
        if(array<0x10000||count>128)return rows.ToArray();
        var entries=new byte[count*12];frame.Read(array,entries,entries.Length);
        for(int i=0;i<count;i++)
        {
            uint pane=BitConverter.ToUInt32(entries,i*12);int depth=BitConverter.ToInt32(entries,i*12+4);
            if(pane<0x10000)continue;
            var header=new byte[0x190];frame.Read(pane,header,header.Length);
            uint vt=BitConverter.ToUInt32(header,0);var controls=new System.Collections.Generic.List<object>();
            if(vt==0x669acc||vt==0x67896c)
            {
                uint list=frame.U32(pane+0x594),n=list<0x10000?0:frame.U32(list+0x14),items=list<0x10000?0:frame.U32(list+0x18);
                if(n<=8&&items>=0x10000)
                {
                    var addresses=new byte[n*4];frame.Read(items,addresses,addresses.Length);
                    for(int j=0;j<n;j++)
                    {
                        uint control=BitConverter.ToUInt32(addresses,j*4);if(control<0x10000)continue;
                        var child=new byte[0x190];frame.Read(control,child,child.Length);
                        controls.Add(new{index=j,type=BitConverter.ToUInt32(child,0).ToString("X"),visible=child[0x130]!=0,
                            x=BitConverter.ToInt32(child,0x12c),y=BitConverter.ToInt32(child,0x128),
                            width=BitConverter.ToInt32(child,0xe8),height=BitConverter.ToInt32(child,0xec)});
                    }
                }
            }
            rows.Add(new{index=i,depth,type=vt.ToString("X"),visible=header[0x130]!=0,flags=BitConverter.ToUInt32(header,0x188),
                x=BitConverter.ToInt32(header,0x12c),y=BitConverter.ToInt32(header,0x128),
                width=BitConverter.ToInt32(header,0xe8),height=BitConverter.ToInt32(header,0xec),controls=controls.ToArray()});
        }
        return rows.ToArray();
    }
    internal static int Run()
    {
        int count=GameHostForm.VerifyCompanionChrome();
        var tracker=new GainTracker();DateTime now=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc);
        tracker.Sample(new ProgressSample{Identity="a",Experience=100,Ability=10},now);
        tracker.Sample(new ProgressSample{Identity="a",Experience=150,Ability=20},now.AddSeconds(5));
        Check(tracker.ExpPerHour==36000&&tracker.AbPerHour==7200,"Rates count observed cumulative gains over session time");count++;
        tracker.Sample(new ProgressSample{Identity="a",Experience=120,Ability=5},now.AddSeconds(6));
        Check(tracker.Experience==50&&tracker.Ability==10,"Spending or reset counters cannot create negative income or unsigned overflow");count++;
        tracker.Sample(new ProgressSample{Identity="b",Experience=100000,Ability=500},now.AddSeconds(7));
        Check(tracker.Experience==0&&tracker.Ability==0,"Changing character resets rate baselines");count++;
        tracker.Sample(null,now.AddSeconds(8));tracker.Sample(new ProgressSample{Identity="c",Experience=100001},now.AddSeconds(10));
        Check(tracker.ExpPerHour==0,"No income is inferred across missing samples");count++;
        using(var map=new MapPanel{Size=new Size(410,138)})
        {
            map.CreateControl();map.PerformLayout();
            Check(map.MiniMap.Text=="Show minimap"&&!map.LockZoom.Checked&&!map.ExperienceRate.Checked&&!map.AbilityRate.Checked,"New map defaults and explicit minimap label");count++;
            var prefs=new HostPreferences{PlayZoom=999,FogOpacity=-1,MapZoom=0,MiniStyle=42,MiniMapZoom=175,LockZoom=true,ExpRate=true,AbRate=true};prefs.Apply(map);
            Check(map.PlayZoom.Value==200&&map.Opacity.Value==0&&map.ZoomScale.Value==1&&map.MiniMapStyle.SelectedIndex==1&&map.LockZoom.Checked,"Preferences clamp malformed saved values");count++;
            prefs.Capture(map);var copy=Program.Json.Deserialize<HostPreferences>(Program.Json.Serialize(prefs));
            Check(copy.LockZoom&&copy.ExpRate&&copy.AbRate&&copy.FogOpacity==0&&copy.MiniMapZoom==175,"Shared setting roundtrip includes minimap zoom");count++;
            using(var image=new Bitmap(map.Width,map.Height)){map.DrawToBitmap(image,map.ClientRectangle);image.Save(Path.Combine(Program.Root,"build","map-settings-preview.png"));}
        }
        using(var mini=new MiniMapControl{Size=new Size(244,164)})
        {
            int zoomEvents=0,overviews=0;mini.ZoomChanged+=delegate{zoomEvents++;};mini.OverviewRequested+=delegate{overviews++;};
            mini.HandleClick(new Point(mini.ZoomInBounds.Left+5,10));
            Check(mini.ZoomPercent==125&&zoomEvents==1&&overviews==0,"Minimap plus changes only local zoom");count++;
            for(int i=0;i<20;i++)mini.HandleClick(new Point(mini.ZoomInBounds.Left+5,10));
            Check(mini.ZoomPercent==400&&overviews==0,"Minimap plus stops at 400 percent");count++;
            for(int i=0;i<20;i++)mini.HandleClick(new Point(mini.ZoomOutBounds.Left+5,10));
            Check(mini.ZoomPercent==10&&overviews==0&&MiniMapControl.NextZoom(10,1)==25,"Minimap minus reaches the 10 percent cached-area view");count++;
            var area=new Rectangle(0,0,200,120);var world=new Rectangle(0,0,400,200);
            var target=MiniMapControl.TargetRectangle(area,world,new Point(300,100),200);
            Check(target.Width==400&&target.Height==200&&target.X==-200&&target.Y==-40,"Minimap zoom centers player and clips target within map area");count++;
            var near=MiniMapControl.AreaSourceRectangle(new Size(2000,1000),new Rectangle(0,0,1088,640),new PointF(1000,500),50);
            var far=MiniMapControl.AreaSourceRectangle(new Size(2000,1000),new Rectangle(0,0,1088,640),new PointF(1000,500),25);
            Check(Math.Abs(far.Width-near.Width*2)<.01&&Math.Abs(far.Height-near.Height*2)<.01,"Cached-area zoom out covers twice the width and height at 25 percent");count++;
            mini.ZoomPercent=200;using(var image=new Bitmap(mini.Width,mini.Height)){mini.DrawToBitmap(image,mini.ClientRectangle);image.Save(Path.Combine(Program.Root,"build","minimap-zoom-controls.png"));}
        }
        var saved=new SavedCharacter{Name="Testonly",Server=ServerProfile.Kru()};
        string folder=Path.Combine(Program.Root,"build","vault-check-"+Guid.NewGuid().ToString("N"));string path=Path.Combine(folder,"characters.json");
        try
        {
            var vault=new CharacterVault(path);vault.Save(saved);
            Check(!File.ReadAllText(path).Contains("Secret")&&!File.ReadAllText(path).Contains("Password"),"Saved-character schema has no credential field");count++;
            var loaded=new CharacterVault(path);Check(loaded.Error==null&&loaded.Characters.Count==1&&loaded.Characters[0].Name==saved.Name,"Vault persistence");count++;
            DateTime lastOnline=new DateTime(2026,10,5,16,0,0,DateTimeKind.Utc);
            Check(loaded.RecordActivity(saved.Name,saved.Server.Key,lastOnline)&&!loaded.RecordActivity(saved.Name,"other-server",lastOnline),"Activity updates only the matching saved character and server");count++;
            loaded.Save(saved);var withActivity=new CharacterVault(path).Characters[0];
            Check(withActivity.LastOnlineUtc==lastOnline,"Last-online time survives vault reload and a character edit");count++;
            string legacy=File.ReadAllText(path).Replace("}]",",\"Secret\":\"synthetic-legacy-encrypted-value\"}]");
            File.WriteAllText(path,legacy);var migrated=new CharacterVault(path);
            Check(migrated.Error==null&&migrated.Characters.Count==1&&migrated.Characters[0].LastOnlineUtc==lastOnline&&!File.ReadAllText(path).Contains("Secret"),"Legacy saved credential is discarded without decryption on load");count++;
            loaded.Remove(loaded.Characters[0]);Check(new CharacterVault(path).Characters.Count==0,"Removing local entry does not retain credential");count++;
            File.WriteAllText(path,"broken");var broken=new CharacterVault(path);bool rejected=false;try{broken.Save(saved);}catch(InvalidOperationException){rejected=true;}
            Check(rejected&&File.ReadAllText(path)=="broken","An unreadable vault must never be overwritten automatically");count++;
        }
        finally
        {
            // This is a fresh, exact test directory that contains only our
            // synthetic fixture. Never point this cleanup at the user's vault.
            if(!Path.GetFullPath(folder).StartsWith(Path.GetFullPath(Path.Combine(Program.Root,"build"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Test cleanup path escaped build folder.");
            if(Directory.Exists(folder)&&(File.GetAttributes(folder)&FileAttributes.ReparsePoint)==0)Directory.Delete(folder,true);
        }
        count+=SkillListStorage.Check(Path.Combine(Program.Root,"build","skill-list-checks"));count+=LoginLayoutChecks();count+=LoginFlowChecks();count+=AppearanceChecks();count+=SkullChecks.Run();
        var login=new LoginScene{Dialog=10,NameField=20,PasswordField=30,NameEditor=21,PasswordEditor=31,FocusedField=20};
        Check(QuickLogin.CanType(login,10,20,20,false),"Verified name editor accepts quick-start input");count++;
        Check(!QuickLogin.CanType(login,10,30,30,false)&&!QuickLogin.CanType(login,10,20,21,false),"Password and unfocused editors reject quick-start input");count++;
        foreach(var args in new[]{new uint[]{11,20,20},new uint[]{10,20,30},new uint[]{10,40,40}})
        {Check(!QuickLogin.CanType(login,args[0],args[1],args[2],false),"Changed dialog, focus or foreign editor blocks name input");count++;}
        Check(!QuickLogin.CanType(login,10,20,20,true)&&!QuickLogin.CanType(new LoginScene(),0,0,0,false),"World and absent login dialog reject name input");count++;
        var ground=new byte[8*8*2];for(int i=0;i<ground.Length;i++)ground[i]=42;var raw=(byte[])ground.Clone();
        Check(TileGrid.GroundAt(raw,16,ground,16,8,8,4,4),"Grid is permitted over confirmed native ground");count++;
        raw[4*16+4*2]=10;Check(!TileGrid.GroundAt(raw,16,ground,16,8,8,4,4)&&!TileGrid.GroundAt(raw,16,ground,16,8,8,3,4),"Wall pixels and their boundary suppress the grid");count++;
        Check(!TileGrid.GroundAt(raw,16,ground,16,8,8,0,0),"Grid must not read beyond the cached ground");count++;
        foreach(int height in new[]{16,50,256})foreach(var tile in new[]{new Point(0,0),new Point(8,12),new Point(41,6)})
        {Check(OverviewCanvas.Tile(OverviewCanvas.Project(tile.X,tile.Y,height),height)==tile,"Overview note coordinate roundtrip");count++;}
        using(var bitmap=OverviewCanvas.Build(new byte[16*16*6],16,16))Check(bitmap.Width==136&&bitmap.Height==68,"Whole map overview geometry");count++;
        Check(new HostPreferences().OverviewFull&&Program.Json.Deserialize<HostPreferences>("{\"Version\":1}").OverviewFull,"New and existing settings default to full area map");count++;
        var mapPreference=new HostPreferences{OverviewFull=false};Check(!Program.Json.Deserialize<HostPreferences>(Program.Json.Serialize(mapPreference)).OverviewFull,"Basic area-map preference is retained");count++;
        foreach(var area in new[]{new Size(1920,1000),new Size(620,420),new Size(320,240)})
        {
            var full=MapOverview.BoundsFor(area,true);var basic=MapOverview.BoundsFor(area,false);
            Check(new Rectangle(Point.Empty,area).Contains(full)&&new Rectangle(Point.Empty,area).Contains(basic)&&full.Width>basic.Width&&full.Height>basic.Height,"Full map grows within the available host area");count++;
        }
        Check(MapOverview.BoundsFor(new Size(1920,1000),false).Size==new Size(900,680),"Basic retains the previous centred map size");count++;
        Check(GameHostForm.CharacterPanelVisible(false,false,false,true),"My characters remains available with no game");count++;
        Check(GameHostForm.CharacterPanelVisible(true,false,false,false),"A new signed-out tab initially offers characters");count++;
        Check(!GameHostForm.CharacterPanelVisible(true,false,false,true),"A dismissed signed-out sidebar stays closed");count++;
        Check(!GameHostForm.CharacterPanelVisible(true,true,false,false)&&!GameHostForm.CharacterPanelVisible(true,true,false,true),"World tabs do not force the character sidebar open");count++;
        Check(GameHostForm.CharacterPanelVisible(true,true,true,true)&&GameHostForm.CharacterPanelVisible(true,false,true,true),"Explicitly opening My characters overrides its prior dismissal");count++;
        foreach(var stageSize in new[]{new Size(1920,1044),new Size(800,564),new Size(380,300)})
        {
            var gallery=GameHostForm.CharacterGalleryBounds(stageSize,108);
            Check(gallery.Top>=120&&gallery.Bottom<=stageSize.Height&&gallery.Left>=0&&gallery.Right<=stageSize.Width,
                "Signed-out character gallery clears the launch controls and remains within the host");count++;
        }
        foreach(string profile in new[]{"960x600","1088x640","1152x680"})
        {
            var image=File.ReadAllBytes(Path.Combine(GameInstallation.PrepareProfile(profile),"Darkages.exe"));
            int at=EndpointPatch.Offset(image,0xaca85);var patched=StartupPatch.SkipIntro(image);
            Check(patched[at]==0x6a&&patched[at+1]==0x02,"Intro skip enters the native post-video state for "+profile);count++;
            patched[at]=0;bool refused=false;try{StartupPatch.SkipIntro(patched);}catch(InvalidOperationException){refused=true;}
            Check(refused,"Intro patch rejects changed instructions for "+profile);count++;
        }
        var customImage=File.ReadAllBytes(Path.Combine(GameInstallation.PrepareProfile("1088x640"),"Darkages.exe"));
        customImage=GameSessions.UniqueImage(StartupPatch.SkipIntro(EndpointPatch.Apply(customImage)),8);
        int introAt=EndpointPatch.Offset(customImage,0xaca85);
        Check(customImage[introAt]==0x6a&&customImage[introAt+1]==0x02,"Skip intro survives custom-server and session-mutex patches");count++;
        return count;
    }
    static int AppearanceChecks()
    {
        int checks=0;var memory=new System.Collections.Generic.Dictionary<uint,byte>();
        Action<uint,byte[]> write=(address,bytes)=>{for(int i=0;i<bytes.Length;i++)memory[address+(uint)i]=bytes[i];};
        Action<uint,uint> put=(a,v)=>write(a,BitConverter.GetBytes(v));
        Action<uint,byte[],int> read=(address,target,length)=>{for(int i=0;i<length;i++){byte b;target[i]=memory.TryGetValue(address+(uint)i,out b)?b:(byte)0;}};
        Func<uint,uint> u=a=>{var b=new byte[4];read(a,b,4);return BitConverter.ToUInt32(b,0);};
        uint world=0x10000,list=0x20000,head=0x30000,node=0x40000,obj=0x50000,canvas=0x60000,pixels=0x70000;
        write(0x73d910,Encoding.ASCII.GetBytes("Testchar\0"));put(0x73d960,world+0x2e8);put(world+0x194,list);put(world+0x214,77);
        put(list,0x68ba70);put(list+0x20,head);put(head+4,node);put(node,head);put(node+8,head);put(node+12,77);put(node+16,obj);
        put(obj,0x68ba2c);put(obj+0x24,77);put(obj+0x98,1);put(obj+0x104,1);put(obj+0xd8,canvas);
        var look=Enumerable.Repeat((byte)219,48).ToArray();look[0]=0;look[6]=1;look[7]=0;write(obj+0xa4,look);
        put(canvas+0x6c,4);put(canvas+0xe8,4);put(canvas+0xec,4);put(canvas+0x9c,12);put(canvas+0x98,pixels);put(0x6d8fdc,1);
        var raw=new byte[48];for(int y=0;y<4;y++)for(int x=0;x<4;x++){raw[y*12+x*2+1]=0xf8;}raw[1]=0;write(pixels+16,raw);
        Func<CharacterAppearance> capture=()=>CharacterAppearanceReader.Read(read,u,"Testchar","official",true,DateTime.UtcNow);
        var result=capture();Check(result!=null&&result.Width==4&&result.Height==4,"Read the verified local player's isolated composite");checks++;
        write(obj+0x192,new byte[]{1});Check(capture()!=null,"Down-facing character may refresh the login portrait");checks++;
        write(obj+0x192,new byte[]{2});Check(capture()==null,"Right-facing character cannot refresh the login portrait");checks++;
        write(obj+0x192,new byte[]{3});Check(capture()==null,"Up-facing character cannot refresh the login portrait");checks++;
        write(obj+0x192,new byte[]{0});Check(capture()!=null,"Left-facing character may refresh the login portrait");checks++;
        using(var image=result.Image())Check(image.GetPixel(0,0).A==0&&image.GetPixel(1,0).ToArgb()==Color.Red.ToArgb(),"Native transparent zero and RGB565 red decode without scenery");checks++;
        var bordered=new byte[8*8*2];for(int y=2;y<6;y++)for(int x=2;x<6;x++)bordered[y*16+x*2+1]=0xf8;
        using(var trimmed=CharacterAppearanceReader.Decode(bordered,8,8,16,true))Check(trimmed.Size==new Size(4,4)&&trimmed.GetPixel(0,0).ToArgb()==Color.Red.ToArgb(),"Transparent canvas margins are trimmed while preserving sprite pixels");checks++;
        var clean=Convert.FromBase64String(result.Appearance);Check(clean[1]==0&&clean[5]==0&&clean[9]==0&&clean[15]==0&&clean[37]==0&&clean[4]==219,"Appearance stores selectors but excludes uninitialized padding");checks++;
        put(obj+0xfc,1);Check(capture()==null,"A dirty composite cannot replace the last good portrait");checks++;put(obj+0xfc,0);
        put(obj+0x98,0);Check(capture()==null,"Never capture another player's sprite");checks++;put(obj+0x98,1);
        put(obj+0x24,88);Check(capture()==null,"Local entity identity must match the player ID");checks++;put(obj+0x24,77);
        put(canvas+0xe8,257);Check(capture()==null,"Reject oversized native canvases before allocation");checks++;put(canvas+0xe8,4);
        put(obj+0x104,0);Check(capture()==null,"A disguise preserves the last ordinary equipment portrait");checks++;put(obj+0x104,1);
        write(0x73d910,Encoding.ASCII.GetBytes("Otherone\0"));Check(capture()==null,"Changing character cannot save the sprite under the old name");checks++;write(0x73d910,Encoding.ASCII.GetBytes("Testchar\0"));
        int sequenceReads=0;Func<uint,uint> changed=a=>a==0x8e241c?(uint)(sequenceReads++*2):u(a);
        Check(CharacterAppearanceReader.Read(read,changed,"Testchar","official",true,DateTime.UtcNow)==null,"A redraw between portrait reads is rejected");checks++;
        string folder=Path.Combine(Program.Root,"build","appearance-check-"+Guid.NewGuid().ToString("N"));
        try
        {
            result.Save(folder);var loaded=CharacterAppearance.Load(result.Key,folder);
            result.Direction=2;File.WriteAllText(CharacterAppearance.FilePath(result.Key,folder),Program.Json.Serialize(result));
            Check(CharacterAppearance.Load(result.Key,folder)==null,"Old right/up portrait stays hidden until a front-facing capture replaces it");checks++;
            result.Direction=0;result.Save(folder);loaded=CharacterAppearance.Load(result.Key,folder);
            Check(loaded!=null&&loaded.Png==result.Png&&Math.Abs((loaded.CapturedUtc-result.CapturedUtc).TotalMilliseconds)<1,"Appearance and sprite persist atomically in one record");checks++;
            var text=File.ReadAllText(CharacterAppearance.FilePath(result.Key,folder));Check(!text.Contains("password")&&!text.Contains("Secret")&&!text.Contains("Testchar"),"Portrait storage contains no credentials or unnecessary character-name text");checks++;
            loaded.Width=256;bool rejected=false;try{using(var image=loaded.Image()){}}catch(InvalidDataException){rejected=true;}Check(rejected,"PNG dimensions must match bounded stored metadata");checks++;
            File.WriteAllText(CharacterAppearance.FilePath(result.Key,folder),"incomplete");Check(CharacterAppearance.Load(result.Key,folder)==null,"Corrupt cache falls back to a placeholder");checks++;
            rejected=false;try{CharacterAppearance.FilePath("../elsewhere",folder);}catch(InvalidDataException){rejected=true;}Check(rejected,"Portrait keys cannot escape their storage folder");checks++;
        }
        finally
        {
            if(!Path.GetFullPath(folder).StartsWith(Path.GetFullPath(Path.Combine(Program.Root,"build"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException();
            if(Directory.Exists(folder)&&(File.GetAttributes(folder)&FileAttributes.ReparsePoint)==0)Directory.Delete(folder,true);
        }
        return checks;
    }
    static int LoginLayoutChecks()
    {
        var memory=new System.Collections.Generic.Dictionary<uint,byte>();
        Action<uint,uint> put=(address,value)=>{var bytes=BitConverter.GetBytes(value);for(int i=0;i<4;i++)memory[address+(uint)i]=bytes[i];};
        Action<uint,byte[],int> read=(address,target,length)=>{for(int i=0;i<length;i++){byte value;target[i]=memory.TryGetValue(address+(uint)i,out value)?value:(byte)0;}};
        Func<uint,uint> u=address=>{var bytes=new byte[4];read(address,bytes,4);return BitConverter.ToUInt32(bytes,0);};
        uint dispatcher=0x10000,array=0x20000,dialog=0x30000,name=0x40000,password=0x50000,menu=0x80000,list=0x90000,controls=0xa0000,ok=0xb0000,cancel=0xc0000,screen=0xd0000;
        put(0x73d944,dispatcher);put(dispatcher+0x64,array);put(dispatcher+0x68,3);
        put(array,screen);put(array+12,menu);put(array+16,1);put(array+24,dialog);put(array+28,2);
        Action<uint,uint,uint,uint,uint,uint> pane=(address,vt,x,y,w,h)=>{put(address,vt);put(address+0x12c,x);put(address+0x128,y);put(address+0xe8,w);put(address+0xec,h);put(address+0x130,1);put(address+0x188,2);};
        // Captured metadata: Screen -> widened MainMenu -> LoginDialog.
        // Controls have flags 1 and are absent from the dispatcher tree.
        pane(screen,0x6835d4,0,0,1088,640);pane(menu,0x6788ec,0,0,1088,640);
        pane(dialog,0x67896c,218,190,217,170);pane(name,0x671ee4,65,57,88,16);pane(password,0x671ee4,65,99,88,16);
        pane(ok,0x671c64,28,130,62,20);pane(cancel,0x671c64,116,130,62,20);
        put(name+0x188,1);put(password+0x188,1);put(dialog+0x594,list);put(dialog+0x5ac,2);
        put(list,0x678304);put(list+0xc,4);put(list+0x14,4);put(list+0x18,controls);
        put(controls,ok);put(controls+4,cancel);put(controls+8,name);put(controls+12,password);
        put(name+0x19c,0x60000);put(password+0x19c,0x70000);put(0x60000,0x685e14);put(0x70000,0x685e14);
        var scene=LoginScene.Read(read,u,false);
        Check(scene.Ready&&scene.NameField==name&&scene.PasswordEditor==0x70000&&scene.PasswordPoint==new Point(303,297)&&scene.FocusedField==name,"Captured login control-list layout and dialog-local focus");
        put(dialog+0x130,0);Check(!LoginScene.Read(read,u,false).Ready,"Hidden parent cannot authorize credential input");put(dialog+0x130,1);
        put(password+0x128,100);Check(!LoginScene.Read(read,u,false).Ready,"Unexpected password geometry blocks login");put(password+0x128,99);
        put(0x70000,0x12345);Check(!LoginScene.Read(read,u,false).Ready,"Unknown linked editor blocks login");put(0x70000,0x685e14);
        bool rejected=false;int reads=0;Func<uint,uint> changing=address=>address==dispatcher+0x1b4?(uint)++reads:u(address);
        try{LoginScene.Read(read,changing,false);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Changing pane generation cancels login recognition");
        Check(!LoginScene.Read(read,u,true).Ready,"In-world UI is never a login form");
        put(menu+0x130,0);Check(!LoginScene.Read(read,u,false).Ready,"Invisible ancestor blocks credential input");put(menu+0x130,1);
        put(dispatcher+0x68,2);Check(LoginScene.Read(read,u,false).Menu&&LoginScene.Read(read,u,false).Continue==new Point(110,319),"Widened main menu retains logical Continue target");
        put(menu+0x500,1);Check(!LoginScene.Read(read,u,false).Menu&&LoginScene.Read(read,u,false).Reusable,"Pending stipulation is reusable but cannot receive Continue");put(menu+0x500,0);
        put(dispatcher+0x68,3);pane(dialog,0x669acc,30,95,444,377);pane(ok,0x671c64,110,352,61,22);
        scene=LoginScene.Read(read,u,false);Check(scene.Agreement==dialog&&!scene.Blocked&&scene.Accept==new Point(170,458),"Known agreement selects its own OK control before menu");
        put(ok,0x12345);Check(LoginScene.Read(read,u,false).Blocked,"Unknown agreement button fails closed");
        put(dialog,0x12345);Check(LoginScene.Read(read,u,false).Blocked,"Unknown popup prevents automated clicks on menu behind it");
        // Live observation: the agreement and focused login form can coexist
        // as sibling panes after the name has already been entered.
        put(dialog,0x67896c);pane(dialog,0x67896c,218,190,217,170);pane(ok,0x671c64,28,130,62,20);
        uint agreement=0xe0000,agreeList=0xf0000,agreeControls=0x110000,agreeOk=0x120000,agreeCancel=0x130000;
        put(dispatcher+0x68,4);put(array+36,agreement);put(array+40,2);
        pane(agreement,0x669acc,95,30,444,377);pane(agreeOk,0x671c64,110,352,61,22);pane(agreeCancel,0x671c64,275,352,61,22);
        put(agreement+0x594,agreeList);put(agreeList,0x678304);put(agreeList+0xc,4);put(agreeList+0x14,4);put(agreeList+0x18,agreeControls);
        put(agreeControls,agreeOk);put(agreeControls+4,agreeCancel);
        uint text=0x140000,label=0x150000;pane(text,0x671e44,15,39,414,300);pane(label,0x671f84,198,7,95,12);
        put(agreeControls+8,text);put(agreeControls+12,label);
        scene=LoginScene.Read(read,u,false);
        Check(scene.Agreement==agreement&&scene.Dialog==dialog&&!scene.Blocked&&!scene.Ready&&scene.Accept==new Point(235,393),
            "Recognized agreement can overlay the already-focused login form");
        return 13;
    }
    static int LoginFlowChecks()
    {
        int checks=0;DateTime time=new DateTime(2026,10,4);bool world=false;var scene=new LoginScene();
        var actions=new System.Collections.Generic.List<string>();
        Func<QuickLogin> create=()=>new QuickLogin(()=>scene,()=>world,()=>time,p=>actions.Add("click:"+p.X),s=>actions.Add("name"));
        var flow=create();Action tick=()=>{time=time.AddMilliseconds(300);flow.Tick();};
        tick();Check(actions.Count==0,"Loading cannot receive guessed startup clicks");checks++;
        scene=new LoginScene{Menu=true,Agreement=1,Accept=new Point(1,1)};tick();tick();Check(actions.SequenceEqual(new[]{"click:1"}),"Agreement is acknowledged once before Continue");checks++;
        scene=new LoginScene{Menu=true,Continue=new Point(2,1)};tick();tick();Check(actions.Last()=="click:2"&&actions.Count==2,"Continue is clicked once after agreement closes");checks++;
        flow.ManualInput();Check(!flow.Done,"Manually dismissing startup notice does not cancel pending name entry");checks++;
        scene=new LoginScene{Dialog=10,NameField=20,PasswordField=30,NamePoint=new Point(3,1),PasswordPoint=new Point(4,1),Submit=new Point(5,1)};
        tick();tick();tick();Check(actions.SequenceEqual(new[]{"click:1","click:2","click:3","name","click:4"})&&flow.Status.Contains("password")&&!flow.Done,"Quick start fills only the name and focuses the manual password field");checks++;
        flow.ManualInput();Check(flow.Done&&flow.Status.Contains("Continue signing in")&&actions.Count==5,"Manual password entry ends name automation without cancelling it");checks++;
        actions.Clear();flow=create();tick();tick();Check(actions.SequenceEqual(new[]{"click:3","name","click:4"}),"Quick start can refill an already-open name form");checks++;
        time=time.AddSeconds(6);tick();Check(flow.Done&&actions.Count==3,"Name-only flow ends after a short agreement watch without submitting login");checks++;
        actions.Clear();scene=new LoginScene{Blocked=true,Menu=true};flow=create();tick();Check(actions.Count==0,"Unknown notices block startup automation");checks++;
        actions.Clear();scene=new LoginScene{Dialog=10,NameField=20,PasswordField=30};flow=create();tick();scene=new LoginScene();tick();Check(flow.Done&&!actions.Contains("name"),"Disappearing pre-entry form stops without typing");checks++;
        actions.Clear();scene=new LoginScene{Dialog=10,NameField=20,PasswordField=30};flow=create();tick();flow.ManualInput();tick();Check(flow.Done&&!actions.Contains("name"),"Manual login interaction cancels before typing");checks++;
        actions.Clear();scene=new LoginScene();flow=create();time=time.AddSeconds(61);tick();Check(flow.Done&&actions.Count==0,"Startup timeout performs no input");checks++;
        actions.Clear();flow=new QuickLogin(()=>{throw new InvalidOperationException();},()=>false,()=>time,p=>actions.Add("input"),s=>actions.Add("name"));tick();Check(!flow.Done&&actions.Count==0,"Transient startup reads retry without typing");checks++;
        actions.Clear();scene=new LoginScene{Dialog=10,NameField=20,PasswordField=30,NamePoint=new Point(3,1),PasswordPoint=new Point(4,1),Accept=new Point(6,1)};
        flow=create();tick();scene.Blocked=true;tick();Check(actions.SequenceEqual(new[]{"click:3"})&&!flow.Done,"Partially registered agreement pauses name entry");checks++;
        scene.Blocked=false;scene.Agreement=11;tick();tick();Check(actions.SequenceEqual(new[]{"click:3","click:6"})&&!flow.Done,"Agreement over an open login form is clicked before typing");checks++;
        time=time.AddMilliseconds(700);tick();Check(actions.Last()=="click:6"&&actions.Count==3,"Persisting agreement receives a bounded retry");checks++;
        scene.Agreement=0;tick();Check(actions.Last()=="click:3"&&!actions.Contains("name"),"Agreement dismissal reselects the name field");checks++;
        tick();Check(actions.Last()=="click:4"&&actions.Contains("name"),"Quick start fills the name after agreement closes");checks++;
        scene.Agreement=11;tick();Check(actions.Last()=="click:6","Late agreement remains dismissible after name entry");checks++;
        scene.Agreement=0;tick();Check(actions.Last()=="click:4"&&!actions.Contains("password"),"Password field is refocused after a late agreement, without typing or submitting");checks++;
        return checks;
    }
}
