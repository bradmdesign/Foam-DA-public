using System;
using System.Drawing;
using System.Runtime.InteropServices;

// Reads control identities, geometry and focus only. Never reads editor text.
internal sealed class LoginScene
{
    internal uint Dialog,NameField,PasswordField,NameEditor,PasswordEditor,FocusedField,Agreement;
    internal Point NamePoint,PasswordPoint,Submit,Continue,Accept;
    internal bool Menu,MenuPresent,Blocked;
    internal bool Ready {get{return !Blocked&&Agreement==0&&Dialog!=0&&NameField!=0&&PasswordField!=0;}}
    internal bool Reusable {get{return MenuPresent||Agreement!=0||Dialog!=0;}}
    internal static LoginScene Read(GameFrame frame)
    {return Read(frame.Read,frame.U32,frame.IsInWorldNow);}
    internal static LoginScene Read(Action<uint,byte[],int> read,Func<uint,uint> u,bool inWorld)
    {
        var result=new LoginScene();if(inWorld)return result;
        uint dispatcher=u(0x73d944);if(dispatcher<0x10000)return result;
        uint array=u(dispatcher+0x64),count=u(dispatcher+0x68),generation=u(dispatcher+0x1b4);
        if(array<0x10000||count==0||count>2048)return result;
        var entries=new byte[count*12];read(array,entries,entries.Length);
        var visible=new bool[64];var origins=new Point[64];var header=new byte[0x190];int loginDialogs=0,agreementDialogs=0;
        for(int i=0;i<count;i++)
        {
            uint pane=BitConverter.ToUInt32(entries,i*12);int depth=BitConverter.ToInt32(entries,i*12+4);
            if(depth<0||depth>=64)throw new InvalidOperationException("Login layout changed.");
            visible[depth]=false;if(depth>0&&!visible[depth-1])continue;
            read(pane,header,header.Length);if(header[0x130]==0)continue;
            visible[depth]=true;uint vt=BitConverter.ToUInt32(header,0);
            Point point=new Point(BitConverter.ToInt32(header,0x12c),BitConverter.ToInt32(header,0x128));if(depth>0)point.Offset(origins[depth-1]);origins[depth]=point;
            if((BitConverter.ToUInt32(header,0x188)&2)==0)continue;
            int w=BitConverter.ToInt32(header,0xe8),h=BitConverter.ToInt32(header,0xec);
            // Our viewport patch resizes MainMenuPane's backing, but its
            // controls keep their original 640 x 480 logical coordinates.
            if(vt==0x6788ec&&w>=640&&w<=1920&&h>=480&&h<=1080)
            {result.MenuPresent=true;if(u(pane+0x500)==0){result.Menu=true;result.Continue=new Point(point.X+110,point.Y+319);}continue;}
            if(vt==0x67896c&&w==217&&h==170)
            {loginDialogs++;result.Dialog=pane;ReadControls(result,pane,point,read,u,false);continue;}
            if(vt==0x669acc&&w==444&&h==377)
            {agreementDialogs++;result.Agreement=pane;ReadControls(result,pane,point,read,u,true);continue;}
            if(vt!=0x6835d4&&vt!=0x67610c)result.Blocked=true;
        }
        // The agreement may overlay an already-open login dialog. Both must
        // have their exact known controls; any duplicate or unknown pane blocks input.
        if(loginDialogs>1||agreementDialogs>1)result.Blocked=true;
        if(u(0x73d944)!=dispatcher||u(dispatcher+0x64)!=array||u(dispatcher+0x68)!=count||u(dispatcher+0x1b4)!=generation)
            throw new InvalidOperationException("Login layout changed.");
        return result;
    }
    static void ReadControls(LoginScene scene,uint pane,Point origin,Action<uint,byte[],int> read,Func<uint,uint> u,bool agreement)
    {
        // DialogPane owns this list independently of the dispatcher tree.
        uint list=u(pane+0x594);
        if(list<0x10000||u(list)!=0x678304||u(list+0xc)!=4){scene.Blocked=true;return;}
        uint count=u(list+0x14),array=u(list+0x18),focus=u(pane+0x5ac);
        if(count!=4||array<0x10000){scene.Blocked=true;return;}
        var entries=new byte[count*4];read(array,entries,entries.Length);var header=new byte[0x190];bool button=false;
        for(int i=0;i<count;i++)
        {
            uint control=BitConverter.ToUInt32(entries,i*4);read(control,header,header.Length);
            if(header[0x130]==0)continue;
            uint vt=BitConverter.ToUInt32(header,0);
            var rect=new Rectangle(BitConverter.ToInt32(header,0x12c),BitConverter.ToInt32(header,0x128),BitConverter.ToInt32(header,0xe8),BitConverter.ToInt32(header,0xec));
            if(vt==0x671c64&&rect==(agreement?new Rectangle(110,352,61,22):new Rectangle(28,130,62,20)))
            {button=true;var center=new Point(origin.X+rect.X+rect.Width/2,origin.Y+rect.Y+rect.Height/2);if(agreement)scene.Accept=center;else scene.Submit=center;}
            if(agreement||vt!=0x671ee4||rect.Size!=new Size(88,16))continue;
            uint editor=u(control+0x19c);if(editor<0x10000||u(editor)!=0x685e14)continue;
            var point=new Point(origin.X+rect.X+20,origin.Y+rect.Y+8);
            if(rect.Location==new Point(65,57)){scene.NameField=control;scene.NameEditor=editor;scene.NamePoint=point;}
            if(rect.Location==new Point(65,99)){scene.PasswordField=control;scene.PasswordEditor=editor;scene.PasswordPoint=point;}
            if(focus==i)scene.FocusedField=control;
        }
        if(!button)scene.Blocked=true;
        if(u(pane+0x594)!=list||u(list+0x14)!=count||u(list+0x18)!=array||u(pane+0x5ac)!=focus)
            throw new InvalidOperationException("Login controls changed.");
    }
}
internal sealed class QuickLogin
{
    readonly GameFrame frame;readonly SavedCharacter entry;readonly string server;
    readonly Func<LoginScene> readScene;readonly Func<bool> inWorld;readonly Func<DateTime> now;
    readonly Action<Point> click;readonly Action<LoginScene> typeName;
    readonly DateTime deadline;DateTime due;uint dialog,agreementPane;int phase,agreementAttempts;bool menuClicked;
    DateTime nextAgreementClick,unsettledSince,nameReadyAt;
    internal bool Done;internal string Status="Quick start waiting for the game to be ready…";
    internal bool Matches(SavedCharacter saved){return entry!=null&&entry.Key==saved.Key;}
    internal QuickLogin(GameFrame game,SavedCharacter saved,string endpoint)
    {
        if(saved.Server.Key!=endpoint)throw new InvalidOperationException("Quick-start server mismatch.");
        frame=game;entry=saved;server=endpoint;readScene=()=>LoginScene.Read(frame);inWorld=()=>frame.IsInWorldNow;
        now=()=>DateTime.UtcNow;deadline=now().AddSeconds(60);click=Click;
        typeName=scene=>{var bytes=System.Text.Encoding.ASCII.GetBytes(entry.Name);try{Type(scene.NameField,bytes);}finally{Array.Clear(bytes,0,bytes.Length);}};
    }
    // Deterministic flow checks use synthetic controls; no credential exists.
    internal QuickLogin(Func<LoginScene> scene,Func<bool> world,Func<DateTime> clock,Action<Point> input,Action<LoginScene> type)
    {readScene=scene;inWorld=world;now=clock;click=input;typeName=type;deadline=now().AddSeconds(60);}
    internal static bool CanType(LoginScene scene,uint expectedDialog,uint field,uint focus,bool world)
    {return !world&&scene.Ready&&scene.Dialog==expectedDialog&&focus==field&&focus!=0&&scene.FocusedField==field&&field==scene.NameField;}
    void CheckField(uint field)
    {
        var scene=LoginScene.Read(frame);
        if(entry.Server.Key!=server||!CanType(scene,dialog,field,scene.FocusedField,frame.IsInWorldNow)
            )throw new InvalidOperationException("Login focus changed.");
    }
    [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr wp,IntPtr lp,uint flags,uint timeout,out IntPtr result);
    void Send(uint message,int value,int param)
    {if(message==0x100||message==0x101)param=KeyScan.Ensure(value,param);IntPtr answer;if(SendMessageTimeout(frame.Window,message,new IntPtr(value),new IntPtr(param),2,500,out answer)==IntPtr.Zero)throw new InvalidOperationException("Game input timed out.");}
    void Click(Point point)
    {int packed=(point.Y<<16)|(point.X&65535);Send(0x6,1,0);Send(0x200,0,packed);Send(0x201,1,packed);Send(0x202,0,packed);}
    void Type(uint field,byte[] text)
    {
        // This client routes printable input through WM_CHAR. Its text canvas
        // handles Backspace there too; Home+Delete left old names intact in an
        // already-open form. Clear from the end without reading field text.
        CheckField(field);Send(0x100,0x23,1);Send(0x101,0x23,unchecked((int)0xc0000001));
        int clearCount=15;
        for(int i=0;i<clearCount;i++){CheckField(field);Send(0x102,8,1);}
        foreach(byte c in text){CheckField(field);Send(0x102,c,1);}
    }
    internal void Cancel(){Done=true;Status="Quick start cancelled. Continue in the game or select the character again.";}
    internal void ManualInput(){if(phase==2){Done=true;Status="Name filled. Continue signing in through the game.";}else if(phase>0)Cancel();else due=now().AddMilliseconds(300);}
    void CloseAgreement(LoginScene scene)
    {
        Status="Closing the startup agreement…";
        unsettledSince=DateTime.MinValue;
        if(scene.Agreement!=agreementPane){agreementPane=scene.Agreement;agreementAttempts=0;nextAgreementClick=DateTime.MinValue;}
        if(now()<nextAgreementClick)return;
        if(agreementAttempts>=3){Done=true;Status="Quick start could not dismiss the agreement. Click OK in the game, then select Quick start again.";return;}
        agreementAttempts++;nextAgreementClick=now().AddSeconds(1);
        click(scene.Accept);
    }
    bool WaitForFormToSettle()
    {
        if(unsettledSince==DateTime.MinValue)unsettledSince=now();
        if((now()-unsettledSince).TotalSeconds>=2)return false;
        Status="Waiting for the agreement and login form to settle…";
        return true;
    }
    internal void Tick()
    {
        if(Done||now()<due)return;
        try
        {
            if(inWorld()){Done=true;Status="Signed in.";return;}
            if(now()>deadline){Done=true;Status="Quick start timed out. Continue in the game or select the character again.";return;}
            due=now().AddMilliseconds(200);
            var scene=readScene();
            if(!scene.Blocked&&scene.Agreement!=0){CloseAgreement(scene);return;}
            if(scene.Agreement==0&&agreementPane!=0)
            {
                // A modal can move native focus. Re-select the intended editor
                // before resuming; only the character name is ever typed.
                if(phase==1||phase==2)
                {
                    if(scene.Dialog!=0&&scene.Dialog!=dialog)throw new InvalidOperationException("Login form changed behind agreement.");
                    if(!scene.Ready){if(WaitForFormToSettle())return;throw new InvalidOperationException("Login form did not return after agreement.");}
                    unsettledSince=DateTime.MinValue;
                    agreementPane=0;agreementAttempts=0;nextAgreementClick=DateTime.MinValue;
                    click(phase==1?scene.NamePoint:scene.PasswordPoint);return;
                }
                agreementPane=0;agreementAttempts=0;nextAgreementClick=DateTime.MinValue;
            }
            if(phase==3)return;
            if(phase==0)
            {
                if(scene.Blocked){Status="Quick start waiting for a game notice to close…";return;}
                if(!scene.Ready)
                {Status="Waiting for the login form…";if(scene.Menu&&!menuClicked){menuClicked=true;click(scene.Continue);}return;}
                dialog=scene.Dialog;phase=1;Status="Entering the saved character in the verified login form…";click(scene.NamePoint);return;
            }
            if(phase==2){
                if((now()-nameReadyAt).TotalSeconds>=5)Done=true;return;}
            if(scene.Dialog!=0&&scene.Dialog!=dialog)throw new InvalidOperationException("Login form changed.");
            if(!scene.Ready){if((scene.Blocked||scene.Dialog!=0)&&WaitForFormToSettle())return;throw new InvalidOperationException("Login form did not return.");}
            unsettledSince=DateTime.MinValue;
            typeName(scene);phase=2;nameReadyAt=now();Status="Name filled. Enter your password in the game.";click(scene.PasswordPoint);
        }
        catch(InvalidOperationException)
        {
            // Registration can change mid-snapshot. Never type into an
            // unverified or changed name editor.
            if(phase==0){due=now().AddMilliseconds(200);Status="Waiting for the startup screen to settle…";return;}
            Done=true;Status=phase==2?"Name filled. Continue signing in through the game.":"Quick start stopped because the login form or focus changed. Check the game and select Quick start again.";
        }
        catch
        {
            Done=true;Status="Quick start stopped before name entry. Check the game and select Quick start again.";
        }
    }
}
