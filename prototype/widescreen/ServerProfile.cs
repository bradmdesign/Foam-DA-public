using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;

// Profiles choose a destination only when launching a new isolated process.
// Existing tabs keep their endpoint and writable character directory.
internal sealed class ServerProfile
{
    public string Name {get;set;}
    public string Address {get;set;}
    public int Port {get;set;}
    internal bool Official {get{return String.IsNullOrEmpty(Address);}}
    internal string Key {get{return Official?"official":Address+":"+Port;}}
    internal string Arguments {get{return Official?"":" "+Address+" "+Port;}}
    public override string ToString(){return Name;}
    internal static ServerProfile Kru(){return new ServerProfile{Name="Official",Port=2610};}
    internal static ServerProfile Local(){return Create("Local server","127.0.0.1",2610);}
    internal static ServerProfile Create(string name,string host,int port)
    {
        if(port<1||port>65535)throw new ArgumentException("Port must be 1–65535.");
        IPAddress address;
        if(!IPAddress.TryParse(host,out address)||address.AddressFamily!=AddressFamily.InterNetwork||host!=address.ToString()||address.Equals(IPAddress.Any)||address.Equals(IPAddress.Broadcast))
            throw new ArgumentException("Enter a dotted IPv4 address, for example 127.0.0.1.");
        return new ServerProfile{Name=name,Address=address.ToString(),Port=port};
    }
    internal static string KeyAt(string game)
    {
        string path=Path.Combine(Path.GetDirectoryName(game),"server-profile.json");
        if(!File.Exists(path))return "official";
        var saved=Program.Json.Deserialize<ServerProfile>(File.ReadAllText(path));
        return saved.Official?"official":Create(saved.Name,saved.Address,saved.Port).Key;
    }
    internal static ServerProfile Read(string game)
    {
        string path=Path.Combine(Path.GetDirectoryName(game),"server-profile.json");
        if(!File.Exists(path))return Kru();
        var saved=Program.Json.Deserialize<ServerProfile>(File.ReadAllText(path));
        return saved.Official?Kru():Create(saved.Name,saved.Address,saved.Port);
    }
    internal static ServerProfile Edit(IWin32Window owner,ServerProfile previous)
    {
        using(var form=new Form {Text="Custom server",ClientSize=new Size(370,185),StartPosition=FormStartPosition.CenterParent,
            FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,BackColor=DATheme.Dark,ForeColor=DATheme.Gold})
        {
            var address=new TextBox {Text=previous==null?"127.0.0.1":previous.Address,Bounds=new Rectangle(105,18,245,24)};
            var port=new NumericUpDown {Minimum=1,Maximum=65535,Value=previous==null?2610:previous.Port,Bounds=new Rectangle(105,52,100,24)};
            var save=new DAButton {Text="Use server",Bounds=new Rectangle(232,144,118,28)};
            form.Controls.AddRange(new Control[]{new Label {Text="IPv4 address",Bounds=new Rectangle(14,21,90,22)},address,
                new Label {Text="Port",Bounds=new Rectangle(14,56,80,22)},port,
                new Label {Text="Applies to new tabs. This client is version 7.41; the server must support its protocol.",Bounds=new Rectangle(14,87,337,48)},save});
            ServerProfile result=null;
            save.Click+=delegate{try{result=Create("Custom · "+address.Text+":"+port.Value,address.Text.Trim(),(int)port.Value);form.DialogResult=DialogResult.OK;}catch(Exception ex){MessageBox.Show(form,ex.Message,"Server address");}};
            form.AcceptButton=save;return form.ShowDialog(owner)==DialogResult.OK?result:null;
        }
    }
}

internal static class EndpointPatch
{
    static int I(byte[] b,int at){return BitConverter.ToInt32(b,at);}
    static void Put(byte[] b,int at,int value){Array.Copy(BitConverter.GetBytes(value),0,b,at,4);}
    static int Align(int n,int alignment){return checked((n+alignment-1)/alignment*alignment);}
    internal static int Offset(byte[] b,int rva)
    {
        int pe=I(b,0x3c),section=pe+24+BitConverter.ToUInt16(b,pe+20),count=BitConverter.ToUInt16(b,pe+6);
        for(int i=0;i<count;i++,section+=40)
        {int start=I(b,section+12),size=I(b,section+16);if(rva>=start&&rva<start+size)return I(b,section+20)+rva-start;}
        throw new InvalidOperationException("Endpoint patch address is outside the image.");
    }
    static void Expect(byte[] b,int rva,byte[] expected)
    {int at=Offset(b,rva);for(int i=0;i<expected.Length;i++)if(b[at+i]!=expected[i])throw new InvalidOperationException("Client does not match the verified endpoint patch.");}
    internal static byte[] Apply(byte[] input)
    {
        Expect(input,0x32253,new byte[]{0xe8,0x28,0x11,0,0});
        Expect(input,0x1655f4,new byte[]{0xc7,0x85,0x94,0xfb,0xff,0xff,0,0,0,0});
        int pe=I(input,0x3c),optional=pe+24,number=BitConverter.ToUInt16(input,pe+6);
        if(BitConverter.ToUInt16(input,pe+4)!=0x14c||BitConverter.ToUInt16(input,optional)!=0x10b||number!=6)
            throw new InvalidOperationException("Unexpected client image format.");
        int section=optional+BitConverter.ToUInt16(input,pe+20)+number*40,headers=I(input,optional+60);
        if(section+40>headers)throw new InvalidOperationException("No room for the endpoint section header.");
        for(int i=section;i<section+40;i++)if(input[i]!=0)throw new InvalidOperationException("Occupied endpoint section header.");
        int last=section-40,alignment=I(input,optional+32),fileAlignment=I(input,optional+36);
        if(alignment!=4096||fileAlignment!=512||I(input,last+20)+I(input,last+16)!=input.Length)throw new InvalidOperationException("Unexpected client section layout.");
        int rva=Align(I(input,last+12)+Math.Max(I(input,last+8),I(input,last+16)),alignment),raw=Align(input.Length,fileAlignment);
        byte[] code=Wrapper(rva),output=new byte[raw+512];Array.Copy(input,output,input.Length);
        Array.Copy(Encoding.ASCII.GetBytes(".dalib"),0,output,section,6);
        Put(output,section+8,code.Length);Put(output,section+12,rva);Put(output,section+16,512);Put(output,section+20,raw);
        Put(output,section+36,0x60000020); // executable, readable; never writable
        output[pe+6]=(byte)(number+1);output[pe+7]=0;
        Put(output,optional+4,checked(I(input,optional+4)+512));Put(output,optional+56,Align(rva+code.Length,alignment));
        Array.Copy(code,0,output,raw,code.Length);
        Put(output,Offset(input,0x32253)+1,rva-(0x32253+5));
        Array.Copy(new byte[]{0xe9,6,0x13,0,0,0x90,0x90,0x90,0x90,0x90},0,output,Offset(input,0x1655f4),10);
        return output; // caller computes checksum after the per-session mutex
    }
    internal static byte[] Wrapper(int rva)
    {
        var code=new List<byte>();Action<int> word=n=>code.AddRange(BitConverter.GetBytes(n));
        code.Add(0x51); // preserve config this while the stock initializer runs
        code.Add(0xe8);word(0x33380-(rva+code.Count+4));
        code.Add(0x59);
        code.Add(0xe8);word(0x33010-(rva+code.Count+4));
        code.AddRange(new byte[]{0x84,0xc0,0x75,16}); // parser returns AL; failure exits
        code.AddRange(new byte[]{0x6a,1,0xe8,0,0,0,0});int pc=rva+code.Count;
        code.Add(0x5a);code.AddRange(new byte[]{0x81,0xc2});word(0x269124-pc);
        code.AddRange(new byte[]{0xff,0x12,0xc3}); // ExitProcess via relative IAT; ASLR-safe
        return code.ToArray();
    }
}

internal static class StartupPatch
{
    // Dark Ages 7.41: enter the client's normal post-video state (2) instead
    // of constructing the intro-video state (1). Applied only to session copies.
    internal const string ClientSha256="054a5d6adc56099c6bfd9d2a58675aff62dc788b63209a3d906492f5b89e96c6";
    internal static byte[] SkipIntro(byte[] image)
    {
        int at=EndpointPatch.Offset(image,0xaca85);
        if(at<0||at+1>=image.Length||image[at]!=0x6a||image[at+1]!=0x01)
            throw new InvalidOperationException("Client does not match the verified intro patch.");
        image[at+1]=0x02;
        return image;
    }
}
