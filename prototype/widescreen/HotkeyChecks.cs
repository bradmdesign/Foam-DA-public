using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

internal static class HotkeyChecks
{
    static int count;
    static void Check(bool ok,string message){if(!ok)throw new Exception("Hotkeys: "+message);count++;}
    static void Invalid(Action action,string message){bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Check(rejected,message);}
    internal static int Run()
    {
        count=0;
        var steps=HotkeySyntax.Parse("s1234 space (pause:500) 5 6");
        var expected=new[]{Keys.S,Keys.D1,Keys.D2,Keys.D3,Keys.D4,Keys.Space,Keys.None,Keys.D5,Keys.D6};
        Check(steps.Length==9,"example length");
        for(int i=0;i<expected.Length;i++)Check(steps[i].Key==expected[i],"example order "+i);
        Check(steps[6].Pause==500,"exact pause value");
        Check(HotkeySyntax.Parse("SPACE Space space").Length==3,"case-insensitive named keys");
        Check(HotkeySyntax.Parse("F5(pause:0)6")[0].Key==Keys.F5,"named F5 and adjacent pause");
        foreach(string text in new[]{"","(pause:500)","1 (pause:-1)","1 (pause:999999999999)","1 (wait:500)","1 (pause:500",new string('1',129),"1(pause:30000)(pause:30000)"})
            Invalid(()=>HotkeySyntax.Parse(text),"reject invalid sequence "+text.Substring(0,Math.Min(20,text.Length)));
        Check(HotkeySyntax.Shortcut("shift 1")== (Keys.Shift|Keys.D1),"space-separated shortcut");
        Check(HotkeySyntax.Shortcut("ALT+2")== (Keys.Alt|Keys.D2),"Alt shortcut");
        Check(HotkeySyntax.Shortcut("ctrl shift F5")== (Keys.Control|Keys.Shift|Keys.F5),"multiple modifiers");
        foreach(Keys key in new[]{Keys.Shift|Keys.D1,Keys.Alt|Keys.D2,Keys.Control|Keys.OemQuestion,Keys.Shift|Keys.Space})
            Check(HotkeySyntax.Shortcut(HotkeySyntax.Caption(key))==key,"shortcut round trip");
        foreach(string text in new[]{"1","Shift","Alt+F4","Ctrl+Tab","Shift+Enter","Ctrl+Escape","Shift+1+2"})
            Invalid(()=>HotkeySyntax.Shortcut(text),"reserved or invalid shortcut "+text);

        var state=new PlayContext{Alive=true,Root=1,Character=2,Map=3};var tapped=new List<Keys>();
        var runner=new HotkeyRunner{ReadContext=()=>state,Tap=key=>tapped.Add(key)};DateTime start=new DateTime(2026,10,3);
        Check(runner.Start(steps,start),"start in world");runner.Tick(start,true,true);Check(tapped.Count==0,"wait for physical modifier release");
        runner.Tick(start,true,false);Check(tapped.Count==1&&tapped[0]==Keys.S,"first key after release");
        Check(!runner.Start(steps,start),"no overlapping combos");
        for(int i=1;i<=5;i++)runner.Tick(start.AddMilliseconds(i*100),true,false);
        Check(tapped.Count==6&&tapped[5]==Keys.Space,"sequence order before pause");
        runner.Tick(start.AddMilliseconds(600),true,false);runner.Tick(start.AddMilliseconds(1099),true,false);
        Check(tapped.Count==6,"no input during pause");runner.Tick(start.AddMilliseconds(1100),true,false);
        Check(tapped.Count==7&&tapped[6]==Keys.D5,"resumes after 500 ms pause");
        runner.Tick(start.AddMilliseconds(1200),true,false);runner.Tick(start.AddMilliseconds(1300),true,false);
        Check(!runner.Running&&tapped.Count==8,"one-shot completion");
        for(int kind=0;kind<7;kind++)
        {
            state=new PlayContext{Alive=true,Root=1,Character=2,Map=3};tapped.Clear();runner.Start(steps,start);runner.Tick(start,true,false);
            if(kind==0)state.Chat=true;if(kind==1)state.Dialog=true;if(kind==2)state.Transfer=true;if(kind==3)state.Alive=false;if(kind==4)state.Map=4;if(kind==5)state.Character=4;
            runner.Tick(start.AddSeconds(1),kind!=6,false);
            Check(!runner.Running&&tapped.Count==1,"stop mid-combo on unsafe state/focus "+kind);
        }
        state=new PlayContext{Alive=true,Chat=true};Check(!runner.Start(steps,start),"chat blocks initial trigger");
        state=new PlayContext{Alive=true,Root=1,Character=2,Map=3};tapped.Clear();runner.Start(steps,start);runner.Tick(start.AddSeconds(4),true,true);
        Check(!runner.Running&&tapped.Count==0,"held-trigger timeout");
        runner.Start(steps,start);runner.Tick(start,true,false);state.X=9;runner.Tick(start.AddMilliseconds(100),true,false);
        Check(runner.Running&&tapped.Count==2,"moving within map allowed");runner.Stop("test complete");

        var gate=new HotkeyInputGate();int triggers=0,releases=0;
        Func<Keys,bool> accept=key=>{triggers++;return key==(Keys.Shift|Keys.D1);};Action release=()=>releases++;
        Check(!gate.Handle(0x100,16,Keys.Shift,accept,release),"modifier alone passes through");
        Check(gate.Handle(0x100,49,Keys.Shift,accept,release),"consume bound trigger");
        Check(gate.Handle(0x100,49,Keys.Shift,accept,release)&&triggers==1&&releases==1,"autorepeat does not trigger twice");
        Check(gate.Handle(0x102,'!',Keys.Shift,accept,release),"consume translated character");
        Check(gate.Handle(0x101,49,Keys.None,accept,release)&&!gate.Held,"consume trigger release");
        Check(!gate.Handle(0x100,50,Keys.Shift,accept,release),"unbound key passes through");
        Check(!gate.Handle(0x100,49,Keys.Shift,key=>false,release),"chat-rejected binding passes through for typing");
        Check(gate.Handle(0x104,50,Keys.Alt,key=>true,release),"consume Alt system key");gate.Clear();Check(!gate.Held,"clear on focus loss");

        const string home="http://127.0.0.1:4173";
        foreach(string url in new[]{home+"/search/",home+"/abilities/example/#ranks",home+"/assets/map.png"})Check(LibraryNavigation.IsLocal(url,home),"local navigation");
        foreach(string url in new[]{"https://www.wanderlustdb.com/",home+".evil.test/","http://127.0.0.1:4174/","file:///C:/test","javascript:alert(1)","http://user@127.0.0.1:4173/"})Check(!LibraryNavigation.IsLocal(url,home),"block external navigation");
        var settings=new HotkeySettings{Enabled=true,Bindings=new List<HotkeyBinding>{new HotkeyBinding{Shortcut="Shift+1",Sequence="s1234 space (pause:500) 5 6"}}};
        var restored=Program.Json.Deserialize<HotkeySettings>(Program.Json.Serialize(settings));
        Check(restored.Enabled && restored.Bindings.Count==1 && restored.Bindings[0].Sequence==settings.Bindings[0].Sequence,"persist binding data");
        // Parked game HWNDs can have no focused pane even with chat open.
        // Exercise actual native metadata reads, not a mocked Chat boolean.
        var memory=new Dictionary<uint,uint>{{0x73d944,0x10000},{0x10064,0x20000},{0x10068,2},{0x30130,1},{0x40130,1},{0x30000,0x123456},{0x40000,0x682fec}};
        var tree=new byte[24];Array.Copy(BitConverter.GetBytes(0x30000u),0,tree,0,4);Array.Copy(BitConverter.GetBytes(0x40000u),0,tree,12,4);tree[16]=1;
        Action<uint,byte[],int> readTree=(address,bytes,length)=>Array.Copy(tree,bytes,length);
        Func<uint,uint> readWord=address=>memory[address];
        foreach(uint vtable in new uint[]{0x682fec,0x68306c,0x6830ec}){memory[0x40000]=vtable;Check(NativeChatGuard.Visible(readTree,readWord,0x10000),"visible Say/Shout/Tell blocks with no focus");}
        memory[0x40130]=0;Check(!NativeChatGuard.Visible(readTree,readWord,0x10000),"hidden input ignored");memory[0x40130]=1;
        memory[0x30130]=0;Check(!NativeChatGuard.Visible(readTree,readWord,0x10000),"hidden parent hides input");memory[0x30130]=1;
        memory[0x40000]=0x123456;Check(!NativeChatGuard.Visible(readTree,readWord,0x10000),"ordinary pane not chat");
        tree[16]=9;Invalid(()=>NativeChatGuard.Visible(readTree,readWord,0x10000),"reject changing hierarchy");tree[16]=1;
        Invalid(()=>NativeChatGuard.Visible((address,bytes,length)=>{Array.Copy(tree,bytes,length);memory[0x10068]=3;},readWord,0x10000),"reject replaced tree");
        return count;
    }
}
