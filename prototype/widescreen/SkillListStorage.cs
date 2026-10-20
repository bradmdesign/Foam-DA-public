using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

internal sealed class SkillListState
{
    public int schema=1;
    public string[] selected=new string[0];
    public string classFilter="",subclass="",pathFilter="";
    internal static SkillListState Validate(SkillListState state)
    {
        if(state==null||state.schema!=1||state.selected==null||state.selected.Length>1000)throw new InvalidDataException("Invalid skill list.");
        if(state.selected.Any(s=>s==null||!Regex.IsMatch(s,"^ability:[a-z0-9-]+~[0-9]{1,3}$")||s.Length>180))throw new InvalidDataException("Invalid selected ability.");
        string[] families={"","Warrior","Wizard","Priest","Monk","Rogue","Peasant"};
        string[] subclasses={"","base:Warrior","base:Wizard","base:Priest","base:Monk","base:Rogue","base:Peasant","Gladiator","Summoner","Bard","Druid","Archer"};
        if(!families.Contains(state.classFilter)||!subclasses.Contains(state.subclass)||!new[]{"","pure","subpath"}.Contains(state.pathFilter))throw new InvalidDataException("Invalid skill list filters.");
        state.selected=state.selected.Distinct().ToArray();return state;
    }
}
internal sealed class SkillListStorage
{
    readonly string path;
    static readonly byte[] entropy=Encoding.UTF8.GetBytes("DA-Library.MySkillList.v1");
    internal string Error;
    internal SkillListStorage(string file=null){path=file??Path.Combine(LocalProfile.Root,"private","my-skill-list.dpapi");}
    internal SkillListState Load()
    {
        try{
            if(!File.Exists(path))return new SkillListState();
            if(new FileInfo(path).Length>256*1024)throw new InvalidDataException();
            var bytes=ProtectedData.Unprotect(File.ReadAllBytes(path),entropy,DataProtectionScope.CurrentUser);
            try{return SkillListState.Validate(Program.Json.Deserialize<SkillListState>(Encoding.UTF8.GetString(bytes)));}
            finally{Array.Clear(bytes,0,bytes.Length);}
        }catch{Error="The encrypted skill list could not be opened. Its saved file has been preserved; you can still export this session's sheet.";return new SkillListState();}
    }
    internal void Save(SkillListState value)
    {
        if(Error!=null)throw new InvalidOperationException(Error);
        value=SkillListState.Validate(value);byte[] plain=Encoding.UTF8.GetBytes(Program.Json.Serialize(value));
        byte[] data;try{data=ProtectedData.Protect(plain,entropy,DataProtectionScope.CurrentUser);}finally{Array.Clear(plain,0,plain.Length);}
        Directory.CreateDirectory(Path.GetDirectoryName(path));string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllBytes(temporary,data);if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    internal static int Check(string folder)
    {
        Directory.CreateDirectory(folder);string path=Path.Combine(folder,"skill-list.dpapi");var store=new SkillListStorage(path);
        var state=new SkillListState{selected=new[]{"ability:archery-archer-skill~2"},classFilter="Rogue",subclass="Archer"};store.Save(state);
        if(Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("archery"))throw new Exception("Skill list was saved unencrypted.");
        var restored=new SkillListStorage(path).Load();if(restored.selected.Length!=1||restored.selected[0]!=state.selected[0]||restored.subclass!="Archer")throw new Exception("Skill list persistence failed.");
        var prior=File.ReadAllBytes(path);bool rejected=false;try{store.Save(new SkillListState{selected=new[]{"../../unexpected"}});}catch(InvalidDataException){rejected=true;}
        if(!rejected||!prior.SequenceEqual(File.ReadAllBytes(path)))throw new Exception("Invalid skill list replaced saved data.");
        File.WriteAllBytes(path,new byte[]{1,2,3});var broken=new SkillListStorage(path);broken.Load();rejected=false;try{broken.Save(state);}catch(InvalidOperationException){rejected=true;}
        if(!rejected||new FileInfo(path).Length!=3)throw new Exception("Unreadable skill list was not preserved.");return 4;
    }
}
