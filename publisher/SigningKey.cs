using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

internal static class SigningKey
{
    static readonly byte[] Entropy=Encoding.UTF8.GetBytes("Foam-DA publisher release signing v1");
    static RSACryptoServiceProvider Open(string file,bool create)
    {
        string folder=Path.GetDirectoryName(Path.GetFullPath(file));Directory.CreateDirectory(folder);
        var rights=new DirectorySecurity();rights.SetAccessRuleProtection(true,false);
        rights.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        Directory.SetAccessControl(folder,rights);
        var rsa=new RSACryptoServiceProvider(3072,new CspParameters(24));rsa.PersistKeyInCsp=false;
        if(File.Exists(file)) {
            byte[] clear=ProtectedData.Unprotect(File.ReadAllBytes(file),Entropy,DataProtectionScope.CurrentUser);
            try{rsa.FromXmlString(Encoding.UTF8.GetString(clear));}finally{Array.Clear(clear,0,clear.Length);}
        }else {
            if(!create)throw new IOException("Publisher key missing. Restore its protected backup; do not replace it.");
            byte[] clear=Encoding.UTF8.GetBytes(rsa.ToXmlString(true));
            try{File.WriteAllBytes(file,ProtectedData.Protect(clear,Entropy,DataProtectionScope.CurrentUser));}finally{Array.Clear(clear,0,clear.Length);}
        }
        return rsa;
    }
    static int Main(string[] args)
    {
        try {
            if(args.Length==3&&args[0]=="--ensure"){using(var rsa=Open(args[1],true))File.WriteAllText(args[2],rsa.ToXmlString(false),new UTF8Encoding(false));return 0;}
            if(args.Length==4&&args[0]=="--sign"){using(var rsa=Open(args[1],false))using(var sha=SHA256.Create())File.WriteAllBytes(args[3],rsa.SignData(File.ReadAllBytes(args[2]),sha));return 0;}
            throw new ArgumentException("Use --ensure <protected-key> <public-xml> or --sign <protected-key> <input> <signature>.");
        }catch(Exception ex){Console.Error.WriteLine(ex.Message);return 1;}
    }
}
