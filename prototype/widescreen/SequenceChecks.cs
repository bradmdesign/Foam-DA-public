// Execute the exact native sequence hooks in this synthetic process only.
using System;
using System.IO;
using System.Runtime.InteropServices;
internal static class SequenceChecks
{
    [DllImport("kernel32.dll")] static extern IntPtr VirtualAlloc(IntPtr address,UIntPtr size,uint allocation,uint protection);
    [DllImport("kernel32.dll")] static extern bool VirtualFree(IntPtr address,UIntPtr size,uint operation);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Invoke(IntPtr self);
    static int Main(string[] args)
    {
        const int state=0x4e241c,begin=0x268680,end=0x268700,bridge=0x268780;
        IntPtr memory=VirtualAlloc(IntPtr.Zero,new UIntPtr(0x4e3000),0x3000,0x40);
        if(memory==IntPtr.Zero)throw new Exception("Test allocation failed");
        IntPtr self=Marshal.AllocHGlobal(16);
        try
        {
            byte[] first=File.ReadAllBytes(args[0]),last=File.ReadAllBytes(args[1]);
            Marshal.Copy(first,0,IntPtr.Add(memory,begin),first.Length);
            Marshal.Copy(last,0,IntPtr.Add(memory,end),last.Length);
            // Native call substitutes record the sequence value they observed,
            // then set a known return register and carry flag.
            foreach(int target in new[]{0x152820,0x4b1b0})
            {
                byte[] stub={0xa1,0,0,0,0,0x89,1,0xb8,0x78,0x56,0x34,0x12,0xf9,0xc3};
                Array.Copy(BitConverter.GetBytes(IntPtr.Add(memory,state).ToInt32()),0,stub,1,4);
                Marshal.Copy(stub,0,IntPtr.Add(memory,target),stub.Length);
            }
            foreach(int target in new[]{begin,end})
            {
                byte[] entry={0x8b,0x4c,0x24,4,0xe8,0,0,0,0,0x9c,0x5a,0x89,0x51,4,0xc3};
                Array.Copy(BitConverter.GetBytes(target-(bridge+9)),0,entry,5,4);
                Marshal.Copy(entry,0,IntPtr.Add(memory,bridge),entry.Length);
                var invoke=(Invoke)Marshal.GetDelegateForFunctionPointer(IntPtr.Add(memory,bridge),typeof(Invoke));
                int result=invoke(self),sequence=Marshal.ReadInt32(IntPtr.Add(memory,state));
                if(result!=0x12345678||Marshal.ReadInt32(self)!=1||(Marshal.ReadInt32(self,4)&1)!=1||sequence!=(target==begin?1:2))
                    throw new Exception("Sequence hook changed native return state or bracket order");
            }
            Console.WriteLine("Passed native sequence begin/end, relocated counter, return register and flags");return 0;
        }
        finally {Marshal.FreeHGlobal(self);VirtualFree(memory,UIntPtr.Zero,0x8000);}
    }
}
