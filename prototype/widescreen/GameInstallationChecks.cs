using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static class GameInstallationChecks
{
    static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    internal static void Run(string installed)
    {
        string folder=Path.Combine(Program.Root,"build","client-install-check-"+Guid.NewGuid().ToString("N"));
        int checks=0;var catalog=GameInstallation.Catalog();
        string exe=Program.HashFile(Path.Combine(installed,"Darkages.exe")),archive=Program.HashFile(Path.Combine(installed,"setoa.dat"));
        try
        {
            Check(GameInstallation.Validate(installed)==Path.GetFullPath(installed),"Supported local installation rejected");checks++;
            foreach(string profile in new[]{"960x600","1088x640","1152x680"})
            {
                string output=GameInstallation.PrepareProfile(profile,installed,Path.Combine(folder,profile));
                Check(Program.HashFile(Path.Combine(output,"Darkages.exe"))==catalog.profiles[profile].test_client_sha256,"Executable reconstruction differs from the validated viewport");checks++;
                Check(Program.HashFile(Path.Combine(output,"setoa.dat"))==catalog.profiles[profile].test_setoa_sha256,"UI reconstruction differs from the validated viewport");checks++;
                Check(!File.Exists(Path.Combine(output,"seo.dat"))&&!Directory.Exists(Path.Combine(output,"maps")),"Preparing a viewport must not copy the whole game");checks++;
                GameInstallation.PrepareProfile(profile,installed,output);
                Check(!Directory.GetFiles(output,"*.tmp").Any(),"Cached preparation left an incomplete output");checks++;
            }
            bool denied=false;try{GameInstallation.Validate(folder);}catch(IOException){denied=true;}
            Check(denied,"Missing executable must be rejected");checks++;
            byte[] source=File.ReadAllBytes(Path.Combine(installed,"Darkages.exe")),patch=File.ReadAllBytes(Path.Combine(GameInstallation.Recipes,"1088x640","client.patch.gz"));
            source[0]^=1;denied=false;try{ClientDelta.Apply(source,patch,catalog.source_client_sha256,catalog.profiles["1088x640"].test_client_sha256);}catch(InvalidDataException){denied=true;}
            Check(denied,"An unsupported source must not be patched");checks++;
            source[0]^=1;denied=false;try{ClientDelta.Apply(source,patch,catalog.source_client_sha256,new string('0',64));}catch(InvalidDataException){denied=true;}
            Check(denied,"A mismatched output recipe must be rejected");checks++;
            denied=false;try{ClientDelta.Apply(source,patch.Take(30).ToArray(),catalog.source_client_sha256,catalog.profiles["1088x640"].test_client_sha256);}catch(IOException){denied=true;}
            Check(denied,"A truncated recipe must be rejected");checks++;
            Check(Program.HashFile(Path.Combine(installed,"Darkages.exe"))==exe&&Program.HashFile(Path.Combine(installed,"setoa.dat"))==archive,"The original game installation changed");checks++;
            Check(Directory.GetFiles(GameInstallation.Recipes,"*",SearchOption.AllDirectories).All(p=>Path.GetExtension(p)==".gz"||Path.GetFileName(p)=="catalog.json"),"Distribution recipes contain a complete executable or archive");checks++;
            File.WriteAllText(Path.Combine(Program.Root,"build","client-install-checks.json"),Program.Json.Serialize(new{status="passed",checks,profiles=3,original_game_unchanged=true,whole_game_copied=false}));
        }
        finally
        {
            string build=Path.GetFullPath(Path.Combine(Program.Root,"build"))+Path.DirectorySeparatorChar;
            if(!Path.GetFullPath(folder).StartsWith(build,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Invalid test cleanup path.");
            if(Directory.Exists(folder))Directory.Delete(folder,true);
        }
    }
}
