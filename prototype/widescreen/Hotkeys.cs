using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

internal sealed class HotkeyStep
{
    internal Keys Key;
    internal int Pause;
}
internal sealed class HotkeyBinding
{
    public string Shortcut {get;set;}
    public string Sequence {get;set;}
    public override string ToString(){return Shortcut+" → "+Sequence;}
}
internal sealed class HotkeySettings
{
    public bool Enabled {get;set;}
    public List<HotkeyBinding> Bindings {get;set;}
}
internal static class HotkeySyntax
{
    internal static Keys Shortcut(string text)
    {
        Keys modifiers=Keys.None,key=Keys.None;
        foreach(string token in Regex.Split((text??"").Trim(),@"[+\s]+"))
        {
            switch(token.ToLowerInvariant())
            {
                case "shift":modifiers|=Keys.Shift;break;
                case "alt":modifiers|=Keys.Alt;break;
                case "ctrl":case "control":modifiers|=Keys.Control;break;
                default:
                    if(key!=Keys.None)throw new InvalidOperationException("Use one shortcut key with Shift, Alt or Ctrl.");
                    Keys[] parsed=SimpleMacroRunner.Parse(token);
                    if(parsed.Length!=1)throw new InvalidOperationException("Choose a shortcut key.");key=parsed[0];break;
            }
        }
        if(modifiers==Keys.None || key==Keys.None)throw new InvalidOperationException("Include Shift, Alt or Ctrl in the shortcut.");
        if(key==Keys.Tab || key==Keys.Escape || key==Keys.Enter || (key==Keys.F4 && (modifiers&Keys.Alt)!=0))
            throw new InvalidOperationException("Tab, Escape, Enter and Alt+F4 are reserved.");
        return modifiers|key;
    }
    internal static string Caption(Keys keys)
    {
        string prefix=((keys&Keys.Control)!=0?"Ctrl+":"")+((keys&Keys.Alt)!=0?"Alt+":"")+((keys&Keys.Shift)!=0?"Shift+":"");
        Keys key=keys&Keys.KeyCode;
        string name=key>=Keys.D0&&key<=Keys.D9?((int)(key-Keys.D0)).ToString():key==Keys.OemQuestion?"/":key==Keys.OemMinus?"-":key==Keys.Oemplus?"=":key.ToString();
        return prefix+name;
    }
    internal static HotkeyStep[] Parse(string text)
    {
        if(String.IsNullOrWhiteSpace(text) || text.Length>1024)throw new InvalidOperationException("Enter a sequence (up to 1024 characters).");
        var result=new List<HotkeyStep>();int offset=0,duration=0;
        while(offset<text.Length)
        {
            if(Char.IsWhiteSpace(text[offset]) || text[offset]==','){offset++;continue;}
            if(text[offset]=='(')
            {
                var pause=Regex.Match(text.Substring(offset),@"^\(pause\s*:\s*(\d+)\)",RegexOptions.IgnoreCase);
                int ms;
                if(!pause.Success || !Int32.TryParse(pause.Groups[1].Value,out ms) || ms>30000)
                    throw new InvalidOperationException("Use (pause:500), with 0–30000 milliseconds.");
                result.Add(new HotkeyStep{Pause=ms});duration+=ms;offset+=pause.Length;
            }
            else
            {
                int end=offset;while(end<text.Length && !Char.IsWhiteSpace(text[end]) && text[end]!=',' && text[end]!='(')end++;
                string token=text.Substring(offset,end-offset);Keys[] keys;
                if(Regex.IsMatch(token,@"^(space|tab|enter|return|esc|escape|slash|F([1-9]|1[0-2]))$",RegexOptions.IgnoreCase))keys=SimpleMacroRunner.Parse(token);
                else
                {
                    var expanded=new List<Keys>();
                    foreach(char c in token)expanded.AddRange(SimpleMacroRunner.Parse(c.ToString()));keys=expanded.ToArray();
                }
                foreach(Keys key in keys){result.Add(new HotkeyStep{Key=key});duration+=100;}
                offset=end;
            }
            if(result.Count>128 || duration>60000)throw new InvalidOperationException("Use at most 128 steps and 60 seconds per combo.");
        }
        if(!result.Exists(s=>s.Key!=Keys.None))throw new InvalidOperationException("Include at least one keypress.");
        return result.ToArray();
    }
}

// A one-shot queue. Every step checks native chat/dialog state; never blocks
// the render thread, sends global input, or keeps a synthesized key held.
internal sealed class HotkeyRunner
{
    internal Func<PlayContext> ReadContext;
    internal Action<Keys> Tap;
    internal bool Running {get;private set;}
    internal string Status="Ready · shortcuts work while the game has focus.";
    HotkeyStep[] steps;
    PlayContext origin;
    int index;
    bool waitingRelease;
    DateTime due,releaseDeadline;
    internal static bool Allowed(PlayContext state){return state!=null && state.Alive && !state.Chat && !state.Dialog && !state.Transfer;}
    internal bool Start(HotkeyStep[] sequence,DateTime now)
    {
        if(Running)return false;
        PlayContext context=ReadContext();if(!Allowed(context))return false;
        origin=new PlayContext{Root=context.Root,Character=context.Character,Map=context.Map};steps=sequence;index=0;waitingRelease=true;due=now;releaseDeadline=now.AddSeconds(3);
        Running=true;Status="Release shortcut keys to run…";return true;
    }
    internal void Stop(string reason){Running=false;Status=reason;}
    internal void Tick(DateTime now,bool focused,bool shortcutHeld)
    {
        if(!Running)return;
        try
        {
            if(!focused){Stop("Stopped · game focus changed.");return;}
            PlayContext context=ReadContext();
            if(!Allowed(context) || context.Root!=origin.Root || context.Character!=origin.Character || context.Map!=origin.Map)
            {Stop("Stopped · chat, dialog or game state changed.");return;}
            if(waitingRelease)
            {
                if(shortcutHeld){if(now>=releaseDeadline)Stop("Stopped · shortcut held too long.");return;}
                waitingRelease=false;due=now;
            }
            if(now<due)return;
            if(index==steps.Length){Stop("Finished.");return;}
            HotkeyStep step=steps[index++];
            if(step.Key!=Keys.None){Tap(step.Key);due=now.AddMilliseconds(100);Status="Running · key "+step.Key;}
            else {due=now.AddMilliseconds(step.Pause);Status="Pause · "+step.Pause+" ms";}
        }
        catch(Exception ex){Stop("Stopped · "+ex.Message);}
    }
}

internal sealed class HotkeyPanel : Panel
{
    readonly DACheckBox enabled=new DACheckBox{Text="Enable hotkeys",AutoSize=true};
    readonly ListBox list=new ListBox();
    readonly TextBox shortcut=new TextBox(),sequence=new TextBox();
    readonly Label note=new Label();
    readonly HotkeyRunner runner=new HotkeyRunner();
    readonly List<HotkeyBinding> bindings=new List<HotkeyBinding>();
    string settingsPath;
    bool loadingSettings;
    string characterKey;
    Keys captured;
    internal GameTools Tools;
    internal bool Running {get{return runner.Running;}}
    internal HotkeyPanel(string path=null)
    {
        settingsPath=path??Path.Combine(Program.Root,"build","hotkeys.json");
        BackColor=DATheme.Dark;ForeColor=DATheme.Gold;Font=DATheme.Font(DATheme.CompanionFontPixels);AutoScroll=true;
        var body=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Padding=new Padding(6),Margin=Padding.Empty};
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var top=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Top,Margin=Padding.Empty};top.Controls.Add(enabled);
        var stop=new DAButton{Text="Stop",Width=55,Height=25};stop.Click+=delegate{Stop("Stopped.");};top.Controls.Add(stop);body.Controls.Add(top);
        list.Height=52;list.Dock=DockStyle.Top;list.IntegralHeight=false;list.BackColor=DATheme.Dark;list.ForeColor=DATheme.Gold;list.AccessibleName="Saved hotkeys";body.Controls.Add(list);
        list.DrawMode=DrawMode.OwnerDrawFixed;list.ItemHeight=18;
        list.DrawItem+=delegate(object sender,DrawItemEventArgs e){
            if(e.Index<0)return;bool selected=(e.State&DrawItemState.Selected)!=0;
            using(var fill=new SolidBrush(selected?Color.FromArgb(105,67,34):DATheme.Dark))e.Graphics.FillRectangle(fill,e.Bounds);
            TextRenderer.DrawText(e.Graphics,list.Items[e.Index].ToString(),list.Font,e.Bounds,DATheme.Gold,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
            if(selected)using(var pen=new Pen(DATheme.Gold))e.Graphics.DrawRectangle(pen,e.Bounds.X,e.Bounds.Y,e.Bounds.Width-1,e.Bounds.Height-1);
        };
        var row=new TableLayoutPanel{AutoSize=true,Dock=DockStyle.Top,ColumnCount=2,Margin=Padding.Empty};
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,130));row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        shortcut.ReadOnly=true;shortcut.AccessibleName="Hotkey shortcut";shortcut.Text="Press shortcut";
        sequence.AccessibleName="Hotkey sequence";sequence.MaxLength=1024;
        foreach(var box in new[]{shortcut,sequence}){box.BackColor=DATheme.Dark;box.ForeColor=DATheme.Gold;box.Dock=DockStyle.Fill;}
        row.Controls.Add(shortcut,0,0);row.Controls.Add(sequence,1,0);body.Controls.Add(row);
        shortcut.KeyDown+=delegate(object sender,KeyEventArgs e){
            e.Handled=e.SuppressKeyPress=true;
            if(e.KeyCode==Keys.ShiftKey || e.KeyCode==Keys.ControlKey || e.KeyCode==Keys.Menu)return;
            try{captured=HotkeySyntax.Shortcut(HotkeySyntax.Caption(e.KeyData));shortcut.Text=HotkeySyntax.Caption(captured);note.Text="Enter the combo, then Save.";}
            catch(Exception ex){note.Text=ex.Message;}
        };
        list.SelectedIndexChanged+=delegate {
            var binding=list.SelectedItem as HotkeyBinding;if(binding==null)return;
            captured=HotkeySyntax.Shortcut(binding.Shortcut);shortcut.Text=binding.Shortcut;sequence.Text=binding.Sequence;
        };
        var actions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Top,Margin=Padding.Empty};
        var save=new DAButton{Text="Save",Width=55,Height=25};var fresh=new DAButton{Text="New",Width=55,Height=25};var remove=new DAButton{Text="Remove",Width=65,Height=25};
        actions.Controls.Add(save);actions.Controls.Add(fresh);actions.Controls.Add(remove);body.Controls.Add(actions);
        fresh.Click+=delegate{list.SelectedIndex=-1;captured=Keys.None;shortcut.Text="Press shortcut";sequence.Clear();shortcut.Focus();};
        save.Click+=delegate {
            try
            {
                Keys key=HotkeySyntax.Shortcut(HotkeySyntax.Caption(captured));HotkeySyntax.Parse(sequence.Text);
                int selected=list.SelectedIndex;
                int duplicate=bindings.FindIndex(b=>HotkeySyntax.Shortcut(b.Shortcut)==key);
                if(duplicate>=0 && duplicate!=selected)throw new InvalidOperationException("That shortcut is already saved. Select it to edit.");
                if(selected<0 && bindings.Count>=24)throw new InvalidOperationException("Use at most 24 hotkeys.");
                var binding=new HotkeyBinding{Shortcut=HotkeySyntax.Caption(key),Sequence=sequence.Text.Trim()};
                if(selected<0){bindings.Add(binding);list.Items.Add(binding);list.SelectedIndex=list.Items.Count-1;}
                else {bindings[selected]=binding;list.Items[selected]=binding;}
                Persist();note.Text="Saved · click the game to use your shortcut.";
            }catch(Exception ex){note.Text=ex.Message;}
        };
        remove.Click+=delegate{int i=list.SelectedIndex;if(i<0)return;Stop("Stopped.");bindings.RemoveAt(i);list.Items.RemoveAt(i);SaveWithStatus();};
        body.Controls.Add(new Label{AutoSize=true,Text="Click shortcut box; press Shift+1, Alt+2, etc.\nExample: s1234 space (pause:500) 5 6\n100 ms between keys · release shortcut to run."});
        note.AutoSize=true;note.MaximumSize=new Size(560,0);body.Controls.Add(note);Controls.Add(body);DATheme.StyleCompanion(this);
        runner.ReadContext=delegate{if(Tools==null)throw new InvalidOperationException("Enter the world first.");return Tools.Context();};
        runner.Tap=delegate(Keys key){Tools.TapKey(key);};
        LoadSettings();enabled.CheckedChanged+=delegate{if(!enabled.Checked)Stop("Hotkeys disabled.");SaveWithStatus();};
    }
    internal void BindCharacter(string name,string server)
    {
        string key=LocalProfile.CharacterKey(name,server);if(key==characterKey)return;
        Stop("Character changed.");characterKey=key;settingsPath=Path.Combine(LocalProfile.Root,"characters",key,"hotkeys.json");
        loadingSettings=true;
        try
        {
            bindings.Clear();list.Items.Clear();captured=Keys.None;shortcut.Text="Press shortcut";sequence.Clear();
            if(File.Exists(settingsPath))LoadSettings();
            else
            {
                // Legacy hotkeys seed a character only once; never inherit a
                // different character's current bindings after a relog.
                string target=settingsPath;settingsPath=Path.Combine(Program.Root,"build","hotkeys.json");LoadSettings();settingsPath=target;
            }
        }finally{loadingSettings=false;}
    }
    void LoadSettings()
    {
        enabled.Checked=true;
        try
        {
            if(File.Exists(settingsPath))
            {
                var saved=Program.Json.Deserialize<HotkeySettings>(File.ReadAllText(settingsPath));
                if(saved==null || saved.Bindings==null || saved.Bindings.Count>24)throw new InvalidOperationException("Invalid hotkey settings.");
                var seen=new HashSet<Keys>();
                foreach(var binding in saved.Bindings){Keys key=HotkeySyntax.Shortcut(binding.Shortcut);HotkeySyntax.Parse(binding.Sequence);if(!seen.Add(key))throw new InvalidOperationException("Duplicate saved shortcut.");}
                bindings.AddRange(saved.Bindings);list.Items.AddRange(bindings.ToArray());enabled.Checked=saved.Enabled;
            }
            note.Text=runner.Status;
        }catch(Exception ex){enabled.Checked=false;note.Text="Could not load hotkeys: "+ex.Message;}
    }
    void Persist()
    {
        if(loadingSettings)return;
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));string temp=settingsPath+".tmp";
        File.WriteAllText(temp,Program.Json.Serialize(new HotkeySettings{Enabled=enabled.Checked,Bindings=bindings}));
        if(File.Exists(settingsPath))File.Replace(temp,settingsPath,settingsPath+".bak");else File.Move(temp,settingsPath);
    }
    void SaveWithStatus(){try{Persist();note.Text="Saved.";}catch(Exception ex){note.Text="Could not save: "+ex.Message;}}
    internal bool Trigger(Keys keys)
    {
        if(!enabled.Checked || Tools==null)return false;
        var binding=bindings.Find(b=>HotkeySyntax.Shortcut(b.Shortcut)==keys);if(binding==null)return false;
        try
        {
            if(!HotkeyRunner.Allowed(Tools.Context()))return false;
            if(runner.Running){note.Text="A combo is already running.";return true;}
            bool accepted=runner.Start(HotkeySyntax.Parse(binding.Sequence),DateTime.UtcNow);note.Text=runner.Status;return accepted;
        }catch(Exception ex){Stop(ex.Message);return true;}
    }
    internal void Tick(bool focused,bool held){if(!runner.Running)return;runner.Tick(DateTime.UtcNow,focused,held);note.Text=runner.Status;}
    internal void Stop(string reason){runner.Stop(reason);note.Text=reason;}
}

internal static class LibraryNavigation
{
    internal static bool IsLocal(string target,string origin)
    {
        Uri uri,home;
        return Uri.TryCreate(target,UriKind.Absolute,out uri) && Uri.TryCreate(origin,UriKind.Absolute,out home) &&
            uri.Scheme==home.Scheme && uri.Host==home.Host && uri.Port==home.Port && String.IsNullOrEmpty(uri.UserInfo);
    }
}

internal sealed class HotkeyInputGate
{
    readonly HashSet<int> consumed=new HashSet<int>();
    internal bool Held {get{return consumed.Count!=0;}}
    internal void Clear(){consumed.Clear();}
    internal bool Handle(int message,int key,Keys modifiers,Func<Keys,bool> trigger,Action release)
    {
        if(message==0x100 || message==0x104)
        {
            if(consumed.Contains(key))return true; // Do not repeat a held trigger.
            if(key==(int)Keys.ShiftKey || key==(int)Keys.ControlKey || key==(int)Keys.Menu)return false;
            if(modifiers!=Keys.None && trigger!=null && trigger((Keys)key|modifiers))
            {release();consumed.Add(key);return true;}
        }
        if(message==0x101 || message==0x105)return consumed.Remove(key);
        if(message==0x102 || message==0x106 || message==0x103 || message==0x107)return Held;
        return false;
    }
}
