using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

internal sealed class TrainingAction
{
    internal LearnedAbility Ability;
    internal bool Self;
    internal double Seconds;
    public override string ToString(){return Ability.Name+" · "+(Self?"self":"activate")+" · "+Seconds+"s";}
}

// One bounded state machine, driven on the UI thread. Tests supply fake reads
// and input sinks, so validation never casts a spell on the live character.
internal sealed class MacroRunner
{
    internal Func<PlayContext> ReadContext;
    internal Func<List<LearnedAbility>> ReadAbilities;
    internal Action<LearnedAbility,int> SelectPanel;
    internal Action<LearnedAbility> Activate;
    internal Action TargetSelf,CancelTarget;
    internal bool Running {get;private set;}
    internal string Status="Stopped · add abilities, then Start.";
    internal int Completed {get;private set;}
    List<TrainingAction> actions;
    PlayContext origin;
    int index,phase;
    DateTime due,deadline;
    internal void Start(IEnumerable<TrainingAction> queue,DateTime now)
    {
        if(Running)throw new InvalidOperationException("Stop the current queue first.");
        var next=new List<TrainingAction>(queue);
        if(next.Count==0)throw new InvalidOperationException("Add at least one learned ability.");
        foreach(var item in next)
        {
            if(item.Ability==null || item.Seconds<1 || item.Seconds>600)throw new InvalidOperationException("Choose a delay between 1 and 600 seconds.");
            if(item.Ability.Spell && (item.Ability.Argument!=2 && item.Ability.Argument!=5))throw new InvalidOperationException("Text-prompt spells are not supported yet.");
            if(item.Ability.Spell && item.Ability.Argument==2 && !item.Self)throw new InvalidOperationException("This spell needs a target. Choose Click self.");
            if(item.Ability.Spell && item.Ability.Argument==5 && item.Self)throw new InvalidOperationException("This spell needs activation only.");
        }
        PlayContext initial=ReadContext();
        if(!initial.CanTrain)throw new InvalidOperationException("Use the full / UI and close game chat/dialogs to start.");
        origin=new PlayContext{Root=initial.Root,Character=initial.Character,Map=initial.Map,X=initial.X,Y=initial.Y};
        actions=next;index=phase=Completed=0;due=now;Running=true;Status="Starting… Use Stop to finish.";
    }
    internal void Stop(string reason)
    {
        bool cancel=Running && phase==2;
        Running=false;phase=0;Status=reason;
        if(cancel && CancelTarget!=null)try {CancelTarget();}catch{}
    }
    internal void Tick(DateTime now,bool hostActive)
    {
        if(!Running)return;
        try
        {
            if(!hostActive){Stop("Stopped · host lost focus.");return;}
            PlayContext current=ReadContext();
            if(!current.Alive || current.Root!=origin.Root || current.Character!=origin.Character || current.Map!=origin.Map)
            {Stop("Stopped · character or map changed.");return;}
            if(!current.CanTrain){phase=0;due=now.AddMilliseconds(100);Status="Waiting · close chat/dialogue and restore full UI to resume.";return;}
            if(now<due)return;
            TrainingAction item=actions[index];
            LearnedAbility ability=ReadAbilities().Find(a=>a.Key==item.Ability.Key);
            if(ability==null){Stop("Stopped · a queued ability moved or disappeared. Refresh it.");return;}
            if(ability.Argument!=item.Ability.Argument){Stop("Stopped · ability target type changed.");return;}
            bool panel=current.Panel==ability.Panel || (current.Panel==10 && ability.Panel==9);
            if(phase==0 && !panel)
            {
                SelectPanel(ability,current.Panel);phase=1;due=now.AddMilliseconds(300);deadline=now.AddSeconds(2);Status="Opening "+(ability.Spell?"spells":"skills")+"…";return;
            }
            if(phase==1 && !panel)
            {
                if(now>=deadline)Stop("Stopped · the requested skill/spell panel did not open.");
                else due=now.AddMilliseconds(100);
                return;
            }
            if(!panel){phase=0;due=now.AddMilliseconds(100);Status="Returning to the selected skill/spell page…";return;}
            if(phase!=2)
            {
                // Reconfirm the context immediately before delivering input.
                PlayContext check=ReadContext();
                if(!check.CanTrain || check.Root!=current.Root || check.Panel!=current.Panel || check.Map!=current.Map)
                {phase=0;due=now.AddMilliseconds(100);Status="Waiting · game controls changed.";return;}
                Activate(ability);
                if(item.Self){phase=2;due=now.AddMilliseconds(300);Status="Targeting self · "+ability.Name;return;}
            }
            else TargetSelf();
            Completed++;Status="Running · "+ability.Name+" · "+Completed+" activations · use Stop to finish";
            index=(index+1)%actions.Count;phase=0;due=now.AddSeconds(item.Seconds);
        }
        catch(Exception ex){Stop("Stopped · "+ex.Message);}
    }
}

internal sealed class MacroPanel : Panel
{
    readonly ComboBox abilities=new ComboBox(),target=new ComboBox();
    readonly NumericUpDown delay=new NumericUpDown();
    readonly ListBox queue=new ListBox();
    readonly Button start=new DAButton(),refresh=new DAButton(),add=new DAButton(),remove=new DAButton();
    readonly Label note=new Label();
    readonly MacroRunner runner=new MacroRunner();
    readonly List<TrainingAction> actions=new List<TrainingAction>();
    readonly SimpleMacroRunner simple=new SimpleMacroRunner();
    readonly ComboBox mode=new ComboBox();
    readonly ComboBox simplePage=new ComboBox();
    readonly TextBox simpleKeys=new TextBox();
    readonly NumericUpDown simpleDelay=new NumericUpDown();
    readonly CheckBox assail=new DACheckBox();
    readonly Button simpleStart=new DAButton();
    readonly Label simpleNote=new Label();
    internal GameTools Tools;
    internal bool Running {get{return runner.Running || simple.Running;}}
    internal int AbilityCount {get{return abilities.Items.Count;}}
    internal string State {get{return mode.SelectedIndex==0?simple.Status:runner.Status;}}
    internal MacroPanel()
    {
        BackColor=DATheme.Dark;ForeColor=DATheme.Gold;Font=DATheme.Font(DATheme.CompanionFontPixels);AutoScroll=true;
        var body=new TableLayoutPanel {Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Padding=new Padding(6),BackColor=DATheme.Dark};
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var row=new TableLayoutPanel {Dock=DockStyle.Top,Height=29,ColumnCount=2,Margin=Padding.Empty};
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,80));
        abilities.Dock=DockStyle.Fill;abilities.DropDownStyle=ComboBoxStyle.DropDownList;abilities.DropDownWidth=390;
        abilities.BackColor=DATheme.Dark;abilities.ForeColor=DATheme.Gold;abilities.FlatStyle=FlatStyle.Flat;
        refresh.Text="Refresh";refresh.Dock=DockStyle.Fill;refresh.Click+=delegate{RefreshAbilities();};
        row.Controls.Add(abilities,0,0);row.Controls.Add(refresh,1,0);body.Controls.Add(row);
        var options=new FlowLayoutPanel {Dock=DockStyle.Top,AutoSize=true,WrapContents=true,Margin=Padding.Empty};
        target.Items.AddRange(new object[]{"Activate only","Click self"});target.DropDownStyle=ComboBoxStyle.DropDownList;target.SelectedIndex=0;target.Width=140;
        target.BackColor=DATheme.Dark;target.ForeColor=DATheme.Gold;target.FlatStyle=FlatStyle.Flat;
        abilities.SelectedIndexChanged+=delegate {var a=abilities.SelectedItem as LearnedAbility;target.SelectedIndex=a!=null && a.Spell && a.Argument==2?1:0;};
        delay.Minimum=1;delay.Maximum=600;delay.Value=10;delay.Width=52;delay.BackColor=DATheme.Dark;delay.ForeColor=DATheme.Gold;
        options.Controls.Add(target);options.Controls.Add(delay);options.Controls.Add(new Label{Text="sec delay",AutoSize=true,Padding=new Padding(0,6,0,0)});
        add.Text="Add";add.Width=48;add.Height=26;add.Click+=delegate {
            var a=abilities.SelectedItem as LearnedAbility;if(a==null)return;
            if(a.Spell && a.Argument!=2 && a.Argument!=5){note.Text="Text-prompt spells are not supported yet.";return;}
            var action=new TrainingAction{Ability=a,Self=target.SelectedIndex==1,Seconds=(double)delay.Value};actions.Add(action);queue.Items.Add(action);
        };options.Controls.Add(add);
        remove.Text="Remove";remove.Width=68;remove.Height=26;remove.Click+=delegate{int i=queue.SelectedIndex;if(i>=0){actions.RemoveAt(i);queue.Items.RemoveAt(i);}};options.Controls.Add(remove);
        body.Controls.Add(options);
        queue.Dock=DockStyle.Top;queue.Height=50;queue.IntegralHeight=false;queue.BackColor=DATheme.Dark;queue.ForeColor=DATheme.Gold;queue.BorderStyle=BorderStyle.FixedSingle;body.Controls.Add(queue);
        var bottom=new FlowLayoutPanel {Dock=DockStyle.Top,AutoSize=true,WrapContents=true,Margin=Padding.Empty};
        start.Text="Start";start.Width=72;start.Height=26;start.Click+=delegate {
            if(runner.Running)Stop("Stopped.");
            else try {if(Tools==null)throw new InvalidOperationException("Enter the world first.");runner.Start(actions,DateTime.UtcNow);}catch(Exception ex){runner.Status=ex.Message;}
            UpdateState();
        };bottom.Controls.Add(start);
        bottom.Controls.Add(new Label {Text="Full UI required · allow for cast time + cooldown",AutoSize=true,Padding=new Padding(0,6,0,0)});body.Controls.Add(bottom);
        note.Text=runner.Status;note.Dock=DockStyle.Top;note.AutoSize=true;note.MaximumSize=new Size(560,0);body.Controls.Add(note);
        var simpleBody=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Padding=new Padding(6),Margin=Padding.Empty};
        simpleBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        simpleKeys.Text="1 2 3 4";simpleKeys.Dock=DockStyle.Top;simpleKeys.BackColor=DATheme.Dark;simpleKeys.ForeColor=DATheme.Gold;simpleKeys.MaxLength=256;simpleKeys.AccessibleName="Simple macro keys";
        var keyRow=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,Margin=Padding.Empty};
        keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,165));keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        simplePage.DropDownStyle=ComboBoxStyle.DropDownList;simplePage.Items.AddRange(new object[]{"Skills","Spells","Medenia skills","Medenia spells","World skills / spells","Current page"});simplePage.SelectedIndex=0;
        simplePage.Dock=DockStyle.Fill;simplePage.DropDownWidth=210;simplePage.BackColor=DATheme.Dark;simplePage.ForeColor=DATheme.Gold;simplePage.FlatStyle=FlatStyle.Flat;simplePage.AccessibleName="Macro skill or spell page";
        keyRow.Controls.Add(simplePage,0,0);keyRow.Controls.Add(simpleKeys,1,0);simpleBody.Controls.Add(keyRow);
        var simpleOptions=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,Margin=Padding.Empty};
        simpleOptions.Controls.Add(new Label{Text="Every",AutoSize=true,Padding=new Padding(0,5,0,0)});
        simpleDelay.Minimum=.1m;simpleDelay.Maximum=3600;simpleDelay.DecimalPlaces=1;simpleDelay.Increment=.5m;simpleDelay.Value=5;simpleDelay.Width=68;simpleDelay.BackColor=DATheme.Dark;simpleDelay.ForeColor=DATheme.Gold;simpleDelay.AccessibleName="Simple macro interval seconds";simpleOptions.Controls.Add(simpleDelay);
        simpleOptions.Controls.Add(new Label{Text="sec",AutoSize=true,Padding=new Padding(0,5,0,0)});
        assail.Text="Assail · Space / 0.5s";assail.AutoSize=true;simpleOptions.Controls.Add(assail);
        simpleStart.Text="Start";simpleStart.Size=new Size(68,26);simpleOptions.Controls.Add(simpleStart);simpleBody.Controls.Add(simpleOptions);
        simpleBody.Controls.Add(new Label{Text="Opens the selected page before pressing keys.\nKeys: 1 2 3 4 Space · Assail runs independently.",AutoSize=true});
        simpleNote.AutoSize=true;simpleNote.Dock=DockStyle.Top;simpleBody.Controls.Add(simpleNote);
        simpleStart.Click+=delegate {
            if(Running)Stop("Stopped.");
            else try {simple.Start(simpleKeys.Text,(double)simpleDelay.Value,assail.Checked,DateTime.UtcNow,new[]{3,1,4,2,9,-1}[simplePage.SelectedIndex]);}catch(Exception ex){simple.Status=ex.Message;}
            UpdateState();
        };
        var layout=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Margin=Padding.Empty};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        mode.DropDownStyle=ComboBoxStyle.DropDownList;mode.Items.AddRange(new object[]{"Simple mode · key presses","Learned abilities · train skills / spells"});mode.Dock=DockStyle.Top;mode.BackColor=DATheme.Dark;mode.ForeColor=DATheme.Gold;mode.FlatStyle=FlatStyle.Flat;
        mode.SelectedIndexChanged+=delegate {simpleBody.Visible=mode.SelectedIndex==0;body.Visible=mode.SelectedIndex==1;UpdateState();};
        layout.Controls.Add(mode);layout.Controls.Add(simpleBody);layout.Controls.Add(body);Controls.Add(layout);mode.SelectedIndex=0;
        runner.ReadContext=delegate {if(Tools==null)throw new InvalidOperationException("The game is detached.");return Tools.Context();};
        runner.ReadAbilities=delegate{return Tools.Abilities();};runner.SelectPanel=delegate(LearnedAbility a,int p){Tools.SelectPanel(a,p);};
        runner.Activate=delegate(LearnedAbility a){Tools.Activate(a);};runner.TargetSelf=delegate{Tools.TargetSelf();};runner.CancelTarget=delegate{if(Tools!=null)Tools.CancelTarget();};
        simple.ReadContext=runner.ReadContext;simple.Tap=delegate(Keys key){Tools.TapKey(key);};simple.SelectPage=delegate(int targetPage,int currentPage){Tools.SelectPage(targetPage,currentPage);};
        DATheme.StyleCompanion(this);
        Resize+=delegate{note.MaximumSize=simpleNote.MaximumSize=new Size(Math.Max(150,ClientSize.Width-30),0);};UpdateState();
    }
    internal void RefreshAbilities()
    {
        if(Running)return;
        try
        {
            if(Tools==null)throw new InvalidOperationException("Enter the world, then Refresh.");
            var found=Tools.Abilities();string selected=abilities.SelectedItem==null?null:((LearnedAbility)abilities.SelectedItem).Key;
            abilities.BeginUpdate();abilities.Items.Clear();
            foreach(var a in found)abilities.Items.Add(a);
            for(int i=0;i<abilities.Items.Count;i++)if(((LearnedAbility)abilities.Items[i]).Key==selected)abilities.SelectedIndex=i;
            if(abilities.SelectedIndex<0 && abilities.Items.Count>0)abilities.SelectedIndex=0;
            abilities.EndUpdate();runner.Status=found.Count+" learned abilities · add a queue and press Start.";
            if(!simple.Running)simple.Status="Stopped · enter keys, then Start.";
        }
        catch(Exception ex){abilities.EndUpdate();runner.Status=ex.Message;}
        UpdateState();
    }
    void UpdateState()
    {
        start.Text=Running?"Stop":"Start";abilities.Enabled=target.Enabled=delay.Enabled=add.Enabled=remove.Enabled=refresh.Enabled=!Running;note.Text=runner.Status;
        simpleStart.Text=Running?"Stop":"Start";mode.Enabled=simplePage.Enabled=simpleKeys.Enabled=simpleDelay.Enabled=assail.Enabled=!Running;simpleNote.Text=simple.Status;
    }
    internal void Tick(bool active){runner.Tick(DateTime.UtcNow,active);simple.Tick(DateTime.UtcNow,active);UpdateState();}
    internal void Stop(string reason){runner.Stop(reason);simple.Stop(reason);UpdateState();}
    internal void Detach(){Stop("Stopped · game detached.");Tools=null;abilities.Items.Clear();actions.Clear();queue.Items.Clear();}
}

internal sealed class MapPanel : Panel
{
    internal readonly CheckBox CustomZoom=new DACheckBox(),Fog=new DACheckBox(),Fade=new DACheckBox(),MiniMap=new DACheckBox(),Grid=new DACheckBox(),LockZoom=new DACheckBox(),ExperienceRate=new DACheckBox(),AbilityRate=new DACheckBox();
    internal readonly NumericUpDown ZoomScale=new NumericUpDown(),Range=new NumericUpDown();
    internal readonly DASlider Opacity=new DASlider(),TabTransparency=new DASlider();
    internal readonly DASlider PlayZoom=new DASlider();
    internal readonly ComboBox Shape=new ComboBox(),MiniMapStyle=new ComboBox();
    internal readonly Label State=new Label();
    internal readonly Button LastSkulled=new DAButton{Text="Last skulled",AccessibleName="Last skulled for all characters"};
    internal event EventHandler OptionsChanged;
    internal MapPanel()
    {
        AutoScroll=true;BackColor=DATheme.Dark;ForeColor=DATheme.Gold;Font=DATheme.Font(DATheme.CompanionFontPixels);
        var body=new TableLayoutPanel {Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,RowCount=5,Padding=new Padding(4),Margin=Padding.Empty};
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int row=0;row<5;row++)body.RowStyles.Add(new RowStyle(SizeType.Absolute,24));
        var playRow=Row(72,100,40,50,98,0);
        playRow.Controls.Add(Caption("Play zoom"),0,0);
        PlayZoom.Minimum=100;PlayZoom.Maximum=200;PlayZoom.Value=100;PlayZoom.SmallChange=5;PlayZoom.LargeChange=25;PlayZoom.AccessibleName="Play area zoom percent";FillCell(PlayZoom);playRow.Controls.Add(PlayZoom,1,0);
        var playLabel=Caption("100%");playRow.Controls.Add(playLabel,2,0);
        var resetZoom=new DAButton{Text="Reset"};FillCell(resetZoom);resetZoom.Click+=delegate{PlayZoom.Value=100;};playRow.Controls.Add(resetZoom,3,0);
        LockZoom.Text="Lock zoom";FillCell(LockZoom);playRow.Controls.Add(LockZoom,4,0);
        PlayZoom.ValueChanged+=delegate{playLabel.Text=PlayZoom.Value+"%";};body.Controls.Add(playRow,0,0);
        var zoomRow=Row(156,44,68,60,40,0);
        CustomZoom.Text="Custom map zoom";FillCell(CustomZoom);zoomRow.Controls.Add(CustomZoom,0,0);
        Number(ZoomScale,1,10,3,"Tab map zoom level");zoomRow.Controls.Add(ZoomScale,1,0);
        zoomRow.Controls.Add(Caption("Tab fade"),2,0);
        TabTransparency.Minimum=0;TabTransparency.Maximum=95;TabTransparency.Value=0;TabTransparency.SmallChange=5;TabTransparency.LargeChange=10;TabTransparency.AccessibleName="Tab map transparency percent";FillCell(TabTransparency);zoomRow.Controls.Add(TabTransparency,3,0);
        var tabFadeLabel=Caption("0%");TabTransparency.ValueChanged+=delegate{tabFadeLabel.Text=TabTransparency.Value+"%";};zoomRow.Controls.Add(tabFadeLabel,4,0);body.Controls.Add(zoomRow,0,1);
        var fogRow=Row(120,0,38,44,44,90);
        fogRow.MaximumSize=new Size(421,0);
        Fog.Text="Dim fog of war";FillCell(Fog);fogRow.Controls.Add(Fog,0,0);
        Opacity.Minimum=0;Opacity.Maximum=100;Opacity.Value=30;Opacity.SmallChange=1;Opacity.LargeChange=5;Opacity.AccessibleName="Fog opacity percent";FillCell(Opacity);fogRow.Controls.Add(Opacity,1,0);
        var opacityLabel=Caption("30%");Opacity.ValueChanged+=delegate{opacityLabel.Text=Opacity.Value+"%";};fogRow.Controls.Add(opacityLabel,2,0);
        fogRow.Controls.Add(Caption("Range"),3,0);Number(Range,6,24,12,"Fog range");fogRow.Controls.Add(Range,4,0);
        Shape.DropDownStyle=ComboBoxStyle.DropDownList;Shape.Items.AddRange(new object[]{"Diamond","Square"});Shape.SelectedIndex=1;Shape.BackColor=DATheme.Dark;Shape.ForeColor=DATheme.Gold;Shape.FlatStyle=FlatStyle.Flat;Shape.AccessibleName="Fog shape";FillCell(Shape);fogRow.Controls.Add(Shape,5,0);body.Controls.Add(fogRow,0,2);
        var fadeRow=Row(156,98,110,0);
        Fade.Text="Fade to fog of war";Fade.Checked=true;FillCell(Fade);fadeRow.Controls.Add(Fade,0,0);
        ExperienceRate.Text="Exp/hour";FillCell(ExperienceRate);fadeRow.Controls.Add(ExperienceRate,1,0);
        AbilityRate.Text="Ability/hour";FillCell(AbilityRate);fadeRow.Controls.Add(AbilityRate,2,0);body.Controls.Add(fadeRow,0,3);
        var miniRow=Row(118,90,90,0);
        MiniMap.Text="Show minimap";MiniMap.AccessibleName="Show minimap";MiniMap.Checked=true;FillCell(MiniMap);miniRow.Controls.Add(MiniMap,0,0);
        MiniMapStyle.DropDownStyle=ComboBoxStyle.DropDownList;MiniMapStyle.Items.AddRange(new object[]{"Live","Standard"});MiniMapStyle.SelectedIndex=0;MiniMapStyle.BackColor=DATheme.Dark;MiniMapStyle.ForeColor=DATheme.Gold;MiniMapStyle.FlatStyle=FlatStyle.Flat;MiniMapStyle.AccessibleName="Minimap style";FillCell(MiniMapStyle);miniRow.Controls.Add(MiniMapStyle,1,0);
        Grid.Text="Tile grid";FillCell(Grid);miniRow.Controls.Add(Grid,2,0);
        LastSkulled.MaximumSize=new Size(116,24);FillCell(LastSkulled);miniRow.Controls.Add(LastSkulled,3,0);body.Controls.Add(miniRow,0,4);Controls.Add(body);
        DATheme.StyleCompanion(this);
        CustomZoom.CheckedChanged+=Changed;ZoomScale.ValueChanged+=Changed;Fog.CheckedChanged+=Changed;Range.ValueChanged+=Changed;Shape.SelectedIndexChanged+=Changed;Opacity.ValueChanged+=Changed;Fade.CheckedChanged+=Changed;PlayZoom.ValueChanged+=Changed;TabTransparency.ValueChanged+=Changed;MiniMap.CheckedChanged+=Changed;MiniMapStyle.SelectedIndexChanged+=Changed;Grid.CheckedChanged+=Changed;
        LockZoom.CheckedChanged+=Changed;ExperienceRate.CheckedChanged+=Changed;AbilityRate.CheckedChanged+=Changed;
    }
    static void FillCell(Control control){control.Dock=DockStyle.Fill;control.Margin=control is ComboBox?new Padding(0,0,2,0):new Padding(0,1,2,1);}
    static Label Caption(string text){var label=new Label{Text=text,TextAlign=ContentAlignment.MiddleLeft};FillCell(label);return label;}
    static void Number(NumericUpDown control,int minimum,int maximum,int value,string name)
    {control.Minimum=minimum;control.Maximum=maximum;control.Value=value;control.BackColor=DATheme.Dark;control.ForeColor=DATheme.Gold;control.AccessibleName=name;FillCell(control);}
    static TableLayoutPanel Row(params int[] widths)
    {
        var row=new TableLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty,ColumnCount=widths.Length,RowCount=1};
        row.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        foreach(int width in widths)row.ColumnStyles.Add(new ColumnStyle(width==0?SizeType.Percent:SizeType.Absolute,width==0?100:width));
        return row;
    }
    void Changed(object sender,EventArgs e){if(OptionsChanged!=null)OptionsChanged(this,e);}
}
