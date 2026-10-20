using System;
using System.Collections.Generic;
using System.Windows.Forms;

// Window-local key messages. No global keyboard hook, SendInput, or packet API.
internal sealed class SimpleMacroRunner
{
    internal Func<PlayContext> ReadContext;
    internal Action<Keys> Tap;
    internal Action<int,int> SelectPage;
    internal bool Running {get;private set;}
    internal string Status="Stopped · enter keys, then Start.";
    internal int Completed {get;private set;}
    Keys[] keys;
    PlayContext origin;
    int index;
    double seconds;
    bool assail;
    int page;
    bool waitingForPage;
    DateTime pageDeadline;
    DateTime due,cycle,nextAssail;

    internal static Keys[] Parse(string text)
    {
        var result=new List<Keys>();
        foreach(string part in (text??"").Split(new[]{' ','\t','\r','\n',','},StringSplitOptions.RemoveEmptyEntries))
        {
            string token=part.ToUpperInvariant();Keys key;
            if(token.Length==1 && token[0]>='0' && token[0]<='9')key=Keys.D0+(token[0]-'0');
            else if(token.Length==1 && token[0]>='A' && token[0]<='Z')key=Keys.A+(token[0]-'A');
            else if(token=="SPACE")key=Keys.Space;
            else if(token=="TAB")key=Keys.Tab;
            else if(token=="ENTER" || token=="RETURN")key=Keys.Enter;
            else if(token=="ESC" || token=="ESCAPE")key=Keys.Escape;
            else if(token=="/" || token=="SLASH")key=Keys.OemQuestion;
            else if(token=="-")key=Keys.OemMinus;
            else if(token=="=")key=Keys.Oemplus;
            else {int n;if(token.StartsWith("F") && Int32.TryParse(token.Substring(1),out n) && n>=1 && n<=12)key=Keys.F1+(n-1);
                else throw new InvalidOperationException("Unknown key '"+part+"'. Use 1 2 3 4, Space, letters, or F1–F12.");}
            result.Add(key);
            if(result.Count>32)throw new InvalidOperationException("Use at most 32 keys in one sequence.");
        }
        return result.ToArray();
    }
    internal void Start(string text,double interval,bool useAssail,DateTime now,int requestedPage=-1)
    {
        Keys[] parsed=Parse(text);
        if(parsed.Length==0 && !useAssail)throw new InvalidOperationException("Enter keys or enable Assail.");
        if(interval<.1 || interval>3600 || Double.IsNaN(interval))throw new InvalidOperationException("Choose 0.1–3600 seconds.");
        PlayContext state=ReadContext();
        if(!CanRun(state))throw new InvalidOperationException("Enter the world and close game chat/dialogs first.");
        if(requestedPage!=-1 && requestedPage!=1 && requestedPage!=2 && requestedPage!=3 && requestedPage!=4 && requestedPage!=9)throw new InvalidOperationException("Choose a skill or spell page.");
        if(requestedPage!=-1 && state.Compact)throw new InvalidOperationException("Use the full / UI to select a skill/spell page, or choose Current page.");
        page=requestedPage;waitingForPage=false;
        origin=new PlayContext{Root=state.Root,Character=state.Character,Map=state.Map,X=state.X,Y=state.Y};
        keys=parsed;seconds=interval;assail=useAssail;index=Completed=0;due=cycle=nextAssail=now;
        Running=true;Status="Running · use Stop to finish.";
    }
    static bool CanRun(PlayContext state){return state.Alive && !state.Chat && !state.Dialog && !state.Transfer;}
    internal void Stop(string reason){Running=false;Status=reason;}
    internal void Tick(DateTime now,bool active)
    {
        if(!Running)return;
        try
        {
            if(!active){Stop("Stopped · host lost focus.");return;}
            PlayContext state=ReadContext();
            if(!state.Alive || state.Root!=origin.Root || state.Character!=origin.Character || state.Map!=origin.Map)
            {Stop("Stopped · character or map changed.");return;}
            if(!CanRun(state))
            {waitingForPage=false;index=0;due=cycle=nextAssail=now.AddMilliseconds(100);Status="Waiting · close chat/dialogue to resume.";return;}
            if(page!=-1)
            {
                if(state.Compact){Stop("Stopped · use the full UI for automatic page selection.");return;}
                bool matches=state.Panel==page || (page==9 && state.Panel==10);
                if(!matches)
                {
                    if(!waitingForPage)
                    {
                        if(SelectPage==null)throw new InvalidOperationException("Page selection is unavailable.");
                        SelectPage(page,state.Panel);waitingForPage=true;pageDeadline=now.AddSeconds(2);Status="Opening selected skill/spell page…";
                    }
                    else if(now>=pageDeadline)Stop("Stopped · the selected skill/spell page did not open.");
                    return; // Never send sequence keys or Assail onto the wrong page.
                }
                if(waitingForPage){waitingForPage=false;due=cycle=nextAssail=now;}
            }
            if(keys.Length>0 && now>=due)
            {
                if(index==0)cycle=now;
                Tap(keys[index]);Completed++;
                if(++index==keys.Length){index=0;due=cycle.AddSeconds(seconds);if(due<now.AddMilliseconds(100))due=now.AddMilliseconds(100);}
                else due=now.AddMilliseconds(100);
            }
            if(assail && now>=nextAssail){Tap(Keys.Space);Completed++;nextAssail=now.AddMilliseconds(500);}
            Status="Running · "+Completed+" key presses · use Stop to finish";
        }
        catch(Exception ex){Stop("Stopped · "+ex.Message);}
    }
}
