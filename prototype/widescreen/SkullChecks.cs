using System;
using System.Drawing;
using System.IO;
using System.Linq;

internal static class SkullChecks
{
    static void Check(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    internal static int Run()
    {
        int count=0;var effects=new byte[30];
        string captureRoot=Program.InstallRoot??Program.Root;
        Check(CaptureStorage.Screenshots==Path.Combine(captureRoot,"screenshots")&&SkullHistory.Folder==Path.Combine(captureRoot,"skulled"),"All captures belong in stable visible folders beside the DA launcher");count++;
        for(int i=0;i<10;i++){effects[i*2]=255;effects[i*2+1]=255;}
        Check(!SkullStatus.ContainsSkull(effects),"Empty local status bar is not skulled");count++;
        effects[4]=89;effects[5]=0;effects[22]=0;
        Check(!SkullStatus.ContainsSkull(effects),"Expired skull icon is ignored");count++;
        for(byte stage=1;stage<=6;stage++){effects[22]=stage;Check(SkullStatus.ContainsSkull(effects),"Every live skull duration stage is recognized");count++;}
        effects[4]=88;Check(!SkullStatus.ContainsSkull(effects),"Adjacent status icon is not a skull");count++;
        effects[4]=89;effects[22]=255;bool invalid=false;try{SkullStatus.ContainsSkull(effects);}catch(InvalidDataException){invalid=true;}
        Check(invalid,"Torn or malformed status must not invent a skull");count++;
        effects[22]=6;
        const uint gui=0x100000,pane=0x200000,world=0x300000;
        Func<uint,uint> u=at=>at==0x82b768?gui:at==0x73d960?world+0x2e8:at==gui+0x4e04?pane:at==pane?0x684e44:at==world?0x68c054:at==world+0x26c?123u:at==world+0x23c?12u:at==world+0x238?34u:0;
        Action<uint,byte[],int> read=(at,b,n)=>{Check(at==pane+0x190&&n==30,"Read only local visible status table");Array.Copy(effects,b,n);};
        var state=SkullStatus.Read(read,u,"Testaisling","official");
        Check(state!=null&&state.Skulled&&state.Map==123&&state.X==12&&state.Y==34,"Stable skull observation carries matching map coordinates");count++;
        int reads=0;Action<uint,byte[],int> torn=(at,b,n)=>{read(at,b,n);if(++reads==2)b[22]=0;};
        Check(SkullStatus.Read(torn,u,"Testaisling","official")==null,"Changing status is retried without recording");count++;
        var tracker=new SkullTracker();Check(tracker.Observe(state),"First observed skull triggers recording");count++;
        tracker.Commit(state);Check(!tracker.Observe(state)&&!tracker.Observe(null)&&!tracker.Observe(state),"One screenshot per skull; missing reads do not rearm");count++;
        state.Skulled=false;Check(!tracker.Observe(state),"Confirmed recovery rearms without taking screenshot");count++;
        state.Skulled=true;Check(tracker.Observe(state),"A later skull creates a new event");count++;
        string folder=Path.Combine(Program.Root,"build","skull-check-"+Guid.NewGuid().ToString("N"));
        try
        {
            var item=SkullHistory.Create(state,new DateTime(2026,10,8,12,0,0,DateTimeKind.Utc));
            using(var image=new Bitmap(16,12)){using(var g=Graphics.FromImage(image))g.Clear(Color.Gold);SkullHistory.Save(item,image,folder);}
            var history=SkullHistory.Load(folder);Check(history.Count==1&&history[0].X==12&&history[0].Y==34&&history[0].Character=="Testaisling","Global local history preserves coordinates and character");count++;
            using(var image=new Bitmap(Path.Combine(folder,history[0].Screenshot)))Check(image.Size==new Size(16,12)&&image.GetPixel(4,4).ToArgb()==Color.Gold.ToArgb(),"Screenshot pixels survive save/load");count++;
            File.WriteAllText(Path.Combine(folder,"broken.json"),"bad json");Check(SkullHistory.Load(folder).Count==1,"Damaged entries do not hide valid history");count++;
            string imported=Path.Combine(folder,"imported");CaptureStorage.Import(folder,imported,true);
            Check(SkullHistory.Load(imported).Count==1&&File.Exists(Path.Combine(folder,item.Screenshot)),"Old captures and metadata are copied together while keeping the backup");count++;
            File.WriteAllText(Path.Combine(imported,item.Screenshot),"existing capture");File.Delete(Path.Combine(imported,item.Id+".json"));
            CaptureStorage.Import(folder,imported,true);
            Check(File.ReadAllText(Path.Combine(imported,item.Screenshot))=="existing capture"&&!File.Exists(Path.Combine(imported,item.Id+".json")),"Import never overwrites a capture or attaches skull metadata to a conflicting image");count++;
            File.WriteAllText(Path.Combine(folder,"private-settings.json"),"private");CaptureStorage.Import(folder,imported,false);
            Check(!File.Exists(Path.Combine(imported,"private-settings.json"))&&!File.Exists(Path.Combine(imported,"broken.json")),"Capture import excludes profiles and orphan metadata");count++;
        }
        finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
        return count;
    }
}
