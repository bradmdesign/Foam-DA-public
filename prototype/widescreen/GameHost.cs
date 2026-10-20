using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal sealed class GameHostForm : Form
{
    readonly ComboBox viewport = new ComboBox(), display = new ComboBox(), filtering = new ComboBox();
    readonly ComboBox serverChoice=new ComboBox();
    ServerProfile customServer;
    readonly Button play = new DAButton(), detach = new DAButton(), restart = new DAButton();
    readonly FlowLayoutPanel gameTabs = new FlowLayoutPanel();
    readonly List<HostedSession> sessions = new List<HostedSession>();
    HostedSession activeSession;
    readonly CheckBox searchToggle = new DACheckBox();
    readonly CheckBox gameCursor = new DACheckBox();
    readonly CheckBox guardRedraws = new DACheckBox();
    readonly ComboBox frameRate = new ComboBox();
    readonly Panel stage = new Panel(), libraryPanel = new DAPanel(), searchPanel = new DAPanel(), controlsPanel = new DAPanel();
    readonly FlowLayoutPanel toolbar = new FlowLayoutPanel();
    readonly FlowLayoutPanel cornerBar = new FlowLayoutPanel();
    readonly FlowLayoutPanel tabRail = new FlowLayoutPanel();
    readonly Panel companionBody = new Panel();
    MacroPanel macros = new MacroPanel();
    HotkeyPanel hotkeys=new HotkeyPanel();
    readonly DAPanel tabsFrame=new DAPanel();
    readonly GainPanel gains=new GainPanel();
    readonly MapOverview overview=new MapOverview();
    bool settingsDirty;DateTime settingsDue;readonly bool previewOnly;
    readonly CharacterVault vault=new CharacterVault();
    readonly HostPreferences preferences=HostPreferences.Load();
    CharacterPanel characters;
    bool preferencesReady,managingCharacters,libraryOpen;
    DateTime lastGainPoll=DateTime.MinValue;
    DateTime lastSkullPoll=DateTime.MinValue;
    readonly FramePresenter macroTimer;
    long macroTicks;
    readonly Button stopAll = new DAButton();
    readonly MapPanel mapOptions = new MapPanel();
    readonly Button expand = new DAButton(), stopMacro = new DAButton();
    readonly ToolTip tabTips = new ToolTip();
    readonly Label controlsHint = new Label();
    readonly Timer chromeTimer = new Timer();
    readonly GameSurface surface = new GameSurface();
    readonly MiniMapControl miniMap=new MiniMapControl();
    readonly WebView2 library = new WebView2(), search = new WebView2();
    readonly Label status = new Label();
    readonly FramePresenter frames;
    GameFrame frame;
    GameTools gameTools;
    IntPtr gameWindow;
    HostNative.Rect oldRect;
    bool busy, closing;
    int missedFrames;
    int previousLayout=1,selectedTab;
    DateTime lastToolsPoll=DateTime.MinValue;
    string libraryBase;
    Uri lastSearch,lastReference;
    Process localServer;
    DateTime lastReport = DateTime.MinValue;
    DateTime rateSample=DateTime.UtcNow;
    int rateFrames;
    double measuredFps;
    DateTime lastControlsHover = DateTime.UtcNow;
    DateTime statusHoldUntil=DateTime.MinValue;
    readonly string[] profileNames = { "960x600", "1088x640", "1152x680" };
    static readonly Color Ink = Color.FromArgb(229,229,224), Chrome = Color.FromArgb(29,31,34);

    internal GameHostForm(bool attachExisting=true,bool startLibrary=false,bool checkChrome=false)
    {
        frames=new FramePresenter(this);macroTimer=new FramePresenter(this);
        previewOnly=!attachExisting;libraryOpen=startLibrary;Text = startLibrary?"Foam DA":previewOnly?"Foam DA · Preview":"Foam DA · Legit";
        AutoScaleMode = AutoScaleMode.None;
        Font = new Font("Segoe UI", 10);
        BackColor = Chrome; ForeColor = Ink;
        MinimumSize = new Size(1000,700);
        Size = new Size(1500,950); StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        toolbar.Dock=DockStyle.Top;toolbar.Height=44;toolbar.AutoSize=true;toolbar.AutoSizeMode=AutoSizeMode.GrowAndShrink;
        toolbar.Padding=new Padding(5,6,4,3);toolbar.WrapContents=true;toolbar.BackColor=DATheme.Dark;
        toolbar.Controls.Add(new Label {Text="FOAM DA",AutoSize=true,Padding=new Padding(0,5,14,0)});
        viewport.DropDownStyle=ComboBoxStyle.DropDownList; viewport.Width=225;
        viewport.Items.AddRange(new object[] {"Small · 960 × 600","Recommended · 1088 × 640","Roomy · 1152 × 680"});
        viewport.SelectedIndex=1;
        toolbar.Controls.Add(viewport);
        serverChoice.DropDownStyle=ComboBoxStyle.DropDownList;serverChoice.Width=180;serverChoice.AccessibleName="Server for new tabs";
        serverChoice.Items.AddRange(new object[]{ServerProfile.Kru(),ServerProfile.Local(),"Custom server…"});serverChoice.SelectedIndex=0;
        serverChoice.SelectedIndexChanged+=delegate {
            if(serverChoice.SelectedIndex!=2)return;
            var chosen=ServerProfile.Edit(this,customServer);
            if(chosen==null){serverChoice.SelectedIndex=0;return;}
            customServer=chosen;
            if(serverChoice.Items.Count==3)serverChoice.Items.Add(chosen);else serverChoice.Items[3]=chosen;
            serverChoice.SelectedIndex=3;
        };
        toolbar.Controls.Add(serverChoice);
        tabTips.SetToolTip(serverChoice,"Destination for new tabs. Restart keeps that tab's server. Custom servers require client 7.41 support.");
        play.Text="Launch new tab"; play.AutoSize=true; play.Click+=delegate {ShowLaunchMenu();};
        toolbar.Controls.Add(play);
        restart.Text="Restart tab at size";restart.AutoSize=true;restart.Enabled=false;restart.Click+=async delegate {await StartGame(true);};toolbar.Controls.Add(restart);
        display.DropDownStyle=ComboBoxStyle.DropDownList; display.Width=160;
        display.Items.AddRange(new object[] {"Game + Library","Fit to window","Stretch to full","Actual size"});
        display.SelectedIndex=1;
        display.SelectedIndexChanged+=delegate {
            if(display.SelectedIndex==2 && filtering.SelectedIndex==2)filtering.SelectedIndex=3;
            if(display.SelectedIndex!=0)previousLayout=display.SelectedIndex;
            ArrangeHost();
        };
        toolbar.Controls.Add(display);
        filtering.DropDownStyle=ComboBoxStyle.DropDownList; filtering.Width=180;
        filtering.Items.AddRange(new object[]{"Smooth · bilinear","Sharp · bicubic","Integer pixels","Crisp · pixel edges"});
        filtering.SelectedIndex=3;
        filtering.SelectedIndexChanged+=delegate {
            if(filtering.SelectedIndex==2 && display.SelectedIndex==2)display.SelectedIndex=1;
            ArrangeHost();
        };
        toolbar.Controls.Add(filtering);
        searchToggle.Text="Companion in corner"; searchToggle.AutoSize=true; searchToggle.Checked=true;
        searchToggle.Padding=new Padding(8,4,0,0); searchToggle.CheckedChanged+=delegate {ArrangeDock();};
        // Companion is mandatory; its former toggle remains internal for compatibility.
        gameCursor.Text="DA cursor";gameCursor.AutoSize=true;gameCursor.Checked=true;gameCursor.Padding=new Padding(4,4,0,0);
        gameCursor.CheckedChanged+=delegate{surface.SetGameCursor(gameCursor.Checked);};surface.SetGameCursor(true);
        guardRedraws.Text="Completed frames";guardRedraws.Checked=true;guardRedraws.AutoSize=true;guardRedraws.Padding=new Padding(4,4,0,0);
        guardRedraws.CheckedChanged+=delegate {foreach(var session in sessions)session.Frame.GuardRedraws=guardRedraws.Checked;};toolbar.Controls.Add(guardRedraws);
        tabTips.SetToolTip(guardRedraws,"On: use the native redraw sequence to reject mixed frames (revision 10). Older clients use the redraw flag. Off: raw capture for comparison. Changes live without blocking input.");
        frameRate.DropDownStyle=ComboBoxStyle.DropDownList;frameRate.Width=88;frameRate.Items.AddRange(new object[]{"60 fps","30 fps","20 fps"});frameRate.SelectedIndex=0;
        frameRate.SelectedIndexChanged+=delegate {frames.Interval=new[]{16,33,50}[Math.Max(0,frameRate.SelectedIndex)];};toolbar.Controls.Add(frameRate);
        tabTips.SetToolTip(frameRate,"Target presentation rate. Actual rate depends on the game, scaling cost and system load. Changes live.");
        detach.Text="Detach game"; detach.AutoSize=true; detach.Enabled=false; detach.Click+=delegate {DetachGame();};
        toolbar.Controls.Add(detach);
        stopAll.Text="Stop all macros";stopAll.AutoSize=true;
        stopAll.Click+=delegate{foreach(var session in sessions)session.Macros.Stop("Stopped · Stop all.");hotkeys.Stop("Stopped · Stop all.");};toolbar.Controls.Add(stopAll);
        status.Dock=DockStyle.Bottom; status.Height=27; status.Padding=new Padding(10,4,0,0);
        status.Text="Choose a viewport before signing in. Display mode changes are live; viewport changes reconnect.";
        status.ForeColor=Color.Silver;
        stage.Dock=DockStyle.None; stage.BackColor=Color.FromArgb(15,16,19);
        stage.Controls.Add(surface);
        var libraryBar=new FlowLayoutPanel {Dock=DockStyle.Top,Height=28,WrapContents=false,Padding=Padding.Empty,BackColor=DATheme.Dark};
        foreach (string title in new[] {"Return","Back","Search","Library"})
        {
            var button=new DAButton {Text=title,Width=72,Height=26,Margin=new Padding(1,0,1,0)};
            string action=title;
            button.Click+=delegate {
                if(action=="Return"){SetLibraryOpen(false);return;}
                if(library.CoreWebView2==null)return;
                if(action=="Back") {if(library.CanGoBack)library.GoBack();}
                else library.Source=new Uri(libraryBase+(action=="Search"?"/search/":"/"));
            };
            libraryBar.Controls.Add(button);
        }
        library.Dock=DockStyle.Fill;
        libraryPanel.Controls.Add(library); libraryPanel.Controls.Add(libraryBar);
        cornerBar.Dock=DockStyle.Top;cornerBar.Height=24;cornerBar.WrapContents=false;cornerBar.Padding=Padding.Empty;cornerBar.BackColor=DATheme.Dark;
        foreach(string title in new[]{"Back","Search"})
        {
            string action=title;
            var button=new DAButton {Text=title,Width=64,Height=24,Margin=new Padding(1,0,1,0)};
            button.Click+=delegate {
                if(search.CoreWebView2==null)return;
                SelectTab(0,false);
                if(action=="Back") {if(search.CanGoBack)search.GoBack();}
                else if(action=="Search")search.Source=lastSearch??new Uri(libraryBase+"/search/");
            };
            cornerBar.Controls.Add(button);
        }
        expand.Text="Expand";expand.Width=64;expand.Height=24;expand.Margin=new Padding(1,0,1,0);
        expand.Click+=delegate {
            display.SelectedIndex=display.SelectedIndex==0?previousLayout:0;};
        cornerBar.Controls.Add(expand);
        stopMacro.Text="Stop";stopMacro.Width=64;stopMacro.Height=24;stopMacro.Margin=new Padding(1,0,1,0);stopMacro.Visible=false;
        stopMacro.Click+=delegate{macros.Stop("Stopped.");hotkeys.Stop("Stopped.");};cornerBar.Controls.Add(stopMacro);
        search.Dock=DockStyle.Fill;
        macros.Dock=mapOptions.Dock=hotkeys.Dock=DockStyle.Fill;
        companionBody.Dock=DockStyle.Fill;companionBody.Controls.Add(search);companionBody.Controls.Add(macros);companionBody.Controls.Add(mapOptions);companionBody.Controls.Add(hotkeys);
        tabRail.Dock=DockStyle.Left;tabRail.Width=38;tabRail.FlowDirection=FlowDirection.TopDown;tabRail.WrapContents=false;tabRail.BackColor=DATheme.Dark;tabRail.AutoScroll=true;
        var companionOrder=new[]{0,1,3,5
            ,2
        };
        foreach(int id in companionOrder)
        {
            int tab=id;var button=new DATabButton{Icon=id,Tag=id,AccessibleName=id==0?"Search":id==1?"Macros":id==2?"Settings":id==3?"Hotkeys":id==5?"My skill list":"Character controls",Size=new Size(32,26),Margin=new Padding(2,1,2,1)};
            tabTips.SetToolTip(button,button.AccessibleName);button.Click+=delegate{SelectTab(tab);};tabRail.Controls.Add(button);
        }
        tabRail.SizeChanged+=delegate{
            int height=Math.Max(18,Math.Min(26,tabRail.ClientSize.Height/tabRail.Controls.Count-2));
            foreach(Control button in tabRail.Controls)if(button.Height!=height)button.Height=height;
        };
        searchPanel.Controls.Add(companionBody);searchPanel.Controls.Add(tabRail);searchPanel.Controls.Add(cornerBar);
        DATheme.StyleCompanion(cornerBar);DATheme.StyleCompanion(libraryBar);
        SelectTab(0);
        mapOptions.OptionsChanged+=delegate{ApplyMapOptions();};
        stage.Controls.Add(overview);overview.Visible=false;
        overview.Full=preferences.OverviewFull;overview.LayoutChanged+=delegate{ArrangeHost();SavePreferences();};
        miniMap.OverviewRequested+=delegate{if(activeSession!=null){overview.ShowMap(activeSession);ApplyMapOptions();ArrangeHost();}};
        miniMap.ZoomChanged+=delegate{SavePreferences();WriteHostState();};
        overview.Closed+=delegate{ApplyMapOptions();surface.Focus();};
        surface.Controls.Add(gains);surface.Controls.Add(miniMap);miniMap.CollapsedChanged+=delegate{ApplyMapOptions();ArrangeMiniMap();};
        tabTips.SetToolTip(mapOptions.TabTransparency,"Tab overlay: 0% is opaque; 95% is almost invisible.");
        surface.ManualInput+=delegate{if(hotkeys.Running)hotkeys.Stop("Stopped · manual game input.");if(activeSession!=null&&activeSession.Login!=null&&!activeSession.Login.Done)activeSession.Login.ManualInput();};
        surface.ScreenshotRequested+=SaveHostedScreenshot;
        mapOptions.LastSkulled.Click+=delegate{SkullHistory.Show(this);};
        surface.TryHotkey=delegate(Keys keys){bool accepted=hotkeys.Trigger(keys);if(accepted)macros.Stop("Stopped · hotkey combo.");return accepted;};
        surface.LostFocus+=delegate{if(hotkeys.Running)hotkeys.Stop("Stopped · game focus changed.");};
        surface.ToggleFog+=delegate{mapOptions.Fog.Checked=!mapOptions.Fog.Checked;};
        surface.PlayZoom+=delegate(int steps){mapOptions.PlayZoom.Value+=steps*5;};
        surface.MapZoom+=delegate(int steps){
            if(gameTools==null || !gameTools.MapVisible)return;
            int current=mapOptions.CustomZoom.Checked?(int)mapOptions.ZoomScale.Value:gameTools.MapScale;
            mapOptions.ZoomScale.Value=Math.Max(1,Math.Min(10,current+steps));
            mapOptions.CustomZoom.Checked=true;ApplyMapOptions();
        };
        stage.Controls.Add(libraryPanel); stage.Controls.Add(searchPanel);
        characters=new CharacterPanel(vault);characters.SelectedServer=delegate{return serverChoice.SelectedItem as ServerProfile??ServerProfile.Kru();};
        characters.Dismissed+=delegate{SetCharacterPanel(false);};
        characters.Launch+=async delegate(SavedCharacter entry){await LoginCharacter(entry);};stage.Controls.Add(characters);
        gameTabs.Dock=DockStyle.Fill;tabsFrame.Controls.Add(gameTabs);
        gameTabs.Height=29;gameTabs.WrapContents=false;gameTabs.AutoScroll=true;gameTabs.BackColor=DATheme.Dark;
        gameTabs.AccessibleName="Character tabs";
        controlsPanel.Controls.Add(toolbar);controlsPanel.Controls.Add(status);
        controlsHint.Text="···";controlsHint.TextAlign=ContentAlignment.MiddleCenter;
        controlsHint.Font=new Font("Segoe UI",10,FontStyle.Bold);controlsHint.ForeColor=DATheme.Gold;controlsHint.BackColor=DATheme.Dark;
        controlsHint.Cursor=Cursors.Hand;controlsHint.AccessibleName="Show game display controls";controlsHint.TabStop=true;
        controlsHint.MouseEnter+=delegate {ShowControls();};controlsHint.Click+=delegate {ShowControls();};
        controlsHint.Enter+=delegate {ShowControls();};
        Controls.Add(stage);Controls.Add(controlsPanel);Controls.Add(controlsHint);Controls.Add(tabsFrame);
        chromeTimer.Interval=100;chromeTimer.Tick+=delegate {UpdateControlsVisibility();HostPointer.Restore(this,surface);};chromeTimer.Start();
        Activated+=delegate {HostPointer.Restore(this,surface);};
        Deactivate+=delegate {surface.ReleaseHeldInput();if(hotkeys.Running)hotkeys.Stop("Stopped · game focus changed.");};
        stage.Resize+=delegate {ArrangeHost();};
        Resize+=delegate {ArrangeHost();};
        surface.GeometryChanged+=delegate {ArrangeDock();};
        Shown+=async delegate {if(checkChrome)return;await InitializeLibrary(); ArrangeHost();if(attachExisting)await AttachExisting();};
        FormClosing+=delegate(object sender,FormClosingEventArgs e) {
            if(busy) {e.Cancel=true; status.Text="Wait for the client restart to finish.";return;}
            SavePreferences();FlushPreferences();closing=true;frames.Stop();while(sessions.Count>0)RemoveSession(sessions[0],true);
        };
        FormClosed+=delegate {
            library.Dispose();search.Dispose();frames.Dispose();chromeTimer.Dispose();macroTimer.Dispose();tabTips.Dispose();
            // Leave a reused site server alone. Stop only the dedicated server
            // this host created on its own port.
            if(localServer!=null) {try {if(!localServer.HasExited)localServer.Kill();}catch{} localServer.Dispose();}
        };
        frames.Interval=16;
        frames.Tick+=delegate {RenderFrame();};
        macroTimer.Interval=50;macroTimer.Tick+=delegate {
            macroTicks++;
            foreach(var session in sessions)
            {
                if(!HostNative.IsWindow(session.Frame.Window)){RecordSessionOffline(session);session.Macros.Stop("Stopped · game closed.");session.Appearance.Flush(session.Frame,session.Tools,session.Server);continue;}
                // Inputs target only this session's HWND. Background macros
                // continue across tab switches and host focus changes.
                session.Macros.Tick(true);
                
                if(session.Login!=null&&!session.Login.Done)
                {
                    string previous=session.Login.Status;
                    session.Login.Tick();
                    if(session==activeSession)
                    {
                        characters.SetStatus(session.Login.Status);
                        if(session.Login.Status!=previous){status.Text=session.Login.Status;statusHoldUntil=DateTime.UtcNow.AddSeconds(session.Login.Done?15:4);WriteHostState();}
                    }
                }
            }
            if((DateTime.UtcNow-lastSkullPoll).TotalMilliseconds>=250)
            {
                lastSkullPoll=DateTime.UtcNow;
                foreach(var session in sessions)
                {
                    session.Skulls.Tick(session.Frame,session.Tools,session.Server);
                    if(session.Skulls.Notice!=null){status.Text=session.Skulls.Notice;session.Skulls.Notice=null;statusHoldUntil=DateTime.UtcNow.AddSeconds(12);}
                }
            }
            if((DateTime.UtcNow-lastGainPoll).TotalSeconds>=1)
            {
                lastGainPoll=DateTime.UtcNow;if(settingsDirty&&DateTime.UtcNow>=settingsDue)FlushPreferences();
                foreach(var session in sessions)try{if(session!=activeSession){session.Tools.PollMap();if(session.WorldObserved&&!session.Frame.IsInWorldNow)RecordSessionOffline(session);}session.Gains.Sample(session.Tools.Progression(session.Server.Key),lastGainPoll);}catch{session.Gains.Sample(null,lastGainPoll);}
                UpdateGains();
                foreach(var session in sessions)
                {
                    session.Appearance.Tick(session.Frame,session.Tools,session.Server);
                    if(session.Appearance.Changed){session.Appearance.Changed=false;characters.RefreshPortraits();}
                }
            }
            hotkeys.Tick(ContainsFocus && surface.Focused,surface.ShortcutHeld);
            stopAll.Enabled=sessions.Exists(s=>s.Macros.Running)||hotkeys.Running;stopMacro.Visible=macros.Running||hotkeys.Running;
            foreach(Control tab in gameTabs.Controls)
            {
                var session=tab.Tag as HostedSession;
                if(session!=null)tab.Text=SessionTitle(session);
            }
        };macroTimer.Start();
        preferences.Apply(mapOptions);miniMap.ZoomPercent=HostPreferences.Clamp(preferences.MiniMapZoom,10,400);display.SelectedIndex=HostPreferences.Clamp(preferences.Display,0,3);filtering.SelectedIndex=HostPreferences.Clamp(preferences.Filter,0,3);
        frameRate.SelectedIndex=HostPreferences.Clamp(preferences.Fps,0,2);viewport.SelectedIndex=HostPreferences.Clamp(preferences.Viewport,0,2);guardRedraws.Checked=preferences.Guard;
        preferencesReady=true;
        foreach(var combo in new[]{display,filtering,frameRate,viewport})combo.SelectedIndexChanged+=delegate{SavePreferences();};
        guardRedraws.CheckedChanged+=delegate{SavePreferences();};RefreshGameTabs();ApplyMapOptions();
    }

    readonly SkillListStorage skillListStorage=new SkillListStorage();
    async Task InitializeLibrary()
    {
        try
        {
            libraryBase=await EnsureLibrary();
            var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(Program.Root,"build",previewOnly?"webview-preview-profile":"webview-profile"));
            await library.EnsureCoreWebView2Async(env);
            await search.EnsureCoreWebView2Async(env);
            string css=File.ReadAllText(Path.Combine(Program.Root,"theme","assets.css"))+File.ReadAllText(Path.Combine(Program.Root,"host-dock.css"));
            css+="\n:root{--da-companion-font-size:"+DATheme.CompanionFontPixels+"px;}";
            string script="window.daSkillListHost=true;document.addEventListener('DOMContentLoaded',function(){if(location.origin!=="+
                Program.Json.Serialize(libraryBase)+")return;"+
                "window.daSkillListHost=true;var s=document.createElement('style');s.textContent="+Program.Json.Serialize(css)+";document.head.appendChild(s);});";
            await search.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
            await library.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
            search.CoreWebView2.SourceChanged+=delegate {
                Uri location;
                if(!Uri.TryCreate(search.CoreWebView2.Source,UriKind.Absolute,out location)||!LibraryNavigation.IsLocal(location.AbsoluteUri,libraryBase))return;
                bool skills=IsSkillList(location);
                if(!skills)lastReference=location;
                if(location.AbsolutePath.TrimEnd('/')=="/search")lastSearch=location;
                if(selectedTab==0||selectedTab==5)SelectTab(skills?5:0,false);
            };
            foreach(var browser in new[]{library,search})
            {
                var current=browser;
                browser.CoreWebView2.NewWindowRequested+=delegate(object sender,CoreWebView2NewWindowRequestedEventArgs e) {
                    // Links stay in the pane where they were opened. Only the
                    // user's Expand button changes the game/library layout.
                    e.Handled=true;if(LibraryNavigation.IsLocal(e.Uri,libraryBase))current.Source=new Uri(e.Uri);
                };
                browser.CoreWebView2.NavigationStarting+=delegate(object sender,CoreWebView2NavigationStartingEventArgs e){
                    if(!LibraryNavigation.IsLocal(e.Uri,libraryBase)){e.Cancel=true;status.Text="External links stay outside this local library.";}
                };
                browser.CoreWebView2.WebMessageReceived+=delegate(object sender,CoreWebView2WebMessageReceivedEventArgs e){HandleSkillListMessage(current,e);};
                browser.CoreWebView2.Settings.IsStatusBarEnabled=false;
                browser.CoreWebView2.Settings.IsZoomControlEnabled=false;
            }
            library.Source=new Uri(libraryBase+"/");
            search.Source=new Uri(libraryBase+(selectedTab==5?"/my-skill-list/":"/search/"));
            status.Text="Library ready. Choose a viewport and launch the game.";
        }
        catch(Exception ex) {status.Text="Library unavailable: "+ex.Message;}
    }
    void HandleSkillListMessage(WebView2 browser,CoreWebView2WebMessageReceivedEventArgs e)
    {
        Uri source;if(!LibraryNavigation.IsLocal(e.Source,libraryBase)||!Uri.TryCreate(e.Source,UriKind.Absolute,out source)||source.AbsolutePath.TrimEnd('/')!="/my-skill-list")return;
        try{
            if(e.WebMessageAsJson.Length>2*1024*1024)return;
            var message=Program.Json.Deserialize<Dictionary<string,object>>(e.WebMessageAsJson);object raw;if(message==null||!message.TryGetValue("type",out raw))return;string type=raw as string;
            if(type=="da-skill-list-load"){
                var state=skillListStorage.Load();browser.CoreWebView2.PostWebMessageAsJson(Program.Json.Serialize(new{type="da-skill-list-state",state=state,error=skillListStorage.Error}));
            }else if(type=="da-skill-list-save"&&message.TryGetValue("state",out raw)){
                skillListStorage.Save(Program.Json.Deserialize<SkillListState>(Program.Json.Serialize(raw)));
            }else if(type=="da-skill-list-export"&&message.TryGetValue("csv",out raw)){
                string csv=raw as string;if(csv==null||csv.Length>1024*1024||!csv.StartsWith("\"My skill list\""))return;
                using(var save=new SaveFileDialog{Title="Save my skill list",FileName="My-skill-list.csv",Filter="CSV spreadsheet (*.csv)|*.csv",DefaultExt="csv",AddExtension=true,OverwritePrompt=true}){
                    if(save.ShowDialog(this)==DialogResult.OK){File.WriteAllText(save.FileName,csv,new System.Text.UTF8Encoding(true));browser.CoreWebView2.PostWebMessageAsJson(Program.Json.Serialize(new{type="da-skill-list-result",message="Requirements sheet saved."}));}
                }
            }
        }catch(Exception ex){
            string message=ex is InvalidOperationException?ex.Message:"The local skill list could not be saved. You can keep working and export a sheet.";
            browser.CoreWebView2.PostWebMessageAsJson(Program.Json.Serialize(new{type="da-skill-list-result",message=message}));
        }
    }
    static async Task<bool> SiteResponds(string address)
    {
        try
        {
            var request=WebRequest.CreateHttp(address+"/");request.Timeout=1500;
            using(var response=await request.GetResponseAsync())
            using(var reader=new StreamReader(response.GetResponseStream()))
            {
                // The static export puts its inlined stylesheet before the title.
                string html=await reader.ReadToEndAsync();
                return html.IndexOf("<title>Foam DA",StringComparison.OrdinalIgnoreCase)>=0||html.IndexOf("<title>Dark Ages Library",StringComparison.OrdinalIgnoreCase)>=0;
            }
        }
        catch {return false;}
    }
    async Task<string> EnsureLibrary()
    {
        string site=Path.Combine(Program.Root,"library");
        bool bundled=Directory.Exists(site);
        if(!bundled)
        {
            if(await SiteResponds("http://127.0.0.1:4173"))return "http://127.0.0.1:4173";
            site=Path.GetFullPath(Path.Combine(Program.Root,"..","..","site"));
        }
        var paths=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(Program.Root,"deps","host-runtime.json")));
        string node=(string)paths["node"];
        if(!Path.IsPathRooted(node))node=Path.GetFullPath(Path.Combine(Program.Root,node));
        string index=Path.Combine(site,"dist","client","index.html");
        if(!File.Exists(node)||(!File.Exists(index)&&!File.Exists(index+".gz")))
            throw new InvalidOperationException("The local library files are missing. Reinstall the complete package.");
        // Every portable installation serves its own content. Never silently
        // connect a release build to an unrelated developer server.
        int port;
        // Host-owned servers are private in development too. Otherwise closing
        // the standalone Library could kill the server used by a game host.
        {
            var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);
            try {listener.Start();port=((IPEndPoint)listener.LocalEndpoint).Port;}
            finally {listener.Stop();}
        }
        string address="http://127.0.0.1:"+port;
        var start=new ProcessStartInfo(node,"\""+Path.Combine(site,"scripts","serve-local.mjs")+"\"");
        start.WorkingDirectory=site;start.UseShellExecute=false;start.CreateNoWindow=true;
        start.EnvironmentVariables["PORT"]=port.ToString();
        localServer=Process.Start(start);
        for(int i=0;i<20;i++) {await Task.Delay(250);if(await SiteResponds(address))return address;}
        throw new InvalidOperationException("The local library server did not become ready.");
    }

    async Task AttachExisting()
    {
        if(busy)return;busy=true;
        try
        {
            foreach(var process in GameSessions.Running())
            using(process)
            {
                try {await AttachProcess(process,0);}
                catch(Exception ex){status.Text="Existing session: "+ex.Message;}
            }
        }
        finally {busy=false;RefreshGameTabs();WriteHostState();}
    }
    async Task LoginCharacter(SavedCharacter login)
    {
        if(busy)return;
        HostedSession target=FindLoginTab(login);
        if(target==null){await StartGame(false,login);return;}
        ActivateSession(target);managingCharacters=false;
        if(target.Login==null||target.Login.Done)
            target.Login=new QuickLogin(target.Frame,login,login.Server.Key);
        characters.SetStatus(target.Login.Status);
        ArrangeHost();WriteHostState();
    }
    HostedSession FindLoginTab(SavedCharacter login)
    {
        var ordered=new List<HostedSession>();if(activeSession!=null)ordered.Add(activeSession);
        foreach(var session in sessions)if(session!=activeSession)ordered.Add(session);
        foreach(var session in ordered)
        {
            if(session.Server.Key!=login.Server.Key||!HostNative.IsWindow(session.Frame.Window))continue;
            // Background frames may be paused; never trust cached InWorld here.
            try
            {
                if(session.Login!=null&&!session.Login.Done)
                {if(session.Login.Matches(login))return session;continue;}
                if(!session.Frame.IsInWorldNow&&LoginScene.Read(session.Frame).Reusable)return session;
            }catch{}
        }
        return null;
    }
    async Task StartGame(bool replace=false,SavedCharacter login=null)
    {
        if(busy)return;
        busy=true;play.Enabled=restart.Enabled=viewport.Enabled=detach.Enabled=serverChoice.Enabled=false;
        HostedSession previous=activeSession;
        try
        {
            string profile=profileNames[viewport.SelectedIndex],reuse=null;int id=0;
            var server=login!=null?login.Server:replace&&previous!=null?previous.Server:(serverChoice.SelectedItem as ServerProfile??ServerProfile.Kru());
            if(replace && previous!=null)
            {
                reuse=previous.GamePath;id=previous.Id;
                using(var process=Process.GetProcessById(previous.Frame.ProcessId))
                {
                    status.Text="Closing this tab's game to change viewport…";
                    macros.Stop("Stopped · restarting tab.");
                    HostNative.PostMessage(previous.Frame.Window,0x10,IntPtr.Zero,IntPtr.Zero);
                    for(int i=0;i<100&&!process.HasExited;i++)await Task.Delay(100);
                    if(!process.HasExited)throw new InvalidOperationException("This game is still closing; its tab has been retained.");
                }
                RemoveSession(previous,false);
            }
            status.Text="Preparing a separate game session…";
            string game=await Task.Run(()=>GameSessions.Prepare(profile,reuse,server));
            uint pid=Program.LaunchInteractive(game,server.Arguments);
            using(var process=Process.GetProcessById((int)pid))await AttachProcess(process,id);
            managingCharacters=false;
            if(login!=null&&activeSession!=null)activeSession.Login=new QuickLogin(activeSession.Frame,login,server.Key);
            ArrangeHost();
            status.Text=activeSession!=null&&activeSession.Login!=null?activeSession.Login.Status:"Game opened in its tab. Sign in here; Launch new tab adds another session.";
            if(activeSession!=null&&activeSession.Login!=null)characters.SetStatus(activeSession.Login.Status);
        }
        catch(Exception ex) {status.Text="Launch: "+ex.Message;statusHoldUntil=DateTime.UtcNow.AddSeconds(15);ShowControls();}
        finally {busy=false;play.Enabled=viewport.Enabled=serverChoice.Enabled=true;RefreshGameTabs();WriteHostState();}
    }
    async Task AttachProcess(Process process,int id)
    {
        GameFrame capture=null;Exception last=null;
        try
        {
            for(int i=0;i<150;i++)
            {
                if(process.HasExited)throw new InvalidOperationException("The game closed during startup.");
                try {capture=new GameFrame(process);break;}catch(Exception ex){last=ex;}
                await Task.Delay(100);
            }
            if(capture==null)throw new InvalidOperationException(last==null?"The game window is not ready.":last.Message);
            string gamePath=process.MainModule.FileName;
            if(id==0)id=String.Equals(gamePath,Program.Game,StringComparison.OrdinalIgnoreCase)?9:int.Parse(Path.GetFileName(Path.GetDirectoryName(gamePath)).Substring(5));
            var session=new HostedSession {Id=id,GamePath=gamePath,Server=ServerProfile.Read(gamePath),
                Profile=capture.Width+"x"+capture.Height,Frame=capture,Tools=new GameTools(capture),Macros=new MacroPanel {Dock=DockStyle.Fill},Hotkeys=new HotkeyPanel {Dock=DockStyle.Fill,Enabled=false}};
            session.Macros.Tools=session.Tools;
            HostNative.GetWindowRect(capture.Window,out session.OldRect);
            if(session.OldRect.Left<0 || session.OldRect.Top<0)
            {session.OldRect.Right-=session.OldRect.Left;session.OldRect.Bottom-=session.OldRect.Top;session.OldRect.Left=session.OldRect.Top=0;}
            if(!HostNative.SetWindowPos(capture.Window,IntPtr.Zero,-10000,-10000,
                session.OldRect.Right-session.OldRect.Left,session.OldRect.Bottom-session.OldRect.Top,0x34))
                throw new InvalidOperationException("The game window could not be parked.");
            sessions.Add(session);sessions.Sort((a,b)=>a.Id.CompareTo(b.Id));capture=null;ActivateSession(session);
        }
        finally {if(capture!=null)capture.Dispose();}
    }
    void ActivateSession(HostedSession session)
    {
        if(libraryOpen)SetLibraryOpen(false);
        managingCharacters=false;
        if(activeSession==session){SetCharacterPanel(false);return;}
        overview.Visible=false;SuspendSession();activeSession=session;
        if(session==null){RefreshGameTabs();return;}
        frame=session.Frame;gameTools=session.Tools;gameWindow=frame.Window;oldRect=session.OldRect;
        frame.Paused=false;frame.GuardRedraws=guardRedraws.Checked;
        companionBody.Controls.Remove(macros);if(!sessions.Exists(s=>s.Macros==macros))macros.Dispose();
        macros=session.Macros;companionBody.Controls.Add(macros);
        companionBody.Controls.Remove(hotkeys);if(!sessions.Exists(s=>s.Hotkeys==hotkeys))hotkeys.Dispose();
        hotkeys=session.Hotkeys;companionBody.Controls.Add(hotkeys);SelectTab(selectedTab);
        surface.Frame=frame;surface.Arrange();macros.Tools=gameTools;hotkeys.Tools=gameTools;
        ApplyMapOptions();if(frame.InWorld){macros.RefreshAbilities();BindCharacter(session);}
        rateSample=DateTime.UtcNow;rateFrames=frame.Frames;measuredFps=0;missedFrames=0;
        // Launch size is a global preference; selecting a tab does not change it.
        frames.Start();ArrangeHost();RefreshGameTabs();WriteHostState();
    }
    void SuspendSession()
    {
        hotkeys.Stop("Stopped · game tab changed.");hotkeys.Tools=null;
        surface.ReleaseHeldInput();
        if(frame!=null)frame.Paused=true;
        frames.Stop();surface.Frame=null;frame=null;gameTools=null;gameWindow=IntPtr.Zero;activeSession=null;
        surface.Arrange();ArrangeDock();
    }
    void RefreshGameTabs()
    {
        while(gameTabs.Controls.Count>0){Control old=gameTabs.Controls[0];var tab=old as DAGameTab;if(tab!=null)tabTips.SetToolTip(tab.CloseButton,null);gameTabs.Controls.Remove(old);old.Dispose();}
        foreach(var entry in sessions)
        {
            var session=entry;
            var tab=new DAGameTab(SessionTitle(session),delegate{if(!busy)ActivateSession(session);},async delegate{await CloseGameTab(session);}) {Tag=session,Enabled=!busy};
            tabTips.SetToolTip(tab.CloseButton,"Close this game tab");gameTabs.Controls.Add(tab);
        }
        var manage=new DAButton{Text="My characters",AutoSize=true,Height=26,Margin=new Padding(2,0,2,0)};
        manage.Click+=delegate{SetCharacterPanel(!characters.Visible);};gameTabs.Controls.Add(manage);
        var site=new DAButton{Text=libraryOpen?"Back to game":"Library",AutoSize=true,Height=26,Margin=new Padding(2,0,2,0),AccessibleName=libraryOpen?"Close full library":"Open full library website"};
        site.Click+=delegate{SetLibraryOpen(!libraryOpen);};gameTabs.Controls.Add(site);
        var settings=new DAButton{Text="Settings",AutoSize=true,Height=26,Margin=new Padding(2,0,2,0)};
        settings.Click+=delegate{ShowSettings(settings);};gameTabs.Controls.Add(settings);
        restart.Enabled=detach.Enabled=activeSession!=null&&!busy;
        play.Text="Launch new tab";ArrangeHost();
    }
    string SessionTitle(HostedSession session){return (session==activeSession?"● ":"")+(String.IsNullOrEmpty(session.CharacterName)?"Game "+session.Id:session.CharacterName)+" · "+session.Profile+(session.Server.Official?"":" · "+session.Server.Name)+(session.Macros.Running?" · M":"");}
    async Task CloseGameTab(HostedSession session)
    {
        if(busy||!sessions.Contains(session))return;
        busy=true;RefreshGameTabs();play.Enabled=viewport.Enabled=serverChoice.Enabled=false;
        session.Macros.Stop("Stopped · closing game tab.");session.Hotkeys.Stop("Stopped · closing game tab.");
        if(session.Login!=null)session.Login.Cancel();
        if(session==activeSession){surface.ReleaseHeldInput();frames.Stop();}
        try {
            status.Text="Closing this game tab…";
            if(HostNative.IsWindow(session.Frame.Window))
                await GameSessions.CloseGame(session.Frame.ProcessId,session.Frame.Window,session.GamePath);
            if(sessions.Contains(session))RemoveSession(session,false);
            status.Text="Game tab closed.";statusHoldUntil=DateTime.UtcNow.AddSeconds(5);
        }catch(Exception ex){status.Text="Could not close this game tab: "+ex.Message;statusHoldUntil=DateTime.UtcNow.AddSeconds(15);ShowControls();}
        finally {
            busy=false;play.Enabled=viewport.Enabled=serverChoice.Enabled=true;
            if(activeSession!=null)frames.Start();RefreshGameTabs();WriteHostState();
        }
    }
    void RemoveSession(HostedSession session,bool restore)
    {
        if(!HostNative.IsWindow(session.Frame.Window))RecordSessionOffline(session);
        session.Appearance.Flush(session.Frame,session.Tools,session.Server);session.Skulls.Flush();
        bool selected=activeSession==session;
        if(selected)SuspendSession();
        if(restore && HostNative.IsWindow(session.Frame.Window))
        {
            var rect=session.OldRect;
            HostNative.SetWindowPos(session.Frame.Window,IntPtr.Zero,Math.Max(0,rect.Left),Math.Max(0,rect.Top),rect.Right-rect.Left,rect.Bottom-rect.Top,0x34);
            HostNative.ShowWindow(session.Frame.Window,5);
        }
        session.Macros.Detach();companionBody.Controls.Remove(session.Macros);session.Macros.Dispose();
        session.Hotkeys.Stop("Game detached.");companionBody.Controls.Remove(session.Hotkeys);session.Hotkeys.Dispose();session.Tools.Dispose();session.Frame.Dispose();sessions.Remove(session);
        if(selected && sessions.Count==0){macros=new MacroPanel {Dock=DockStyle.Fill};hotkeys=new HotkeyPanel{Dock=DockStyle.Fill};companionBody.Controls.Add(macros);companionBody.Controls.Add(hotkeys);SelectTab(selectedTab);}
        if(!closing && selected && sessions.Count>0)ActivateSession(sessions[0]);
        RefreshGameTabs();
    }
    void RenderFrame()
    {
        if(frame==null)return;
        if(!HostNative.IsWindow(gameWindow)) {RemoveSession(activeSession,false);status.Text="The game has closed. Launch a new tab to play again.";return;}
        try
        {
            bool wasWorld=frame.InWorld, wasCompact=frame.Compact;
            Rectangle wasMap=frame.TravelMap;
            int before=frame.Frames;frame.Update();missedFrames=0;
            bool changed=frame.Frames!=before;if(changed)surface.Invalidate();
            ArrangeMiniMap();
            // A full-window pixel-edge paint can exceed the frame interval.
            // WM_PAINT then keeps servicing the parent while the child waits.
            // Complete the small child paint at its own 10 Hz refresh rate;
            // never force an additional game repaint.
            if(changed)miniMap.PresentFrame();
            overview.RefreshScene(activeSession);
            if(frame.ReadStatus!=null)status.Text=frame.ReadStatus+" · reconnecting automatically";
            double sampleSeconds=(DateTime.UtcNow-rateSample).TotalSeconds;
            if(sampleSeconds>=2){measuredFps=(frame.Frames-rateFrames)/sampleSeconds;rateFrames=frame.Frames;rateSample=DateTime.UtcNow;}
            if(wasWorld!=frame.InWorld){if(frame.InWorld)managingCharacters=false;else RecordSessionOffline(activeSession);ArrangeHost();}else if(wasCompact!=frame.Compact || wasMap!=frame.TravelMap)ArrangeDock();
            if(!wasWorld && frame.InWorld){macros.RefreshAbilities();BindCharacter(activeSession);}
            if((DateTime.UtcNow-lastToolsPoll).TotalMilliseconds>=150)
            {
                lastToolsPoll=DateTime.UtcNow;
                stopMacro.Visible=macros.Running||hotkeys.Running;
                try
                {
                    gameTools.PollMap();surface.MapOverlayVisible=gameTools.MapVisible;
                    mapOptions.State.Text=surface.WorldZoomPercent>100&&!frame.HasFrameSequence?"Play zoom needs a revision-10 game tab.":
                        gameTools.MapVisible?"Tab map open · scale "+gameTools.MapScale:"Play zoom "+surface.DisplayedWorldZoom+"% · Tab opens the map.";
                }
                catch(Exception ex){mapOptions.State.Text=ex.Message;surface.MapOverlayVisible=true;}
            }
            stopMacro.Visible=macros.Running||hotkeys.Running;
            if((DateTime.UtcNow-lastReport).TotalSeconds>=3)
            {
                foreach(var session in sessions)BindCharacter(session);
                WriteHostState();lastReport=DateTime.UtcNow;
                Rectangle currentView=surface.View;
                if(!frame.InWorld && frame.ReadStatus==null && !busy && DateTime.UtcNow>=statusHoldUntil)status.Text="Game "+activeSession.Id+" · "+frame.Width+" × "+frame.Height+" · "+measuredFps.ToString("0")+" fps · Sign in inside this tab";
                if(frame.InWorld && frame.ReadStatus==null && !busy && DateTime.UtcNow>=statusHoldUntil) status.Text=frame.Width+" × "+frame.Height+" world canvas · "+(frame.Compact?"Compact UI":"Full UI")+" · "+display.Text+" · "+filtering.Text+
                    (surface.Filter==FrameFilter.IntegerPixels && currentView.Width<frame.Width?" (smooth fit below 1×)":"")+" · "+(guardRedraws.Checked?"Guarded":"Raw")+" · "+measuredFps.ToString("0")+" fps · Click game for keyboard focus";
            }
        }
        catch(Exception ex)
        {
            missedFrames++;status.Text="Waiting for game: "+ex.Message;
        }
    }
    void BindCharacter(HostedSession session)
    {
        try
        {
            session.CharacterName=session.Tools.CharacterName();session.Hotkeys.Enabled=!String.IsNullOrEmpty(session.CharacterName);
            if(session.Hotkeys.Enabled)
            {
                session.Hotkeys.BindCharacter(session.CharacterName,session.Server.Key);
                if(!session.WorldObserved&&session.Frame.IsInWorldNow)
                {session.WorldObserved=true;if(vault.RecordActivity(session.CharacterName,session.Server.Key,DateTime.UtcNow))characters.RefreshEntries();}
            }
        }
        catch{session.Hotkeys.Enabled=false;}
    }
    void RecordSessionOffline(HostedSession session)
    {
        if(session==null||!session.WorldObserved)return;
        session.WorldObserved=false;
        if(vault.RecordActivity(session.CharacterName,session.Server.Key,DateTime.UtcNow))characters.RefreshEntries();
    }
    void ArrangeHost()
    {
        int quickHeight=36;
        tabsFrame.SetBounds(0,0,ClientSize.Width,quickHeight);
        stage.SetBounds(0,quickHeight,ClientSize.Width,Math.Max(1,ClientSize.Height-quickHeight));
        int w=stage.ClientSize.Width,h=stage.ClientSize.Height;
        bool characterView=!libraryOpen&&CharacterPanelVisible(frame!=null,frame!=null&&frame.InWorld,managingCharacters,activeSession!=null&&activeSession.CharactersDismissed);
        bool side=display.SelectedIndex==0||characterView;
        int sideWidth=side?Math.Min(620,Math.Max(characterView?480:360,w/3)):0;
        int surfaceWidth=Math.Max(1,w-sideWidth-(side?8:0));
        int controlsWidth=frame==null?w:surfaceWidth;
        int controlsHeight=Math.Max(44,toolbar.GetPreferredSize(new Size(Math.Max(1,controlsWidth-10),0)).Height)+status.Height+10;
        if(characters!=null)
        {
            characters.CanDismiss=frame!=null;
            characters.Visible=characterView;
            if(frame==null)characters.Bounds=CharacterGalleryBounds(new Size(w,h),controlsHeight);
            else characters.SetBounds(Math.Max(0,w-sideWidth),0,Math.Min(w,sideWidth),h);
            characters.BringToFront();
        }
        surface.Visible=frame!=null&&!libraryOpen;
        surface.SetBounds(0,0,surfaceWidth,Math.Max(1,h));
        libraryPanel.SetBounds(0,0,w,h);libraryPanel.Visible=libraryOpen;
        surface.Stretch=display.SelectedIndex==2;surface.Actual=display.SelectedIndex==3;
        surface.Filter=(FrameFilter)Math.Max(0,filtering.SelectedIndex);
        surface.Arrange();ArrangeDock();
        controlsPanel.SetBounds(0,quickHeight,controlsWidth,controlsHeight);
        controlsHint.SetBounds(Math.Max(0,(controlsWidth-94)/2),quickHeight,94,11);
        overview.Bounds=MapOverview.BoundsFor(new Size(w,h),overview.Full);
        if(overview.Visible)overview.BringToFront();
        if(libraryOpen)libraryPanel.BringToFront();
        if(overview.Visible||libraryOpen){controlsPanel.Visible=false;controlsHint.Visible=false;}
        else UpdateControlsVisibility();
        controlsPanel.BringToFront();controlsHint.BringToFront();tabsFrame.BringToFront();
    }
    internal static bool CharacterPanelVisible(bool attached,bool inWorld,bool opened,bool dismissed)
    {return !attached||opened||(!inWorld&&!dismissed);}
    internal static Rectangle CharacterGalleryBounds(Size stageSize,int controlsHeight)
    {
        int w=Math.Max(1,stageSize.Width),h=Math.Max(1,stageSize.Height);
        int width=Math.Min(w,Math.Min(1120,Math.Max(320,w-40)));
        int top=Math.Min(Math.Max(0,h-1),Math.Max(12,controlsHeight+12));
        return new Rectangle(Math.Max(0,(w-width)/2),top,width,Math.Max(1,h-top-12));
    }
    void SetCharacterPanel(bool show)
    {
        if(show&&libraryOpen){libraryOpen=false;RefreshGameTabs();}
        managingCharacters=show;
        if(activeSession!=null)activeSession.CharactersDismissed=!show;
        ArrangeHost();if(!show&&frame!=null)surface.Focus();
    }
    void SetLibraryOpen(bool show)
    {
        if(libraryOpen==show)return;
        libraryOpen=show;if(show){overview.Visible=false;managingCharacters=false;}
        RefreshGameTabs();ArrangeHost();if(!show&&frame!=null)surface.Focus();
        WriteHostState();
    }
    void ArrangeMiniMap()
    {
        miniMap.Frame=frame;miniMap.GamePath=activeSession==null?null:activeSession.GamePath;miniMap.Standard=mapOptions.MiniMapStyle.SelectedIndex==1;
        miniMap.Visible=mapOptions.MiniMap.Checked&&frame!=null&&frame.InWorld&&frame.TravelMap.IsEmpty;
        Rectangle view=surface.View;
        int width=Math.Min(244,Math.Max(160,view.Width/3));
        miniMap.SetBounds(Math.Max(view.Left+4,view.Right-width-34),view.Top,width,miniMap.Collapsed?25:164);UpdateGains();
    }
    void ArrangeDock()
    {
        ArrangeMiniMap();
        if(searchPanel==null)return;
        bool side=display.SelectedIndex==0;
        expand.Text=side?"Shrink":"Expand";
        searchPanel.Visible=!libraryOpen&&frame!=null&&frame.InWorld&&!managingCharacters&&(side || (searchToggle.Checked && frame.TravelMap.IsEmpty));
        if(!searchPanel.Visible)return;
        Rectangle rect;
        if(side){int available=stage.ClientSize.Width;
            int width=Math.Min(620,Math.Max(360,available/3));rect=new Rectangle(available-width,0,width,stage.ClientSize.Height);}
        else {rect=ViewGeometry.Dock(surface.View,new Size(frame.Width,frame.Height),frame.Compact);rect.Offset(surface.Location);}
        searchPanel.Bounds=rect;searchPanel.BringToFront();if(overview.Visible)overview.BringToFront();
        // Companion text stays at the Macro panel's physical pixel size,
        // independent of the game canvas scale or expanded/corner layout.
        if(search.CoreWebView2!=null && Math.Abs(search.ZoomFactor-1)>.01)search.ZoomFactor=1;
    }
    void DetachGame()
    {
        if(activeSession!=null)RemoveSession(activeSession,true);
        if(!closing)status.Text="Selected game detached. Other tabs stay hosted.";
        WriteHostState();
    }
    void WriteHostState()
    {
        Rectangle view=surface.View;
        var report=new {
            status=frame==null?"waiting_for_game":"hosting",
            session_count=sessions.Count,active_session=activeSession==null?0:activeSession.Id,
            sessions=sessions.ConvertAll(s=>new {id=s.Id,process=s.Frame.ProcessId,profile=s.Profile,active=s==activeSession,macro_running=s.Macros.Running}),
            quick_login_status=activeSession==null||activeSession.Login==null?null:activeSession.Login.Status,
            frame_reader=frame==null?null:frame.ReadStatus,
            viewport=frame==null?null:new[]{frame.Width,frame.Height},
            world=frame!=null&&frame.InWorld,compact=frame!=null&&frame.Compact,
            native_frame_sequence=frame!=null&&frame.HasFrameSequence,
            travel_map_visible=frame!=null&&!frame.TravelMap.IsEmpty,
            display=display.Text,
            filter=filtering.Text,
            controls_visible=controlsPanel.Visible,
            tabs_visible=gameTabs.Visible,tabs_rectangle=new[]{gameTabs.Left,gameTabs.Top,gameTabs.Width,gameTabs.Height},
            controls_rectangle=new[]{controlsPanel.Left,controlsPanel.Top,controlsPanel.Width,controlsPanel.Height},
            companion_tab=selectedTab==5?"skill-list":new[]{"search","macros","map","hotkeys"}[selectedTab],expanded=display.SelectedIndex==0,
            hotkey_running=hotkeys.Running,
            learned_ability_count=macros.AbilityCount,macro_running=macros.Running,
            tab_map_scale=gameTools==null?0:gameTools.MapScale,custom_tab_scale=(int)(mapOptions.CustomZoom.Checked?mapOptions.ZoomScale.Value:0),fog_dim=surface.DimFog,
            tab_map_refreshes=gameTools==null?0:gameTools.MapRefreshCount,macro_status=macros.State,macro_tick_count=macroTicks,
            skin="Original DA HUD textures and da.lft bitmap glyphs",
            frame_count=frame==null?0:frame.Frames,
            skipped_drawing_frames=frame==null?0:frame.SkippedFrames,fog_opacity=surface.FogOpacity,game_cursor=surface.UseGameCursor,
            cursor_recoveries=HostPointer.Recoveries,
            screenshot_mode="F12 saves the displayed game view locally without forwarding the key to the parked client",
            play_zoom_percent=surface.WorldZoomPercent,displayed_play_zoom_percent=surface.DisplayedWorldZoom,world_underlay_ready=frame!=null&&frame.WorldReady,
            captured_frames=frame==null?0:frame.CapturedFrames,max_capture_gap_ms=frame==null?0:frame.MaxCaptureGapMilliseconds,
            guarded_redraws=guardRedraws.Checked,target_fps=new[]{60,30,20}[Math.Max(0,frameRate.SelectedIndex)],measured_fps=Math.Round(measuredFps,1),
            minimap_painted_frames=miniMap.PaintedFrames,minimap_visible=miniMap.Visible,minimap_collapsed=miniMap.Collapsed,minimap_style=miniMap.Standard?"standard":"live",minimap_zoom_percent=miniMap.ZoomPercent,minimap_markers=frame==null?0:frame.Scene.Markers.Length,tile_grid=mapOptions.Grid.Checked,tab_transparency=surface.TabTransparency,map_scene_ready=frame!=null&&frame.Scene.Ready,library_open=libraryOpen,
            area_map_visible=overview.Visible,area_map_size=overview.Full?"full":"basic",characters_visible=characters.Visible,
            fog_shape=surface.FogSquare?"square":"diamond",
            fog_fade=surface.FogFade,fog_color="Black",fog_layers_ready=frame!=null&&frame.LayersReady,
            fog_layer_retries=frame==null?0:frame.LayerMisses,fog_layer_last_retry=frame==null?null:frame.LayerFailure,
            game_process=frame==null?0:frame.ProcessId,
            mouse_down_count=surface.MouseDownCount,mouse_up_count=surface.MouseUpCount,capture_loss_count=surface.CaptureLossCount,
            presentation="Read-only frame bridge; native input; optional bounded Tab-map scale setting",
            rendered_rectangle=new[]{view.X,view.Y,view.Width,view.Height},
            search_rectangle=new[]{searchPanel.Left,searchPanel.Top,searchPanel.Width,searchPanel.Height},
            search_visible=searchPanel.Visible,library_visible=libraryPanel.Visible,
            library_ready=library.CoreWebView2!=null,search_ready=search.CoreWebView2!=null,
            privacy="No frames, input logs, passwords, packets or entities in diagnostics. Passwords are never saved or entered by Foam DA; character names and preferences stay local."
        };
        File.WriteAllText(Path.Combine(Program.Root,"build",previewOnly?"host-preview-state.json":"host-state.json"),Program.Json.Serialize(report));
    }
    void ShowControls(){lastControlsHover=DateTime.UtcNow;controlsPanel.Visible=true;controlsPanel.BringToFront();controlsHint.Visible=false;}
    void UpdateControlsVisibility()
    {
        if(overview.Visible||libraryOpen){controlsPanel.Visible=false;controlsHint.Visible=false;return;}
        // This is app behavior driven by the real pointer, not an input hook.
        Point pointer=PointToClient(Cursor.Position);
        bool reveal=controlsHint.Bounds.Contains(pointer);
        bool over=controlsPanel.Visible&&controlsPanel.Bounds.Contains(pointer);
        bool menu=viewport.DroppedDown||display.DroppedDown||filtering.DroppedDown||frameRate.DroppedDown||serverChoice.DroppedDown;
        if(frame==null||busy||reveal||over||menu||DateTime.UtcNow<statusHoldUntil) {ShowControls();return;}
        if((DateTime.UtcNow-lastControlsHover).TotalMilliseconds<700)return;
        controlsPanel.Visible=false;controlsHint.Visible=true;controlsHint.BringToFront();
    }
    static bool IsSkillList(Uri location){return location!=null&&location.AbsolutePath.TrimEnd('/')=="/my-skill-list";}
    void SelectTab(int tab,bool navigate=true)
    {
        selectedTab=tab;search.Visible=tab==0||tab==5;macros.Visible=tab==1;mapOptions.Visible=tab==2;hotkeys.Visible=tab==3;
        if(navigate&&libraryBase!=null&&search.CoreWebView2!=null){
            if(tab==5&&!IsSkillList(search.Source))search.Source=new Uri(libraryBase+"/my-skill-list/");
            else if(tab==0&&IsSkillList(search.Source))search.Source=lastReference??lastSearch??new Uri(libraryBase+"/search/");
        }
        foreach(DATabButton button in tabRail.Controls){button.Selected=(int)button.Tag==tab;button.Invalidate();}
        if(tab==1 && macros.AbilityCount==0 && gameTools!=null)macros.RefreshAbilities();
    }
    internal static int VerifyCompanionChrome()
    {
        using(var host=new GameHostForm(false,false,true)){
            host.ShowInTaskbar=false;host.StartPosition=FormStartPosition.Manual;host.Location=new Point(-20000,-20000);host.Show();
            host.chromeTimer.Stop();host.macroTimer.Stop();host.frames.Stop();
            host.searchPanel.SetBounds(0,0,479,180);host.searchPanel.Visible=true;host.searchPanel.BringToFront();Application.DoEvents();
            var buttons=host.tabRail.Controls.Cast<DATabButton>().ToArray();
            var list=buttons.Single(b=>b.Icon==5);var settings=buttons.Last();var hotkey=buttons.Single(b=>b.Icon==3);
            if(Array.IndexOf(buttons,list)!=Array.IndexOf(buttons,hotkey)+1||list.AccessibleName!="My skill list"||settings.Icon!=2||settings.AccessibleName!="Settings")throw new Exception("Skill list or settings is in the wrong toolbar position.");
            if(buttons.Any(b=>!host.tabRail.ClientRectangle.Contains(b.Bounds)))throw new Exception("A companion shortcut is clipped in the native game corner.");
            list.PerformClick();
            if(host.selectedTab!=5||!list.Selected||!host.search.Visible||host.macros.Visible||host.mapOptions.Visible||host.hotkeys.Visible)throw new Exception("The skill list shortcut opened the wrong companion panel.");
            settings.PerformClick();
            if(host.selectedTab!=2||!settings.Selected||!host.mapOptions.Visible||host.search.Visible)throw new Exception("The last cog shortcut did not open settings.");
            host.SelectTab(3);
            if(!hotkey.Selected||!host.hotkeys.Visible)throw new Exception("Reordering the toolbar changed the hotkey destination.");
            using(var image=new Bitmap(host.tabRail.Width,host.tabRail.Height)){host.tabRail.DrawToBitmap(image,host.tabRail.ClientRectangle);image.Save(Path.Combine(Program.Root,"build","companion-shortcuts.png"));}
            host.Close();return 5;
        }
    }
    void ApplyMapOptions()
    {
        foreach(var session in sessions)session.Tools.DesiredMapScale=mapOptions.CustomZoom.Checked?(int)mapOptions.ZoomScale.Value:0;
        surface.DimFog=mapOptions.Fog.Checked;surface.FogRange=(int)mapOptions.Range.Value;surface.FogSquare=mapOptions.Shape.SelectedIndex==1;surface.FogOpacity=(int)mapOptions.Opacity.Value;surface.FogFade=mapOptions.Fade.Checked;if(frame!=null)frame.FogLayersRequested=surface.DimFog&&surface.FogOpacity>0;surface.Invalidate();
        surface.TabTransparency=mapOptions.TabTransparency.Value;
        if(frame!=null){frame.ExtraWorldRequested=overview.Visible||(mapOptions.MiniMap.Checked&&!miniMap.Collapsed)||mapOptions.Grid.Checked||mapOptions.TabTransparency.Value>0;frame.StandardMiniMapRequested=overview.Visible||(mapOptions.MiniMap.Checked&&!miniMap.Collapsed&&mapOptions.MiniMapStyle.SelectedIndex==1);frame.TileGridEnabled=mapOptions.Grid.Checked;frame.WorldUnderlayRequested=mapOptions.TabTransparency.Value>0;}
        ArrangeMiniMap();
        surface.WorldZoomPercent=mapOptions.PlayZoom.Value;surface.LockZoom=mapOptions.LockZoom.Checked;
        foreach(var session in sessions){session.Frame.GuardRedraws=guardRedraws.Checked;session.Frame.WorldZoomRequested=surface.WorldZoomPercent>100;session.Frame.FogLayersRequested=surface.DimFog&&surface.FogOpacity>0;}
        UpdateGains();SavePreferences();
    }
    void SavePreferences()
    {
        if(!preferencesReady||previewOnly)return;
        preferences.Capture(mapOptions);preferences.Display=display.SelectedIndex;preferences.Filter=filtering.SelectedIndex;preferences.Fps=frameRate.SelectedIndex;preferences.Viewport=viewport.SelectedIndex;preferences.Guard=guardRedraws.Checked;preferences.OverviewFull=overview.Full;preferences.MiniMapZoom=miniMap.ZoomPercent;
        settingsDirty=true;settingsDue=DateTime.UtcNow.AddMilliseconds(450);
    }
    void FlushPreferences()
    {
        if(!settingsDirty)return;settingsDirty=false;
        try{preferences.Save();}catch{status.Text="Display settings could not be saved locally.";}
    }
    void UpdateGains()
    {
        gains.Visible=activeSession!=null&&frame!=null&&frame.InWorld&&frame.TravelMap.IsEmpty&&(mapOptions.ExperienceRate.Checked||mapOptions.AbilityRate.Checked);
        if(!gains.Visible)return;
        gains.Text=activeSession.Gains.Text(mapOptions.ExperienceRate.Checked,mapOptions.AbilityRate.Checked);
        var view=surface.View;int h=mapOptions.ExperienceRate.Checked&&mapOptions.AbilityRate.Checked?44:28;
        int hudTop=view.Top+(int)((frame.Height-(frame.Compact?56:168))*view.Height/(double)frame.Height);
        gains.SetBounds(view.Left+5,Math.Max(view.Top,hudTop-h-3),185,h);gains.Invalidate();
    }
    async void SaveHostedScreenshot(object sender,EventArgs e)
    {
        Bitmap image=null;
        try
        {
            int process=frame==null?0:frame.ProcessId;
            image=surface.CaptureScreenshot();
            string folder=CaptureStorage.Screenshots;
            string name="DA-"+DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff")+"-"+process+".png";
            string path=Path.Combine(folder,name);
            Bitmap captured=image;
            await Task.Run(()=>{CaptureStorage.Ensure("screenshots");captured.Save(path,System.Drawing.Imaging.ImageFormat.Png);});
            if(!IsDisposed){status.Text="Screenshot saved · "+path;statusHoldUntil=DateTime.UtcNow.AddSeconds(12);ShowControls();}
        }
        catch
        {
            if(!IsDisposed){status.Text="Screenshot could not be saved. Check the local Foam DA screenshots folder.";statusHoldUntil=DateTime.UtcNow.AddSeconds(12);ShowControls();}
        }
        finally{if(image!=null)image.Dispose();}
    }
    void ShowSettings(Control anchor)
    {
        var menu=new ContextMenuStrip{BackColor=DATheme.Dark,ForeColor=DATheme.Gold};
        menu.Items.Add("Display controls (shared)",null,delegate{ShowControls();});
        menu.Items.Add("Map settings (shared)",null,delegate{managingCharacters=false;SelectTab(2);ArrangeHost();if(frame==null||!frame.InWorld)ShowMapSettings();});
        menu.Items.Add("My characters",null,delegate{SetCharacterPanel(true);});
        var update=menu.Items.Add("Install update...",null,delegate{
            try{string root=Program.InstallRoot;Process.Start(new ProcessStartInfo(Path.Combine(root,"DA-Library.exe"),"--update"){WorkingDirectory=root,UseShellExecute=true});}
            catch{status.Text="Could not open updates. Use DA-Library.exe --update in the installation folder.";ShowControls();}
        });update.Enabled=Program.InstallRoot!=null;
        menu.Items.Add("Open screenshots folder",null,delegate{try{Process.Start(new ProcessStartInfo(CaptureStorage.Ensure("screenshots")){UseShellExecute=true});}catch{status.Text="Could not open the screenshots folder beside the DA launcher.";ShowControls();}});
        menu.Items.Add("Last skulled (all characters)",null,delegate{SkullHistory.Show(this);});
        menu.Closed+=delegate{BeginInvoke(new Action(menu.Dispose));};menu.Show(anchor,new Point(0,anchor.Height));
    }
    void ShowMapSettings()
    {
        using(var form=new Form{Text="Map settings · shared by every character",ClientSize=new Size(490,145),StartPosition=FormStartPosition.CenterParent,BackColor=DATheme.Dark,MinimizeBox=false,MaximizeBox=false,FormBorderStyle=FormBorderStyle.FixedDialog})
        {
            companionBody.Controls.Remove(mapOptions);form.Controls.Add(mapOptions);mapOptions.Visible=true;
            form.ShowDialog(this);form.Controls.Remove(mapOptions);companionBody.Controls.Add(mapOptions);SelectTab(selectedTab);
        }
    }
    void ShowLaunchMenu()
    {
        var menu=new ContextMenuStrip{BackColor=DATheme.Dark,ForeColor=DATheme.Gold};
        foreach(var item in vault.Characters){var entry=item;menu.Items.Add(entry.ToString(),null,async delegate{await StartGame(false,entry);});}
        menu.Items.Add("New character · manual login",null,async delegate{await StartGame();});
        menu.Items.Add("Add saved character…",null,delegate{SetCharacterPanel(true);characters.NewCharacter();});
        menu.Closed+=delegate{BeginInvoke(new Action(menu.Dispose));};menu.Show(play,new Point(play.Width,0));
    }

}
