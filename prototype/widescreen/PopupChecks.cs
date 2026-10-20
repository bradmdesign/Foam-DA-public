// Runs only synthetic popup geometry in this test process. No game process
// access, windows, input, rendering, or calls into the client are involved.
using System;
using System.IO;
using System.Runtime.InteropServices;
internal static class PopupChecks
{
    [DllImport("kernel32.dll")] static extern IntPtr VirtualAlloc(IntPtr address,UIntPtr size,uint allocation,uint protection);
    [DllImport("kernel32.dll")] static extern bool VirtualFree(IntPtr address,UIntPtr size,uint operation);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Invoke(IntPtr self,IntPtr rectangle);
    static int Main(string[] args)
    {
        byte[] code=File.ReadAllBytes(args[0]);int rva=int.Parse(args[1]),original=code.Length-11;
        int width=int.Parse(args[2]),height=int.Parse(args[3]),table=int.Parse(args[4]),checks=0;bool centered=args[5]=="center";
        IntPtr memory=VirtualAlloc(IntPtr.Zero,new UIntPtr((uint)(rva+code.Length+128)),0x3000,0x40);
        if(memory==IntPtr.Zero)throw new Exception("Test allocation failed");
        IntPtr self=Marshal.AllocHGlobal(24),rect=Marshal.AllocHGlobal(16);
        try
        {
            Marshal.Copy(code,0,IntPtr.Add(memory,rva),code.Length);
            // Replace only the original-function trampoline with a test sink:
            // record the native rectangle passed by the wrapper and return.
            byte[] sink={0x8b,0x44,0x24,4,0x8b,0x10,0x89,0x51,4,
                0x8b,0x50,4,0x89,0x51,8,0x8b,0x50,8,0x89,0x51,12,
                0x8b,0x50,12,0x89,0x51,16,0xc2,4,0};
            Marshal.Copy(sink,0,IntPtr.Add(memory,rva+original),sink.Length);
            int bridge=rva+original+sink.Length;
            byte[] entry={0x8b,0x4c,0x24,4,0xff,0x74,0x24,8,0xe8,0,0,0,0,0xc3};
            Array.Copy(BitConverter.GetBytes(rva-(bridge+13)),0,entry,9,4);
            Marshal.Copy(entry,0,IntPtr.Add(memory,bridge),entry.Length);
            var invoke=(Invoke)Marshal.GetDelegateForFunctionPointer(IntPtr.Add(memory,bridge),typeof(Invoke));
            foreach(int[] input in new[]{new[]{10,20,300,601},new[]{2,562,286,618},new[]{0,0,480,640},new[]{0,0,1200,2100}})
            foreach(bool matched in new[]{true,false})
            {
                Marshal.WriteInt32(self,unchecked(memory.ToInt32()+(matched?table:0x28c054)));
                Marshal.Copy(input,0,rect,4);invoke(self,rect);
                int[] actual=new int[4],unchanged=new int[4];Marshal.Copy(IntPtr.Add(self,4),actual,0,4);Marshal.Copy(rect,unchanged,0,4);
                int h=input[2]-input[0],w=input[3]-input[1],y=Math.Max(0,centered?(height-h)>>1:height-169-h),x=Math.Max(0,centered?(width-w)>>1:width-22-w);
                int[] expected=matched?new[]{y,x,y+h,x+w}:input;
                for(int i=0;i<4;i++)if(actual[i]!=expected[i]||unchanged[i]!=input[i])throw new Exception("Geometry or caller rectangle corrupted");
                checks++;
            }
            Console.WriteLine("Passed "+checks+" native x86 geometry cases at relocated base "+memory.ToString("x"));return 0;
        }
        finally{Marshal.FreeHGlobal(self);Marshal.FreeHGlobal(rect);VirtualFree(memory,UIntPtr.Zero,0x8000);}
    }
}
